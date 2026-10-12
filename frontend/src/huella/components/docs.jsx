/* Piezas de interfaz para documentos reales de DocIntegrity.Api. */
import { useEffect, useRef } from 'react';
import Glyph from './Glyph.jsx';
import { Icon } from './Icon.jsx';
import { Chip, EmptyState, Kv, Notice, Soon } from './ui.jsx';
import { docTitle, docType, hasTx } from '../lib/docs.js';
import { fmtNum, fmtRel, reduceMotion } from '../lib/format.js';

/*
 * Los datos de blockchain vienen de PostgreSQL: son los que devolvió la red al
 * registrar. Esta pantalla no consulta la cadena activa, así que no afirma que
 * la transacción siga existiendo en ella.
 */
export function TxChip({ doc }) {
  return hasTx(doc)
    ? <span title="PostgreSQL guarda la transacción y el bloque del registro. Su presencia en la cadena activa se comprueba al verificar."><Chip tone="info">Transacción guardada</Chip></span>
    : <span title="Este registro no tiene transacción de blockchain en PostgreSQL."><Chip tone="warn">Sin transacción</Chip></span>;
}

export function ChainHistoryNote() {
  return (
    <Notice tone="info" icon="info">
      <strong>Datos históricos de blockchain.</strong> La transacción y el bloque se guardaron en PostgreSQL al registrar. Huella no consulta aquí la red activa: si el nodo de Hardhat se reinició, la cadena actual puede no contenerlos. La presencia de la huella en la cadena se comprueba al verificar.
    </Notice>
  );
}

export function DocRow({ doc }) {
  return (
    <li>
      <a className="doc-row" href={`#/documentos/${doc.id}`}>
        <Glyph hex={doc.sha256} size={40} />
        <span className="doc-row-main"><span className="doc-row-title">{docTitle(doc)}</span><span className="doc-row-sub">{docType(doc)}, {fmtRel(doc.registeredAt)}</span></span>
        <TxChip doc={doc} />
      </a>
    </li>
  );
}

/* Error de carga con reintento. 404 y errores de conexión tienen su propio texto. */
export function LoadError({ error, onRetry, what = 'los documentos' }) {
  const [title, detail] = error.kind === 'network' ? ['No se pudo conectar con la API.', 'Comprueba que DocIntegrity.Api esté en ejecución.']
    : error.kind === 'timeout' ? ['La API tardó demasiado en responder.', error.message]
    : error.kind === 'config' ? ['Falta configurar la API.', error.message]
    : [`No se pudieron cargar ${what}.`, error.message];
  return (
    <Notice tone="alert" icon="alert" action={onRetry && <button className="btn btn-sm btn-secondary" type="button" onClick={onRetry}><Icon name="refresh" />Reintentar</button>}>
      <strong>{title}</strong> {detail}
    </Notice>
  );
}

export function Loading({ text = 'Cargando documentos…' }) {
  return <div className="empty" role="status"><span className="spinner" aria-hidden="true" /><p className="empty-text">{text}</p></div>;
}

/* Número que sube de 0 al valor final, como en el prototipo. */
export function CountUp({ value }) {
  const ref = useRef(null);
  useEffect(() => {
    const el = ref.current;
    if (!el || reduceMotion || !value) return undefined;
    let raf; const t0 = performance.now();
    const tick = (now) => {
      const p = Math.min(1, (now - t0) / 900); const e = 1 - Math.pow(1 - p, 4);
      el.textContent = fmtNum(Math.round(value * e));
      if (p < 1) raf = requestAnimationFrame(tick);
    };
    raf = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(raf);
  }, [value]);
  return <span ref={ref}>{fmtNum(value)}</span>;
}

/* Análisis de IA tal como lo guardó PostgreSQL. */
export function AiBlock({ doc, compact = false }) {
  const ai = doc.ai;
  const keys = ai ? Object.keys(ai.metadata) : [];
  return (
    <section className="ai-block">
      <header className="ai-head"><span className="ai-badge"><Icon name="spark" />Análisis de IA</span><span className="muted small">Ollama, ejecutado en local</span></header>
      {ai ? (
        <>
          <Kv rows={[
            ai.documentType && ['Tipo', ai.documentType],
            ai.title && ['Título', ai.title],
            ai.summary && ['Resumen', <span className="prose">{ai.summary}</span>],
          ]} />
          {keys.length > 0 && (
            <div className="meta-table">
              <div className="meta-head">Metadatos extraídos</div>
              <dl>{keys.slice(0, compact ? 5 : 50).map((k) => <div key={k}><dt>{k}</dt><dd>{String(ai.metadata[k])}</dd></div>)}</dl>
              {compact && keys.length > 5 && <a className="link small" href={`#/documentos/${doc.id}`}>Ver los {keys.length} metadatos</a>}
            </div>
          )}
        </>
      ) : (
        <>
          <p className="muted">Este registro no tiene análisis de IA guardado. Puede ser anterior a la integración con Ollama.</p>
          <button className="btn btn-sm btn-secondary" type="button" disabled><Icon name="spark" />Analizar ahora</button>
          <p className="muted small"><Soon title="Requiere un endpoint que analice y guarde el resultado de un documento ya registrado." /></p>
        </>
      )}
      <p className="ai-note">La IA describe el documento. No participa en la verificación de integridad.</p>
    </section>
  );
}

export function NoDocuments() {
  return <EmptyState icon="files" title="Aún no hay documentos" text="Registra tu primer PDF para guardar su huella en blockchain." action={<a className="btn btn-primary" href="#/registrar">Registrar documento</a>} />;
}
