namespace ProtocolLab.Shared.Contracts;

/// <summary>
/// Identifica qual demonstração gerou um evento. O valor é serializado como string
/// (ver <see cref="ProtocolJson"/>) porque o frontend usa esse nome como chave de aba.
/// </summary>
public enum ProtocolKind
{
    Udp,
    Quic,
    Mqtt,
    Amqp,
    Coap,
    Gateway
}

/// <summary>
/// Direção do tráfego do ponto de vista do <c>Gateway</c>.
/// </summary>
public enum EventDirection
{
    /// <summary>O gateway enviou algo (publish, request, datagrama).</summary>
    Outbound,

    /// <summary>O gateway recebeu algo (delivery, response, notificação).</summary>
    Inbound,

    /// <summary>Mudança de estado sem tráfego de dados: conectou, reconectou, timeout, ack.</summary>
    Internal
}

public enum EventLevel
{
    Debug,
    Info,
    Warning,
    Error
}
