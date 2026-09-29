// Utilidades de las pruebas de la presentación: orígenes en memoria (mock de la V5 con la pasarela de reservas), sesión
// web en memoria (V7), carrito en memoria (V7) y un render con todos los proveedores (sesión → catálogo → avisos →
// armado → carrito → enrutador en memoria). V7 · W3a: `renderPanel` dibuja una pantalla del panel (sesión del personal,
// avisos y enrutador en memoria, sin catálogo). Solo lo importan las pruebas: ninguna toca la red ni el almacenamiento.
//
// V7 · W3b: `renderRoutes` arma la aplicación como `App.tsx` (el catálogo ya no bloquea el enrutador: solo las páginas de
// la tienda lo esperan) y por defecto espera a que el catálogo esté listo; con `waitForCatalog: false` no espera (el
// catálogo puede fallar: `failingSources()`). `signedInAs(rol)` ingresa con el usuario de muestra de cada rol (matriz
// `ROLE_PERMISSIONS`) y `preloadPanel()` descarga el panel una vez antes de las pruebas que navegan a él.
//
// La regla de recarga en caliente (solo exportar componentes) no aplica: este archivo es solo de pruebas y nunca se
// recarga en caliente; tiene un componente propio (`CatalogReadyMark`) junto a las funciones que exporta.
/* oxlint-disable react/only-export-components */

import { render, screen, waitFor, type RenderResult } from '@testing-library/react';
import { expect } from 'vitest';
import type { ReactNode } from 'react';
import { createMemoryRouter, MemoryRouter, Route, RouterProvider, Routes } from 'react-router-dom';
import { RequireSession } from '@/4-presentation/components/auth/RequireSession';
import type { SessionKind } from '@/1-domain/auth/types';
import type { CartItem } from '@/1-domain/cart/types';
import type { ICatalogSource } from '@/1-domain/ports/ICatalogSource';
import { StorefrontError } from '@/1-domain/storefront/errors';
import type { CartUseCases } from '@/2-application';
import { createCartServices, createMockSources, createWebServicesFrom, type Sources, type WebGateways, type WebServices } from '@/4-presentation/app/container';
import { createAppRoutes } from '@/4-presentation/app/routeTable';
import { ToastProvider } from '@/4-presentation/components/feedback/ToastProvider';
import { useOptionalServices } from '@/4-presentation/hooks/useServices';
import { BuilderProvider } from '@/4-presentation/state/BuilderProvider';
import { CartProvider } from '@/4-presentation/state/CartProvider';
import { CatalogProvider, CatalogStateProvider } from '@/4-presentation/state/CatalogProvider';
import { SessionProvider } from '@/4-presentation/state/SessionProvider';
import { InMemoryReservationGateway, MOCK_CATALOG, MockCatalogSource } from '@/3-infrastructure/data/mockCatalog';
import { DEMO_USERS, InMemoryWebBackend, ROLE_SAMPLE_USERS, type InMemoryWebOptions, type StaffRole } from '@/3-infrastructure/data/mockWeb';
import type { InMemoryCatalogData } from '@/3-infrastructure/InMemoryCatalogRepository';
import { InMemoryCartStore } from '@/3-infrastructure/storage/memoryCartStore';

export { ROLE_PERMISSIONS, ROLE_SAMPLE_USERS, type StaffRole } from '@/3-infrastructure/data/mockWeb';

/** Orígenes en memoria sobre el mock (o sobre datos propios): la fuente ve lo que reserva la pasarela. */
export function mockSources(data: InMemoryCatalogData = MOCK_CATALOG): Sources {
  return createMockSources(data, { MockCatalogSource, InMemoryReservationGateway });
}

/** Orígenes cuya tienda está caída: la carga del catálogo falla siempre (sin red: responde al instante). */
export function failingSources(): Sources {
  const source: ICatalogSource = {
    load: async () => {
      throw new StorefrontError({ kind: 'network', status: 0, detail: 'No hubo respuesta de la tienda.' });
    },
    product: async () => undefined,
    presets: async () => [],
  };
  return { ...mockSources(), source };
}

/**
 * Descarga el panel (esqueleto, registro y módulos) antes de las pruebas que navegan a `/panel`: es un fragmento grande y
 * su PRIMERA transformación en la prueba puede tardar más que la espera de las búsquedas. `beforeAll(preloadPanel, 30_000)`.
 */
