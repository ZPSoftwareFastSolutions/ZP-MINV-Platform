// Estado de la carga del catálogo de la tienda (V7 · W3b). Lo publica `CatalogStateProvider` para que la tienda muestre
// «cargando» o el error con «Reintentar» DENTRO de su estructura (`CatalogGate`), sin bloquear el resto del sitio: las
// pantallas de la sesión (ingresar, registrarse, cambiar contraseña, «Mi cuenta») y el panel no dependen del catálogo.

import { createContext } from 'react';
import type { StorefrontError } from '@/1-domain/storefront/errors';

export interface CatalogStatus {
  /** `loading` hasta la primera instantánea; `error` si la primera carga falló; `ready` desde que hay una. */
  status: 'loading' | 'error' | 'ready';
  /** Por qué falló la última carga que se mostró (null si no falló). */
  error: StorefrontError | null;
  /** «Reintentar» está en curso. */
  retrying: boolean;
  /** Vuelve a cargar la instantánea después de un error. */
  retry(): void;
}

export const CatalogStatusContext = createContext<CatalogStatus | null>(null);
