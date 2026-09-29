import { RouterProvider } from 'react-router-dom';
import { ToastProvider } from '@/4-presentation/components/feedback/ToastProvider';
import { BuilderProvider } from '@/4-presentation/state/BuilderProvider';
import { CatalogProvider } from '@/4-presentation/state/CatalogProvider';
import { SessionProvider } from '@/4-presentation/state/SessionProvider';
import { createSources, createWebServices } from './container';
import { router } from './router';

/** Origen del catálogo según VITE_API_URL (API de tienda o mock); se elige una sola vez. */
const sources = createSources();
/** Sesión web y RPC (servidor en la nube por `/api/v1/web`, o en memoria con el mock); se eligen una sola vez. */
const web = createWebServices();

/**
 * Composición de la aplicación: sesión web (pregunta por la sesión mientras carga el catálogo) → catálogo (carga y
 * refresco) → servicios → avisos → armado en memoria → enrutador.
 */
export function App() {
  return (
    <SessionProvider web={web}>
      <CatalogProvider sources={sources}>
        <ToastProvider>
          <BuilderProvider>
            <RouterProvider router={router} />
          </BuilderProvider>
        </ToastProvider>
      </CatalogProvider>
    </SessionProvider>
  );
}
