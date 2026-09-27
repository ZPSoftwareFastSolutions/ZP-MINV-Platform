import { useEffect } from 'react';
import { STORE } from '@/shared/constants';

/** Título de la pestaña: «Procesadores · Tech Zone». */
export function useDocumentTitle(title?: string) {
  useEffect(() => {
    const previous = document.title;
    document.title = title ? `${title} · ${STORE.shortName}` : `${STORE.shortName} Gaming · Armá tu PC`;
    return () => {
      document.title = previous;
    };
  }, [title]);
}
