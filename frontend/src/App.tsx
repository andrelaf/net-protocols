import { useEffect, useState } from 'react';
import { api } from './api/client';
import type { GatewayStatus, ProtocolKind } from './api/types';
import { useProtocolEvents } from './api/useProtocolEvents';
import { EventLog } from './components/EventLog';
import { AmqpTab } from './tabs/AmqpTab';
import { CoapTab } from './tabs/CoapTab';
import { MqttTab } from './tabs/MqttTab';
import { OverviewTab } from './tabs/OverviewTab';
import { QuicTab } from './tabs/QuicTab';
import { UdpTab } from './tabs/UdpTab';

type TabKey = 'overview' | Exclude<ProtocolKind, 'Gateway'>;

const TABS: { key: TabKey; label: string; accent?: string }[] = [
  { key: 'overview', label: 'Visão geral' },
  { key: 'Udp', label: 'UDP', accent: 'var(--udp)' },
  { key: 'Quic', label: 'QUIC', accent: 'var(--quic)' },
  { key: 'Mqtt', label: 'MQTT', accent: 'var(--mqtt)' },
  { key: 'Amqp', label: 'AMQP', accent: 'var(--amqp)' },
  { key: 'Coap', label: 'CoAP', accent: 'var(--coap)' },
];

/** Revalida a disponibilidade dos brokers: eles podem subir depois do frontend. */
const STATUS_POLL_MS = 5000;

export default function App() {
  const [tab, setTab] = useState<TabKey>('overview');
  const [status, setStatus] = useState<GatewayStatus | null>(null);
  const [gatewayDown, setGatewayDown] = useState(false);

  const { events, status: connection, clear } = useProtocolEvents();

  useEffect(() => {
    let cancelled = false;

    const poll = async () => {
      try {
        const next = await api.status();
        if (!cancelled) {
          setStatus(next);
          setGatewayDown(false);
        }
      } catch {
        if (!cancelled) setGatewayDown(true);
      }
    };

    void poll();
    const timer = setInterval(() => void poll(), STATUS_POLL_MS);
    return () => {
      cancelled = true;
      clearInterval(timer);
    };
  }, []);

  const availability = (protocol: string) =>
    status?.protocols.find((p) => p.protocol === protocol)?.available ?? false;

  return (
    <div className="app">
      <header className="masthead">
        <h1>Protocol Lab</h1>
        <p>
          Cinco protocolos de comunicação em execução, lado a lado, carregando a mesma leitura de
          sensor. Cada aba executa tráfego real contra um servidor de verdade, e explica os cenários de
          uso, as boas práticas e os antipatterns de cada um.
        </p>
        <div className="masthead-meta">
          {TABS.filter((t) => t.key !== 'overview').map((t) => {
            const ok = availability(t.key);
            return (
              <span key={t.key} className={`badge ${ok ? 'ok' : 'down'}`}>
                <span className="badge-dot" />
                {t.label}
              </span>
            );
          })}
        </div>
      </header>

      {gatewayDown && (
        <div className="banner" style={{ marginTop: 20 }}>
          <strong>Gateway inacessível.</strong> Rode{' '}
          <code>dotnet run --project backend/Gateway</code> na raiz do repositório. Sem ele, nenhuma aba
          consegue gerar tráfego.
        </div>
      )}

      <nav className="tabs" role="tablist">
        {TABS.map((t) => (
          <button
            key={t.key}
            role="tab"
            className="tab"
            aria-selected={tab === t.key}
            data-available={t.key === 'overview' || availability(t.key)}
            style={{ '--tab-accent': t.accent } as React.CSSProperties}
            onClick={() => setTab(t.key)}
          >
            {t.key !== 'overview' && <span className="tab-dot" />}
            {t.label}
          </button>
        ))}
      </nav>

      {tab === 'overview' ? (
        <OverviewTab status={status} />
      ) : (
        <div className="split">
          <main>
            {tab === 'Udp' && <UdpTab />}
            {tab === 'Quic' && <QuicTab available={availability('Quic')} />}
            {tab === 'Mqtt' && <MqttTab available={availability('Mqtt')} />}
            {tab === 'Amqp' && <AmqpTab available={availability('Amqp')} />}
            {tab === 'Coap' && <CoapTab />}
          </main>

          <EventLog events={events} protocol={tab} status={connection} onClear={clear} />
        </div>
      )}
    </div>
  );
}
