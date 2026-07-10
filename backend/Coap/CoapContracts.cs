namespace ProtocolLab.Coap;

/// <param name="Code">Código de resposta formatado, ex. "2.05".</param>
/// <param name="Payload">Corpo da resposta.</param>
/// <param name="ElapsedMs">Tempo total, incluindo eventuais retransmissões.</param>
/// <param name="Transmissions">Quantas vezes a requisição precisou ser enviada. &gt; 1 significa perda.</param>
/// <param name="Piggybacked">
/// Verdadeiro quando a resposta veio dentro do próprio ACK. É o caminho comum e o mais barato:
/// uma requisição, uma resposta, dois pacotes no total.
/// </param>
/// <param name="RequestBytes">Tamanho da requisição no fio, cabeçalho incluído.</param>
/// <param name="ResponseBytes">Tamanho da resposta no fio, cabeçalho incluído.</param>
public sealed record CoapResponse(
    string Code,
    string Payload,
    double ElapsedMs,
    int Transmissions,
    bool Piggybacked,
    int RequestBytes,
    int ResponseBytes);

/// <param name="Notifications">Notificações recebidas durante a observação.</param>
public sealed record CoapObserveResult(
    string Resource,
    int Notifications,
    double ElapsedMs,
    IReadOnlyList<string> Payloads);
