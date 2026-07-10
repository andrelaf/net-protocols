import { useState } from 'react';
import { api } from '../api/client';
import { Metric, ResultBox, Segmented, Toggle } from '../components/controls';
import { DocsPanel } from '../components/DocsPanel';
import { useAction } from '../components/useAction';
import { protocolDocs } from '../content/protocolDocs';

const DEVICES = ['sensor-01', 'sensor-02', 'sensor-03'];

const QOS_EXPLANATION: Record<number, string> = {
  0: 'At-most-once. Nenhum handshake: o publish retorna assim que os bytes saem do socket. O broker pode nunca ter recebido.',
  1: 'At-least-once. Esperamos o PUBACK do broker — um round-trip. A mensagem chega pelo menos uma vez, e pode chegar duplicada.',
  2: 'Exactly-once. Handshake de quatro etapas: PUBLISH → PUBREC → PUBREL → PUBCOMP. Dois round-trips. Caro.',
};

export function MqttTab({ available }: { available: boolean }) {
  const [qos, setQos] = useState(1);
  const [retain, setRetain] = useState(false);
  const [device, setDevice] = useState(DEVICES[0]);

  const publish = useAction(api.mqtt.publish);
  const clear = useAction(api.mqtt.clearRetained);

  return (
    <>
      {!available && (
        <div className="banner">
          <strong>Broker MQTT fora do ar.</strong> Suba o Mosquitto com{' '}
          <code>docker compose -f infra/docker-compose.yml up -d</code>. O gateway reconecta sozinho em
          poucos segundos.
        </div>
      )}

      <section className="panel">
        <h2>Publicar telemetria</h2>
        <p className="panel-hint">
          O gateway está assinado em <code>lab/telemetry/#</code>, então cada publicação volta para ele
          como um delivery — você verá o par publish/receive no log. Publique nos três níveis de QoS e{' '}
          <strong>compare a latência</strong>: ela mede o custo real de cada garantia.
        </p>

        <div className="controls">
          <Segmented
            label="Qualidade de serviço"
            value={qos}
            onChange={setQos}
            options={[
              { value: 0, label: 'QoS 0' },
              { value: 1, label: 'QoS 1' },
              { value: 2, label: 'QoS 2' },
            ]}
            hint={QOS_EXPLANATION[qos]}
          />

          <Segmented
            label="Dispositivo"
            value={device}
            onChange={setDevice}
            options={DEVICES.map((d) => ({ value: d, label: d }))}
            hint={`Publica em lab/telemetry/${device}. O id do dispositivo é um segmento do tópico, então um assinante pode filtrar por dispositivo sem que o publicador saiba disso.`}
          />

          <Toggle
            label="Mensagem retida"
            checked={retain}
            onChange={setRetain}
            hint="O broker guarda esta mensagem e a entrega imediatamente a qualquer novo assinante do tópico. Retained não expira: a única forma de apagar é publicar payload vazio com retain ligado."
          />

          <div className="actions">
            <button
              className="primary"
              disabled={publish.running || !available}
              onClick={() => void publish.run(qos, retain, device)}
            >
              {publish.running ? 'Publicando…' : 'Publicar'}
            </button>
            <button
              className="secondary"
              disabled={clear.running || !available}
              onClick={() => void clear.run(device)}
            >
              Limpar retained de {device}
            </button>
          </div>
        </div>

        {(publish.error ?? clear.error) && (
          <ResultBox title="Falhou" error>
            {publish.error ?? clear.error}
          </ResultBox>
        )}

        {publish.result && (
          <ResultBox
            title="PUBLISH confirmado"
            note={
              publish.result.qoS === 0 ? (
                <>
                  Em QoS 0 o <code>packetId</code> é zero: não há pacote para confirmar, porque não há
                  confirmação. O tempo medido é só o custo de escrever no socket.
                </>
              ) : (
                <>
                  O <code>packetId</code> correlaciona o PUBLISH com o {publish.result.qoS === 1 ? 'PUBACK' : 'PUBCOMP'}. É
                  esse ida-e-volta que a latência acima está medindo.
                </>
              )
            }
          >
            <div className="metrics">
              <Metric label="Tópico" value={<span style={{ fontSize: '0.85rem' }}>{publish.result.topic}</span>} />
              <Metric label="QoS" value={publish.result.qoS} />
              <Metric label="Packet id" value={publish.result.packetIdentifier} />
              <Metric label="Reason" value={publish.result.reasonCode} />
              <Metric label="Latência" value={`${publish.result.elapsedMs.toFixed(1)} ms`} />
            </div>
          </ResultBox>
        )}

        {clear.result && (
          <ResultBox title="Retained limpo">
            Publicamos payload vazio com retain=true em <code>lab/telemetry/{clear.result.deviceId}</code>.
            Não existe DELETE em MQTT — sem isso, todo novo assinante continuaria recebendo o valor
            antigo, indefinidamente.
          </ResultBox>
        )}
      </section>

      <section className="panel">
        <h2>Last Will and Testament</h2>
        <p className="panel-hint">
          No CONNECT, o gateway registrou uma mensagem <code>offline</code> retida no tópico{' '}
          <code>lab/status/gateway</code>. Se o processo morrer sem um DISCONNECT limpo — mate-o com{' '}
          <kbd>Ctrl+C</kbd> ou derrube a rede — o <em>broker</em> publica esse testamento por ele. Ao
          conectar, o gateway publica <code>online</code> no mesmo tópico.
        </p>
        <div className="callout">
          <strong>Por que isso não existe em HTTP.</strong> Um cliente HTTP que morre simplesmente para
          de fazer requisições, e ninguém é notificado — o servidor só descobre por timeout, e não tem
          a quem contar. O LWT é o broker agindo como testemunha da morte do dispositivo. É a razão
          pela qual painéis de IoT conseguem mostrar "sensor offline" em segundos.
        </div>
      </section>

      <DocsPanel doc={protocolDocs.Mqtt} />
    </>
  );
}
