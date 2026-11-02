import { useMemo, useState } from 'react';
import Dropzone from '../components/Dropzone.jsx';
import Glyph from '../components/Glyph.jsx';
import { Icon } from '../components/Icon.jsx';
import Stamp from '../components/Stamp.jsx';
import { Chip, EmptyState, FileCard, HashBlock, HexGroups, Kv, Notice, PageHead, Pipeline } from '../components/ui.jsx';
import { APP } from '../config.js';
import { api } from '../lib/api.js';
import {
  CHANGE_TYPES, COMPARISON_VERDICTS, EXTRACTION_STATUS, FILE_LABEL, METADATA_FIELDS, NOT_COMPARABLE_REASONS, RELIABILITY,
  TAGS, WARNING_TONE, comparisonErrorText, filterChanges, locationText, pagesOf, sortWarnings, unitName,
} from '../lib/comparison.js';
import { validatePdf } from '../lib/files.js';
import { bitDiff } from '../lib/glyph.js';
import { fmtBytes, fmtDate, fmtDay, fmtNum, plural } from '../lib/format.js';
import { sha256Hex } from '../lib/sha256.js';

/* Pasos de DocumentComparisonService, en su orden real. */
const STEPS = [
  { id: 'hash', label: 'Calcular las huellas SHA-256', service: 'HashService, sobre los dos archivos' },
  { id: 'extract', label: 'Extraer el texto por página', service: 'PdfPig, con informe de calidad de cada página' },
  { id: 'diff', label: 'Comparar el texto', service: 'Normalización documentada y algoritmo de Myers por palabras' },
];

const SIDES = [
  { key: 'original', step: 1, label: 'Versión original', drop: 'Arrastra aquí el PDF original' },
  { key: 'modified', step: 2, label: 'Nueva versión', drop: 'Arrastra aquí la nueva versión' },
];

const freshSide = () => ({ file: null, sha: null, error: null });
const freshRun = () => ({ status: 'idle', result: null, failure: null, at: null });

function stepsFor(run) {
  if (run.status !== 'done') return STEPS.map((s) => ({ ...s, state: 'pending' }));
  const r = run.result;
  const same = r.verdict === 'identical';
  const extraction = (f) => EXTRACTION_STATUS[r.files[f].extraction?.status] || '—';
  const detail = {
    // Huellas distintas o cambios en el texto son resultados, no fallos: el veredicto los comunica.
    hash: { detail: same ? 'Las dos huellas son iguales.' : 'Las huellas son distintas.' },
    extract: same
      ? { state: 'skipped', detail: 'No hizo falta: los archivos son idénticos.' }
      : { tone: r.reliability === 'complete' ? 'ok' : 'bad', detail: `Original: ${extraction('original')}. Nueva versión: ${extraction('modified')}.` },
    diff: same || r.verdict === 'not_comparable'
      ? { state: 'skipped', detail: same ? 'No hizo falta.' : 'No se comparó: el texto no es fiable.' }
      : { detail: r.verdict === 'same_text' ? 'El texto coincide.' : r.diffIncomplete ? 'El texto difiere; demasiadas diferencias para detallarlas.' : `${plural(r.totalChanges, 'cambio', 'cambios')} en el texto.` },
  };
  return STEPS.map((s) => ({ ...s, state: 'done', ...detail[s.id] }));
}

function verdictText(r) {
  if (r.verdict === 'not_comparable') return NOT_COMPARABLE_REASONS[r.notComparableReason] || COMPARISON_VERDICTS.not_comparable.text;
  if (r.verdict === 'modified' && r.stats) {
    const s = r.stats;
    return `Se detectaron ${plural(r.totalChanges, 'cambio', 'cambios')} en el texto: ${fmtNum(s.added)} ${s.added === 1 ? 'añadido' : 'añadidos'}, ${fmtNum(s.removed)} ${s.removed === 1 ? 'eliminado' : 'eliminados'} y ${fmtNum(s.replaced)} ${s.replaced === 1 ? 'reemplazado' : 'reemplazados'}.`;
  }
  return COMPARISON_VERDICTS[r.verdict].text;
}

