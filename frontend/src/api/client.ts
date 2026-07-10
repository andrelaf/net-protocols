import type {
  AmqpPublishResult,
  CoapObserveResult,
  CoapResponse,
  GatewayStatus,
  MqttPublishResult,
  ProtocolEvent,
  QuicRunResult,
  UdpBurstResult,
} from './types';

/**
 * Vazio por padrão: as chamadas vão para a origem do próprio Vite, que as encaminha ao
 * gateway pelo proxy de dev (ver `vite.config.ts`). Nenhum CORS envolvido.
 *
 * Definindo `VITE_API_BASE=http://localhost:5080`, o cliente passa a falar direto com o
 * gateway — origem diferente, logo CORS obrigatório. E como o SignalR envia credenciais no
 * handshake, o backend não pode usar `AllowAnyOrigin`: precisa listar as origens. É o
 * caminho que valerá em produção, e está configurado em `Program.cs`.
 */
export const API_BASE = import.meta.env.VITE_API_BASE ?? '';

/** O backend responde erros esperados (broker fora do ar) como RFC 7807 ProblemDetails. */
interface ProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
}

export class ApiError extends Error {
  readonly status: number;

  constructor(message: string, status: number) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
  }
}

async function request<T>(path: string, body?: unknown): Promise<T> {
  const response = await fetch(`${API_BASE}${path}`, {
    method: body === undefined ? 'GET' : 'POST',
    headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  });

  if (!response.ok) {
    let message = `${response.status} ${response.statusText}`;
    try {
      const problem = (await response.json()) as ProblemDetails;
      message = problem.detail ?? problem.title ?? message;
    } catch {
      // Resposta sem corpo JSON: fica a mensagem de status mesmo.
    }
    throw new ApiError(message, response.status);
  }

  return (await response.json()) as T;
}

export const api = {
  status: () => request<GatewayStatus>('/api/status'),
  recentEvents: () => request<ProtocolEvent[]>('/api/events/recent'),

  udp: {
    burst: (count: number, lossPercent: number, reorderPercent: number, deviceId?: string) =>
      request<UdpBurstResult>('/api/udp/burst', { count, lossPercent, reorderPercent, deviceId }),
    oversized: (sizeBytes: number) =>
      request<{ sizeBytes: number; detail: string }>('/api/udp/oversized', { sizeBytes }),
  },

  quic: {
    run: (streams: number, message: string, slowFirstStream: boolean) =>
      request<QuicRunResult>('/api/quic/run', { streams, message, slowFirstStream }),
  },

  mqtt: {
    publish: (qos: number, retain: boolean, deviceId?: string) =>
      request<MqttPublishResult>('/api/mqtt/publish', { qos, retain, deviceId }),
    clearRetained: (deviceId: string) =>
      request<{ deviceId: string; cleared: boolean }>('/api/mqtt/clear-retained', { deviceId }),
  },

  amqp: {
    publish: (poison: boolean, persistent: boolean, routingKey?: string, deviceId?: string) =>
      request<AmqpPublishResult>('/api/amqp/publish', { poison, persistent, routingKey, deviceId }),
    drainDlq: () => request<{ drained: number }>('/api/amqp/dlq/drain', {}),
  },

  coap: {
    get: (path: string, confirmable: boolean) =>
      request<CoapResponse>('/api/coap/get', { path, confirmable }),
    observe: (maxNotifications: number) =>
      request<CoapObserveResult>('/api/coap/observe', { maxNotifications }),
  },
};
