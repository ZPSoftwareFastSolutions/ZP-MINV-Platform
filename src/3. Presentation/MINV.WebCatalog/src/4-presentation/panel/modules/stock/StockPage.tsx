// Inventario › Stock (en el escritorio: StockView + StockViewModel). Existencias al instante del almacén de la sucursal
// activa (u otra sucursal visible), con lo reservado y lo disponible; o el stock CONSOLIDADO de todas las sucursales que
// la sesión ve. Filtros en la dirección: búsqueda, sucursal, categoría, marca, semáforo, con reservas y sin rotación.
// Tabla ordenable y paginada (detalle de cada fila desplegable), ficha lateral con kardex, reservas y existencias por
// sucursal (`?ficha=SKU` la abre desde otro módulo), exportar CSV y el resumen PLEGADO detrás de «Ver resumen».
//
// Operaciones: GetStockProjectionQuery y GetStockReservationsQuery (por almacén), ConsolidatedStockQuery (todas),
// GetBranchesQuery (lista de sucursales), GetWorkspaceQuery (almacén de trabajo y días sin rotación) y
// SearchTechProductsQuery (la marca de cada producto). Todas exigen `inventory.stock.view`.

import { ArrowLeftRight, ChartColumn, Download, Eye, ListFilter, PackageMinus, PackagePlus, PackageSearch, RefreshCw, ShoppingCart, TriangleAlert } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { ROUTES } from '@/4-presentation/app/routes';
import { usePermissions, useRpcQuery, useTableState } from '@/4-presentation/panel/hooks';
import {
  Alert,
  Button,
  Collapsible,
  DataTable,
  DetailList,
  FilterBar,
  Page,
  SearchField,
  SelectField,
  StatusBadge,
  Toolbar,
  statusOptions,
  useNotify,
  type DataTableColumn,
} from '@/4-presentation/panel/kit';
import { exportCsv, formatDate, formatMoney, formatNumber, formatQuantity } from '@/4-presentation/panel/lib';
import {
  ALL_BRANCHES,
  BRAND_LOOKUP,
  DETAIL_PARAM,
  RESERVATION_OPTIONS,
  ROTATION_OPTIONS,
  STOCK_CSV,
  STOCK_FILTERS,
  STOCK_STATUSES,
  activeWarehouseLabel,
  brandBySku,
  brandOptions,
  categoryOptions,
  consolidatedCsv,
  coverageText,
  filterConsolidated,
  filterStock,
  lastMovementText,
  minMaxText,
  movementLink,
  quantityWithUnit,
  reservedBySku,
  statusPriority,
  toConsolidatedEntries,
  toStockEntries,
  warehouseOptions,
  type ConsolidatedData,
  type ConsolidatedEntry,
  type StockEntry,
} from './stock';
import { StockDetailPanel, type DetailTab } from './StockDetailPanel';
import { ConsolidatedSummary, WarehouseSummary } from './StockSummary';

/** Columnas de la tabla por almacén: fuera del componente (no se vuelven a crear en cada dibujo). */
const COLUMNS: DataTableColumn<StockEntry>[] = [
  {
    id: 'producto',
    header: 'Producto',
    card: 'title',
    value: (entry) => entry.row.name,
    cell: (entry) => (
      <span className="block min-w-48">
        <span className="block">{entry.row.name}</span>
        <span className="block text-xs font-normal text-text-muted">
          {entry.row.sku} · {entry.row.category}
        </span>
      </span>
    ),
  },
  {
    id: 'semaforo',
    header: 'Semáforo',
    value: (entry) => statusPriority(entry.row.status),
    cell: (entry) => <StatusBadge status={entry.row.status} statuses={STOCK_STATUSES} />,
  },
  { id: 'existencias', header: 'Existencias', align: 'end', value: (entry) => entry.row.stock, cell: (entry) => quantityWithUnit(entry.row.stock, entry.row.unit) },
  { id: 'reservado', header: 'Reservado', align: 'end', value: (entry) => entry.reserved, cell: (entry) => (entry.reserved > 0 ? formatQuantity(entry.reserved) : '—') },
  { id: 'disponible', header: 'Disponible', align: 'end', value: (entry) => entry.available, cell: (entry) => formatQuantity(entry.available) },
  { id: 'minimo', header: 'Mínimo', align: 'end', value: (entry) => entry.row.minimum, cell: (entry) => formatQuantity(entry.row.minimum) },
  { id: 'cobertura', header: 'Cobertura', align: 'end', value: (entry) => entry.row.coverageDays, cell: (entry) => coverageText(entry.row.coverageDays) },
];

