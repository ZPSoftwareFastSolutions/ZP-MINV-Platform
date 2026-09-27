import { useEffect } from 'react';
import { DEFAULT_DESCRIPTION, DEFAULT_TITLE, STORE } from '@/shared/constants';

const SUFFIX = ` · ${STORE.shortName} Gaming`;

export interface DocumentMeta {
  /** Título de la página sin el sufijo de marca («Procesadores»); sin título se usa el de la portada. */
  title?: string;
  /** Contenido de `<meta name="description">` para esta ruta. */
  description?: string;
}

/**
 * Título de la pestaña («Procesadores · Tech Zone Gaming») y meta description por página. Cada página fija los suyos;
 * no hace falta restaurar los anteriores al desmontar (una sola asignación por cambio).
 */
export function useDocumentMeta({ title, description }: DocumentMeta = {}) {
  useEffect(() => {
    document.title = title ? `${title}${SUFFIX}` : DEFAULT_TITLE;
    let meta = document.head.querySelector<HTMLMetaElement>('meta[name="description"]');
    if (!meta) {
      meta = document.createElement('meta');
      meta.name = 'description';
      document.head.appendChild(meta);
    }
    meta.content = description ?? DEFAULT_DESCRIPTION;
  }, [title, description]);
}
