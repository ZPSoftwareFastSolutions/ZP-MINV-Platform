// Análisis › Reportes › Sucursales (`GetBranchReportQuery`: tablero gerencial por sucursal, módulo comercial «Auditoría
// global»). Solo se ofrece a quien ve varias sucursales. Ventas, ingresos, IVA, ticket promedio, valor del stock y
// participación de cada sucursal visible, o los ingresos de cada una por día; lo que está en tránsito se cuenta una vez.
// Sale del modelo de lectura del servidor: la pantalla dice de cuándo son los datos (regla B-14).

import { Building2, ChartColumn, Eye } from 'lucide-react';
import { useMemo, useState } from 'react';
import { usePermissions, useRpcQuery, useTableState } from '@/4-presentation/panel/hooks';
import { Alert, BarList, Button, Collapsible, DataTable, DetailList, FilterBar, MiniBars, SearchField, SelectField, SidePanel, useNotify } from '@/4-presentation/panel/kit';
import { exportCsv, formatDateTime, formatMoney, formatNumber, formatPercent } from '@/4-presentation/panel/lib';
import { inTableOrder, toCsvColumns, toTableColumns, type ReportColumn } from './columns';
import { PeriodFields } from './PeriodFields';
import { periodName, rangeText, resolvePeriod } from './period';
import { PrintSheet } from './PrintSheet';
import { ReportToolbar, ReportTotals } from './ReportParts';
import {
  BRANCH_COLUMNS,
  BRANCH_FILTERS,
  BRANCH_GROUPINGS,
  branchDayColumns,
  branchDayRows,
  branchGroupingOf,
  branchSummary,
  filterBranches,
  seriesPoints,
  type BranchDayRow,
  type BranchRecord,
} from './reports';
import { usePrint } from './usePrint';
import { useRowMenuGuard } from './useRowMenuGuard';

const BRANCH_TABLE = toTableColumns(BRANCH_COLUMNS);

