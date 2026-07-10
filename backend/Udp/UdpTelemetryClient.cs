using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProtocolLab.Shared.Contracts;

namespace ProtocolLab.Udp;

public sealed record UdpBurstResult(int Requested, int Sent, int DroppedBySimulation, int Reordered, double ElapsedMs);

/// <summary>
/// Cliente UDP do laboratório.
///
/// <para>
/// <b>Boa prática aplicada aqui:</b> um único <see cref="UdpClient"/> reutilizado por toda
/// a vida do processo. Criar um socket por mensagem esgota portas efêmeras (o estado
/// <c>TIME_WAIT</c> não existe em UDP, mas o limite de descritores e o custo de bind existem),
/// e é o mesmo erro que se comete instanciando <c>HttpClient</c> em loop.
/// </para>
/// </summary>
public sealed class UdpTelemetryClient : IDisposable
{
    private readonly UdpClient _client = new();
    private readonly IProtocolEventSink _sink;
    private readonly ILogger<UdpTelemetryClient> _logger;
    private readonly UdpDemoOptions _options;
    private readonly TelemetryGenerator _generator;
    private readonly IPEndPoint _target;

    public UdpTelemetryClient(
        IProtocolEventSink sink,
        IOptions<UdpDemoOptions> options,
        TelemetryGenerator generator,
        ILogger<UdpTelemetryClient> logger)
    {
        _sink = sink;
        _logger = logger;
        _generator = generator;
        _options = options.Value;
        _target = new IPEndPoint(IPAddress.Parse(_options.Host), _options.Port);
    }

    /// <summary>
    /// Envia uma rajada de leituras, aplicando as simulações de perda e reordenação
    /// configuradas. Repare que o método retorna assim que os bytes são entregues ao
    /// kernel: <c>SendAsync</c> completar <b>não</b> significa que o pacote chegou.
    /// </summary>
    public async Task<UdpBurstResult> SendBurstAsync(
        int count,
        string? deviceId,
        int? lossPercentOverride = null,
        int? reorderPercentOverride = null,
        CancellationToken ct = default)
    {
        count = Math.Clamp(count, 1, 200);

        // Uma rajada usa um único dispositivo por padrão. O servidor rastreia sequência *por
        // dispositivo*, então alternar entre três produziria três contadores intercalados e
        // esconderia justamente o que queremos ver: buracos e trocas de ordem num mesmo fluxo.
        deviceId ??= TelemetryGenerator.Devices[0];

        var lossPercent = Math.Clamp(lossPercentOverride ?? _options.SimulatedLossPercent, 0, 100);
        var reorderPercent = Math.Clamp(reorderPercentOverride ?? _options.SimulatedReorderPercent, 0, 100);

        var stopwatch = Stopwatch.StartNew();
        var sent = 0;
        var dropped = 0;
        var reordered = 0;
        var delayed = new List<byte[]>();

        for (var i = 0; i < count; i++)
        {
            var reading = _generator.Next(deviceId);
            var payload = ProtocolJson.SerializeToUtf8(reading);

            if (Random.Shared.Next(100) < lossPercent)
            {
                // Simulamos a perda no cliente para que ela seja observável. Numa rede real
                // o descarte aconteceria num roteador congestionado, e ninguém seria avisado.
                dropped++;
                await _sink.PublishAsync(ProtocolEvent.Create(
                    ProtocolKind.Udp,
                    EventDirection.Outbound,
                    $"Pacote #{reading.Sequence} descartado antes de sair",
                    "Simulação de perda. O cliente não recebe erro algum: para a aplicação, o envio 'funcionou'.",
                    sizeBytes: payload.Length,
                    metadata: new Dictionary<string, string> { ["device"] = reading.DeviceId, ["simulated"] = "loss" },
                    level: EventLevel.Warning), ct);
                continue;
            }

            if (Random.Shared.Next(100) < reorderPercent && i < count - 1)
            {
                // Segura o datagrama e envia depois do próximo, produzindo troca de ordem.
                reordered++;
                delayed.Add(payload);
                continue;
            }

            await SendOneAsync(payload, reading, ct);
            sent++;

            // Drena o que estava represado — agora chegando fora de ordem.
            foreach (var held in delayed)
            {
                await _client.SendAsync(held, _target, ct);
                sent++;
            }
            delayed.Clear();
        }

        foreach (var held in delayed)
        {
            await _client.SendAsync(held, _target, ct);
            sent++;
        }

        stopwatch.Stop();
        _logger.LogInformation("Rajada UDP: {Sent}/{Count} enviados em {Elapsed}ms", sent, count, stopwatch.Elapsed.TotalMilliseconds);

        return new UdpBurstResult(count, sent, dropped, reordered, stopwatch.Elapsed.TotalMilliseconds);
    }

