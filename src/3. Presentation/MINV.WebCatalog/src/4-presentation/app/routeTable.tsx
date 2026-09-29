// Tabla de rutas del sitio. La tienda pública (V5 y V6) se carga con la página; las pantallas de la V7 (ingresar,
// registrarse, cambiar contraseña, «Mi cuenta», carrito, reserva y panel) se descargan SOLO al visitarlas (carga
// diferida): quien solo mira la tienda no baja el panel.
//
// Rutas protegidas (la guarda `RequireSession` redirige a `/ingresar?volver=…`):
//   /panel/*             sesión del personal   → `panel/PanelRoot` (lo construye el paquete W3)
//   /mi-cuenta/*         sesión de cliente     → `pages/account/AccountPage`
//   /cambiar-contrasena  cualquier sesión
// Rutas públicas nuevas: /ingresar, /registrarse, /carrito (el carrito de compras) y /reservar (provisional: la
// construye el paquete siguiente; recibe el carrito o un artículo suelto con `?sku=…&cantidad=…`).

import type { RouteObject } from 'react-router-dom';
import { RequireSession } from '@/4-presentation/components/auth/RequireSession';
import { RootLayout } from '@/4-presentation/components/auth/RootLayout';
import { RouteLoading } from '@/4-presentation/components/feedback/RouteLoading';
import { AppShell } from '@/4-presentation/components/layout/AppShell';
import { BuilderPage } from '@/4-presentation/pages/builder/BuilderPage';
import { CatalogPage } from '@/4-presentation/pages/catalog/CatalogPage';
import { HomePage } from '@/4-presentation/pages/home/HomePage';
import { NotFoundPage } from '@/4-presentation/pages/NotFoundPage';
import { ProductPage } from '@/4-presentation/pages/product/ProductPage';
import { ReservationPage } from '@/4-presentation/pages/reservation/ReservationPage';
import { RouteErrorPage } from '@/4-presentation/pages/RouteErrorPage';

const lazyLogin = () => import('@/4-presentation/pages/auth/LoginPage').then((module) => ({ Component: module.LoginPage }));
const lazyRegister = () => import('@/4-presentation/pages/auth/RegisterPage').then((module) => ({ Component: module.RegisterPage }));
const lazyChangePassword = () => import('@/4-presentation/pages/auth/ChangePasswordPage').then((module) => ({ Component: module.ChangePasswordPage }));
const lazyAccount = () => import('@/4-presentation/pages/account/AccountPage').then((module) => ({ Component: module.AccountPage }));
const lazyCart = () => import('@/4-presentation/pages/cart/CartPage').then((module) => ({ Component: module.CartPage }));
const lazyCheckout = () => import('@/4-presentation/pages/cart/CheckoutPage').then((module) => ({ Component: module.CheckoutPage }));
const lazyPanel = () => import('@/4-presentation/panel/PanelRoot').then((module) => ({ Component: module.PanelRoot }));

/** Rutas del sitio (las pruebas las montan en un enrutador en memoria). */
export function createAppRoutes(): RouteObject[] {
  return [
    {
      element: <RootLayout />,
      errorElement: <RouteErrorPage />,
      HydrateFallback: RouteLoading,
      children: [
        {
          path: '/',
          element: <AppShell />,
          children: [
            { index: true, element: <HomePage /> },
            { path: 'catalogo', element: <CatalogPage /> },
            { path: 'catalogo/:categoria', element: <CatalogPage /> },
            { path: 'producto/:slug', element: <ProductPage /> },
            { path: 'arma-tu-pc', element: <BuilderPage /> },
            { path: 'reserva', element: <ReservationPage /> },
            { path: 'reserva/:numero', element: <ReservationPage /> },
            // V7 · acceso
            { path: 'ingresar', lazy: lazyLogin },
            { path: 'registrarse', lazy: lazyRegister },
            { element: <RequireSession />, children: [{ path: 'cambiar-contrasena', lazy: lazyChangePassword }] },
            // V7 · cuenta del cliente
            // Una sola ruta con la sección opcional: cambiar de pestaña no vuelve a montar la página.
            { element: <RequireSession kind="customer" />, children: [{ path: 'mi-cuenta/:seccion?', lazy: lazyAccount }] },
            // V7 · carrito y reserva
            { path: 'carrito', lazy: lazyCart },
            { path: 'reservar', lazy: lazyCheckout },
            { path: '*', element: <NotFoundPage /> },
          ],
        },
        // V7 · panel del personal, con su propia estructura (sin la cabecera de la tienda). La guarda es una ruta SIN
        // path (no coincide sola) y el panel toma `/panel` y todo lo que cuelga: dentro usa sus propias rutas.
        { element: <RequireSession kind="staff" />, children: [{ path: '/panel/*', lazy: lazyPanel }] },
      ],
    },
  ];
}
