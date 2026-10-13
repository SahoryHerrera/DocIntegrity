import { useEffect, useState, useSyncExternalStore } from 'react';
import Dropzone from '../components/Dropzone.jsx';
import Glyph from '../components/Glyph.jsx';
import { Icon } from '../components/Icon.jsx';
import Stamp from '../components/Stamp.jsx';
import { AiBlock } from '../components/docs.jsx';
import { Chip, FileCard, HashBlock, HexGroups, Kv, Notice, PageHead, Pipeline } from '../components/ui.jsx';
import { docTitle } from '../lib/docs.js';
import { fmtBytes, fmtClock, fmtDate, fmtDay, fmtNum, plural } from '../lib/format.js';
import { registerStore } from '../lib/registerStore.js';

/* Pasos de DocumentsController.RegisterDocument, en su orden real. */
const STEPS = [
  { id: 'hash', label: 'Calcular la huella SHA-256', service: 'HashService' },
  { id: 'dup', label: 'Comprobar tu biblioteca y la blockchain', service: 'PostgreSQL e isDocumentRegistered(bytes32)' },
  { id: 'text', label: 'Extraer el texto del PDF', service: 'PdfTextExtractorService con PdfPig' },
  { id: 'ai', label: 'Clasificar y extraer metadatos', service: 'OllamaAiService, modelo local' },
  { id: 'chain', label: 'Registrar la huella en blockchain', service: 'BlockchainService, registerDocument(bytes32)' },
  { id: 'db', label: 'Guardar el registro', service: 'PostgreSQL, entidad Document' },
];

/*
 * El backend procesa todo en una sola solicitud y no informa del avance, así
 * que mientras corre ningún paso se marca como completado. Al terminar, cada
 * paso muestra lo que devolvió el servidor; si falla la IA se sabe en qué paso
 * fue, porque la IA solo corre después de los tres primeros.
 */
function stepsFor(r) {
  if (r.status === 'done') {
    const d = r.result;
    const detail = {
      hash: { detail: d.sha256, mono: true },
      dup: { detail: d.blockchainEvidenceReused ? 'No estaba en tu biblioteca. La huella ya estaba en la blockchain activa.' : 'No estaba en tu biblioteca ni en la blockchain activa.' },
      text: {},
      ai: { detail: d.ai ? `${d.ai.documentType || 'Sin tipo'}. ${plural(Object.keys(d.ai.metadata).length, 'metadato extraído', 'metadatos extraídos')}.` : 'El servidor no devolvió análisis.' },
      chain: { detail: d.blockchainEvidenceReused
        ? `No se envió otra transacción: se reutilizó la ya confirmada en el bloque ${fmtNum(d.blockchainBlockNumber)}.`
        : `Confirmada en el bloque ${fmtNum(d.blockchainBlockNumber)}.` },
      db: { detail: `Registro ${d.id} guardado.` },
    };
    return STEPS.map((s) => ({ ...s, state: 'done', ...detail[s.id] }));
  }
  const code = r.error && r.error.data && r.error.data.code;
  if (r.status === 'error' && code && code.startsWith('ai_')) {
    return STEPS.map((s, i) => (i < 3 ? { ...s, state: 'done' } : s.id === 'ai' ? { ...s, state: 'error', detail: r.error.message } : { ...s, state: 'skipped' }));
  }
  return STEPS.map((s) => ({ ...s, state: 'pending' }));
}

function Elapsed({ since }) {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => { const t = setInterval(() => setNow(Date.now()), 1000); return () => clearInterval(t); }, []);
  return <span className="tnum">{fmtClock(now - since)}</span>;
}

function errorText(error) {
  if (error.kind === 'timeout') return 'El servidor no respondió a tiempo. Es posible que el registro se haya completado: revisa Documentos antes de intentarlo otra vez.';
  if (error.kind === 'network') return 'No se pudo conectar con la API de DocIntegrity. Comprueba que el backend esté en ejecución.';
  return error.message;
}

function ResultAside({ r, animate }) {
  const d = r.result;
  const mismatch = r.previewSha && r.previewSha !== d.sha256;
  return (
    <div className="reg-result">
      <div className="reg-glyph">
        <Glyph hex={d.sha256} size={220} animate={animate} />
        {/* La transacción se acaba de confirmar en la red activa: el backend esperó el recibo. */}
        <span className="reg-stamp"><Stamp tone="seal" title="REGISTRADO" ring={`Huella anclada en blockchain · Bloque ${d.blockchainBlockNumber} · `} sub={fmtDay(d.registeredAt).toUpperCase()} size={104} animate={animate} label={`Sello: registrado en el bloque ${d.blockchainBlockNumber}`} /></span>
      </div>
      <div className="reg-status"><Chip tone="seal">Registrado en blockchain</Chip><span className="muted small">{fmtDate(d.registeredAt)}</span></div>
      <h2 className="reg-title">{docTitle(d)}</h2>
      {mismatch && <Notice tone="warn" icon="alert">La huella oficial del servidor no coincide con la vista previa calculada en el navegador. Se muestra la del servidor.</Notice>}
      {d.blockchainEvidenceReused && (
        <Notice tone="info" icon="info">
          <strong>Esta huella ya existía en la blockchain.</strong> El archivo idéntico se había registrado antes, así que se reutilizó la transacción confirmada en lugar de enviar otra. El documento queda en tu biblioteca privada.
        </Notice>
      )}
      <Kv rows={[
        ['Huella SHA-256', <HashBlock value={d.sha256} />],
        ['Transacción', <HashBlock value={d.blockchainTransactionHash} msg="Transacción copiada" label="Copiar transacción" />],
        ['Bloque', <span className="tnum">{fmtNum(d.blockchainBlockNumber)}</span>],
      ]} />
      <AiBlock doc={d} compact />
      <div className="btn-row">
        <a className="btn btn-primary" href={`#/documentos/${d.id}`}>Abrir el registro</a>
        <button className="btn btn-ghost" type="button" onClick={registerStore.reset}>Registrar otro</button>
      </div>
    </div>
  );
}

