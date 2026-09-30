// Análisis › Contabilidad › Estado de resultados (`GetIncomeStatementQuery`; en el escritorio: AccountingView ›
// Estado de resultados). Ingresos, costo de ventas, utilidad bruta, gastos de operación y utilidad neta del período, con
// cada cuenta y su parte de los ingresos. Cada cuenta lleva a sus asientos del libro diario. Exportar CSV; el gráfico
// «¿A dónde va cada boliviano vendido?» está PLEGADO (regla P-10).

import { ChartColumn, Download, NotebookText, RefreshCw } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useRpcQuery, useTableState } from '@/4-presentation/panel/hooks';
import { Alert, BarList, Button, Collapsible, ErrorState, FilterBar, LoadingState, Toolbar, useNotify } from '@/4-presentation/panel/kit';
import { exportCsv, formatMoney, formatPercent } from '@/4-presentation/panel/lib';
import { STATEMENT_CSV, statementLines, type StatementLine } from './accounting';
import { PeriodFields } from './PeriodFields';
import { PERIOD_FILTERS, periodName, rangeText, resolvePeriod } from './period';

export interface IncomeStatementViewProps {
  /** Cambia después de registrar un asiento: vuelve a consultar. */
  version: number;
  /** Abre el libro diario con los asientos de una cuenta. */
  onShowAccount: (code: string) => void;
}

const SECTION_TITLES = { ingresos: 'Ingresos', costos: 'Costo de ventas', gastos: 'Gastos de operación' } as const;

export function IncomeStatementView({ version, onShowAccount }: IncomeStatementViewProps) {
  const notify = useNotify();
  const table = useTableState({ filters: PERIOD_FILTERS });
  const [now] = useState(() => new Date());
  const period = resolvePeriod(table.filters, now);
  const statement = useRpcQuery('GetIncomeStatementQuery', { from: period.from, to: period.to }, { enabled: period.problem === null });
  const reload = statement.reload;
  // Después de registrar un asiento (la versión cambia) se vuelve a consultar; al montarse ya consulta sola.
  const [mountedVersion] = useState(version);
  useEffect(() => {
    if (version !== mountedVersion) reload();
  }, [version, mountedVersion, reload]);

  const data = statement.data;
  const lines = data ? statementLines(data) : [];
  const exportRows = () => {
    const file = exportCsv(`estado de resultados ${period.from} ${period.to}`, STATEMENT_CSV, lines);
    notify.success('Exportación lista', `Se descargó ${file}.`);
  };

  let body;
  if (period.problem) {
    body = (
      <Alert tone="warning" title="Revise las fechas">
        {period.problem} El estado de resultados se actualiza al corregirlas.
      </Alert>
    );
  } else if (statement.error && !data) {
    body = <ErrorState error={statement.error} operation="GetIncomeStatementQuery" onRetry={statement.reload} retrying={statement.fetching} />;
  } else if (!data) {
    body = <LoadingState label="Cargando el estado de resultados…" rows={4} />;
  } else {
    body = <StatementTable lines={lines} onShowAccount={onShowAccount} caption={`Estado de resultados ${rangeText(period)}`} />;
  }

  return (
    <div className="space-y-5">
      <FilterBar activeCount={table.activeFilterCount} onClear={table.clearFilters}>
        <PeriodFields period={period} onChange={(values) => table.setFilters(values)} />
      </FilterBar>

      <Collapsible label="Ver gráfico" openLabel="Ocultar gráfico" icon={<ChartColumn />} description="¿A dónde va cada boliviano vendido?">
        {data ? (
          <BarList
            label="De los ingresos del período"
            items={[
              { label: 'Costo de ventas', value: Math.max(0, data.totalCosts) },
              { label: 'Gastos de operación', value: Math.max(0, data.totalExpenses) },
              { label: data.netIncome >= 0 ? 'Utilidad neta' : 'Pérdida neta', value: Math.abs(data.netIncome) },
            ]}
            format={(value) => `${formatMoney(value)}${data.totalRevenue > 0 ? ` (${formatPercent((value / data.totalRevenue) * 100, 1)})` : ''}`}
            max={Math.max(data.totalRevenue, data.totalCosts + data.totalExpenses)}
          />
        ) : (
          <p className="text-sm text-text-muted">Cargando el estado de resultados…</p>
        )}
      </Collapsible>

      <Toolbar
        label="Acciones del estado de resultados"
        end={
          <>
            <Button variant="outline" leftIcon={<RefreshCw />} loading={statement.fetching && !statement.loading} onClick={statement.reload}>
              Actualizar
            </Button>
            <Button variant="outline" leftIcon={<Download />} disabled={!data} onClick={exportRows}>
              Exportar CSV
            </Button>
          </>
        }
      >
        <span className="text-sm text-text-muted">
          {periodName(period)}: {rangeText(period)} · Los asientos de ventas, compras, anulaciones y ajustes se generan solos.
        </span>
      </Toolbar>

      {statement.error && data && (
        <Alert tone="danger" title="No se pudo actualizar">
          Se muestran los últimos datos. Pulse «Actualizar» para reintentar.
        </Alert>
      )}

      {body}
    </div>
  );
}

