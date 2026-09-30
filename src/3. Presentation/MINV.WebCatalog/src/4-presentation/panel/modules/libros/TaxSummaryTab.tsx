// Módulo «Libros fiscales» · pestaña «Resumen IVA / IT» (`GetTaxSummaryQuery`): las líneas del resumen del mes para el
// contador (ventas brutas, devoluciones, débito y créditos fiscales, IVA a pagar, saldo a favor e IT), cada una con su
// explicación, como el escritorio; «Exportar CSV».

import { Download, RefreshCw } from 'lucide-react';
import clsx from 'clsx';
import { useRpcQuery } from '@/4-presentation/panel/hooks';
import { Button, ErrorState, LoadingState, Toolbar, useNotify } from '@/4-presentation/panel/kit';
import { exportCsv, formatNumber } from '@/4-presentation/panel/lib';
import { TAX_CSV, monthLabel, taxLines, type MonthValue } from './books';

export function TaxSummaryTab({ month }: { month: MonthValue }) {
  const notify = useNotify();
  const summary = useRpcQuery('GetTaxSummaryQuery', { year: month.year, month: month.month });
  const lines = summary.data ? taxLines(summary.data) : [];

  const exportLines = () => {
    const file = exportCsv(`resumen-iva-it-${month.year}-${String(month.month).padStart(2, '0')}`, TAX_CSV, lines);
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(lines.length)} filas).`);
  };

  let body;
  if (summary.error) body = <ErrorState error={summary.error} operation="GetTaxSummaryQuery" onRetry={summary.reload} retrying={summary.fetching} />;
  else if (!summary.data) body = <LoadingState label="Cargando el resumen del mes…" rows={3} />;
  else
    body = (
      <section aria-label={`Resumen IVA e IT de ${monthLabel(month)}`} className="rounded-card border border-border bg-surface shadow-card">
        <p className="border-b border-border px-4 py-3 text-sm text-text-muted">
          {formatNumber(summary.data.invoices)} facturas · {formatNumber(summary.data.voidedInvoices)} anuladas · {formatNumber(summary.data.notes)} notas crédito-débito
        </p>
        <dl className="divide-y divide-border" data-testid="resumen-iva">
          {lines.map((line) => (
            <div key={line.label} className={clsx('grid gap-1 px-4 py-3 sm:grid-cols-[1fr_auto] sm:gap-4', line.total && 'bg-surface-2')}>
              <dt className="min-w-0">
                <span className={clsx('block text-sm', line.total ? 'font-semibold text-text' : 'text-text')}>{line.label}</span>
                <span className="block text-xs text-text-muted">{line.explanation}</span>
              </dt>
              <dd className={clsx('text-right tabular-nums sm:self-center', line.total ? 'text-lg font-semibold' : 'text-sm')}>{line.amount}</dd>
            </div>
          ))}
        </dl>
      </section>
    );

  return (
    <div className="space-y-4">
      <Toolbar
        label="Acciones del resumen"
        end={
          <>
            <Button variant="outline" leftIcon={<RefreshCw />} loading={summary.fetching && !summary.loading} onClick={summary.reload}>
              Actualizar
            </Button>
            <Button variant="outline" leftIcon={<Download />} disabled={lines.length === 0} onClick={exportLines}>
              Exportar CSV
            </Button>
          </>
        }
      />
      {body}
    </div>
  );
}