function VerdictCard({ result, date }) {
  const V = COMPARISON_VERDICTS[result.verdict] || COMPARISON_VERDICTS.not_comparable;
  return (
    <div className={`verdict verdict-${V.tone} is-new`}>
      <span className="verdict-stamp"><Stamp tone={V.tone} title={V.stamp} ring={V.ring} sub={fmtDay(date).toUpperCase()} size={112} animate label={`Sello: ${V.title}`} /></span>
      <div><h2>{V.title}</h2><p>{verdictText(result)}</p><p className="verdict-next">{V.next}</p></div>
    </div>
  );
}

function extractionRow(file) {
  const e = file.extraction;
  if (!e) return `${file.fileName}, ${fmtBytes(file.size)}`;
  const parts = [`${file.fileName}, ${fmtBytes(file.size)}`];
  if (Number.isInteger(file.pageCount)) parts.push(plural(file.pageCount, 'página', 'páginas'));
  if (e.words) parts.push(plural(e.words, 'palabra', 'palabras'));
  return <>{parts.join(' · ')}<br /><span className={e.status === 'complete' ? 't-seal' : 't-warn'}>Extracción: {EXTRACTION_STATUS[e.status] || e.status}</span></>;
}

function Summary({ run, onAgain }) {
  const r = run.result;
  const a = r.files.original.sha256;
  const b = r.files.modified.sha256;
  const n = bitDiff(a, b);
  return (
    <>
      <VerdictCard result={r} date={run.at} />
      <div className="compare">
        <figure><Glyph hex={a} size={156} /><figcaption>Original</figcaption></figure>
        <span className={`compare-vs ${n ? 't-alert' : 't-seal'}`} aria-hidden="true">{n ? '≠' : '='}</span>
        <figure><Glyph hex={b} size={156} diffWith={a} animate={n > 0} /><figcaption>Nueva versión</figcaption></figure>
      </div>
      <p className="bits">{n === 0 ? 'Los 256 bits de las huellas coinciden: es el mismo archivo.' : `Las huellas difieren en ${n} de 256 bits: no es el mismo archivo.`}</p>
      <div className="hexcmp cmp-hexcmp">
        <div><span className="hexcmp-k">Orig.</span><HexGroups hex={a} /></div>
        <div><span className="hexcmp-k">Nueva</span><HexGroups hex={b} other={a} /></div>
      </div>
      <Kv rows={[
        ['¿Mismo archivo?', n === 0 ? <span className="t-seal">Sí, mismo SHA-256</span> : <span className="t-alert">No, SHA-256 distinto</span>],
        ['Original', extractionRow(r.files.original)],
        ['Nueva versión', extractionRow(r.files.modified)],
        ['Fiabilidad del texto', RELIABILITY[r.reliability] || r.reliability],
        r.granularity === 'line' && ['Detalle', 'Por líneas: los documentos son muy distintos.'],
        ['Comparado', fmtDate(run.at)],
      ]} />
      <div className="btn-row"><button className="btn btn-ghost" type="button" onClick={onAgain}><Icon name="refresh" />Comparar otros archivos</button></div>
    </>
  );
}

function Idle({ sides, running }) {
  const a = sides.original.sha;
  const b = sides.modified.sha;
  const both = a && b;
  const same = both && a === b;
  return (
    <>
      <div className="compare compare-idle">
        <figure>{a ? <Glyph hex={a} size={156} spin={running} /> : <Glyph size={156} empty />}<figcaption>Original</figcaption></figure>
        <span className={`compare-vs ${both ? (same ? 't-seal' : 't-alert') : ''}`} aria-hidden="true">{running ? <span className="spinner" /> : both ? (same ? '=' : '≠') : '='}</span>
        <figure>{b ? <Glyph hex={b} size={156} spin={running} diffWith={a} /> : <Glyph size={156} empty />}<figcaption>Nueva versión</figcaption></figure>
      </div>
      <p className="reg-ph-text center">
        {running ? 'El servidor está calculando las huellas, extrayendo el texto y comparándolo…'
          : !a && !b ? 'Sube las dos versiones del documento que quieres comparar.'
          : !both ? 'Ahora sube la otra versión.'
          : same ? 'Vista previa: las dos huellas son iguales, así que son el mismo archivo. El servidor lo confirmará.'
          : 'Vista previa: las huellas son distintas. El servidor comparará también el texto.'}
      </p>
    </>
  );
}

