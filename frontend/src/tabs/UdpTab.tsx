import { useState } from 'react';
import { api } from '../api/client';
import { Metric, ResultBox, Slider } from '../components/controls';
import { DocsPanel } from '../components/DocsPanel';
import { useAction } from '../components/useAction';
import { protocolDocs } from '../content/protocolDocs';

const SAFE_MTU = 1472;
const MAX_DATAGRAM = 65507;

export function UdpTab() {
  const [count, setCount] = useState(12);
  const [loss, setLoss] = useState(25);
  const [reorder, setReorder] = useState(25);
  const [size, setSize] = useState(2000);

  const burst = useAction(api.udp.burst);
  const oversized = useAction(api.udp.oversized);

  return (
    <>
      <section className="panel">
        <h2>Rajada de datagramas</h2>
        <p className="panel-hint">
          O cliente envia leituras numeradas para o servidor UDP embutido no gateway. O servidor
          compara os números de sequência e deduz o que a rede fez com os pacotes. Aumente a perda e a
          reordenação e observe o log: <strong>o cliente não recebe erro algum</strong> pelos pacotes
          que somem.
        </p>

        <div className="controls">
          <Slider label="Datagramas" value={count} min={1} max={50} onChange={setCount} />
          <Slider
            label="Perda simulada"
            suffix="%"
            value={loss}
            min={0}
            max={80}
            onChange={setLoss}
            hint="O cliente descarta o datagrama antes de enviar. Numa rede real, quem descarta é um roteador congestionado — e ninguém é avisado."
          />
          <Slider
            label="Reordenação simulada"
            suffix="%"
            value={reorder}
            min={0}
            max={80}
            onChange={setReorder}
            hint="Segura um datagrama e o envia depois do próximo. Na internet isso acontece por roteamento multi-caminho (ECMP)."
          />

          <div className="actions">
            <button
              className="primary"
              disabled={burst.running}
              onClick={() => void burst.run(count, loss, reorder, undefined)}
            >
              {burst.running ? 'Enviando…' : 'Enviar rajada'}
            </button>
          </div>
        </div>

        {burst.error && (
          <ResultBox title="Falhou" error>
            {burst.error}
          </ResultBox>
        )}

        {burst.result && (
          <ResultBox
            title="Resultado da rajada"
            note={
              <>
                Repare que <code>sent</code> conta o que o kernel aceitou, não o que chegou. Só o
                servidor, olhando os números de sequência, sabe o que realmente foi entregue — e é
                por isso que numerar mensagens é obrigatório em UDP.
              </>
            }
          >
            <div className="metrics">
              <Metric label="Solicitados" value={burst.result.requested} />
              <Metric label="Enviados" value={burst.result.sent} />
              <Metric label="Descartados" value={burst.result.droppedBySimulation} />
              <Metric label="Reordenados" value={burst.result.reordered} />
              <Metric label="Duração" value={`${burst.result.elapsedMs.toFixed(0)} ms`} />
            </div>
          </ResultBox>
        )}
      </section>

      <section className="panel">
        <h2>Limites de tamanho</h2>
        <p className="panel-hint">
          Dois limites concretos: acima de <code>{SAFE_MTU}</code> bytes o IP fragmenta o datagrama, e
          perder <em>um</em> fragmento descarta o datagrama inteiro. Acima de{' '}
          <code>{MAX_DATAGRAM.toLocaleString('pt-BR')}</code> bytes o socket recusa o envio.
        </p>

        <div className="controls">
          <Slider
            label="Tamanho do payload"
            suffix=" bytes"
            value={size}
            min={64}
            max={70000}
            step={64}
            onChange={setSize}
            hint={
              size > MAX_DATAGRAM
                ? 'Acima do máximo absoluto: o socket vai lançar SocketError.MessageSize.'
                : size > SAFE_MTU
                  ? 'Acima do MTU útil: o IP vai fragmentar em múltiplos pacotes.'
                  : 'Cabe num único pacote IP, sem fragmentação.'
            }
          />

          <div className="actions">
            <button className="primary" disabled={oversized.running} onClick={() => void oversized.run(size)}>
              {oversized.running ? 'Enviando…' : 'Enviar datagrama'}
            </button>
            <button className="secondary" onClick={() => setSize(SAFE_MTU)}>
              Ir ao limite sem fragmentação
            </button>
            <button className="secondary" onClick={() => setSize(70000)}>
              Estourar o máximo
            </button>
          </div>
        </div>

        {oversized.error && (
          <ResultBox title="Falhou" error>
            {oversized.error}
          </ResultBox>
        )}

        {oversized.result && <ResultBox title="O que aconteceu">{oversized.result.detail}</ResultBox>}
      </section>

      <DocsPanel doc={protocolDocs.Udp} />
    </>
  );
}