export async function preloadPanel(): Promise<void> {
  await import('@/4-presentation/panel/PanelRoot');
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

/** Usuario de muestra de un rol del personal (el ADMIN es el de `DEMO_USERS`). */
export function roleUser(role: StaffRole) {
  const user = role === 'ADMIN' ? demoUser('staff') : ROLE_SAMPLE_USERS.find((item) => item.role === role);
  if (!user) throw new Error(`No hay un usuario de muestra con el rol ${role}.`);
  return user;
}

/**
 * Sesión web en memoria con el usuario de muestra de ese ROL ya ingresado. El servidor en memoria conoce a todos los
 * usuarios de muestra (los dos de la pantalla de ingreso y uno por rol), así la actividad los nombra a todos.
 */
export async function signedInAs(role: StaffRole, options: InMemoryWebOptions = {}): Promise<MockWeb> {
  const web = mockWeb({ users: [...DEMO_USERS, ...ROLE_SAMPLE_USERS], ...options });
  const user = roleUser(role);
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

/**
 * Todos los proveedores de la aplicación alrededor de `children` (no es un componente: devuelve el árbol ya armado).
 * `gate`: el catálogo espera a cargar antes de dibujar `children` (para dibujar una página suelta de la tienda); sin
 * `gate`, como `App.tsx` (el enrutador se dibuja enseguida y las páginas de la tienda esperan solas).
 */
function withProviders(sources: Sources | Promise<Sources>, web: WebServices | Promise<WebServices>, cart: CartUseCases, children: ReactNode, gate = true): ReactNode {
  const inner = (
    <ToastProvider>
      <BuilderProvider>
        <CartProvider cart={cart}>{children}</CartProvider>
      </BuilderProvider>
    </ToastProvider>
  );
  return <SessionProvider web={web}>{gate ? <CatalogProvider sources={sources}>{inner}</CatalogProvider> : <CatalogStateProvider sources={sources}>{inner}</CatalogStateProvider>}</SessionProvider>;
}

/** Marca invisible que aparece cuando el catálogo está listo (la aplicación ya no espera el catálogo para dibujarse). */
function CatalogReadyMark() {
  return useOptionalServices() ? <span data-testid="catalogo-listo" hidden /> : null;
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

export interface RenderRoutesOptions extends Omit<RenderAppOptions, 'path'> {
  /** Esperar a que el catálogo esté listo (por defecto sí). Con false, la prueba sigue enseguida (el catálogo puede fallar). */
  waitForCatalog?: boolean;
}

/**
 * Dibuja la aplicación con la tabla de rutas REAL (guardas, carga diferida y vigilante de la sesión) en un enrutador en
 * memoria, compuesta como `App.tsx` (el catálogo no bloquea el enrutador), y espera a que el catálogo esté listo.
 */
export async function renderRoutes({
  sources = mockSources(),
  web = mockWeb().services,
  cart = mockCart().cart,
  route = '/',
  waitForCatalog = true,
}: RenderRoutesOptions = {}): Promise<RenderRoutesResult> {
  // jsdom no implementa `scrollTo`, que usa <ScrollRestoration> de la tienda: se deja sin efecto para no ensuciar la salida.
  window.scrollTo = (() => undefined) as typeof window.scrollTo;
  const router = createMemoryRouter(createAppRoutes(), { initialEntries: [route] });
  const result = render(
    withProviders(
      sources,
      web,
      cart,
      <>
        <CatalogReadyMark />
        <RouterProvider router={router} />
      </>,
      false,
    ),
  );
  if (waitForCatalog) await screen.findByTestId('catalogo-listo');
  // La ruta inicial puede ser diferida (se descarga antes de dibujarse): con la suite completa en paralelo, esa primera
  // descarga puede tardar más que la espera de las búsquedas. Se espera a que el enrutador termine su carga inicial.
  if (!router.state.initialized) await waitFor(() => expect(router.state.initialized).toBe(true), { timeout: 15_000 });
  const location = () => `${router.state.location.pathname}${router.state.location.search}${router.state.location.hash}`;
  return Object.assign(result, { router, location });
}

// ==================================================================================================== V7 · panel (W3a)

export interface RenderPanelOptions {
  /** Sesión web (por defecto, la de muestra del personal ya ingresada, con su RPC en memoria). */
  web?: WebServices | Promise<WebServices>;
  /** Ruta inicial del enrutador en memoria (por defecto `/panel`). */
  route?: string;
  /** Patrón de la ruta que dibuja `ui` (por defecto cualquiera). */
  path?: string;
}

export interface RenderPanelResult extends RenderResult {
  /** Ruta actual del enrutador en memoria (`/panel/x?estado=pagada`). */
  location(): string;
  router: ReturnType<typeof createMemoryRouter>;
}

/**
 * Dibuja `ui` como una pantalla del panel: sesión web (detrás de la guarda del personal), avisos y enrutador en
 * memoria. Sin el catálogo de la tienda (el panel no lo usa). Espera a que la guarda deje pasar.
 */
export async function renderPanel(ui: ReactNode, { web, route = '/panel', path = '*' }: RenderPanelOptions = {}): Promise<RenderPanelResult> {
  const services = web ?? (await signedInWeb('staff')).services;
  const router = createMemoryRouter(
    [
      // Si la sesión vence, la guarda manda aquí (sin la pantalla real de ingreso).
      { path: '/ingresar', element: <p data-testid="ingresar">Ingresar</p> },
      {
        path,
        element: (
          <RequireSession kind="staff">
            <span data-testid="panel-listo" hidden />
            {ui}
          </RequireSession>
        ),
      },
    ],
    { initialEntries: [route] },
  );
  const result = render(
    <SessionProvider web={services}>
      <ToastProvider>
        <RouterProvider router={router} />
      </ToastProvider>
    </SessionProvider>,
  );
  await screen.findByTestId('panel-listo');
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
