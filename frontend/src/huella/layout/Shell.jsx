import { useEffect, useRef, useState } from 'react';
import { BrandMark, Icon } from '../components/Icon.jsx';
import { useAuth } from '../hooks/useAuth.js';
import { useToast } from '../hooks/useToast.js';
import { fmtClock, initials } from '../lib/format.js';

const NAV = [
  ['panel', 'Panel', 'panel'], ['registrar', 'Registrar', 'upload'], ['documentos', 'Documentos', 'files'],
  ['verificar', 'Verificar', 'shield'], ['cadena', 'Blockchain', 'blocks'], ['actividad', 'Actividad', 'activity'], ['ajustes', 'Ajustes', 'sliders'],
];
const isMac = /Mac|iPhone|iPad/.test(navigator.platform || navigator.userAgent);
const current = (active, key) => (active === key ? 'page' : undefined);

/* Tiempo restante de la sesión, actualizado cada segundo, con aviso a 5 minutos del final. */
function useSessionClock(auth) {
  const toast = useToast();
  const [now, setNow] = useState(() => Date.now());
  const warned = useRef(false);
  useEffect(() => { const t = setInterval(() => setNow(Date.now()), 1000); return () => clearInterval(t); }, []);
  const left = Math.max(0, auth.expiresAt - now);
  useEffect(() => {
    if (left > 0 && left <= 5 * 60_000 && !warned.current) {
      warned.current = true;
      toast('Tu sesión expira en menos de 5 minutos.', 'warn', { timeout: 8000 });
    }
  }, [left, toast]);
  return { left, total: auth.sessionMinutes * 60_000 };
}

function Sidebar({ active, onNavigate, onLogout }) {
  const auth = useAuth();
  const { left, total } = useSessionClock(auth);
  const user = auth.user || {};
  return (
    <aside className="sidebar" aria-label="Navegación principal">
      <a className="sb-brand" href="#/panel" onClick={onNavigate}><BrandMark size={28} /><span>Huella</span></a>
      <nav className="sb-nav" aria-label="Secciones">
        {NAV.map(([k, label, ic]) => (
          <a key={k} href={`#/${k}`} data-label={label} aria-current={current(active, k)} onClick={onNavigate}><Icon name={ic} /><span>{label}</span></a>
        ))}
      </nav>
      <div className="sb-foot">
        <div className="sb-user">
          <span className="avatar" aria-hidden="true">{initials(user.name)}</span>
          <span className="sb-user-text"><strong>{user.name}</strong><span>{user.email}</span></span>
          <button className="icon-btn" type="button" onClick={onLogout} aria-label="Cerrar sesión" title="Cerrar sesión"><Icon name="logout" /></button>
        </div>
        <a className="sb-session" href="#/ajustes" onClick={onNavigate} title="Ver detalles de la sesión">
          <span className="sb-session-row"><span>La sesión expira en</span><span className="tnum">{fmtClock(left)}</span></span>
          <span className="sb-meter"><i style={{ transform: `scaleX(${total ? Math.min(1, left / total) : 0})` }} /></span>
        </a>
      </div>
    </aside>
  );
}

function Topbar({ onPalette, theme }) {
  const dark = theme.effective === 'dark';
  const themeLabel = dark ? 'Usar tema claro' : 'Usar tema oscuro';
  return (
    <header className="topbar">
      <a className="topbar-brand" href="#/panel" aria-label="Huella, ir al panel"><BrandMark size={26} /><span>Huella</span></a>
      <button className="search-trigger" type="button" onClick={onPalette} aria-keyshortcuts="Control+K Meta+K" aria-label="Buscar documentos, huellas o acciones">
        <Icon name="search" />
        <span>Buscar documentos, huellas o acciones</span><kbd>{isMac ? '⌘ K' : 'Ctrl K'}</kbd>
      </button>
      <div className="topbar-right">
        <button className="icon-btn" id="theme-btn" type="button" onClick={theme.toggle} aria-label={themeLabel} title={themeLabel}><Icon name={dark ? 'sun' : 'moon'} /></button>
      </div>
    </header>
  );
}

function Tabbar({ active, onMenu, onNavigate }) {
  return (
    <nav className="tabbar" aria-label="Navegación rápida">
      <a href="#/panel" aria-current={current(active, 'panel')} onClick={onNavigate}><Icon name="panel" /><span>Panel</span></a>
      <a href="#/documentos" aria-current={current(active, 'documentos')} onClick={onNavigate}><Icon name="files" /><span>Documentos</span></a>
      <a href="#/registrar" className="tab-fab" aria-current={current(active, 'registrar')} onClick={onNavigate}><span className="fab"><Icon name="plus" /></span><span>Registrar</span></a>
      <a href="#/verificar" aria-current={current(active, 'verificar')} onClick={onNavigate}><Icon name="shield" /><span>Verificar</span></a>
      <button type="button" onClick={onMenu} aria-label="Más secciones y tu cuenta"><Icon name="menu" /><span>Más</span></button>
    </nav>
  );
}

export default function Shell({ active, theme, onPalette, onLogout, children }) {
  const [menuOpen, setMenuOpen] = useState(false);
  const closeMenu = () => setMenuOpen(false);

  useEffect(() => {
    document.body.classList.toggle('menu-open', menuOpen);
    if (!menuOpen) return undefined;
    const onKey = (e) => { if (e.key === 'Escape') setMenuOpen(false); };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [menuOpen]);
  useEffect(() => () => document.body.classList.remove('menu-open'), []);

  return (
    <div className="app">
      <Sidebar active={active} onNavigate={closeMenu} onLogout={() => { closeMenu(); onLogout(); }} />
      <div className="main">
        <Topbar onPalette={onPalette} theme={theme} />
        {children}
      </div>
      <Tabbar active={active} onMenu={() => setMenuOpen((v) => !v)} onNavigate={closeMenu} />
      <div className="scrim" onClick={closeMenu} />
    </div>
  );
}

/* Contenedor de cada vista: título, foco, desplazamiento y animación de entrada. */
export function ViewFrame({ title, children }) {
  const ref = useRef(null);
  const [entering, setEntering] = useState(true);
  useEffect(() => {
    document.title = `${title} | Huella`;
  }, [title]);
  useEffect(() => {
    window.scrollTo(0, 0);
    if (ref.current) ref.current.focus({ preventScroll: true });
    const t = setTimeout(() => setEntering(false), 1400);
    return () => clearTimeout(t);
  }, []);
  return <main id="view" ref={ref} className={`view ${entering ? 'is-entering' : ''}`} tabIndex={-1}>{children}</main>;
}
