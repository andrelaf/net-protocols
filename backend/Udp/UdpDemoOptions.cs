namespace ProtocolLab.Udp;

public sealed class UdpDemoOptions
{
    public const string SectionName = "Udp";

    /// <summary>Porta do servidor de telemetria embutido.</summary>
    public int Port { get; set; } = 5001;

    public string Host { get; set; } = "127.0.0.1";

    /// <summary>
    /// Probabilidade (0–100) de o cliente <i>deliberadamente</i> não enviar um datagrama,
    /// simulando perda na rede. UDP não retransmite: o pacote simplesmente some, e o
    /// receptor só percebe porque numeramos as mensagens.
    /// </summary>
    public int SimulatedLossPercent { get; set; }

    /// <summary>
    /// Probabilidade (0–100) de atrasar um datagrama alguns milissegundos, fazendo-o chegar
    /// depois do seguinte. Na internet real isso acontece por roteamento multi-caminho (ECMP).
    /// </summary>
    public int SimulatedReorderPercent { get; set; }

    /// <summary>
    /// Maior payload que o .NET aceita num único datagrama IPv4:
    /// 65.535 (total IP) − 20 (cabeçalho IP) − 8 (cabeçalho UDP) = 65.507 bytes.
    /// Acima disso o socket lança <c>SocketException</c> (<c>MessageSize</c>).
    /// </summary>
    public const int MaxDatagramPayload = 65_507;

    /// <summary>
    /// Limite prático para evitar fragmentação IP em redes Ethernet:
    /// 1500 (MTU) − 20 (IP) − 8 (UDP) = 1472 bytes.
    /// Acima disso o datagrama é quebrado em fragmentos IP, e <b>perder um único fragmento
    /// descarta o datagrama inteiro</b>. É por isso que QUIC, DNS e QoS de VoIP mantêm os
    /// pacotes abaixo desse valor.
    /// </summary>
    public const int SafePayloadWithoutFragmentation = 1472;
}
