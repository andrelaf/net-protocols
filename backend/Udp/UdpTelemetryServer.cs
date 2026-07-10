using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProtocolLab.Shared.Contracts;

namespace ProtocolLab.Udp;

/// <summary>
/// Servidor UDP embutido. Escuta datagramas de telemetria e, a partir do número de
/// sequência de cada dispositivo, deduz o que a rede fez com os pacotes.
///
/// <para>
/// <b>O ponto pedagógico:</b> este servidor não confirma nada. Não há handshake, não há
/// ACK, não há retransmissão. Tudo que ele sabe sobre a saúde da comunicação vem de um
/// contador que <i>a aplicação</i> colocou dentro do payload. Em TCP essa contabilidade
/// existe no kernel e você nunca a vê; em UDP, se você quiser, tem que construir.
/// </para>
/// </summary>
public sealed class UdpTelemetryServer : BackgroundService
{
    private readonly IProtocolEventSink _sink;
    private readonly ILogger<UdpTelemetryServer> _logger;
    private readonly UdpDemoOptions _options;

    /// <summary>Última sequência vista por dispositivo. Base para detectar perda e reordenação.</summary>
    private readonly Dictionary<string, long> _lastSequence = [];

    /// <summary>Sequências já entregues, para identificar duplicatas (UDP permite entrega dupla).</summary>
    private readonly HashSet<(string Device, long Sequence)> _seen = [];

