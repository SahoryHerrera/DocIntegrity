import { useEffect, useRef, useState } from 'react';
import { BrandMark, Icon } from '../components/Icon.jsx';
import Glyph from '../components/Glyph.jsx';
import Guilloche from '../components/Guilloche.jsx';
import { HexGroups, Soon } from '../components/ui.jsx';
import { bitDiff } from '../lib/glyph.js';
import { sha256Hex } from '../lib/sha256.js';

const SAMPLE = 'Constancia de notas - Estudiante: Ana López - Calificación final: 85';

/* Prueba de huella: SHA-256 real, calculado en el navegador sobre el texto escrito. */
function HashDemo() {
  const [text, setText] = useState(SAMPLE);
  const [state, setState] = useState({ hash: '', prev: '', first: true, note: 'Cambia una sola letra: cerca de la mitad de los 256 bits cambia.' });
  const last = useRef('');

  useEffect(() => {
    let alive = true;
    sha256Hex(text).then((h) => {
      if (!alive) return;
      const prev = last.current;
      last.current = h;
      setState((s) => ({
        hash: h,
        prev: prev && prev !== h ? prev : s.prev,
        first: !prev,
        note: prev && prev !== h ? `${bitDiff(prev, h)} de 256 bits cambiaron con tu última edición. En rojo, los que cambiaron.` : s.note,
      }));
    });
    return () => { alive = false; };
  }, [text]);

  const diff = !state.first && state.prev ? state.prev : null;
  return (
    <aside className="login-demo" aria-label="Prueba de huella SHA-256">
      <Guilloche className="demo-guilloche" rings={11} inner={40} outer={198} waves={26} amp={8} />
      <div className="demo-glyph">
        {state.hash ? <Glyph hex={state.hash} size={300} spin animate={state.first} diffWith={diff} /> : <Glyph size={300} empty />}
      </div>
      <label className="demo-label" htmlFor="demo-input">Escribe cualquier texto y mira su huella</label>
      <textarea id="demo-input" className="demo-input" rows={2} spellCheck="false" value={text} onChange={(e) => setText(e.target.value)} />
      <div className="demo-hash mono" aria-live="polite">{state.hash && <HexGroups hex={state.hash} other={diff} />}</div>
      <p className="demo-note">{state.note}</p>
    </aside>
  );
}

export default function LoginView() {
  useEffect(() => { document.title = 'Iniciar sesión | Huella'; }, []);
  return (
    <div className="login">
      <section className="login-main">
        <a className="login-brand" href="#/panel"><BrandMark size={30} /><span>Huella</span></a>
        <div className="login-copy">
          <h1 className="login-title">Comprueba que un documento sigue siendo el que registraste.</h1>
          <p>Huella calcula la huella SHA-256 de cada PDF, la registra en blockchain y usa un modelo de IA local para clasificarlo y extraer sus datos.</p>
          {/* El inicio de sesión con Google y el JWT del backend se conectan en la etapa 6. */}
          <button className="btn btn-google" type="button" disabled><Icon name="user" /><span>Continuar con Google</span></button>
          <p className="login-fine"><Soon title="El inicio de sesión se conectará con POST /api/auth/google." /> Tu identidad la confirma Google. Huella no guarda contraseñas.</p>
        </div>
        <ul className="login-facts">
          <li><Icon name="lock" /><span>El archivo no se guarda. Solo su huella y sus metadatos.</span></li>
          <li><Icon name="blocks" /><span>Cada registro queda en una transacción y un bloque consultables.</span></li>
          <li><Icon name="spark" /><span>La IA describe el documento; la criptografía decide si es íntegro.</span></li>
        </ul>
        <p className="login-credit">Proyecto de graduación de Ingeniería en Sistemas Computacionales, UNITEC, 2026.</p>
      </section>
      <HashDemo />
    </div>
  );
}
