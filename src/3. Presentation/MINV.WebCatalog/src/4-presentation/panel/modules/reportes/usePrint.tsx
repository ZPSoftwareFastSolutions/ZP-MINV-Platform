// Módulo «Reportes» · IMPRIMIR un reporte con la impresión del navegador. `print(hoja)` dibuja la hoja (al final del
// <body>, oculta en la pantalla), marca el <body> para que al imprimir se oculte todo lo demás (el panel, el menú),
// abre el diálogo de impresión y, al volver, quita la hoja y la marca. Así Ctrl+P en cualquier otro momento sigue
// imprimiendo la pantalla normal. Todo con clases de Tailwind (`print:`), sin hojas de estilo propias.

import { useState, type ReactNode } from 'react';
import { createPortal, flushSync } from 'react-dom';

/** Clase del <body>: al imprimir, oculta todo lo que no sea la hoja del reporte. */
const PRINT_ONLY_CLASS = 'print:[&>*:not([data-imprimible])]:hidden';

export interface ReportPrinter {
  /** Imprime la hoja; false si el navegador no puede imprimir. */
  print(sheet: ReactNode): boolean;
  /** Va en la pantalla (dibuja la hoja mientras se imprime). */
  area: ReactNode;
}

export function usePrint(): ReportPrinter {
  const [sheet, setSheet] = useState<ReactNode>(null);

  const print = (next: ReactNode): boolean => {
    if (typeof window.print !== 'function') return false;
    // La hoja tiene que estar en la página ANTES de abrir el diálogo (el navegador toma la página tal como está).
    flushSync(() => setSheet(next));
    document.body.classList.add(PRINT_ONLY_CLASS);
    try {
      window.print();
    } finally {
      document.body.classList.remove(PRINT_ONLY_CLASS);
      setSheet(null);
    }
    return true;
  };

  const area =
    sheet === null
      ? null
      : createPortal(
          <div data-imprimible="" data-testid="area-imprimible" className="hidden bg-white p-6 text-black print:block">
            {sheet}
          </div>,
          document.body,
        );

  return { print, area };
}
