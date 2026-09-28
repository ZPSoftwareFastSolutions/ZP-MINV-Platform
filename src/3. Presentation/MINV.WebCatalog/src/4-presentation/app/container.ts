// Composición de dependencias: el ÚNICO archivo de la presentación que importa la infraestructura.
// `createSources()` elige el origen según VITE_API_URL (la API pública de tienda, o el mock de la V5 con «mock»);
// `createServices()` arma los casos de uso sobre una instantánea ya cargada. El resto de la presentación llega a todo
// por `useServices()`; CatalogProvider es quien carga la instantánea y la refresca.

import type { Product } from '@/1-domain/catalog/types';
import type { ICatalogSource } from '@/1-domain/ports/ICatalogSource';
import type { IReservationGateway } from '@/1-domain/ports/IReservationGateway';
import type { CatalogSnapshot } from '@/1-domain/storefront/types';
import { createCatalogUseCases, createReservationUseCases, type CatalogUseCases, type ReservationUseCases } from '@/2-application';
import { HttpCatalogSource } from '@/3-infrastructure/http/HttpCatalogSource';
import { HttpReservationGateway } from '@/3-infrastructure/http/HttpReservationGateway';
import { isMockApiUrl, resolveApiUrl, StorefrontApi } from '@/3-infrastructure/http/api';
import { InMemoryCatalogRepository, type InMemoryCatalogData } from '@/3-infrastructure/InMemoryCatalogRepository';

export type CatalogMode = 'api' | 'mock';

/** Origen del catálogo y pasarela de reservas ya elegidos (API o mock). */
export interface Sources {
  mode: CatalogMode;
  /** Base de la API (vacía en modo mock). */
  apiUrl: string;
  source: ICatalogSource;
  gateway: IReservationGateway;
}

/** Consultas que sí van a la red después de la carga inicial (disponibilidad fresca de una ficha). */
export interface LiveCatalog {
  product(slug: string): Promise<Product | undefined>;
}

export interface Services {
  catalog: CatalogUseCases;
  reservations: ReservationUseCases;
  live: LiveCatalog;
  mode: CatalogMode;
  /** Cuándo se generó la instantánea que alimenta `catalog`. */
  generatedAt: Date;
  /** Vuelve a cargar la instantánea (sin parpadeo: la anterior sigue visible hasta que llegue la nueva). */
  refresh(): Promise<void>;
}

/**
 * Elige el origen según `VITE_API_URL`: «mock» carga el mock de la V5 en un fragmento aparte (solo se descarga en ese
 * modo); cualquier otro valor (o ninguno → http://localhost:5090) es la base del API Gateway.
 */
export async function createSources(apiUrl: string | undefined = import.meta.env.VITE_API_URL): Promise<Sources> {
  if (isMockApiUrl(apiUrl)) {
    const mock = await import('@/3-infrastructure/data/mockCatalog');
    return createMockSources(mock.MOCK_CATALOG, mock);
  }
  const api = new StorefrontApi(resolveApiUrl(apiUrl));
  return { mode: 'api', apiUrl: api.baseUrl, source: new HttpCatalogSource(api), gateway: new HttpReservationGateway(api) };
}

type MockModule = typeof import('@/3-infrastructure/data/mockCatalog');

/** Fuente y pasarela en memoria sobre unos datos (el mock de la V5 o datos de prueba): comparten la disponibilidad. */
export function createMockSources(data: InMemoryCatalogData, mock: Pick<MockModule, 'MockCatalogSource' | 'InMemoryReservationGateway'>): Sources {
  const gateway = new mock.InMemoryReservationGateway(data);
  // La fuente lee la disponibilidad desde la pasarela: lo reservado en memoria se refleja al refrescar y en la ficha.
  const source = new mock.MockCatalogSource({
    ...data,
    get products() {
      return data.products.map((product) => gateway.product(product.sku) ?? product);
    },
  });
  return { mode: 'mock', apiUrl: '', source, gateway };
}

/** Casos de uso sobre una instantánea ya cargada. */
export function createServices(snapshot: CatalogSnapshot, sources: Sources, refresh: () => Promise<void>): Services {
  const repository = InMemoryCatalogRepository.fromSnapshot(snapshot);
  return {
    catalog: createCatalogUseCases(repository),
    reservations: createReservationUseCases(sources.gateway),
    live: { product: (slug) => sources.source.product(slug) },
    mode: sources.mode,
    generatedAt: snapshot.generatedAt,
    refresh,
  };
}
