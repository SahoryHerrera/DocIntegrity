import { useState } from 'react';
import { Icon } from '../components/Icon.jsx';
import { EmptyState, PageHead } from '../components/ui.jsx';

/* Listado de documentos. Los datos de GET /api/documents se conectan en la etapa 3. */
export default function DocumentosView() {
  const [filter, setFilter] = useState({ q: '', type: 'all', sort: 'new' });
  const set = (k) => (e) => setFilter((f) => ({ ...f, [k]: e.target.value }));
  return (
    <>
      <PageHead
        title="Documentos"
        lede="Documentos registrados. Abre uno para ver su evidencia, su análisis y su verificación."
        actions={<>
          <button className="btn btn-secondary" type="button" disabled><Icon name="download" />Exportar CSV</button>
          <a className="btn btn-primary" href="#/registrar"><Icon name="plus" />Registrar documento</a>
        </>}
      />
      <div className="toolbar" role="search">
        <label className="search-field"><Icon name="search" /><span className="sr-only">Buscar</span><input className="input" placeholder="Busca por nombre, título o huella" value={filter.q} onChange={set('q')} /></label>
        <div className="select-wrap">
          <label className="sr-only" htmlFor="doc-type">Tipo</label>
          <select id="doc-type" className="input" value={filter.type} onChange={set('type')}><option value="all">Todos los tipos</option></select>
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
      <div className="table-wrap">
        <table className="table table-docs table-cards">
          <thead><tr><th>Documento</th><th>Tipo</th><th>Registrado</th><th className="hide-md">Bloque</th><th>Estado</th><th><span className="sr-only">Acciones</span></th></tr></thead>
          <tbody>
            <tr><td colSpan={6}><EmptyState icon="files" title="Sin datos todavía" text="Los documentos se cargarán desde la API de DocIntegrity." /></td></tr>
          </tbody>
        </table>
      </div>
    </>
  );
}
