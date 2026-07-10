using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProtocolLab.Shared.Contracts;

namespace ProtocolLab.Quic;

/// <summary>
/// Servidor QUIC de eco.
///
/// <para>
/// Estrutura em três níveis, que é a forma correta de pensar QUIC:
/// <list type="number">
///   <item><b>Listener</b> — aceita conexões numa porta UDP.</item>
///   <item><b>Conexão</b> — um handshake TLS 1.3, uma identidade, um controle de congestionamento.</item>
///   <item><b>Stream</b> — unidade de troca de dados, barata, independente das demais.</item>
/// </list>
/// A independência do nível 3 é a razão de o QUIC existir. Em HTTP/2 sobre TCP, um pacote
/// perdido trava <i>todas</i> as requisições multiplexadas naquela conexão, porque o TCP
/// entrega bytes em ordem para a camada acima. Em QUIC, a perda afeta apenas o stream
/// daquele pacote.
/// </para>
/// </summary>
public sealed class QuicEchoServer : BackgroundService
{
    private readonly IProtocolEventSink _sink;
    private readonly ILogger<QuicEchoServer> _logger;
    private readonly QuicDemoOptions _options;

    public QuicEchoServer(IProtocolEventSink sink, IOptions<QuicDemoOptions> options, ILogger<QuicEchoServer> logger)
    {
        _sink = sink;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // QUIC depende do msquic. Vem embutido no runtime no Windows 11 / Server 2022+;
        // no Linux exige o pacote libmsquic. Sempre verifique antes de usar.
        if (!QuicListener.IsSupported)
        {
            _logger.LogWarning("QUIC não é suportado neste ambiente (msquic ausente ou TLS 1.3 indisponível)");
            await _sink.PublishAsync(ProtocolEvent.Create(
                ProtocolKind.Quic,
                EventDirection.Internal,
                "QUIC indisponível neste host",
                "QuicListener.IsSupported retornou false. No Linux instale libmsquic; no Windows são necessários Win11/Server 2022 ou superior. " +
                "A aba QUIC continuará explicando o protocolo, mas sem tráfego real.",
                level: EventLevel.Error), stoppingToken);
            return;
        }

        using var certificate = DevelopmentCertificate.CreateSelfSigned();

        await using var listener = await QuicListener.ListenAsync(new QuicListenerOptions
        {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, _options.Port),
            ApplicationProtocols = [_options.ApplicationProtocol],
            ConnectionOptionsCallback = (_, _, _) => ValueTask.FromResult(new QuicServerConnectionOptions
            {
                // Códigos de erro no nível QUIC. Diferente de TCP, o encerramento é explícito
                // e carrega um código que o par consegue interpretar.
                DefaultStreamErrorCode = 0x0A,
                DefaultCloseErrorCode = 0x0B,
                ServerAuthenticationOptions = new SslServerAuthenticationOptions
                {
                    ApplicationProtocols = [_options.ApplicationProtocol],
                    ServerCertificate = certificate
                }
            })
        }, stoppingToken);

        _logger.LogInformation("Listener QUIC em {Endpoint} (ALPN {Alpn})", listener.LocalEndPoint, _options.Alpn);

        await _sink.PublishAsync(ProtocolEvent.Create(
            ProtocolKind.Quic,
            EventDirection.Internal,
            $"Listener QUIC em :{_options.Port} (ALPN '{_options.Alpn}')",
            "O listener já carregou um certificado TLS 1.3. Não existe QUIC sem criptografia — nem em loopback.",
            metadata: new Dictionary<string, string> { ["alpn"] = _options.Alpn }), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            QuicConnection connection;
            try
            {
                connection = await listener.AcceptConnectionAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (QuicException ex)
            {
                _logger.LogWarning(ex, "Falha ao aceitar conexão QUIC");
                continue;
            }

            // Cada conexão é tratada em paralelo; não bloqueamos o accept loop.
            _ = HandleConnectionAsync(connection, stoppingToken);
        }
    }

    private async Task HandleConnectionAsync(QuicConnection connection, CancellationToken ct)
    {
        await using (connection)
        {
            await _sink.PublishAsync(ProtocolEvent.Create(
                ProtocolKind.Quic,
                EventDirection.Inbound,
                $"Conexão aceita de {connection.RemoteEndPoint}",
                "O handshake TLS 1.3 terminou junto com o handshake de transporte — um único round-trip. " +
                "Numa reconexão o cliente pode usar 0-RTT e enviar dados no primeiro pacote.",
                metadata: new Dictionary<string, string>
                {
                    ["alpn"] = connection.NegotiatedApplicationProtocol.ToString(),
                    ["remote"] = connection.RemoteEndPoint.ToString()
                }), ct);

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var stream = await connection.AcceptInboundStreamAsync(ct);

                    // Streams também são tratados em paralelo. Se processássemos um de cada vez,
                    // reintroduziríamos exatamente o head-of-line blocking que o QUIC elimina.
                    _ = HandleStreamAsync(stream, ct);
                }
            }
            catch (QuicException ex) when (ex.QuicError is QuicError.ConnectionAborted or QuicError.ConnectionIdle)
            {
                // Encerramento normal: o cliente fechou a conexão ou ela expirou por inatividade.
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erro na conexão QUIC");
            }
        }
    }

    private async Task HandleStreamAsync(QuicStream stream, CancellationToken ct)
    {
        await using (stream)
        {
            try
            {
                // O cliente sinaliza fim da requisição com CompleteWrites(); lemos até o EOF.
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, ct);

                var request = ProtocolJson.Deserialize<QuicEchoRequest>(buffer.ToArray())
                              ?? new QuicEchoRequest("(vazio)");

                var delay = request.Slow ? _options.SlowStreamDelayMs : 0;
                if (delay > 0)
                {
                    await _sink.PublishAsync(ProtocolEvent.Create(
                        ProtocolKind.Quic,
                        EventDirection.Internal,
                        $"Stream {stream.Id} ('{request.StreamLabel}') vai demorar {delay}ms",
                        "Este stream foi marcado como lento. Observe que os outros streams da mesma conexão respondem imediatamente: " +
                        "eles não esperam por este. Em HTTP/2 sobre TCP, um pacote perdido aqui travaria todos os demais.",
                        metadata: new Dictionary<string, string> { ["streamId"] = stream.Id.ToString() },
                        level: EventLevel.Warning), ct);

                    await Task.Delay(delay, ct);
                }

                var response = new QuicEchoResponse(request.Message, stream.Id, delay);
                var payload = ProtocolJson.SerializeToUtf8(response);

                await stream.WriteAsync(payload, ct);

                // Sinaliza fim da resposta. Sem isso, o cliente ficaria bloqueado lendo.
                stream.CompleteWrites();

                await _sink.PublishAsync(ProtocolEvent.Create(
                    ProtocolKind.Quic,
                    EventDirection.Outbound,
                    $"Eco no stream {stream.Id} ('{request.StreamLabel}')",
                    "Streams bidirecionais abertos pelo cliente recebem ids 0, 4, 8… — os dois bits menos significativos codificam quem abriu e se é bidirecional.",
                    payload: request.Message,
                    sizeBytes: payload.Length,
                    metadata: new Dictionary<string, string>
                    {
                        ["streamId"] = stream.Id.ToString(),
                        ["slow"] = request.Slow.ToString(),
                        ["serverDelayMs"] = delay.ToString()
                    }), ct);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erro no stream QUIC {StreamId}", stream.Id);
            }
        }
    }
}
