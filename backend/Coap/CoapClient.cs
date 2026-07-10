using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProtocolLab.Shared.Contracts;

namespace ProtocolLab.Coap;

/// <summary>
/// Cliente CoAP com retransmissão de mensagens confirmáveis e suporte a Observe.
/// </summary>
public sealed class CoapClient
{
    private readonly IProtocolEventSink _sink;
    private readonly ILogger<CoapClient> _logger;
    private readonly CoapDemoOptions _options;
    private readonly IPEndPoint _server;

    private int _messageIdCounter = Random.Shared.Next(ushort.MaxValue);

    public CoapClient(IProtocolEventSink sink, IOptions<CoapDemoOptions> options, ILogger<CoapClient> logger)
    {
        _sink = sink;
        _logger = logger;
        _options = options.Value;
        _server = new IPEndPoint(IPAddress.Parse(_options.Host), _options.Port);
    }

    /// <summary>
    /// GET num recurso. Com <paramref name="confirmable"/>, o cliente retransmite até receber
    /// o ACK — <b>é aqui que o CoAP compra de volta a confiabilidade que o UDP não oferece</b>,
    /// e paga por ela apenas quando a aplicação pede.
    /// </summary>
    public async Task<CoapResponse> GetAsync(string path, bool confirmable = true, CancellationToken ct = default)
    {
        using var socket = new UdpClient(0);
        DisableConnectionResetOnWindows(socket.Client);

        var request = BuildGet(path, confirmable, observe: null);
        var encoded = request.Encode();
        var stopwatch = Stopwatch.StartNew();

        await _sink.PublishAsync(ProtocolEvent.Create(
            ProtocolKind.Coap,
            EventDirection.Outbound,
            $"{(confirmable ? "CON" : "NON")} GET {path}",
            $"{encoded.Length} bytes no fio: 4 de cabeçalho + {request.Token.Length} de token + opções. " +
            "Uma requisição HTTP/1.1 equivalente passaria de 40 bytes só na linha de request e no header Host.",
            sizeBytes: encoded.Length,
            metadata: new Dictionary<string, string>
            {
                ["type"] = request.Type.ToString(),
                ["messageId"] = request.MessageId.ToString(),
                ["token"] = request.TokenHex
            }), ct);

        // NON não espera nada: dispara e retorna. Se a resposta vier, ótimo; se não, paciência.
        if (!confirmable)
        {
            await socket.SendAsync(encoded, _server, ct);
            var nonResponse = await TryReceiveAsync(socket, request.Token, TimeSpan.FromMilliseconds(_options.AckTimeoutMs), ct);
            stopwatch.Stop();

            return nonResponse is null
                ? new CoapResponse("(sem resposta)", string.Empty, stopwatch.Elapsed.TotalMilliseconds, 1, false, encoded.Length, 0)
                : await ReportAsync(nonResponse, stopwatch, 1, encoded.Length, ct);
        }

        // Retransmissão exponencial: ACK_TIMEOUT, 2×, 4×, 8×… até MAX_RETRANSMIT.
        var timeout = TimeSpan.FromMilliseconds(_options.AckTimeoutMs);

        for (var attempt = 1; attempt <= _options.MaxRetransmit + 1; attempt++)
        {
            await socket.SendAsync(encoded, _server, ct);

            if (attempt > 1)
            {
                await _sink.PublishAsync(ProtocolEvent.Create(
                    ProtocolKind.Coap,
                    EventDirection.Outbound,
                    $"Retransmissão {attempt - 1} de /{path} (timeout {timeout.TotalMilliseconds:F0}ms)",
                    "Nenhum ACK chegou. O Message ID é o mesmo, então o servidor reconhece a duplicata e não processa o pedido duas vezes.",
                    metadata: new Dictionary<string, string> { ["attempt"] = attempt.ToString() },
                    level: EventLevel.Warning), ct);
            }

            var response = await TryReceiveAsync(socket, request.Token, timeout, ct);
            if (response is not null)
            {
                stopwatch.Stop();
                return await ReportAsync(response, stopwatch, attempt, encoded.Length, ct);
            }

            timeout *= 2;
        }

        stopwatch.Stop();
        var attempts = _options.MaxRetransmit + 1;

        await _sink.PublishAsync(ProtocolEvent.Create(
            ProtocolKind.Coap,
            EventDirection.Internal,
            $"Desisti de /{path} após {attempts} tentativas",
            "Esgotado MAX_RETRANSMIT. O CoAP entrega o erro à aplicação em vez de tentar para sempre — " +
            "um sensor a bateria não pode gastar rádio num servidor que não responde.",
            durationMs: stopwatch.Elapsed.TotalMilliseconds,
            level: EventLevel.Error), ct);

        return new CoapResponse("(timeout)", string.Empty, stopwatch.Elapsed.TotalMilliseconds, attempts, false, encoded.Length, 0);
    }

