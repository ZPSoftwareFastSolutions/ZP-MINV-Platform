import { createBrowserRouter } from 'react-router-dom';
import { AppShell } from '@/4-presentation/components/layout/AppShell';
import { BuilderPage } from '@/4-presentation/pages/builder/BuilderPage';
import { CatalogPage } from '@/4-presentation/pages/catalog/CatalogPage';
import { HomePage } from '@/4-presentation/pages/home/HomePage';
import { NotFoundPage } from '@/4-presentation/pages/NotFoundPage';
import { ProductPage } from '@/4-presentation/pages/product/ProductPage';
import { RouteErrorPage } from '@/4-presentation/pages/RouteErrorPage';

/** Rutas del catálogo: «/», «/catalogo», «/catalogo/:categoria», «/producto/:slug», «/arma-tu-pc» y 404. */
export const router = createBrowserRouter([
  {
    path: '/',
    element: <AppShell />,
    errorElement: <RouteErrorPage />,
    children: [
      { index: true, element: <HomePage /> },
      { path: 'catalogo', element: <CatalogPage /> },
      { path: 'catalogo/:categoria', element: <CatalogPage /> },
      { path: 'producto/:slug', element: <ProductPage /> },
      { path: 'arma-tu-pc', element: <BuilderPage /> },
      { path: '*', element: <NotFoundPage /> },
    ],
  },
]);
