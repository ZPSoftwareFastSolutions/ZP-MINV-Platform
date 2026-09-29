import { useContext } from 'react';
import type { Services } from '@/4-presentation/app/container';
import { ServicesContext } from '@/4-presentation/app/ServicesContext';

/** Casos de uso del catálogo: `const { catalog } = useServices(); catalog.searchCatalog({ q })`. */
export function useServices(): Services {
  const services = useContext(ServicesContext);
  if (!services) throw new Error('useServices() debe usarse dentro de <ServicesProvider>.');
  return services;
}

/**
 * Los casos de uso del catálogo, o null mientras la instantánea no llegó (o si falló). V7 · W3b: para lo que vive fuera
 * de las páginas de la tienda (armado, carrito, estructura del sitio), que se dibuja también sin catálogo.
 */
export function useOptionalServices(): Services | null {
  return useContext(ServicesContext);
}
