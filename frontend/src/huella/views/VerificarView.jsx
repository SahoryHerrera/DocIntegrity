import { useState } from 'react';
import Dropzone from '../components/Dropzone.jsx';
import Glyph from '../components/Glyph.jsx';
import { Icon } from '../components/Icon.jsx';
import { FileCard, Notice, PageHead, Soon } from '../components/ui.jsx';
import { validatePdf } from '../lib/files.js';
import { fmtBytes } from '../lib/format.js';
import { sha256Hex } from '../lib/sha256.js';

const fresh = () => ({ file: null, sha: null, error: null });

/*
 * Verificación. POST /api/documents/{id}/verify se conecta en la etapa 5.
 * La consulta por huella necesita un endpoint que el backend aún no tiene.
 */
export default function VerificarView() {
  const [ver, setVer] = useState(fresh);

  const onFile = async (file) => {
    const v = await validatePdf(file);
    if (v.error) { setVer({ ...fresh(), error: v.error }); return; }
    const sha = await sha256Hex(v.bytes);
    setVer({ file: { name: file.name, size: file.size }, sha, error: null });
  };
  const reset = () => setVer(fresh());

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
                  <select id="ver-doc" className="input" disabled><option value="">Elige un registro</option></select>
                  <Icon name="chevronDown" />
                </div>
                <p className="field-hint">Los registros se cargarán desde la API de DocIntegrity.</p>
              </div>
            </div>
            <div className="field-step">
              <span className="step-num" aria-hidden="true">2</span>
              <div className="field-step-body">
                <span className="field-label">Archivo a verificar</span>
                {ver.file
                  ? <FileCard name={ver.file.name} sub={fmtBytes(ver.file.size)} onRemove={reset} />
                  : <Dropzone id="ver" title="Arrastra aquí el PDF que recibiste" onFile={onFile} />}
              </div>
            </div>
            {ver.error && <Notice tone="alert" icon="alert">{ver.error}</Notice>}
            <div className="btn-row">
              <button className="btn btn-primary" type="button" disabled><Icon name="shield" />Verificar integridad</button>
              {ver.file && <button className="btn btn-ghost" type="button" onClick={reset}>Limpiar</button>}
              <Soon title="La verificación se conectará con POST /api/documents/{id}/verify." />
            </div>
          </section>
          <section className="ver-out" aria-live="polite">
            <div className="compare compare-idle">
              <figure><Glyph size={156} empty /><figcaption>Huella registrada <span className="muted">H₀</span></figcaption></figure>
              <span className="compare-vs" aria-hidden="true">=</span>
              <figure>{ver.sha ? <Glyph hex={ver.sha} size={156} /> : <Glyph size={156} empty />}<figcaption>Huella presentada <span className="muted">H₁</span></figcaption></figure>
            </div>
            <p className="reg-ph-text center">
              {ver.sha ? 'H₁ es una vista previa calculada en tu navegador. El servidor la recalculará al verificar.' : 'Elige el registro con el que quieres comparar y sube el archivo que recibiste.'}
            </p>
          </section>
        </div>
      </div>
    </>
  );
}
