import { useMemo, useState } from 'react';
import Glyph from '../components/Glyph.jsx';
import { Icon } from '../components/Icon.jsx';
import { LoadError, Loading, NoDocuments, TxChip } from '../components/docs.jsx';
import { EmptyState, PageHead } from '../components/ui.jsx';
import { useDocuments } from '../hooks/useDocuments.js';
import { useToast } from '../hooks/useToast.js';
import { docTitle, docType, downloadText, filterDocs, toCsv } from '../lib/docs.js';
import { fmtDay, fmtNum, plural, short } from '../lib/format.js';

/* El filtro se conserva al navegar entre el listado y el detalle. */
const saved = { q: '', type: 'all', sort: 'new' };

function Rows({ list, filtered, onClear }) {
  if (!filtered.length) {
    return <tr><td colSpan={6}>{list.length
      ? <EmptyState icon="search" title="No hay documentos que coincidan" text="Prueba con otra búsqueda o quita los filtros." action={<button className="btn btn-sm btn-secondary" type="button" onClick={onClear}>Quitar filtros</button>} />
      : <NoDocuments />}</td></tr>;
  }
  return filtered.map((d, i) => (
    <tr key={d.id} style={{ '--k': Math.min(i, 12) }} onClick={(e) => { if (!e.target.closest('a, button')) location.hash = `#/documentos/${d.id}`; }}>
      <td className="cell-main"><a className="cell-doc" href={`#/documentos/${d.id}`}><Glyph hex={d.sha256} size={34} /><span><span className="cell-title">{docTitle(d)}</span><span className="cell-sub mono">{short(d.sha256, 12, 6)}</span></span></a></td>
      <td className="cell-a" data-label="Tipo">{docType(d)}</td>
      <td className="cell-b tnum" data-label="Registrado">{fmtDay(d.registeredAt)}</td>
      <td className="hide-md cell-hide tnum" data-label="Bloque">{d.blockchainBlockNumber != null ? fmtNum(d.blockchainBlockNumber) : '—'}</td>
      <td className="cell-status"><TxChip doc={d} /></td>
      <td className="cell-actions cell-hide"><a className="btn btn-sm btn-ghost" href={`#/verificar/${d.id}`}>Verificar</a></td>
    </tr>
  ));
}

export default function DocumentosView() {
  const docs = useDocuments();
  const toast = useToast();
  const [filter, setFilter] = useState(() => ({ ...saved }));
  const update = (patch) => setFilter((f) => { const n = { ...f, ...patch }; Object.assign(saved, n); return n; });
  const set = (k) => (e) => update({ [k]: e.target.value });

  const list = docs.items;
  const types = useMemo(() => [...new Set(list.map(docType))].sort((a, b) => a.localeCompare(b, 'es')), [list]);
  const filtered = useMemo(() => filterDocs(list, filter), [list, filter]);
  const loading = docs.status === 'loading' || docs.status === 'idle';
  const failed = docs.status === 'error' && !list.length;

  const exportCsv = () => {
    const rows = filtered.map((d) => [d.id, d.fileName, d.size, d.sha256, d.registeredAt, d.blockchainTransactionHash || '', d.blockchainBlockNumber ?? '', d.ai?.documentType || '', d.ai?.title || '', d.ai?.summary || '']);
    downloadText('documentos.csv', toCsv(['Id', 'FileName', 'Size', 'Sha256', 'RegisteredAt', 'BlockchainTransactionHash', 'BlockchainBlockNumber', 'AiDocumentType', 'AiTitle', 'AiSummary'], rows), 'text/csv;charset=utf-8');
    toast(`Exportados ${plural(rows.length, 'documento', 'documentos')}.`, 'ok');
  };

  const lede = docs.status === 'ready' || list.length
    ? `${plural(list.length, 'documento en tu biblioteca', 'documentos en tu biblioteca')}. Solo tú puedes verlos. Abre uno para ver su evidencia y su análisis.`
    : 'Documentos registrados. Abre uno para ver su evidencia y su análisis.';

  return (
    <>
      <PageHead
        title="Documentos"
        lede={lede}
        actions={<>
          <button className="btn btn-secondary" type="button" onClick={exportCsv} disabled={!filtered.length}><Icon name="download" />Exportar CSV</button>
          <a className="btn btn-primary" href="#/registrar"><Icon name="plus" />Registrar documento</a>
        </>}
      />
      {docs.status === 'error' && <LoadError error={docs.error} onRetry={docs.reload} />}
      <div className="toolbar" role="search">
        <label className="search-field"><Icon name="search" /><span className="sr-only">Buscar</span><input className="input" placeholder="Busca por nombre, título o huella" value={filter.q} onChange={set('q')} /></label>
        <div className="select-wrap">
          <label className="sr-only" htmlFor="doc-type">Tipo</label>
          <select id="doc-type" className="input" value={filter.type} onChange={set('type')}>
            <option value="all">Todos los tipos</option>
            {types.map((t) => <option key={t} value={t}>{t}</option>)}
          </select>
          <Icon name="chevronDown" />
        </div>
        <div className="select-wrap">
          <label className="sr-only" htmlFor="doc-sort">Orden</label>
          <select id="doc-sort" className="input" value={filter.sort} onChange={set('sort')}>
            <option value="new">Más recientes</option><option value="old">Más antiguos</option><option value="name">Por título</option>
          </select>
          <Icon name="chevronDown" />
        </div>
      </div>
      <div className="table-wrap" aria-busy={loading}>
        <table className="table table-docs table-cards">
          <thead><tr><th>Documento</th><th>Tipo</th><th>Registrado</th><th className="hide-md">Bloque</th><th>Blockchain</th><th><span className="sr-only">Acciones</span></th></tr></thead>
          <tbody>
            {loading ? <tr><td colSpan={6}><Loading /></td></tr>
              : failed ? <tr><td colSpan={6}><EmptyState icon="alert" title="Sin datos" text="No se pudo obtener la lista de documentos." /></td></tr>
              : <Rows list={list} filtered={filtered} onClear={() => update({ q: '', type: 'all' })} />}
          </tbody>
        </table>
      </div>
    </>
  );
}