function DuplicateAside({ c }) {
  return (
    <div className="reg-placeholder">
      {c.sha256 ? <Glyph hex={c.sha256} size={200} /> : <Glyph size={200} empty />}
      <Notice tone="info" icon="info">
        <strong>{c.message}</strong>{' '}
        Un mismo archivo siempre produce la misma huella, así que no se guarda dos veces en tu biblioteca.
      </Notice>
      <div className="btn-row">
        {c.documentId && <a className="btn btn-primary" href={`#/documentos/${c.documentId}`}>Abrir el registro</a>}
        <button className="btn btn-ghost" type="button" onClick={registerStore.reset}>Registrar otro</button>
      </div>
    </div>
  );
}

function PlaceholderAside({ r }) {
  const running = r.status === 'running';
  return (
    <div className={`reg-placeholder ${running ? 'is-working' : ''}`}>
      <div className={`scan ${running ? 'is-scanning' : ''}`}>
        {r.previewSha ? <Glyph hex={r.previewSha} size={220} spin={running} /> : <Glyph size={220} empty />}
      </div>
      <p className="reg-ph-text" aria-live="polite">
        {running ? 'El servidor está calculando la huella oficial, analizando el documento y registrándolo en blockchain…'
          : r.previewSha ? 'Vista previa calculada en tu navegador. La huella oficial la calcula el servidor al registrar.'
          : 'La huella de tu documento aparecerá aquí: 256 bits dibujados en ocho anillos.'}
      </p>
      {r.previewSha && <div className="reg-ph-hash"><HexGroups hex={r.previewSha} /></div>}
    </div>
  );
}

export default function RegistrarView() {
  // Un resultado que ya se vio no se vuelve a mostrar al regresar a esta vista.
  useState(() => { registerStore.leave(); return null; });
  const r = useSyncExternalStore(registerStore.subscribe, registerStore.getSnapshot);
  const animate = !r.seen;
  useEffect(() => { registerStore.markSeen(); }, [r.status]);

  const running = r.status === 'running';
  const showSteps = running || r.status === 'done' || (r.status === 'error' && r.error && r.error.data && String(r.error.data.code || '').startsWith('ai_'));

  return (
    <>
      <PageHead
        title="Registrar documento"
        lede="Calculamos la huella SHA-256 del PDF, la registramos en blockchain y la IA lo clasifica. Así podrás comprobar más adelante que no cambió."
      />
      <div className="cols cols-reg">
        <section className="reg-left">
          {!r.file ? (
            <>
              <Dropzone id="reg" title="Arrastra aquí el PDF que quieres registrar" onFile={registerStore.choose} />
              {r.error && r.error.kind === 'validation' && <Notice tone="alert" icon="alert">{r.error.message}</Notice>}
              <div className="split-facts">
                <div><h3>Lo que se guarda</h3><p>Nombre y tamaño del archivo, su huella SHA-256, la fecha, la transacción y el bloque, y el análisis de la IA.</p></div>
                <div><h3>Lo que no se guarda</h3><p>El archivo. En blockchain solo queda la huella de 32 bytes, que no revela el contenido.</p></div>
              </div>
            </>
          ) : (
            <>
              <FileCard name={r.file.name} sub={fmtBytes(r.file.size)} onRemove={r.status === 'ready' ? registerStore.reset : null} />
              {r.status === 'ready' && (
                <>
                  <p className="field-hint">El servidor analiza el documento con IA local antes de registrarlo; puede tardar varios minutos.</p>
                  <div className="btn-row">
                    <button className="btn btn-primary" type="button" onClick={registerStore.start}><Icon name="upload" />Registrar documento</button>
                    <button className="btn btn-ghost" type="button" onClick={registerStore.reset}>Elegir otro archivo</button>
                  </div>
                </>
              )}
              {r.status === 'error' && (
                <Notice tone="alert" icon="alert" action={<button className="btn btn-sm btn-secondary" type="button" onClick={registerStore.reset}>Empezar de nuevo</button>}>
                  <strong>No se pudo registrar.</strong> {errorText(r.error)}
                </Notice>
              )}
              {showSteps && (
                <>
                  <h2 className="sub-head">{running ? <>En el servidor · <Elapsed since={r.startedAt} /></> : `En el servidor · ${fmtClock(r.finishedAt - r.startedAt)}`}</h2>
                  {running && <p className="field-hint">El backend procesa todo en una sola solicitud y no informa del avance de cada paso.</p>}
                  <Pipeline steps={stepsFor(r)} />
                </>
              )}
            </>
          )}
        </section>
        <aside className="reg-aside" aria-live="polite">
          {r.status === 'done' ? <ResultAside r={r} animate={animate} />
            : r.status === 'duplicate' ? <DuplicateAside c={r.conflict} />
            : <PlaceholderAside r={r} />}
        </aside>
      </div>
    </>
  );
}
