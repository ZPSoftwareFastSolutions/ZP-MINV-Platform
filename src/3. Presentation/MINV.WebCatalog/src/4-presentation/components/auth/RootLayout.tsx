import { Outlet } from 'react-router-dom';
import { SessionEndWatcher } from './SessionEndWatcher';

/** Raíz de todas las rutas: vigila el fin de la sesión (vencida o cerrada) y dibuja la ruta que corresponda. */
export function RootLayout() {
  return (
    <>
      <SessionEndWatcher />
      <Outlet />
    </>
  );
}
