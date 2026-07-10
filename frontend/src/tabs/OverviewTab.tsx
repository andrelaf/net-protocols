import type { GatewayStatus } from '../api/types';
import { comparisonRows, protocolDocs } from '../content/protocolDocs';

const ORDER = ['Udp', 'Quic', 'Mqtt', 'Amqp', 'Coap'] as const;

export function OverviewTab({ status }: { status: GatewayStatus | null }) {
  return (
    <>
      <section className="panel">
        <h2>Por que existe um gateway .NET no meio</h2>
        <p className="essence" style={{ marginTop: 8 }}>
          Um navegador não fala UDP, QUIC bruto, MQTT sobre TCP, AMQP nem CoAP. Ele fala HTTP e
          WebSocket — e só. Qualquer painel que afirme "mostrar MQTT" está, na verdade, conversando com
          um processo servidor que fala MQTT por ele. Tornar essa ponte explícita é metade da lição de
          arquitetura aqui: <strong>a escolha do protocolo acontece entre serviços, não entre o
          navegador e o mundo</strong>.
        </p>
        <p className="essence">
          Neste laboratório, o gateway ASP.NET Core hospeda os servidores UDP, QUIC e CoAP em processo,
          e age como cliente do Mosquitto e do RabbitMQ. Cada operação de protocolo emite um evento num
          envelope comum, que chega ao seu navegador por SignalR. As bibliotecas de protocolo não sabem
          que o SignalR existe: elas conhecem apenas a interface <code>IProtocolEventSink</code>.
        </p>

        <div className="callout" style={{ marginTop: 20 }}>
          <strong>A exceção que vale conhecer.</strong> MQTT sobre WebSocket permite que o navegador
          seja um cliente MQTT de primeira classe, sem gateway. O <code>mosquitto.conf</code> deste
          projeto habilita essa porta (9883 no host) justamente para você poder comparar as duas
          abordagens.
        </div>
      </section>

      <section className="panel">
        <h2>Comparação lado a lado</h2>
        <p className="panel-hint">
          Os cinco protocolos carregam a mesma carga de domínio — uma leitura de sensor com número de
          sequência. Usar o mesmo payload em todos é o que torna a comparação honesta.
        </p>

        <div className="table-scroll">
          <table>
            <thead>
              <tr>
                <th />
                {ORDER.map((key) => (
                  <th key={key}>{protocolDocs[key].name}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {comparisonRows.map((row) => (
                <tr key={row.label}>
                  <th scope="row">{row.label}</th>
                  {ORDER.map((key) => (
                    <td key={key}>{row.get(protocolDocs[key])}</td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <section className="panel">
        <h2>Como escolher</h2>
        <ul className="doc-list">
          <li className="doc-item use">
            <strong>Preciso da menor latência possível e posso perder mensagens</strong>
            <span>
              UDP. Mídia em tempo real, telemetria densa, descoberta de serviços. Se você começar a
              adicionar ACKs e retransmissão por cima, pare: você está reescrevendo TCP, pior.
            </span>
          </li>
          <li className="doc-item use">
            <strong>Preciso de confiabilidade e concorrência, na borda, com perda e mobilidade</strong>
            <span>
              QUIC. Streams independentes eliminam head-of-line blocking, o handshake é um round-trip, e
              a conexão sobrevive à troca de rede. No data center, HTTP/2 já resolve.
            </span>
          </li>
          <li className="doc-item use">
            <strong>Muitos dispositivos publicando, consumidores desconhecidos, rede intermitente</strong>
            <span>
              MQTT. Pub/sub com QoS por mensagem, sessão persistente e Last Will. Mas não é fila de
              trabalho: não há ack por consumidor nem dead-letter.
            </span>
          </li>
          <li className="doc-item use">
            <strong>Distribuir trabalho entre workers, com retentativa e quarentena</strong>
            <span>
              AMQP. Ack manual, prefetch, publisher confirms e dead-letter queue. O consumidor decide o
              roteamento pelo binding, sem tocar no publicador.
            </span>
          </li>
          <li className="doc-item use">
            <strong>Dispositivo restrito, a bateria, onde cada byte custa energia</strong>
            <span>
              CoAP. REST sobre UDP com cabeçalho de 4 bytes, e Observe para eventos sem broker. Fora do
              mundo restrito, HTTP/2 tem ferramental muito melhor.
            </span>
          </li>
        </ul>
      </section>

      {status && (
        <section className="panel">
          <h2>Estado do laboratório</h2>
          <div className="table-scroll">
            <table>
              <thead>
                <tr>
                  <th>Protocolo</th>
                  <th>Disponível</th>
                  <th>Depende de</th>
                </tr>
              </thead>
              <tbody>
                {status.protocols.map((protocol) => (
                  <tr key={protocol.protocol}>
                    <td>{protocol.protocol}</td>
                    <td>
                      <span className={`badge ${protocol.available ? 'ok' : 'down'}`}>
                        <span className="badge-dot" />
                        {protocol.available ? 'sim' : 'não'}
                      </span>
                    </td>
                    <td>
                      {protocol.requires}
                      {protocol.detail && (
                        <>
                          <br />
                          <span style={{ color: 'var(--text-faint)', fontSize: '0.8rem' }}>{protocol.detail}</span>
                        </>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <p className="result-note">
            Runtime .NET {status.runtime}. Eventos descartados por contrapressão da UI:{' '}
            <strong>{status.droppedEvents}</strong> — o canal do gateway prefere descartar telemetria de
            diagnóstico a degradar o sistema observado.
          </p>
        </section>
      )}
    </>
  );
}
