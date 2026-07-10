using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProtocolLab.Shared.Contracts;

namespace ProtocolLab.Coap;

/// <summary>
/// Servidor CoAP mínimo (RFC 7252) com suporte a Observe (RFC 7641).
///
/// <para>
/// Recursos expostos:
/// <list type="bullet">
///   <item><c>/telemetry</c> — GET devolve uma leitura; observável.</item>
///   <item><c>/time</c> — GET devolve a hora do servidor.</item>
///   <item><c>/.well-known/core</c> — descoberta de recursos em <c>application/link-format</c>.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Escopo:</b> implementamos o subconjunto que ensina o protocolo — CON/NON, respostas
/// piggyback, Observe, e o formato binário completo das opções. Ficam de fora transferência
/// em blocos (RFC 7959), deduplicação por Message ID, e DTLS. Um servidor de produção
/// precisa dos três.
/// </para>
/// </summary>
public sealed class CoapServer : BackgroundService
{
    private readonly IProtocolEventSink _sink;
    private readonly ILogger<CoapServer> _logger;
    private readonly CoapDemoOptions _options;
    private readonly TelemetryGenerator _generator;

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _observers = new();
    private int _messageIdCounter = Random.Shared.Next(ushort.MaxValue);

    private UdpClient? _socket;

