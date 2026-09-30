// Módulo «Sucursales» · pestaña «Stock consolidado» (`ConsolidatedStockQuery`): existencias de cada producto por
// sucursal (una columna por sucursal visible) más lo que está EN TRÁNSITO entre sucursales, contado UNA sola vez (regla
// B-04), el total y el valor al costo promedio. La búsqueda la hace el servidor (`search`); categoría y «en tránsito» se
// filtran en la página. El valor por sucursal va plegado en «Ver valor por sucursal» (regla P-10). Exporta CSV.

import { BarChart3, Boxes, Download, RefreshCw } from 'lucide-react';
import { useMemo } from 'react';
import { useRpcQuery, useTableState } from '@/4-presentation/panel/hooks';
import { BarList, Button, Collapsible, DataTable, FilterBar, SearchField, SelectField, StatCard, Toolbar, useNotify, type DataTableColumn } from '@/4-presentation/panel/kit';
import { exportCsv, formatMoney, formatNumber, formatQuantity } from '@/4-presentation/panel/lib';
import { STOCK_FILTERS, TRANSIT_OPTIONS, categoryOptions, consolidatedCsv, filterConsolidated, type ConsolidatedData, type ConsolidatedLine } from './branches';

function stockColumns(branches: ConsolidatedData['branches']): DataTableColumn<ConsolidatedLine>[] {
  return [
    {
      id: 'producto',
      header: 'Producto',
      value: (row) => row.name,
      card: 'title',
      cell: (row) => (
        <span className="block min-w-44">
          <span className="block">{row.name}</span>
          <span className="block text-xs text-text-muted">
            {row.sku} · {row.category}
          </span>
        </span>
      ),
    },
    ...branches.map((branch, index) => ({
      id: `s-${branch.code}`,
      header: branch.code,
      align: 'end' as const,
      value: (row: ConsolidatedLine) => row.byBranch[index] ?? 0,
      cell: (row: ConsolidatedLine) => formatQuantity(row.byBranch[index] ?? 0),
    })),
    {
      id: 'transito',
      header: 'En tránsito',
      align: 'end',
      value: (row) => row.inTransit,
      cell: (row) => (row.inTransit > 0 ? <span className="font-semibold text-accent-hover">{formatQuantity(row.inTransit)}</span> : '—'),
    },
    { id: 'total', header: 'Total', align: 'end', value: (row) => row.total, cell: (row) => formatQuantity(row.total, { unit: row.unit }) },
    {
      id: 'valor',
      header: 'Valor',
      align: 'end',
      value: (row) => row.value,
      cell: (row) => formatMoney(row.value),
      footer: (rows) => formatMoney(rows.reduce((sum, row) => sum + row.value, 0)),
    },
  ];
}

export function ConsolidatedStockTab() {
  const notify = useNotify();
  const table = useTableState({ prefix: 's_', filters: STOCK_FILTERS, sort: { column: 'producto', direction: 'asc' } });
  const filters = table.filters;
  const stock = useRpcQuery('ConsolidatedStockQuery', { search: filters.q || null });
  const branches = useMemo(() => stock.data?.branches ?? [], [stock.data]);
  const all = useMemo(() => stock.data?.rows ?? [], [stock.data]);
  const rows = useMemo(() => filterConsolidated(all, filters), [all, filters]);
  const columns = useMemo(() => stockColumns(branches), [branches]);
  const categories = useMemo(() => categoryOptions(all), [all]);

  const exportRows = () => {
    const file = exportCsv('stock-consolidado', consolidatedCsv(branches), rows);
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(rows.length)} filas).`);
  };

  return (
    <div className="space-y-5">
      <FilterBar activeCount={table.activeFilterCount} onClear={table.clearFilters}>
        <SearchField label="Buscar producto" placeholder="SKU o nombre" value={filters.q} onChange={(q) => table.setFilter('q', q)} />
        <SelectField label="Categoría" allLabel="Todas las categorías" value={filters.categoria} onChange={(value) => table.setFilter('categoria', value)} options={categories} />
        <SelectField label="En tránsito" value={filters.transito} onChange={(value) => table.setFilter('transito', value)} options={TRANSIT_OPTIONS} />
      </FilterBar>

      <Toolbar
        label="Acciones del stock consolidado"
        end={
          <>
            <Button variant="outline" leftIcon={<RefreshCw />} loading={stock.fetching && !stock.loading} onClick={stock.reload}>
              Actualizar
            </Button>
            <Button variant="outline" leftIcon={<Download />} disabled={rows.length === 0} onClick={exportRows}>
              Exportar CSV
            </Button>
          </>
        }
      >
        {stock.data && (
          <span className="text-sm text-text-muted" data-testid="consolidado-resumen">
            {formatNumber(rows.length)} de {formatNumber(all.length)} productos · sucursales: {branches.map((branch) => branch.code).join(', ') || '—'}
          </span>
        )}
      </Toolbar>

      <Collapsible label="Ver valor por sucursal" openLabel="Ocultar valor por sucursal" icon={<BarChart3 />} description="Valor del stock de cada sucursal, lo que está en tránsito y el total.">
        {stock.data && (
          <div className="space-y-4" data-testid="valor-por-sucursal">
            <div className="grid grid-cols-[repeat(auto-fit,minmax(11rem,1fr))] gap-3">
              <StatCard label="Valor total" value={formatMoney(stock.data.totalValue)} hint="Sucursales + en tránsito" icon={<Boxes />} />
              <StatCard label="En tránsito" value={formatMoney(stock.data.inTransitValue)} hint="Contado una sola vez" />
            </div>
            <BarList
              label="Valor del stock por sucursal"
              items={branches.map((branch, index) => ({ label: `${branch.code} · ${branch.name}`, value: stock.data?.valueByBranch[index] ?? 0 }))}
              format={formatMoney}
            />
          </div>
        )}
      </Collapsible>

      <DataTable
        caption="Stock consolidado por sucursal"
        columns={columns}
        rows={stock.data ? rows : undefined}
        rowKey={(row) => row.sku}
        rowLabel={(row) => `${row.name} (${row.sku})`}
        loading={stock.loading}
        refreshing={stock.fetching && !stock.loading}
        error={stock.error}
        onRetry={stock.reload}
        operation="ConsolidatedStockQuery"
        {...table.tableProps}
        empty={{
          title: all.length === 0 ? 'Sin existencias con esa búsqueda' : 'Ningún producto coincide con los filtros',
          icon: <Boxes />,
          action: (
            <Button variant="outline" onClick={table.clearFilters}>
              Limpiar filtros
            </Button>
          ),
        }}
      />
    </div>
  );
}
