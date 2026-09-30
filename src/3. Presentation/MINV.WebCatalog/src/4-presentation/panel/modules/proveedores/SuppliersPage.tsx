// Compras › Proveedores (paquete M7): la pantalla «Proveedores» del escritorio (SuppliersView) en la web. Lista de
// `GetSuppliersQuery` con filtros en la dirección (búsqueda por nombre, código, NIT o contacto —`?q=`—; estado; con o sin
// órdenes abiertas; plazo de entrega), tabla ordenable, detalle lateral con «Ver sus órdenes de compra»
// (`/panel/compras?proveedor=<código>`) y «Nueva orden de compra», alta y edición en un diálogo (`SaveSupplierCommand`) y
// activar/desactivar (con confirmación al desactivar). Los indicadores del escritorio van PLEGADOS en «Ver resumen de
// los proveedores» (regla P-10). `?nuevo=1` abre «Nuevo proveedor».

import { BarChart3, Clock, ClipboardList, Download, Eye, FilePlus2, Pencil, RefreshCw, Trophy, Truck, UserCheck, UserPlus, UserX } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { usePermissions, useRpcCommand, useRpcQuery, useTableState } from '@/4-presentation/panel/hooks';
import {
  Button,
  Collapsible,
  ConfirmDialog,
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
import { exportCsv, formatMoney, formatNumber, sortRows, type SortState } from '@/4-presentation/panel/lib';
import { SupplierDialog, type SupplierTarget } from './SupplierDialog';
import { useRowMenuGuard } from './rowGuard';
import {
  LEAD_FILTER_OPTIONS,
  OPEN_ORDER_OPTIONS,
  SUPPLIERS_CSV,
  SUPPLIER_FILTERS,
  SUPPLIER_STATES,
  daysText,
  filterSuppliers,
  newOrderLink,
  ordersLink,
  plainMessage,
  suppliersSummary,
  toSupplierItems,
  toggleActivePayload,
  type SupplierItem,
} from './suppliers';

const COLUMNS: DataTableColumn<SupplierItem>[] = [
  {
    id: 'nombre',
    header: 'Proveedor',
    value: (item) => item.row.name,
    card: 'title',
    cell: (item) => (
      <span className="block min-w-44">
        <span className="block">{item.row.name}</span>
        <span className="block text-xs font-normal text-text-muted">{item.row.code}</span>
      </span>
    ),
  },
  { id: 'nit', header: 'NIT', value: (item) => item.row.taxId, className: 'whitespace-nowrap' },
  { id: 'contacto', header: 'Contacto', value: (item) => item.contact, sortable: false, cell: (item) => <span className="block max-w-64 min-w-40 break-words">{item.contact || '—'}</span> },
  { id: 'entrega', header: 'Entrega', align: 'end', value: (item) => item.row.leadTimeDays, cell: (item) => daysText(item.row.leadTimeDays) },
  { id: 'productos', header: 'Productos', align: 'end', value: (item) => item.row.products },
  {
    id: 'abiertas',
    header: 'Órdenes abiertas',
    align: 'end',
    value: (item) => item.row.openOrders,
    footer: (rows) => formatNumber(rows.reduce((sum, item) => sum + item.row.openOrders, 0)),
  },
  {
    id: 'comprado',
    header: 'Comprado',
    align: 'end',
    value: (item) => item.row.purchased,
    cell: (item) => (item.row.purchased > 0 ? formatMoney(item.row.purchased) : '—'),
    footer: (rows) => formatMoney(rows.reduce((sum, item) => sum + item.row.purchased, 0)),
  },
  { id: 'estado', header: 'Estado', value: (item) => SUPPLIER_STATES[item.state].label, cell: (item) => <StatusBadge status={item.state} statuses={SUPPLIER_STATES} /> },
];

function inTableOrder(list: readonly SupplierItem[], sort: SortState | null): readonly SupplierItem[] {
  const column = sort ? COLUMNS.find((item) => item.id === sort.column) : undefined;
  return sort && column?.value ? sortRows(list, column.value, sort.direction) : list;
}

interface Shown<T> {
  item: T;
  open: boolean;
}

export function SuppliersPage() {
  const notify = useNotify();
  const navigate = useNavigate();
  const { canRun } = usePermissions();
  const table = useTableState({ filters: SUPPLIER_FILTERS, sort: { column: 'nombre', direction: 'asc' } });
  const filters = table.filters;
  const [params, setParams] = useSearchParams();

  const suppliers = useRpcQuery('GetSuppliersQuery', {});
  const items = useMemo(() => toSupplierItems(suppliers.data ?? []), [suppliers.data]);
  const rows = useMemo(() => filterSuppliers(items, filters), [items, filters]);
  const summary = useMemo(() => suppliersSummary(suppliers.data ?? []), [suppliers.data]);

  const canEdit = canRun('SaveSupplierCommand');
  const canSeeOrders = canRun('GetPurchaseOrdersQuery');
  const canOrder = canRun('CreatePurchaseOrderCommand');
  const { fromMenu, guardOpen } = useRowMenuGuard();

  const [detail, setDetail] = useState<Shown<SupplierItem> | null>(null);
  const [dialogSession, setDialogSession] = useState(0);
  const [editing, setEditing] = useState<SupplierTarget | null>(null);
  const [deactivating, setDeactivating] = useState<Shown<SupplierItem> | null>(null);
  const toggle = useRpcCommand('SaveSupplierCommand', { notifyError: false });

  const detailItem = detail ? (items.find((item) => item.key === detail.item.key) ?? detail.item) : null;
  const openDetail = (item: SupplierItem) => setDetail({ item, open: true });
  const closeDetail = () => setDetail((current) => (current?.open ? { ...current, open: false } : current));
  const askEdit = (item: SupplierItem | null) => {
    setDialogSession((count) => count + 1);
    setEditing({ row: item?.row ?? null });
  };
  const askDeactivate = (item: SupplierItem) => {
    toggle.reset();
    setDeactivating({ item, open: true });
  };

  // `?nuevo=1`: abre «Nuevo proveedor» una vez y se quita de la dirección.
  const newParam = params.get('nuevo');
  const [handledNew, setHandledNew] = useState<string | null>(null);
  if (newParam !== handledNew) {
    setHandledNew(newParam);
    if (newParam !== null && canEdit) {
      setDialogSession((count) => count + 1);
      setEditing({ row: null });
    }
  }
  useEffect(() => {
    if (newParam === null) return;
    setParams(
      (previous) => {
        const updated = new URLSearchParams(previous);
        updated.delete('nuevo');
        return updated;
      },
      { replace: true },
    );
  }, [newParam, setParams]);

  const activate = async (item: SupplierItem) => {
    const outcome = await toggle.run(toggleActivePayload(item.row, true));
    if (outcome.ok) {
      notify.success('Proveedor activado', `${item.row.code} · ${item.row.name}`);
      suppliers.reload();
    } else {
      notify.error('No se pudo activar el proveedor', outcome.error);
    }
  };

  const confirmDeactivate = async () => {
    const target = deactivating?.item;
    if (!target) return false;
    const outcome = await toggle.run(toggleActivePayload(target.row, false));
    if (outcome.ok) {
      notify.success('Proveedor desactivado', `${target.row.code} · ${target.row.name}`);
      suppliers.reload();
    }
    return outcome;
  };

  const exportRows = () => {
    const file = exportCsv('proveedores', SUPPLIERS_CSV, inTableOrder(rows, table.sort));
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(rows.length)} filas).`);
  };

  const rowActions = (item: SupplierItem) => [
    { label: 'Ver detalle', icon: <Eye />, onSelect: fromMenu(() => openDetail(item)) },
    { label: 'Ver sus órdenes de compra', icon: <ClipboardList />, onSelect: fromMenu(() => navigate(ordersLink(item.row.code))), hidden: !canSeeOrders },
    { label: 'Nueva orden de compra', icon: <FilePlus2 />, onSelect: fromMenu(() => navigate(newOrderLink(item.row.code))), hidden: !canOrder || !item.row.isActive },
    { label: 'Editar', icon: <Pencil />, onSelect: fromMenu(() => askEdit(item)), hidden: !canEdit },
    { label: 'Activar', icon: <UserCheck />, onSelect: fromMenu(() => void activate(item)), hidden: !canEdit || item.row.isActive },
    { label: 'Desactivar', icon: <UserX />, tone: 'danger' as const, onSelect: fromMenu(() => askDeactivate(item)), hidden: !canEdit || !item.row.isActive },
  ];

  const current = detailItem;

  return (
    <Page
      title="Proveedores"
      description="Contactos, NIT y plazos de entrega de los proveedores, con sus órdenes abiertas y lo comprado a cada uno."
      actions={
        canEdit && (
          <Button leftIcon={<UserPlus />} onClick={() => askEdit(null)}>
            Nuevo proveedor
          </Button>
        )
      }
    >
      <FilterBar activeCount={table.activeFilterCount} onClear={table.clearFilters}>
        <SearchField label="Buscar" placeholder="Nombre, código, NIT, contacto o teléfono" value={filters.q} onChange={(q) => table.setFilter('q', q)} />
        <SelectField label="Estado" value={filters.estado} onChange={(value) => table.setFilter('estado', value)} options={statusOptions(SUPPLIER_STATES)} />
        <SelectField label="Órdenes abiertas" value={filters.ordenes} onChange={(value) => table.setFilter('ordenes', value)} options={OPEN_ORDER_OPTIONS} />
        <SelectField label="Plazo de entrega" value={filters.entrega} onChange={(value) => table.setFilter('entrega', value)} options={LEAD_FILTER_OPTIONS} />
      </FilterBar>

      <Toolbar
        end={
          <>
            <Button variant="outline" leftIcon={<RefreshCw />} loading={suppliers.fetching && !suppliers.loading} onClick={suppliers.reload}>
              Actualizar
            </Button>
            <Button variant="outline" leftIcon={<Download />} disabled={rows.length === 0} onClick={exportRows}>
              Exportar CSV
            </Button>
          </>
        }
      >
        {suppliers.data && (
          <span className="text-sm text-text-muted" data-testid="proveedores-resumen">
            {formatNumber(rows.length)} de {formatNumber(items.length)} proveedores · {formatNumber(summary.active)} activos
          </span>
        )}
      </Toolbar>

      <Collapsible label="Ver resumen de los proveedores" openLabel="Ocultar resumen de los proveedores" icon={<BarChart3 />} description="Activos, órdenes abiertas, lo comprado y la entrega promedio.">
        <div className="grid grid-cols-[repeat(auto-fit,minmax(11rem,1fr))] gap-3" data-testid="resumen-proveedores">
          <StatCard label="Proveedores activos" value={formatNumber(summary.active)} hint={`${formatNumber(summary.products)} productos asignados`} icon={<Truck />} />
          <StatCard
            label="Órdenes abiertas"
            value={formatNumber(summary.openOrders)}
            hint={`${formatNumber(summary.withOpenOrders)} proveedores con pedidos en curso`}
            icon={<ClipboardList />}
            tone={summary.openOrders > 0 ? 'warning' : 'default'}
          />
          <StatCard label="Comprado (recibido)" value={formatMoney(summary.purchased)} hint={summary.top ? `Principal: ${summary.top.name}` : undefined} icon={<Trophy />} tone="success" />
          <StatCard
            label="Entrega promedio"
            value={summary.averageLead === null ? '—' : `${formatNumber(summary.averageLead, { maxDecimals: 1 })} días`}
            hint="Plazo que usa el pedido sugerido"
            icon={<Clock />}
          />
        </div>
      </Collapsible>

      <DataTable
        caption="Proveedores"
        columns={COLUMNS}
        rows={suppliers.data ? rows : undefined}
        rowKey={(item) => item.key}
        rowLabel={(item) => `el proveedor ${item.row.name} (${item.row.code})`}
        loading={suppliers.loading}
        refreshing={suppliers.fetching && !suppliers.loading}
        error={suppliers.error}
        onRetry={suppliers.reload}
        operation="GetSuppliersQuery"
        {...table.tableProps}
        onRowOpen={guardOpen(openDetail)}
        activeRowKey={detail?.open ? detail.item.key : null}
        rowActions={rowActions}
        empty={{
          title: items.length === 0 ? 'Todavía no hay proveedores' : 'Ningún proveedor coincide con los filtros',
          description: items.length === 0 ? 'Registre el primero con «Nuevo proveedor».' : 'Pruebe con otra búsqueda o limpie los filtros.',
          icon: <Truck />,
          action:
            items.length === 0 ? (
              canEdit && (
                <Button leftIcon={<UserPlus />} onClick={() => askEdit(null)}>
                  Nuevo proveedor
                </Button>
              )
            ) : (
              <Button variant="outline" onClick={table.clearFilters}>
                Limpiar filtros
              </Button>
            ),
        }}
      />

      <SidePanel
        open={detail?.open ?? false}
        onClose={closeDetail}
        title={current ? current.row.name : 'Proveedor'}
        description={current ? `Proveedor ${current.row.code}` : undefined}
        headerExtra={current && <StatusBadge status={current.state} statuses={SUPPLIER_STATES} />}
        footer={
          current && (
            <div className="flex flex-col gap-2">
              {canSeeOrders && (
                <Button leftIcon={<ClipboardList />} fullWidth to={ordersLink(current.row.code)}>
                  Ver sus órdenes de compra
                </Button>
              )}
              {canOrder && current.row.isActive && (
                <Button variant="outline" leftIcon={<FilePlus2 />} fullWidth to={newOrderLink(current.row.code)}>
                  Nueva orden de compra
                </Button>
              )}
              {canEdit && (
                <Button variant="outline" leftIcon={<Pencil />} fullWidth onClick={() => askEdit(current)}>
                  Editar
                </Button>
              )}
              {canEdit && !current.row.isActive && (
                <Button variant="outline" leftIcon={<UserCheck />} fullWidth loading={toggle.sending} onClick={() => void activate(current)}>
                  Activar
                </Button>
              )}
              {canEdit && current.row.isActive && (
                <Button variant="danger" leftIcon={<UserX />} fullWidth onClick={() => askDeactivate(current)}>
                  Desactivar
                </Button>
              )}
            </div>
          )
        }
      >
        {current && (
          <DetailList
            items={[
              { label: 'Código', value: current.row.code },
              { label: 'NIT', value: current.row.taxId },
              { label: 'Contacto', value: current.row.contact },
              { label: 'Teléfono', value: current.row.phone },
              { label: 'Correo', value: current.row.email },
              { label: 'Días de entrega', value: daysText(current.row.leadTimeDays) },
              { label: 'Productos asignados', value: formatNumber(current.row.products) },
              { label: 'Órdenes abiertas', value: formatNumber(current.row.openOrders) },
              { label: 'Comprado (recibido)', value: formatMoney(current.row.purchased), wide: true },
            ]}
          />
        )}
      </SidePanel>

      <SupplierDialog key={`proveedor-${dialogSession}`} target={editing} onClose={() => setEditing(null)} onSaved={() => suppliers.reload()} />
      <ConfirmDialog
        open={deactivating?.open ?? false}
        onClose={() => setDeactivating((shown) => (shown?.open ? { ...shown, open: false } : shown))}
        tone="danger"
        title={`¿Desactivar a ${deactivating?.item.row.name ?? 'este proveedor'}?`}
        message="No recibe órdenes de compra nuevas. Sus órdenes, productos y compras se conservan, y se puede volver a activar cuando haga falta."
        confirmLabel="Desactivar"
        onConfirm={confirmDeactivate}
        error={toggle.errorText ? plainMessage(toggle.errorText) : undefined}
      />
    </Page>
  );
}
