/* Formato de fechas, tamaños y textos (portado de huella-prototipo.html). */

const dtf = new Intl.DateTimeFormat('es-HN', { day: 'numeric', month: 'short', year: 'numeric', hour: 'numeric', minute: '2-digit' });
const dfDay = new Intl.DateTimeFormat('es-HN', { day: 'numeric', month: 'short', year: 'numeric' });
const rtf = new Intl.RelativeTimeFormat('es', { numeric: 'auto' });
const nf = new Intl.NumberFormat('es-HN');

export const fmtDate = (iso) => dtf.format(new Date(iso));
export const fmtDay = (iso) => dfDay.format(new Date(iso));
export const fmtNum = (n) => nf.format(n);

export function fmtRel(iso, now = Date.now()) {
  const diff = (new Date(iso).getTime() - now) / 1000;
  const abs = Math.abs(diff);
  if (abs < 60) return 'hace un momento';
  const units = [['minute', 60], ['hour', 3600], ['day', 86400], ['week', 604800], ['month', 2592000]];
  let unit = 'minute'; let size = 60;
  for (const [u, s] of units) if (abs >= s) { unit = u; size = s; }
  return rtf.format(Math.round(diff / size), unit);
}

export function fmtBytes(n) {
  if (n < 1024) return `${n} B`;
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(1).replace('.', ',')} KB`;
  return `${(n / 1024 / 1024).toFixed(2).replace('.', ',')} MB`;
}

export function fmtClock(ms) {
  const t = Math.max(0, Math.floor(ms / 1000));
  const h = Math.floor(t / 3600); const m = Math.floor((t % 3600) / 60); const s = t % 60;
  return `${h}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`;
}

export const short = (h, a = 10, b = 6) => (h && h.length > a + b + 1 ? `${h.slice(0, a)}…${h.slice(-b)}` : h || '');
export const plural = (n, one, many) => `${fmtNum(n)} ${n === 1 ? one : many}`;
export const ucFirst = (s) => (s ? s.charAt(0).toUpperCase() + s.slice(1) : s);
export const initials = (name) => String(name || '').split(/\s+/).filter(Boolean).slice(0, 2).map((p) => p[0]).join('').toUpperCase();

export const reduceMotion = typeof window !== 'undefined' && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
