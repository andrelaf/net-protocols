using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProtocolLab.Shared.Contracts;

/// <summary>
/// Payload de domínio comum a todas as demos: uma leitura de sensor.
///
/// <para>
/// Usar a mesma carga em UDP, QUIC, MQTT, AMQP e CoAP é o que torna a comparação honesta.
/// O <see cref="Sequence"/> existe para tornar visíveis perda e reordenação: em UDP você
/// vai ver buracos e trocas de ordem; em AMQP e MQTT QoS 1+, não.
/// </para>
/// </summary>
/// <param name="DeviceId">Identifica o produtor. Vira routing key no AMQP e segmento de tópico no MQTT.</param>
/// <param name="Sensor">Tipo da grandeza medida (<c>temperature</c>, <c>humidity</c>…).</param>
/// <param name="Value">Valor da medição.</param>
/// <param name="Unit">Unidade (<c>C</c>, <c>%</c>).</param>
/// <param name="Sequence">Número monotônico por dispositivo; revela perda e reordenação.</param>
/// <param name="SentAt">Instante de emissão, em UTC. Permite calcular latência fim-a-fim.</param>
public sealed record TelemetryReading(
    string DeviceId,
    string Sensor,
    double Value,
    string Unit,
    long Sequence,
    DateTimeOffset SentAt)
{
    /// <summary>Latência fim-a-fim aproximada. Só é confiável com relógios sincronizados.</summary>
    public double ElapsedMsSince() => (DateTimeOffset.UtcNow - SentAt).TotalMilliseconds;
}

/// <summary>
/// Opções de serialização compartilhadas.
///
/// <para>
/// <b>Boa prática:</b> um <see cref="JsonSerializerOptions"/> único e estático. Instanciar
/// <c>new JsonSerializerOptions()</c> a cada chamada recompila os metadados de reflexão do
/// tipo toda vez — é uma das causas mais comuns de lentidão silenciosa em serviços .NET.
/// </para>
///
/// <para>
/// <b>Antipattern demonstrado nas abas:</b> usar JSON como formato de fio em transportes
/// restritos. Em CoAP sobre 6LoWPAN, o MTU útil gira em torno de 60–80 bytes; um JSON de
/// 120 bytes já força fragmentação em blocos. Produção usaria CBOR (RFC 8949) ou Protobuf.
/// Aqui mantemos JSON porque o objetivo é que você <i>leia</i> o pacote no navegador.
/// </para>
/// </summary>
public static class ProtocolJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        // Enums como string: o frontend usa "Mqtt"/"Udp" como chave de aba, não 0/1.
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static byte[] SerializeToUtf8<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Options);

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(ReadOnlySpan<byte> utf8Json) => JsonSerializer.Deserialize<T>(utf8Json, Options);
}
