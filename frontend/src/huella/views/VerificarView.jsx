import { useState } from 'react';
import Dropzone from '../components/Dropzone.jsx';
import Glyph from '../components/Glyph.jsx';
import { Icon } from '../components/Icon.jsx';
import Stamp from '../components/Stamp.jsx';
import { FileCard, HexGroups, Kv, Notice, PageHead, Pipeline, Soon } from '../components/ui.jsx';
import { useDocuments } from '../hooks/useDocuments.js';
import { api } from '../lib/api.js';
import { docTitle } from '../lib/docs.js';
import { validatePdf } from '../lib/files.js';
import { bitDiff } from '../lib/glyph.js';
import { fmtBytes, fmtDate, fmtDay, short } from '../lib/format.js';
import { sha256Hex } from '../lib/sha256.js';
import { VERDICTS, verdictOf } from '../lib/verdicts.js';

/* Pasos de DocumentsController.VerifyDocument, en su orden real. */
const STEPS = [
  { id: 'h0', label: 'Recuperar la huella registrada (H₀)', service: 'PostgreSQL, entidad Document' },
  { id: 'h1', label: 'Calcular la huella del archivo (H₁)', service: 'HashService, SHA-256' },
  { id: 'cmp', label: 'Comparar H₁ con H₀', service: 'Comparación sin distinguir mayúsculas' },
  { id: 'chain', label: 'Consultar el contrato inteligente', service: 'isDocumentRegistered(H₁), lectura sin gas' },
];

const fresh = () => ({ file: null, previewSha: null, fileError: null, status: 'idle', result: null, failure: null });
const isChainFailure = (e) => !!(e && e.data && typeof e.data.code === 'string' && e.data.code.startsWith('blockchain_'));

function stepsFor(ver) {
  if (ver.status === 'done') {
    const v = ver.result; const n = bitDiff(v.registeredSha256, v.currentSha256);
    const detail = {
      h0: { detail: v.registeredSha256, mono: true },
      h1: { detail: v.currentSha256, mono: true },
      cmp: { tone: v.hashMatches ? 'ok' : 'bad', detail: v.hashMatches ? 'H₁ = H₀. Las huellas son idénticas.' : `H₁ ≠ H₀. Difieren en ${n} de 256 bits.` },
      chain: { tone: v.registeredInBlockchain ? 'ok' : 'bad', detail: `isDocumentRegistered(H₁) devolvió ${v.registeredInBlockchain}.` },
    };
    return STEPS.map((s) => ({ ...s, state: 'done', ...detail[s.id] }));
  }
  // El backend solo consulta la cadena después de recuperar, calcular y comparar las huellas.
  if (ver.status === 'incomplete') {
    return STEPS.map((s) => (s.id === 'chain' ? { ...s, state: 'error', detail: ver.failure.message } : { ...s, state: 'done' }));
  }
  return STEPS.map((s) => ({ ...s, state: 'pending' }));
}

function errorText(e) {
  if (e.kind === 'network') return 'No se pudo conectar con la API de DocIntegrity. Comprueba que el backend esté en ejecución.';
  if (e.kind === 'timeout') return 'El servidor no respondió a tiempo. Inténtalo de nuevo.';
  return e.message;
}

function VerdictCard({ verdict, date, animate }) {
  const V = VERDICTS[verdict];
  return (
    <div className={`verdict verdict-${V.tone} ${animate ? 'is-new' : ''}`}>
      <span className="verdict-stamp"><Stamp tone={V.tone} title={V.stamp} ring={V.ring} sub={fmtDay(date).toUpperCase()} size={112} animate={animate} label={`Sello: ${V.title}`} /></span>
      <div><h2>{V.title}</h2><p>{V.text}</p><p className="verdict-next">{V.next}</p></div>
    </div>
  );
}

