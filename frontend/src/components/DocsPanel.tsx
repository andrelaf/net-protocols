import type { DocEntry, ProtocolDoc } from '../content/protocolDocs';

function DocList({ entries, variant }: { entries: DocEntry[]; variant: 'use' | 'good' | 'bad' }) {
  return (
    <ul className="doc-list">
      {entries.map((entry) => (
        <li key={entry.title} className={`doc-item ${variant}`}>
          <strong>{entry.title}</strong>
          <span>{entry.body}</span>
        </li>
      ))}
    </ul>
  );
}

/**
 * Painel de documentação de um protocolo: essência, ficha técnica, cenários de uso,
 * boas práticas, antipatterns e quando não usar.
 *
 * O conteúdo vive em `content/protocolDocs.ts`, separado dos componentes, para que ele
 * possa ser lido, revisado e corrigido como texto — que é o que ele é.
 */
export function DocsPanel({ doc }: { doc: ProtocolDoc }) {
  return (
    <>
      <section className="panel">
        <div className="protocol-header">
          <h2>{doc.name}</h2>
          <span className="tagline">{doc.tagline}</span>
        </div>

        <p className="essence">{doc.essence}</p>

        <div className="spec-grid">
          <Spec label="Padrão" value={doc.rfc} />
          <Spec label="Transporte" value={doc.transport} />
          <Spec label="Topologia" value={doc.topology} />
          <Spec label="Entrega" value={doc.delivery} />
          <Spec label="Ordenação" value={doc.ordering} />
          <Spec label="Overhead" value={doc.overhead} />
        </div>
      </section>

      <section className="panel">
        <div className="doc-section" style={{ marginTop: 0 }}>
          <h3>Cenários de uso</h3>
          <DocList entries={doc.useCases} variant="use" />
        </div>

        <div className="doc-section">
          <h3>Boas práticas</h3>
          <DocList entries={doc.bestPractices} variant="good" />
        </div>

        <div className="doc-section">
          <h3>Antipatterns</h3>
          <DocList entries={doc.antipatterns} variant="bad" />
        </div>

        <div className="doc-section">
          <h3>Quando não usar</h3>
          <div className="callout">
            <strong>Escolha outro protocolo quando… </strong>
            {doc.avoidWhen}
          </div>
        </div>
      </section>
    </>
  );
}

function Spec({ label, value }: { label: string; value: string }) {
  return (
    <div className="spec">
      <div className="spec-key">{label}</div>
      <div className="spec-val">{value}</div>
    </div>
  );
}
