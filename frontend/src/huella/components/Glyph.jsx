import { useMemo, useState } from 'react';
import { glyphGeometry } from '../lib/glyph.js';
import { reduceMotion, short } from '../lib/format.js';

/* Huella visual: cada uno de los 256 bits del SHA-256 es un segmento de los ocho anillos. */
export default function Glyph({ hex = '', size = 120, diffWith = null, animate = false, empty = false, spin = false, className = '', label }) {
  const geo = useMemo(() => glyphGeometry(hex, { size, diffWith, empty }), [hex, size, diffWith, empty]);
  // Fase de giro común a todas las huellas, para que no salten al volver a montarse.
  const [phase] = useState(() => (performance.now() / 1000) % 600);
  const classes = ['glyph', animate && !reduceMotion ? 'is-drawing' : '', spin ? 'is-spinning' : '', empty ? 'is-empty' : '', className].filter(Boolean).join(' ');
  const aria = label || (empty ? 'Huella pendiente' : `Huella visual de ${short(hex, 8, 4)}`);
  return (
    <svg className={classes} viewBox="0 0 100 100" width={size} height={size} role="img" aria-label={aria} strokeWidth={geo.sw} style={spin ? { '--t': `${phase.toFixed(2)}s` } : undefined}>
      {geo.rings.map((ring) => (
        <g key={ring.r} className="gl-ring" style={{ '--i': ring.r, '--p': `${ring.period}s` }}>
          {ring.track && <circle className="gl-track" cx="50" cy="50" r={ring.rad} />}
          {ring.full && <circle className={`gl-${ring.full}`} style={{ '--i': ring.r }} pathLength="1" cx="50" cy="50" r={ring.rad} />}
          {ring.arcs.map((a, j) => <path key={j} className={`gl-${a.kind}`} style={{ '--i': ring.r }} pathLength="1" d={a.d} />)}
        </g>
      ))}
      <circle className="gl-core" cx="50" cy="50" r={geo.coreR} />
    </svg>
  );
}
