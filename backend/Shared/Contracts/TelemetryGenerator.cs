namespace ProtocolLab.Shared.Contracts;

/// <summary>
/// Gera leituras sintéticas com número de sequência monotônico por dispositivo.
///
/// <para>
/// Thread-safe: o contador usa <see cref="Interlocked"/> porque as demos publicam de
/// múltiplos threads (loop de background do UDP, handler HTTP do MQTT, consumidor AMQP).
/// </para>
/// </summary>
public sealed class TelemetryGenerator
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, long> _sequences = [];
    private readonly Random _random = Random.Shared;

    /// <summary>Dispositivos fictícios usados como routing keys / segmentos de tópico.</summary>
    public static readonly string[] Devices = ["sensor-01", "sensor-02", "sensor-03"];

    public TelemetryReading Next(string? deviceId = null, string sensor = "temperature")
    {
        deviceId ??= Devices[_random.Next(Devices.Length)];

        long sequence;
        lock (_gate)
        {
            _sequences.TryGetValue(deviceId, out var current);
            sequence = current + 1;
            _sequences[deviceId] = sequence;
        }

        var (value, unit) = sensor switch
        {
            "humidity" => (Math.Round(40 + _random.NextDouble() * 30, 2), "%"),
            "pressure" => (Math.Round(980 + _random.NextDouble() * 40, 2), "hPa"),
            _ => (Math.Round(18 + _random.NextDouble() * 12, 2), "C")
        };

        return new TelemetryReading(deviceId, sensor, value, unit, sequence, DateTimeOffset.UtcNow);
    }
}
