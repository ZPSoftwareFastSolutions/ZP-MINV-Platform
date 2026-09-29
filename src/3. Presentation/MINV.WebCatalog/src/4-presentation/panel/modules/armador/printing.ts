// Módulo «Armador de PC» · imprimir la cotización con la vista imprimible del navegador: pone <html data-print="cotizacion">
// (así `quotePrint.css` muestra SOLO la cotización al imprimir), abre el diálogo de impresión y lo quita al terminar.

export const PRINT_ATTRIBUTE = 'data-print';
export const PRINT_VALUE = 'cotizacion';

/** Abre la impresión del navegador con la cotización. false si el navegador no puede imprimir. */
export function printQuote(): boolean {
  if (typeof window.print !== 'function') return false;
  const root = document.documentElement;
  root.setAttribute(PRINT_ATTRIBUTE, PRINT_VALUE);
  window.addEventListener('afterprint', () => root.removeAttribute(PRINT_ATTRIBUTE), { once: true });
  window.print();
  return true;
}
