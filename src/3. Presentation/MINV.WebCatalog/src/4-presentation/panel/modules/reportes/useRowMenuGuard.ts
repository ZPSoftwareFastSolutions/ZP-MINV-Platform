// Módulo «Reportes» · guarda del menú «⋯» de una fila. El menú se dibuja aparte (portal) y React igual le pasa el clic a
// la fila, que abriría el detalle encima de la acción elegida. Mientras se atiende una opción del menú, el clic de la
// fila no hace nada (defecto de `kit/aria.ts`: anotado en «Pendientes» del informe M9).
//
//   const menu = useRowMenuGuard();
//   onRowOpen={(fila) => { if (!menu.busy()) abrir(fila); }}
//   rowActions={(fila) => [{ label: 'Ver detalle', onSelect: menu.guard(() => abrir(fila)) }]}

import { useRef } from 'react';

export interface RowMenuGuard {
  /** Envuelve la acción de una opción del menú. */
  guard(action: () => void): () => void;
  /** ¿Se está atendiendo una opción del menú? (el clic de la fila se ignora). */
  busy(): boolean;
}

export function useRowMenuGuard(): RowMenuGuard {
  const picking = useRef(false);
  return {
    guard: (action) => () => {
      picking.current = true;
      action();
      setTimeout(() => {
        picking.current = false;
      }, 0);
    },
    busy: () => picking.current,
  };
}
