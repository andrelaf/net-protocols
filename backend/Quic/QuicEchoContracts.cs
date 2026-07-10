namespace ProtocolLab.Quic;

/// <param name="Message">Conteúdo a ecoar.</param>
/// <param name="Slow">
/// Quando verdadeiro, o servidor espera antes de responder. É o coração da demonstração:
/// um stream lento na mesma conexão não deve atrasar os demais.
/// </param>
/// <param name="StreamLabel">Rótulo escolhido pelo cliente, só para exibição na UI.</param>
public sealed record QuicEchoRequest(string Message, bool Slow = false, string StreamLabel = "stream");

/// <param name="Echo">O texto recebido de volta.</param>
/// <param name="ServerStreamId">
/// Id do stream atribuído pelo QUIC. Ids crescem de 4 em 4 para streams bidirecionais
/// iniciados pelo cliente (0, 4, 8…): os 2 bits menos significativos codificam iniciador
/// e direcionalidade.
/// </param>
/// <param name="ServerDelayMs">Atraso que o servidor aplicou deliberadamente.</param>
public sealed record QuicEchoResponse(string Echo, long ServerStreamId, int ServerDelayMs);

/// <summary>Resultado por stream, como a UI exibe.</summary>
public sealed record QuicStreamResult(
    string Label,
    long StreamId,
    bool Slow,
    double ElapsedMs,
    string? Echo,
    string? Error);

/// <summary>Resultado agregado de uma rodada de streams paralelos numa única conexão.</summary>
public sealed record QuicRunResult(
    double HandshakeMs,
    double TotalMs,
    string NegotiatedAlpn,
    string RemoteCertificateSubject,
    IReadOnlyList<QuicStreamResult> Streams);
