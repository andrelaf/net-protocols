import { useState } from 'react';
import { api } from '../api/client';
import { Metric, ResultBox, Segmented, Slider, Toggle } from '../components/controls';
import { DocsPanel } from '../components/DocsPanel';
import { useAction } from '../components/useAction';
import { protocolDocs } from '../content/protocolDocs';

const RESOURCES = [
  { value: 'telemetry', label: '/telemetry' },
  { value: 'time', label: '/time' },
  { value: '.well-known/core', label: '/.well-known/core' },
  { value: 'nao-existe', label: '/nao-existe' },
];

export function CoapTab() {
  const [path, setPath] = useState('telemetry');
  const [confirmable, setConfirmable] = useState(true);
  const [notifications, setNotifications] = useState(4);

  const get = useAction(api.coap.get);
  const observe = useAction(api.coap.observe);

  return (
    <>
      <section className="panel">
        <h2>Requisição a um recurso</h2>
        <p className="panel-hint">
          CoAP é o modelo mental do HTTP — métodos, códigos, URIs — comprimido num cabeçalho binário de
          4 bytes, sobre UDP. Observe o tamanho da requisição no resultado:{' '}
          <strong>cerca de 18 bytes</strong>. Um <code>GET /telemetry HTTP/1.1</code> com header{' '}
          <code>Host</code> já passa de 40 bytes só na primeira linha.
        </p>

        <div className="controls">
          <Segmented label="Recurso" value={path} onChange={setPath} options={RESOURCES} />

          <Toggle
            label="Confirmable (CON)"
            checked={confirmable}
            onChange={setConfirmable}
            hint={
              confirmable
                ? 'CON exige ACK. Se não vier, o cliente retransmite após ACK_TIMEOUT (2 s), dobrando o intervalo a cada tentativa, até 4 retransmissões. É assim que o CoAP compra de volta a confiabilidade que o UDP não oferece.'
                : 'NON dispara e esquece. Se a resposta se perder, ninguém retransmite. Adequado para telemetria de alta frequência, onde a próxima amostra chega em segundos.'
            }
          />

          <div className="actions">
            <button className="primary" disabled={get.running} onClick={() => void get.run(path, confirmable)}>
              {get.running ? 'Requisitando…' : `GET /${path}`}
            </button>
          </div>
        </div>

        {get.error && (
          <ResultBox title="Falhou" error>
            {get.error}
          </ResultBox>
        )}

        {get.result && (
          <ResultBox
            title={`Resposta ${get.result.code}`}
            note={
              get.result.transmissions > 1 ? (
                <>
                  Foram necessárias <strong>{get.result.transmissions} transmissões</strong>. O Message
                  ID é o mesmo em todas, então o servidor reconhece a duplicata. Isso é retransmissão de
                  CON funcionando.
                </>
              ) : get.result.piggybacked ? (
                <>
                  Resposta <em>piggybacked</em>: o ACK carregou o corpo. Dois pacotes no total — o custo
                  mínimo de um request/response confiável sobre UDP.
                </>
              ) : (
                <>Resposta não-confirmável: não há ACK a carregar.</>
              )
            }
          >
            <div className="metrics">
              <Metric label="Código" value={get.result.code} />
              <Metric label="Requisição" value={`${get.result.requestBytes} B`} />
              <Metric label="Resposta" value={`${get.result.responseBytes} B`} />
              <Metric label="Transmissões" value={get.result.transmissions} />
              <Metric label="Latência" value={`${get.result.elapsedMs.toFixed(0)} ms`} />
            </div>
            {get.result.payload && <pre className="event-payload">{get.result.payload}</pre>}
          </ResultBox>
        )}
      </section>

      <section className="panel">
        <h2>Observe: pub/sub sem broker</h2>
        <p className="panel-hint">
          O cliente faz um <code>GET /telemetry</code> com a opção <code>Observe</code>. O servidor
          guarda o token e passa a empurrar notificações sempre que a representação mudar. É a resposta
          do CoAP ao MQTT — e a diferença é que <strong>não há broker para provisionar, escalar e pagar</strong>.
        </p>

        <div className="controls">
          <Slider
            label="Notificações a receber"
            value={notifications}
            min={1}
            max={6}
            onChange={setNotifications}
            hint="A primeira vem piggybacked no ACK do GET. As seguintes chegam como NON, sem confirmação: perder uma leitura de sensor não vale o custo de retransmitir. Ao final, o cliente envia RST para cancelar o registro."
          />

          <div className="actions">
            <button className="primary" disabled={observe.running} onClick={() => void observe.run(notifications)}>
              {observe.running ? 'Observando…' : 'Observar /telemetry'}
            </button>
          </div>
        </div>

        {observe.error && (
          <ResultBox title="Falhou" error>
            {observe.error}
          </ResultBox>
        )}

        {observe.result && (
          <ResultBox
            title="Observação encerrada"
            note={
              <>
                O número de sequência do <code>Observe</code> cresce monotonicamente. Ele existe porque
                UDP reordena: sem ele, o cliente poderia aplicar uma leitura antiga por cima de uma
                recente. Veja os valores no log ao lado.
              </>
            }
          >
            <div className="metrics">
              <Metric label="Recurso" value={observe.result.resource} />
              <Metric label="Notificações" value={observe.result.notifications} />
              <Metric label="Duração" value={`${observe.result.elapsedMs.toFixed(0)} ms`} />
            </div>
            <pre className="event-payload" style={{ marginTop: 12 }}>
              {observe.result.payloads.join('\n')}
            </pre>
          </ResultBox>
        )}
      </section>

      <section className="panel">
        <h2>O pacote no fio</h2>
        <p className="panel-hint">
          O servidor e o cliente CoAP deste laboratório foram escritos do zero sobre{' '}
          <code>UdpClient</code>, sem biblioteca — o ecossistema .NET para CoAP está estagnado, e num
          repositório didático expor o formato binário é justamente o conteúdo que interessa. Este é um{' '}
          <code>GET /telemetry</code> com a opção Observe, codificado:
        </p>
        <pre className="event-payload">
{`44 01 1234 AABBCCDD 60 59 74656C656D65747279
│  │  │    │        │  │  └─ "telemetry"
│  │  │    │        │  └──── delta 5 (→ opção 11 Uri-Path), len 9
│  │  │    │        └─────── delta 6 (→ opção 6 Observe), len 0
│  │  │    └──────────────── token (4 bytes)
│  │  └───────────────────── message id
│  └──────────────────────── código 0.01 GET
└─────────────────────────── ver=1, type=CON, TKL=4`}
        </pre>
        <div className="callout">
          <strong>Message ID e Token não são a mesma coisa.</strong> O Message ID casa um ACK com o CON
          que o originou — é a camada de <em>mensagens</em>. O Token casa uma resposta com a requisição
          que a pediu — é a camada de <em>requisição/resposta</em>. Numa resposta separada, quando o
          servidor demora e responde depois num CON novo, os dois divergem. Quem correlaciona pelo
          Message ID quebra.
        </div>
      </section>

      <DocsPanel doc={protocolDocs.Coap} />
    </>
  );
}
