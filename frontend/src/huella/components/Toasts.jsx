import { useCallback, useState } from 'react';
import { ToastContext } from '../hooks/useToast.js';
import { Icon } from './Icon.jsx';

const TONE_ICON = { ok: 'check', alert: 'alert', warn: 'alert', neutral: 'info' };
let seq = 0;

export default function ToastProvider({ children }) {
  const [items, setItems] = useState([]);

  const toast = useCallback((msg, tone = 'neutral', { timeout = 4200 } = {}) => {
    const id = ++seq;
    setItems((list) => [...list, { id, msg, tone, leaving: false }]);
    setTimeout(() => {
      setItems((list) => list.map((t) => (t.id === id ? { ...t, leaving: true } : t)));
      setTimeout(() => setItems((list) => list.filter((t) => t.id !== id)), 260);
    }, timeout);
  }, []);

  return (
    <ToastContext.Provider value={toast}>
      {children}
      <div className="toasts" aria-live="polite">
        {items.map((t) => (
          <div key={t.id} className={`toast toast-${t.tone} ${t.leaving ? 'is-leaving' : ''}`} role={t.tone === 'alert' ? 'alert' : 'status'}>
            <Icon name={TONE_ICON[t.tone] || 'info'} />
            <span className="toast-msg">{t.msg}</span>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  );
}