function Result({ ver, docs, onUse, onAgain }) {
  const v = ver.result; const verdict = verdictOf(v);
  const n = bitDiff(v.registeredSha256, v.currentSha256);
  const other = docs.find((d) => d.sha256 === v.currentSha256 && d.id !== v.documentId);
  return (
    <>
      <VerdictCard verdict={verdict} date={ver.at} animate />
      {verdict === 'distinto' && (other
        ? <Notice tone="info" icon="info" action={<button className="btn btn-sm btn-secondary" type="button" onClick={() => onUse(other.id)}>Usar ese registro</button>}>El archivo corresponde a <a className="link" href={`#/documentos/${other.id}`}>{docTitle(other)}</a>, registrado el {fmtDate(other.registeredAt)}.</Notice>
        : <Notice tone="info" icon="info">La huella está en la blockchain activa, pero no corresponde a ningún documento de tu biblioteca.</Notice>)}
      {verdict === 'alterado' && other && (
        <Notice tone="info" icon="info">El archivo coincide con otro documento de tu biblioteca, <a className="link" href={`#/documentos/${other.id}`}>{docTitle(other)}</a>, que tampoco tiene evidencia en la blockchain activa.</Notice>
      )}
      <div className="compare">
        <figure><Glyph hex={v.registeredSha256} size={156} /><figcaption>Huella registrada <span className="muted">H₀</span></figcaption></figure>
        <span className={`compare-vs ${n ? 't-alert' : 't-seal'}`} aria-hidden="true">{n ? '≠' : '='}</span>
        <figure><Glyph hex={v.currentSha256} size={156} diffWith={v.registeredSha256} animate={n > 0} /><figcaption>Huella presentada <span className="muted">H₁</span></figcaption></figure>
      </div>
      <p className="bits">{n === 0 ? 'Los 256 bits coinciden.' : `${n} de 256 bits son distintos (${Math.round((n / 256) * 100)} %). En rojo, los bits que cambiaron.`}</p>
      <div className="hexcmp">
        <div><span className="hexcmp-k">H₀</span><HexGroups hex={v.registeredSha256} /></div>
        <div><span className="hexcmp-k">H₁</span><HexGroups hex={v.currentSha256} other={v.registeredSha256} /></div>
      </div>
      <Kv rows={[
        ['¿Coincide la huella?', v.hashMatches ? <span className="t-seal">Sí</span> : <span className="t-alert">No</span>],
        ['¿Huella en la blockchain activa?', v.registeredInBlockchain ? <span className="t-seal">Sí</span> : <span className="t-alert">No</span>],
        ['Archivo presentado', `${v.fileName}, ${fmtBytes(ver.file.size)}`],
        ['Verificado', fmtDate(ver.at)],
      ]} />
      <div className="btn-row"><button className="btn btn-ghost" type="button" onClick={onAgain}>Verificar otro archivo</button></div>
    </>
  );
}

function Idle({ ver, sel }) {
  const running = ver.status === 'running';
  const incomplete = ver.status === 'incomplete';
  return (
    <>
      {incomplete && <VerdictCard verdict="incompleta" date={ver.at} animate />}
      <div className="compare compare-idle">
        <figure>{sel ? <Glyph hex={sel.sha256} size={156} spin={running} /> : <Glyph size={156} empty />}<figcaption>Huella registrada <span className="muted">H₀</span></figcaption></figure>
        <span className="compare-vs" aria-hidden="true">{running ? <span className="spinner" /> : incomplete ? '?' : '='}</span>
        <figure>{ver.previewSha ? <Glyph hex={ver.previewSha} size={156} spin={running} /> : <Glyph size={156} empty />}<figcaption>Huella presentada <span className="muted">H₁{ver.previewSha ? ', vista previa' : ''}</span></figcaption></figure>
      </div>
      <p className="reg-ph-text center">
        {running ? 'El servidor está calculando H₁, comparándola con H₀ y consultando el contrato…'
          : ver.status === 'incomplete' ? 'Sin respuesta de la blockchain no se puede emitir un veredicto.'
          : !sel ? 'Elige el registro con el que quieres comparar.'
          : !ver.file ? 'Ahora sube el archivo que recibiste.'
          : 'H₁ es una vista previa calculada en tu navegador. El servidor la recalculará al verificar.'}
      </p>
    </>
  );
}

