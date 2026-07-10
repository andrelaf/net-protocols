namespace ProtocolLab.Amqp;

public sealed record AmqpStatus(bool Connected, string Broker, string Exchange, string Queue, string? LastError);

/// <param name="RoutingKey">Chave usada pelo exchange para decidir as filas de destino.</param>
/// <param name="Confirmed">
/// Se o broker confirmou a gravação (publisher confirm). Sem confirms, <c>BasicPublishAsync</c>
/// retorna assim que os bytes saem do socket — e uma queda do broker perde a mensagem em silêncio.
/// </param>
/// <param name="Routed">
/// Falso quando <c>mandatory=true</c> e nenhuma fila casou com a routing key: o broker devolve
/// a mensagem via <c>basic.return</c> em vez de descartá-la calado.
/// </param>
/// <param name="ElapsedMs">Tempo até a confirmação do broker.</param>
public sealed record AmqpPublishResult(
    string RoutingKey,
    bool Confirmed,
    bool Routed,
    bool Persistent,
    double ElapsedMs,
    string? Error = null);
