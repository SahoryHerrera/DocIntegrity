import { useId } from 'react';
import { reduceMotion } from '../lib/format.js';

/* Sello de goma de los veredictos y comprobantes. */
export default function Stamp({ tone = 'seal', title = 'ÍNTEGRO', ring = '', sub = '', size = 128, animate = false, label }) {
  const id = `stp${useId().replace(/[^a-zA-Z0-9_-]/g, '')}`;
  const seed = Array.from(id).reduce((a, c) => a + c.charCodeAt(0), 0) % 97;
  const fs = Math.min(11.5, 54 / (title.length * 0.64));
  return (
    <svg className={`stamp stamp-${tone} ${animate && !reduceMotion ? 'is-stamping' : ''}`} viewBox="0 0 100 100" width={size} height={size} role="img" aria-label={label || `Sello: ${title}`}>
      <defs>
        <path id={`${id}-p`} d="M50,50 m-39.5,0 a39.5,39.5 0 1,1 79,0 a39.5,39.5 0 1,1 -79,0" />
        <filter id={`${id}-ink`} x="-10%" y="-10%" width="120%" height="120%">
          <feTurbulence type="fractalNoise" baseFrequency="0.6" numOctaves="2" seed={seed} result="n" />
          <feColorMatrix in="n" type="matrix" values="0 0 0 0 0  0 0 0 0 0  0 0 0 0 0  0 0 0 13 -3.6" result="m" />
          <feComposite in="SourceGraphic" in2="m" operator="in" />
        </filter>
      </defs>
      <g filter={`url(#${id}-ink)`}>
        <circle className="st-line" cx="50" cy="50" r="47.6" strokeWidth="2" />
        <circle className="st-line" cx="50" cy="50" r="44.6" strokeWidth=".7" />
        <circle className="st-line" cx="50" cy="50" r="33" strokeWidth="1" />
        <text className="st-ring"><textPath href={`#${id}-p`} startOffset="0" textLength="246" lengthAdjust="spacing">{ring.toUpperCase()}</textPath></text>
        <text className="st-title" x="50" y={sub ? 50.5 : 53.6} textAnchor="middle" style={{ fontSize: `${fs.toFixed(2)}px` }}>{title}</text>
        {sub && <text className="st-sub" x="50" y="60.5" textAnchor="middle">{sub}</text>}
        <path className="st-line" d={`M33 ${sub ? 39.5 : 41}h34M33 ${sub ? 65 : 60}h34`} strokeWidth=".7" />
      </g>
    </svg>
  );
}
