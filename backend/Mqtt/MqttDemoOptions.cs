namespace ProtocolLab.Mqtt;

public sealed class MqttDemoOptions
{
    public const string SectionName = "Mqtt";

    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 1883;

    /// <summary>
    /// O client id identifica a <i>sessão</i> no broker, não a conexão TCP. Com
    /// <see cref="CleanStart"/> desligado, o broker guarda assinaturas e mensagens QoS 1/2
    /// pendentes sob este id e as reentrega quando o mesmo id reconectar.
    ///
    /// <para>
    /// <b>Antipattern:</b> reutilizar o mesmo client id em duas instâncias. O broker aceita a
    /// nova conexão e <i>derruba</i> a antiga; as duas ficam num laço de reconexão que se
    /// expulsa mutuamente. Se você escalar horizontalmente um serviço MQTT sem tornar o
    /// client id único por réplica, é exatamente isso que acontece.
    /// </para>
    /// </summary>
    public string ClientId { get; set; } = $"protocol-lab-gateway-{Environment.MachineName}";

    /// <summary>Raiz dos tópicos publicados pela demo.</summary>
    public string TelemetryTopicRoot { get; set; } = "lab/telemetry";

    /// <summary>Tópico retido que anuncia se o gateway está online. Também é o Last Will.</summary>
    public string StatusTopic { get; set; } = "lab/status/gateway";

    /// <summary>
    /// Sessão persistente. Falso = o broker preserva o estado da sessão entre conexões.
    /// </summary>
    public bool CleanStart { get; set; }

    /// <summary>
    /// Keep-alive: se o broker não receber nada do cliente nesse intervalo, considera a
    /// conexão morta e publica o Last Will. Valor baixo detecta queda rápido e gasta mais
    /// PINGREQ; valor alto economiza rádio (importante em NB-IoT) e demora a detectar.
    /// </summary>
    public int KeepAliveSeconds { get; set; } = 30;

    /// <summary>Por quanto tempo o broker guarda a sessão após a desconexão (MQTT 5).</summary>
    public uint SessionExpirySeconds { get; set; } = 300;

    public int ReconnectDelaySeconds { get; set; } = 5;
}
