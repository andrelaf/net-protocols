namespace ProtocolLab.Gateway.Endpoints;

/// <param name="Count">Quantos datagramas enviar (1–200).</param>
/// <param name="LossPercent">Probabilidade de descartar cada datagrama antes do envio.</param>
/// <param name="ReorderPercent">Probabilidade de atrasar um datagrama, trocando a ordem.</param>
public sealed record UdpBurstRequest(int Count = 10, string? DeviceId = null, int? LossPercent = null, int? ReorderPercent = null);

/// <param name="SizeBytes">Tamanho do datagrama. 1472 = limite sem fragmentação; 65507 = máximo absoluto.</param>
public sealed record UdpOversizedRequest(int SizeBytes = 2000);

/// <param name="Streams">Quantos streams paralelos abrir na mesma conexão (1–10).</param>
/// <param name="SlowFirstStream">Marca o primeiro stream como lento, para evidenciar a ausência de head-of-line blocking.</param>
public sealed record QuicRunRequest(int Streams = 3, string Message = "ola-quic", bool SlowFirstStream = true);

/// <param name="Qos">0 = at-most-once, 1 = at-least-once, 2 = exactly-once.</param>
/// <param name="Retain">O broker guarda a mensagem e a entrega a novos assinantes.</param>
public sealed record MqttPublishRequest(int Qos = 1, bool Retain = false, string? DeviceId = null);

public sealed record MqttClearRetainedRequest(string DeviceId);

/// <param name="Poison">Faz o consumidor rejeitar a mensagem, exercitando a dead-letter queue.</param>
/// <param name="Persistent">Grava em disco antes de confirmar.</param>
/// <param name="RoutingKey">Sobrescreve a routing key. Use algo como "sem.binding" para ver o basic.return.</param>
public sealed record AmqpPublishRequest(bool Poison = false, bool Persistent = true, string? DeviceId = null, string? RoutingKey = null);

/// <param name="Path">Recurso: "telemetry", "time" ou ".well-known/core".</param>
/// <param name="Confirmable">CON retransmite até receber ACK; NON dispara e esquece.</param>
public sealed record CoapGetRequest(string Path = "telemetry", bool Confirmable = true);

public sealed record CoapObserveRequest(int MaxNotifications = 4);
