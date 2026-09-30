// Análisis › Reportes › Tecnología (`GetTechDashboardQuery`, el tablero de la edición Tecnología del escritorio): ventas
// por categoría y por plataforma, tarjetas de video y consolas más vendidas, garantías abiertas por estado y unidades con
// serie en stock, más los totales de series, garantías, cotizaciones, armados vendidos y reservas web. El servidor mide
// los ÚLTIMOS N días hasta hoy (no un rango libre): por eso este reporte tiene su propia lista «Período».

import { ChartColumn, Cpu, ExternalLink } from 'lucide-react';
import { useMemo } from 'react';
import { usePermissions, useRpcQuery, useTableState } from '@/4-presentation/panel/hooks';
import { BarList, Button, Collapsible, DataTable, FilterBar, SearchField, SelectField, useNotify } from '@/4-presentation/panel/kit';
import { exportCsv, formatMoney, formatNumber, matchesSearch } from '@/4-presentation/panel/lib';
import { inTableOrder, toCsvColumns, toTableColumns } from './columns';
import { PrintSheet } from './PrintSheet';
import { ReportToolbar, ReportTotals } from './ReportParts';
import { TECH_CATEGORY_CODES, TECH_DAYS, TECH_FILTERS, TECH_LISTS, isCountList, scopeText, techColumns, techDaysOf, techListLink, techListOf, techRows, techSummary } from './reports';
import { usePrint } from './usePrint';

export function TechReportView() {
  const notify = useNotify();
  const { session } = usePermissions();
  const table = useTableState({ filters: TECH_FILTERS, sort: null });
  const filters = table.filters;
  const days = techDaysOf(filters.dias);
  const list = techListOf(filters.lista);

  const view = useRpcQuery('GetTechDashboardQuery', { days, gpuCategoryCode: TECH_CATEGORY_CODES.gpu, consoleCategoryCode: TECH_CATEGORY_CODES.consoles });
  const allRows = useMemo(() => (view.data ? techRows(view.data, list) : []), [view.data, list]);
  const rows = useMemo(() => allRows.filter((row) => matchesSearch(filters.q, [row.name])), [allRows, filters.q]);
  const columns = useMemo(() => techColumns(list), [list]);
  const tableColumns = useMemo(() => toTableColumns(columns), [columns]);

  const printer = usePrint();
  const listName = TECH_LISTS.find((option) => option.value === list)?.label ?? '';
  const daysName = TECH_DAYS.find((option) => Number(option.value) === days)?.label ?? '';
  const scope = scopeText(session?.access);
  const link = techListLink(list);

  const exportRows = () => {
    const file = exportCsv(`reporte tecnologia ${listName} ${days} dias`, toCsvColumns(columns), inTableOrder(rows, columns, table.sort));
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(rows.length)} filas).`);
  };
  const printRows = () => {
    if (!view.data) return;
    const printed = printer.print(
      <PrintSheet
        company={session?.company ?? ''}
        title={`Tecnología · ${listName}`}
        lines={[isCountList(list) ? 'Situación de hoy' : daysName, scope, ...(filters.q ? [`Búsqueda: «${filters.q}»`] : [])]}
        summary={techSummary(view.data)}
        columns={columns}
        rows={inTableOrder(rows, columns, table.sort)}
        rowKey={(row) => row.key}
        printedBy={session?.displayName ?? ''}
        printedAt={new Date()}
      />,
    );
    if (!printed) notify.error('No se pudo imprimir', 'Este navegador no permite imprimir desde la página.');
  };

  return (
    <div className="space-y-5">
      <FilterBar activeCount={table.activeFilterCount} onClear={table.clearFilters}>
        <SelectField label="Período" allLabel={false} value={String(days)} onChange={(value) => table.setFilter('dias', value || TECH_FILTERS.dias)} options={TECH_DAYS} hint="Hasta hoy. Garantías y series son la situación de hoy." />
        <SelectField label="Ver" allLabel={false} value={list} onChange={(value) => table.setFilter('lista', value || TECH_FILTERS.lista)} options={TECH_LISTS} />
        <SearchField label="Buscar" placeholder="Nombre" value={filters.q} onChange={(q) => table.setFilter('q', q)} />
      </FilterBar>

      <ReportTotals title="Totales de la tienda de tecnología" description={`${daysName} (armados vendidos) y situación de hoy · ${scope}.`} items={view.data ? techSummary(view.data) : null} testId="tecnologia-totales" />

      <Collapsible label="Ver gráfico" openLabel="Ocultar gráfico" icon={<ChartColumn />} description={listName}>
        {view.data ? (
          <BarList
            label={listName}
            items={allRows.slice(0, 12).map((row) => ({ label: row.name, value: isCountList(list) ? (row.count ?? 0) : (row.amount ?? 0) }))}
            format={isCountList(list) ? (value) => formatNumber(value) : (value) => formatMoney(value)}
            tone="accent"
          />
        ) : (
          <p className="text-sm text-text-muted">Cargando el reporte…</p>
        )}
      </Collapsible>

      <ReportToolbar
        summary={view.data ? `${formatNumber(rows.length)} de ${formatNumber(allRows.length)} filas · ${listName.toLowerCase()}` : undefined}
        onReload={view.reload}
        reloading={view.fetching && !view.loading}
        hasRows={rows.length > 0}
        onExport={exportRows}
        onPrint={printRows}
      />

      <DataTable
        caption={listName}
        columns={tableColumns}
        rows={view.data ? rows : undefined}
        rowKey={(row) => row.key}
        loading={view.loading}
        refreshing={view.fetching && !view.loading}
        error={view.error}
        onRetry={view.reload}
        operation="GetTechDashboardQuery"
        {...table.tableProps}
        empty={{
          title: allRows.length === 0 ? 'Sin datos para esta lista' : 'Ninguna fila coincide con la búsqueda',
          description: allRows.length === 0 ? 'Pruebe con un período más largo u otra lista.' : 'Pruebe con otro texto o limpie los filtros.',
          icon: <Cpu />,
          action: (
            <Button variant="outline" onClick={table.clearFilters}>
              Limpiar filtros
            </Button>
          ),
        }}
      />

      {link && (
        <div>
          <Button variant="outline" leftIcon={<ExternalLink />} to={link.to}>
            {link.label}
          </Button>
        </div>
      )}

      {printer.area}
    </div>
  );
}
