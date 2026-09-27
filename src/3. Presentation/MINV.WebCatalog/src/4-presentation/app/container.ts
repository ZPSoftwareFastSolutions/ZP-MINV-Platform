// Composición de dependencias: el ÚNICO archivo de la presentación que importa la infraestructura.
// El resto de la presentación llega a los casos de uso por `useServices()`.

import { createCatalogUseCases, type CatalogUseCases } from '@/2-application';
import { InMemoryCatalogRepository } from '@/3-infrastructure/InMemoryCatalogRepository';

export interface Services {
  catalog: CatalogUseCases;
}

export function createServices(): Services {
  const repository = new InMemoryCatalogRepository();
  return { catalog: createCatalogUseCases(repository) };
}

/** Servicios de la aplicación (mock en memoria; se crean una sola vez). */
export const services: Services = createServices();
