import { ICONS } from '../lib/icons.js';

/* Los trazos vienen de un mapa de constantes internas, no de datos externos. */
export function Icon({ name, className = '' }) {
  return <svg className={`ic ${className}`.trim()} viewBox="0 0 24 24" aria-hidden="true" dangerouslySetInnerHTML={{ __html: ICONS[name] || '' }} />;
}

export function BrandMark({ size = 26 }) {
  return (
    <svg className="brand-mark" viewBox="0 0 32 32" width={size} height={size} aria-hidden="true">
      <g fill="none" stroke="currentColor" strokeLinecap="round" strokeWidth="2.3">
        <circle cx="16" cy="16" r="13" strokeDasharray="9 4.6" transform="rotate(-78 16 16)" />
        <circle cx="16" cy="16" r="8.2" strokeDasharray="6.2 3.9" transform="rotate(32 16 16)" />
      </g>
      <circle cx="16" cy="16" r="2.7" fill="currentColor" />
    </svg>
  );
}
