// Módulo «Documentos fiscales» · lo que se IMPRIME con «Imprimir» del navegador (el documento que se está viendo). Mientras
// está montado, su contenido se agrega al final de la página, oculto en la pantalla y visible SOLO al imprimir; el resto
// de la página se oculta al imprimir (clase del <body> que se quita al desmontar). En papel: negro sobre blanco.

import type { ReactNode } from 'react';
import { useEffect } from 'react';
import { createPortal } from 'react-dom';

/** Clase del <body>: al imprimir, oculta todo lo que no sea el área imprimible. */
const PRINT_ONLY_CLASS = 'print:[&>*:not([data-imprimible])]:hidden';

export function PrintArea({ children }: { children: ReactNode }) {
  useEffect(() => {
    document.body.classList.add(PRINT_ONLY_CLASS);
    return () => document.body.classList.remove(PRINT_ONLY_CLASS);
  }, []);
  return createPortal(
    <div data-imprimible="" data-testid="area-imprimible" className="hidden bg-white p-6 text-black print:block">
      {children}
    </div>,
    document.body,
  );
}
