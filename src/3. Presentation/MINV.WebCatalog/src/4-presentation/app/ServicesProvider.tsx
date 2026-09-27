import type { ReactNode } from 'react';
import type { Services } from './container';
import { ServicesContext } from './ServicesContext';

interface ServicesProviderProps {
  services: Services;
  children: ReactNode;
}

/** Entrega los casos de uso a toda la presentación (en pruebas se puede inyectar otro repositorio). */
export function ServicesProvider({ services, children }: ServicesProviderProps) {
  return <ServicesContext.Provider value={services}>{children}</ServicesContext.Provider>;
}
