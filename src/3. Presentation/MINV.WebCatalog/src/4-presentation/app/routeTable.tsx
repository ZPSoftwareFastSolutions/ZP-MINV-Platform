// Tabla de rutas del sitio. La tienda pública (V5 y V6) se carga con la página; las pantallas de la V7 (ingresar,
// registrarse, cambiar contraseña, «Mi cuenta», carrito, reserva y panel) se descargan SOLO al visitarlas (carga
// diferida): quien solo mira la tienda no baja el panel.
//
// Rutas protegidas (la guarda `RequireSession` redirige a `/ingresar?volver=…`):
//   /panel/*             sesión del personal   → `panel/PanelRoot` (esqueleto por rol y módulos, paquete W3b)
//   /mi-cuenta/*         sesión de cliente     → `pages/account/AccountPage`
//   /cambiar-contrasena  cualquier sesión
// Rutas públicas nuevas: /ingresar, /registrarse, /carrito (el carrito de compras) y /reservar (la reserva del carrito
// o de un artículo suelto con `?sku=…&cantidad=…`, con o sin cuenta de cliente).
//
// V7 · W3b · El catálogo de la tienda NO bloquea el sitio: solo las páginas de la tienda lo esperan (`CatalogGate`, con el
// esqueleto o el error con «Reintentar» dentro de la estructura de la tienda). Las pantallas de la sesión y el panel se
// abren aunque la tienda esté caída.

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
import { CatalogGate } from '@/4-presentation/state/CatalogProvider';

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
            // V7 · acceso: no dependen del catálogo de la tienda.
            { path: 'ingresar', lazy: lazyLogin },
            { path: 'registrarse', lazy: lazyRegister },
            { element: <RequireSession />, children: [{ path: 'cambiar-contrasena', lazy: lazyChangePassword }] },
            // V7 · cuenta del cliente (tampoco depende del catálogo).
            // Una sola ruta con la sección opcional: cambiar de pestaña no vuelve a montar la página.
            { element: <RequireSession kind="customer" />, children: [{ path: 'mi-cuenta/:seccion?', lazy: lazyAccount }] },
            // La tienda: espera el catálogo (esqueleto o error con «Reintentar» dentro de la estructura de la tienda).
            {
              element: <CatalogGate />,
              children: [
                { index: true, element: <HomePage /> },
                { path: 'catalogo', element: <CatalogPage /> },
                { path: 'catalogo/:categoria', element: <CatalogPage /> },
                { path: 'producto/:slug', element: <ProductPage /> },
                { path: 'arma-tu-pc', element: <BuilderPage /> },
                { path: 'reserva', element: <ReservationPage /> },
                { path: 'reserva/:numero', element: <ReservationPage /> },
                // V7 · carrito y reserva
                { path: 'carrito', lazy: lazyCart },
                { path: 'reservar', lazy: lazyCheckout },
                { path: '*', element: <NotFoundPage /> },
              ],
            },
          ],
        },
        // V7 · panel del personal, con su propia estructura (sin la cabecera de la tienda) y sin depender del catálogo.
        // La guarda es una ruta SIN path (no coincide sola) y el panel toma `/panel` y todo lo que cuelga: dentro usa sus
        // propias rutas (registro de módulos, paquete W3b).
        // `/panel/_componentes` (paquete W3a) es la muestra interna de los componentes del panel: SOLO en desarrollo
        // (en la versión publicada la condición es falsa y el fragmento ni siquiera se genera).
        {
          element: <RequireSession kind="staff" />,
          children: [
            { path: '/panel/*', lazy: lazyPanel },
            ...(import.meta.env.DEV
              ? [
                  {
                    path: '/panel/_componentes',
                    lazy: () => import('@/4-presentation/panel/showcase/ComponentsPage').then((module) => ({ Component: module.ComponentsPage })),
                  },
                ]
              : []),
          ],
        },
      ],
    },
  ];
}
