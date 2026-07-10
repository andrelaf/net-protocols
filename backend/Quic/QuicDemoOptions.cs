using System.Net.Security;

namespace ProtocolLab.Quic;

public sealed class QuicDemoOptions
{
    public const string SectionName = "Quic";

    public int Port { get; set; } = 5002;

    public string Host { get; set; } = "127.0.0.1";

    /// <summary>
    /// ALPN é <b>obrigatório</b> em QUIC — não existe conexão QUIC sem negociar um protocolo
    /// de aplicação. HTTP/3 usa "h3"; aqui usamos um identificador próprio, porque este
    /// eco não é HTTP. Cliente e servidor precisam anunciar exatamente a mesma string.
    /// </summary>
    public string Alpn { get; set; } = "protocol-lab";

    /// <summary>
    /// Atraso injetado no stream marcado como lento, para demonstrar ausência de
    /// head-of-line blocking entre streams da mesma conexão.
    /// </summary>
    public int SlowStreamDelayMs { get; set; } = 1500;

    public SslApplicationProtocol ApplicationProtocol => new(Alpn);
}
