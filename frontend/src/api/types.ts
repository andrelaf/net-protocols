/**
 * Espelho dos contratos de `backend/Shared/Contracts`.
 *
 * Mantidos à mão de propósito: gerar clientes a partir do OpenAPI seria o certo num
 * produto, mas aqui a duplicação é pequena e explícita, e o leitor consegue comparar
 * lado a lado com o C# sem abrir um gerador.
 */

export type ProtocolKind = 'Udp' | 'Quic' | 'Mqtt' | 'Amqp' | 'Coap' | 'Gateway';

export type EventDirection = 'Outbound' | 'Inbound' | 'Internal';

export type EventLevel = 'Debug' | 'Info' | 'Warning' | 'Error';

export interface ProtocolEvent {
  id: string;
  protocol: ProtocolKind;
  direction: EventDirection;
  title: string;
  detail?: string;
  payload?: string;
  sizeBytes?: number;
  durationMs?: number;
  metadata?: Record<string, string>;
  level: EventLevel;
  timestamp: string;
}

export interface ProtocolAvailability {
  protocol: string;
  available: boolean;
  requires: string;
  detail?: string;
}

export interface GatewayStatus {
  protocols: ProtocolAvailability[];
  droppedEvents: number;
  runtime: string;
}

export interface UdpBurstResult {
  requested: number;
  sent: number;
  droppedBySimulation: number;
  reordered: number;
  elapsedMs: number;
}

export interface QuicStreamResult {
  label: string;
  streamId: number;
  slow: boolean;
  elapsedMs: number;
  echo?: string;
  error?: string;
}

export interface QuicRunResult {
  handshakeMs: number;
  totalMs: number;
  negotiatedAlpn: string;
  remoteCertificateSubject: string;
  streams: QuicStreamResult[];
}

export interface MqttPublishResult {
  topic: string;
  qoS: number;
  retained: boolean;
  packetIdentifier: number;
  reasonCode: string;
  elapsedMs: number;
}

export interface AmqpPublishResult {
  routingKey: string;
  confirmed: boolean;
  routed: boolean;
  persistent: boolean;
  elapsedMs: number;
  error?: string;
}

export interface CoapResponse {
  code: string;
  payload: string;
  elapsedMs: number;
  transmissions: number;
  piggybacked: boolean;
  requestBytes: number;
  responseBytes: number;
}

export interface CoapObserveResult {
  resource: string;
  notifications: number;
  elapsedMs: number;
  payloads: string[];
}
