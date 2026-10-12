import { guillochePath } from '../lib/glyph.js';

/* Patrón de fondo de los papeles de seguridad. Decorativo. */
export default function Guilloche({ className = '', ...params }) {
  const size = params.size || 400;
  return (
    <svg className={`guilloche ${className}`.trim()} viewBox={`0 0 ${size} ${size}`} aria-hidden="true" focusable="false">
      <path d={guillochePath(params)} />
    </svg>
  );
}
