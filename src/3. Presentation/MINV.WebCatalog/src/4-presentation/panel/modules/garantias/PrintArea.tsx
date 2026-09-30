// Módulo «Garantías» · lo que se IMPRIME con «Imprimir» del navegador (la orden de servicio). Mientras está montado: su
// contenido se agrega al final de la página, oculto en la pantalla y visible SOLO al imprimir, y el resto de la página (el
// panel y el diálogo) se oculta al imprimir. Todo con clases de Tailwind (`print:`), sin estilos en línea ni hojas
// propias: la regla que oculta lo demás es una clase del <body> que se quita al desmontar.
//
// En papel el texto va en negro sobre blanco (los colores del tema son para la pantalla oscura: impresos se perderían).

import type { ReactNode } from 'react';
import { useEffect } from 'react';
import { createPortal } from 'react-dom';

/** Clase del <body>: al imprimir, oculta todo lo que no sea el área imprimible. */
const PRINT_ONLY_CLASS = 'print:[&>*:not([data-imprimible])]:hidden';

export interface PrintAreaProps {
  children: ReactNode;
}

export function PrintArea({ children }: PrintAreaProps) {
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
