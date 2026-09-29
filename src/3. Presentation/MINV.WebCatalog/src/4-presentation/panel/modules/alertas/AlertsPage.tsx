// Inventario › Alertas (en el escritorio: AlertsView + AlertsViewModel). La vista PRIORIZADA de los productos que
// necesitan atención en un almacén (sin stock, críticos, bajos, inconsistentes y con exceso), con la acción sugerida de
// cada una. Filtros en la dirección: tipo, categoría, proveedor, sucursal y búsqueda. Tabla ordenable y paginada,
// detalle lateral y accesos: ficha y kardex («Stock»), registrar la entrada o el ajuste («Movimientos») y el pedido
// sugerido. Resumen por tipo PLEGADO. Operaciones: GetStockProjectionQuery y GetBranchesQuery (`inventory.stock.view`).

import { ChartColumn, CircleCheck, ClipboardList, Download, Eye, ListFilter, PackagePlus, PackageSearch, RefreshCw, ShoppingCart, Wrench } from 'lucide-react';
import { useMemo, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { ROUTES } from '@/4-presentation/app/routes';
import { usePermissions, useRpcQuery, useTableState } from '@/4-presentation/panel/hooks';
import {
  Button,
  Collapsible,
  DataTable,
  DetailList,
  FilterBar,
  Page,
  SearchField,
  SelectField,
  SidePanel,
  StatCard,
  StatusBadge,
  Toolbar,
  statusOptions,
  useNotify,
  type DataTableColumn,
} from '@/4-presentation/panel/kit';
import { exportCsv, formatDate, formatNumber, formatQuantity, sortRows, type SortState } from '@/4-presentation/panel/lib';
import {
  ALERT_CSV,
  ALERT_FILTERS,
  ALERT_TYPES,
  activeWarehouseLabel,
  adjustmentLink,
  alertCounts,
  alertTypeOrder,
  categoryOptions,
  countText,
  entryLink,
  filterAlerts,
  isReplenishable,
  lastMovementText,
  orderLink,
  productCardLink,
  quantityWithUnit,
  suggestedAction,
  supplierOf,
  supplierOptions,
  warehouseOptions,
  type AlertRecord,
} from './alerts';

const COLUMNS: DataTableColumn<AlertRecord>[] = [
  { id: 'prioridad', header: 'Prioridad', align: 'end', value: (alert) => alert.position, cell: (alert) => `#${alert.position}`, className: 'w-24' },
  {
    id: 'producto',
    header: 'Producto',
    card: 'title',
    value: (alert) => alert.name,
    cell: (alert) => (
      <span className="block min-w-48">
        <span className="block">{alert.name}</span>
        <span className="block text-xs font-normal text-text-muted">
          {alert.sku} · {alert.category}
        </span>
      </span>
    ),
  },
  { id: 'tipo', header: 'Tipo', value: (alert) => alertTypeOrder(alert.status), cell: (alert) => <StatusBadge status={alert.status} statuses={ALERT_TYPES} /> },
  {
    id: 'existencias',
    header: 'Existencias',
    align: 'end',
    value: (alert) => alert.stock,
    cell: (alert) => (
      <span className="block">
        <span className="block">{quantityWithUnit(alert.stock, alert.unit)}</span>
        <span className="block text-xs text-text-muted">mín. {formatQuantity(alert.minimum)}</span>
      </span>
    ),
  },
  { id: 'faltan', header: 'Faltan', align: 'end', value: (alert) => alert.shortfall, cell: (alert) => (alert.shortfall > 0 ? formatQuantity(alert.shortfall) : '—') },
  {
    id: 'sugerido',
    header: 'Sugerido',
    align: 'end',
    value: (alert) => alert.suggestedQuantity,
    cell: (alert) => (alert.suggestedQuantity > 0 ? formatQuantity(alert.suggestedQuantity) : '—'),
  },
  {
    id: 'accion',
    header: 'Acción sugerida',
    sortable: false,
    value: (alert) => suggestedAction(alert.status),
    cell: (alert) => <span className="block min-w-44 text-text-muted">{suggestedAction(alert.status)}</span>,
  },
];

function inTableOrder(list: readonly AlertRecord[], sort: SortState | null): readonly AlertRecord[] {
  const column = sort ? COLUMNS.find((item) => item.id === sort.column) : undefined;
  return sort && column?.value ? sortRows(list, column.value, sort.direction) : list;
}

export function AlertsPage() {
  const notify = useNotify();
  const navigate = useNavigate();
  const { can, session } = usePermissions();
  const table = useTableState({ filters: ALERT_FILTERS, sort: { column: 'prioridad', direction: 'asc' } });
  const filters = table.filters;
  const warehouseCode = filters.sucursal || null;

  const branches = useRpcQuery('GetBranchesQuery', {});
  const projection = useRpcQuery('GetStockProjectionQuery', { warehouseCode });
  const alerts = useMemo(() => projection.data?.result.alerts ?? [], [projection.data]);
  const rows = useMemo(() => filterAlerts(alerts, filters), [alerts, filters]);
  const categories = useMemo(() => categoryOptions(alerts), [alerts]);
  const suppliers = useMemo(() => supplierOptions(alerts), [alerts]);
  const warehouses = useMemo(() => warehouseOptions(branches.data ?? []), [branches.data]);
  const activeBranch = session?.access.branches.find((branch) => branch.id === session.access.activeBranchId) ?? null;

  const canWarehouse = can('inventory.movements.register.warehouse');
  const [detail, setDetail] = useState<{ alert: AlertRecord; open: boolean } | null>(null);
  const openDetail = (alert: AlertRecord) => setDetail({ alert, open: true });

  // El menú «⋯» se dibuja aparte (portal) y React le pasa el clic a la fila: mientras se atiende una acción del menú, el
  // clic de la fila no abre el detalle (ver «Pendientes» del informe M5).
  const pickingAction = useRef(false);
  const fromMenu = (action: () => void) => () => {
    pickingAction.current = true;
    action();
    setTimeout(() => {
      pickingAction.current = false;
    }, 0);
  };
  const onlySupplier = (alert: AlertRecord) => {
    table.setFilter('proveedor', supplierOf(alert));
    setDetail((current) => (current ? { ...current, open: false } : current));
  };

  const exportRows = () => {
    const file = exportCsv(`alertas ${projection.data?.warehouseCode ?? ''}`, ALERT_CSV, inTableOrder(rows, table.sort));
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(rows.length)} filas).`);
  };

  const counts = alertCounts(alerts);
  const alert = detail?.alert;

  return (
    <Page
      title="Alertas"
      description="Productos que necesitan atención, ordenados por urgencia, con la acción sugerida para cada uno."
      actions={
        <>
          <Button variant="outline" leftIcon={<PackageSearch />} to={ROUTES.panelModule('stock')}>
            Ver el stock
          </Button>
          <Button leftIcon={<ShoppingCart />} to={orderLink()}>
            Ver pedido sugerido
          </Button>
        </>
      }
    >
      <Collapsible label="Ver resumen de alertas" openLabel="Ocultar resumen de alertas" icon={<ChartColumn />} description="Cuántas alertas hay de cada tipo y qué hacer.">
        {projection.data ? (
          counts.total === 0 ? (
            <p className="text-sm text-text-muted">Sin alertas en este almacén.</p>
          ) : (
            <div className="grid grid-cols-[repeat(auto-fit,minmax(11rem,1fr))] gap-3" data-testid="resumen-alertas">
              {counts.byType.map((item) => (
                <StatCard
                  key={item.status}
                  label={item.label}
                  value={formatNumber(item.count)}
                  hint={item.action}
                  tone={item.status === 'OutOfStock' || item.status === 'Critical' ? 'danger' : item.status === 'Low' ? 'warning' : 'default'}
                />
              ))}
            </div>
          )
        ) : (
          <p className="text-sm text-text-muted">Cargando el resumen…</p>
        )}
      </Collapsible>

      <FilterBar activeCount={table.activeFilterCount} onClear={table.clearFilters}>
        <SearchField label="Buscar" placeholder="SKU, nombre, categoría o proveedor" value={filters.q} onChange={(q) => table.setFilter('q', q)} />
        <SelectField label="Tipo" allLabel="Todos los tipos" value={filters.tipo} onChange={(value) => table.setFilter('tipo', value)} options={statusOptions(ALERT_TYPES)} />
        <SelectField label="Categoría" allLabel="Todas las categorías" value={filters.categoria} onChange={(value) => table.setFilter('categoria', value)} options={categories} />
        <SelectField label="Proveedor" allLabel="Todos los proveedores" value={filters.proveedor} onChange={(value) => table.setFilter('proveedor', value)} options={suppliers} />
        <SelectField
          label="Sucursal"
          allLabel={activeWarehouseLabel(activeBranch)}
          value={filters.sucursal}
          onChange={(value) => table.setFilter('sucursal', value)}
          options={warehouses}
          hint={branches.error ? 'No se pudo cargar la lista de sucursales.' : undefined}
        />
      </FilterBar>

      <Toolbar
        end={
          <>
            <Button variant="outline" leftIcon={<RefreshCw />} loading={projection.fetching && !projection.loading} onClick={projection.reload}>
              Actualizar
            </Button>
            <Button variant="outline" leftIcon={<Download />} disabled={rows.length === 0} onClick={exportRows}>
              Exportar CSV
            </Button>
          </>
        }
      >
        {projection.data && (
          <span className="text-sm text-text-muted" data-testid="alertas-resumen">
            {countText(rows.length, alerts.length)} · almacén {projection.data.warehouseCode} · al {formatDate(projection.data.today)}
          </span>
        )}
      </Toolbar>

      <DataTable
        caption="Alertas del almacén"
        columns={COLUMNS}
        rows={projection.data ? rows : undefined}
        rowKey={(item) => item.sku}
        rowLabel={(item) => `la alerta de ${item.name}`}
        loading={projection.loading}
        refreshing={projection.fetching && !projection.loading}
        error={projection.error}
        onRetry={projection.reload}
        operation="GetStockProjectionQuery"
        {...table.tableProps}
        renderExpanded={(item) => (
          <DetailList
            items={[
              { label: 'Proveedor', value: supplierOf(item) },
              { label: 'Máximo', value: quantityWithUnit(item.maximum, item.unit) },
              { label: 'Último movimiento', value: lastMovementText(item) },
            ]}
          />
        )}
        onRowOpen={(item) => {
          if (!pickingAction.current) openDetail(item);
        }}
        activeRowKey={detail?.open ? detail.alert.sku : null}
        rowActions={(item) => [
          { label: 'Ver detalle', icon: <Eye />, onSelect: fromMenu(() => openDetail(item)) },
          { label: 'Ver ficha y kardex', icon: <PackageSearch />, onSelect: fromMenu(() => navigate(productCardLink(item.sku))) },
          { label: 'Registrar entrada', icon: <PackagePlus />, onSelect: fromMenu(() => navigate(entryLink(item.sku))), hidden: !canWarehouse || !isReplenishable(item.status) },
          { label: 'Registrar ajuste', icon: <Wrench />, onSelect: fromMenu(() => navigate(adjustmentLink(item.sku))), hidden: !canWarehouse || isReplenishable(item.status) },
          { label: 'Ver en el pedido sugerido', icon: <ShoppingCart />, onSelect: fromMenu(() => navigate(orderLink(item.sku))), hidden: !isReplenishable(item.status) },
          { label: 'Ver solo este proveedor', icon: <ListFilter />, onSelect: fromMenu(() => onlySupplier(item)), hidden: filters.proveedor === supplierOf(item) },
        ]}
        empty={{
          title: alerts.length === 0 ? '¡Todo en orden!' : 'No hay alertas con estos filtros',
          description:
            alerts.length === 0 ? 'Ningún producto está sin stock, crítico, bajo, inconsistente ni con exceso.' : 'Pruebe con otra búsqueda o limpie los filtros.',
          icon: alerts.length === 0 ? <CircleCheck /> : <ClipboardList />,
          action:
            alerts.length === 0 ? undefined : (
              <Button variant="outline" onClick={table.clearFilters}>
                Limpiar filtros
              </Button>
            ),
        }}
      />

      <SidePanel
        open={detail?.open ?? false}
        onClose={() => setDetail((current) => (current?.open ? { ...current, open: false } : current))}
        title={alert ? alert.name : 'Alerta'}
        description={alert ? `${alert.sku} · prioridad #${alert.position}` : undefined}
        headerExtra={alert && <StatusBadge status={alert.status} statuses={ALERT_TYPES} />}
        footer={
          alert && (
            <div className="flex flex-col gap-2">
              {canWarehouse && isReplenishable(alert.status) && (
                <Button leftIcon={<PackagePlus />} fullWidth to={entryLink(alert.sku)}>
                  Registrar entrada
                </Button>
              )}
              {canWarehouse && !isReplenishable(alert.status) && (
                <Button leftIcon={<Wrench />} fullWidth to={adjustmentLink(alert.sku)}>
                  Registrar ajuste
                </Button>
              )}
              {isReplenishable(alert.status) && (
                <Button variant="outline" leftIcon={<ShoppingCart />} fullWidth to={orderLink(alert.sku)}>
                  Ver en el pedido sugerido
                </Button>
              )}
              <Button variant="outline" leftIcon={<PackageSearch />} fullWidth to={productCardLink(alert.sku)}>
                Ver ficha y kardex
              </Button>
              {filters.proveedor !== supplierOf(alert) && (
                <Button variant="ghost" leftIcon={<ListFilter />} fullWidth onClick={() => onlySupplier(alert)}>
                  Ver solo las alertas de {supplierOf(alert)}
                </Button>
              )}
            </div>
          )
        }
      >
        {alert && (
          <div data-testid="detalle-alerta">
            <DetailList
              items={[
                { label: 'Acción sugerida', value: suggestedAction(alert.status), wide: true },
                { label: 'Existencias', value: quantityWithUnit(alert.stock, alert.unit) },
                { label: 'Mínimo / máximo', value: `${formatQuantity(alert.minimum)} / ${formatQuantity(alert.maximum)}` },
                { label: 'Faltan para el mínimo', value: alert.shortfall > 0 ? quantityWithUnit(alert.shortfall, alert.unit) : 'Nada' },
                { label: 'Cantidad sugerida', value: alert.suggestedQuantity > 0 ? quantityWithUnit(alert.suggestedQuantity, alert.unit) : 'Nada' },
                { label: 'Categoría', value: alert.category },
                { label: 'Proveedor', value: supplierOf(alert) },
                { label: 'Movimientos', value: lastMovementText(alert), wide: true },
              ]}
            />
          </div>
        )}
      </SidePanel>
    </Page>
  );
}