    public UdpTelemetryServer(
        IProtocolEventSink sink,
        IOptions<UdpDemoOptions> options,
        ILogger<UdpTelemetryServer> logger)
    {
        _sink = sink;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Bind explícito em loopback. Um servidor UDP exposto em 0.0.0.0 sem autenticação
        // nem rate limit é vetor de amplificação de DDoS — ver comentários da aba UDP.
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, _options.Port));

        // No Windows, um ICMP "port unreachable" de um envio anterior faz o próximo
        // ReceiveAsync lançar SocketException(ConnectionReset). Desligamos esse comportamento:
        // um servidor UDP não deve morrer porque um cliente sumiu.
        DisableConnectionResetOnWindows(udp.Client);

        _logger.LogInformation("Servidor UDP escutando em {Endpoint}", udp.Client.LocalEndPoint);

        await _sink.PublishAsync(ProtocolEvent.Create(
            ProtocolKind.Udp,
            EventDirection.Internal,
            $"Servidor UDP escutando em :{_options.Port}",
            "Nenhum handshake aconteceu. O socket está apenas ligado a uma porta, pronto para receber datagramas de qualquer origem."),
            stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await udp.ReceiveAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException ex)
            {
                // Um datagrama malformado ou um reset não pode derrubar o servidor.
                _logger.LogWarning(ex, "Falha ao receber datagrama UDP");
                continue;
            }

            await HandleDatagramAsync(result, stoppingToken);
        }

        _logger.LogInformation("Servidor UDP encerrado");
    }

    private async Task HandleDatagramAsync(UdpReceiveResult result, CancellationToken ct)
    {
        var bytes = result.Buffer.Length;

        TelemetryReading? reading;
        try
        {
            reading = ProtocolJson.Deserialize<TelemetryReading>(result.Buffer);
        }
        catch (Exception ex)
        {
            // Payload que não é JSON: no lab isso vem do botão "enviar payload grande".
            await _sink.PublishAsync(ProtocolEvent.Create(
                ProtocolKind.Udp,
                EventDirection.Inbound,
                $"Datagrama opaco de {bytes:N0} bytes",
                $"Recebido de {result.RemoteEndPoint}, mas o corpo não é uma leitura JSON válida ({ex.GetType().Name}). " +
                "Numa porta UDP pública, qualquer um pode enviar qualquer coisa: valide antes de desserializar.",
                sizeBytes: bytes,
                level: EventLevel.Warning), ct);
            return;
        }

        if (reading is null)
        {
            return;
        }

        var latencyMs = reading.ElapsedMsSince();
        var metadata = new Dictionary<string, string>
        {
            ["remoteEndpoint"] = result.RemoteEndPoint.ToString(),
            ["device"] = reading.DeviceId,
            ["sequence"] = reading.Sequence.ToString(),
            ["fragmented"] = (bytes > UdpDemoOptions.SafePayloadWithoutFragmentation).ToString()
        };

        var (title, detail, level) = ClassifyDelivery(reading, metadata);

        await _sink.PublishAsync(ProtocolEvent.Create(
            ProtocolKind.Udp,
            EventDirection.Inbound,
            title,
            detail,
            payload: ProtocolJson.Serialize(reading),
            sizeBytes: bytes,
            durationMs: latencyMs,
            metadata: metadata,
            level: level), ct);
    }

    /// <summary>
    /// Compara a sequência recebida com a última vista e traduz a diferença em linguagem
    /// de rede: buraco = perda, retrocesso = reordenação, repetição = duplicata.
    /// </summary>
    private (string Title, string Detail, EventLevel Level) ClassifyDelivery(
        TelemetryReading reading,
        Dictionary<string, string> metadata)
    {
        var key = (reading.DeviceId, reading.Sequence);

        if (!_seen.Add(key))
        {
            metadata["anomaly"] = "duplicate";
            return ($"Duplicata: {reading.DeviceId} #{reading.Sequence}",
                "O mesmo datagrama chegou duas vezes. UDP não deduplica — um roteador pode reenviar, ou o cliente pode ter retransmitido por conta própria. " +
                "Por isso consumidores UDP precisam ser idempotentes.",
                EventLevel.Warning);
        }

        _lastSequence.TryGetValue(reading.DeviceId, out var last);

        if (last == 0 || reading.Sequence == last + 1)
        {
            _lastSequence[reading.DeviceId] = Math.Max(last, reading.Sequence);
            return ($"{reading.DeviceId} #{reading.Sequence} = {reading.Value} {reading.Unit}",
                "Entrega em ordem. Note que o servidor não enviou nenhum ACK: o cliente não tem como saber que este pacote chegou.",
                EventLevel.Info);
        }

        if (reading.Sequence > last + 1)
        {
            var missing = reading.Sequence - last - 1;
            _lastSequence[reading.DeviceId] = reading.Sequence;
            metadata["anomaly"] = "loss";
            metadata["missingCount"] = missing.ToString();

            return ($"Perda detectada: faltam {missing} pacote(s) de {reading.DeviceId}",
                $"Recebemos #{reading.Sequence} logo após #{last}. Os datagramas #{last + 1}..#{reading.Sequence - 1} nunca chegarão — " +
                "não existe retransmissão em UDP. A aplicação decide se ignora (telemetria, voz, vídeo) ou se pede reenvio por conta própria.",
                EventLevel.Warning);
        }

        metadata["anomaly"] = "reorder";
        return ($"Fora de ordem: {reading.DeviceId} #{reading.Sequence} chegou após #{last}",
            "Datagramas são independentes e podem trafegar por caminhos diferentes (ECMP), chegando trocados. " +
            "TCP esconderia isso de você às custas de head-of-line blocking; UDP entrega na hora e deixa a decisão para a aplicação.",
            EventLevel.Warning);
    }

    /// <summary>
    /// Desativa <c>SIO_UDP_CONNRESET</c>. Sem isso, no Windows, um ICMP Port Unreachable
    /// recebido em resposta a um envio anterior faz o próximo <c>ReceiveAsync</c> lançar
    /// <c>SocketException</c> — uma armadilha clássica de servidores UDP em .NET.
    /// </summary>
    private static void DisableConnectionResetOnWindows(Socket socket)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        const int SIO_UDP_CONNRESET = unchecked((int)0x9800000C);
        socket.IOControl(SIO_UDP_CONNRESET, [0, 0, 0, 0], null);
    }
}