    public CoapServer(
        IProtocolEventSink sink,
        IOptions<CoapDemoOptions> options,
        TelemetryGenerator generator,
        ILogger<CoapServer> logger)
    {
        _sink = sink;
        _logger = logger;
        _generator = generator;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, _options.Port));
        DisableConnectionResetOnWindows(socket.Client);
        _socket = socket;

        _logger.LogInformation("Servidor CoAP escutando em {Endpoint}", socket.Client.LocalEndPoint);

        await _sink.PublishAsync(ProtocolEvent.Create(
            ProtocolKind.Coap,
            EventDirection.Internal,
            $"Servidor CoAP escutando em :{_options.Port}",
            "Recursos: /telemetry (observável), /time, /.well-known/core. Sem DTLS — em produção seria coaps:// na 5684.",
            metadata: new Dictionary<string, string> { ["port"] = _options.Port.ToString() }), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            UdpReceiveResult received;
            try
            {
                received = await socket.ReceiveAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException ex)
            {
                _logger.LogWarning(ex, "Erro ao receber datagrama CoAP");
                continue;
            }

            _ = HandleAsync(received, stoppingToken);
        }

        foreach (var cts in _observers.Values)
        {
            await cts.CancelAsync();
        }
    }

    private async Task HandleAsync(UdpReceiveResult received, CancellationToken ct)
    {
        CoapMessage request;
        try
        {
            request = CoapMessage.Decode(received.Buffer);
        }
        catch (FormatException ex)
        {
            _logger.LogWarning("Mensagem CoAP malformada de {Remote}: {Message}", received.RemoteEndPoint, ex.Message);
            return;
        }

        // RST cancela uma observação. É como um cliente CoAP diz "pare de me mandar isso"
        // quando reinicia e recebe uma notificação que já não espera.
        if (request.Type == CoapMessageType.Reset)
        {
            RemoveObserversOf(received.RemoteEndPoint);
            await _sink.PublishAsync(ProtocolEvent.Create(
                ProtocolKind.Coap,
                EventDirection.Inbound,
                $"RST de {received.RemoteEndPoint}: observação cancelada",
                "Um Reset em resposta a uma notificação remove o observador. É o mecanismo de limpeza do RFC 7641.",
                metadata: new Dictionary<string, string> { ["messageId"] = request.MessageId.ToString() }), ct);
            return;
        }

        if (request.Code is not CoapCode.Get)
        {
            await SendAsync(BuildResponse(request, CoapCode.MethodNotAllowed, "Apenas GET nesta demo."u8.ToArray(), CoapContentFormat.TextPlain), received.RemoteEndPoint, ct);
            return;
        }

        await _sink.PublishAsync(ProtocolEvent.Create(
            ProtocolKind.Coap,
            EventDirection.Inbound,
            $"{(request.Type == CoapMessageType.Confirmable ? "CON" : "NON")} GET /{request.UriPath}",
            $"Message ID {request.MessageId}, token 0x{request.TokenHex}. Cabeçalho de 4 bytes + {request.Token.Length} de token. " +
            "O Message ID casa o ACK; o token casa a resposta com a requisição.",
            sizeBytes: received.Buffer.Length,
            metadata: new Dictionary<string, string>
            {
                ["type"] = request.Type.ToString(),
                ["messageId"] = request.MessageId.ToString(),
                ["token"] = request.TokenHex,
                ["uriPath"] = "/" + request.UriPath
            }), ct);

        // Perda simulada: ignoramos a requisição de propósito. Um CON será retransmitido
        // pelo cliente; um NON simplesmente se perde. É a demonstração da diferença.
        if (_options.SimulatedRequestLossPercent > 0 && Random.Shared.Next(100) < _options.SimulatedRequestLossPercent)
        {
            await _sink.PublishAsync(ProtocolEvent.Create(
                ProtocolKind.Coap,
                EventDirection.Internal,
                $"Requisição {request.MessageId} descartada (perda simulada)",
                request.Type == CoapMessageType.Confirmable
                    ? "Como é CON, o cliente vai retransmitir após ACK_TIMEOUT, dobrando o intervalo a cada tentativa."
                    : "Como é NON, ninguém retransmite. A requisição se perdeu para sempre.",
                level: EventLevel.Warning), ct);
            return;
        }

        var observeOption = request.GetOption(CoapOptionNumber.Observe);
        var path = "/" + request.UriPath;

        if (observeOption is not null && path == "/telemetry")
        {
            if (observeOption.Value.Length > 0 && observeOption.AsUInt() == 1)
            {
                RemoveObserversOf(received.RemoteEndPoint);
                await SendAsync(BuildResponse(request, CoapCode.Content, "observe cancelado"u8.ToArray(), CoapContentFormat.TextPlain), received.RemoteEndPoint, ct);
                return;
            }

            await StartObserveAsync(request, received.RemoteEndPoint, ct);
            return;
        }

        var (code, payload, format) = Resolve(path);
        await SendAsync(BuildResponse(request, code, payload, format), received.RemoteEndPoint, ct);

        await _sink.PublishAsync(ProtocolEvent.Create(
            ProtocolKind.Coap,
            EventDirection.Outbound,
            $"{CoapCode.Describe(code)} para {path}",
            request.Type == CoapMessageType.Confirmable
                ? "Resposta piggybacked: o ACK carrega o corpo. Duas mensagens no total — o custo mínimo de um request/response confiável."
                : "Resposta NON: não há ACK a carregar, então mandamos uma mensagem não-confirmável.",
            payload: Encoding.UTF8.GetString(payload),
            sizeBytes: payload.Length,
            metadata: new Dictionary<string, string>
            {
                ["code"] = CoapCode.Format(code),
                ["token"] = request.TokenHex
            }), ct);
    }

    private (byte Code, byte[] Payload, int Format) Resolve(string path) => path switch
    {
        "/telemetry" => (CoapCode.Content, ProtocolJson.SerializeToUtf8(_generator.Next()), CoapContentFormat.Json),

        "/time" => (CoapCode.Content, Encoding.UTF8.GetBytes(DateTimeOffset.UtcNow.ToString("O")), CoapContentFormat.TextPlain),

        // Descoberta padronizada: um cliente CoAP faz GET aqui para saber o que existe.
        // 'obs' marca o recurso como observável; 'ct=50' é o content-format JSON.
        "/.well-known/core" => (
            CoapCode.Content,
            Encoding.UTF8.GetBytes("</telemetry>;obs;ct=50;rt=\"sensor\",</time>;ct=0"),
            CoapContentFormat.LinkFormat),

        _ => (CoapCode.NotFound, Encoding.UTF8.GetBytes($"Recurso {path} não existe."), CoapContentFormat.TextPlain)
    };

    /// <summary>
    /// Registra o observador e responde imediatamente com a primeira representação, marcada
    /// com a opção Observe. Depois, empurra notificações periódicas com o <b>mesmo token</b>.
    /// </summary>
    private async Task StartObserveAsync(CoapMessage request, IPEndPoint remote, CancellationToken ct)
    {
        var key = $"{remote}|{request.TokenHex}";
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        if (_observers.TryRemove(key, out var existing))
        {
            await existing.CancelAsync();
        }
        _observers[key] = cts;

        uint sequence = 2;
        var first = BuildResponse(request, CoapCode.Content, ProtocolJson.SerializeToUtf8(_generator.Next()), CoapContentFormat.Json)
            with
        { Options = [.. BuildResponseOptions(CoapContentFormat.Json), CoapOption.FromUInt(CoapOptionNumber.Observe, sequence)] };

        await SendAsync(first, remote, ct);

        await _sink.PublishAsync(ProtocolEvent.Create(
            ProtocolKind.Coap,
            EventDirection.Outbound,
            $"Observe registrado para token 0x{request.TokenHex}",
            "O cliente enviou GET com a opção Observe=0. Agora o servidor empurra notificações quando o recurso muda — " +
            "pub/sub sem broker, direto entre as duas pontas. É a resposta do CoAP ao MQTT.",
            payload: first.PayloadAsString(),
            metadata: new Dictionary<string, string>
            {
                ["token"] = request.TokenHex,
                ["observeSeq"] = sequence.ToString()
            }), ct);

        _ = Task.Run(async () =>
        {
            try
            {
                for (var i = 0; i < _options.ObserveNotificationLimit && !cts.Token.IsCancellationRequested; i++)
                {
                    await Task.Delay(_options.ObserveIntervalMs, cts.Token);
                    sequence++;

                    // Notificação é NON: perder uma amostra de sensor não justifica retransmitir.
                    // Message ID novo, token igual — é o token que diz ao cliente "isto responde
                    // àquele GET que você fez".
                    var notification = new CoapMessage
                    {
                        Type = CoapMessageType.NonConfirmable,
                        Code = CoapCode.Content,
                        MessageId = NextMessageId(),
                        Token = request.Token,
                        Options =
                        [
                            new CoapOption(CoapOptionNumber.ContentFormat, [CoapContentFormat.Json]),
                            CoapOption.FromUInt(CoapOptionNumber.Observe, sequence)
                        ],
                        Payload = ProtocolJson.SerializeToUtf8(_generator.Next())
                    };

                    await SendAsync(notification, remote, cts.Token);

                    await _sink.PublishAsync(ProtocolEvent.Create(
                        ProtocolKind.Coap,
                        EventDirection.Outbound,
                        $"Notificação Observe #{sequence} → {remote}",
                        "O número de sequência do Observe cresce monotonicamente. Ele existe porque UDP reordena: " +
                        "sem ele, o cliente poderia aplicar uma leitura antiga por cima de uma recente.",
                        payload: notification.PayloadAsString(),
                        sizeBytes: notification.Encode().Length,
                        metadata: new Dictionary<string, string>
                        {
                            ["observeSeq"] = sequence.ToString(),
                            ["token"] = request.TokenHex
                        }), cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha no laço de observe");
            }
            finally
            {
                _observers.TryRemove(key, out _);
                cts.Dispose();
            }
        }, CancellationToken.None);
    }

    private void RemoveObserversOf(IPEndPoint remote)
    {
        var prefix = $"{remote}|";
        foreach (var key in _observers.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)))
        {
            if (_observers.TryRemove(key, out var cts))
            {
                cts.Cancel();
            }
        }
    }

    /// <summary>
    /// Constrói a resposta. Para um CON, respondemos com ACK carregando o corpo (piggyback):
    /// mesmo Message ID, mesmo token. Para um NON, respondemos com NON e um Message ID novo.
    /// </summary>
    private CoapMessage BuildResponse(CoapMessage request, byte code, byte[] payload, int contentFormat)
    {
        var piggybacked = request.Type == CoapMessageType.Confirmable;

        return new CoapMessage
        {
            Type = piggybacked ? CoapMessageType.Acknowledgement : CoapMessageType.NonConfirmable,
            Code = code,
            MessageId = piggybacked ? request.MessageId : NextMessageId(),
            Token = request.Token,
            Options = BuildResponseOptions(contentFormat),
            Payload = payload
        };
    }

    private static IReadOnlyList<CoapOption> BuildResponseOptions(int contentFormat) =>
        [new CoapOption(CoapOptionNumber.ContentFormat, contentFormat == 0 ? [] : [(byte)contentFormat])];

    private async Task SendAsync(CoapMessage message, IPEndPoint remote, CancellationToken ct)
    {
        var socket = _socket;
        if (socket is null)
        {
            return;
        }

        var bytes = message.Encode();
        await socket.SendAsync(bytes, remote, ct);
    }

    private ushort NextMessageId() => (ushort)(Interlocked.Increment(ref _messageIdCounter) & 0xFFFF);

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
