import { useSyncExternalStore } from 'react';
import { authStore } from '../lib/session.js';

/* Estado de la sesión: { status, user, expiresAt, sessionMinutes, pending, error, notice }. */
export const useAuth = () => useSyncExternalStore(authStore.subscribe, authStore.getSnapshot);
