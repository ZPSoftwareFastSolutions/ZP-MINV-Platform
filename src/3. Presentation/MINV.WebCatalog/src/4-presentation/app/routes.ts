// Rutas del sitio en un solo lugar: las páginas y componentes construyen enlaces con estas funciones.

import { AUTH_PATHS, changePasswordPath, loginPath, registerPath } from '@/2-application/auth/navigation';
import { CART_PATH, CHECKOUT_PATH, checkoutItemPath } from '@/2-application/cart/checkout';

/** Secciones de «Mi cuenta» (`/mi-cuenta/:seccion`). */
export const ACCOUNT_SECTIONS = ['reservas', 'datos', 'contrasena'] as const;
export type AccountSection = (typeof ACCOUNT_SECTIONS)[number];

export const ROUTES = {
  home: '/',
  catalog: '/catalogo',
  category: (slug: string) => `/catalogo/${slug}`,
  product: (slug: string) => `/producto/${slug}`,
  builder: '/arma-tu-pc',
  presets: '/arma-tu-pc#armados',
  offers: '/catalogo?tags=oferta',
  newArrivals: '/catalogo?tags=nuevo',
  search: (q: string) => (q.trim() ? `/catalogo?q=${encodeURIComponent(q.trim())}` : '/catalogo'),
  /** «Consultar mi reserva» (V6): sin número, o con el número ya escrito (`/reserva/ARM-WEB-000001`). */
  reservations: '/reserva',
  reservation: (number: string) => `/reserva/${encodeURIComponent(number)}`,

  // V7 · sesión web. `volver` solo acepta rutas internas (2-application/auth/navigation.ts).
  login: AUTH_PATHS.login,
  loginReturning: (returnTo?: string | null) => loginPath(returnTo),
  register: AUTH_PATHS.register,
  registerReturning: (returnTo?: string | null) => registerPath(returnTo),
  changePassword: AUTH_PATHS.changePassword,
  changePasswordReturning: (returnTo?: string | null) => changePasswordPath(returnTo),
  /** Cuenta del cliente (solo sesión de cliente). */
  account: AUTH_PATHS.account,
  accountSection: (section: AccountSection) => `${AUTH_PATHS.account}/${section}`,
  /** Panel del personal (solo sesión del personal); los módulos cuelgan de `/panel/<módulo>`. */
  panel: AUTH_PATHS.panel,
  panelModule: (path: string) => `${AUTH_PATHS.panel}/${path.replace(/^\/+/, '')}`,
  /** Carrito de compras. */
  cart: CART_PATH,
  /** Reserva del carrito completo. */
  checkout: CHECKOUT_PATH,
  /** «Reservar ahora»: la reserva de ESE solo artículo (`/reservar?sku=…&cantidad=…`), sin pasar por el carrito. */
  checkoutItem: (sku: string, quantity = 1) => checkoutItemPath(sku, quantity),
} as const;

/** Nombre del parámetro de búsqueda en /catalogo. */
export const SEARCH_PARAM = 'q';
