// Ventas › Clientes: la cartera de clientes del escritorio (CustomersView) en la web. Lista de `GetCustomersQuery` con sus
// datos de factura (`GetCustomerFiscalIdentitiesQuery`, si la empresa factura con el SIN), filtros en la dirección
// (búsqueda por nombre, código, NIT/CI, correo o teléfono; categoría; activo; con o sin datos de factura; origen web o
// tienda), tabla ordenable, detalle lateral con «Ver sus ventas» (`/panel/ventas?cliente=<código>`), alta y edición en un
// diálogo, y activar/desactivar (con confirmación). Los clientes que se registraron en la tienda web (`WEB-…`) llevan la
// marca «Cliente web». El resumen de la cartera va PLEGADO en «Ver resumen de la cartera» (regla P-10).
//
// Dirección: los filtros (`?q=`, `?categoria=`, `?estado=`, `?factura=`, `?origen=`) y `?nuevo=1` (abre «Nuevo cliente»;
// lo usa el tablero).

import { BarChart3, Download, Eye, Pencil, Receipt, RefreshCw, Trophy, UserCheck, UserPlus, UserX, Users } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { ROUTES } from '@/4-presentation/app/routes';
import { usePermissions, useRpcCommand, useRpcQuery, useTableState } from '@/4-presentation/panel/hooks';
import {
  Alert,
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
import { exportCsv, formatDate, formatMoney, formatNumber, toDate } from '@/4-presentation/panel/lib';
import { CustomerDialog, type CustomerTarget } from './CustomerDialog';
import {
  CUSTOMERS_CSV,
  CUSTOMER_FILTERS,
  CUSTOMER_STATES,
  DOCUMENT_TYPES,
  FINAL_CONSUMER,
  INVOICE_DATA_OPTIONS,
  ORIGIN_OPTIONS,
  cartSummary,
  categoryOptions,
  filterCustomers,
  plainMessage,
  toCustomerItems,
  toggleActivePayload,
  type CustomerItem,
} from './customers';

function customerColumns(fiscalEnabled: boolean): DataTableColumn<CustomerItem>[] {
  return [
    {
      id: 'nombre',
      header: 'Cliente',
      value: (item) => item.row.name,
      card: 'title',
      cell: (item) => (
        <span className="block min-w-44">
          <span className="block">{item.row.name}</span>
          <span className="flex flex-wrap items-center gap-1.5 text-xs font-normal text-text-muted">
            {item.row.code}
            {item.webCustomer && <StatusBadge tone="info">Cliente web</StatusBadge>}
          </span>
        </span>
      ),
    },
    {
      id: 'documento',
      header: fiscalEnabled ? 'Documento' : 'NIT / CI',
      value: (item) => item.document,
      cell: (item) =>
        item.document ? (
          <span className="block whitespace-nowrap">
            {item.document}
            {fiscalEnabled && !item.hasInvoiceData && <span className="block text-xs text-text-muted">Sin tipo de documento</span>}
          </span>
        ) : (
          <span className="text-text-muted">Sin documento</span>
        ),
    },
    { id: 'contacto', header: 'Contacto', value: (item) => item.contact, sortable: false, cell: (item) => <span className="block max-w-64 min-w-40 break-words">{item.contact || '—'}</span> },
    { id: 'categoria', header: 'Categoría', value: (item) => item.row.category },
    {
      id: 'compras',
      header: 'Compras',
      align: 'end',
      value: (item) => item.row.purchases,
      footer: (rows) => formatNumber(rows.reduce((sum, item) => sum + item.row.purchases, 0)),
    },
    {
      id: 'total',
      header: 'Total comprado',
      align: 'end',
      value: (item) => item.row.total,
      cell: (item) => (item.row.total > 0 ? formatMoney(item.row.total) : '—'),
      footer: (rows) => formatMoney(rows.reduce((sum, item) => sum + item.row.total, 0)),
    },
    {
      id: 'ultima',
      header: 'Última compra',
      value: (item) => toDate(item.row.lastPurchase),
      cell: (item) => (item.row.lastPurchase ? formatDate(item.row.lastPurchase) : 'Nunca'),
      className: 'whitespace-nowrap',
    },
    { id: 'estado', header: 'Estado', value: (item) => CUSTOMER_STATES[item.state].label, cell: (item) => <StatusBadge status={item.state} statuses={CUSTOMER_STATES} /> },
  ];
}

interface Shown<T> {
  item: T;
  open: boolean;
}

export function CustomersPage() {
  const notify = useNotify();
  const navigate = useNavigate();
  const { can, canRun } = usePermissions();
  const table = useTableState({ filters: CUSTOMER_FILTERS, sort: { column: 'nombre', direction: 'asc' } });
  const filters = table.filters;
  const [params, setParams] = useSearchParams();

  const customers = useRpcQuery('GetCustomersQuery', {});
  const billing = useRpcQuery('GetBillingAccessQuery', {});
  const fiscalEnabled = billing.data?.moduleActive !== false;
  const identities = useRpcQuery('GetCustomerFiscalIdentitiesQuery', {}, { enabled: fiscalEnabled });
  const categories = useMemo(() => customers.data?.categories ?? [], [customers.data]);
  const items = useMemo(
    () => toCustomerItems(customers.data?.customers ?? [], fiscalEnabled ? (identities.data ?? []) : []),
    [customers.data, identities.data, fiscalEnabled],
  );
  const rows = useMemo(() => filterCustomers(items, fiscalEnabled ? filters : { ...filters, factura: '' }), [items, filters, fiscalEnabled]);
  const columns = useMemo(() => customerColumns(fiscalEnabled), [fiscalEnabled]);
  const summary = useMemo(() => cartSummary(items), [items]);
  const categoryChoices = useMemo(() => categoryOptions(categories, items), [categories, items]);

  const canEdit = canRun('SaveCustomerCommand');
  const canSeeSales = can('sales.view');

  const [detail, setDetail] = useState<Shown<CustomerItem> | null>(null);
  const [dialogSession, setDialogSession] = useState(0);
  const [editing, setEditing] = useState<CustomerTarget | null>(null);
  const [deactivating, setDeactivating] = useState<Shown<CustomerItem> | null>(null);
  const toggle = useRpcCommand('SaveCustomerCommand', { notifyError: false });

  const detailItem = detail ? (items.find((item) => item.key === detail.item.key) ?? detail.item) : null;
  const reload = () => {
    customers.reload();
    identities.reload();
  };

  const openDetail = (item: CustomerItem) => setDetail({ item, open: true });
  const closeDetail = () => setDetail((current) => (current?.open ? { ...current, open: false } : current));
  const askEdit = (item: CustomerItem | null) => {
    setDialogSession((count) => count + 1);
    setEditing({ item });
  };
  const askDeactivate = (item: CustomerItem) => {
    toggle.reset();
    setDeactivating({ item, open: true });
  };
  const salesOf = (item: CustomerItem) => ROUTES.panelModule(`ventas?cliente=${encodeURIComponent(item.row.code)}`);

  // `?nuevo=1` (tablero «Nuevo cliente»): abre el diálogo (una vez por pedido) y se quita de la dirección.
  const newParam = params.get('nuevo');
  const [handledNew, setHandledNew] = useState<string | null>(null);
  if (newParam !== handledNew) {
    setHandledNew(newParam);
    if (newParam !== null && canEdit) {
      setDialogSession((count) => count + 1);
      setEditing({ item: null });
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

  const activate = async (item: CustomerItem) => {
    const outcome = await toggle.run(toggleActivePayload(item, true));
    if (outcome.ok) {
      notify.success('Cliente activado', `${item.row.code} · ${item.row.name}`);
      reload();
    } else {
      notify.error('No se pudo activar el cliente', outcome.error);
    }
  };

  const confirmDeactivate = async () => {
    const target = deactivating?.item;
    if (!target) return false;
    const outcome = await toggle.run(toggleActivePayload(target, false));
    if (outcome.ok) {
      notify.success('Cliente desactivado', `${target.row.code} · ${target.row.name}`);
      reload();
    }
    return outcome;
  };

  const exportRows = () => {
    const file = exportCsv('clientes', CUSTOMERS_CSV, rows);
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(rows.length)} filas).`);
  };

  const rowActions = (item: CustomerItem) => [
    { label: 'Ver detalle', icon: <Eye />, onSelect: () => openDetail(item) },
    { label: 'Ver sus ventas', icon: <Receipt />, onSelect: () => navigate(salesOf(item)), hidden: !canSeeSales },
    { label: 'Editar', icon: <Pencil />, onSelect: () => askEdit(item), hidden: !canEdit },
    { label: 'Activar', icon: <UserCheck />, onSelect: () => void activate(item), hidden: !canEdit || item.row.isActive },
    {
      label: 'Desactivar',
      icon: <UserX />,
      tone: 'danger' as const,
      onSelect: () => askDeactivate(item),
      hidden: !canEdit || !item.row.isActive,
      disabled: item.row.code === FINAL_CONSUMER,
      disabledReason: item.row.code === FINAL_CONSUMER ? 'El consumidor final no se puede desactivar.' : undefined,
    },
  ];

  const current = detailItem;
  const documentType = current?.documentType ? DOCUMENT_TYPES.find((type) => type.code === current.documentType)?.label : null;

  return (
    <Page
      title="Clientes"
      description="Cartera de clientes: datos de contacto, datos para la factura del SIN, compras y sus ventas."
      actions={
        canEdit && (
          <Button leftIcon={<UserPlus />} onClick={() => askEdit(null)}>
            Nuevo cliente
          </Button>
        )
      }
    >
      <FilterBar activeCount={table.activeFilterCount} onClear={table.clearFilters}>
        <SearchField label="Buscar" placeholder="Nombre, código, NIT/CI, correo o teléfono" value={filters.q} onChange={(q) => table.setFilter('q', q)} />
        <SelectField label="Categoría" allLabel="Todas las categorías" value={filters.categoria} onChange={(value) => table.setFilter('categoria', value)} options={categoryChoices} />
        <SelectField label="Estado" value={filters.estado} onChange={(value) => table.setFilter('estado', value)} options={statusOptions(CUSTOMER_STATES)} />
        {fiscalEnabled && (
          <SelectField label="Datos de factura" value={filters.factura} onChange={(value) => table.setFilter('factura', value)} options={INVOICE_DATA_OPTIONS} />
        )}
        <SelectField label="Origen" value={filters.origen} onChange={(value) => table.setFilter('origen', value)} options={ORIGIN_OPTIONS} />
      </FilterBar>

      <Toolbar
        end={
          <>
            <Button variant="outline" leftIcon={<RefreshCw />} loading={customers.fetching && !customers.loading} onClick={reload}>
              Actualizar
            </Button>
            <Button variant="outline" leftIcon={<Download />} disabled={rows.length === 0} onClick={exportRows}>
              Exportar CSV
            </Button>
          </>
        }
      >
        {customers.data && (
          <span className="text-sm text-text-muted" data-testid="clientes-resumen">
            {formatNumber(rows.length)} de {formatNumber(items.length)} clientes · {formatNumber(summary.active)} activos
          </span>
        )}
      </Toolbar>

      {fiscalEnabled && identities.error && (
        <Alert
          tone="warning"
          title="No se pudieron leer los datos de factura"
          actions={
            <Button variant="outline" leftIcon={<RefreshCw />} onClick={identities.reload}>
              Reintentar
            </Button>
          }
        >
          La lista se ve igual, pero sin el tipo de documento de cada cliente.
        </Alert>
      )}

      <Collapsible label="Ver resumen de la cartera" openLabel="Ocultar resumen de la cartera" icon={<BarChart3 />} description="Clientes activos, con compras, lo vendido y el mejor cliente.">
        <div className="grid grid-cols-[repeat(auto-fit,minmax(11rem,1fr))] gap-3" data-testid="resumen-cartera">
          <StatCard label="Clientes activos" value={formatNumber(summary.active)} hint={`${formatNumber(summary.total)} registrados · ${formatNumber(summary.web)} de la tienda web`} icon={<Users />} />
          <StatCard label="Con compras" value={formatNumber(summary.buyers)} hint={`${formatNumber(summary.purchases)} compras en total`} icon={<Receipt />} />
          <StatCard label="Vendido a clientes" value={formatMoney(summary.soldToCustomers)} hint={`Consumidor final: ${formatMoney(summary.finalConsumer)}`} icon={<BarChart3 />} tone="success" />
          <StatCard
            label="Mejor cliente"
            value={summary.top?.name ?? '—'}
            hint={summary.top ? `${formatMoney(summary.top.total)} en ${formatNumber(summary.top.purchases)} compras` : undefined}
            icon={<Trophy />}
          />
        </div>
      </Collapsible>

      <DataTable
        caption="Clientes"
        columns={columns}
        rows={customers.data ? rows : undefined}
        rowKey={(item) => item.key}
        rowLabel={(item) => `el cliente ${item.row.name} (${item.row.code})`}
        loading={customers.loading}
        refreshing={customers.fetching && !customers.loading}
        error={customers.error}
        onRetry={customers.reload}
        operation="GetCustomersQuery"
        {...table.tableProps}
        onRowOpen={openDetail}
        activeRowKey={detail?.open ? detail.item.key : null}
        rowActions={rowActions}
        empty={{
          title: items.length === 0 ? 'Todavía no hay clientes' : 'Ningún cliente coincide con los filtros',
          description: items.length === 0 ? 'Registre el primero con «Nuevo cliente».' : 'Pruebe con otra búsqueda o limpie los filtros.',
          icon: <Users />,
          action:
            items.length === 0 ? (
              canEdit && (
                <Button leftIcon={<UserPlus />} onClick={() => askEdit(null)}>
                  Nuevo cliente
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
        title={current ? current.row.name : 'Cliente'}
        description={current ? `Cliente ${current.row.code}` : undefined}
        headerExtra={current && <StatusBadge status={current.state} statuses={CUSTOMER_STATES} />}
        footer={
          current && (
            <div className="flex flex-col gap-2">
              {canSeeSales && (
                <Button leftIcon={<Receipt />} fullWidth to={salesOf(current)}>
                  Ver sus ventas
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
              {canEdit && current.row.isActive && current.row.code !== FINAL_CONSUMER && (
                <Button variant="danger" leftIcon={<UserX />} fullWidth onClick={() => askDeactivate(current)}>
                  Desactivar
                </Button>
              )}
            </div>
          )
        }
      >
        {current && (
          <div className="space-y-5" data-testid="detalle-cliente">
            {current.webCustomer && (
              <Alert tone="info" title="Cliente web">
                Se registró en la tienda en línea (código {current.row.code}).
              </Alert>
            )}
            <DetailList
              items={[
                { label: 'Código', value: current.row.code },
                { label: 'Categoría', value: current.row.category },
                { label: 'NIT / CI', value: current.row.taxId },
                ...(fiscalEnabled
                  ? [
                      { label: 'Tipo de documento', value: documentType ?? 'Sin tipo de documento' },
                      { label: 'Complemento', value: current.complement },
                      { label: 'Datos de factura', value: current.hasInvoiceData ? 'Completos: se le factura a su nombre' : 'Incompletos: falta el tipo o el número de documento' },
                    ]
                  : []),
                { label: 'Correo', value: current.row.email },
                { label: 'Teléfono', value: current.row.phone },
                { label: 'Compras', value: formatNumber(current.row.purchases) },
                { label: 'Total comprado', value: formatMoney(current.row.total) },
                { label: 'Última compra', value: current.row.lastPurchase ? formatDate(current.row.lastPurchase) : 'Nunca' },
              ]}
            />
          </div>
        )}
      </SidePanel>

      <CustomerDialog
        key={`cliente-${dialogSession}`}
        target={editing}
        categories={categories}
        fiscalEnabled={fiscalEnabled}
        onClose={() => setEditing(null)}
        onSaved={reload}
      />
      <ConfirmDialog
        open={deactivating?.open ?? false}
        onClose={() => setDeactivating((shown) => (shown?.open ? { ...shown, open: false } : shown))}
        tone="danger"
        title={`¿Desactivar a ${deactivating?.item.row.name ?? 'este cliente'}?`}
        message="Deja de aparecer para vender. Sus ventas y sus datos se conservan, y se puede volver a activar cuando haga falta."
        confirmLabel="Desactivar"
        onConfirm={confirmDeactivate}
        error={toggle.errorText ? plainMessage(toggle.errorText) : undefined}
      />
    </Page>
  );
}
