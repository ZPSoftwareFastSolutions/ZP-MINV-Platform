import { useSyncExternalStore } from 'react';

function subscribe(query: string, callback: () => void): () => void {
  const media = window.matchMedia(query);
  media.addEventListener('change', callback);
  return () => media.removeEventListener('change', callback);
}

/** `useMediaQuery('(min-width: 1024px)')` → true/false (false en pruebas sin matchMedia). */
export function useMediaQuery(query: string): boolean {
  return useSyncExternalStore(
    (callback) => (typeof window.matchMedia === 'function' ? subscribe(query, callback) : () => {}),
    () => (typeof window.matchMedia === 'function' ? window.matchMedia(query).matches : false),
    () => false,
  );
}

export function usePrefersReducedMotion(): boolean {
  return useMediaQuery('(prefers-reduced-motion: reduce)');
}
