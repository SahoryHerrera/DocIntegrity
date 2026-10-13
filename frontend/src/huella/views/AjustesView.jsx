import { useEffect, useState } from 'react';
import ConfirmDialog from '../components/ConfirmDialog.jsx';
import { Icon } from '../components/Icon.jsx';
import ServiceList from '../components/ServiceList.jsx';
import { Kv, PageHead, Soon } from '../components/ui.jsx';
import { API, APP, ENDPOINTS } from '../config.js';
import { useRequestLog } from '../hooks/useRequestLog.js';
import { useToast } from '../hooks/useToast.js';
import { fmtClock, fmtDate, fmtNum, fmtRel, initials, plural } from '../lib/format.js';
import { authStore } from '../lib/session.js';

const THEMES = [['system', 'Sistema'], ['light', 'Claro'], ['dark', 'Oscuro']];
const shortGuid = (path) => path.replace(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i, (m) => `${m.slice(0, 8)}…`);
const statusTone = (s) => (!s ? 't-alert' : s < 300 ? 't-seal' : s < 500 ? 't-warn' : 't-alert');

function RequestLog() {
  const log = useRequestLog();
  return (
    <div className="panel">
      <header className="panel-head"><h3>Solicitudes recientes</h3><span className="muted small">{plural(log.length, 'solicitud', 'solicitudes')}</span></header>
      {log.length ? (
        <div className="table-wrap"><table className="table table-compact">
          <thead><tr><th>Método</th><th>Ruta</th><th>Estado</th><th className="hide-sm">Tiempo</th><th className="hide-sm">Cuándo</th></tr></thead>
          <tbody>{log.slice(0, 12).map((r) => (
            <tr key={r.at + r.path}>
              <td><span className={`method method-${r.method.toLowerCase()}`}>{r.method}</span></td>
              <td className="mono small">{shortGuid(r.path)}</td>
              <td><span className={`status ${statusTone(r.status)}`}>{r.status || 'Sin respuesta'}</span></td>
              <td className="hide-sm tnum">{fmtNum(r.ms)} ms</td>
              <td className="hide-sm muted">{fmtRel(r.at)}</td>
            </tr>
          ))}</tbody>
        </table></div>
      ) : <p className="muted">Aún no hay solicitudes en esta sesión del navegador.</p>}
    </div>
  );
}

function SessionPanel({ auth }) {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => { const t = setInterval(() => setNow(Date.now()), 1000); return () => clearInterval(t); }, []);
  return (
    <div className="panel">
      <header className="panel-head"><h3>Sesión</h3><span className="jwt-status t-seal"><Icon name="check" />Validada por el servidor</span></header>
      <Kv rows={[
        ['Expira', <>{fmtDate(auth.expiresAt)} <span className="muted">(en <span className="tnum">{fmtClock(auth.expiresAt - now)}</span>)</span></>],
        ['Duración', `${auth.sessionMinutes} minutos desde el inicio de sesión`],
        ['Credencial', 'JWT del servidor en una cookie HttpOnly, Secure y SameSite=Strict. JavaScript no puede leerla, así que no se guarda en el navegador.'],
        ['Protección CSRF', 'Las solicitudes que modifican datos solo se aceptan desde Huella y con la cabecera X-Requested-With.'],
      ]} />
    </div>
  );
}

export default function AjustesView({ theme, auth }) {
  const [confirm, setConfirm] = useState(false);
  const toast = useToast();
  const user = auth.user || {};
  return (
    <>
      <PageHead title="Ajustes" lede="Tu cuenta y tu sesión, el estado de los servicios y la API del sistema." />
      <div className="settings">
        <section className="set-sec" aria-labelledby="s-cuenta">
          <div className="set-intro"><h2 id="s-cuenta">Cuenta y sesión</h2><p>Inicias sesión con Google. El servidor valida el ID token y emite su propio JWT, que viaja en una cookie que JavaScript no puede leer. Tu biblioteca de documentos es privada.</p></div>
          <div className="stack">
            <div className="panel profile">
              <span className="avatar avatar-lg" aria-hidden="true">{initials(user.name)}</span>
              <div><strong>{user.name}</strong><p className="muted">{user.email}, cuenta de Google</p></div>
              <button className="btn btn-secondary" type="button" onClick={() => setConfirm(true)}><Icon name="logout" />Cerrar sesión</button>
            </div>
            <SessionPanel auth={auth} />
          </div>
        </section>
        {confirm && (
          <ConfirmDialog
            title="Cerrar sesión" confirm="Cerrar sesión"
            text="Tendrás que iniciar sesión con Google de nuevo para ver tu biblioteca, registrar o verificar documentos."
            onConfirm={async () => { await authStore.logout(); toast('Sesión cerrada.', 'ok'); }}
            onClose={() => setConfirm(false)}
          />
        )}

        <section className="set-sec" aria-labelledby="s-serv">
          <div className="set-intro"><h2 id="s-serv">Servicios</h2><p>Componentes de la arquitectura por capas.</p></div>
          <div className="stack">
            <div className="panel">
              <header className="panel-head"><h3>Estado</h3><Soon title="Requiere un endpoint de salud en el backend." /></header>
              <ServiceList />
            </div>
            <div className="panel">
              <header className="panel-head"><h3>Configuración</h3></header>
              <Kv rows={[
                ['API', API.baseUrl
                  ? <span className="mono small">{API.baseUrl}</span>
                  : <span className="t-alert">Falta VITE_API_URL en el archivo .env del frontend</span>],
                ['Tiempo de espera', `${API.timeoutMs / 1000} s; ${API.longTimeoutMs / 60000} min al registrar o analizar con IA`],
                ['Tamaño máximo', `${APP.maxFileMB} MB por archivo, solo PDF`],
              ]} />
            </div>
          </div>
        </section>

        <section className="set-sec" aria-labelledby="s-apar">
          <div className="set-intro"><h2 id="s-apar">Apariencia</h2><p>El tema sistema sigue la preferencia de tu dispositivo.</p></div>
          <div className="panel">
            <div className="seg" role="radiogroup" aria-label="Tema">
              {THEMES.map(([k, l]) => <button key={k} type="button" role="radio" aria-checked={theme.theme === k} onClick={() => theme.setTheme(k)}>{l}</button>)}
            </div>
          </div>
        </section>

        <section className="set-sec" aria-labelledby="s-api">
          <div className="set-intro"><h2 id="s-api">API REST</h2><p>Endpoints implementados en el backend de DocIntegrity (ASP.NET Core).</p></div>
          <div className="stack">
            <div className="panel"><div className="table-wrap"><table className="table table-compact">
              <thead><tr><th>Método</th><th>Ruta</th><th className="hide-sm">Función</th></tr></thead>
              <tbody>{ENDPOINTS.map(([m, p, f]) => (
                <tr key={m + p}><td><span className={`method method-${m.toLowerCase()}`}>{m}</span></td><td className="mono small">{p}</td><td className="hide-sm muted">{f}</td></tr>
              ))}</tbody>
            </table></div></div>
            <RequestLog />
          </div>
        </section>
      </div>
    </>
  );
}
