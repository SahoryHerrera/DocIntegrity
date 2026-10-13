import { useEffect } from 'react';
import Glyph from '../components/Glyph.jsx';
import { Icon } from '../components/Icon.jsx';
import { AiBlock, LoadError, Loading, TxChip } from '../components/docs.jsx';
import { Chip, EmptyState, HashBlock, Kv, Notice, PageHead, Soon } from '../components/ui.jsx';
import { useBlockchainStatus, useDocument, useDocuments } from '../hooks/useDocuments.js';
import { docTitle, sameHash } from '../lib/docs.js';
import { fmtBytes, fmtDate, fmtNum, fmtRel, short } from '../lib/format.js';

const back = <a className="back" href="#/documentos"><Icon name="back" />Documentos</a>;

/* Qué significa cada estado de la transacción guardada en PostgreSQL. */
const TX_STATUS = {
  confirmed: { tone: 'seal', label: 'Confirmada en la cadena activa', text: (s) => `La red activa contiene esta transacción en el bloque ${fmtNum(s.storedTransaction.activeChainBlockNumber)}: se ejecutó correctamente, fue al contrato configurado y registró esta huella.` },
  not_found: { tone: 'warn', label: 'No existe en la cadena activa', text: () => 'La red activa no conoce esta transacción. Probablemente pertenece a una cadena anterior, por ejemplo si el nodo de Hardhat se reinició después del registro.' },
  failed: { tone: 'alert', label: 'Fallida en la cadena activa', text: () => 'La red activa contiene la transacción, pero su ejecución fue revertida.' },
  mismatch: { tone: 'alert', label: 'No corresponde a este registro', text: (s) => `La red activa contiene una transacción con este hash${s.storedTransaction.activeChainBlockNumber != null ? ` en el bloque ${fmtNum(s.storedTransaction.activeChainBlockNumber)}` : ''}, pero no es el registro de esta huella en este contrato o está en otro bloque.` },
  not_stored: { tone: 'neutral', label: 'Sin transacción guardada', text: () => 'PostgreSQL no guarda ninguna transacción para este registro.' },
};

function ChainHeadChip({ status, chain }) {
  if (status === 'loading') return <Chip tone="neutral" icon="clock">Consultando…</Chip>;
  if (status === 'error') return <Chip tone="neutral" icon="alert">Estado no disponible</Chip>;
  return chain.registeredInBlockchain
    ? <Chip tone="seal">Confirmado en la cadena activa</Chip>
    : <Chip tone="warn">Sin evidencia en la cadena activa</Chip>;
}

/* Estado actual en la blockchain activa (GET /api/documents/{id}/blockchain-status). */
function ChainStatus({ status, chain, error, retry }) {
  if (status === 'loading') return <p className="muted small"><span className="spinner" aria-hidden="true" /> Consultando la blockchain activa…</p>;
  if (status === 'error') {
    return (
      <Notice tone="warn" icon="alert" action={<button className="btn btn-sm btn-secondary" type="button" onClick={retry}><Icon name="refresh" />Reintentar</button>}>
        <strong>Estado no disponible.</strong> {error.kind === 'network' ? 'No se pudo conectar con la API de DocIntegrity.' : error.message} Sin esa consulta no se puede afirmar si el registro está en la cadena activa.
      </Notice>
    );
  }
  const tx = TX_STATUS[chain.storedTransaction.status] || TX_STATUS.not_stored;
  return (
    <Kv rows={[
      ['Evidencia del archivo', chain.registeredInBlockchain
        ? <><Chip tone="seal">Huella en el contrato</Chip><span className="prose small muted">getDocument confirma la huella, registrada el {fmtDate(chain.onChainRegisteredAt)} por <span className="mono">{short(chain.onChainRegisteredBy, 8, 6)}</span>.</span></>
        : <><Chip tone="warn">Huella ausente del contrato</Chip><span className="prose small muted">El contrato de la red activa no tiene esta huella. Verificar este archivo dará «Sin evidencia en cadena».</span></>],
      ['Transacción guardada', <><Chip tone={tx.tone}>{tx.label}</Chip><span className="prose small muted">{tx.text(chain)}</span></>],
      ['Red consultada', <>Chain ID <span className="tnum">{chain.networkChainId}</span>, contrato <span className="mono small">{short(chain.contractAddress, 10, 6)}</span></>],
      ['Consultado', fmtDate(chain.checkedAt)],
    ]} />
  );
}

