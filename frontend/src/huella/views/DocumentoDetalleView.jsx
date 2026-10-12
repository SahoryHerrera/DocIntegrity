import { useEffect } from 'react';
import Glyph from '../components/Glyph.jsx';
import { Icon } from '../components/Icon.jsx';
import { AiBlock, ChainHistoryNote, LoadError, Loading, TxChip } from '../components/docs.jsx';
import { EmptyState, HashBlock, Kv, PageHead, Soon } from '../components/ui.jsx';
import { useDocument, useDocuments } from '../hooks/useDocuments.js';
import { docTitle, sameHash } from '../lib/docs.js';
import { fmtBytes, fmtDate, fmtNum, fmtRel } from '../lib/format.js';

const back = <a className="back" href="#/documentos"><Icon name="back" />Documentos</a>;

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
            <header className="panel-head"><h2>Evidencia de integridad</h2><TxChip doc={d} /></header>
            <Kv rows={[
              ['Huella SHA-256', <HashBlock value={d.sha256} />],
              ['Transacción', d.blockchainTransactionHash ? <HashBlock value={d.blockchainTransactionHash} msg="Transacción copiada" label="Copiar transacción" /> : <span className="muted">No guardada</span>],
              ['Bloque', d.blockchainBlockNumber != null ? <span className="tnum">{fmtNum(d.blockchainBlockNumber)}</span> : <span className="muted">—</span>],
              ['Registrado', <>{fmtDate(d.registeredAt)} <span className="muted">({fmtRel(d.registeredAt)})</span></>],
              ['Identificador', <span className="mono small">{d.id}</span>],
            ]} />
            {d.blockchainTransactionHash && <div className="panel-foot"><ChainHistoryNote /></div>}
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
