import { useEffect, useId, useRef } from 'react';
import { createPortal } from 'react-dom';
import { Icon } from './Icon.jsx';

const stack = [];
const FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]):not(.sr-only), select, textarea, [tabindex]:not([tabindex="-1"])';

/* Diálogo modal (hoja inferior en celular). Atrapa el foco y lo devuelve al cerrar. */
export default function Modal({ title, size = 'md', className = '', footer, dismissible = true, onClose, children }) {
  const wrap = useRef(null);
  const titleId = useId();
  const closeRef = useRef(onClose);
  useEffect(() => { closeRef.current = onClose; });

  useEffect(() => {
    const el = wrap.current;
    const prev = document.activeElement;
    stack.push(el);
    document.body.classList.add('has-modal');
    const onKey = (e) => {
      if (stack[stack.length - 1] !== el) return;
      if (e.key === 'Escape' && dismissible) { e.preventDefault(); closeRef.current && closeRef.current(); }
      if (e.key === 'Tab') {
        const f = Array.from(el.querySelectorAll(FOCUSABLE)).filter((x) => x.offsetParent !== null);
        if (!f.length) return;
        if (e.shiftKey && document.activeElement === f[0]) { e.preventDefault(); f[f.length - 1].focus(); }
        else if (!e.shiftKey && document.activeElement === f[f.length - 1]) { e.preventDefault(); f[0].focus(); }
      }
    };
    document.addEventListener('keydown', onKey, true);
    const raf = requestAnimationFrame(() => {
      const target = el.querySelector('[autofocus]') || el.querySelector('.modal-body input, .modal-body button, .modal-foot .btn-primary') || el.querySelector('.modal');
      if (target) target.focus();
    });
    return () => {
      cancelAnimationFrame(raf);
      document.removeEventListener('keydown', onKey, true);
      stack.splice(stack.indexOf(el), 1);
      if (!stack.length) document.body.classList.remove('has-modal');
      if (prev && prev.focus && document.contains(prev)) prev.focus();
    };
  }, [dismissible]);

  const close = () => dismissible && onClose && onClose();
  return createPortal(
    <div className={`modal-wrap ${className}`.trim()} ref={wrap}>
      <div className="modal-backdrop" onClick={close} />
      <div className={`modal modal-${size}`} role="dialog" aria-modal="true" aria-labelledby={title ? titleId : undefined} aria-label={title ? undefined : 'Diálogo'} tabIndex={-1}>
        {title && (
          <header className="modal-head">
            <h2 id={titleId}>{title}</h2>
            {dismissible && <button className="icon-btn" type="button" onClick={close} aria-label="Cerrar"><Icon name="x" /></button>}
          </header>
        )}
        <div className="modal-body">{children}</div>
        {footer && <footer className="modal-foot">{footer}</footer>}
      </div>
    </div>,
    document.body,
  );
}
