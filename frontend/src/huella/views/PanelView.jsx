import Glyph from '../components/Glyph.jsx';
import { Icon } from '../components/Icon.jsx';
import Guilloche from '../components/Guilloche.jsx';
import ServiceList from '../components/ServiceList.jsx';
import { CountUp, DocRow, LoadError, Loading, NoDocuments } from '../components/docs.jsx';
import { Soon } from '../components/ui.jsx';
import { useDocuments } from '../hooks/useDocuments.js';
import { docTitle, hasTx } from '../lib/docs.js';
import { fmtNum, fmtRel, plural } from '../lib/format.js';

function Stat({ value, label, sub, bar, soon }) {
  return (
    <div className="stat">
      <span className="stat-value tnum">{value == null ? '—' : <CountUp value={value} />}</span>
      <span className="stat-label">{label}</span>
      <span className="stat-sub">{soon ? <Soon /> : sub}</span>
      {bar != null && <span className="stat-bar" style={{ '--v': bar }} />}
    </div>
  );
}

/* Últimas transacciones guardadas en PostgreSQL (datos históricos, no la cadena activa). */
function TxCoins({ docs }) {
  const withTx = docs.filter(hasTx).sort((a, b) => new Date(a.registeredAt) - new Date(b.registeredAt)).slice(-8);
  if (!withTx.length) return <p className="hero-chain-cap">Aún no hay transacciones guardadas.</p>;
  const last = withTx[withTx.length - 1];
  return (
    <>
      <ol className="coins" aria-label="Últimas transacciones guardadas">
        {withTx.map((d, i) => (
          <li key={d.id} style={{ '--k': i }}>
            {/* Sin la animación de "bloque en vivo": es un dato guardado, no la cabeza de la cadena activa. */}
            <a className="coin" href={`#/documentos/${d.id}`} aria-label={`${docTitle(d)}, bloque ${d.blockchainBlockNumber}`}>
              <Glyph hex={d.sha256} size={30} label={`Huella de ${docTitle(d)}`} />
            </a>
            <span className="coin-n tnum">{d.blockchainBlockNumber}</span>
          </li>
        ))}
      </ol>
      <p className="hero-chain-cap">Últimas transacciones guardadas en PostgreSQL. La más reciente, bloque {fmtNum(last.blockchainBlockNumber)}, {fmtRel(last.registeredAt)}.</p>
    </>
  );
}

export default function PanelView({ user }) {
  const firstName = String((user && user.name) || '').trim().split(/\s+/)[0];
  const docs = useDocuments();
  const list = docs.items;
  const ready = docs.status === 'ready' || list.length > 0;
  const last = list[0];
  const withTx = list.filter(hasTx).length;

  return (
    <>
      <section className="hero" aria-labelledby="hero-t">
        <Guilloche className="hero-guilloche" rings={12} inner={36} outer={198} waves={28} amp={7} />
        <div className="hero-main">
          <p className="hero-hello">{firstName ? `Hola, ${firstName}` : 'Hola'}</p>
          <h1 className="hero-title" id="hero-t">¿Qué documento quieres registrar o verificar hoy?</h1>
          <div className="hero-actions">
            <a className="btn btn-mint" href="#/registrar"><Icon name="upload" />Registrar documento</a>
            <a className="btn btn-glass" href="#/verificar"><Icon name="shield" />Verificar documento</a>
          </div>
        </div>
        {last && (
          <a className="hero-glyph" href={`#/documentos/${last.id}`} aria-label={`Último registro: ${docTitle(last)}`}>
            <Glyph hex={last.sha256} size={196} spin label={`Huella de ${docTitle(last)}`} />
            <span className="hero-glyph-cap"><span>Último registro, {fmtRel(last.registeredAt)}</span><strong>{docTitle(last)}</strong></span>
          </a>
        )}
        <div className="hero-chain">
          {ready ? <TxCoins docs={list} /> : <p className="hero-chain-cap">{docs.status === 'error' ? 'No se pudieron cargar los registros.' : 'Cargando registros…'}</p>}
        </div>
      </section>
      {docs.status === 'error' && <LoadError error={docs.error} onRetry={docs.reload} />}
      <section className="stats" aria-label="Resumen">
        <Stat value={ready ? list.length : null} label="Documentos registrados" sub={ready ? plural(withTx, 'con transacción guardada', 'con transacción guardada') : docs.status === 'error' ? 'Sin datos' : 'Cargando…'} bar={ready && list.length ? withTx / list.length : null} />
        <Stat label="Verificaciones" soon />
        <Stat label="Resultaron íntegros" soon />
        <Stat label="Alertas detectadas" soon />
      </section>
      <div className="cols cols-main">
        <section className="panel">
          <header className="panel-head"><h2>Registrados recientemente</h2><a className="link" href="#/documentos">Ver todos</a></header>
          {ready
            ? (list.length ? <ul className="doc-list">{list.slice(0, 5).map((d) => <DocRow key={d.id} doc={d} />)}</ul> : <NoDocuments />)
            : docs.status === 'error' ? <p className="muted">Sin datos.</p> : <Loading />}
        </section>
        <div className="stack">
          <section className="panel">
            <header className="panel-head"><h2>Últimas verificaciones</h2><Soon title="El backend todavía no guarda el historial de verificaciones." /></header>
            <div className="empty"><Icon name="shield" /><p className="empty-title">Historial no disponible</p><p className="empty-text">El backend todavía no guarda las verificaciones realizadas.</p></div>
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
