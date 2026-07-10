import { useMemo } from 'react';
import type { ProtocolEvent, ProtocolKind } from '../api/types';
import type { ConnectionStatus } from '../api/useProtocolEvents';

const ARROWS: Record<ProtocolEvent['direction'], string> = {
  Outbound: '──▶',
  Inbound: '◀──',
  Internal: ' ⚙ ',
};

function formatTime(iso: string) {
  const date = new Date(iso);
  return (
    date.toLocaleTimeString('pt-BR', { hour12: false }) +
    '.' +
    date.getMilliseconds().toString().padStart(3, '0')
  );
}

function formatBytes(bytes: number) {
  return bytes < 1024 ? `${bytes} B` : `${(bytes / 1024).toFixed(1)} KB`;
}

interface Props {
  events: ProtocolEvent[];
  protocol: ProtocolKind;
  status: ConnectionStatus;
  onClear: () => void;
}

/**
 * Log ao vivo dos eventos do protocolo selecionado.
 *
 * Só renderiza os eventos da aba atual, e o hook que alimenta a lista já a limita a 400
 * entradas. Um painel de observabilidade que cresce sem limite acaba consumindo mais
 * recursos do que o sistema observado.
 */
export function EventLog({ events, protocol, status, onClear }: Props) {
  const filtered = useMemo(() => events.filter((e) => e.protocol === protocol), [events, protocol]);

  const statusClass =
    status === 'connected' ? 'ok' : status === 'disconnected' ? 'down' : 'warn';

  const statusLabel = {
    connected: 'ao vivo',
    connecting: 'conectando',
    reconnecting: 'reconectando',
    disconnected: 'offline',
  }[status];

  return (
    <aside className="log">
      <div className="log-head">
        <h3>Eventos</h3>
        <span className={`badge ${statusClass}`}>
          <span className="badge-dot" />
          {statusLabel}
        </span>
        <button className="secondary" style={{ padding: '4px 10px' }} onClick={onClear}>
          Limpar
        </button>
      </div>

      <div className="log-body">
        {filtered.length === 0 ? (
          <p className="log-empty">
            Nenhum evento ainda.
            <br />
            Execute uma ação ao lado.
          </p>
        ) : (
          filtered.map((event) => (
            <article key={event.id} className="event" data-direction={event.direction} data-level={event.level}>
              <div className="event-head">
                <span className="event-arrow">{ARROWS[event.direction]}</span>
                <span className="event-title">{event.title}</span>
                <span className="event-time">{formatTime(event.timestamp)}</span>
              </div>

              {event.detail && <p className="event-detail">{event.detail}</p>}

              {event.payload && <pre className="event-payload">{event.payload}</pre>}

              <div className="event-meta">
                {event.sizeBytes !== undefined && <span className="chip">{formatBytes(event.sizeBytes)}</span>}
                {event.durationMs !== undefined && (
                  <span className="chip">{event.durationMs.toFixed(1)} ms</span>
                )}
                {Object.entries(event.metadata ?? {}).map(([key, value]) => (
                  <span className="chip" key={key}>
                    {key}={value}
                  </span>
                ))}
              </div>
            </article>
          ))
        )}
      </div>
    </aside>
  );
}