    /// <summary>
    /// Observa <c>/telemetry</c>: um GET com a opção Observe, e o servidor passa a empurrar
    /// notificações. Ao final, mandamos RST para cancelar o registro.
    /// </summary>
    public async Task<CoapObserveResult> ObserveAsync(int maxNotifications = 4, CancellationToken ct = default)
    {
        using var socket = new UdpClient(0);
        DisableConnectionResetOnWindows(socket.Client);

        var request = BuildGet("telemetry", confirmable: true, observe: 0);
        var stopwatch = Stopwatch.StartNew();

        await socket.SendAsync(request.Encode(), _server, ct);

        await _sink.PublishAsync(ProtocolEvent.Create(
            ProtocolKind.Coap,
            EventDirection.Outbound,
            "GET /telemetry com Observe=0",
            "Registrando interesse no recurso. O servidor guardará nosso token e endereço, e enviará notificações " +
            "sempre que a representação mudar. Nenhum broker no meio.",
            metadata: new Dictionary<string, string> { ["token"] = request.TokenHex }), ct);

        var payloads = new List<string>();
        ushort lastNotificationId = 0;

        // Timeout generoso: precisa cobrir o intervalo entre notificações do servidor.
        var window = TimeSpan.FromMilliseconds(_options.ObserveIntervalMs * 2 + _options.AckTimeoutMs);

        while (payloads.Count < maxNotifications && !ct.IsCancellationRequested)
        {
            var message = await TryReceiveAsync(socket, request.Token, window, ct);
            if (message is null)
            {
                break;
            }

            lastNotificationId = message.MessageId;
            payloads.Add(message.PayloadAsString());

            var sequence = message.GetOption(CoapOptionNumber.Observe)?.AsUInt();

            await _sink.PublishAsync(ProtocolEvent.Create(
                ProtocolKind.Coap,
                EventDirection.Inbound,
                $"Notificação recebida (Observe seq {sequence?.ToString() ?? "—"})",
                payloads.Count == 1
                    ? "A primeira notificação veio piggybacked no ACK do GET. As próximas chegarão como NON, sem confirmação."
                    : "Notificação NON: o servidor não espera ACK. Perder uma leitura de sensor não vale o custo de retransmitir.",
                payload: message.PayloadAsString(),
                sizeBytes: message.Encode().Length,
                metadata: new Dictionary<string, string>
                {
                    ["observeSeq"] = sequence?.ToString() ?? "-",
                    ["token"] = message.TokenHex,
                    ["type"] = message.Type.ToString()
                }), ct);
        }

        // Cancela a observação com um RST referenciando a última notificação.
        if (lastNotificationId != 0)
        {
            var reset = new CoapMessage
            {
                Type = CoapMessageType.Reset,
                Code = CoapCode.Empty,
                MessageId = lastNotificationId
            };

            await socket.SendAsync(reset.Encode(), _server, ct);

            await _sink.PublishAsync(ProtocolEvent.Create(
                ProtocolKind.Coap,
                EventDirection.Outbound,
                "RST enviado: observação cancelada",
                "Um RST em resposta a uma notificação é a forma canônica de se desinscrever — inclusive quando o cliente " +
                "reinicia e recebe notificações de um registro que já esqueceu.",
                metadata: new Dictionary<string, string> { ["messageId"] = lastNotificationId.ToString() }), ct);
        }

        stopwatch.Stop();
        return new CoapObserveResult("/telemetry", payloads.Count, stopwatch.Elapsed.TotalMilliseconds, payloads);
    }

