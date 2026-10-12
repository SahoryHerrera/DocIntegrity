import { Icon } from '../components/Icon.jsx';
import Guilloche from '../components/Guilloche.jsx';
import { EmptyState, Soon } from '../components/ui.jsx';
import ServiceList from '../components/ServiceList.jsx';

function Stat({ label, sub, soon }) {
  return (
    <div className="stat">
      <span className="stat-value tnum">—</span>
      <span className="stat-label">{label}</span>
      <span className="stat-sub">{soon ? <Soon /> : sub}</span>
    </div>
  );
}

export default function PanelView() {
  return (
    <>
      <section className="hero" aria-labelledby="hero-t">
        <Guilloche className="hero-guilloche" rings={12} inner={36} outer={198} waves={28} amp={7} />
        <div className="hero-main">
          <p className="hero-hello">Hola</p>
          <h1 className="hero-title" id="hero-t">¿Qué documento quieres registrar o verificar hoy?</h1>
          <div className="hero-actions">
            <a className="btn btn-mint" href="#/registrar"><Icon name="upload" />Registrar documento</a>
            <a className="btn btn-glass" href="#/verificar"><Icon name="shield" />Verificar documento</a>
          </div>
        </div>
        <div className="hero-chain">
          <p className="hero-chain-cap">Los últimos bloques aparecerán cuando se carguen los registros desde la API.</p>
        </div>
      </section>
      <section className="stats" aria-label="Resumen">
        <Stat label="Documentos registrados" sub="Se cargarán desde la API" />
        <Stat label="Verificaciones" soon />
        <Stat label="Resultaron íntegros" soon />
        <Stat label="Alertas detectadas" soon />
      </section>
      <div className="cols cols-main">
        <section className="panel">
          <header className="panel-head"><h2>Registrados recientemente</h2><a className="link" href="#/documentos">Ver todos</a></header>
          <EmptyState icon="files" title="Sin datos todavía" text="Los registros se cargarán desde la API de DocIntegrity." />
        </section>
        <div className="stack">
          <section className="panel">
            <header className="panel-head"><h2>Últimas verificaciones</h2><Soon title="El backend todavía no guarda el historial de verificaciones." /></header>
            <EmptyState icon="shield" title="Historial no disponible" text="El backend todavía no guarda las verificaciones realizadas." />
          </section>
          <section className="panel">
            <header className="panel-head"><h2>Estado de los servicios</h2><Soon title="Requiere un endpoint de salud en el backend." /></header>
            <ServiceList />
          </section>
        </div>
      </div>
    </>
  );
}
