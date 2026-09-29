// Utilidades de las pruebas de la presentación: orígenes en memoria (mock de la V5 con la pasarela de reservas), sesión
// web en memoria (V7), carrito en memoria (V7) y un render con todos los proveedores (sesión → catálogo → avisos →
// armado → carrito → enrutador en memoria). Solo lo importan las pruebas: ninguna toca la red ni el almacenamiento.

import { render, screen, type RenderResult } from '@testing-library/react';
import type { ReactNode } from 'react';
import { createMemoryRouter, MemoryRouter, Route, RouterProvider, Routes } from 'react-router-dom';
import type { SessionKind } from '@/1-domain/auth/types';
import type { CartItem } from '@/1-domain/cart/types';
import type { CartUseCases } from '@/2-application';
import { createCartServices, createMockSources, createWebServicesFrom, type Sources, type WebGateways, type WebServices } from '@/4-presentation/app/container';
import { createAppRoutes } from '@/4-presentation/app/routeTable';
import { ToastProvider } from '@/4-presentation/components/feedback/ToastProvider';
import { BuilderProvider } from '@/4-presentation/state/BuilderProvider';
import { CartProvider } from '@/4-presentation/state/CartProvider';
import { CatalogProvider } from '@/4-presentation/state/CatalogProvider';
import { SessionProvider } from '@/4-presentation/state/SessionProvider';
import { InMemoryReservationGateway, MOCK_CATALOG, MockCatalogSource } from '@/3-infrastructure/data/mockCatalog';
import { DEMO_USERS, InMemoryWebBackend, type InMemoryWebOptions } from '@/3-infrastructure/data/mockWeb';
import type { InMemoryCatalogData } from '@/3-infrastructure/InMemoryCatalogRepository';
import { InMemoryCartStore } from '@/3-infrastructure/storage/memoryCartStore';

/** Orígenes en memoria sobre el mock (o sobre datos propios): la fuente ve lo que reserva la pasarela. */
export function mockSources(data: InMemoryCatalogData = MOCK_CATALOG): Sources {
  return createMockSources(data, { MockCatalogSource, InMemoryReservationGateway });
}

export interface MockWeb {
  /** Servidor de la sesión en memoria: permite vencer la sesión o exigir el cambio de contraseña. */
  backend: InMemoryWebBackend;
  services: WebServices;
}

/** Sesión web en memoria con los dos usuarios de muestra (personal y cliente), sin nadie ingresado. */
export function mockWeb(options: InMemoryWebOptions = {}): MockWeb {
  const backend = new InMemoryWebBackend({ products: MOCK_CATALOG.products, ...options });
  return { backend, services: createWebServicesFrom({ session: backend.session, rpc: backend.rpc }, 'mock', DEMO_USERS) };
}

/** Servicios de la sesión sobre pasarelas propias (simuladas con `vi.fn`). */
export function webWith(gateways: WebGateways): WebServices {
  return createWebServicesFrom(gateways, 'api');
}

export function demoUser(kind: SessionKind) {
  const user = DEMO_USERS.find((item) => item.kind === kind);
  if (!user) throw new Error(`No hay un usuario de muestra de tipo ${kind}.`);
  return user;
}

/** Sesión web en memoria con el usuario de muestra ya ingresado (como si la cookie siguiera vigente al abrir la página). */
export async function signedInWeb(kind: SessionKind, options: InMemoryWebOptions = {}): Promise<MockWeb> {
  const web = mockWeb(options);
  const user = demoUser(kind);
  await web.backend.session.login({ email: user.email, password: user.password });
  return web;
}

export interface MockCart {
  /** Almacén en memoria: `store.changeFromElsewhere(items)` simula que otra pestaña cambió el carrito. */
  store: InMemoryCartStore;
  cart: CartUseCases;
}

/** Carrito en memoria (por defecto vacío) con las líneas indicadas ya guardadas. */
export function mockCart(items: readonly CartItem[] = [], options: { persistent?: boolean } = {}): MockCart {
  const store = new InMemoryCartStore(items, { persistent: options.persistent ?? true });
  return { store, cart: createCartServices(store) };
}

export interface RenderAppOptions {
  sources?: Sources | Promise<Sources>;
  /** Sesión web (por defecto en memoria y sin nadie ingresado). */
  web?: WebServices | Promise<WebServices>;
  /** Carrito (por defecto en memoria y vacío; cada render estrena el suyo). */
  cart?: CartUseCases;
  /** Ruta inicial del enrutador en memoria. */
  route?: string;
  /** Patrón de la ruta que dibuja `ui` (por ejemplo `/reserva/:numero`); por defecto cualquiera. */
  path?: string;
}

/** Todos los proveedores de la aplicación alrededor de `children` (no es un componente: devuelve el árbol ya armado). */
function withProviders(sources: Sources | Promise<Sources>, web: WebServices | Promise<WebServices>, cart: CartUseCases, children: ReactNode): ReactNode {
  return (
    <SessionProvider web={web}>
      <CatalogProvider sources={sources}>
        <ToastProvider>
          <BuilderProvider>
            <CartProvider cart={cart}>{children}</CartProvider>
          </BuilderProvider>
        </ToastProvider>
      </CatalogProvider>
    </SessionProvider>
  );
}

/** Dibuja `ui` dentro de la aplicación completa y espera a que el catálogo esté listo. */
export async function renderWithApp(
  ui: ReactNode,
  { sources = mockSources(), web = mockWeb().services, cart = mockCart().cart, route = '/', path = '*' }: RenderAppOptions = {},
): Promise<RenderResult> {
  const result = render(
    withProviders(
      sources,
      web,
      cart,
      <MemoryRouter initialEntries={[route]}>
        <Routes>
          <Route path={path} element={withReadyMark(ui)} />
        </Routes>
      </MemoryRouter>,
    ),
  );
  await screen.findByTestId('catalogo-listo');
  return result;
}

export interface RenderRoutesResult extends RenderResult {
  /** Ruta actual del enrutador (`/ingresar?volver=%2Fpanel`). */
  location(): string;
  router: ReturnType<typeof createMemoryRouter>;
}

/**
 * Dibuja la aplicación con la tabla de rutas REAL (guardas, carga diferida y vigilante de la sesión) en un enrutador en
 * memoria, y espera a que el catálogo esté listo.
 */
export async function renderRoutes({
  sources = mockSources(),
  web = mockWeb().services,
  cart = mockCart().cart,
  route = '/',
}: Omit<RenderAppOptions, 'path'> = {}): Promise<RenderRoutesResult> {
  // jsdom no implementa `scrollTo`, que usa <ScrollRestoration> de la tienda: se deja sin efecto para no ensuciar la salida.
  window.scrollTo = (() => undefined) as typeof window.scrollTo;
  const router = createMemoryRouter(createAppRoutes(), { initialEntries: [route] });
  const result = render(
    withProviders(
      sources,
      web,
      cart,
      <>
        <span data-testid="catalogo-listo" hidden />
        <RouterProvider router={router} />
      </>,
    ),
  );
  await screen.findByTestId('catalogo-listo');
  const location = () => `${router.state.location.pathname}${router.state.location.search}${router.state.location.hash}`;
  return Object.assign(result, { router, location });
}

/** Marca invisible que las pruebas esperan para saber que el catálogo ya cargó (no es un componente: es un fragmento). */
function withReadyMark(ui: ReactNode): ReactNode {
  return (
    <>
      <span data-testid="catalogo-listo" hidden />
      {ui}
    </>
  );
}