function Warnings({ result }) {
  const list = sortWarnings(result.warnings).filter((w) => w.code !== 'identical_files');
  if (!list.length) return null;
  return (
    <div className="cmp-warnings">
      {list.map((w, i) => (
        <Notice key={`${w.code}-${w.file}-${i}`} tone={WARNING_TONE[w.severity] || 'info'} icon={w.severity === 'info' ? 'info' : 'alert'}>
          {w.file && <strong>{FILE_LABEL[w.file] || w.file}: </strong>}
          {w.message}
          {w.pages?.length > 0 && <> Páginas: {w.pages.join(', ')}.</>}
        </Notice>
      ))}
    </div>
  );
}

function Stats({ result }) {
  const s = result.stats;
  if (!s || result.verdict !== 'modified') return null;
  const unit = (n) => `${fmtNum(n)} ${unitName(result.granularity, n)}`;
  return (
    <div className="stats" role="group" aria-label="Estadísticas de la comparación">
      <div className="stat"><span className="stat-value">{fmtNum(s.added)}</span><span className="stat-label">Añadidos</span><span className="stat-sub">{unit(s.unitsAdded)} nuevas en total</span></div>
      <div className="stat"><span className="stat-value">{fmtNum(s.removed)}</span><span className="stat-label">Eliminados</span><span className="stat-sub">{unit(s.unitsRemoved)} quitadas en total</span></div>
      <div className="stat"><span className="stat-value">{fmtNum(s.replaced)}</span><span className="stat-label">Reemplazados</span><span className="stat-sub">Páginas: {s.pagesAffectedModified.length ? s.pagesAffectedModified.join(', ') : '—'}</span></div>
      <div className="stat">
        <span className="stat-value">{Math.round(s.similarity * 1000) / 10}<span className="stat-of"> %</span></span>
        <span className="stat-label">Texto sin cambios</span>
        <span className="stat-bar" style={{ '--v': s.similarity }} aria-hidden="true" />
      </div>
    </div>
  );
}

