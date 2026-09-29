// Composición de dependencias: el ÚNICO archivo de la presentación que importa la infraestructura.
// `createSources()` elige el origen según VITE_API_URL (la API pública de tienda, o el mock de la V5 con «mock»);
// `createServices()` arma los casos de uso sobre una instantánea ya cargada. El resto de la presentación llega a todo
// por `useServices()`; CatalogProvider es quien carga la instantánea y la refresca.
//
// V7: `createWebServices()` arma la sesión web, el RPC y la cuenta del cliente (servidor en la nube por `/api/v1/web`, o
// todo en memoria con «mock»). La presentación los usa con `useSession()`, `useRpc()` y `useAccount()`. El contrato
// generado también sale a la presentación por aquí (ver `./contract.ts`).
//
// V7 · carrito: `createCartServices()` arma los casos de uso del carrito sobre el almacenamiento del navegador (o la
// memoria, si el navegador no deja guardar). La presentación lo usa con `useCart()`.
//
// V7 · modo mock: la tienda (reservas sin sesión) y la cuenta del cliente (reservas por RPC) comparten UNA pasarela de
// reservas en memoria, así lo que reserva cualquiera de las dos vías baja lo disponible que muestra el catálogo.

import type { SessionKind } from '@/1-domain/auth/types';
import type { Product } from '@/1-domain/catalog/types';
import type { ICartStore } from '@/1-domain/ports/ICartStore';
import type { ICatalogSource } from '@/1-domain/ports/ICatalogSource';
import type { IReservationGateway } from '@/1-domain/ports/IReservationGateway';
import type { IRpcGateway } from '@/1-domain/ports/IRpcGateway';
import type { ISessionGateway } from '@/1-domain/ports/ISessionGateway';
import type { CatalogSnapshot } from '@/1-domain/storefront/types';
import {
  createAccountUseCases,
  createCartUseCases,
  createCatalogUseCases,
  createReservationUseCases,
  createSessionUseCases,
  watchSession,
  type AccountUseCases,
  type CartUseCases,
  type CatalogUseCases,
  type ReservationUseCases,
  type SessionUseCases,
} from '@/2-application';
import { ACCOUNT_OPERATIONS, type RpcOperations } from '@/3-infrastructure/http/contract';
import { HttpCatalogSource } from '@/3-infrastructure/http/HttpCatalogSource';
import { HttpReservationGateway } from '@/3-infrastructure/http/HttpReservationGateway';
import { HttpRpcGateway } from '@/3-infrastructure/http/HttpRpcGateway';
import { HttpSessionGateway } from '@/3-infrastructure/http/HttpSessionGateway';
import { RpcAccountGateway } from '@/3-infrastructure/http/RpcAccountGateway';
import { isMockApiUrl, StorefrontApi } from '@/3-infrastructure/http/api';
import { WebApi } from '@/3-infrastructure/http/webApi';
import { createCartStore } from '@/3-infrastructure/storage/cartStorage';
import { InMemoryCatalogRepository, type InMemoryCatalogData } from '@/3-infrastructure/InMemoryCatalogRepository';

// Contrato generado del servidor (tipos de cada petición y respuesta, operaciones, permisos y roles): la presentación
// lo importa desde `@/4-presentation/app/contract`, que reexporta esto. Nadie declara a mano un tipo del servidor (P-07).
export type * from '@/3-infrastructure/http/contract';
export {
  ACCOUNT_OPERATIONS,
  PERMISSION_LIST,
  ROLE_LIST,
  RPC_OPERATIONS,
  isRpcOperation,
  permissionName,
  roleName,
  rpcOperation,
} from '@/3-infrastructure/http/contract';

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
  /**
   * Vuelve a cargar la instantánea (sin parpadeo: la anterior sigue visible hasta que llegue la nueva). Devuelve si
   * llegó una nueva; con falso la consulta falló y se conserva la anterior (el carrito avisa que no pudo actualizar).
   */
  refresh(): Promise<boolean>;
}

/**
 * Elige el origen según `VITE_API_URL`: «mock» carga el mock de la V5 en un fragmento aparte (solo se descarga en ese
 * modo); cualquier otro valor (o ninguno → http://localhost:5090) es la base del API Gateway.
 */
export async function createSources(apiUrl: string | undefined = import.meta.env.VITE_API_URL): Promise<Sources> {
  if (isMockApiUrl(apiUrl)) {
    const { mock, gateway } = await mockWorld();
    return createMockSources(mock.MOCK_CATALOG, mock, gateway);
  }
  // El valor crudo: StorefrontApi lo resuelve UNA vez («/» = mismo origen). Resolverlo aquí antes lo convertía en vacío y el
  // constructor lo tomaba como «sin configurar» (volvía a http://localhost:5090 y la tienda pública no cargaba).
  const api = new StorefrontApi(apiUrl ?? '');
  return { mode: 'api', apiUrl: api.baseUrl, source: new HttpCatalogSource(api), gateway: new HttpReservationGateway(api) };
}

type MockModule = typeof import('@/3-infrastructure/data/mockCatalog');
type MockReservationGateway = InstanceType<MockModule['InMemoryReservationGateway']>;

interface MockWorld {
  mock: MockModule;
  /** La pasarela de reservas del modo mock: una sola para la tienda y para la cuenta del cliente. */
  gateway: MockReservationGateway;
}

