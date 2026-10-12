import { useEffect, useState } from 'react';

const parse = () => {
  const [name = '', arg] = location.hash.replace(/^#\/?/, '').split('/');
  return { name, arg: arg || null };
};

/* Rutas con hash (#/panel, #/documentos/{id}…), igual que el prototipo. */
export function useHashRoute() {
  const [route, setRoute] = useState(parse);
  useEffect(() => {
    const onChange = () => setRoute(parse());
    window.addEventListener('hashchange', onChange);
    return () => window.removeEventListener('hashchange', onChange);
  }, []);
  return route;
}