export default function VerificarView({ id }) {
  const docs = useDocuments();
  const [docId, setDocId] = useState(id || '');
  const [ver, setVer] = useState(fresh);
  const sel = docs.items.find((d) => d.id === docId) || null;
  const loadingDocs = docs.status === 'loading' || docs.status === 'idle';
  const running = ver.status === 'running';

  const selectDoc = (value) => {
    setDocId(value);
    setVer((s) => ({ ...fresh(), file: s.file, previewSha: s.previewSha }));
    // Actualiza la URL sin volver a montar la vista.
    history.replaceState(null, '', value ? `#/verificar/${value}` : '#/verificar');
  };
  const onFile = async (file) => {
    const v = await validatePdf(file);
    if (v.error) { setVer({ ...fresh(), fileError: v.error }); return; }
    const previewSha = await sha256Hex(v.bytes);
    setVer({ ...fresh(), file, previewSha });
  };
  const clearFile = () => setVer(fresh());
  const start = async () => {
    if (!sel || !ver.file || running) return;
    setVer((s) => ({ ...s, status: 'running', result: null, failure: null }));
    try {
      const result = await api.documents.verify(docId, ver.file);
      setVer((s) => ({ ...s, status: 'done', result, at: new Date().toISOString() }));
    } catch (e) {
      setVer((s) => ({ ...s, status: isChainFailure(e) ? 'incomplete' : 'error', failure: e, at: new Date().toISOString() }));
    }
  };

  const suggest = ver.previewSha && ver.status === 'idle' ? docs.items.find((d) => d.sha256 === ver.previewSha) : null;
  const showSteps = running || ver.status === 'done' || ver.status === 'incomplete';

  return (
    <>
      <PageHead
        title="Verificar documento"
        lede="Compara el archivo que recibiste con su registro. El resultado lo deciden la huella SHA-256 y el contrato inteligente; la IA no interviene."
      />
      <div className="tabs" role="tablist" aria-label="Forma de verificar">
        <button role="tab" id="tab-file" type="button" aria-selected="true" aria-controls="ver-panel">Con el archivo</button>
        <button role="tab" id="tab-hash" type="button" aria-selected="false" disabled title="Requiere un endpoint de consulta por huella en el backend.">Con la huella <Soon /></button>
      </div>
      <div id="ver-panel" role="tabpanel" aria-labelledby="tab-file">
        <div className="cols cols-ver">
          <section className="ver-form">
            <div className="field-step">
              <span className="step-num" aria-hidden="true">1</span>
              <div className="field-step-body">
                <label className="field-label" htmlFor="ver-doc">Documento registrado</label>
                <div className="select-wrap">
                  <select id="ver-doc" className="input" value={sel ? docId : ''} onChange={(e) => selectDoc(e.target.value)} disabled={!docs.items.length || running}>
                    <option value="">{loadingDocs ? 'Cargando registros…' : docs.items.length ? 'Elige un registro' : 'No hay registros'}</option>
                    {docs.items.map((d) => <option key={d.id} value={d.id}>{docTitle(d)}, {fmtDay(d.registeredAt)}</option>)}
                  </select>
                  <Icon name="chevronDown" />
                </div>
                {sel && <p className="field-hint">{sel.fileName}. Huella <span className="mono">{short(sel.sha256, 10, 6)}</span>.</p>}
                {docs.status === 'error' && <p className="field-error">{docs.error.message} <button className="link" type="button" onClick={docs.reload}>Reintentar</button></p>}
                {docs.status === 'ready' && docId && !sel && <p className="field-error">El registro indicado en el enlace no existe.</p>}
              </div>
            </div>
            <div className="field-step">
              <span className="step-num" aria-hidden="true">2</span>
              <div className="field-step-body">
                <span className="field-label">Archivo a verificar</span>
                {ver.file
                  ? <FileCard name={ver.file.name} sub={fmtBytes(ver.file.size)} onRemove={running ? null : clearFile} />
                  : <Dropzone id="ver" title="Arrastra aquí el PDF que recibiste" onFile={onFile} />}
                {suggest && suggest.id !== docId && (
                  <Notice tone="info" icon="info" action={<button className="btn btn-sm btn-secondary" type="button" onClick={() => selectDoc(suggest.id)}>Usar ese registro</button>}>
                    Este archivo coincide con el registro «{docTitle(suggest)}».
                  </Notice>
                )}
              </div>
            </div>
            {ver.fileError && <Notice tone="alert" icon="alert">{ver.fileError}</Notice>}
            {ver.status === 'error' && <Notice tone="alert" icon="alert"><strong>No se pudo verificar.</strong> {errorText(ver.failure)}</Notice>}
            {ver.status !== 'done' ? (
              <div className="btn-row">
                <button className="btn btn-primary" type="button" onClick={start} disabled={!sel || !ver.file || running}>
                  {running ? <><span className="spinner" aria-hidden="true" />Verificando…</> : <><Icon name="shield" />Verificar integridad</>}
                </button>
                {(ver.file || docId) && !running && <button className="btn btn-ghost" type="button" onClick={() => { clearFile(); selectDoc(''); }}>Limpiar</button>}
              </div>
            ) : (
              <div className="btn-row"><button className="btn btn-ghost" type="button" onClick={() => { clearFile(); selectDoc(''); }}><Icon name="refresh" />Empezar de nuevo</button></div>
            )}
            {showSteps && <><h2 className="sub-head">Comprobaciones en el servidor</h2><Pipeline steps={stepsFor(ver)} /></>}
          </section>
          <section className="ver-out" aria-live="polite">
            {ver.status === 'done'
              ? <Result ver={ver} docs={docs.items} onUse={selectDoc} onAgain={clearFile} />
              : <Idle ver={ver} sel={sel} />}
          </section>
        </div>
      </div>
    </>
  );
}
