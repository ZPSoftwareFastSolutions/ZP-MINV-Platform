// Análisis › Reportes › Inventario (en el escritorio: ReportsView › Inventario). El valor del inventario HOY (no depende
// del período) por categoría, con los productos en alerta y los que no se mueven hace más de 30 días, de la proyección de
// stock del servidor (`GetStockProjectionQuery`, la misma de Inventario › Stock). Filtro por sucursal (almacén) y
// búsqueda; detalle lateral con los productos de más valor de la categoría; exportar CSV e imprimir; gráfico plegado.

import { Boxes, ChartColumn, ExternalLink, Eye, PackageSearch } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { ROUTES } from '@/4-presentation/app/routes';
import { usePermissions, useRpcQuery, useTableState } from '@/4-presentation/panel/hooks';
import { BarList, Button, Collapsible, DataTable, DetailList, FilterBar, SearchField, SelectField, SidePanel, useNotify } from '@/4-presentation/panel/kit';
import { exportCsv, formatDate, formatMoney, formatNumber, formatPercent, formatQuantity, matchesSearch } from '@/4-presentation/panel/lib';
import { inTableOrder, toCsvColumns, toTableColumns } from './columns';
import { PrintSheet } from './PrintSheet';
import { ReportToolbar, ReportTotals } from './ReportParts';
import { CATEGORY_COLUMNS, INVENTORY_FILTERS, categoryRows, inventorySummary, scopeText, warehouseOptions, type CategoryRow } from './reports';
import { usePrint } from './usePrint';
import { useRowMenuGuard } from './useRowMenuGuard';

const TABLE_COLUMNS = toTableColumns(CATEGORY_COLUMNS);
const CSV_COLUMNS = toCsvColumns(CATEGORY_COLUMNS);
/** Productos que se muestran en el detalle de una categoría. */
const TOP_PRODUCTS = 10;

