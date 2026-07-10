namespace ProtocolLab.Coap;

public sealed class CoapDemoOptions
{
    public const string SectionName = "Coap";

    /// <summary>5683 é a porta padrão do CoAP (5684 para CoAPS sobre DTLS).</summary>
    public int Port { get; set; } = 5683;

    public string Host { get; set; } = "127.0.0.1";

    /// <summary>
    /// ACK_TIMEOUT do RFC 7252 §4.8. O cliente espera este tempo pelo ACK antes de
    /// retransmitir um CON, dobrando o intervalo a cada tentativa.
    /// </summary>
    public int AckTimeoutMs { get; set; } = 2000;

    /// <summary>MAX_RETRANSMIT: até 4 retransmissões, totalizando 5 tentativas.</summary>
    public int MaxRetransmit { get; set; } = 4;

    /// <summary>
    /// Probabilidade de o servidor <i>ignorar</i> uma requisição CON, para que a retransmissão
    /// do cliente fique visível. É a diferença prática entre CON e NON.
    /// </summary>
    public int SimulatedRequestLossPercent { get; set; }

    /// <summary>Intervalo entre notificações de um recurso observado.</summary>
    public int ObserveIntervalMs { get; set; } = 1500;

    /// <summary>Quantas notificações enviar antes de encerrar o observe automaticamente.</summary>
    public int ObserveNotificationLimit { get; set; } = 8;
}
