using System.Buffers;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using ProtocolLab.Shared.Contracts;

namespace ProtocolLab.Mqtt;

/// <summary>
/// Cliente MQTT do laboratório: mantém uma sessão viva com o broker, assina a árvore de
/// tópicos da demo e publica telemetria sob demanda.
///
/// <para>
/// MQTT é <b>pub/sub mediado por broker</b>. Publicador e assinante nunca se conhecem; o
/// acoplamento é a string do tópico. Isso torna trivial adicionar um consumidor novo, e
/// torna o tópico um contrato tão rígido quanto uma assinatura de método — só que sem
/// compilador para protegê-lo. Trate a hierarquia de tópicos como API pública.
/// </para>
/// </summary>
public sealed class MqttDemoService : BackgroundService
{
    private readonly IProtocolEventSink _sink;
    private readonly ILogger<MqttDemoService> _logger;
    private readonly MqttDemoOptions _options;
    private readonly TelemetryGenerator _generator;
    private readonly IMqttClient _client;
    private readonly MqttClientOptions _clientOptions;
    private readonly MqttClientFactory _factory = new();

    private volatile string? _lastError;

    public MqttDemoService(
        IProtocolEventSink sink,
        IOptions<MqttDemoOptions> options,
        TelemetryGenerator generator,
        ILogger<MqttDemoService> logger)
    {
        _sink = sink;
        _logger = logger;
        _generator = generator;
        _options = options.Value;
        _client = _factory.CreateMqttClient();

        _clientOptions = BuildClientOptions(_options);

        _client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
        _client.ConnectedAsync += OnConnectedAsync;
        _client.DisconnectedAsync += OnDisconnectedAsync;
    }

    public MqttStatus Status => new(_client.IsConnected, $"{_options.Host}:{_options.Port}", _options.ClientId, _lastError);

