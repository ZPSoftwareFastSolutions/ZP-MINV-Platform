// Compras › Órdenes de compra (paquete M7): lo mismo que «Órdenes de compra» del escritorio (PurchaseOrdersView +
// PurchasingViewModels) y más, en dos pestañas para no mezclar:
//   · «Órdenes»: lista de `GetPurchaseOrdersQuery` con filtros en la dirección (búsqueda, estado —también «Por recibir» y
//     «Abiertas»—, proveedor `?proveedor=<código>`, sucursal y fechas), tabla ordenable, detalle lateral con líneas,
//     recibido y pendiente, y las acciones: nueva orden (`CreatePurchaseOrderCommand`), desde el pedido sugerido
//     (`CreateSuggestedPurchaseOrdersCommand`), aprobar, recibir (con series) y anular, cada una con su confirmación.
//   · «Facturas de proveedores»: recepciones sin factura y facturas registradas (`InvoicesTab`).
// Los indicadores del escritorio van PLEGADOS en «Ver resumen de las compras» (regla P-10). Cada acción solo se ofrece a
// quien puede ejecutar su comando (`canRun`); el servidor decide igual (regla P-01).

import { BarChart3, Ban, CircleCheck, ClipboardList, Download, Eye, FilePlus2, FileText, Filter, PackageCheck, RefreshCw, Sparkles, Truck } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { ROUTES } from '@/4-presentation/app/routes';
import { usePermissions, useRpcCommand, useRpcQuery, useTableState } from '@/4-presentation/panel/hooks';
import {
  Button,
  Collapsible,
  ConfirmDialog,
  DataTable,
  DateRangeField,
  FilterBar,
  Page,
  SearchField,
  SelectField,
  StatusBadge,
  TabPanel,
  Tabs,
  Toolbar,
  useNotify,
  type DataTableColumn,
  type TabItem,
} from '@/4-presentation/panel/kit';
import { exportCsv, formatDate, formatMoney, formatNumber, laPazToday, sortRows, toDate, type SortState } from '@/4-presentation/panel/lib';
import { InvoicesTab } from './InvoicesTab';
import { NewOrderDialog } from './NewOrderDialog';
import { OrderDetailPanel, type OrderPermissions } from './OrderDetailPanel';
import { PurchasesOverview } from './PurchasesOverview';
import { ReceiveOrderDialog } from './ReceiveOrderDialog';
import { useTabParam } from './params';
import {
  ORDERS_CSV,
  ORDER_FILTERS,
  ORDER_STATES,
  branchOptions,
  filterOrders,
  orderStateLabel,
  orderStateOptions,
  plainMessage,
  purchaseSummary,
  supplierOptions,
  toOrderItem,
  toOrderItems,
  type OrderItem,
  type OrderRecord,
  type ReceiptOutcome,
} from './purchasing';
import { useRowMenuGuard } from './rowGuard';

type PurchasesTab = 'ordenes' | 'facturas';
const TABS: readonly PurchasesTab[] = ['ordenes', 'facturas'];

const COLUMNS: DataTableColumn<OrderItem>[] = [
  {
    id: 'numero',
    header: 'Orden',
    value: (item) => item.row.number,
    card: 'title',
    className: 'whitespace-nowrap',
  },
  {
    id: 'proveedor',
    header: 'Proveedor',
    value: (item) => item.row.supplier,
    cell: (item) => (
      <span className="block min-w-40">
        <span className="block">{item.row.supplier}</span>
        <span className="block text-xs text-text-muted">{item.row.supplierCode}</span>
      </span>
    ),
  },
  { id: 'fecha', header: 'Fecha', value: (item) => toDate(item.row.orderDate), cell: (item) => formatDate(item.row.orderDate), className: 'whitespace-nowrap' },
  {
    id: 'entrega',
    header: 'Entrega',
    value: (item) => toDate(item.row.expectedDate),
    cell: (item) => (
      <span className="block whitespace-nowrap">
        {formatDate(item.row.expectedDate)}
        {item.overdue && <span className="block text-xs font-semibold text-warning-text">Vencida</span>}
      </span>
    ),
  },
  { id: 'estado', header: 'Estado', value: (item) => orderStateLabel(item.row.status), cell: (item) => <StatusBadge status={item.row.status} statuses={ORDER_STATES} /> },
  { id: 'lineas', header: 'Líneas', align: 'end', value: (item) => item.row.lines },
  {
    id: 'total',
    header: 'Total',
    align: 'end',
    value: (item) => item.row.total,
    cell: (item) => formatMoney(item.row.total),
    footer: (rows) => formatMoney(rows.reduce((sum, item) => sum + item.row.total, 0)),
  },
  { id: 'recibido', header: 'Recibido', align: 'end', value: (item) => item.row.receivedPercent, cell: (item) => `${formatNumber(item.row.receivedPercent)} %` },
];

