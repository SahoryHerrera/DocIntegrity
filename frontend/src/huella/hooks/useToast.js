import { createContext, useContext } from 'react';

export const ToastContext = createContext(() => {});

/* toast(mensaje, tono = 'neutral' | 'ok' | 'warn' | 'alert', { timeout }) */
export const useToast = () => useContext(ToastContext);
