/* Utilidades sobre documentos ya normalizados (ver adapters.js). */

export const UNCLASSIFIED = 'Sin clasificar';
export const docTitle = (d) => (d.ai && d.ai.title) || d.fileName;
export const docType = (d) => (d.ai && d.ai.documentType) || UNCLASSIFIED;
export const hasTx = (d) => !!d.blockchainTransactionHash;

export function filterDocs(items, { q = '', type = 'all', sort = 'new' }) {
  const term = q.trim().toLowerCase().replace(/^0x/, '');
  const list = items.filter((d) => {
    if (type !== 'all' && docType(d) !== type) return false;
    if (!term) return true;
    return [d.fileName, d.ai && d.ai.title, d.ai && d.ai.documentType, d.ai && d.ai.summary, d.sha256, d.blockchainTransactionHash, d.id]
      .some((x) => x && x.toLowerCase().includes(term));
  });
  const by = {
    new: (a, b) => new Date(b.registeredAt) - new Date(a.registeredAt),
    old: (a, b) => new Date(a.registeredAt) - new Date(b.registeredAt),
    name: (a, b) => docTitle(a).localeCompare(docTitle(b), 'es'),
  };
  return [...list].sort(by[sort] || by.new);
}

/* Otros registros con la misma huella SHA-256 (el backend aún no impide duplicados). */
export const sameHash = (items, d) => items.filter((x) => x.id !== d.id && x.sha256 === d.sha256);

export function toCsv(headers, rows) {
  const q = (v) => `"${String(v ?? '').replace(/"/g, '""')}"`;
  return '﻿' + [headers.map(q).join(','), ...rows.map((r) => r.map(q).join(','))].join('\r\n');
}

export function downloadText(name, content, type) {
  const url = URL.createObjectURL(new Blob([content], { type }));
  const a = Object.assign(document.createElement('a'), { href: url, download: name });
  document.body.appendChild(a); a.click(); a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1500);
}
