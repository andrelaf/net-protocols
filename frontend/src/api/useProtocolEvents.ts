import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import type { HubConnection } from '@microsoft/signalr';
import { useEffect, useRef, useState } from 'react';
import { API_BASE, api } from './client';
import type { ProtocolEvent } from './types';

/** Quantos eventos manter em memória. Além disso, a lista vira um vazamento de memória lento. */
const MAX_EVENTS = 400;

export type ConnectionStatus = 'connecting' | 'connected' | 'reconnecting' | 'disconnected';

/**
 * Assina o stream de eventos do gateway.
 *
 * O navegador não fala UDP, QUIC bruto, MQTT, AMQP nem CoAP — ele fala HTTP e WebSocket.
 * Tudo que você vê nas abas passou por um processo .NET que fala esses protocolos por você.
 * Essa ponte não é um detalhe de implementação do laboratório: é a arquitetura real de
 * qualquer painel que mostre tráfego de IoT ou de mensageria.
 */
export function useProtocolEvents() {
  const [events, setEvents] = useState<ProtocolEvent[]>([]);
  const [status, setStatus] = useState<ConnectionStatus>('connecting');
  const connectionRef = useRef<HubConnection | null>(null);

  useEffect(() => {
    let disposed = false;

    const connection = new HubConnectionBuilder()
      .withUrl(`${API_BASE}/hub/events`)
      // Backoff exponencial com teto. Sem isso, um gateway reiniciando levaria uma
      // enxurrada de tentativas de reconexão de cada aba aberta.
      .withAutomaticReconnect([0, 2000, 5000, 10000, 20000])
      .configureLogging(LogLevel.Warning)
      .build();

    connectionRef.current = connection;

    connection.on('ProtocolEvent', (event: ProtocolEvent) => {
      setEvents((current) => [event, ...current].slice(0, MAX_EVENTS));
    });

    connection.onreconnecting(() => setStatus('reconnecting'));
    connection.onreconnected(() => setStatus('connected'));
    connection.onclose(() => setStatus('disconnected'));

    const started = connection
      .start()
      .then(async () => {
        if (disposed) return;
        setStatus('connected');

        // Preenche a tela com o histórico recente: uma aba aberta agora não deveria
        // encarar uma lista vazia só porque chegou depois.
        try {
          const recent = await api.recentEvents();
          setEvents((live) => {
            const known = new Set(live.map((e) => e.id));
            const backfill = recent.filter((e) => !known.has(e.id));
            return [...live, ...backfill]
              .sort((a, b) => b.timestamp.localeCompare(a.timestamp))
              .slice(0, MAX_EVENTS);
          });
        } catch {
          // Backfill é conveniência, não requisito.
        }
      })
      .catch(() => {
        if (!disposed) setStatus('disconnected');
      });

    return () => {
      disposed = true;

      // Encadeamos o stop() na promise do start(). Chamar stop() enquanto a negociação
      // está em curso aborta o handshake e lança "The connection was stopped during
      // negotiation" — o que acontece em toda montagem sob StrictMode do React, que monta,
      // desmonta e remonta o componente em desenvolvimento.
      void started.finally(() => {
        if (connection.state !== HubConnectionState.Disconnected) {
          void connection.stop();
        }
      });
    };
  }, []);

  return { events, status, clear: () => setEvents([]) };
}
