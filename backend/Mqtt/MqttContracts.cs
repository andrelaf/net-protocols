namespace ProtocolLab.Mqtt;

/// <param name="Connected">Se há uma sessão MQTT ativa agora.</param>
/// <param name="Broker">Endereço do broker configurado.</param>
/// <param name="ClientId">Id de sessão usado no CONNECT.</param>
/// <param name="LastError">Motivo da última falha, quando houver.</param>
public sealed record MqttStatus(bool Connected, string Broker, string ClientId, string? LastError);

/// <param name="Topic">Tópico publicado.</param>
/// <param name="QoS">0, 1 ou 2.</param>
/// <param name="Retained">Se o broker deve guardar a mensagem como "última conhecida".</param>
/// <param name="PacketIdentifier">
/// Presente apenas em QoS 1 e 2. Em QoS 0 vale zero: não há pacote para confirmar,
/// porque não há confirmação.
/// </param>
/// <param name="ReasonCode">Código de retorno do PUBACK/PUBCOMP (MQTT 5).</param>
/// <param name="ElapsedMs">Tempo até o broker confirmar. Cresce visivelmente de QoS 0 → 1 → 2.</param>
public sealed record MqttPublishResult(
    string Topic,
    int QoS,
    bool Retained,
    ushort PacketIdentifier,
    string ReasonCode,
    double ElapsedMs);
