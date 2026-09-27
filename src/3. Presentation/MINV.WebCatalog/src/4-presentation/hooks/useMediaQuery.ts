import { useCallback, useSyncExternalStore } from 'react';

function hasMatchMedia(): boolean {
  return typeof window !== 'undefined' && typeof window.matchMedia === 'function';
}

/** `useMediaQuery('(min-width: 1024px)')` → true/false (false en pruebas sin matchMedia). */
export function useMediaQuery(query: string): boolean {
  // La suscripción se memoriza por consulta: si cambiara en cada render, React se volvería a suscribir cada vez.
  const subscribe = useCallback(
    (callback: () => void) => {
      if (!hasMatchMedia()) return () => {};
      const media = window.matchMedia(query);
      media.addEventListener('change', callback);
      return () => media.removeEventListener('change', callback);
    },
    [query],
  );
  return useSyncExternalStore(
    subscribe,
    () => (hasMatchMedia() ? window.matchMedia(query).matches : false),
    () => false,
  );
}

export function usePrefersReducedMotion(): boolean {
  return useMediaQuery('(prefers-reduced-motion: reduce)');
}