    private async Task<CoapResponse> ReportAsync(CoapMessage response, Stopwatch stopwatch, int transmissions, int requestBytes, CancellationToken ct)
    {
        var piggybacked = response.Type == CoapMessageType.Acknowledgement;
        var responseBytes = response.Encode().Length;

        await _sink.PublishAsync(ProtocolEvent.Create(
            ProtocolKind.Coap,
            EventDirection.Inbound,
            $"{CoapCode.Describe(response.Code)} em {stopwatch.Elapsed.TotalMilliseconds:F0}ms",
            piggybacked
                ? $"Resposta piggybacked: o ACK trouxe o corpo junto. Total no fio: {requestBytes} + {responseBytes} bytes."
                : "Resposta não-confirmável.",
            payload: response.PayloadAsString(),
            sizeBytes: responseBytes,
            durationMs: stopwatch.Elapsed.TotalMilliseconds,
            metadata: new Dictionary<string, string>
            {
                ["code"] = CoapCode.Format(response.Code),
                ["transmissions"] = transmissions.ToString(),
                ["token"] = response.TokenHex
            },
            level: response.Code >= 0x80 ? EventLevel.Warning : EventLevel.Info), ct);

        return new CoapResponse(
            CoapCode.Format(response.Code),
            response.PayloadAsString(),
            stopwatch.Elapsed.TotalMilliseconds,
            transmissions,
            piggybacked,
            requestBytes,
            responseBytes);
    }

    /// <summary>
    /// Recebe até <paramref name="timeout"/>, descartando mensagens cujo token não bate.
    /// É o token — não o Message ID — que correlaciona resposta e requisição.
    /// </summary>
    private async Task<CoapMessage?> TryReceiveAsync(UdpClient socket, byte[] token, TimeSpan timeout, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        try
        {
            while (!timeoutCts.Token.IsCancellationRequested)
            {
                var received = await socket.ReceiveAsync(timeoutCts.Token);

                CoapMessage message;
                try
                {
                    message = CoapMessage.Decode(received.Buffer);
                }
                catch (FormatException ex)
                {
                    _logger.LogWarning("Resposta CoAP malformada: {Message}", ex.Message);
                    continue;
                }

                if (message.Token.AsSpan().SequenceEqual(token))
                {
                    return message;
                }

                _logger.LogDebug("Ignorando mensagem com token 0x{Token}", message.TokenHex);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timeout: o chamador decide se retransmite.
        }

        return null;
    }

    private CoapMessage BuildGet(string path, bool confirmable, uint? observe)
    {
        // Cada segmento do caminho é uma opção Uri-Path separada. Não existe a string "/a/b"
        // no fio: existem duas opções, "a" e "b". Isso elimina parsing de URL no dispositivo.
        var options = new List<CoapOption>();
        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            options.Add(new CoapOption(CoapOptionNumber.UriPath, System.Text.Encoding.UTF8.GetBytes(segment)));
        }

        if (observe.HasValue)
        {
            options.Add(CoapOption.FromUInt(CoapOptionNumber.Observe, observe.Value));
        }

        // Token aleatório de 4 bytes. Precisa ser imprevisível: um atacante que adivinha o
        // token consegue forjar respostas, já que UDP não protege contra spoofing de origem.
        var token = new byte[4];
        Random.Shared.NextBytes(token);

        return new CoapMessage
        {
            Type = confirmable ? CoapMessageType.Confirmable : CoapMessageType.NonConfirmable,
            Code = CoapCode.Get,
            MessageId = NextMessageId(),
            Token = token,
            Options = options
        };
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
