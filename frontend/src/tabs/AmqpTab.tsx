import { useState } from 'react';
import { api } from '../api/client';
import { Metric, ResultBox, Toggle } from '../components/controls';
import { DocsPanel } from '../components/DocsPanel';
import { useAction } from '../components/useAction';
import { protocolDocs } from '../content/protocolDocs';

export function AmqpTab({ available }: { available: boolean }) {
  const [poison, setPoison] = useState(false);
  const [persistent, setPersistent] = useState(true);
  const [unroutable, setUnroutable] = useState(false);

  const publish = useAction(api.amqp.publish);
  const drain = useAction(api.amqp.drainDlq);

  return (
    <>
      {!available && (
        <div className="banner">
          <strong>Broker RabbitMQ fora do ar.</strong> Suba com{' '}
          <code>docker compose -f infra/docker-compose.yml up -d</code>. A UI de gestão fica em{' '}
          <code>http://localhost:15673</code> (guest/guest) — vale abrir para ver exchange, bindings e
          filas sendo criados.
        </div>
      )}

      <section className="panel">
        <h2>Publicar no exchange</h2>
        <p className="panel-hint">
          O publicador não escolhe a fila. Ele publica no exchange <code>lab.telemetry</code> (tipo{' '}
          <code>topic</code>) com a routing key <code>telemetry.&lt;device&gt;</code>. O exchange
          consulta seus bindings e decide o destino. Quem define o roteamento é{' '}
          <strong>o consumidor</strong>, ao declarar o binding <code>telemetry.#</code>.
        </p>

        <div className="controls">
          <Toggle
            label="Mensagem persistente"
            checked={persistent}
            onChange={setPersistent}
            hint="Persistente = o broker grava em disco antes de confirmar; sobrevive a um restart. Transiente = confirmado a partir da memória, mais rápido, perdido no restart. Fila durável com mensagem transiente dá falsa sensação de durabilidade."
          />

          <Toggle
            label="Mensagem venenosa (falha no consumidor)"
            checked={poison}
            onChange={setPoison}
            hint="O consumidor rejeita com requeue=false, e o broker move a mensagem para a dead-letter queue. Se rejeitasse com requeue=true, ela voltaria à fila, falharia de novo, e giraria num laço infinito consumindo 100% de CPU."
          />

          <Toggle
            label="Routing key sem binding"
            checked={unroutable}
            onChange={setUnroutable}
            hint="Publica em 'sem.binding', que nenhuma fila casa. Como usamos mandatory=true, o broker devolve a mensagem via basic.return em vez de descartá-la em silêncio. Sem mandatory, um binding errado só é descoberto quando alguém pergunta por que os dados sumiram."
          />

          <div className="actions">
            <button
              className="primary"
              disabled={publish.running || !available}
              onClick={() => void publish.run(poison, persistent, unroutable ? 'sem.binding' : undefined, undefined)}
            >
              {publish.running ? 'Publicando…' : 'Publicar'}
            </button>
            <button
              className="secondary"
              disabled={drain.running || !available}
              onClick={() => void drain.run()}
            >
              Drenar dead-letter queue
            </button>
          </div>
        </div>

        {(publish.error ?? drain.error) && (
          <ResultBox title="Falhou" error>
            {publish.error ?? drain.error}
          </ResultBox>
        )}

        {publish.result && (
          <ResultBox
            title="Publisher confirm"
            note={
              <>
                Com <em>publisher confirms</em> ligados, este <code>await</code> só completou quando o
                broker garantiu a gravação. Sem confirms, <code>BasicPublishAsync</code> retornaria
                assim que os bytes saíssem do socket — e uma queda do broker perderia a mensagem em
                silêncio.
              </>
            }
          >
            <div className="metrics">
              <Metric label="Routing key" value={<span style={{ fontSize: '0.85rem' }}>{publish.result.routingKey}</span>} />
              <Metric label="Confirmado" value={publish.result.confirmed ? 'sim' : 'não'} />
              <Metric label="Persistente" value={publish.result.persistent ? 'sim' : 'não'} />
              <Metric label="Latência" value={`${publish.result.elapsedMs.toFixed(1)} ms`} />
            </div>
          </ResultBox>
        )}

        {drain.result && (
          <ResultBox title="Dead-letter queue">
            {drain.result.drained === 0 ? (
              <>
                A DLQ está vazia. Publique uma mensagem venenosa e drene de novo: você verá a mensagem
                rejeitada aparecer aqui, fora do caminho quente e disponível para inspeção.
              </>
            ) : (
              <>
                <div className="metrics">
                  <Metric label="Mensagens recuperadas" value={drain.result.drained} />
                </div>
                <p className="result-note">
                  Em produção, este é o ponto de inspeção manual, correção e reprocessamento. Uma
                  mensagem que sempre falha sai da fila principal e para de bloquear as boas atrás dela.
                </p>
              </>
            )}
          </ResultBox>
        )}
      </section>

      <section className="panel">
        <h2>Topologia declarada pelo gateway</h2>
        <div className="table-scroll">
          <table>
            <thead>
              <tr>
                <th>Objeto</th>
                <th>Nome</th>
                <th>Papel</th>
              </tr>
            </thead>
            <tbody>
              <tr>
                <td>Exchange</td>
                <td className="mono">lab.telemetry</td>
                <td>Tipo topic. Recebe do publicador e decide o roteamento pelos bindings.</td>
              </tr>
              <tr>
                <td>Fila</td>
                <td className="mono">lab.telemetry.q</td>
                <td>
                  Durável, ligada por <code>telemetry.#</code>, com{' '}
                  <code>x-dead-letter-exchange</code> apontando para a DLX. Prefetch 20.
                </td>
              </tr>
              <tr>
                <td>Exchange (DLX)</td>
                <td className="mono">lab.telemetry.dlx</td>
                <td>Fanout. Recebe o que for rejeitado com requeue=false, expirado por TTL ou descartado por limite.</td>
              </tr>
              <tr>
                <td>Fila (DLQ)</td>
                <td className="mono">lab.telemetry.dlq</td>
                <td>Quarentena das mensagens venenosas.</td>
              </tr>
            </tbody>
          </table>
        </div>
        <p className="result-note">
          Declarações são idempotentes, mas falham com <code>PRECONDITION_FAILED</code> se os
          parâmetros divergirem de uma declaração anterior. Mudar <code>durable</code> numa fila que já
          existe exige apagá-la — por isso topologia é decisão de arquitetura, não de configuração.
        </p>
      </section>

      <DocsPanel doc={protocolDocs.Amqp} />
    </>
  );
}
