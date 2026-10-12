/* Geometría de la huella visual: 256 bits repartidos en 8 anillos. */

export const RING_SEGMENTS = [16, 20, 24, 28, 36, 40, 44, 48]; // suma = 256

export function hexToBits(hex) {
  const h = (hex || '').replace(/^0x/, '');
  const bits = [];
  for (const ch of h) { const v = parseInt(ch, 16); for (let i = 3; i >= 0; i--) bits.push((v >> i) & 1); }
  while (bits.length < 256) bits.push(0);
  return bits.slice(0, 256);
}

export function bitDiff(a, b) {
  const x = hexToBits(a); const y = hexToBits(b); let n = 0;
  for (let i = 0; i < 256; i++) if (x[i] !== y[i]) n++;
  return n;
}

function arc(rad, a0, a1) {
  const p = (a) => [(50 + rad * Math.cos(a)).toFixed(2), (50 + rad * Math.sin(a)).toFixed(2)];
  if (a1 - a0 < 0.015) {
    const [x, y] = p((a0 + a1) / 2);
    return `M${x} ${y}h0.01`;
  }
  const [x0, y0] = p(a0); const [x1, y1] = p(a1);
  const large = a1 - a0 > Math.PI ? 1 : 0;
  return `M${x0} ${y0}A${rad.toFixed(2)} ${rad.toFixed(2)} 0 ${large} 1 ${x1} ${y1}`;
}

/*
 * Devuelve la descripción de cada anillo: pista de fondo y arcos (on, diff, ghost).
 * Mismo algoritmo que el prototipo; el componente <Glyph> solo lo dibuja.
 */
export function glyphGeometry(hex, { size = 120, diffWith = null, empty = false } = {}) {
  const bits = empty ? new Array(256).fill(0) : hexToBits(hex);
  const ref = diffWith ? hexToBits(diffWith) : null;
  const small = size < 64;
  const inner = 16.5; const outer = 47; const n = RING_SEGMENTS.length;
  const step = (outer - inner) / (n - 1);
  const sw = step * (small ? 0.66 : 0.5);
  let idx = 0;
  const rings = RING_SEGMENTS.map((count, r) => {
    const rad = inner + r * step;
    const seg = bits.slice(idx, idx + count);
    const refSeg = ref ? ref.slice(idx, idx + count) : null;
    idx += count;
    const ring = { r, rad: rad.toFixed(2), period: 26 + r * 9, track: !small || empty, full: null, arcs: [] };
    if (empty) return ring;
    const unit = (Math.PI * 2) / count;
    const rot = -Math.PI / 2 + (r % 2 ? unit / 2 : 0);
    const gap = (sw / 2 + 0.75) / rad;
    const kind = seg.map((b, i) => (b ? (refSeg && refSeg[i] !== b ? 'diff' : 'on') : (refSeg && refSeg[i] ? 'ghost' : 'off')));
    const start = kind.findIndex((k, i) => k !== kind[(i - 1 + count) % count]);
    if (start === -1) {
      if (kind[0] !== 'off') ring.full = kind[0];
      return ring;
    }
    let i = 0;
    while (i < count) {
      const s = (start + i) % count; const k = kind[s]; let len = 1;
      while (i + len < count && kind[(start + i + len) % count] === k) len++;
      if (k !== 'off') ring.arcs.push({ kind: k, d: arc(rad, rot + s * unit + gap, rot + (s + len) * unit - gap) });
      i += len;
    }
    return ring;
  });
  return { rings, sw: sw.toFixed(2), coreR: empty ? 1.6 : 2.6 };
}

/* Patrón guilloché de los papeles de seguridad (se cachea por parámetros). */
const guillocheCache = new Map();
export function guillochePath({ size = 400, inner = 64, outer = 196, rings = 9, waves = 22, amp = 9, points = 264 } = {}) {
  const key = [size, inner, outer, rings, waves, amp, points].join('|');
  let d = guillocheCache.get(key);
  if (!d) {
    const c = size / 2; const parts = [];
    for (let k = 0; k < rings; k++) {
      const R = inner + ((outer - inner) * k) / Math.max(1, rings - 1);
      for (const ph of [0, Math.PI]) {
        let s = '';
        for (let i = 0; i <= points; i++) {
          const a = (i / points) * Math.PI * 2;
          const rr = R + amp * Math.sin(waves * a + ph + k * 0.55);
          s += `${i ? 'L' : 'M'}${(c + rr * Math.cos(a)).toFixed(1)} ${(c + rr * Math.sin(a)).toFixed(1)}`;
        }
        parts.push(s + 'Z');
      }
    }
    d = parts.join('');
    guillocheCache.set(key, d);
  }
  return d;
}
