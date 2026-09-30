// Módulo «Toma física» · imprimir la PLANILLA DE CONTEO con la vista imprimible del navegador: pone
// <html data-print="planilla-conteo"> (así `countPrint.css` muestra SOLO la planilla al imprimir), abre el diálogo de
// impresión y lo quita al terminar. Ctrl+P desde cualquier otra pantalla sigue imprimiendo la página normal.

export const PRINT_ATTRIBUTE = 'data-print';
export const PRINT_VALUE = 'planilla-conteo';

/** Abre la impresión del navegador con la planilla. false si el navegador no puede imprimir. */
export function printCountSheet(): boolean {
  if (typeof window.print !== 'function') return false;
  const root = document.documentElement;
  root.setAttribute(PRINT_ATTRIBUTE, PRINT_VALUE);
  window.addEventListener('afterprint', () => root.removeAttribute(PRINT_ATTRIBUTE), { once: true });
  window.print();
  return true;
}
