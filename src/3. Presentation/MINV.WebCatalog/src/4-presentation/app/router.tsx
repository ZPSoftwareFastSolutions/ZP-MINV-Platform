import { createBrowserRouter } from 'react-router-dom';
import { createAppRoutes } from './routeTable';

/**
 * Rutas del sitio (definidas en routeTable.tsx): la tienda («/», «/catalogo», «/catalogo/:categoria», «/producto/:slug»,
 * «/arma-tu-pc», «/reserva[/:numero]»), el acceso («/ingresar», «/registrarse», «/cambiar-contrasena»), la cuenta del
 * cliente («/mi-cuenta/*»), el carrito («/carrito», «/reservar»), el panel del personal («/panel/*») y 404.
 */
export const router = createBrowserRouter(createAppRoutes());