function StatementTable({ lines, onShowAccount, caption }: { lines: readonly StatementLine[]; onShowAccount: (code: string) => void; caption: string }) {
  return (
    <div className="min-w-0 overflow-x-auto rounded-card border border-border bg-surface shadow-card">
      <table className="w-full border-separate border-spacing-0 text-sm" data-testid="estado-resultados">
        <caption className="sr-only">{caption}</caption>
        <thead>
          <tr>
            <th scope="col" className="border-b border-border bg-surface-2 px-3 py-3 text-left text-xs font-semibold uppercase tracking-wide text-text-muted">
              Concepto
            </th>
            <th scope="col" className="border-b border-border bg-surface-2 px-3 py-3 text-right text-xs font-semibold uppercase tracking-wide text-text-muted">
              Monto
            </th>
            <th scope="col" className="border-b border-border bg-surface-2 px-3 py-3 text-right text-xs font-semibold uppercase tracking-wide text-text-muted max-sm:hidden">
              % de los ingresos
            </th>
          </tr>
        </thead>
        <tbody>
          {lines.flatMap((line, index) => {
            const rows = [];
            // Título de la sección antes de su primera cuenta (las cuentas de una sección vienen juntas).
            if (line.kind === 'cuenta' && line.section && lines[index - 1]?.section !== line.section) {
              rows.push(
                <tr key={`titulo-${line.section}`}>
                  <th scope="rowgroup" colSpan={3} className="border-b border-border px-3 pt-4 pb-1 text-left text-xs font-semibold uppercase tracking-wide text-text-faint">
                    {SECTION_TITLES[line.section]}
                  </th>
                </tr>,
              );
            }
            rows.push(
              <tr key={line.key} data-testid={`linea-${line.key}`} className={line.kind === 'resultado' ? 'bg-primary-soft/40' : line.kind === 'total' ? 'bg-surface-2/60' : undefined}>
                <th scope="row" className={`border-b border-border px-3 py-2 text-left ${line.kind === 'cuenta' ? 'font-normal' : 'font-semibold'}`}>
                  {line.kind === 'cuenta' && line.code ? (
                    <button
                      type="button"
                      onClick={() => onShowAccount(line.code ?? '')}
                      title="Ver los asientos de esta cuenta"
                      className="inline-flex min-h-11 cursor-pointer items-center gap-2 text-left text-text underline-offset-2 hover:text-accent-hover hover:underline"
                    >
                      <span className="font-mono text-xs text-text-muted">{line.code}</span>
                      <span>{line.name}</span>
                      <NotebookText aria-hidden="true" className="size-4 text-text-faint" />
                    </button>
                  ) : (
                    line.name
                  )}
                </th>
                <td className={`border-b border-border px-3 py-2 text-right whitespace-nowrap tabular-nums ${line.kind === 'cuenta' ? '' : 'font-semibold'} ${line.amount < 0 ? 'text-danger-text' : ''}`}>
                  {formatMoney(line.amount)}
                </td>
                <td className="border-b border-border px-3 py-2 text-right whitespace-nowrap text-text-muted tabular-nums max-sm:hidden">{line.ofRevenue === null ? '—' : formatPercent(line.ofRevenue * 100, 1)}</td>
              </tr>,
            );
            return rows;
          })}
        </tbody>
      </table>
    </div>
  );
}
