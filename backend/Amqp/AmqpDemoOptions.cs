namespace ProtocolLab.Amqp;

public sealed class AmqpDemoOptions
{
    public const string SectionName = "Amqp";

    /// <summary>
    /// Credenciais padrão do RabbitMQ. <c>guest</c> só funciona a partir de <c>localhost</c>;
    /// o broker recusa <c>guest</c> vindo da rede, o que é uma boa configuração padrão.
    /// </summary>
    public string Uri { get; set; } = "amqp://guest:guest@localhost:5672/";

    /// <summary>
    /// Exchange do tipo <c>topic</c>. É o exchange que decide o roteamento — não a fila,
    /// e definitivamente não o publicador. O publicador só conhece uma routing key.
    /// </summary>
    public string Exchange { get; set; } = "lab.telemetry";

    public string Queue { get; set; } = "lab.telemetry.q";

    /// <summary>Dead-letter exchange: para onde vão mensagens rejeitadas sem requeue.</summary>
    public string DeadLetterExchange { get; set; } = "lab.telemetry.dlx";

    public string DeadLetterQueue { get; set; } = "lab.telemetry.dlq";

    /// <summary>
    /// Padrão de binding. <c>#</c> casa zero ou mais palavras; <c>*</c> casa exatamente uma.
    /// Publicamos com routing key <c>telemetry.&lt;device&gt;</c>.
    /// </summary>
    public string BindingPattern { get; set; } = "telemetry.#";

    /// <summary>
    /// Quantas mensagens não confirmadas o broker entrega a este consumidor antes de parar.
    ///
    /// <para>
    /// <b>Este é o parâmetro mais importante e mais ignorado do AMQP.</b> Sem
    /// <c>basic.qos</c>, o broker despeja a fila inteira no primeiro consumidor que
    /// conectar: a memória do processo explode e os outros consumidores ficam ociosos.
    /// Prefetch é o que faz balanceamento de carga funcionar.
    /// </para>
    /// </summary>
    public ushort Prefetch { get; set; } = 20;

    public int ReconnectDelaySeconds { get; set; } = 5;
}