    private static MqttClientOptions BuildClientOptions(MqttDemoOptions options) =>
        new MqttClientOptionsBuilder()
            .WithTcpServer(options.Host, options.Port)
            .WithClientId(options.ClientId)
            .WithProtocolVersion(MqttProtocolVersion.V500)
            .WithCleanStart(options.CleanStart)
            .WithSessionExpiryInterval(options.SessionExpirySeconds)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(options.KeepAliveSeconds))

            // Last Will and Testament: o broker publica esta mensagem se a conexão cair sem
            // um DISCONNECT limpo. É a forma de um dispositivo anunciar a própria morte —
            // um recurso que HTTP simplesmente não tem. Retido, para que quem assinar depois
            // saiba o estado atual sem esperar a próxima mudança.
            .WithWillTopic(options.StatusTopic)
            .WithWillPayload("offline"u8.ToArray())
            .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithWillRetain()
            .Build();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Laço de supervisão. O broker pode não estar de pé quando o gateway sobe
        // (docker compose ainda subindo), e isso não pode impedir o resto da aplicação.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // TryPingAsync devolve false se desconectado, sem lançar. Mais barato e mais
                // confiável do que checar IsConnected, que não detecta conexão meio-morta.
                if (!await _client.TryPingAsync(stoppingToken))
                {
                    await ConnectAndSubscribeAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                _logger.LogWarning("Broker MQTT indisponível em {Host}:{Port} — {Message}", _options.Host, _options.Port, ex.Message);
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

        await DisconnectGracefullyAsync();
    }

    private async Task ConnectAndSubscribeAsync(CancellationToken ct)
    {
        var response = await _client.ConnectAsync(_clientOptions, ct);
        _lastError = null;

        // Assinatura com wildcard. '+' casa exatamente um nível; '#' casa o resto da árvore
        // e só pode aparecer no final. Assinar '#' na raiz de um broker de produção é o
        // caminho mais curto para derrubar o próprio consumidor.
        var subscribeOptions = _factory.CreateSubscribeOptionsBuilder()
            .WithTopicFilter(f => f
                .WithTopic($"{_options.TelemetryTopicRoot}/#")
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce))
            .WithTopicFilter(f => f
                .WithTopic(_options.StatusTopic)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce))
            .Build();

        await _client.SubscribeAsync(subscribeOptions, ct);

        // Anuncia presença com mensagem retida, contrapartida do Last Will.
        await _client.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(_options.StatusTopic)
            .WithPayload("online"u8.ToArray())
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithRetainFlag()
            .Build(), ct);

        await _sink.PublishAsync(ProtocolEvent.Create(
            ProtocolKind.Mqtt,
            EventDirection.Internal,
            $"Conectado ao broker {_options.Host}:{_options.Port}",
            $"CONNACK: {response.ResultCode}. Sessão preexistente no broker: {response.IsSessionPresent}. " +
            $"Assinado '{_options.TelemetryTopicRoot}/#' e publicado '{_options.StatusTopic}' = online (retido).",
            metadata: new Dictionary<string, string>
            {
                ["clientId"] = _options.ClientId,
                ["sessionPresent"] = response.IsSessionPresent.ToString(),
                ["resultCode"] = response.ResultCode.ToString()
            }), ct);
    }

    /// <summary>
    /// Publica uma leitura de telemetria. O tópico inclui o id do dispositivo, o que permite
    /// a um assinante filtrar por dispositivo (<c>lab/telemetry/sensor-01</c>) ou receber todos
    /// (<c>lab/telemetry/+</c>) sem que o publicador saiba disso.
    /// </summary>
    public async Task<MqttPublishResult> PublishTelemetryAsync(
        int qos,
        bool retain,
        string? deviceId = null,
        CancellationToken ct = default)
    {
        if (!_client.IsConnected)
        {
            throw new InvalidOperationException(
                $"Cliente MQTT desconectado de {_options.Host}:{_options.Port}. Suba o broker com 'docker compose -f infra/docker-compose.yml up -d'.");
        }

        var qosLevel = MapQoS(qos);
        var reading = _generator.Next(deviceId);
        var topic = $"{_options.TelemetryTopicRoot}/{reading.DeviceId}";
        var payload = ProtocolJson.SerializeToUtf8(reading);

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .WithQualityOfServiceLevel(qosLevel)
            .WithRetainFlag(retain)
            .WithContentType("application/json")

            // User properties são pares chave/valor arbitrários, novidade do MQTT 5.
            // São o equivalente a headers HTTP: metadados que o broker roteia sem interpretar.
            .WithUserProperty("sequence", Encoding.UTF8.GetBytes(reading.Sequence.ToString()))
            .Build();

        var stopwatch = Stopwatch.StartNew();
        var result = await _client.PublishAsync(message, ct);
        stopwatch.Stop();

        // Nulo em QoS 0: não existe packet identifier quando não existe confirmação a correlacionar.
        var packetId = result.PacketIdentifier ?? 0;

        await _sink.PublishAsync(ProtocolEvent.Create(
            ProtocolKind.Mqtt,
            EventDirection.Outbound,
            $"PUBLISH {topic} (QoS {qos}{(retain ? ", retido" : "")})",
            DescribeQoS(qos, stopwatch.Elapsed.TotalMilliseconds) +
            (retain ? " A flag retain faz o broker guardar esta mensagem e entregá-la imediatamente a qualquer novo assinante do tópico." : ""),
            payload: ProtocolJson.Serialize(reading),
            sizeBytes: payload.Length,
            durationMs: stopwatch.Elapsed.TotalMilliseconds,
            metadata: new Dictionary<string, string>
            {
                ["topic"] = topic,
                ["qos"] = qos.ToString(),
                ["retain"] = retain.ToString(),
                ["packetId"] = packetId.ToString(),
                ["reasonCode"] = result.ReasonCode.ToString()
            }), ct);

        return new MqttPublishResult(
            topic,
            qos,
            retain,
            packetId,
            result.ReasonCode.ToString(),
            stopwatch.Elapsed.TotalMilliseconds);
    }

    /// <summary>Remove uma mensagem retida: publica payload vazio com retain ligado.</summary>
    public async Task ClearRetainedAsync(string deviceId, CancellationToken ct = default)
    {
        var topic = $"{_options.TelemetryTopicRoot}/{deviceId}";

        await _client.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(Array.Empty<byte>())
            .WithRetainFlag()
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build(), ct);

        await _sink.PublishAsync(ProtocolEvent.Create(
            ProtocolKind.Mqtt,
            EventDirection.Outbound,
            $"Retained limpo em {topic}",
            "Publicar payload vazio com retain=true é a única forma de apagar uma mensagem retida. " +
            "Não existe 'DELETE' em MQTT — e assinantes novos continuariam recebendo o valor antigo indefinidamente.",
            metadata: new Dictionary<string, string> { ["topic"] = topic }), ct);
    }

    private Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        // Desliga o ack automático: em QoS 1/2 o PUBACK deve sair depois que a mensagem foi
        // processada com sucesso, não quando ela chegou. Confirmar cedo transforma QoS 1 em
        // QoS 0 disfarçado — se o processo morrer entre o ack e o processamento, a mensagem
        // se perde e ninguém percebe.
        args.AutoAcknowledge = false;

        _ = Task.Run(async () =>
        {
            var message = args.ApplicationMessage;
            var payloadBytes = message.Payload.ToArray();
            var text = Encoding.UTF8.GetString(payloadBytes);

            try
            {
                double? latency = null;
                if (message.Topic.StartsWith(_options.TelemetryTopicRoot, StringComparison.Ordinal) && payloadBytes.Length > 0)
                {
                    var reading = ProtocolJson.Deserialize<TelemetryReading>(payloadBytes);
                    latency = reading?.ElapsedMsSince();
                }

                await _sink.PublishAsync(ProtocolEvent.Create(
                    ProtocolKind.Mqtt,
                    EventDirection.Inbound,
                    $"Recebido {message.Topic} (QoS {(int)message.QualityOfServiceLevel})",
                    message.Retain
                        ? "A flag retain neste delivery indica que a mensagem veio do armazenamento do broker, não de uma publicação ao vivo."
                        : "Entrega ao vivo. O broker desacoplou completamente publicador e assinante: nenhum dos dois conhece o endereço do outro.",
                    payload: text,
                    sizeBytes: payloadBytes.Length,
                    durationMs: latency,
                    metadata: new Dictionary<string, string>
                    {
                        ["topic"] = message.Topic,
                        ["qos"] = ((int)message.QualityOfServiceLevel).ToString(),
                        ["retain"] = message.Retain.ToString()
                    }));

                // Só agora confirmamos.
                await args.AcknowledgeAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                // Sem ack: o broker reentrega no próximo reconnect (QoS 1/2).
                _logger.LogError(ex, "Falha ao processar mensagem de {Topic}", message.Topic);
            }
        });

        return Task.CompletedTask;
    }

    private async Task OnConnectedAsync(MqttClientConnectedEventArgs args)
    {
        _logger.LogInformation("MQTT conectado: {ResultCode}", args.ConnectResult.ResultCode);
        await Task.CompletedTask;
    }

    private async Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args)
    {
        _lastError = args.Exception?.Message ?? args.Reason.ToString();

        if (args.ClientWasConnected)
        {
            await _sink.PublishAsync(ProtocolEvent.Create(
                ProtocolKind.Mqtt,
                EventDirection.Internal,
                $"Desconectado do broker ({args.Reason})",
                "O laço de supervisão vai reconectar. Como CleanStart=false, o broker guardou a sessão: " +
                "as assinaturas voltam sozinhas e mensagens QoS 1/2 pendentes serão reentregues.",
                level: EventLevel.Warning));
        }
    }

    private async Task DisconnectGracefullyAsync()
    {
        if (!_client.IsConnected)
        {
            return;
        }

        try
        {
            // Publica "offline" antes de sair. Um DISCONNECT limpo faz o broker descartar o
            // Last Will — então quem quer anunciar a saída precisa fazê-lo explicitamente.
            await _client.PublishAsync(new MqttApplicationMessageBuilder()
                .WithTopic(_options.StatusTopic)
                .WithPayload("offline"u8.ToArray())
                .WithRetainFlag()
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build(), CancellationToken.None);

            await _client.DisconnectAsync(new MqttClientDisconnectOptions
            {
                Reason = MqttClientDisconnectOptionsReason.NormalDisconnection
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao desconectar do broker MQTT");
        }
    }

    private static MqttQualityOfServiceLevel MapQoS(int qos) => qos switch
    {
        0 => MqttQualityOfServiceLevel.AtMostOnce,
        1 => MqttQualityOfServiceLevel.AtLeastOnce,
        2 => MqttQualityOfServiceLevel.ExactlyOnce,
        _ => throw new ArgumentOutOfRangeException(nameof(qos), qos, "QoS deve ser 0, 1 ou 2.")
    };

    private static string DescribeQoS(int qos, double elapsedMs) => qos switch
    {
        0 => $"QoS 0 (at-most-once): nenhum handshake. PublishAsync voltou em {elapsedMs:F1}ms porque só entregou os bytes ao socket — o broker pode nunca ter recebido.",
        1 => $"QoS 1 (at-least-once): esperamos o PUBACK do broker ({elapsedMs:F1}ms, um round-trip). A mensagem chega pelo menos uma vez, e pode chegar duplicada. O consumidor precisa ser idempotente.",
        2 => $"QoS 2 (exactly-once): handshake de quatro etapas PUBLISH→PUBREC→PUBREL→PUBCOMP ({elapsedMs:F1}ms, dois round-trips). Caro. Use só quando duplicata for inaceitável e o consumidor não puder deduplicar.",
        _ => string.Empty
    };

    public override void Dispose()
    {
        _client.Dispose();
        base.Dispose();
    }
}
