using System.Diagnostics;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProtocolLab.Shared.Contracts;

namespace ProtocolLab.Quic;

/// <summary>
/// Cliente QUIC que abre vários streams numa única conexão para tornar visível a ausência
/// de head-of-line blocking.
/// </summary>
public sealed class QuicEchoClient
{
    private readonly IProtocolEventSink _sink;
    private readonly ILogger<QuicEchoClient> _logger;
    private readonly QuicDemoOptions _options;

    public QuicEchoClient(IProtocolEventSink sink, IOptions<QuicDemoOptions> options, ILogger<QuicEchoClient> logger)
    {
        _sink = sink;
        _logger = logger;
        _options = options.Value;
    }

    public static bool IsSupported => QuicConnection.IsSupported;

    /// <summary>
    /// Abre uma conexão e dispara <paramref name="streamCount"/> streams em paralelo.
    /// O stream de índice 0 é marcado como lento.
    /// </summary>
    public async Task<QuicRunResult> RunParallelStreamsAsync(
        int streamCount,
        string message,
        bool markFirstStreamSlow,
        CancellationToken ct = default)
    {
        if (!QuicConnection.IsSupported)
        {
            throw new PlatformNotSupportedException(
                "QUIC não está disponível. No Linux instale libmsquic; no Windows use Win11 ou Server 2022+.");
        }

        streamCount = Math.Clamp(streamCount, 1, 10);

        var total = Stopwatch.StartNew();
        var handshake = Stopwatch.StartNew();

        await using var connection = await QuicConnection.ConnectAsync(new QuicClientConnectionOptions
        {
            RemoteEndPoint = new IPEndPoint(IPAddress.Parse(_options.Host), _options.Port),
            DefaultStreamErrorCode = 0x0A,
            DefaultCloseErrorCode = 0x0B,
            MaxInboundBidirectionalStreams = 0,
            MaxInboundUnidirectionalStreams = 0,
            ClientAuthenticationOptions = new SslClientAuthenticationOptions
            {
                ApplicationProtocols = [_options.ApplicationProtocol],
                TargetHost = "localhost",

                // ANTIPATTERN, deliberado e isolado neste laboratório: aceitar qualquer
                // certificado. O servidor usa um self-signed gerado em memória, então não há
                // cadeia para validar. Em produção isto anula o TLS: um atacante em posição
                // de man-in-the-middle apresenta o certificado dele e você o aceita.
                RemoteCertificateValidationCallback = (_, _, _, _) => true
            }
        }, ct);

        handshake.Stop();

        var alpn = connection.NegotiatedApplicationProtocol.ToString();
        var subject = connection.RemoteCertificate?.Subject ?? "(nenhum)";

        await _sink.PublishAsync(ProtocolEvent.Create(
            ProtocolKind.Quic,
            EventDirection.Outbound,
            $"Conectado em {handshake.Elapsed.TotalMilliseconds:F1}ms",
            "Handshake de transporte e handshake TLS 1.3 aconteceram juntos. Um TCP+TLS equivalente gastaria dois round-trips separados.",
            durationMs: handshake.Elapsed.TotalMilliseconds,
            metadata: new Dictionary<string, string>
            {
                ["alpn"] = alpn,
                ["certificate"] = subject
            }), ct);

        // Todos os streams são abertos e aguardados em paralelo. É aqui que o experimento
        // acontece: o stream lento não deve empurrar a latência dos outros para cima.
        var tasks = Enumerable.Range(0, streamCount)
            .Select(index => ExchangeOnStreamAsync(
                connection,
                index,
                message,
                slow: markFirstStreamSlow && index == 0,
                ct))
            .ToArray();

        var streams = await Task.WhenAll(tasks);
        total.Stop();

        _logger.LogInformation("QUIC: {Count} streams em {Elapsed}ms", streamCount, total.Elapsed.TotalMilliseconds);

        return new QuicRunResult(
            handshake.Elapsed.TotalMilliseconds,
            total.Elapsed.TotalMilliseconds,
            alpn,
            subject,
            streams);
    }

    private async Task<QuicStreamResult> ExchangeOnStreamAsync(
        QuicConnection connection,
        int index,
        string message,
        bool slow,
        CancellationToken ct)
    {
        var label = slow ? $"stream-{index} (lento)" : $"stream-{index}";
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Abrir um stream é barato: nenhum round-trip. É apenas um id novo dentro
            // de uma conexão que já existe.
            await using var stream = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, ct);

            var request = new QuicEchoRequest(message, slow, label);
            await stream.WriteAsync(ProtocolJson.SerializeToUtf8(request), ct);

            // Meia-fechadura: encerramos nosso lado de escrita, mas continuamos lendo.
            // O servidor usa isso como delimitador de fim de requisição.
            stream.CompleteWrites();

            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);

            var response = ProtocolJson.Deserialize<QuicEchoResponse>(buffer.ToArray());
            stopwatch.Stop();

            await _sink.PublishAsync(ProtocolEvent.Create(
                ProtocolKind.Quic,
                EventDirection.Inbound,
                $"{label} respondeu em {stopwatch.Elapsed.TotalMilliseconds:F1}ms",
                slow
                    ? "Este é o stream lento. Compare o tempo dele com os demais: os outros não esperaram."
                    : "Respondeu sem esperar pelo stream lento. Streams QUIC têm controle de fluxo independente.",
                payload: response?.Echo,
                durationMs: stopwatch.Elapsed.TotalMilliseconds,
                metadata: new Dictionary<string, string>
                {
                    ["streamId"] = stream.Id.ToString(),
                    ["slow"] = slow.ToString()
                }), ct);

            return new QuicStreamResult(label, stream.Id, slow, stopwatch.Elapsed.TotalMilliseconds, response?.Echo, null);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(ex, "Falha no {Label}", label);
            return new QuicStreamResult(label, -1, slow, stopwatch.Elapsed.TotalMilliseconds, null, ex.Message);
        }
    }
}
