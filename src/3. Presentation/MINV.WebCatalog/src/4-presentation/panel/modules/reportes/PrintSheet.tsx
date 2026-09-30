// Módulo «Reportes» · la HOJA IMPRESA de un reporte: empresa, nombre del reporte, período y alcance, los totales y la
// tabla COMPLETA (todas las filas filtradas, no solo la página que se ve). Texto negro sobre blanco (los colores del tema
// son para la pantalla oscura). La muestra `usePrint` solo al imprimir.

import { formatDateTime } from '@/4-presentation/panel/lib';
import type { ReportColumn } from './columns';

export interface PrintSummaryItem {
  label: string;
  value: string;
}

export interface PrintSheetProps<T> {
  company: string;
  title: string;
  /** Período, sucursal, filtros aplicados. */
  lines: readonly string[];
  summary?: readonly PrintSummaryItem[];
  columns: readonly ReportColumn<T>[];
  rows: readonly T[];
  rowKey: (row: T) => string;
  printedBy: string;
  printedAt: Date;
}

export function PrintSheet<T>({ company, title, lines, summary = [], columns, rows, rowKey, printedBy, printedAt }: PrintSheetProps<T>) {
  const hasFooter = columns.some((column) => column.footer);
  return (
    <article className="text-[10pt] leading-snug text-black" data-testid="reporte-impreso">
      <header className="mb-3 border-b-2 border-black pb-2">
        <p className="text-[9pt] uppercase tracking-wide">{company}</p>
        <h1 className="text-[16pt] font-bold">{title}</h1>
        {lines.map((line) => (
          <p key={line}>{line}</p>
        ))}
      </header>
      {summary.length > 0 && (
        <dl className="mb-3 grid grid-cols-3 gap-x-4 gap-y-1">
          {summary.map((item) => (
            <div key={item.label}>
              <dt className="text-[8pt] uppercase">{item.label}</dt>
              <dd className="font-semibold">{item.value}</dd>
            </div>
          ))}
        </dl>
      )}
      <table className="w-full border-collapse">
        <thead>
          <tr>
            {columns.map((column) => (
              <th key={column.id} scope="col" className={`border-b border-black px-1 py-1 text-[8pt] uppercase ${column.align === 'end' ? 'text-right' : 'text-left'}`}>
                {column.header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={rowKey(row)} className="break-inside-avoid">
              {columns.map((column) => (
                <td key={column.id} className={`border-b border-gray-300 px-1 py-0.5 align-top ${column.align === 'end' ? 'text-right tabular-nums whitespace-nowrap' : 'text-left'}`}>
                  {column.text(row)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
        {hasFooter && rows.length > 0 && (
          <tfoot>
            <tr>
              {columns.map((column, index) => (
                <td key={column.id} className={`border-t-2 border-black px-1 py-1 font-bold ${column.align === 'end' ? 'text-right whitespace-nowrap' : 'text-left'}`}>
                  {column.footer ? (column.footer(rows) ?? '') : index === 0 ? 'Totales' : ''}
                </td>
              ))}
            </tr>
          </tfoot>
        )}
      </table>
      {rows.length === 0 && <p className="mt-2">Sin filas para este período y estos filtros.</p>}
      <p className="mt-3 text-[8pt]">
        Impreso el {formatDateTime(printedAt)} por {printedBy} · {rows.length === 1 ? '1 fila' : `${rows.length} filas`}
      </p>
    </article>
  );
}
