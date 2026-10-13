import { useEffect, useState } from 'react';
import './huella.css';
import './huella-extra.css';
import CommandPalette from './components/CommandPalette.jsx';
import ConfirmDialog from './components/ConfirmDialog.jsx';
import ToastProvider from './components/Toasts.jsx';
import { useAuth } from './hooks/useAuth.js';
import { useHashRoute } from './hooks/useHashRoute.js';
import { useTheme } from './hooks/useTheme.js';
import { useToast } from './hooks/useToast.js';
import { authStore } from './lib/session.js';
import Shell, { ViewFrame } from './layout/Shell.jsx';
import ActividadView from './views/ActividadView.jsx';
import AjustesView from './views/AjustesView.jsx';
import CadenaView from './views/CadenaView.jsx';
import DocumentoDetalleView from './views/DocumentoDetalleView.jsx';
import DocumentosView from './views/DocumentosView.jsx';
import LoginView from './views/LoginView.jsx';
import PanelView from './views/PanelView.jsx';
import RegistrarView from './views/RegistrarView.jsx';
import VerificarView from './views/VerificarView.jsx';

const VIEWS = {
  panel: { title: 'Panel', render: (arg, ctx) => <PanelView user={ctx.auth.user} /> },
  registrar: { title: 'Registrar documento', render: () => <RegistrarView /> },
  documentos: { title: 'Documentos', render: (arg) => (arg ? <DocumentoDetalleView id={arg} /> : <DocumentosView />) },
  verificar: { title: 'Verificar documento', render: (arg) => <VerificarView id={arg} /> },
  cadena: { title: 'Blockchain', render: () => <CadenaView /> },
  actividad: { title: 'Actividad', render: () => <ActividadView /> },
  ajustes: { title: 'Ajustes', render: (arg, ctx) => <AjustesView theme={ctx.theme} auth={ctx.auth} /> },
};

function Root() {
  const route = useHashRoute();
  const theme = useTheme();
  const auth = useAuth();
  const toast = useToast();
  const [paletteOpen, setPaletteOpen] = useState(false);
  const [confirmLogout, setConfirmLogout] = useState(false);
  const signedIn = auth.status === 'authenticated';

  // Al cargar la página se pregunta al servidor si la cookie de sesión sigue siendo válida.
  useEffect(() => { authStore.restore(); }, []);

  useEffect(() => {
    const onKey = (e) => {
      if (signedIn && (e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') { e.preventDefault(); setPaletteOpen(true); }
    };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [signedIn]);

  // Todas las vistas son privadas. Sin sesión se muestra el inicio y se conserva la ruta pedida.
  if (auth.status === 'checking') return <div className="boot" role="status">Comprobando tu sesión…</div>;
  if (!signedIn) return <LoginView dark={theme.effective === 'dark'} />;

  const name = VIEWS[route.name] ? route.name : 'panel';
  const view = VIEWS[name];
  const logout = async () => { await authStore.logout(); toast('Sesión cerrada.', 'ok'); };
  return (
    <>
      <Shell active={name} theme={theme} onPalette={() => setPaletteOpen(true)} onLogout={() => setConfirmLogout(true)}>
        <ViewFrame key={`${auth.user.id}/${name}/${route.arg || ''}`} title={view.title}>{view.render(route.arg, { theme, auth })}</ViewFrame>
      </Shell>
      {paletteOpen && <CommandPalette onClose={() => setPaletteOpen(false)} onToggleTheme={theme.toggle} onLogout={() => setConfirmLogout(true)} />}
      {confirmLogout && (
        <ConfirmDialog
          title="Cerrar sesión" confirm="Cerrar sesión"
          text="Tendrás que iniciar sesión con Google de nuevo para ver tu biblioteca, registrar o verificar documentos."
          onConfirm={logout} onClose={() => setConfirmLogout(false)}
        />
      )}
    </>
  );
}

export default function HuellaApp() {
  return <ToastProvider><Root /></ToastProvider>;
}