function Duplicates({ doc }) {
  const { items } = useDocuments();
  const others = sameHash(items, doc);
  if (!others.length) return null;
  return (
    <section className="panel">
      <header className="panel-head"><h2>Registros con la misma huella</h2><span className="muted small">{others.length}</span></header>
      <p className="muted small">PostgreSQL contiene otros registros del mismo archivo (mismo SHA-256).</p>
      <ul className="doc-list">
        {others.map((d) => (
          <li key={d.id}><a className="doc-row" href={`#/documentos/${d.id}`}>
            <Glyph hex={d.sha256} size={40} />
            <span className="doc-row-main"><span className="doc-row-title">{d.fileName}</span><span className="doc-row-sub">{fmtDate(d.registeredAt)}{d.blockchainBlockNumber != null ? `, bloque ${fmtNum(d.blockchainBlockNumber)}` : ''}</span></span>
            <TxChip doc={d} />
          </a></li>
        ))}
      </ul>
    </section>
  );
}

export default function DocumentoDetalleView({ id }) {
  const { status, doc, error, retry } = useDocument(id);
  const chainState = useBlockchainStatus(id);
  useEffect(() => { if (doc) document.title = `${docTitle(doc)} | Huella`; }, [doc]);

  if (status === 'loading') {
    return <><PageHead title="Documento" back={back} /><section className="panel"><Loading text="Cargando el documento…" /></section></>;
  }
  if (status === 'error') {
    if (error.status === 404) {
      return (
        <>
          <PageHead title="Documento no encontrado" lede="El enlace puede estar incompleto o el registro ya no existe." back={back} />
          <section className="panel"><EmptyState icon="search" title="No existe un registro con ese identificador" text={id} action={<a className="btn btn-secondary" href="#/documentos">Ver todos los documentos</a>} /></section>
        </>
      );
    }
    return <><PageHead title="Documento" back={back} /><LoadError error={error} onRetry={retry} what="el documento" /></>;
  }

  const d = doc;
  return (
    <>
      <PageHead
        title={docTitle(d)}
        lede={`${d.fileName}, ${fmtBytes(d.size)}`}
        back={back}
        actions={<a className="btn btn-primary" href={`#/verificar/${d.id}`}><Icon name="shield" />Verificar este documento</a>}
      />
      <div className="cols cols-detail">
        <aside className="detail-glyph">
          <Glyph hex={d.sha256} size={240} animate />
          <p className="muted small">Huella visual: cada uno de los 256 bits del SHA-256 es un segmento de los ocho anillos.</p>
        </aside>
        <div className="stack">
          <section className="panel">
            <header className="panel-head"><h2>Evidencia de integridad</h2><ChainHeadChip status={chainState.status} chain={chainState.chain} /></header>
            <Kv rows={[
              ['Huella SHA-256', <HashBlock value={d.sha256} />],
              ['Transacción guardada', d.blockchainTransactionHash ? <HashBlock value={d.blockchainTransactionHash} msg="Transacción copiada" label="Copiar transacción" /> : <span className="muted">No guardada</span>],
              ['Bloque guardado', d.blockchainBlockNumber != null ? <span className="tnum">{fmtNum(d.blockchainBlockNumber)}</span> : <span className="muted">—</span>],
              ['Registrado', <>{fmtDate(d.registeredAt)} <span className="muted">({fmtRel(d.registeredAt)})</span></>],
              ['Identificador', <span className="mono small">{d.id}</span>],
            ]} />
            <h3 className="sub-head">Estado en la blockchain activa</h3>
            <ChainStatus {...chainState} />
          </section>
          <AiBlock doc={d} />
          <Duplicates doc={d} />
          <section className="panel">
            <header className="panel-head"><h2>Verificaciones</h2><Soon title="El backend todavía no guarda el historial de verificaciones." /></header>
            <EmptyState icon="shield" title="Historial no disponible" text="El backend todavía no guarda las verificaciones de cada documento." />
          </section>
        </div>
      </div>
    </>
  );
}