/** Columnas del consolidado: una por sucursal visible, en tránsito, total y valor (con totales al pie). */
function consolidatedColumns(branches: ConsolidatedData['branches']): DataTableColumn<ConsolidatedEntry>[] {
  const quantity = (value: number) => formatQuantity(value);
  return [
    {
      id: 'producto',
      header: 'Producto',
      card: 'title',
      value: (entry) => entry.row.name,
      cell: (entry) => (
        <span className="block min-w-48">
          <span className="block">{entry.row.name}</span>
          <span className="block text-xs font-normal text-text-muted">
            {entry.row.sku} · {entry.row.category} · {entry.row.unit}
          </span>
        </span>
      ),
    },
    ...branches.map(
      (branch, index): DataTableColumn<ConsolidatedEntry> => ({
        id: `sucursal-${branch.code}`,
        header: branch.code,
        align: 'end',
        value: (entry) => entry.row.byBranch[index] ?? 0,
        cell: (entry) => quantity(entry.row.byBranch[index] ?? 0),
        footer: (rows) => quantity(rows.reduce((sum, entry) => sum + (entry.row.byBranch[index] ?? 0), 0)),
      }),
    ),
    {
      id: 'transito',
      header: 'En tránsito',
      align: 'end',
      value: (entry) => entry.row.inTransit,
      cell: (entry) => (entry.row.inTransit > 0 ? quantity(entry.row.inTransit) : '—'),
      footer: (rows) => quantity(rows.reduce((sum, entry) => sum + entry.row.inTransit, 0)),
    },
    {
      id: 'total',
      header: 'Total',
      align: 'end',
      value: (entry) => entry.row.total,
      cell: (entry) => quantity(entry.row.total),
      footer: (rows) => quantity(rows.reduce((sum, entry) => sum + entry.row.total, 0)),
    },
    {
      id: 'valor',
      header: 'Valor',
      align: 'end',
      value: (entry) => entry.row.value,
      cell: (entry) => formatMoney(entry.row.value),
      footer: (rows) => formatMoney(rows.reduce((sum, entry) => sum + entry.row.value, 0)),
    },
  ];
}

/** Detalle desplegable de una fila (lo que no entra en la tabla). */
function StockRowDetail({ entry }: { entry: StockEntry }) {
  const row = entry.row;
  return (
    <DetailList
      items={[
        { label: 'Mínimo / máximo', value: minMaxText(row.minimum, row.maximum) },
        { label: 'Valor del inventario', value: formatMoney(row.inventoryValue) },
        { label: 'Costo promedio', value: formatMoney(row.unitCost) },
        { label: 'Salidas en 30 días', value: row.sales30Days > 0 ? quantityWithUnit(row.sales30Days, row.unit) : 'Sin ventas' },
        { label: 'Ranking de ventas', value: row.salesRank !== null ? `N.º ${row.salesRank}` : null },
        { label: 'Último movimiento', value: `${lastMovementText(row)}${row.lastMovement ? ` (${formatDate(row.lastMovement)})` : ''}` },
        { label: 'Proveedor', value: row.supplier || 'Sin proveedor' },
        { label: 'Marca', value: entry.brand },
        { label: 'Rotación', value: entry.withoutRotation ? 'Sin rotación' : null },
      ]}
    />
  );
}

