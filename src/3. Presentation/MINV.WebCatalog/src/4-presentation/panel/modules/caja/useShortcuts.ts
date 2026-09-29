// Módulo «Caja» · atajos de teclado de la caja (se muestran en la pantalla): F2 buscar o escanear, F4 cobrar y Esc
// cancelar (borra la búsqueda y vuelve a ella; dentro de una ventana, Esc la cierra: eso lo hace el conjunto del panel).
// Solo mientras la caja está a la vista y no hay una ventana propia abierta.

import { useEffect, useRef } from 'react';

export interface ShortcutHandlers {
  search: () => void;
  charge: () => void;
  escape: () => void;
}

/** ¿La tecla nació dentro de una ventana, un menú o una lista desplegable? (esas manejan su propio Esc). */
function insideLayer(target: EventTarget | null): boolean {
  return target instanceof Element && target.closest('[role="dialog"], [role="alertdialog"], [role="menu"], [role="listbox"]') !== null;
}

export function useShortcuts(handlers: ShortcutHandlers, active: boolean): void {
  const ref = useRef(handlers);
  useEffect(() => {
    ref.current = handlers;
  });

  useEffect(() => {
    if (!active) return;
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.defaultPrevented || event.altKey || event.ctrlKey || event.metaKey || insideLayer(event.target)) return;
      if (event.key === 'F2') {
        event.preventDefault();
        ref.current.search();
      } else if (event.key === 'F4') {
        event.preventDefault();
        ref.current.charge();
      } else if (event.key === 'Escape') {
        ref.current.escape();
      }
    };
    document.addEventListener('keydown', onKeyDown);
    return () => document.removeEventListener('keydown', onKeyDown);
  }, [active]);
}
