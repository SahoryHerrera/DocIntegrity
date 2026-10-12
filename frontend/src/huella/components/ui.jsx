/* Piezas pequeñas de la interfaz, con el mismo marcado que el prototipo. */
import { useToast } from '../hooks/useToast.js';
import { Icon } from './Icon.jsx';

export function Chip({ tone = 'neutral', icon, children, className = '' }) {
  return <span className={`chip chip-${tone} ${className}`.trim()}>{icon ? <Icon name={icon} /> : <i className="chip-dot" />}{children}</span>;
}

/* Marca de las funciones que aún no tienen backend. */
export function Soon({ title = 'Esta función todavía no está disponible en el backend.' }) {
  return <span className="chip chip-neutral chip-xs chip-soon" title={title}><Icon name="clock" />Próximamente</span>;
}

export function Notice({ tone = 'info', icon = 'info', action, children }) {
  return (
    <div className={`notice notice-${tone}`} role={tone === 'alert' ? 'alert' : 'status'}>
      <Icon name={icon} />
      <div className="notice-body">{children}</div>
      {action}
    </div>
  );
}

export function Kv({ rows }) {
  return (
    <dl className="kv">
      {rows.filter(Boolean).map(([k, v]) => <div key={k}><dt>{k}</dt><dd>{v}</dd></div>)}
    </dl>
  );
}

/* Hash en grupos de 8; si se pasa `other`, resalta los caracteres distintos. */
export function HexGroups({ hex, other = null }) {
  const h = hex.replace(/^0x/, '');
  const o = other ? other.replace(/^0x/, '') : null;
  const groups = [];
  for (let i = 0; i < h.length; i += 8) {
    const chars = [];
    for (let j = i; j < Math.min(i + 8, h.length); j++) chars.push(o && o[j] !== h[j] ? <b key={j}>{h[j]}</b> : h[j]);
    groups.push(<span key={i}>{chars}</span>);
  }
  return <span className="hexg">{hex.startsWith('0x') && <span className="hex0">0x</span>}{groups}</span>;
}

async function writeClipboard(text) {
  try { await navigator.clipboard.writeText(text); return true; } catch {
    const ta = document.createElement('textarea'); ta.value = text; ta.setAttribute('readonly', ''); ta.style.cssText = 'position:fixed;opacity:0';
    document.body.appendChild(ta); ta.select();
    let ok;
    try { ok = document.execCommand('copy'); } catch { ok = false; }
    ta.remove();
    return ok;
  }
}

export function CopyButton({ value, msg = 'Copiado al portapapeles', label = 'Copiar' }) {
  const toast = useToast();
  const copy = async () => {
    const ok = await writeClipboard(value);
    toast(ok ? msg : 'No se pudo copiar. Selecciona el texto y cópialo manualmente.', ok ? 'ok' : 'warn');
  };
  return <button className="icon-btn sm" type="button" onClick={copy} aria-label={label} title={label}><Icon name="copy" /></button>;
}

export function HashBlock({ value, msg = 'Huella copiada', label = 'Copiar huella' }) {
  return <div className="hash"><HexGroups hex={value} /><CopyButton value={value} msg={msg} label={label} /></div>;
}

export function PageHead({ title, lede, actions, back }) {
  return (
    <header className="page-head">
      <div className="page-head-text">{back}<h1>{title}</h1>{lede && <p className="lede">{lede}</p>}</div>
      {actions && <div className="page-actions">{actions}</div>}
    </header>
  );
}

export function EmptyState({ icon, title, text, action }) {
  return <div className="empty"><Icon name={icon} /><p className="empty-title">{title}</p><p className="empty-text">{text}</p>{action}</div>;
}

export function FileCard({ name, size, sub, onRemove }) {
  return (
    <div className="file-card">
      <span className="file-ic"><Icon name="file" /></span>
      <span className="file-main"><span className="file-name">{name}</span><span className="file-sub">{sub ?? size}</span></span>
      {onRemove && <button className="icon-btn" type="button" onClick={onRemove} aria-label="Quitar archivo" title="Quitar archivo"><Icon name="x" /></button>}
    </div>
  );
}

const STATE_TEXT = { pending: 'Pendiente', active: 'En curso', done: 'Completado', error: 'Con error', skipped: 'Omitido' };

/* Lista de pasos del proceso en el servidor. */
export function Pipeline({ steps }) {
  return (
    <ol className="pipeline">
      {steps.map((s, i) => {
        const mark = s.state === 'done' ? <Icon name={s.tone === 'bad' ? 'x' : 'check'} />
          : s.state === 'error' ? <Icon name="alert" />
          : s.state === 'active' ? <span className="spinner" aria-hidden="true" />
          : s.state === 'skipped' ? <Icon name="chevron" />
          : <span>{i + 1}</span>;
        return (
          <li key={s.id} className={`step is-${s.state} ${s.tone === 'bad' ? 'is-bad' : ''}`}>
            <span className="step-mark" aria-hidden="true">{mark}</span>
            <div className="step-body">
              <div className="step-title"><span>{s.label}</span><span className="sr-only">: {STATE_TEXT[s.state]}</span></div>
              <div className="step-service">{s.service}</div>
              {s.detail && <div className={`step-detail ${s.mono ? 'mono' : ''}`}>{s.mono ? <HexGroups hex={s.detail} /> : s.detail}</div>}
            </div>
          </li>
        );
      })}
    </ol>
  );
}