export function BranchReportView() {
  const notify = useNotify();
  const { session } = usePermissions();
  const table = useTableState({ filters: BRANCH_FILTERS, sort: null });
  const filters = table.filters;
  const [now] = useState(() => new Date());
  const period = resolvePeriod(filters, now);
  const grouping = branchGroupingOf(filters.agrupar);

  const report = useRpcQuery('GetBranchReportQuery', { from: period.from, to: period.to }, { enabled: period.problem === null });
  const data = report.data;
  const branches = useMemo(() => filterBranches(data?.branches ?? [], filters), [data, filters]);
  const days = useMemo(() => (data ? branchDayRows(data) : []), [data]);
  const dayColumns = useMemo(() => branchDayColumns(data?.branches ?? []), [data]);
  const dayTable = useMemo(() => toTableColumns(dayColumns), [dayColumns]);
  const branchOptions = useMemo(() => (data?.branches ?? []).map((row) => ({ value: row.code, label: `${row.code} · ${row.name}` })), [data]);

  const printer = usePrint();
  const menu = useRowMenuGuard();
  const [detail, setDetail] = useState<{ row: BranchRecord; open: boolean } | null>(null);
  const openDetail = (row: BranchRecord) => setDetail({ row, open: true });
  const closeDetail = () => setDetail((current) => (current ? { ...current, open: false } : current));

  const periodLine = `${periodName(period)}: ${rangeText(period)}`;
  const byDay = grouping === 'dia';

  const exportRows = () => {
    const file = byDay
      ? exportCsv(`reporte de sucursales por dia ${period.from} ${period.to}`, toCsvColumns(dayColumns), inTableOrder(days, dayColumns, table.sort))
      : exportCsv(`reporte de sucursales ${period.from} ${period.to}`, toCsvColumns(BRANCH_COLUMNS), inTableOrder(branches, BRANCH_COLUMNS, table.sort));
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(byDay ? days.length : branches.length)} filas).`);
  };
  const printRows = () => {
    if (!data) return;
    const common = { company: session?.company ?? '', lines: [periodLine, 'Sucursales visibles para su cuenta'], summary: branchSummary(data), printedBy: session?.displayName ?? '', printedAt: new Date() };
    const printed = byDay
      ? printer.print(<PrintSheet<BranchDayRow> {...common} title="Ingresos por sucursal y por día" columns={dayColumns} rows={inTableOrder(days, dayColumns, table.sort)} rowKey={(row) => row.key} />)
      : printer.print(<PrintSheet<BranchRecord> {...common} title="Reporte por sucursal" columns={BRANCH_COLUMNS} rows={inTableOrder(branches, BRANCH_COLUMNS, table.sort)} rowKey={(row) => row.code} />);
    if (!printed) notify.error('No se pudo imprimir', 'Este navegador no permite imprimir desde la página.');
  };

  const row = detail?.row;
  const shownRows = byDay ? days.length : branches.length;
  const totalRows = byDay ? days.length : (data?.branches.length ?? 0);

  return (
    <div className="space-y-5">
      <FilterBar activeCount={table.activeFilterCount} onClear={table.clearFilters}>
        <PeriodFields period={period} onChange={(values) => table.setFilters(values)} />
        <SelectField label="Ver" allLabel={false} value={grouping} onChange={(value) => table.setFilter('agrupar', value || BRANCH_FILTERS.agrupar)} options={BRANCH_GROUPINGS} />
        {!byDay && <SelectField label="Sucursal" allLabel="Todas las sucursales" value={filters.sucursal} onChange={(value) => table.setFilter('sucursal', value)} options={branchOptions} />}
        {!byDay && <SearchField label="Buscar" placeholder="Código o nombre" value={filters.q} onChange={(q) => table.setFilter('q', q)} />}
      </FilterBar>

      <ReportTotals
        description={`${periodLine}. Lo que está en tránsito entre sucursales se cuenta una sola vez.${data ? ` Datos al ${data.refreshedAt ? formatDateTime(data.refreshedAt) : '—'}.` : ''}`}
        items={data ? branchSummary(data) : null}
        testId="sucursales-totales"
      />

      <Collapsible label="Ver gráfico" openLabel="Ocultar gráfico" icon={<ChartColumn />} description="Ingresos por sucursal y total por día.">
        {data ? (
          <div className="grid gap-6 lg:grid-cols-2" data-testid="sucursales-grafico">
            <BarList label="Ingresos por sucursal" items={data.branches.map((item) => ({ label: `${item.code} · ${item.name}`, value: item.revenue, hint: formatPercent(item.sharePercent, 1) }))} format={(value) => formatMoney(value)} tone="accent" />
            <div>
              <p className="mb-2 text-sm font-semibold text-text">Ingresos de todas las sucursales por día</p>
              <MiniBars label="Ingresos por día del período" points={seriesPoints(days.map((day) => ({ date: day.day, amount: day.total })))} format={(value) => formatMoney(value)} height={96} showAxis />
            </div>
          </div>
        ) : (
          <p className="text-sm text-text-muted">Cargando el reporte…</p>
        )}
      </Collapsible>

      <ReportToolbar
        summary={data ? `${formatNumber(shownRows)} de ${formatNumber(totalRows)} ${byDay ? 'días' : 'sucursales'}` : undefined}
        onReload={report.reload}
        reloading={report.fetching && !report.loading}
        hasRows={shownRows > 0}
        onExport={exportRows}
        onPrint={printRows}
      />

      {period.problem ? (
        <Alert tone="warning" title="Revise las fechas">
          {period.problem} El reporte se actualiza al corregirlas.
        </Alert>
      ) : byDay ? (
        <DataTable
          caption="Ingresos por sucursal y por día"
          columns={dayTable}
          rows={data ? days : undefined}
          rowKey={(item) => item.key}
          loading={report.loading}
          refreshing={report.fetching && !report.loading}
          error={report.error}
          onRetry={report.reload}
          operation="GetBranchReportQuery"
          {...table.tableProps}
          empty={{ title: 'No hay días en este período', icon: <Building2 /> }}
        />
      ) : (
        <DataTable
          caption="Reporte por sucursal"
          columns={BRANCH_TABLE}
          rows={data ? branches : undefined}
          rowKey={(item) => item.code}
          rowLabel={(item) => `la sucursal ${item.code} · ${item.name}`}
          loading={report.loading}
          refreshing={report.fetching && !report.loading}
          error={report.error}
          onRetry={report.reload}
          operation="GetBranchReportQuery"
          {...table.tableProps}
          onRowOpen={(item) => {
            if (!menu.busy()) openDetail(item);
          }}
          activeRowKey={detail?.open ? detail.row.code : null}
          rowActions={(item) => [
            { label: 'Ver detalle', icon: <Eye />, onSelect: menu.guard(() => openDetail(item)) },
            { label: 'Ver solo esta sucursal', icon: <Building2 />, onSelect: menu.guard(() => table.setFilter('sucursal', item.code)), hidden: filters.sucursal === item.code },
          ]}
          empty={{
            title: (data?.branches.length ?? 0) === 0 ? 'No hay sucursales visibles para su cuenta' : 'Ninguna sucursal coincide con los filtros',
            icon: <Building2 />,
            action: (
              <Button variant="outline" onClick={table.clearFilters}>
                Limpiar filtros
              </Button>
            ),
          }}
        />
      )}

      <SidePanel open={detail?.open ?? false} onClose={closeDetail} title={row ? `${row.code} · ${row.name}` : 'Sucursal'} description={rangeText(period)}>
        {row && (
          <div data-testid="sucursal-detalle">
            <DetailList
              items={BRANCH_COLUMNS.filter((column: ReportColumn<BranchRecord>) => column.id !== 'sucursal').map((column) => ({ label: column.header, value: column.text(row) }))}
            />
          </div>
        )}
      </SidePanel>

      {printer.area}
    </div>
  );
}