export function StockPage() {
  const notify = useNotify();
  const navigate = useNavigate();
  const { can, session } = usePermissions();
  const table = useTableState({ filters: STOCK_FILTERS, sort: { column: 'producto', direction: 'asc' } });
  const filters = table.filters;
  const consolidated = filters.sucursal === ALL_BRANCHES;
  const warehouseCode = consolidated || filters.sucursal === '' ? null : filters.sucursal;

  // Lo que filtra el SERVIDOR (el almacén) va en el pedido; el resto se filtra en la página.
  const branches = useRpcQuery('GetBranchesQuery', {});
  const workspace = useRpcQuery('GetWorkspaceQuery', {});
  const projection = useRpcQuery('GetStockProjectionQuery', { warehouseCode }, { enabled: !consolidated });
  const reservations = useRpcQuery('GetStockReservationsQuery', { warehouseCode }, { enabled: !consolidated });
  const totals = useRpcQuery('ConsolidatedStockQuery', { search: null }, { enabled: consolidated });
  const brands = useRpcQuery('SearchTechProductsQuery', BRAND_LOOKUP);

  const daysWithoutRotation = workspace.data?.daysWithoutRotation ?? null;
  const brandMap = useMemo(() => brandBySku(brands.data ?? []), [brands.data]);
  const reservedMap = useMemo(() => reservedBySku(reservations.data ?? []), [reservations.data]);
  // La primera vez se espera también el reservado (si falla, la lista sigue sin él y lo avisa).
  const ready = projection.data !== undefined && (reservations.data !== undefined || reservations.error !== null);
  const entries = useMemo(
    () => (ready && projection.data ? toStockEntries(projection.data.result.stock, reservedMap, brandMap, daysWithoutRotation) : []),
    [ready, projection.data, reservedMap, brandMap, daysWithoutRotation],
  );
  const rows = useMemo(() => filterStock(entries, filters), [entries, filters]);
  const consolidatedEntries = useMemo(() => toConsolidatedEntries(totals.data?.rows ?? [], brandMap), [totals.data, brandMap]);
  const consolidatedRows = useMemo(() => filterConsolidated(consolidatedEntries, filters), [consolidatedEntries, filters]);
  const consolidatedCols = useMemo(() => consolidatedColumns(totals.data?.branches ?? []), [totals.data]);

  const categories = useMemo(
    () => categoryOptions(consolidated ? consolidatedEntries.map((entry) => entry.row.category) : entries.map((entry) => entry.row.category)),
    [consolidated, consolidatedEntries, entries],
  );
  const brandChoices = useMemo(() => brandOptions((consolidated ? consolidatedEntries : entries).map((entry) => entry.brand)), [consolidated, consolidatedEntries, entries]);
  const warehouses = useMemo(() => warehouseOptions(branches.data ?? []), [branches.data]);
  const activeBranch = session?.access.branches.find((branch) => branch.id === session.access.activeBranchId) ?? null;

  // Ficha lateral: se abre con `?ficha=SKU` (también desde otros módulos). Se guarda el último SKU para que el panel se
  // deslice con su contenido al cerrarse.
  const [params, setParams] = useSearchParams();
  const detailSku = params.get(DETAIL_PARAM);
  const [shown, setShown] = useState<{ sku: string | null; tab: DetailTab }>({ sku: detailSku, tab: 'ficha' });
  if (detailSku && detailSku !== shown.sku) setShown({ sku: detailSku, tab: shown.tab });
  const openDetail = (sku: string, tab: DetailTab = 'ficha') => {
    setShown({ sku, tab });
    setParams(
      (previous) => {
        const next = new URLSearchParams(previous);
        next.set(DETAIL_PARAM, sku);
        return next;
      },
      { replace: true },
    );
  };
  const closeDetail = () =>
    setParams(
      (previous) => {
        const next = new URLSearchParams(previous);
        next.delete(DETAIL_PARAM);
        return next;
      },
      { replace: true },
    );

  const canWarehouse = can('inventory.movements.register.warehouse');
  const canSales = can('inventory.movements.register.sales');
  const workWarehouse = workspace.data?.warehouseCode ?? null;
  const otherWarehouse = warehouseCode && workWarehouse && warehouseCode.toUpperCase() !== workWarehouse.toUpperCase() ? warehouseCode : null;

  const reload = () => {
    if (consolidated) totals.reload();
    else {
      projection.reload();
      reservations.reload();
    }
  };

  const exportRows = () => {
    const file = consolidated && totals.data ? exportCsv('stock consolidado', consolidatedCsv(totals.data.branches), consolidatedRows) : exportCsv(`stock ${projection.data?.warehouseCode ?? ''}`, STOCK_CSV, rows);
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(consolidated ? consolidatedRows.length : rows.length)} filas).`);
  };

  const withReservations = entries.filter((entry) => entry.reserved > 0).length;
  const visibleCount = consolidated ? consolidatedRows.length : rows.length;
  const totalCount = consolidated ? consolidatedEntries.length : entries.length;
  const loaded = consolidated ? totals.data !== undefined : ready;
  const emptyAll = totalCount === 0;
  const empty = {
    title: emptyAll ? (consolidated ? 'Ninguna sucursal tiene existencias' : 'Este almacén todavía no tiene productos') : 'No hay productos con estos filtros',
    description: emptyAll ? undefined : 'Pruebe con otra búsqueda o limpie los filtros.',
    icon: <PackageSearch />,
    action: emptyAll ? undefined : (
      <Button variant="outline" onClick={table.clearFilters}>
        Limpiar filtros
      </Button>
    ),
  };

  return (
    <Page
      title="Stock"
      description="Existencias, reservas y disponible de cada producto, calculados al instante. Use los filtros para encontrar lo que busca."
      actions={
        <>
          <Button variant="outline" leftIcon={<TriangleAlert />} to={ROUTES.panelModule('alertas')}>
            Ver alertas
          </Button>
          <Button variant="outline" leftIcon={<ShoppingCart />} to={ROUTES.panelModule('pedido')}>
            Pedido sugerido
          </Button>
          {(canWarehouse || canSales) && (
            <Button leftIcon={<ArrowLeftRight />} to={movementLink('1')}>
              Registrar movimiento
            </Button>
          )}
        </>
      }
    >
      <Collapsible label="Ver resumen del inventario" openLabel="Ocultar resumen del inventario" icon={<ChartColumn />} description="Valor, productos activos, alertas y reservas.">
        {consolidated ? (
          totals.data ? (
            <ConsolidatedSummary data={totals.data} />
          ) : (
            <p className="text-sm text-text-muted">Cargando el resumen…</p>
          )
        ) : projection.data ? (
          <WarehouseSummary rows={projection.data.result.stock} alerts={projection.data.result.alerts} reservedProducts={withReservations} movements={projection.data.result.movements} />
        ) : (
          <p className="text-sm text-text-muted">Cargando el resumen…</p>
        )}
      </Collapsible>

      <FilterBar activeCount={table.activeFilterCount} onClear={table.clearFilters}>
        <SearchField label="Buscar" placeholder="SKU, nombre, categoría, marca o proveedor" value={filters.q} onChange={(q) => table.setFilter('q', q)} />
        <SelectField
          label="Sucursal"
          allLabel={activeWarehouseLabel(activeBranch)}
          value={filters.sucursal}
          onChange={(value) => table.setFilter('sucursal', value)}
          options={warehouses}
          hint={branches.error ? 'No se pudo cargar la lista de sucursales.' : undefined}
        />
        <SelectField label="Categoría" allLabel="Todas las categorías" value={filters.categoria} onChange={(value) => table.setFilter('categoria', value)} options={categories} />
        <SelectField
          label="Marca"
          allLabel="Todas las marcas"
          value={filters.marca}
          onChange={(value) => table.setFilter('marca', value)}
          options={brandChoices}
          hint={brands.error ? 'No se pudieron cargar las marcas.' : undefined}
        />
        <SelectField
          label="Semáforo"
          value={filters.estado}
          onChange={(value) => table.setFilter('estado', value)}
          options={statusOptions(STOCK_STATUSES)}
          disabled={consolidated}
          hint={consolidated ? 'Elija una sucursal para usarlo.' : undefined}
        />
        <SelectField
          label="Reservas"
          allLabel="Con y sin reservas"
          value={filters.reservas}
          onChange={(value) => table.setFilter('reservas', value)}
          options={RESERVATION_OPTIONS}
          disabled={consolidated}
        />
        <SelectField
          label="Rotación"
          allLabel="Con y sin rotación"
          value={filters.rotacion}
          onChange={(value) => table.setFilter('rotacion', value)}
          options={ROTATION_OPTIONS}
          disabled={consolidated || daysWithoutRotation === null}
          hint={daysWithoutRotation !== null ? `Sin rotación: con stock y sin movimientos hace más de ${daysWithoutRotation} días.` : undefined}
        />
      </FilterBar>

      {!consolidated && reservations.error && (
        <Alert tone="warning" title="No se pudieron leer las reservas">
          La lista muestra las existencias sin descontar lo reservado. Pulse «Actualizar» para intentarlo de nuevo.
        </Alert>
      )}

      <Toolbar
        end={
          <>
            <Button
              variant="outline"
              leftIcon={<RefreshCw />}
              loading={consolidated ? totals.fetching && !totals.loading : (projection.fetching && !projection.loading) || (reservations.fetching && !reservations.loading)}
              onClick={reload}
            >
              Actualizar
            </Button>
            <Button variant="outline" leftIcon={<Download />} disabled={visibleCount === 0} onClick={exportRows}>
              Exportar CSV
            </Button>
          </>
        }
      >
        {loaded && (
          <span className="text-sm text-text-muted" data-testid="stock-resumen">
            {formatNumber(visibleCount)} de {formatNumber(totalCount)} productos
            {consolidated
              ? ` · ${formatNumber(totals.data?.branches.length ?? 0)} sucursales`
              : ` · almacén ${projection.data?.warehouseCode ?? ''} · al ${formatDate(projection.data?.today)} · ${formatNumber(withReservations)} con reservas`}
          </span>
        )}
      </Toolbar>

      {consolidated ? (
        <DataTable
          caption="Stock consolidado de las sucursales"
          columns={consolidatedCols}
          rows={totals.data ? consolidatedRows : undefined}
          rowKey={(entry) => entry.key}
          rowLabel={(entry) => `el producto ${entry.row.name}`}
          loading={totals.loading}
          refreshing={totals.fetching && !totals.loading}
          error={totals.error}
          onRetry={totals.reload}
          operation="ConsolidatedStockQuery"
          {...table.tableProps}
          footerLabel="Totales"
          onRowOpen={(entry) => openDetail(entry.row.sku, 'sucursales')}
          activeRowKey={detailSku}
          rowActions={(entry) => [
            { label: 'Ver existencias por sucursal', icon: <Eye />, onSelect: () => openDetail(entry.row.sku, 'sucursales') },
            { label: 'Ver ficha y kardex', icon: <PackageSearch />, onSelect: () => openDetail(entry.row.sku, 'ficha') },
            { label: 'Ver solo esta categoría', icon: <ListFilter />, onSelect: () => table.setFilter('categoria', entry.row.category), hidden: filters.categoria === entry.row.category },
          ]}
          empty={empty}
        />
      ) : (
        <DataTable
          caption="Stock del almacén"
          columns={COLUMNS}
          rows={ready ? rows : undefined}
          rowKey={(entry) => entry.key}
          rowLabel={(entry) => `el producto ${entry.row.name}`}
          loading={!ready && projection.error === null}
          refreshing={(projection.fetching && !projection.loading) || (reservations.fetching && ready)}
          error={projection.error}
          onRetry={projection.reload}
          operation="GetStockProjectionQuery"
          {...table.tableProps}
          renderExpanded={(entry) => <StockRowDetail entry={entry} />}
          onRowOpen={(entry) => openDetail(entry.row.sku)}
          activeRowKey={detailSku}
          rowActions={(entry) => [
            { label: 'Ver ficha y kardex', icon: <Eye />, onSelect: () => openDetail(entry.row.sku) },
            { label: 'Registrar entrada', icon: <PackagePlus />, onSelect: () => navigate(movementLink('ENTRADA', entry.row.sku)), hidden: !canWarehouse },
            { label: 'Registrar salida', icon: <PackageMinus />, onSelect: () => navigate(movementLink('SALIDA', entry.row.sku)), hidden: !canSales },
            { label: 'Ver solo esta categoría', icon: <ListFilter />, onSelect: () => table.setFilter('categoria', entry.row.category), hidden: filters.categoria === entry.row.category },
          ]}
          empty={empty}
        />
      )}

      <StockDetailPanel
        sku={shown.sku}
        open={detailSku !== null}
        onClose={closeDetail}
        initialTab={shown.tab}
        workWarehouse={workWarehouse}
        otherWarehouse={otherWarehouse}
      />
    </Page>
  );
}
