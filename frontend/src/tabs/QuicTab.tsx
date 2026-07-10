import { useState } from 'react';
import { api } from '../api/client';
import { Metric, ResultBox, Slider, Toggle } from '../components/controls';
import { DocsPanel } from '../components/DocsPanel';
import { useAction } from '../components/useAction';
import { protocolDocs } from '../content/protocolDocs';

export function QuicTab({ available }: { available: boolean }) {
  const [streams, setStreams] = useState(4);
  const [slowFirst, setSlowFirst] = useState(true);

  const run = useAction(api.quic.run);
  const result = run.result;

  const fastest = result?.streams.filter((s) => !s.slow).map((s) => s.elapsedMs) ?? [];
  const slowest = result?.streams.find((s) => s.slow)?.elapsedMs;

  return (
    <>
      {!available && (
        <div className="banner">
          <strong>QUIC indisponível neste host.</strong> O .NET precisa do msquic — embutido no
          runtime no Windows 11 e Server 2022+, mas no Linux exige o pacote <code>libmsquic</code>. A
          documentação abaixo continua válida; só não haverá tráfego real.
        </div>
      )}

      <section className="panel">
        <h2>Streams paralelos numa única conexão</h2>
        <p className="panel-hint">
          Abre uma conexão QUIC e dispara N streams ao mesmo tempo. Com a opção ligada, o primeiro
          stream demora 1,5 s de propósito. <strong>Compare a latência dele com a dos outros:</strong> os
          demais respondem em milissegundos, sem esperar. Em HTTP/2 sobre TCP, um pacote perdido nesse
          stream lento seguraria todos os outros — é o head-of-line blocking que o QUIC elimina.
        </p>

        <div className="controls">
          <Slider label="Streams paralelos" value={streams} min={1} max={10} onChange={setStreams} />
          <Toggle
            label="Tornar o primeiro stream lento"
            checked={slowFirst}
            onChange={setSlowFirst}
            hint="O servidor espera 1500 ms antes de responder só nesse stream."
          />

          <div className="actions">
            <button
              className="primary"
              disabled={run.running || !available}
              onClick={() => void run.run(streams, 'ola-quic', slowFirst)}
            >
              {run.running ? 'Executando…' : 'Abrir conexão e disparar streams'}
            </button>
          </div>
        </div>

        {run.error && (
          <ResultBox title="Falhou" error>
            {run.error}
          </ResultBox>
        )}

        {result && (
          <ResultBox
            title="Resultado"
            note={
              slowFirst && slowest && fastest.length > 0 ? (
                <>
                  O stream lento levou <strong>{slowest.toFixed(0)} ms</strong>; os demais, cerca de{' '}
                  <strong>{Math.max(...fastest).toFixed(0)} ms</strong>. Eles não esperaram. Abrir um
                  stream não custa round-trip algum — é só um id novo dentro de uma conexão que já existe.
                </>
              ) : (
                <>
                  Handshake de transporte e TLS 1.3 aconteceram juntos, num único round-trip. Um
                  TCP+TLS equivalente gastaria dois.
                </>
              )
            }
          >
            <div className="metrics">
              <Metric label="Handshake" value={`${result.handshakeMs.toFixed(1)} ms`} />
              <Metric label="Total" value={`${result.totalMs.toFixed(0)} ms`} />
              <Metric label="ALPN" value={result.negotiatedAlpn} />
            </div>

            <div className="table-scroll" style={{ marginTop: 16 }}>
              <table>
                <thead>
                  <tr>
                    <th>Stream</th>
                    <th>Id</th>
                    <th>Latência</th>
                    <th>Eco</th>
                  </tr>
                </thead>
                <tbody>
                  {result.streams.map((stream) => (
                    <tr key={stream.label}>
                      <td>{stream.label}</td>
                      <td className="mono">{stream.streamId}</td>
                      <td className="mono">{stream.elapsedMs.toFixed(0)} ms</td>
                      <td className="mono">{stream.error ?? stream.echo}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <p className="result-note">
              Os ids crescem de 4 em 4 (0, 4, 8…) porque os dois bits menos significativos codificam
              quem abriu o stream e se ele é bidirecional.
            </p>
          </ResultBox>
        )}
      </section>

      <DocsPanel doc={protocolDocs.Quic} />
    </>
  );
}
