import { useEffect, useRef, useState } from 'react';
import { GoogleLogin } from '@react-oauth/google';
import { BrandMark, Icon } from '../components/Icon.jsx';
import Glyph from '../components/Glyph.jsx';
import Guilloche from '../components/Guilloche.jsx';
import { HexGroups, Notice } from '../components/ui.jsx';
import { useAuth } from '../hooks/useAuth.js';
import { authStore } from '../lib/session.js';

const GOOGLE_CLIENT_ID = (import.meta.env ?? {}).VITE_GOOGLE_CLIENT_ID;
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

/* Botón oficial de Google y estados del inicio de sesión. */
function GoogleSignIn({ auth, dark }) {
  if (!GOOGLE_CLIENT_ID) {
    return <Notice tone="alert" icon="alert">Falta configurar <span className="mono">VITE_GOOGLE_CLIENT_ID</span> en el archivo .env del frontend.</Notice>;
  }
  if (auth.status === 'unavailable') {
    return (
      <Notice tone="alert" icon="alert" action={<button className="btn btn-sm btn-secondary" type="button" onClick={authStore.restore}><Icon name="refresh" />Reintentar</button>}>
        <strong>No se pudo comprobar tu sesión.</strong> {auth.error && auth.error.kind === 'network' ? 'No se pudo conectar con la API de DocIntegrity.' : auth.error && auth.error.message}
      </Notice>
    );
  }
  return (
    <>
      {auth.notice && <Notice tone={auth.notice.tone} icon={auth.notice.tone === 'warn' ? 'clock' : 'check'}>{auth.notice.text}</Notice>}
      {auth.error && <Notice tone="alert" icon="alert"><strong>No se pudo iniciar sesión.</strong> {auth.error.kind === 'network' ? 'No se pudo conectar con la API de DocIntegrity.' : auth.error.message}</Notice>}
      <div className="login-google" aria-busy={auth.pending}>
        {auth.pending
          ? <p className="login-pending"><span className="spinner" aria-hidden="true" /> Validando tu identidad con el servidor…</p>
          : <GoogleLogin
              onSuccess={(response) => authStore.loginWithGoogle(response.credential)}
              onError={authStore.googleFailed}
              theme={dark ? 'filled_black' : 'outline'}
              size="large" shape="pill" text="continue_with" locale="es" width="300"
            />}
      </div>
    </>
  );
}

export default function LoginView({ dark }) {
  const auth = useAuth();
  useEffect(() => { document.title = 'Iniciar sesión | Huella'; }, []);
  return (
    <div className="login">
      <section className="login-main">
        <span className="login-brand"><BrandMark size={30} /><span>Huella</span></span>
        <div className="login-copy">
          <h1 className="login-title">Comprueba que un documento sigue siendo el que registraste.</h1>
          <p>Huella calcula la huella SHA-256 de cada PDF, la registra en blockchain y usa un modelo de IA local para clasificarlo y extraer sus datos.</p>
          <GoogleSignIn auth={auth} dark={dark} />
          <p className="login-fine">Tu identidad la confirma Google. Huella no guarda contraseñas, tu biblioteca es privada y tu sesión dura {auth.sessionMinutes} minutos.</p>
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
