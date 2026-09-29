// Compras › Pedido sugerido (en el escritorio: OrderView + OrderViewModel, y «Desde el pedido sugerido» de
// PurchaseOrdersView). Qué comprar y a quién en la sucursal activa: los productos sin stock, críticos o bajos hasta su
// máximo, agrupados por proveedor. Filtros en la dirección (proveedor, categoría, semáforo y búsqueda); dos pestañas:
//   - Productos: las líneas con la cantidad EDITABLE (se revisa antes de pedir), subtotal y total.
//   - Proveedores: el pedido de cada uno (total, entrega, contacto y órdenes abiertas), con detalle lateral, «Copiar
//     pedido» (correo o WhatsApp) y «Crear orden de compra» con las cantidades revisadas (`CreatePurchaseOrderCommand`).
// «Generar órdenes de compra» envía `CreateSuggestedPurchaseOrdersCommand` (una orden en borrador por proveedor, con las
// cantidades que calcula el servidor; omite a los que ya tienen una orden abierta), con confirmación y enlace a
// «Órdenes de compra» (`/panel/compras`). Ver el pedido exige `inventory.stock.view`; crear órdenes, `purchasing.manage`.

import { ChartColumn, CircleX, ClipboardCopy, ClipboardList, Download, Eye, FilePlus2, ListFilter, PackageSearch, RefreshCw, RotateCcw, ShoppingCart, Undo2 } from 'lucide-react';
import { useMemo, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { usePermissions, useRpcCommand, useRpcQuery, useTableState } from '@/4-presentation/panel/hooks';
import {
  Alert,
  BarList,
  Button,
  Collapsible,
  ConfirmDialog,
  DataTable,
  DetailList,
  FilterBar,
  NumberField,
  Page,
  SearchField,
  SelectField,
  SidePanel,
  StatCard,
  StatusBadge,
  TabPanel,
  Tabs,
  Toolbar,
  statusOptions,
  useNotify,
  type DataTableColumn,
} from '@/4-presentation/panel/kit';
import { exportCsv, formatDate, formatMoney, formatNumber, formatQuantity } from '@/4-presentation/panel/lib';
import {
  ORDER_CSV,
  ORDER_FILTERS,
  ORDER_STATUSES,
  SUPPLIER_CSV,
  busySuppliers,
  cannotOrderReason,
  categoriesBySku,
  categoryOptions,
  countText,
  deliveryText,
  filterOrder,
  orderText,
  productCardLink,
  purchaseOrdersLink,
  supplierGroups,
  supplierName,
  supplierOptions,
  toOrderEntries,
  type OrderEntry,
  type SupplierGroup,
} from './order';
import { SupplierOrderDialog } from './SupplierOrderDialog';

type OrderTab = 'productos' | 'proveedores';

const SUPPLIER_COLUMNS: DataTableColumn<SupplierGroup>[] = [
  {
    id: 'proveedor',
    header: 'Proveedor',
    card: 'title',
    value: (group) => supplierName(group.name),
    cell: (group) => (
      <span className="block min-w-44">
        <span className="block">{supplierName(group.name)}</span>
        <span className="block text-xs font-normal text-text-muted">{group.code ?? 'Sin código'}</span>
      </span>
    ),
  },
  { id: 'productos', header: 'Productos', align: 'end', value: (group) => group.toOrder.length },
  { id: 'total', header: 'Total', align: 'end', value: (group) => group.total, cell: (group) => formatMoney(group.total), footer: (rows) => formatMoney(rows.reduce((sum, group) => sum + group.total, 0)) },
  { id: 'entrega', header: 'Entrega', value: (group) => group.leadTimeDays, cell: (group) => <span className="block min-w-40 text-text-muted">{deliveryText(group)}</span> },
  {
    id: 'abiertas',
    header: 'Órdenes abiertas',
    align: 'end',
    value: (group) => group.openOrders,
    cell: (group) => (group.openOrders > 0 ? <StatusBadge tone="warning">{formatNumber(group.openOrders)}</StatusBadge> : '—'),
  },
  {
    id: 'contacto',
    header: 'Contacto',
    sortable: false,
    value: (group) => group.contact,
    cell: (group) => (
      <span className="block min-w-40">
        <span className="block">{group.contact || 'Sin contacto registrado'}</span>
        <span className="block text-xs text-text-muted">{[group.phone, group.email].filter(Boolean).join(' · ')}</span>
      </span>
    ),
  },
];

export function OrderPage() {
  const notify = useNotify();
  const navigate = useNavigate();
  const { can, canRun } = usePermissions();
  const table = useTableState({ filters: ORDER_FILTERS });
  const filters = table.filters;
  const [tab, setTab] = useState<OrderTab>('productos');

  const projection = useRpcQuery('GetStockProjectionQuery', { warehouseCode: null });
  const suppliers = useRpcQuery('GetSuppliersQuery', {});
  const [edits, setEdits] = useState<ReadonlyMap<string, number | null>>(() => new Map());

  const categories = useMemo(() => categoriesBySku(projection.data?.result.stock ?? []), [projection.data]);
  const entries = useMemo(() => toOrderEntries(projection.data?.result.order ?? [], categories, edits), [projection.data, categories, edits]);
  const rows = useMemo(() => filterOrder(entries, filters), [entries, filters]);
  const groups = useMemo(() => supplierGroups(rows, suppliers.data ?? []), [rows, suppliers.data]);
  const allGroups = useMemo(() => supplierGroups(entries, suppliers.data ?? []), [entries, suppliers.data]);
  const supplierChoices = useMemo(() => supplierOptions(entries), [entries]);
  const categoryChoices = useMemo(() => categoryOptions(entries), [entries]);

  const canPurchase = canRun('CreateSuggestedPurchaseOrdersCommand');
  const canCreateOrder = canRun('CreatePurchaseOrderCommand');
  const seesPurchaseOrders = can('purchasing.manage');
  const editedCount = entries.filter((entry) => entry.edited).length;
  const total = rows.reduce((sum, entry) => sum + entry.subtotal, 0);

  const setQuantity = (sku: string, value: number | null) =>
    setEdits((current) => {
      const next = new Map(current);
      next.set(sku, value);
      return next;
    });
  const resetQuantity = (sku: string) =>
    setEdits((current) => {
      const next = new Map(current);
      next.delete(sku);
      return next;
    });

  // ------------------------------------------------------------------------------------------------ comandos
  const [confirming, setConfirming] = useState(false);
  const [created, setCreated] = useState<{ numbers: string[]; supplierCode: string | null } | null>(null);
  const generate = useRpcCommand('CreateSuggestedPurchaseOrdersCommand', {
    success: (numbers) => (numbers.length === 1 ? '1 orden creada en borrador' : `${numbers.length} órdenes creadas en borrador`),
    notifyError: false,
  });
  const reloadAll = () => {
    projection.reload();
    suppliers.reload();
  };
  const runGenerate = async () => {
    const outcome = await generate.run({});
    if (outcome.ok) {
      setCreated({ numbers: [...outcome.result], supplierCode: null });
      setEdits(new Map());
      reloadAll();
    }
    return outcome;
  };
  const [creatingKey, setCreatingKey] = useState<string | null>(null);
  const creating = creatingKey ? (allGroups.find((group) => group.key === creatingKey) ?? null) : null;

  // ------------------------------------------------------------------------------------------------ detalle del proveedor
  const [detail, setDetail] = useState<{ key: string; open: boolean } | null>(null);
  const shownGroup = detail ? (allGroups.find((group) => group.key === detail.key) ?? null) : null;
  const pickingAction = useRef(false);
  const fromMenu = (action: () => void) => () => {
    pickingAction.current = true;
    action();
    setTimeout(() => {
      pickingAction.current = false;
    }, 0);
  };
  const openSupplier = (group: SupplierGroup) => setDetail({ key: group.key, open: true });
  const onlySupplier = (name: string) => {
    table.setFilter('proveedor', name);
    setTab('productos');
    setDetail((current) => (current ? { ...current, open: false } : current));
  };

  const copyOrder = async (group: SupplierGroup) => {
    try {
      await navigator.clipboard.writeText(orderText(group, projection.data?.today ?? null));
      notify.success('Pedido copiado', `Pedido para ${supplierName(group.name)}: péguelo en un correo o en WhatsApp.`);
    } catch {
      notify.warning('No se pudo copiar', 'El navegador no permitió usar el portapapeles. Exporte el CSV en su lugar.');
    }
  };

  const exportRows = () => {
    const file = tab === 'productos' ? exportCsv('pedido sugerido', ORDER_CSV, rows) : exportCsv('pedido por proveedor', SUPPLIER_CSV, groups);
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(tab === 'productos' ? rows.length : groups.length)} filas).`);
  };

  // ------------------------------------------------------------------------------------------------ columnas de productos
  const productColumns = useMemo<DataTableColumn<OrderEntry>[]>(
    () => [
      {
        id: 'producto',
        header: 'Producto',
        card: 'title',
        value: (entry) => entry.line.name,
        cell: (entry) => (
          <span className="block min-w-44">
            <span className="block">{entry.line.name}</span>
            <span className="block text-xs font-normal text-text-muted">
              {entry.line.sku} · {entry.category}
            </span>
          </span>
        ),
      },
      { id: 'proveedor', header: 'Proveedor', value: (entry) => supplierName(entry.line.supplier) },
      { id: 'semaforo', header: 'Semáforo', value: (entry) => entry.line.status, cell: (entry) => <StatusBadge status={entry.line.status} statuses={ORDER_STATUSES} /> },
      {
        id: 'existencias',
        header: 'Existencias',
        align: 'end',
        value: (entry) => entry.line.stock,
        cell: (entry) => (
          <span className="block">
            <span className="block">{formatQuantity(entry.line.stock, { unit: entry.line.unit })}</span>
            <span className="block text-xs text-text-muted">
              mín. {formatQuantity(entry.line.minimum)} · máx. {formatQuantity(entry.line.maximum)}
            </span>
          </span>
        ),
      },
      {
        id: 'apedir',
        header: 'A pedir',
        align: 'end',
        value: (entry) => entry.quantity,
        cell: (entry) => (
          <NumberField
            label={`Cantidad a pedir de ${entry.line.sku}`}
            hideLabel
            value={entry.quantity}
            onChange={(value) => setQuantity(entry.line.sku, value)}
            decimals={3}
            unit={entry.line.unit}
            className="ml-auto w-32"
            hint={entry.edited ? `Sugerido: ${formatQuantity(entry.line.quantityToOrder)}` : undefined}
          />
        ),
      },
      { id: 'costo', header: 'Costo unitario', align: 'end', value: (entry) => entry.line.unitCost, cell: (entry) => formatMoney(entry.line.unitCost) },
      {
        id: 'subtotal',
        header: 'Subtotal',
        align: 'end',
        value: (entry) => entry.subtotal,
        cell: (entry) => formatMoney(entry.subtotal),
        footer: (list) => formatMoney(list.reduce((sum, entry) => sum + entry.subtotal, 0)),
      },
    ],
    [],
  );

  const busy = busySuppliers(allGroups);
  const loaded = projection.data !== undefined;
  const noOrder = loaded && entries.length === 0;

  return (
    <Page
      title="Pedido sugerido"
      description={
        projection.data
          ? `Qué comprar y a quién en la sucursal activa (almacén ${projection.data.warehouseCode}, al ${formatDate(projection.data.today)}): productos bajo el mínimo, hasta su máximo.`
          : 'Qué comprar y a quién en la sucursal activa: productos bajo el mínimo, hasta su máximo.'
      }
      actions={
        <>
          {seesPurchaseOrders && (
            <Button variant="outline" leftIcon={<ClipboardList />} to={purchaseOrdersLink()}>
              Ver órdenes de compra
            </Button>
          )}
          {canPurchase && (
            <Button leftIcon={<FilePlus2 />} disabled={!loaded || entries.length === 0} onClick={() => setConfirming(true)}>
              Generar órdenes de compra
            </Button>
          )}
        </>
      }
    >
      {created && (
        <Alert
          tone="success"
          title={created.numbers.length === 1 ? 'Orden de compra creada en borrador' : 'Órdenes de compra creadas en borrador'}
          actions={
            seesPurchaseOrders && (
              <Button variant="outline" leftIcon={<ClipboardList />} to={purchaseOrdersLink(created.supplierCode)}>
                Ver órdenes de compra
              </Button>
            )
          }
        >
          <span data-testid="ordenes-creadas">{created.numbers.join(', ')}</span>. Apruébelas y recíbalas en «Órdenes de compra».
        </Alert>
      )}

      <Collapsible label="Ver resumen del pedido" openLabel="Ocultar resumen del pedido" icon={<ChartColumn />} description="Total, productos y lo que se pide a cada proveedor.">
        {loaded ? (
          <div className="space-y-5" data-testid="resumen-pedido">
            <div className="grid grid-cols-[repeat(auto-fit,minmax(11rem,1fr))] gap-3">
              <StatCard label="Total a pedir" value={formatMoney(entries.reduce((sum, entry) => sum + entry.subtotal, 0))} icon={<ShoppingCart />} />
              <StatCard label="Productos" value={formatNumber(entries.filter((entry) => entry.quantity > 0).length)} icon={<PackageSearch />} />
              <StatCard label="Proveedores" value={formatNumber(allGroups.length)} icon={<ClipboardList />} />
            </div>
            <BarList label="Total por proveedor" items={allGroups.map((group) => ({ label: supplierName(group.name), value: group.total })).sort((a, b) => b.value - a.value)} format={(value) => formatMoney(value)} />
          </div>
        ) : (
          <p className="text-sm text-text-muted">Cargando el resumen…</p>
        )}
      </Collapsible>

      <FilterBar activeCount={table.activeFilterCount} onClear={table.clearFilters}>
        <SearchField label="Buscar" placeholder="SKU, producto, proveedor o categoría" value={filters.q} onChange={(q) => table.setFilter('q', q)} />
        <SelectField label="Proveedor" allLabel="Todos los proveedores" value={filters.proveedor} onChange={(value) => table.setFilter('proveedor', value)} options={supplierChoices} />
        <SelectField label="Categoría" allLabel="Todas las categorías" value={filters.categoria} onChange={(value) => table.setFilter('categoria', value)} options={categoryChoices} />
        <SelectField label="Semáforo" value={filters.estado} onChange={(value) => table.setFilter('estado', value)} options={statusOptions(ORDER_STATUSES)} />
      </FilterBar>

      <Toolbar
        end={
          <>
            {editedCount > 0 && (
              <Button variant="outline" leftIcon={<Undo2 />} onClick={() => setEdits(new Map())}>
                Volver a las cantidades sugeridas
              </Button>
            )}
            <Button variant="outline" leftIcon={<RefreshCw />} loading={projection.fetching && !projection.loading} onClick={reloadAll}>
              Actualizar
            </Button>
            <Button variant="outline" leftIcon={<Download />} disabled={(tab === 'productos' ? rows.length : groups.length) === 0} onClick={exportRows}>
              Exportar CSV
            </Button>
          </>
        }
      >
        {loaded && (
          <span className="text-sm text-text-muted" data-testid="pedido-resumen">
            {countText(rows.length, groups.length)} · total {formatMoney(total)}
            {editedCount > 0 && ` · ${formatNumber(editedCount)} ${editedCount === 1 ? 'cantidad revisada' : 'cantidades revisadas'}`}
          </span>
        )}
      </Toolbar>

      <Tabs
        label="Vistas del pedido"
        value={tab}
        onChange={setTab}
        tabs={[
          { id: 'productos', label: 'Productos', count: loaded ? rows.length : undefined },
          { id: 'proveedores', label: 'Proveedores', count: loaded ? groups.length : undefined },
        ]}
      >
        <TabPanel id="productos">
          <DataTable
            caption="Productos del pedido sugerido"
            columns={productColumns}
            rows={loaded ? rows : undefined}
            rowKey={(entry) => entry.key}
            rowLabel={(entry) => `el producto ${entry.line.name}`}
            loading={projection.loading}
            refreshing={projection.fetching && !projection.loading}
            error={projection.error}
            onRetry={projection.reload}
            operation="GetStockProjectionQuery"
            {...table.tableProps}
            rowActions={(entry) => [
              { label: 'Ver ficha y kardex', icon: <Eye />, onSelect: () => navigate(productCardLink(entry.line.sku)) },
              { label: 'Volver a la cantidad sugerida', icon: <RotateCcw />, onSelect: () => resetQuantity(entry.line.sku), hidden: !entry.edited },
              { label: 'No pedir este producto', icon: <CircleX />, onSelect: () => setQuantity(entry.line.sku, 0), hidden: entry.quantity === 0 },
              { label: 'Ver solo este proveedor', icon: <ListFilter />, onSelect: () => onlySupplier(entry.line.supplier), hidden: filters.proveedor === entry.line.supplier },
            ]}
            empty={{
              title: noOrder ? 'No hay nada que reponer' : 'No hay productos con estos filtros',
              description: noOrder ? 'Todos los productos están por encima de su mínimo.' : 'Pruebe con otra búsqueda o limpie los filtros.',
              icon: <ShoppingCart />,
              action: noOrder ? undefined : (
                <Button variant="outline" onClick={table.clearFilters}>
                  Limpiar filtros
                </Button>
              ),
            }}
          />
        </TabPanel>
        <TabPanel id="proveedores">
          <DataTable
            caption="Pedido por proveedor"
            columns={SUPPLIER_COLUMNS}
            rows={loaded ? groups : undefined}
            rowKey={(group) => group.key}
            rowLabel={(group) => `el pedido de ${supplierName(group.name)}`}
            loading={projection.loading}
            error={projection.error}
            onRetry={projection.reload}
            operation="GetStockProjectionQuery"
            onRowOpen={(group) => {
              if (!pickingAction.current) openSupplier(group);
            }}
            activeRowKey={detail?.open ? detail.key : null}
            rowActions={(group) => [
              { label: 'Ver detalle', icon: <Eye />, onSelect: fromMenu(() => openSupplier(group)) },
              {
                label: 'Crear orden de compra',
                icon: <FilePlus2 />,
                onSelect: fromMenu(() => setCreatingKey(group.key)),
                hidden: !canCreateOrder,
                disabled: cannotOrderReason(group) !== null,
                disabledReason: cannotOrderReason(group) ?? undefined,
              },
              { label: 'Copiar pedido', icon: <ClipboardCopy />, onSelect: fromMenu(() => void copyOrder(group)) },
              { label: 'Ver sus productos', icon: <ListFilter />, onSelect: fromMenu(() => onlySupplier(group.name)) },
            ]}
            empty={{ title: noOrder ? 'No hay nada que reponer' : 'No hay proveedores con estos filtros', icon: <ShoppingCart /> }}
          />
        </TabPanel>
      </Tabs>

      <SidePanel
        open={(detail?.open ?? false) && shownGroup !== null}
        onClose={() => setDetail((current) => (current?.open ? { ...current, open: false } : current))}
        title={shownGroup ? supplierName(shownGroup.name) : 'Proveedor'}
        description={shownGroup ? `${formatNumber(shownGroup.toOrder.length)} ${shownGroup.toOrder.length === 1 ? 'producto' : 'productos'} a pedir · total ${formatMoney(shownGroup.total)}` : undefined}
        footer={
          shownGroup && (
            <div className="flex flex-col gap-2">
              {canCreateOrder && (
                <Button leftIcon={<FilePlus2 />} fullWidth disabled={cannotOrderReason(shownGroup) !== null} onClick={() => setCreatingKey(shownGroup.key)}>
                  Crear orden de compra
                </Button>
              )}
              <Button variant="outline" leftIcon={<ClipboardCopy />} fullWidth onClick={() => void copyOrder(shownGroup)}>
                Copiar pedido
              </Button>
              <Button variant="outline" leftIcon={<ListFilter />} fullWidth onClick={() => onlySupplier(shownGroup.name)}>
                Ver solo sus productos
              </Button>
              {seesPurchaseOrders && shownGroup.code && (
                <Button variant="ghost" leftIcon={<ClipboardList />} fullWidth to={purchaseOrdersLink(shownGroup.code)}>
                  Ver sus órdenes de compra
                </Button>
              )}
            </div>
          )
        }
      >
        {shownGroup && (
          <div className="space-y-4" data-testid="detalle-proveedor">
            {cannotOrderReason(shownGroup) && canCreateOrder && <Alert tone="info">{cannotOrderReason(shownGroup)}</Alert>}
            <DetailList
              items={[
                { label: 'Código', value: shownGroup.code },
                { label: 'Órdenes abiertas', value: formatNumber(shownGroup.openOrders) },
                { label: 'Contacto', value: shownGroup.contact || 'Sin contacto registrado' },
                { label: 'Teléfono', value: shownGroup.phone },
                { label: 'Correo', value: shownGroup.email, wide: true },
                { label: 'Entrega', value: deliveryText(shownGroup), wide: true },
              ]}
            />
            <ul className="divide-y divide-border rounded-xl border border-border text-sm">
              {shownGroup.entries.map((entry) => (
                <li key={entry.key} className="flex flex-wrap justify-between gap-x-4 gap-y-1 px-3 py-2">
                  <span className="min-w-0">
                    <span className="block text-text">{entry.line.name}</span>
                    <span className="block text-xs text-text-muted">
                      {entry.line.sku} · {formatQuantity(entry.quantity, { unit: entry.line.unit })}
                      {entry.edited && ` (sugerido ${formatQuantity(entry.line.quantityToOrder)})`}
                    </span>
                  </span>
                  <span className="shrink-0 font-semibold tabular-nums">{formatMoney(entry.subtotal)}</span>
                </li>
              ))}
            </ul>
          </div>
        )}
      </SidePanel>

      <ConfirmDialog
        open={confirming}
        onClose={() => {
          setConfirming(false);
          generate.reset();
        }}
        title="¿Generar las órdenes de compra?"
        message="Se crea una orden en borrador por cada proveedor con productos bajo el mínimo (hasta el máximo), con las cantidades que calcula el sistema. Los proveedores que ya tienen una orden abierta se omiten."
        confirmLabel="Generar órdenes"
        onConfirm={runGenerate}
        error={generate.errorText}
      >
        {editedCount > 0 && (
          <Alert tone="warning" title="Usa las cantidades sugeridas">
            Revisó {formatNumber(editedCount)} {editedCount === 1 ? 'cantidad' : 'cantidades'}: esta opción no las usa. Para pedir con sus cantidades, cree la orden de
            cada proveedor desde la pestaña «Proveedores».
          </Alert>
        )}
        {busy.length > 0 && (
          <Alert tone="info" title="Se omitirán">
            {busy.map((group) => supplierName(group.name)).join(', ')} ya {busy.length === 1 ? 'tiene' : 'tienen'} una orden abierta.
          </Alert>
        )}
      </ConfirmDialog>

      <SupplierOrderDialog
        group={creating}
        onClose={() => setCreatingKey(null)}
        onCreated={(number, supplierCode) => {
          setCreated({ numbers: [number], supplierCode });
          setDetail((current) => (current ? { ...current, open: false } : current));
          reloadAll();
        }}
      />
    </Page>
  );
}