function MetadataTable({ result }) {
  const rows = result.metadataDifferences;
  if (!rows.length) return null;
  return (
    <section className="panel">
      <header className="panel-head"><h2>Metadatos del PDF</h2><Chip tone="neutral">No influyen en el veredicto</Chip></header>
      <div className="table-wrap">
        <table className="table table-compact table-cards">
          <thead><tr><th>Campo</th><th>Original</th><th>Nueva versión</th></tr></thead>
          <tbody>
            {rows.map((m) => (
              <tr key={m.field}>
                <td className="cell-main"><span className="cell-title">{METADATA_FIELDS[m.field] || m.field}</span></td>
                <td data-label="Original" className="cmp-break">{m.original ?? <span className="muted">(vacío)</span>}</td>
                <td data-label="Nueva versión" className="cmp-break">{m.modified ?? <span className="muted">(vacío)</span>}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}

/* El cambio en línea: contexto, texto eliminado y texto añadido. */
function ChangeText({ change }) {
  const removed = change.original.insertionPoint ? null : change.original.text;
  const added = change.modified.insertionPoint ? null : change.modified.text;
  return (
    <p className="diff-line">
      {change.context.before && <span className="diff-ctx">…{change.context.before} </span>}
      {removed && <del className="diff-del"><span className="sr-only">Eliminado: </span>{removed}{change.original.textTruncated && '…'}</del>}
      {removed && added && ' '}
      {added && <ins className="diff-ins"><span className="sr-only">Añadido: </span>{added}{change.modified.textTruncated && '…'}</ins>}
      {change.context.after && <span className="diff-ctx"> {change.context.after}…</span>}
    </p>
  );
}

function ChangeRow({ change, index }) {
  const T = CHANGE_TYPES[change.type];
  return (
    <tr style={{ '--k': Math.min(index, 20) }}>
      <td data-label="Tipo" className="cell-status">
        <Chip tone={T.tone} icon={T.icon}>{T.label}</Chip>
        {change.suspicious && <Chip tone="alert" icon="alert" className="cmp-flag">Sospechoso</Chip>}
        {change.onIncompletePage && <Chip tone="warn" icon="info" className="cmp-flag">Página no fiable</Chip>}
      </td>
      <td data-label="Original" className="nowrap">{locationText(change.original)}</td>
      <td data-label="Nueva versión" className="nowrap">{locationText(change.modified)}</td>
      <td data-label="Cambio" className="cell-wide">
        <ChangeText change={change} />
        {change.section && <span className="cell-sub">Sección aproximada: {change.section.title}</span>}
      </td>
      <td data-label="Etiquetas" className="cell-wide">
        <span className="cmp-tags">
          {change.tags.length ? change.tags.map((t) => <Chip key={t} tone={TAGS[t]?.tone || 'neutral'}>{TAGS[t]?.label || t}</Chip>) : <span className="muted">—</span>}
        </span>
      </td>
    </tr>
  );
}

function Changes({ result }) {
  const [filter, setFilter] = useState({ type: 'all', page: 'all', suspicious: false });
  const changes = result.changes;
  const pages = useMemo(() => pagesOf(changes), [changes]);
  const shown = useMemo(() => filterChanges(changes, filter), [changes, filter]);
  const counts = useMemo(() => Object.fromEntries(Object.keys(CHANGE_TYPES).map((k) => [k, changes.filter((c) => c.type === k).length])), [changes]);
  const anySuspicious = changes.some((c) => c.suspicious);
  const set = (patch) => setFilter((f) => ({ ...f, ...patch }));

  if (result.verdict !== 'modified') return null;

  return (
    <section className="panel">
      <header className="panel-head">
        <h2>Cambios en el texto</h2>
        <span className="muted small">{result.changesTruncated ? `Se muestran ${fmtNum(changes.length)} de ${fmtNum(result.totalChanges)}` : plural(result.totalChanges, 'cambio', 'cambios')}</span>
      </header>
      {result.diffIncomplete
        ? <EmptyState icon="alert" title="Sin detalle de cambios" text="Los documentos tienen demasiadas diferencias para detallarlas dentro de los límites del servidor. El texto es distinto." />
        : (
          <>
            <div className="toolbar cmp-toolbar">
              <div className="seg" role="radiogroup" aria-label="Tipo de cambio">
                <button type="button" role="radio" aria-checked={filter.type === 'all'} onClick={() => set({ type: 'all' })}>Todos <span className="muted">{fmtNum(changes.length)}</span></button>
                {Object.entries(CHANGE_TYPES).map(([k, T]) => (
                  <button key={k} type="button" role="radio" aria-checked={filter.type === k} onClick={() => set({ type: k })} disabled={!counts[k]}>
                    {T.plural} <span className="muted">{fmtNum(counts[k])}</span>
                  </button>
                ))}
              </div>
              <div className="select-wrap">
                <label className="sr-only" htmlFor="cmp-page">Página</label>
                <select id="cmp-page" className="input" value={filter.page} onChange={(e) => set({ page: e.target.value })}>
                  <option value="all">Todas las páginas</option>
                  {pages.map((p) => <option key={p} value={p}>Página {p}</option>)}
                </select>
                <Icon name="chevronDown" />
              </div>
              {anySuspicious && (
                <label className="switch">
                  <input type="checkbox" checked={filter.suspicious} onChange={(e) => set({ suspicious: e.target.checked })} />
                  <span className="switch-track" aria-hidden="true" />
                  <span className="switch-text">Solo sospechosos</span>
                </label>
              )}
            </div>
            <div className="table-wrap">
              <table className="table table-diff table-cards">
                <thead><tr><th>Tipo</th><th>Original</th><th>Nueva versión</th><th>Cambio</th><th>Etiquetas</th></tr></thead>
                <tbody>
                  {shown.length
                    ? shown.map((c, i) => <ChangeRow key={c.id} change={c} index={i} />)
                    : <tr><td colSpan={5}><EmptyState icon="filter" title="Ningún cambio con estos filtros" text="Prueba con otro tipo o con otra página." action={<button className="btn btn-sm btn-secondary" type="button" onClick={() => setFilter({ type: 'all', page: 'all', suspicious: false })}>Quitar filtros</button>} /></td></tr>}
                </tbody>
              </table>
            </div>
            <p className="field-hint cmp-note">Páginas y líneas son aproximadas: dependen del orden en que el PDF guarda su texto. «(posición)» indica dónde se insertó o eliminó el texto en esa versión.</p>
          </>
        )}
    </section>
  );
}

function Normalization({ result }) {
  if (!result.normalization.length || result.verdict === 'identical') return null;
  return (
    <details className="panel cmp-details">
      <summary><h2>Cómo se comparó el texto</h2></summary>
      <p className="prose">{result.scope?.note}</p>
      <ul className="cmp-norm">{result.normalization.map((n) => <li key={n.code}>{n.description}</li>)}</ul>
    </details>
  );
}

export default function CompararView() {
  const [sides, setSides] = useState({ original: freshSide(), modified: freshSide() });
  const [run, setRun] = useState(freshRun);
  const running = run.status === 'running';
  const ready = sides.original.file && sides.modified.file;

  const onFile = (key) => async (file) => {
    setRun(freshRun());
    const v = await validatePdf(file);
    if (v.error) { setSides((s) => ({ ...s, [key]: { ...freshSide(), error: v.error } })); return; }
    const sha = await sha256Hex(v.bytes);
    setSides((s) => ({ ...s, [key]: { file, sha, error: null } }));
  };
  const clearSide = (key) => () => { setRun(freshRun()); setSides((s) => ({ ...s, [key]: freshSide() })); };
  const reset = () => { setRun(freshRun()); setSides({ original: freshSide(), modified: freshSide() }); };

  const start = async () => {
    if (!ready || running) return;
    setRun({ ...freshRun(), status: 'running' });
    try {
      const result = await api.comparisons.compare(sides.original.file, sides.modified.file);
      setRun({ status: 'done', result, failure: null, at: new Date().toISOString() });
    } catch (e) {
      setRun({ status: 'error', result: null, failure: e, at: new Date().toISOString() });
    }
  };

  const failure = run.status === 'error' ? run.failure : null;
  const failedField = failure?.data?.field;
  const totalBytes = (sides.original.file?.size || 0) + (sides.modified.file?.size || 0);
  const done = run.status === 'done';

  return (
    <>
      <PageHead
        title="Comparar versiones"
        lede="Compara dos versiones de un PDF. El resultado lo deciden las huellas SHA-256 y una comparación determinista del texto extraído; la IA no interviene. Los archivos no se guardan ni se registran en la blockchain."
      />
      <div className="cols cols-ver">
        <section className="ver-form">
          {SIDES.map((side) => {
            const s = sides[side.key];
            return (
              <div className="field-step" key={side.key}>
                <span className="step-num" aria-hidden="true">{side.step}</span>
                <div className="field-step-body">
                  <span className="field-label">{side.label}</span>
                  {s.file
                    ? <>
                        <FileCard name={s.file.name} sub={fmtBytes(s.file.size)} onRemove={running ? null : clearSide(side.key)} />
                        <div className="cmp-hash"><span className="field-hint">SHA-256 calculado en tu navegador</span><HashBlock value={s.sha} label={`Copiar la huella de la ${side.label.toLowerCase()}`} /></div>
                      </>
                    : <Dropzone id={`cmp-${side.key}`} title={side.drop} onFile={onFile(side.key)} />}
                  {s.error && <p className="field-error" role="alert">{s.error}</p>}
                  {failedField === side.key && <p className="field-error" role="alert">{failure.message}</p>}
                </div>
              </div>
            );
          })}
          {failure && !failedField && (
            <Notice tone="alert" icon="alert" action={failure.status === 503 ? <button className="btn btn-sm btn-secondary" type="button" onClick={start}>Reintentar</button> : null}>
              <strong>No se pudo comparar.</strong> {comparisonErrorText(failure, totalBytes, APP.maxFileMB)}
            </Notice>
          )}
          <div className="btn-row">
            {!done
              ? <>
                  <button className="btn btn-primary" type="button" onClick={start} disabled={!ready || running}>
                    {running ? <><span className="spinner" aria-hidden="true" />Comparando…</> : <><Icon name="compare" />Comparar versiones</>}
                  </button>
                  {(sides.original.file || sides.modified.file) && !running && <button className="btn btn-ghost" type="button" onClick={reset}>Limpiar</button>}
                </>
              : <button className="btn btn-ghost" type="button" onClick={reset}><Icon name="refresh" />Empezar de nuevo</button>}
          </div>
          {(running || done) && <><h2 className="sub-head">Comprobaciones en el servidor</h2><Pipeline steps={stepsFor(run)} /></>}
        </section>
        <section className="ver-out" aria-live="polite">
          {done ? <Summary run={run} onAgain={reset} /> : <Idle sides={sides} running={running} />}
        </section>
      </div>
      {done && (
        <div className="cmp-results">
          <Warnings result={run.result} />
          <Stats result={run.result} />
          <Changes result={run.result} />
          <MetadataTable result={run.result} />
          <Normalization result={run.result} />
        </div>
      )}
    </>
  );
}
