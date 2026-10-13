import { useState } from 'react';
import Modal from './Modal.jsx';
import Glyph from './Glyph.jsx';
import { Icon } from './Icon.jsx';
import { useDocuments } from '../hooks/useDocuments.js';
import { docTitle } from '../lib/docs.js';
import { short } from '../lib/format.js';

const go = (hash) => () => { location.hash = hash; };

/* Paleta de comandos (Ctrl/⌘ + K). Busca en los documentos reales y en las acciones. */
export default function CommandPalette({ onClose, onToggleTheme, onLogout }) {
  const { items: documents } = useDocuments();
  const [q, setQ] = useState('');
  const [idx, setIdx] = useState(0);
  const actions = [
    { label: 'Registrar documento', ic: 'upload', run: go('#/registrar') },
    { label: 'Verificar documento', ic: 'shield', run: go('#/verificar') },
    { label: 'Ver documentos', ic: 'files', run: go('#/documentos') },
    { label: 'Explorar la blockchain', ic: 'blocks', run: go('#/cadena') },
    { label: 'Ver actividad', ic: 'activity', run: go('#/actividad') },
    { label: 'Ajustes y sesión', ic: 'sliders', run: go('#/ajustes') },
    { label: 'Cambiar tema claro u oscuro', ic: 'moon', run: onToggleTheme },
    { label: 'Cerrar sesión', ic: 'logout', run: onLogout },
  ];
  const term = q.trim().toLowerCase().replace(/^0x/, '');
  const docs = documents
    .filter((d) => !term || [d.fileName, d.ai && d.ai.title, d.ai && d.ai.documentType, d.sha256].some((x) => x && x.toLowerCase().includes(term)))
    .slice(0, 6)
    .map((d) => ({ key: d.id, label: docTitle(d), sub: short(d.sha256, 12, 6), sha: d.sha256, run: go(`#/documentos/${d.id}`) }));
  const items = [...docs, ...actions.filter((a) => !term || a.label.toLowerCase().includes(term))];
  const sel = Math.min(idx, Math.max(0, items.length - 1));
  const run = (i) => { const it = items[i]; if (!it) return; onClose(); it.run(); };
  const onKeyDown = (e) => {
    const n = Math.max(1, items.length);
    if (e.key === 'ArrowDown') { e.preventDefault(); setIdx((sel + 1) % n); }
    else if (e.key === 'ArrowUp') { e.preventDefault(); setIdx((sel - 1 + n) % n); }
    else if (e.key === 'Enter') { e.preventDefault(); run(sel); }
  };
  return (
    <Modal className="palette" onClose={onClose}>
      <div className="pal-input">
        <Icon name="search" />
        <input
          id="pal-q" autoComplete="off" spellCheck="false" placeholder="Busca un documento, una huella o una acción" aria-label="Buscar"
          aria-controls="pal-list" aria-activedescendant={items.length ? `pal-${sel}` : undefined} autoFocus
          value={q} onChange={(e) => { setQ(e.target.value); setIdx(0); }} onKeyDown={onKeyDown}
        />
      </div>
      <ul id="pal-list" className="pal-list" role="listbox" aria-label="Resultados">
        {items.length ? items.map((it, i) => (
          <li key={it.key || it.label} role="option" id={`pal-${i}`} aria-selected={i === sel} onClick={() => run(i)}>
            {it.sha ? <Glyph hex={it.sha} size={26} /> : <span className="pal-ic"><Icon name={it.ic} /></span>}
            <span className="pal-label">{it.label}</span>
            {it.sub && <span className="pal-sub mono">{it.sub}</span>}
          </li>
        )) : <li className="pal-empty">Sin resultados.</li>}
      </ul>
      <p className="pal-hint"><kbd>↑</kbd><kbd>↓</kbd> para moverte, <kbd>Enter</kbd> para abrir, <kbd>Esc</kbd> para cerrar</p>
    </Modal>
  );
}
