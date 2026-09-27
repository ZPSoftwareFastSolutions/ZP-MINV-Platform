import { useContext } from 'react';
import type { Services } from '@/4-presentation/app/container';
import { ServicesContext } from '@/4-presentation/app/ServicesContext';

/** Casos de uso del catálogo: `const { catalog } = useServices(); catalog.searchCatalog({ q })`. */
export function useServices(): Services {
  const services = useContext(ServicesContext);
  if (!services) throw new Error('useServices() debe usarse dentro de <ServicesProvider>.');
  return services;
}
