import { RouterProvider } from 'react-router-dom';
import { ToastProvider } from '@/4-presentation/components/feedback/ToastProvider';
import { BuilderProvider } from '@/4-presentation/state/BuilderProvider';
import { CatalogProvider } from '@/4-presentation/state/CatalogProvider';
import { createSources } from './container';
import { router } from './router';

/** Origen del catálogo según VITE_API_URL (API de tienda o mock); se elige una sola vez. */
const sources = createSources();

/** Composición de la aplicación: catálogo (carga y refresco) → servicios → avisos → armado en memoria → enrutador. */
export function App() {
  return (
    <CatalogProvider sources={sources}>
      <ToastProvider>
        <BuilderProvider>
          <RouterProvider router={router} />
        </BuilderProvider>
      </ToastProvider>
    </CatalogProvider>
  );
}
