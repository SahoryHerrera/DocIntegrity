import { useEffect, useState } from 'react';
import './huella.css';
import './huella-extra.css';
import CommandPalette from './components/CommandPalette.jsx';
import ToastProvider from './components/Toasts.jsx';
import { useHashRoute } from './hooks/useHashRoute.js';
import { useTheme } from './hooks/useTheme.js';
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
  panel: { title: 'Panel', render: () => <PanelView /> },
  registrar: { title: 'Registrar documento', render: () => <RegistrarView /> },
  documentos: { title: 'Documentos', render: (arg) => (arg ? <DocumentoDetalleView id={arg} /> : <DocumentosView />) },
  verificar: { title: 'Verificar documento', render: () => <VerificarView /> },
  cadena: { title: 'Blockchain', render: () => <CadenaView /> },
  actividad: { title: 'Actividad', render: () => <ActividadView /> },
  ajustes: { title: 'Ajustes', render: (arg, ctx) => <AjustesView theme={ctx.theme} /> },
};

function Root() {
  const route = useHashRoute();
  const theme = useTheme();
  const [paletteOpen, setPaletteOpen] = useState(false);

  useEffect(() => {
    const onKey = (e) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') { e.preventDefault(); setPaletteOpen(true); }
    };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, []);

  // Etapa 1: la pantalla de inicio es una ruta más. El acceso con sesión se exige en la etapa 6.
  if (route.name === 'login') return <LoginView />;

  const name = VIEWS[route.name] ? route.name : 'panel';
  const view = VIEWS[name];
  return (
    <>
      <Shell active={name} theme={theme} onPalette={() => setPaletteOpen(true)}>
        <ViewFrame key={`${name}/${route.arg || ''}`} title={view.title}>{view.render(route.arg, { theme })}</ViewFrame>
      </Shell>
      {paletteOpen && <CommandPalette onClose={() => setPaletteOpen(false)} onToggleTheme={theme.toggle} />}
    </>
  );
}

export default function HuellaApp() {
  return <ToastProvider><Root /></ToastProvider>;
}
