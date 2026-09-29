import { RouterProvider } from 'react-router-dom';
import { ToastProvider } from '@/4-presentation/components/feedback/ToastProvider';
import { BuilderProvider } from '@/4-presentation/state/BuilderProvider';
import { CartProvider } from '@/4-presentation/state/CartProvider';
import { CatalogProvider } from '@/4-presentation/state/CatalogProvider';
import { SessionProvider } from '@/4-presentation/state/SessionProvider';
import { createCartServices, createSources, createWebServices } from './container';
import { router } from './router';

/** Origen del catálogo según VITE_API_URL (API de tienda o mock); se elige una sola vez. */
const sources = createSources();
/** Sesión web y RPC (servidor en la nube por `/api/v1/web`, o en memoria con el mock); se eligen una sola vez. */
const web = createWebServices();
/** Carrito de compras guardado en este navegador (o en memoria si el navegador no deja guardar); uno solo por página. */
const cart = createCartServices();

/**
 * Composición de la aplicación: sesión web (pregunta por la sesión mientras carga el catálogo) → catálogo (carga y
 * refresco) → servicios → avisos → armado en memoria → carrito → enrutador.
 */
export function App() {
  return (
    <SessionProvider web={web}>
      <CatalogProvider sources={sources}>
        <ToastProvider>
          <BuilderProvider>
            <CartProvider cart={cart}>
              <RouterProvider router={router} />
            </CartProvider>
          </BuilderProvider>
        </ToastProvider>
      </CatalogProvider>
    </SessionProvider>
  );
}
