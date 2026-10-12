import { useEffect, useState } from 'react';

const KEY = 'huella.tema';
const darkMQ = window.matchMedia('(prefers-color-scheme: dark)');
const read = () => { try { return localStorage.getItem(KEY) || 'system'; } catch { return 'system'; } };

/* Tema 'system' | 'light' | 'dark'. Se guarda solo en este navegador. */
export function useTheme() {
  const [theme, setTheme] = useState(read);
  const [systemDark, setSystemDark] = useState(darkMQ.matches);

  useEffect(() => {
    const onChange = (e) => setSystemDark(e.matches);
    darkMQ.addEventListener('change', onChange);
    return () => darkMQ.removeEventListener('change', onChange);
  }, []);

  useEffect(() => {
    if (theme === 'system') document.documentElement.removeAttribute('data-theme');
    else document.documentElement.setAttribute('data-theme', theme);
    try { localStorage.setItem(KEY, theme); } catch { /* sin almacenamiento */ }
  }, [theme]);

  const effective = theme === 'system' ? (systemDark ? 'dark' : 'light') : theme;
  const toggle = () => setTheme(effective === 'dark' ? 'light' : 'dark');
  return { theme, setTheme, effective, toggle };
}