/** Las filas en el orden de la tabla (para exportar lo mismo que se ve). */
function inTableOrder(list: readonly OrderItem[], sort: SortState | null): readonly OrderItem[] {
  const column = sort ? COLUMNS.find((item) => item.id === sort.column) : undefined;
  return sort && column?.value ? sortRows(list, column.value, sort.direction) : list;
}

interface Shown<T> {
  item: T;
  open: boolean;
}

export function PurchaseOrdersPage() {
  const notify = useNotify();
  const navigate = useNavigate();
  const { can, canRun, session } = usePermissions();
  const [today] = useState(() => laPazToday());
  const [tab, setTab] = useTabParam<PurchasesTab>(TABS, 'ordenes');
  const table = useTableState({ filters: ORDER_FILTERS, sort: { column: 'fecha', direction: 'desc' } });
  const filters = table.filters;
  const [params, setParams] = useSearchParams();

  const orders = useRpcQuery('GetPurchaseOrdersQuery', { status: null });
  const suppliers = useRpcQuery('GetSuppliersQuery', {});
  const items = useMemo(() => toOrderItems(orders.data ?? [], today), [orders.data, today]);
  const rows = useMemo(() => filterOrders(items, filters), [items, filters]);
  const supplierChoices = useMemo(() => supplierOptions(suppliers.data ?? [], items), [suppliers.data, items]);
  const branchChoices = useMemo(() => branchOptions(session?.access.branches ?? [], items.map((item) => item.branchCode)), [session, items]);
  const summary = useMemo(() => purchaseSummary(orders.data ?? [], today), [orders.data, today]);

  const allowed: OrderPermissions = {
    approve: canRun('ApprovePurchaseOrderCommand'),
    receive: canRun('ReceivePurchaseOrderCommand'),
    cancel: canRun('CancelPurchaseOrderCommand'),
    invoices: canRun('GetSupplierInvoicesQuery'),
    suppliers: can('purchasing.manage') && can('inventory.stock.view'),
  };
  const canCreate = canRun('CreatePurchaseOrderCommand');
  const canSuggest = canRun('CreateSuggestedPurchaseOrdersCommand');
  const { fromMenu, guardOpen } = useRowMenuGuard();

  // Detalle y diálogos (cada diálogo con formulario se monta de nuevo en cada apertura: `key`).
  const [detail, setDetail] = useState<Shown<OrderItem> | null>(null);
  const [dialogSession, setDialogSession] = useState(0);
  const [creating, setCreating] = useState(false);
  const [receiving, setReceiving] = useState<OrderItem | null>(null);
  const [approving, setApproving] = useState<Shown<OrderItem> | null>(null);
  const [cancelling, setCancelling] = useState<Shown<OrderItem> | null>(null);
  const [suggesting, setSuggesting] = useState(false);
  const approve = useRpcCommand('ApprovePurchaseOrderCommand', { notifyError: false });
  const cancel = useRpcCommand('CancelPurchaseOrderCommand', { notifyError: false });
  const suggest = useRpcCommand('CreateSuggestedPurchaseOrdersCommand', { notifyError: false });

  const detailItem = detail ? (items.find((item) => item.key === detail.item.key) ?? detail.item) : null;
  const openDetail = (item: OrderItem) => setDetail({ item, open: true });
  const closeDetail = () => setDetail((current) => (current?.open ? { ...current, open: false } : current));
  const askCreate = () => {
    setDialogSession((count) => count + 1);
    setCreating(true);
  };
  const askReceive = (item: OrderItem) => {
    setDialogSession((count) => count + 1);
    setReceiving(item);
  };
  const askApprove = (item: OrderItem) => {
    approve.reset();
    setApproving({ item, open: true });
  };
  const askCancel = (item: OrderItem) => {
    cancel.reset();
    setCancelling({ item, open: true });
  };
  const askSuggest = () => {
    suggest.reset();
    setSuggesting(true);
  };
  const onlySupplier = (item: OrderItem) => {
    table.setFilter('proveedor', item.row.supplierCode);
    closeDetail();
  };

  // `?nueva=1` (tablero «Nueva orden de compra» o Proveedores): abre la orden nueva una vez y se quita de la dirección.
  const newParam = params.get('nueva');
  const [handledNew, setHandledNew] = useState<string | null>(null);
  if (newParam !== handledNew) {
    setHandledNew(newParam);
    if (newParam !== null && canCreate) {
      setDialogSession((count) => count + 1);
      setCreating(true);
    }
  }
  useEffect(() => {
    if (newParam === null) return;
    setParams(
      (previous) => {
        const updated = new URLSearchParams(previous);
        updated.delete('nueva');
        return updated;
      },
      { replace: true },
    );
  }, [newParam, setParams]);

  const created = (row: OrderRecord) => {
    notify.success(`Orden ${row.number} creada en borrador`, `${row.supplier} · ${formatMoney(row.total)}. Apruébela para poder recibirla.`);
    orders.reload();
    suppliers.reload();
    if (tab !== 'ordenes') setTab('ordenes');
    setDetail({ item: toOrderItem(row, today), open: true });
  };

  const received = (result: ReceiptOutcome) => {
    notify.success(
      `Recepción ${result.receiptNumber} registrada`,
      `Orden ${result.orderNumber}: ${formatNumber(result.lines)} líneas · ${formatMoney(result.total)} · asiento ${result.journalNumber}`,
    );
    orders.reload();
    suppliers.reload();
  };

  const confirmApprove = async () => {
    const target = approving?.item;
    if (!target) return false;
    const outcome = await approve.run({ id: target.key });
    if (outcome.ok) {
      notify.success('Orden aprobada', `${target.row.number}: ya se puede recibir.`);
      orders.reload();
    }
    return outcome;
  };

  const confirmCancel = async () => {
    const target = cancelling?.item;
    if (!target) return false;
    const outcome = await cancel.run({ id: target.key });
    if (outcome.ok) {
      notify.success('Orden anulada', target.row.number);
      orders.reload();
      suppliers.reload();
    }
    return outcome;
  };

  const confirmSuggest = async () => {
    const outcome = await suggest.run({});
    if (outcome.ok) {
      const numbers = outcome.result;
      notify.success(numbers.length === 1 ? '1 orden creada en borrador' : `${formatNumber(numbers.length)} órdenes creadas en borrador`, numbers.join(', '));
      setTab('ordenes', { estado: 'Draft' });
      orders.reload();
      suppliers.reload();
    }
    return outcome;
  };

  const exportRows = () => {
    const file = exportCsv('ordenes-de-compra', ORDERS_CSV, inTableOrder(rows, table.sort));
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(rows.length)} filas).`);
  };

  const rowActions = (item: OrderItem) => [
    { label: 'Ver detalle', icon: <Eye />, onSelect: fromMenu(() => openDetail(item)) },
    { label: 'Recibir mercadería', icon: <PackageCheck />, onSelect: fromMenu(() => askReceive(item)), hidden: !allowed.receive || !item.abilities.receive },
    { label: 'Aprobar', icon: <CircleCheck />, onSelect: fromMenu(() => askApprove(item)), hidden: !allowed.approve || !item.abilities.approve },
    {
      label: 'Ver solo este proveedor',
      icon: <Filter />,
      onSelect: fromMenu(() => onlySupplier(item)),
      hidden: filters.proveedor.toUpperCase() === item.row.supplierCode.toUpperCase(),
    },
    {
      label: 'Ver el proveedor',
      icon: <Truck />,
      onSelect: fromMenu(() => navigate(ROUTES.panelModule(`proveedores?q=${encodeURIComponent(item.row.supplierCode)}`))),
      hidden: !allowed.suppliers,
    },
    { label: 'Anular', icon: <Ban />, tone: 'danger' as const, onSelect: fromMenu(() => askCancel(item)), hidden: !allowed.cancel || !item.abilities.cancel },
  ];

  const tabItems: TabItem<PurchasesTab>[] = [
    { id: 'ordenes', label: 'Órdenes', icon: <ClipboardList /> },
    ...(allowed.invoices ? [{ id: 'facturas' as const, label: 'Facturas de proveedores', icon: <FileText /> }] : []),
  ];

  const approvingItem = approving?.item;
  const cancellingItem = cancelling?.item;

  return (
    <Page
      title="Órdenes de compra"
      description="Pida mercadería a los proveedores, apruebe las órdenes, reciba lo que llega (entra al stock) y registre sus facturas."
      actions={
        <>
          {canSuggest && (
            <Button variant="outline" leftIcon={<Sparkles />} onClick={askSuggest}>
              Desde el pedido sugerido
            </Button>
          )}
          {canCreate && (
            <Button leftIcon={<FilePlus2 />} onClick={askCreate}>
              Nueva orden
            </Button>
          )}
        </>
      }
    >
      <Tabs label="Secciones de las compras" value={tab} onChange={(next) => setTab(next)} tabs={tabItems}>
        <TabPanel id="ordenes" className="space-y-5">
          <FilterBar activeCount={table.activeFilterCount} onClear={table.clearFilters}>
            <SearchField label="Buscar" placeholder="Número, proveedor o notas" value={filters.q} onChange={(q) => table.setFilter('q', q)} />
            <SelectField label="Estado" value={filters.estado} onChange={(value) => table.setFilter('estado', value)} options={orderStateOptions()} />
            <SelectField label="Proveedor" allLabel="Todos los proveedores" value={filters.proveedor} onChange={(value) => table.setFilter('proveedor', value)} options={supplierChoices} />
            <SelectField label="Sucursal" allLabel="Todas las sucursales" value={filters.sucursal} onChange={(value) => table.setFilter('sucursal', value)} options={branchChoices} />
            <DateRangeField label="Fecha de la orden" value={table.dateRange()} onChange={(range) => table.setDateRange(range)} />
          </FilterBar>

          <Toolbar
            end={
              <>
                <Button variant="outline" leftIcon={<RefreshCw />} loading={orders.fetching && !orders.loading} onClick={orders.reload}>
                  Actualizar
                </Button>
                <Button variant="outline" leftIcon={<Download />} disabled={rows.length === 0} onClick={exportRows}>
                  Exportar CSV
                </Button>
              </>
            }
          >
            {orders.data && (
              <span className="text-sm text-text-muted" data-testid="ordenes-resumen">
                {formatNumber(rows.length)} de {formatNumber(items.length)} órdenes · {formatNumber(rows.filter((item) => item.abilities.receive).length)} por recibir
              </span>
            )}
          </Toolbar>

          <Collapsible
            label="Ver resumen de las compras"
            openLabel="Ocultar resumen de las compras"
            icon={<BarChart3 />}
            description="Borradores, órdenes por recibir, lo recibido este mes y proveedores con órdenes."
          >
            <PurchasesOverview summary={orders.data ? summary : null} loading={!orders.data} />
          </Collapsible>

          <DataTable
            caption="Órdenes de compra"
            columns={COLUMNS}
            rows={orders.data ? rows : undefined}
            rowKey={(item) => item.key}
            rowLabel={(item) => `la orden ${item.row.number} de ${item.row.supplier}`}
            loading={orders.loading}
            refreshing={orders.fetching && !orders.loading}
            error={orders.error}
            onRetry={orders.reload}
            operation="GetPurchaseOrdersQuery"
            {...table.tableProps}
            onRowOpen={guardOpen(openDetail)}
            activeRowKey={detail?.open ? detail.item.key : null}
            rowActions={rowActions}
            empty={{
              title: items.length === 0 ? 'Todavía no hay órdenes de compra' : 'Ninguna orden coincide con los filtros',
              description: items.length === 0 ? 'Cree la primera con «Nueva orden» o desde el pedido sugerido.' : 'Pruebe con otro estado, proveedor o fechas.',
              icon: <ClipboardList />,
              action:
                items.length === 0 ? (
                  canCreate && (
                    <Button leftIcon={<FilePlus2 />} onClick={askCreate}>
                      Nueva orden
                    </Button>
                  )
                ) : (
                  <Button variant="outline" onClick={table.clearFilters}>
                    Limpiar filtros
                  </Button>
                ),
            }}
          />
        </TabPanel>
        <TabPanel id="facturas">
          <InvoicesTab />
        </TabPanel>
      </Tabs>

      <OrderDetailPanel
        item={detailItem}
        open={detail?.open ?? false}
        onClose={closeDetail}
        allowed={allowed}
        onApprove={askApprove}
        onReceive={askReceive}
        onCancel={askCancel}
        onOnlySupplier={onlySupplier}
        filteredBySupplier={Boolean(detailItem && filters.proveedor.toUpperCase() === detailItem.row.supplierCode.toUpperCase())}
      />

      <NewOrderDialog
        key={`orden-${dialogSession}`}
        open={creating}
        suppliers={suppliers.data ?? []}
        initialSupplier={filters.proveedor || null}
        onClose={() => setCreating(false)}
        onCreated={created}
      />
      <ReceiveOrderDialog key={`recepcion-${dialogSession}`} target={receiving} onClose={() => setReceiving(null)} onReceived={received} />

      <ConfirmDialog
        open={approving?.open ?? false}
        onClose={() => setApproving((shown) => (shown?.open ? { ...shown, open: false } : shown))}
        title={`¿Aprobar la orden ${approvingItem?.row.number ?? ''}?`}
        message={
          approvingItem
            ? `${approvingItem.row.supplier} · ${formatNumber(approvingItem.row.lines)} líneas · ${formatMoney(approvingItem.row.total)}. Una orden aprobada ya no se edita: queda lista para recibir.`
            : ''
        }
        confirmLabel="Aprobar"
        onConfirm={confirmApprove}
        error={approve.errorText ? plainMessage(approve.errorText) : undefined}
      />
      <ConfirmDialog
        open={cancelling?.open ?? false}
        onClose={() => setCancelling((shown) => (shown?.open ? { ...shown, open: false } : shown))}
        tone="danger"
        title={`¿Anular la orden ${cancellingItem?.row.number ?? ''}?`}
        message={cancellingItem ? `La orden a ${cancellingItem.row.supplier} por ${formatMoney(cancellingItem.row.total)} queda anulada (no se borra). No se puede deshacer.` : ''}
        confirmLabel="Anular orden"
        onConfirm={confirmCancel}
        error={cancel.errorText ? plainMessage(cancel.errorText) : undefined}
      />
      <ConfirmDialog
        open={suggesting}
        onClose={() => setSuggesting(false)}
        title="¿Crear órdenes desde el pedido sugerido?"
        message="Se crea una orden en borrador por cada proveedor con productos bajo el mínimo (hasta el máximo). Los proveedores que ya tienen una orden abierta se omiten."
        confirmLabel="Crear órdenes"
        onConfirm={confirmSuggest}
        error={suggest.errorText ? plainMessage(suggest.errorText) : undefined}
      >
        <Button variant="ghost" to={ROUTES.panelModule('pedido')}>
          Revisar antes el pedido sugerido
        </Button>
      </ConfirmDialog>
    </Page>
  );
}
