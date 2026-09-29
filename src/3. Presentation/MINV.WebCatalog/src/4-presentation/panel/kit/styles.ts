// Clases compartidas por los componentes del panel: TODOS los campos se ven iguales (alto de 44 px, borde de control con
// contraste ≥ 3:1, foco con el contorno global). Salen de los tokens del tema (src/index.css).

import { FIELD_CLASSES } from '@/4-presentation/components/ui/TextField';

/** Control de texto, lista o área de texto (el mismo que usa la tienda). */
export const CONTROL = FIELD_CLASSES;
export const LABEL = 'block text-sm font-medium text-text';
export const HINT = 'mt-1 text-xs text-text-faint';
export const ERROR = 'mt-1 text-sm text-danger-text';
/** Casilla y opción: tamaño visible de 20 px dentro de un objetivo táctil de 44 px (la etiqueta completa es clicable). */
export const TICK = 'mt-0.5 size-5 shrink-0 cursor-pointer rounded border-border-control accent-primary disabled:cursor-not-allowed';
/** Botón de solo ícono dentro de un campo (limpiar, desplegar). */
export const FIELD_ICON_BUTTON =
  'flex size-11 shrink-0 cursor-pointer items-center justify-center rounded-xl text-text-muted transition-colors duration-200 hover:text-text disabled:cursor-not-allowed disabled:opacity-50 [&_svg]:size-4';
/** Superficie flotante (listas desplegables y menús). */
export const POPOVER = 'rounded-xl border border-border-strong bg-surface-2 p-1 shadow-card';
/** Opción de una lista o de un menú. */
export const OPTION =
  'flex min-h-11 w-full cursor-pointer items-center gap-3 rounded-lg px-3 py-2 text-left text-sm text-text transition-colors duration-150 [&_svg]:size-4 [&_svg]:shrink-0';
