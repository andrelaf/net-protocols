using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProtocolLab.Shared.Contracts;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace ProtocolLab.Amqp;

/// <summary>
/// Demonstração de AMQP 0-9-1 sobre RabbitMQ.
///
/// <para>
/// A ideia central do AMQP 0-9-1, e o que o separa do MQTT, é que <b>o publicador não escolhe
/// o destino</b>. Ele publica num <i>exchange</i> com uma <i>routing key</i>; o exchange
/// consulta seus <i>bindings</i> e decide quais filas recebem cópias. Quem define o roteamento
/// é o consumidor, ao declarar seu binding. Isso permite adicionar um consumidor novo — um
/// serviço de auditoria, por exemplo — sem tocar em uma linha do publicador.
/// </para>
///
/// <para>
/// O outro eixo é a <b>entrega confiável</b>: ack manual, confirms de publicação, mensagens
/// persistentes e dead-lettering. MQTT tem QoS; AMQP tem um vocabulário bem mais rico para
/// dizer o que fazer quando o processamento falha.
/// </para>
/// </summary>
public sealed class AmqpDemoService : BackgroundService
{
    /// <summary>Header que marca uma mensagem como "veneno" para exercitar o caminho da DLQ.</summary>
    public const string PoisonHeader = "x-simulate-failure";

    private readonly IProtocolEventSink _sink;
    private readonly ILogger<AmqpDemoService> _logger;
    private readonly AmqpDemoOptions _options;
    private readonly TelemetryGenerator _generator;

    private IConnection? _connection;
    private IChannel? _publishChannel;
    private IChannel? _consumeChannel;
    private volatile string? _lastError;

    public AmqpDemoService(
        IProtocolEventSink sink,
        IOptions<AmqpDemoOptions> options,
        TelemetryGenerator generator,
        ILogger<AmqpDemoService> logger)
    {
        _sink = sink;
        _logger = logger;
        _generator = generator;
        _options = options.Value;
    }

