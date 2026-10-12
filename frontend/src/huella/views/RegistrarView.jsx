import { useState } from 'react';
import Dropzone from '../components/Dropzone.jsx';
import Glyph from '../components/Glyph.jsx';
import { Icon } from '../components/Icon.jsx';
import { FileCard, HexGroups, Notice, PageHead, Soon } from '../components/ui.jsx';
import { validatePdf } from '../lib/files.js';
import { fmtBytes } from '../lib/format.js';
import { sha256Hex } from '../lib/sha256.js';

const fresh = () => ({ file: null, sha: null, error: null });

function Aside({ sha }) {
  return (
    <div className="reg-placeholder">
      <div className="scan">
        {sha ? <Glyph hex={sha} size={220} animate /> : <Glyph size={220} empty />}
      </div>
      <p className="reg-ph-text" aria-live="polite">
        {sha
          ? 'Vista previa calculada en tu navegador. Al registrar, el servidor calculará la huella oficial.'
          : 'La huella de tu documento aparecerá aquí: 256 bits dibujados en ocho anillos.'}
      </p>
      {sha && <div className="reg-ph-hash"><HexGroups hex={sha} /></div>}
    </div>
  );
}

export default function RegistrarView() {
  const [reg, setReg] = useState(fresh);

  const onFile = async (file) => {
    const v = await validatePdf(file);
    if (v.error) { setReg({ ...fresh(), error: v.error }); return; }
    const sha = await sha256Hex(v.bytes);
    setReg({ file: { name: file.name, size: file.size }, sha, error: null });
  };
  const reset = () => setReg(fresh());

  return (
    <>
      <PageHead
        title="Registrar documento"
        lede="Calculamos la huella SHA-256 del PDF, la registramos en blockchain y la IA lo clasifica. Así podrás comprobar más adelante que no cambió."
      />
      <div className="cols cols-reg">
        <section className="reg-left">
          {!reg.file ? (
            <>
              <Dropzone id="reg" title="Arrastra aquí el PDF que quieres registrar" onFile={onFile} />
              {reg.error && <Notice tone="alert" icon="alert">{reg.error}</Notice>}
              <div className="split-facts">
                <div><h3>Lo que se guarda</h3><p>Nombre y tamaño del archivo, su huella SHA-256, la fecha, la transacción y el bloque, y el análisis de la IA.</p></div>
                <div><h3>Lo que no se guarda</h3><p>El archivo. En blockchain solo queda la huella de 32 bytes, que no revela el contenido.</p></div>
              </div>
            </>
          ) : (
            <>
              <FileCard name={reg.file.name} sub={fmtBytes(reg.file.size)} onRemove={reset} />
              <div className="btn-row">
                {/* POST /api/documents/register se conecta en la etapa 4. */}
                <button className="btn btn-primary" type="button" disabled><Icon name="upload" />Registrar documento</button>
                <button className="btn btn-ghost" type="button" onClick={reset}>Elegir otro archivo</button>
                <Soon title="El registro se conectará con POST /api/documents/register." />
              </div>
            </>
          )}
        </section>
        <aside className="reg-aside" aria-live="polite"><Aside sha={reg.sha} /></aside>
      </div>
    </>
  );
}
