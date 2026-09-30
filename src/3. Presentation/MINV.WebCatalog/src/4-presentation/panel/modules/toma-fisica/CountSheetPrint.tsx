// Módulo «Toma física» · la PLANILLA DE CONTEO imprimible: los productos del alcance elegido (categoría, ubicación y
// búsqueda), ordenados por posición, con una columna vacía para anotar lo contado a mano y la firma de quien contó. No
// muestra lo que dice el sistema (conteo a ciegas). Se monta fuera de la página (portal en <body>) y solo se ve al
// imprimir (`printing.ts` + `countPrint.css`).

import './countPrint.css';
import { createPortal } from 'react-dom';
import { formatDate, formatDateTime, formatNumber } from '@/4-presentation/panel/lib';
import type { LookupRecord } from './count';

export interface CountSheetPrintProps {
  rows: readonly LookupRecord[];
  /** «CF-20260929-1» o null si todavía no hay una toma abierta. */
  number: string | null;
  countDate: string | null;
  warehouse: string;
  /** Filtros aplicados en palabras («Categoría: Monitores · Posición: CM-A1-01»). */
  scope: string;
  printedAt: Date;
}

export function CountSheetPrint({ rows, number, countDate, warehouse, scope, printedAt }: CountSheetPrintProps) {
  return createPortal(
    <section className="minv-planilla-print" aria-hidden="true" data-testid="planilla-imprimible">
      <header className="minv-planilla-print__header">
        <div>
          <h1>Planilla de conteo{number ? ` · ${number}` : ''}</h1>
          <p>Almacén {warehouse}</p>
          <p>{scope || 'Todos los productos activos'}</p>
        </div>
        <div>
          {countDate && <p>Toma del {formatDate(countDate)}</p>}
          <p>Impresa el {formatDateTime(printedAt)}</p>
          <p>{formatNumber(rows.length)} productos</p>
        </div>
      </header>
      <table>
        <thead>
          <tr>
            <th>N.º</th>
            <th>SKU</th>
            <th>Producto</th>
            <th>Posición</th>
            <th>Unidad</th>
            <th>Contado</th>
            <th>Observación</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((product, index) => (
            <tr key={product.sku}>
              <td>{index + 1}</td>
              <td>{product.sku}</td>
              <td>{product.name}</td>
              <td>{product.primaryBin ?? '—'}</td>
              <td>{product.unit}</td>
              <td className="minv-planilla-print__blank" />
              <td className="minv-planilla-print__blank" />
            </tr>
          ))}
        </tbody>
      </table>
      <footer className="minv-planilla-print__footer">
        <p>Contó (nombre y firma)</p>
        <p>Revisó (nombre y firma)</p>
      </footer>
    </section>,
    document.body,
  );
}
