namespace ProtocolLab.Shared.Contracts;

/// <summary>
/// Envelope único de telemetria que todas as demonstrações emitem e o frontend consome.
///
/// <para>
/// A escolha de um envelope comum é deliberada: ela permite comparar protocolos muito
/// diferentes lado a lado (um datagrama UDP, um delivery AMQP com ack manual, um stream
/// QUIC) sem que a UI precise conhecer o modelo de objetos de cada biblioteca cliente.
/// </para>
///
/// <para>
/// <b>Boa prática:</b> o envelope carrega <see cref="SizeBytes"/> e <see cref="DurationMs"/>
/// porque as duas perguntas que realmente diferenciam protocolos na prática são
/// "quantos bytes isso custou?" e "quanto tempo levou?". Sem esses dois números, comparar
/// protocolos vira opinião.
/// </para>
/// </summary>
public sealed record ProtocolEvent
{
    /// <summary>
    /// Payloads são truncados antes de sair pelo SignalR. Um cliente MQTT assinando
    /// <c>#</c> num broker movimentado consegue derrubar o navegador do usuário se cada
    /// mensagem for repassada inteira — este é um antipattern real, não teórico.
    /// </summary>
    public const int MaxPayloadPreview = 512;

    /// <summary>Identificador do evento; a UI usa como chave de renderização.</summary>
    public required string Id { get; init; }

    /// <summary>Aba de destino no frontend.</summary>
    public required ProtocolKind Protocol { get; init; }

    /// <summary>Se o gateway enviou, recebeu, ou apenas mudou de estado.</summary>
    public required EventDirection Direction { get; init; }

    /// <summary>Resumo curto, uma linha, exibido na lista de eventos.</summary>
    public required string Title { get; init; }

    /// <summary>Explicação didática do que acabou de acontecer no fio.</summary>
    public string? Detail { get; init; }

    /// <summary>Prévia textual do corpo, truncada em <see cref="MaxPayloadPreview"/>.</summary>
    public string? Payload { get; init; }

    /// <summary>Tamanho do payload de aplicação em bytes (não inclui overhead de cabeçalho).</summary>
    public int? SizeBytes { get; init; }

    /// <summary>Latência medida, quando a operação tem começo e fim observáveis.</summary>
    public double? DurationMs { get; init; }

    /// <summary>
    /// Campos específicos do protocolo: QoS e retain no MQTT, delivery tag no AMQP,
    /// stream id no QUIC, message id e token no CoAP. É aqui que mora a personalidade
    /// de cada protocolo.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    public EventLevel Level { get; init; } = EventLevel.Info;

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Fábrica que aplica o truncamento e gera o id. Prefira este método ao inicializador
    /// de objeto: ele centraliza as invariantes do envelope.
    /// </summary>
    public static ProtocolEvent Create(
        ProtocolKind protocol,
        EventDirection direction,
        string title,
        string? detail = null,
        string? payload = null,
        int? sizeBytes = null,
        double? durationMs = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        EventLevel level = EventLevel.Info)
    {
        // O tamanho reportado é o do payload original, antes do truncamento para exibição.
        var actualSize = sizeBytes ?? (payload is null ? null : System.Text.Encoding.UTF8.GetByteCount(payload));

        var preview = payload is { Length: > MaxPayloadPreview }
            ? string.Concat(payload.AsSpan(0, MaxPayloadPreview), "… [truncado]")
            : payload;

        return new ProtocolEvent
        {
            Id = Guid.NewGuid().ToString("n"),
            Protocol = protocol,
            Direction = direction,
            Title = title,
            Detail = detail,
            Payload = preview,
            SizeBytes = actualSize,
            DurationMs = durationMs,
            Metadata = metadata,
            Level = level
        };
    }
}
