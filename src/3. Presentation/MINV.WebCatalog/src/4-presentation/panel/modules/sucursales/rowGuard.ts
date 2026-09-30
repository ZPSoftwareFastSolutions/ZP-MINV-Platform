// Módulo «Sucursales» · guarda del menú «⋯» de las filas. El menú se dibuja aparte (portal) y React le pasa el
// clic de una opción a la fila, que abriría el detalle (defecto de `kit/aria.ts` con el portal, anotado en el informe
// M7): mientras se atiende una acción del menú, la fila no abre el detalle.

import { useCallback, useRef } from 'react';

export function useRowMenuGuard() {
  const picking = useRef(false);
  /** Envuelve la acción de una opción del menú. */
  const fromMenu = useCallback(
    (action: () => void) => () => {
      picking.current = true;
      action();
      setTimeout(() => {
        picking.current = false;
      }, 0);
    },
    [],
  );
  /** Envuelve la apertura del detalle desde la fila. */
  const guardOpen = useCallback(
    <T>(open: (row: T) => void) =>
      (row: T) => {
        if (!picking.current) open(row);
      },
    [],
  );
  return { fromMenu, guardOpen };
}
