namespace ProtocolLab.Shared.Contracts;

/// <summary>
/// Destino dos eventos emitidos pelas demonstrações.
///
/// <para>
/// Existe para que <c>ProtocolLab.Udp</c>, <c>.Mqtt</c>, <c>.Amqp</c> etc. não referenciem
/// SignalR nem ASP.NET Core. Cada projeto de protocolo depende só de <c>Shared</c>, e o
/// <c>Gateway</c> é o único que sabe que os eventos acabam num WebSocket.
/// </para>
///
/// <para>
/// <b>Contrato importante:</b> implementações devem ser <i>não-bloqueantes e tolerantes a falha</i>.
/// Um sink que lança exceção derruba o loop de recepção do protocolo, e um sink lento
/// aplica contrapressão indevida no consumidor MQTT ou AMQP. Se o transporte da UI estiver
/// lento, o certo é descartar eventos — não segurar o consumidor.
/// </para>
/// </summary>
public interface IProtocolEventSink
{
    ValueTask PublishAsync(ProtocolEvent protocolEvent, CancellationToken cancellationToken = default);
}

/// <summary>
/// Sink nulo, usado em testes e quando uma demo roda fora do gateway.
/// </summary>
public sealed class NullProtocolEventSink : IProtocolEventSink
{
    public static readonly NullProtocolEventSink Instance = new();

    public ValueTask PublishAsync(ProtocolEvent protocolEvent, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;
}
