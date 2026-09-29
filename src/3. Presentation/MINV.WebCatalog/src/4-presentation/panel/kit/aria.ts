// Ayudas de accesibilidad compartidas por los componentes del panel.

import type { ReactNode } from 'react';

/** Ids de la ayuda y del error de un campo (para `aria-describedby`); undefined si no hay ninguno. */
export function describedBy(id: string, hint: ReactNode, error: ReactNode, extra?: string): string | undefined {
  const ids = [hint ? `${id}-ayuda` : null, error ? `${id}-error` : null, extra ?? null].filter(Boolean);
  return ids.length > 0 ? ids.join(' ') : undefined;
}

/** ¿El clic nació en un control propio (botón, enlace, campo, menú)? Así un clic en la fila no pisa sus controles. */
export function fromInteractive(target: EventTarget | null, container: Element): boolean {
  if (!(target instanceof Element)) return false;
  const interactive = target.closest('a, button, input, select, textarea, label, summary, [role="menu"], [role="menuitem"], [role="switch"], [data-no-row-click]');
  return interactive !== null && container.contains(interactive);
}
