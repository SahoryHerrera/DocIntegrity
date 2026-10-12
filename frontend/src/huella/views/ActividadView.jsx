import { Icon } from '../components/Icon.jsx';
import { EmptyState, PageHead, Soon } from '../components/ui.jsx';

/* Bitácora de actividad: pendiente en el backend. */
export default function ActividadView() {
  return (
    <>
      <PageHead
        title="Actividad"
        lede="Quién registró, verificó o consultó cada documento, y cuándo. Sirve como bitácora de trazabilidad."
        actions={<button className="btn btn-secondary" type="button" disabled><Icon name="download" />Exportar CSV</button>}
      />
      <div className="toolbar">
        <label className="search-field"><Icon name="search" /><span className="sr-only">Buscar</span><input className="input" placeholder="Busca por archivo o acción" disabled /></label>
        <div className="select-wrap">
          <label className="sr-only" htmlFor="act-type">Acción</label>
          <select id="act-type" className="input" disabled><option>Todas las acciones</option></select>
          <Icon name="chevronDown" />
        </div>
      </div>
      <section className="panel">
        <EmptyState icon="activity" title="Bitácora no disponible" text="El backend todavía no registra la actividad de los usuarios." action={<Soon />} />
      </section>
    </>
  );
}