let mockWorldPromise: Promise<MockWorld> | null = null;

/** Datos del modo mock (fragmento aparte) y su única pasarela de reservas, creados una vez por página. */
function mockWorld(): Promise<MockWorld> {
  mockWorldPromise ??= import('@/3-infrastructure/data/mockCatalog').then((mock) => ({ mock, gateway: new mock.InMemoryReservationGateway(mock.MOCK_CATALOG) }));
  return mockWorldPromise;
}

/**
 * Fuente y pasarela en memoria sobre unos datos (el mock de la V5 o datos de prueba): comparten la disponibilidad. Se
 * puede pasar la pasarela para que otra vía (la cuenta del cliente del modo mock) reserve sobre el mismo stock.
 */
export function createMockSources(
  data: InMemoryCatalogData,
  mock: Pick<MockModule, 'MockCatalogSource' | 'InMemoryReservationGateway'>,
  gateway: MockReservationGateway = new mock.InMemoryReservationGateway(data),
): Sources {
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
export function createServices(snapshot: CatalogSnapshot, sources: Sources, refresh: () => Promise<boolean>): Services {
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

// ==================================================================================================== V7 · sesión web

/** RPC tipado con las operaciones del contrato: `rpc.send('GetMyAccountQuery', {})`. */
export type WebRpc = IRpcGateway<RpcOperations>;

/** Pasarelas de la sesión web ya elegidas (servidor en la nube o memoria). */
export interface WebGateways {
  session: ISessionGateway;
  rpc: WebRpc;
}

/** Usuario de muestra del modo mock (la pantalla de ingreso lo ofrece en la demostración). */
export interface DemoUser {
  kind: SessionKind;
  label: string;
  name: string;
  email: string;
  password: string;
}

export interface WebServices {
  mode: CatalogMode;
  session: SessionUseCases;
  /** RPC VIGILADO: si el servidor responde 401, avisa a quien escuche `onSessionExpired`. */
  rpc: WebRpc;
  account: AccountUseCases;
  /** Avisa cuando un pedido descubre que la sesión venció. Devuelve cómo dejar de escuchar. */
  onSessionExpired(listener: () => void): () => void;
  /** Usuarios de muestra (solo en modo mock; vacío contra el servidor). */
  demoUsers: readonly DemoUser[];
}

/** Arma los casos de uso de la sesión sobre unas pasarelas (las pruebas pasan las suyas). */
export function createWebServicesFrom(gateways: WebGateways, mode: CatalogMode, demoUsers: readonly DemoUser[] = []): WebServices {
  const listeners = new Set<() => void>();
  const rpc = watchSession(gateways.rpc, {
    isSessionAlive: async () => (await gateways.session.current()) !== null,
    onExpired: () => {
      for (const listener of [...listeners]) listener();
    },
    // Cambiar la contraseña responde 401 cuando la contraseña ACTUAL es incorrecta: no es una sesión vencida.
    credentialOperations: [ACCOUNT_OPERATIONS.changePassword],
  });
  return {
    mode,
    session: createSessionUseCases(gateways.session),
    rpc,
    account: createAccountUseCases(new RpcAccountGateway(rpc)),
    onSessionExpired(listener) {
      listeners.add(listener);
      return () => {
        listeners.delete(listener);
      };
    },
    demoUsers,
  };
}

/**
 * Sesión web según `VITE_API_URL`: «mock» usa una sesión y un RPC en memoria con dos usuarios de muestra (fragmento
 * aparte); cualquier otro valor usa el servidor en la nube por `/api/v1/web`, SIEMPRE en el mismo origen de la página
 * (el nginx del catálogo lo reenvía; en desarrollo, el proxy de Vite).
 */
export async function createWebServices(apiUrl: string | undefined = import.meta.env.VITE_API_URL): Promise<WebServices> {
  if (isMockApiUrl(apiUrl)) {
    const [web, world] = await Promise.all([import('@/3-infrastructure/data/mockWeb'), mockWorld()]);
    // Las reservas de la cuenta descuentan el MISMO stock que las de la tienda (y que ve el catálogo). V7 · W3b: además de
    // los dos usuarios que ofrece la pantalla de ingreso, el servidor en memoria conoce un usuario del personal por rol
    // (bodega@, ventas@, cajero@, gerencia@ y consulta@techzone.example) para recorrer el panel con cada rol.
    const backend = new web.InMemoryWebBackend({
      products: world.mock.MOCK_CATALOG.products,
      stock: world.gateway,
      users: [...web.DEMO_USERS, ...web.ROLE_SAMPLE_USERS],
    });
    return createWebServicesFrom({ session: backend.session, rpc: backend.rpc }, 'mock', web.DEMO_USERS);
  }
  const api = new WebApi();
  return createWebServicesFrom({ session: new HttpSessionGateway(api), rpc: new HttpRpcGateway(api) }, 'api');
}

// ======================================================================================================== V7 · carrito

/**
 * Carrito de compras sobre un almacén: por defecto el almacenamiento del navegador (o la memoria si el navegador no lo
 * ofrece o lo bloquea). Las pruebas pasan un almacén en memoria.
 */
export function createCartServices(store: ICartStore = createCartStore()): CartUseCases {
  return createCartUseCases(store);
}