    private async Task SendOneAsync(byte[] payload, TelemetryReading reading, CancellationToken ct)
    {
        await _client.SendAsync(payload, _target, ct);

        await _sink.PublishAsync(ProtocolEvent.Create(
            ProtocolKind.Udp,
            EventDirection.Outbound,
            $"Datagrama #{reading.Sequence} → {_target}",
            "Fire-and-forget: o kernel colocou os bytes no fio. Não há confirmação, e SendAsync retornar não prova entrega.",
            payload: ProtocolJson.Serialize(reading),
            sizeBytes: payload.Length,
            metadata: new Dictionary<string, string>
            {
                ["device"] = reading.DeviceId,
                ["sequence"] = reading.Sequence.ToString()
            }), ct);
    }

    /// <summary>
    /// Envia um payload de tamanho arbitrário para tornar visíveis os dois limites do UDP:
    /// fragmentação IP acima de ~1472 bytes e erro de socket acima de 65.507 bytes.
    /// </summary>
    public async Task<string> SendOversizedAsync(int sizeBytes, CancellationToken ct = default)
    {
        sizeBytes = Math.Clamp(sizeBytes, 1, 70_000);
        var payload = new byte[sizeBytes];
        Random.Shared.NextBytes(payload);

        var fragmented = sizeBytes > UdpDemoOptions.SafePayloadWithoutFragmentation;

        try
        {
            await _client.SendAsync(payload, _target, ct);

            var detail = fragmented
                ? $"{sizeBytes:N0} bytes excedem o MTU útil de {UdpDemoOptions.SafePayloadWithoutFragmentation} bytes. " +
                  "O IP fragmentou o datagrama; se um único fragmento se perder, o datagrama inteiro é descartado — a probabilidade de perda cresce com o número de fragmentos."
                : $"{sizeBytes:N0} bytes cabem num único pacote IP. Nenhuma fragmentação.";

            await _sink.PublishAsync(ProtocolEvent.Create(
                ProtocolKind.Udp,
                EventDirection.Outbound,
                $"Datagrama de {sizeBytes:N0} bytes enviado",
                detail,
                sizeBytes: sizeBytes,
                metadata: new Dictionary<string, string> { ["fragmented"] = fragmented.ToString() },
                level: fragmented ? EventLevel.Warning : EventLevel.Info), ct);

            return detail;
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.MessageSize)
        {
            var detail = $"{sizeBytes:N0} bytes ultrapassam o máximo teórico de {UdpDemoOptions.MaxDatagramPayload:N0} bytes " +
                         "(65535 − 20 de cabeçalho IP − 8 de cabeçalho UDP). O socket recusou o envio com SocketError.MessageSize.";

            await _sink.PublishAsync(ProtocolEvent.Create(
                ProtocolKind.Udp,
                EventDirection.Outbound,
                $"Envio de {sizeBytes:N0} bytes rejeitado pelo socket",
                detail,
                sizeBytes: sizeBytes,
                level: EventLevel.Error), ct);

            return detail;
        }
    }

    public void Dispose() => _client.Dispose();
}