    public AmqpStatus Status => new(
        _connection?.IsOpen == true && _publishChannel?.IsOpen == true,
        _options.Uri,
        _options.Exchange,
        _options.Queue,
        _lastError);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_connection?.IsOpen != true)
                {
                    await ConnectAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                _logger.LogWarning("RabbitMQ indisponível — {Message}", ex.Message);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.ReconnectDelaySeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        await DisposeAmqpAsync();
    }

    private async Task ConnectAsync(CancellationToken ct)
    {
        var factory = new ConnectionFactory
        {
            Uri = new Uri(_options.Uri),

            // Recuperação automática: a biblioteca reconecta e redeclara topologia sozinha.
            // Ainda assim mantemos o laço de supervisão acima, porque a recuperação automática
            // só age depois de uma conexão ter existido — ela não cobre a primeira tentativa.
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true,
            ClientProvidedName = "protocol-lab-gateway"
        };

        _connection = await factory.CreateConnectionAsync(ct);

        // Canais separados para publicar e consumir. Um IChannel não é thread-safe, e
        // compartilhá-lo entre o consumidor e requisições HTTP concorrentes é uma das causas
        // mais comuns de erros intermitentes de framing em aplicações .NET com RabbitMQ.
        _publishChannel = await _connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true),
            ct);

        _consumeChannel = await _connection.CreateChannelAsync(cancellationToken: ct);

        await DeclareTopologyAsync(_publishChannel, ct);
        await StartConsumerAsync(_consumeChannel, ct);

        _publishChannel.BasicReturnAsync += OnBasicReturnAsync;

        _lastError = null;

        await _sink.PublishAsync(ProtocolEvent.Create(
            ProtocolKind.Amqp,
            EventDirection.Internal,
            "Conectado ao RabbitMQ",
            $"Topologia declarada: exchange '{_options.Exchange}' (topic) → fila '{_options.Queue}' via binding '{_options.BindingPattern}'. " +
            $"Dead-letter: '{_options.DeadLetterExchange}' → '{_options.DeadLetterQueue}'. Prefetch = {_options.Prefetch}.",
            metadata: new Dictionary<string, string>
            {
                ["exchange"] = _options.Exchange,
                ["queue"] = _options.Queue,
                ["prefetch"] = _options.Prefetch.ToString()
            }), ct);
    }

    private async Task DeclareTopologyAsync(IChannel channel, CancellationToken ct)
    {
        // Declarações são idempotentes, mas falham se os parâmetros divergirem de uma
        // declaração anterior (PRECONDITION_FAILED). Mudar 'durable' de uma fila existente
        // exige apagá-la: por isso topologia é decisão de arquitetura, não de configuração.
        await channel.ExchangeDeclareAsync(_options.DeadLetterExchange, ExchangeType.Fanout, durable: true, autoDelete: false, cancellationToken: ct);
        await channel.QueueDeclareAsync(_options.DeadLetterQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await channel.QueueBindAsync(_options.DeadLetterQueue, _options.DeadLetterExchange, routingKey: string.Empty, cancellationToken: ct);

        await channel.ExchangeDeclareAsync(_options.Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);

        var arguments = new Dictionary<string, object?>
        {
            // Mensagens rejeitadas com requeue=false, expiradas por TTL, ou descartadas por
            // limite de tamanho vão para este exchange em vez de sumirem.
            ["x-dead-letter-exchange"] = _options.DeadLetterExchange
        };

        await channel.QueueDeclareAsync(_options.Queue, durable: true, exclusive: false, autoDelete: false, arguments: arguments, cancellationToken: ct);
        await channel.QueueBindAsync(_options.Queue, _options.Exchange, _options.BindingPattern, cancellationToken: ct);
    }

    private async Task StartConsumerAsync(IChannel channel, CancellationToken ct)
    {
        // prefetchSize=0 (sem limite de bytes), prefetchCount=N, global=false (por consumidor).
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: _options.Prefetch, global: false, cancellationToken: ct);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += OnMessageAsync;

        // autoAck: false — o broker só remove a mensagem da fila quando confirmarmos.
        // Com autoAck: true a mensagem é dada como entregue no instante em que sai do broker,
        // e uma queda do consumidor durante o processamento a perde definitivamente.
        await channel.BasicConsumeAsync(_options.Queue, autoAck: false, consumer: consumer, cancellationToken: ct);
    }

    private async Task OnMessageAsync(object sender, BasicDeliverEventArgs args)
    {
        var channel = _consumeChannel;
        if (channel is null)
        {
            return;
        }

        var body = args.Body.ToArray();
        var isPoison = ReadHeader(args.BasicProperties.Headers, PoisonHeader) == "true";

        try
        {
            if (isPoison)
            {
                throw new InvalidOperationException("Falha de processamento simulada.");
            }

            var reading = ProtocolJson.Deserialize<TelemetryReading>(body);

            await _sink.PublishAsync(ProtocolEvent.Create(
                ProtocolKind.Amqp,
                EventDirection.Inbound,
                $"Entregue '{args.RoutingKey}' (deliveryTag {args.DeliveryTag})",
                args.Redelivered
                    ? "Flag redelivered ligada: esta mensagem já tinha sido entregue antes e voltou à fila. Consumidores AMQP precisam ser idempotentes."
                    : "O exchange roteou pela routing key até a fila ligada por este binding. O publicador nunca soube que esta fila existe.",
                payload: Encoding.UTF8.GetString(body),
                sizeBytes: body.Length,
                durationMs: reading?.ElapsedMsSince(),
                metadata: new Dictionary<string, string>
                {
                    ["routingKey"] = args.RoutingKey,
                    ["deliveryTag"] = args.DeliveryTag.ToString(),
                    ["redelivered"] = args.Redelivered.ToString(),
                    ["exchange"] = args.Exchange
                }));

            // Ack só depois do processamento bem-sucedido. multiple=false confirma apenas esta.
            await channel.BasicAckAsync(args.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            // requeue: false envia a mensagem ao dead-letter exchange configurado na fila.
            //
            // ANTIPATTERN: usar requeue=true aqui. A mensagem volta ao início da fila, é
            // entregue de novo, falha de novo — um laço infinito que consome 100% de CPU e
            // bloqueia as mensagens boas atrás dela. Sempre tenha uma DLQ.
            await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false);

            await _sink.PublishAsync(ProtocolEvent.Create(
                ProtocolKind.Amqp,
                EventDirection.Internal,
                $"NACK em '{args.RoutingKey}' → dead-letter",
                $"O processamento falhou ({ex.Message}). Rejeitamos com requeue=false, então o broker moveu a mensagem para " +
                $"'{_options.DeadLetterExchange}' → '{_options.DeadLetterQueue}'. Ela sai do caminho quente e fica disponível para inspeção.",
                payload: Encoding.UTF8.GetString(body),
                sizeBytes: body.Length,
                metadata: new Dictionary<string, string>
                {
                    ["routingKey"] = args.RoutingKey,
                    ["deliveryTag"] = args.DeliveryTag.ToString(),
                    ["deadLetterQueue"] = _options.DeadLetterQueue
                },
                level: EventLevel.Warning));
        }
    }

    /// <summary>
    /// Publica uma leitura no exchange topic.
    /// </summary>
    /// <param name="poison">Marca a mensagem para falhar no consumidor e exercitar a DLQ.</param>
    /// <param name="persistent">
    /// Persistente = o broker grava a mensagem em disco antes de confirmar. Sobrevive a
    /// restart do broker, ao custo de latência. Uma fila durável com mensagens transientes
    /// perde tudo no restart — os dois flags precisam andar juntos.
    /// </param>
    /// <param name="routingKeyOverride">
    /// Permite publicar com uma chave que nenhum binding casa, para ver o <c>basic.return</c>.
    /// </param>
    public async Task<AmqpPublishResult> PublishAsync(
        bool poison = false,
        bool persistent = true,
        string? deviceId = null,
        string? routingKeyOverride = null,
        CancellationToken ct = default)
    {
        var channel = _publishChannel;
        if (channel?.IsOpen != true)
        {
            throw new InvalidOperationException(
                $"Canal AMQP fechado ({_options.Uri}). Suba o broker com 'docker compose -f infra/docker-compose.yml up -d'.");
        }

        var reading = _generator.Next(deviceId);
        var routingKey = routingKeyOverride ?? $"telemetry.{reading.DeviceId}";
        var body = ProtocolJson.SerializeToUtf8(reading);

        var properties = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = persistent ? DeliveryModes.Persistent : DeliveryModes.Transient,
            MessageId = Guid.NewGuid().ToString("n"),
            Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
            Headers = poison
                ? new Dictionary<string, object?> { [PoisonHeader] = "true" }
                : null
        };

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // mandatory=true: se nenhuma fila casar com a routing key, o broker devolve a
            // mensagem via basic.return em vez de descartá-la silenciosamente.
            // Com publisher confirms ligados, este await só completa quando o broker confirma.
            await channel.BasicPublishAsync(
                exchange: _options.Exchange,
                routingKey: routingKey,
                mandatory: true,
                basicProperties: properties,
                body: body,
                cancellationToken: ct);

            stopwatch.Stop();

            await _sink.PublishAsync(ProtocolEvent.Create(
                ProtocolKind.Amqp,
                EventDirection.Outbound,
                $"Publicado em '{_options.Exchange}' com routing key '{routingKey}'",
                $"Confirmado pelo broker em {stopwatch.Elapsed.TotalMilliseconds:F1}ms. " +
                (persistent
                    ? "Persistente: gravado em disco antes do confirm."
                    : "Transiente: confirmado a partir da memória — mais rápido, e perdido se o broker reiniciar.") +
                (poison ? " Marcada como venenosa: o consumidor vai rejeitá-la e ela cairá na DLQ." : ""),
                payload: ProtocolJson.Serialize(reading),
                sizeBytes: body.Length,
                durationMs: stopwatch.Elapsed.TotalMilliseconds,
                metadata: new Dictionary<string, string>
                {
                    ["routingKey"] = routingKey,
                    ["persistent"] = persistent.ToString(),
                    ["poison"] = poison.ToString(),
                    ["messageId"] = properties.MessageId
                }), ct);

            return new AmqpPublishResult(routingKey, Confirmed: true, Routed: true, persistent, stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(ex, "Falha ao publicar em {RoutingKey}", routingKey);

            await _sink.PublishAsync(ProtocolEvent.Create(
                ProtocolKind.Amqp,
                EventDirection.Outbound,
                $"Publicação em '{routingKey}' não foi confirmada",
                $"O broker não confirmou: {ex.Message}. Com publisher confirms, uma falha aqui é visível. " +
                "Sem confirms, esta mensagem teria sido dada como enviada e desaparecido.",
                sizeBytes: body.Length,
                durationMs: stopwatch.Elapsed.TotalMilliseconds,
                level: EventLevel.Error), ct);

            return new AmqpPublishResult(routingKey, Confirmed: false, Routed: false, persistent, stopwatch.Elapsed.TotalMilliseconds, ex.Message);
        }
    }

    /// <summary>Lê a DLQ sem consumir em laço: um <c>basic.get</c> por vez.</summary>
    public async Task<int> DrainDeadLetterQueueAsync(int max = 10, CancellationToken ct = default)
    {
        var channel = _consumeChannel;
        if (channel?.IsOpen != true)
        {
            throw new InvalidOperationException("Canal AMQP fechado.");
        }

        var drained = 0;
        for (var i = 0; i < max; i++)
        {
            var result = await channel.BasicGetAsync(_options.DeadLetterQueue, autoAck: true, cancellationToken: ct);
            if (result is null)
            {
                break;
            }

            drained++;
            await _sink.PublishAsync(ProtocolEvent.Create(
                ProtocolKind.Amqp,
                EventDirection.Inbound,
                $"Drenado da DLQ: {_options.DeadLetterQueue}",
                "Mensagem recuperada da dead-letter queue. Em produção, este é o ponto de inspeção manual, correção e reprocessamento.",
                payload: Encoding.UTF8.GetString(result.Body.ToArray()),
                sizeBytes: result.Body.Length,
                metadata: new Dictionary<string, string> { ["originalRoutingKey"] = result.RoutingKey }), ct);
        }

        return drained;
    }

    private async Task OnBasicReturnAsync(object sender, BasicReturnEventArgs args)
    {
        await _sink.PublishAsync(ProtocolEvent.Create(
            ProtocolKind.Amqp,
            EventDirection.Inbound,
            $"basic.return: '{args.RoutingKey}' não casou com nenhum binding",
            $"Código {args.ReplyCode} — {args.ReplyText}. Publicamos com mandatory=true, então o broker devolveu a mensagem. " +
            "Sem mandatory, ela teria sido descartada em silêncio: o exchange não guarda mensagens sem fila de destino.",
            payload: Encoding.UTF8.GetString(args.Body.ToArray()),
            sizeBytes: args.Body.Length,
            metadata: new Dictionary<string, string>
            {
                ["routingKey"] = args.RoutingKey,
                ["replyCode"] = args.ReplyCode.ToString()
            },
            level: EventLevel.Warning));
    }

    /// <summary>Headers AMQP chegam como <c>byte[]</c>, não string. Armadilha clássica.</summary>
    private static string? ReadHeader(IDictionary<string, object?>? headers, string key)
    {
        if (headers is null || !headers.TryGetValue(key, out var value))
        {
            return null;
        }

        return value switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            string text => text,
            _ => value?.ToString()
        };
    }

    private async Task DisposeAmqpAsync()
    {
        try
        {
            if (_consumeChannel is not null)
            {
                await _consumeChannel.DisposeAsync();
            }

            if (_publishChannel is not null)
            {
                await _publishChannel.DisposeAsync();
            }

            if (_connection is not null)
            {
                await _connection.DisposeAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao encerrar recursos AMQP");
        }
    }
}