export function InventoryReportView() {
  const notify = useNotify();
  const navigate = useNavigate();
  const { session } = usePermissions();
  const table = useTableState({ filters: INVENTORY_FILTERS, sort: null });
  const filters = table.filters;

  const branches = useRpcQuery('GetBranchesQuery', {});
  const projection = useRpcQuery('GetStockProjectionQuery', { warehouseCode: filters.sucursal || null });
  const stock = useMemo(() => projection.data?.result.stock ?? [], [projection.data]);
  const allRows = useMemo(() => categoryRows(stock), [stock]);
  const rows = useMemo(() => allRows.filter((row) => matchesSearch(filters.q, [row.name])), [allRows, filters.q]);
  const warehouses = useMemo(() => warehouseOptions(branches.data ?? []), [branches.data]);

  const printer = usePrint();
  const menu = useRowMenuGuard();
  const [detail, setDetail] = useState<{ row: CategoryRow; open: boolean } | null>(null);
  const openDetail = (row: CategoryRow) => setDetail({ row, open: true });
  const closeDetail = () => setDetail((current) => (current ? { ...current, open: false } : current));
  const stockLink = (row: CategoryRow) => ROUTES.panelModule(`stock?categoria=${encodeURIComponent(row.name)}`);

  const warehouseLine = projection.data ? `Almacén ${projection.data.warehouseCode} · al ${formatDate(projection.data.today)}` : scopeText(session?.access);
  const summary = projection.data ? inventorySummary(stock, projection.data.result.alerts.length) : null;

  const exportRows = () => {
    const file = exportCsv(`reporte de inventario ${projection.data?.warehouseCode ?? ''}`, CSV_COLUMNS, inTableOrder(rows, CATEGORY_COLUMNS, table.sort));
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(rows.length)} filas).`);
  };
  const printRows = () => {
    if (!summary) return;
    const printed = printer.print(
      <PrintSheet
        company={session?.company ?? ''}
        title="Reporte de inventario por categoría"
        lines={[warehouseLine, ...(filters.q ? [`Búsqueda: «${filters.q}»`] : [])]}
        summary={summary}
        columns={CATEGORY_COLUMNS}
        rows={inTableOrder(rows, CATEGORY_COLUMNS, table.sort)}
        rowKey={(row) => row.key}
        printedBy={session?.displayName ?? ''}
        printedAt={new Date()}
      />,
    );
    if (!printed) notify.error('No se pudo imprimir', 'Este navegador no permite imprimir desde la página.');
  };

  const row = detail?.row;

  return (
    <div className="space-y-5">
      <FilterBar activeCount={table.activeFilterCount} onClear={table.clearFilters}>
        <SelectField
          label="Sucursal"
          allLabel="Sucursal activa"
          value={filters.sucursal}
          onChange={(value) => table.setFilter('sucursal', value)}
          options={warehouses}
          hint={branches.error ? 'No se pudo cargar la lista de sucursales.' : 'El inventario de hoy: no depende del período.'}
        />
        <SearchField label="Buscar" placeholder="Categoría" value={filters.q} onChange={(q) => table.setFilter('q', q)} />
      </FilterBar>

      <ReportTotals title="Totales de hoy" description={`${warehouseLine}. Solo productos activos; el valor es al costo promedio.`} items={summary} testId="inventario-totales" />

      <Collapsible label="Ver gráfico" openLabel="Ocultar gráfico" icon={<ChartColumn />} description="Valor del inventario por categoría.">
        {projection.data ? (
          <BarList label="Valor por categoría" items={allRows.slice(0, 12).map((item) => ({ label: item.name, value: item.value, hint: formatPercent(item.share * 100, 1) }))} format={(value) => formatMoney(value)} />
        ) : (
          <p className="text-sm text-text-muted">Cargando el reporte…</p>
        )}
      </Collapsible>

      <ReportToolbar
        summary={projection.data ? `${formatNumber(rows.length)} de ${formatNumber(allRows.length)} categorías` : undefined}
        onReload={projection.reload}
        reloading={projection.fetching && !projection.loading}
        hasRows={rows.length > 0}
        onExport={exportRows}
        onPrint={printRows}
      />

      <DataTable
        caption="Inventario por categoría"
        columns={TABLE_COLUMNS}
        rows={projection.data ? rows : undefined}
        rowKey={(item) => item.key}
        rowLabel={(item) => `la categoría ${item.name}`}
        loading={projection.loading}
        refreshing={projection.fetching && !projection.loading}
        error={projection.error}
        onRetry={projection.reload}
        operation="GetStockProjectionQuery"
        {...table.tableProps}
        onRowOpen={(item) => {
          if (!menu.busy()) openDetail(item);
        }}
        activeRowKey={detail?.open ? detail.row.key : null}
        rowActions={(item) => [
          { label: 'Ver detalle', icon: <Eye />, onSelect: menu.guard(() => openDetail(item)) },
          { label: 'Ver el stock de la categoría', icon: <ExternalLink />, onSelect: menu.guard(() => navigate(stockLink(item))) },
        ]}
        empty={{
          title: allRows.length === 0 ? 'No hay productos activos en este almacén' : 'Ninguna categoría coincide con la búsqueda',
          description: allRows.length === 0 ? 'Pruebe con otra sucursal.' : 'Pruebe con otro texto o limpie los filtros.',
          icon: <Boxes />,
          action: (
            <Button variant="outline" onClick={table.clearFilters}>
              Limpiar filtros
            </Button>
          ),
        }}
      />

      <SidePanel
        open={detail?.open ?? false}
        onClose={closeDetail}
        title={row ? row.name : 'Categoría'}
        description={row ? warehouseLine : undefined}
        footer={
          row && (
            <Button variant="outline" leftIcon={<PackageSearch />} fullWidth to={stockLink(row)}>
              Ver el stock de la categoría
            </Button>
          )
        }
      >
        {row && (
          <div className="space-y-5" data-testid="inventario-detalle">
            <DetailList
              items={[
                { label: 'Valor', value: formatMoney(row.value) },
                { label: 'Participación', value: formatPercent(row.share * 100, 1) },
                { label: 'Productos activos', value: formatNumber(row.products) },
                { label: 'Unidades', value: formatQuantity(row.units) },
                { label: 'En alerta', value: formatNumber(row.alerts) },
                { label: 'Sin movimiento (más de 30 días)', value: formatNumber(row.idle) },
              ]}
            />
            <BarList
              label={`Los ${Math.min(TOP_PRODUCTS, row.items.length)} productos de más valor`}
              items={row.items.slice(0, TOP_PRODUCTS).map((item) => ({ label: item.name, value: item.inventoryValue, hint: `${item.sku} · ${formatQuantity(item.stock, { unit: item.unit })}` }))}
              format={(value) => formatMoney(value)}
            />
          </div>
        )}
      </SidePanel>

      {printer.area}
    </div>
  );
}
