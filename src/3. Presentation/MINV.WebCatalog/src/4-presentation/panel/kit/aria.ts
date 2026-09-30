// Ayudas de accesibilidad compartidas por los componentes del panel.

import type { ReactNode } from 'react';

/** Ids de la ayuda y del error de un campo (para `aria-describedby`); undefined si no hay ninguno. */
export function describedBy(id: string, hint: ReactNode, error: ReactNode, extra?: string): string | undefined {
  const ids = [hint ? `${id}-ayuda` : null, error ? `${id}-error` : null, extra ?? null].filter(Boolean);
  return ids.length > 0 ? ids.join(' ') : undefined;
}

/**
 * ¿El clic nació en un control propio (botón, enlace, campo, menú)? Así un clic en la fila no pisa sus controles.
 * Un clic que llega de un portal (el menú de acciones de la fila, un diálogo abierto desde ella) está fuera de la fila en el
 * DOM pero React lo propaga igual por el árbol de componentes: tampoco debe abrir la fila.
 */
export function fromInteractive(target: EventTarget | null, container: Element): boolean {
  if (!(target instanceof Element)) return false;
  if (!container.contains(target)) return true;
  const interactive = target.closest('a, button, input, select, textarea, label, summary, [role="menu"], [role="menuitem"], [role="switch"], [data-no-row-click]');
  return interactive !== null && container.contains(interactive);
}
