// Módulo «Contabilidad» · las líneas de un asiento (cuenta, glosa, Debe y Haber) con sus totales. Se usa en la fila
// desplegable del libro diario y en el detalle lateral.

import { formatMoney } from '@/4-presentation/panel/lib';
import type { JournalLineRecord } from './accounting';

export interface EntryLinesTableProps {
  number: string;
  lines: readonly JournalLineRecord[];
  /** Resalta las líneas de esta cuenta (el filtro «Cuenta» del libro). */
  highlight?: string;
}

const CELL = 'border-b border-border px-2 py-1.5';

export function EntryLinesTable({ number, lines, highlight }: EntryLinesTableProps) {
  const debit = lines.reduce((sum, line) => sum + Math.round(line.debit * 100), 0) / 100;
  const credit = lines.reduce((sum, line) => sum + Math.round(line.credit * 100), 0) / 100;
  return (
    <div className="min-w-0 overflow-x-auto">
      <table className="w-full border-separate border-spacing-0 text-sm" data-testid={`lineas-${number}`}>
        <caption className="sr-only">Líneas del asiento {number}</caption>
        <thead>
          <tr className="text-xs uppercase tracking-wide text-text-muted">
            <th scope="col" className={`${CELL} text-left font-semibold`}>
              Cuenta
            </th>
            <th scope="col" className={`${CELL} text-right font-semibold`}>
              Debe
            </th>
            <th scope="col" className={`${CELL} text-right font-semibold`}>
              Haber
            </th>
          </tr>
        </thead>
        <tbody>
          {lines.map((line, index) => (
            <tr key={`${line.accountCode}-${index}`} className={highlight === line.accountCode ? 'bg-accent-soft/40' : undefined}>
              <td className={`${CELL} min-w-40`}>
                <span className="block">
                  <span className="font-mono text-xs text-text-muted">{line.accountCode}</span> {line.accountName}
                </span>
                {line.memo && <span className="block text-xs text-text-muted">{line.memo}</span>}
              </td>
              <td className={`${CELL} text-right whitespace-nowrap tabular-nums`}>{line.debit !== 0 ? formatMoney(line.debit) : ''}</td>
              <td className={`${CELL} text-right whitespace-nowrap tabular-nums`}>{line.credit !== 0 ? formatMoney(line.credit) : ''}</td>
            </tr>
          ))}
        </tbody>
        <tfoot>
          <tr className="font-semibold">
            <td className="px-2 py-1.5">Totales</td>
            <td className="px-2 py-1.5 text-right whitespace-nowrap tabular-nums">{formatMoney(debit)}</td>
            <td className="px-2 py-1.5 text-right whitespace-nowrap tabular-nums">{formatMoney(credit)}</td>
          </tr>
        </tfoot>
      </table>
    </div>
  );
}
