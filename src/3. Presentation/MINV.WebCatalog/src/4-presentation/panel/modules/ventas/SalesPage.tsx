// Ventas › Ventas: el historial de ventas del escritorio (SalesView) en la web, con dos pestañas.
//   «Ventas»: las ventas del período (`GetSalesQuery`, HOY por defecto) con su factura del SIN (`GetSalesFiscalStatusQuery`,
//   si la empresa factura) y sus devoluciones (`GetSalesReturnsQuery`). Filtros en la dirección: fechas, búsqueda
//   (número, pedido, cliente o N° de factura), sucursal, cliente, cajero, medio de pago, estado y factura del SIN. Tabla
//   con totales al pie, detalle lateral y acciones: reimprimir, enviar la factura por correo, devolver productos y anular.
//   «Devoluciones»: las devoluciones del período con su nota crédito-débito.
// El resumen del período (lo que el escritorio mostraba en tarjetas) va PLEGADO en «Ver resumen del período» (P-10).
//
// Dirección: `?vista=devoluciones` (pestaña), `?cliente=<código>` (lo usa «Ver sus ventas» de Clientes: sin fechas en la
// dirección, muestra TODAS las ventas de ese cliente), `?devolver=1` (abre «Registrar una devolución»; lo usa el tablero) o
// `?devolver=<número>` (la devolución de esa venta).

import { BarChart3, Ban, Download, Eye, Filter, Mail, Printer, Receipt, RefreshCw, ShoppingCart, Undo2 } from 'lucide-react';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { ROUTES } from '@/4-presentation/app/routes';
import { usePermissions, useRpcQuery, useTableState } from '@/4-presentation/panel/hooks';
import {
  Alert,
  Button,
  Collapsible,
  ComboBox,
  DataTable,
  DateRangeField,
  DetailList,
  FilterBar,
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
  type RowActionItem,
} from '@/4-presentation/panel/kit';
import { EMPTY_RANGE, exportCsv, formatDateTime, formatMoney, formatNumber, laPazToday } from '@/4-presentation/panel/lib';
import { ReprintDialog } from './ReprintDialog';
import { ReturnDialog, type ReturnTarget } from './ReturnDialog';
import { SaleDetailPanel } from './SaleDetailPanel';
import {
  FISCAL_STATES,
  NOTE_STATES,
  RETURNS_CSV,
  SALES_CSV,
  SALE_STATES,
  VIEWS,
  activeFilters,
  branchOptions,
  clearedFilters,
  customerOptions,
  distinctOptions,
  filterReturns,
  filterSales,
  fiscalText,
  normalizeSaleNumber,
  returnsRange,
  saleActions,
  salesFilters,
  salesSummary,
  serverRange,
  toReturnItems,
  toSaleItems,
  viewOf,
  type ReturnItem,
  type SaleAbilities,
  type SaleItem,
  type SalesView,
} from './sales';
import { SendInvoiceDialog, type InvoiceTarget } from './SendInvoiceDialog';
import { VoidSaleDialog } from './VoidSaleDialog';

// ---------------------------------------------------------------------------------------------------- columnas

function saleColumns(fiscalEnabled: boolean): DataTableColumn<SaleItem>[] {
  const columns: DataTableColumn<SaleItem>[] = [
    {
      id: 'numero',
      header: 'Venta',
      value: (item) => item.row.invoiceNumber,
      card: 'title',
      className: 'whitespace-nowrap',
      cell: (item) => (
        <span className="block">
          <span className="block">{item.row.invoiceNumber}</span>
          <span className="block text-xs font-normal text-text-muted">Pedido {item.row.orderNumber}</span>
        </span>
      ),
    },
    {
      id: 'fecha',
      header: 'Fecha y hora',
      value: (item) => new Date(item.row.issuedAt),
      cell: (item) => formatDateTime(item.row.issuedAt),
      className: 'whitespace-nowrap',
    },
    {
      id: 'cliente',
      header: 'Cliente',
      value: (item) => item.row.customer,
      cell: (item) => (
        <span className="block min-w-40">
          <span className="block">{item.row.customer}</span>
          <span className="flex flex-wrap items-center gap-1.5 text-xs text-text-muted">
            {item.row.customerCode}
            {item.webCustomer && <StatusBadge tone="info">Cliente web</StatusBadge>}
          </span>
        </span>
      ),
    },
    {
      id: 'cajero',
      header: 'Cajero y pago',
      value: (item) => item.row.cashier,
      cell: (item) => (
        <span className="block min-w-36">
          <span className="block">{item.row.cashier}</span>
          <span className="block text-xs text-text-muted">
            {item.row.paymentMethod} · {item.row.items === 1 ? '1 línea' : `${formatNumber(item.row.items)} líneas`}
          </span>
        </span>
      ),
    },
    {
      id: 'total',
      header: 'Total',
      align: 'end',
      value: (item) => item.row.total,
      cell: (item) => <span className={item.state === 'anulada' ? 'text-text-muted line-through' : undefined}>{formatMoney(item.row.total)}</span>,
      footer: (rows) => formatMoney(salesSummary(rows).total),
    },
    {
      id: 'estado',
      header: 'Estado',
      value: (item) => SALE_STATES[item.state].label,
      cell: (item) => <StatusBadge status={item.state} statuses={SALE_STATES} />,
    },
  ];
  if (fiscalEnabled) {
    columns.push({
      id: 'factura',
      header: 'Factura del SIN',
      value: (item) => fiscalText(item.fiscal),
      cell: (item) =>
        item.fiscal ? (
          <span className="flex flex-wrap items-center gap-1.5">
            <span className="whitespace-nowrap">N° {item.fiscal.number}</span>
            <StatusBadge status={item.fiscalState} statuses={FISCAL_STATES} />
          </span>
        ) : (
          <span className="text-text-muted">Sin factura</span>
        ),
    });
  }
  return columns;
}

const RETURN_COLUMNS: DataTableColumn<ReturnItem>[] = [
  { id: 'numero', header: 'Devolución', value: (item) => item.row.number, card: 'title', className: 'whitespace-nowrap' },
  { id: 'fecha', header: 'Fecha y hora', value: (item) => new Date(item.row.returnedAt), cell: (item) => formatDateTime(item.row.returnedAt), className: 'whitespace-nowrap' },
  { id: 'venta', header: 'Venta', value: (item) => item.row.invoiceNumber, className: 'whitespace-nowrap' },
  {
    id: 'cliente',
    header: 'Cliente y motivo',
    value: (item) => item.row.customer,
    cell: (item) => (
      <span className="block min-w-44">
        <span className="block">{item.row.customer}</span>
        <span className="line-clamp-2 block text-xs text-text-muted">{item.row.reason}</span>
      </span>
    ),
  },
  {
    id: 'reembolso',
    header: 'Reembolso',
    align: 'end',
    value: (item) => item.row.refund,
    cell: (item) => formatMoney(item.row.refund),
    footer: (rows) => formatMoney(rows.reduce((sum, item) => sum + item.row.refund, 0)),
  },
  {
    id: 'nota',
    header: 'Nota crédito-débito',
    value: (item) => item.row.creditNote ?? 'Sin nota',
    cell: (item) =>
      item.row.creditNote ? (
        <span className="flex flex-wrap items-center gap-1.5">
          <span className="whitespace-nowrap">{item.row.creditNote}</span>
          <StatusBadge status={item.noteState} statuses={NOTE_STATES} />
        </span>
      ) : (
        <span className="text-text-muted">Sin nota (venta sin factura)</span>
      ),
  },
];

/** Una cosa abierta en un panel o diálogo: se guarda el registro y si está abierto (al cerrar se desliza con su contenido). */
interface Shown<T> {
  item: T;
  open: boolean;
}

export function SalesPage() {
  const notify = useNotify();
  const { can, canRun, session } = usePermissions();
  // «Hoy» se fija al abrir la pantalla (el filtro por defecto es el día de hoy en La Paz).
  const [today] = useState(() => laPazToday());
  const defaults = useMemo(() => salesFilters(today), [today]);
  const table = useTableState({ filters: defaults, sort: { column: 'fecha', direction: 'desc' } });
  const returnsTable = useTableState({ prefix: 'd_', sort: { column: 'fecha', direction: 'desc' } });
  const filters = table.filters;
  const [params, setParams] = useSearchParams();
  const view = viewOf(params.get('vista'));

  // «Ver sus ventas» (desde Clientes) llega con `?cliente=` y sin fechas: se muestran TODAS las ventas del cliente.
  const [pendingAllDates, setPendingAllDates] = useState(() => params.has('cliente') && !params.has('desde') && !params.has('hasta'));
  const setDateRange = table.setDateRange;
  useEffect(() => {
    if (!pendingAllDates) return;
    setDateRange(EMPTY_RANGE);
    setPendingAllDates(false);
  }, [pendingAllDates, setDateRange]);
  const range = pendingAllDates ? EMPTY_RANGE : table.dateRange();
  const salesRange = serverRange(range, today);

  // Lo que filtra el SERVIDOR (las fechas) va en los pedidos; el resto se filtra en la página.
  const billing = useRpcQuery('GetBillingAccessQuery', {});
  const fiscalEnabled = billing.data?.moduleActive !== false;
  const request = salesRange ?? { from: today, to: today };
  const sales = useRpcQuery('GetSalesQuery', request, { enabled: salesRange !== null });
  const fiscal = useRpcQuery('GetSalesFiscalStatusQuery', request, { enabled: salesRange !== null && fiscalEnabled });
  const returns = useRpcQuery('GetSalesReturnsQuery', returnsRange(request, today), { enabled: salesRange !== null });

  const items = useMemo(
    () => toSaleItems(sales.data ?? [], fiscalEnabled ? (fiscal.data ?? []) : [], returns.data ?? []),
    [sales.data, fiscal.data, returns.data, fiscalEnabled],
  );
  const rows = useMemo(() => filterSales(items, filters), [items, filters]);
  const returnItems = useMemo(() => toReturnItems(returns.data ?? []), [returns.data]);
  const customerFilter = useMemo(() => {
    if (!filters.cliente) return null;
    const own = items.filter((item) => item.row.customerCode === filters.cliente);
    return { invoices: new Set(own.map((item) => item.key.toUpperCase())), name: own[0]?.row.customer ?? null };
  }, [items, filters.cliente]);
  const returnRows = useMemo(() => filterReturns(returnItems, filters, range, customerFilter), [returnItems, filters, range, customerFilter]);

  const columns = useMemo(() => saleColumns(fiscalEnabled), [fiscalEnabled]);
  const summary = useMemo(() => salesSummary(rows), [rows]);

  // Listas desplegables con lo que aparece en las ventas cargadas.
  const branches = useMemo(() => session?.access.branches ?? [], [session]);
  const branchChoices = useMemo(() => branchOptions(branches), [branches]);
  const cashierChoices = useMemo(() => distinctOptions(items.map((item) => item.row.cashier)), [items]);
  const methodChoices = useMemo(() => distinctOptions(items.map((item) => item.row.paymentMethod)), [items]);
  const customerChoices = useMemo(() => customerOptions(items), [items]);
  const selectedCustomer = filters.cliente
    ? (customerChoices.find((option) => option.value === filters.cliente) ?? { value: filters.cliente, label: filters.cliente, description: filters.cliente })
    : null;
  const branchLabel = useCallback((code: string | null) => {
    if (!code) return '—';
    const branch = branches.find((item) => item.code === code);
    return branch ? `${branch.code} · ${branch.name}` : code;
  }, [branches]);

  const abilities: SaleAbilities = {
    reprint: canRun('GetFiscalPrintModelQuery'),
    email: canRun('SendFiscalDocumentEmailCommand'),
    returns: canRun('CreateSalesReturnCommand'),
    void: canRun('VoidSaleCommand'),
  };

  // Detalles y diálogos. Cada diálogo se monta de nuevo en cada apertura (`key`): su formulario empieza vacío.
  const [detail, setDetail] = useState<Shown<SaleItem> | null>(null);
  const [returnDetail, setReturnDetail] = useState<Shown<ReturnItem> | null>(null);
  const [session_, setDialogSession] = useState(0);
  const [voiding, setVoiding] = useState<SaleItem | null>(null);
  const [returning, setReturning] = useState<ReturnTarget | null>(null);
  const [reprinting, setReprinting] = useState<InvoiceTarget | null>(null);
  const [emailing, setEmailing] = useState<InvoiceTarget | null>(null);
  const nextDialog = () => setDialogSession((count) => count + 1);

  const detailItem = detail ? (items.find((item) => item.key === detail.item.key) ?? detail.item) : null;
  const detailActions = detailItem ? saleActions(detailItem, abilities, fiscalEnabled) : null;

  const reloadAll = () => {
    sales.reload();
    fiscal.reload();
    returns.reload();
  };

  const setView = (next: SalesView) =>
    setParams(
      (previous) => {
        const updated = new URLSearchParams(previous);
        if (next === VIEWS.sales) updated.delete('vista');
        else updated.set('vista', next);
        return updated;
      },
      { replace: true },
    );

  // `?devolver=` (tablero «Registrar una devolución»): abre el diálogo y se quita de la dirección.
  const devolver = params.get('devolver');
  const canReturn = abilities.returns;
  useEffect(() => {
    if (devolver === null) return;
    setParams(
      (previous) => {
        const updated = new URLSearchParams(previous);
        updated.delete('devolver');
        return updated;
      },
      { replace: true },
    );
    if (!canReturn) return;
    setDialogSession((count) => count + 1);
    setReturning({ invoiceNumber: /^[a-z]+-/i.test(devolver) ? normalizeSaleNumber(devolver) : null });
  }, [devolver, canReturn, setParams]);

  // ---------------------------------------------------------------------------------------------- acciones
  const openDetail = (item: SaleItem) => setDetail({ item, open: true });
  const closeDetail = () => setDetail((current) => (current?.open ? { ...current, open: false } : current));
  const onlyCustomer = (item: SaleItem) => {
    table.setFilter('cliente', item.row.customerCode);
    closeDetail();
  };
  const invoiceOf = (item: SaleItem): InvoiceTarget | null => (item.fiscal ? { invoiceNumber: item.key, customer: item.row.customer, fiscal: item.fiscal } : null);
  const askReprint = (item: SaleItem) => {
    nextDialog();
    setReprinting(invoiceOf(item));
  };
  const askEmail = (item: SaleItem) => {
    nextDialog();
    setEmailing(invoiceOf(item));
  };
  const askReturn = (invoiceNumber: string | null) => {
    nextDialog();
    setReturning({ invoiceNumber });
  };
  const askVoid = (item: SaleItem) => {
    nextDialog();
    setVoiding(item);
  };

  const rowActions = (item: SaleItem): RowActionItem[] => {
    const actions = saleActions(item, abilities, fiscalEnabled);
    return [
      { label: 'Ver detalle', icon: <Eye />, onSelect: () => openDetail(item) },
      { label: 'Ver solo este cliente', icon: <Filter />, onSelect: () => onlyCustomer(item), hidden: filters.cliente === item.row.customerCode },
      { label: 'Reimprimir la factura', icon: <Printer />, onSelect: () => askReprint(item), hidden: !actions.reprint.visible },
      {
        label: 'Enviar la factura por correo',
        icon: <Mail />,
        onSelect: () => askEmail(item),
        hidden: !actions.email.visible,
        disabled: actions.email.blocked !== null,
        disabledReason: actions.email.blocked ?? undefined,
      },
      {
        label: 'Devolver productos',
        icon: <Undo2 />,
        onSelect: () => askReturn(item.key),
        hidden: !actions.returns.visible,
        disabled: actions.returns.blocked !== null,
        disabledReason: actions.returns.blocked ?? undefined,
      },
      {
        label: 'Anular la venta',
        icon: <Ban />,
        tone: 'danger',
        onSelect: () => askVoid(item),
        hidden: !actions.void.visible,
        disabled: actions.void.blocked !== null,
        disabledReason: actions.void.blocked ?? undefined,
      },
    ];
  };

  const exportSales = () => {
    const file = exportCsv('ventas', SALES_CSV, rows);
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(rows.length)} filas).`);
  };
  const exportReturns = () => {
    const file = exportCsv('devoluciones', RETURNS_CSV, returnRows);
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(returnRows.length)} filas).`);
  };

  const clearFilters = () => table.setFilters(clearedFilters(defaults, view));
  const openSale = (invoiceNumber: string) => {
    // «Ver la venta» desde una devolución: la lista de ventas buscando ese número, en todas las fechas.
    table.setFilters({ ...clearedFilters(defaults, VIEWS.sales), q: invoiceNumber, desde: '', hasta: '' });
    setReturnDetail((current) => (current?.open ? { ...current, open: false } : current));
    setView(VIEWS.sales);
  };

  const loadingRange = salesRange === null;
  const rangeHint = 'Hoy por defecto. «Todas» revisa todas las ventas y puede tardar.';
  const secondaryError = (fiscalEnabled ? fiscal.error : null) ?? returns.error;
  const returnItem = returnDetail?.item;

  // ---------------------------------------------------------------------------------------------- filtros
  const sharedFilters = (
    <>
      <SearchField
        label="Buscar"
        placeholder={view === VIEWS.sales ? 'Número de venta, pedido, cliente o factura' : 'Devolución, venta, cliente o motivo'}
        value={filters.q}
        onChange={(q) => table.setFilter('q', q)}
      />
      {branchChoices.length > 1 && (
        <SelectField label="Sucursal" allLabel="Todas las sucursales" value={filters.sucursal} onChange={(value) => table.setFilter('sucursal', value)} options={branchChoices} />
      )}
      <ComboBox
        label="Cliente"
        placeholder="Nombre o código"
        value={selectedCustomer}
        onChange={(option) => table.setFilter('cliente', option?.value ?? '')}
        options={customerChoices}
        emptyText="Ningún cliente con ventas en estas fechas"
      />
    </>
  );

  return (
    <Page
      title="Ventas"
      description="Ventas de sus sucursales con su factura del SIN: detalle, reimpresión, devoluciones y anulaciones."
      actions={
        <>
          {canReturn && (
            <Button variant="outline" leftIcon={<Undo2 />} onClick={() => askReturn(null)}>
              Registrar devolución
            </Button>
          )}
          {can('sales.pos.operate') && (
            <Button leftIcon={<ShoppingCart />} to={ROUTES.panelModule('caja')}>
              Nueva venta
            </Button>
          )}
        </>
      }
    >
      <Tabs
        label="Ventas y devoluciones"
        value={view}
        onChange={setView}
        tabs={[
          { id: VIEWS.sales, label: 'Ventas', icon: <Receipt />, count: sales.data ? rows.length : undefined },
          { id: VIEWS.returns, label: 'Devoluciones', icon: <Undo2 />, count: returns.data ? returnRows.length : undefined },
        ]}
      >
        <TabPanel id={VIEWS.sales} className="space-y-4">
          <FilterBar activeCount={activeFilters(filters, defaults, VIEWS.sales)} onClear={clearFilters}>
            <DateRangeField label="Fechas" value={range} onChange={(value) => table.setDateRange(value)} hint={rangeHint} />
            {sharedFilters}
            <SelectField label="Estado" value={filters.estado} onChange={(value) => table.setFilter('estado', value)} options={statusOptions(SALE_STATES)} />
            {fiscalEnabled && (
              <SelectField label="Factura del SIN" value={filters.fiscal} onChange={(value) => table.setFilter('fiscal', value)} options={statusOptions(FISCAL_STATES)} />
            )}
            <SelectField label="Cajero" allLabel="Todos los cajeros" value={filters.cajero} onChange={(value) => table.setFilter('cajero', value)} options={cashierChoices} />
            <SelectField label="Medio de pago" allLabel="Todos los medios" value={filters.pago} onChange={(value) => table.setFilter('pago', value)} options={methodChoices} />
          </FilterBar>

          <Toolbar
            end={
              <>
                <Button variant="outline" leftIcon={<RefreshCw />} loading={sales.fetching && !sales.loading} onClick={reloadAll}>
                  Actualizar
                </Button>
                <Button variant="outline" leftIcon={<Download />} disabled={rows.length === 0} onClick={exportSales}>
                  Exportar CSV
                </Button>
              </>
            }
          >
            {sales.data && (
              <span className="text-sm text-text-muted" data-testid="ventas-resumen">
                {formatNumber(rows.length)} de {formatNumber(items.length)} ventas · vendido {formatMoney(summary.total)} · {formatNumber(summary.voided)}{' '}
                {summary.voided === 1 ? 'anulada' : 'anuladas'}
              </span>
            )}
          </Toolbar>

          {secondaryError && (
            <Alert
              tone="warning"
              title="Parte de la información no se pudo cargar"
              actions={
                <Button variant="outline" leftIcon={<RefreshCw />} onClick={reloadAll}>
                  Reintentar
                </Button>
              }
            >
              {fiscalEnabled && fiscal.error ? 'No se pudo leer el estado de las facturas del SIN. ' : ''}
              {returns.error ? 'No se pudieron leer las devoluciones.' : ''}
            </Alert>
          )}

          <Collapsible label="Ver resumen del período" openLabel="Ocultar resumen del período" icon={<BarChart3 />} description="Lo vendido, el ticket promedio, las anuladas y las devoluciones de la lista.">
            <div className="grid grid-cols-[repeat(auto-fit,minmax(11rem,1fr))] gap-3" data-testid="resumen-periodo">
              <StatCard label="Vendido" value={formatMoney(summary.total)} hint={`IVA incluido ${formatMoney(summary.tax)}`} icon={<Receipt />} tone="success" />
              <StatCard label="Ventas" value={formatNumber(summary.valid)} hint={`${formatNumber(summary.lines)} líneas vendidas`} icon={<ShoppingCart />} />
              <StatCard
                label="Ticket promedio"
                value={summary.valid > 0 ? formatMoney(summary.average) : '—'}
                hint={summary.valid > 0 ? `La mayor: ${formatMoney(summary.largest)}` : undefined}
                icon={<BarChart3 />}
              />
              <StatCard
                label="Anuladas"
                value={formatNumber(summary.voided)}
                hint={summary.voided > 0 ? `${formatMoney(summary.voidedTotal)} anulados` : 'Sin anulaciones'}
                icon={<Ban />}
                tone={summary.voided > 0 ? 'danger' : 'default'}
              />
              <StatCard
                label="Con devolución"
                value={formatNumber(summary.withReturns)}
                hint={summary.refunded > 0 ? `${formatMoney(summary.refunded)} reembolsados` : 'Sin devoluciones'}
                icon={<Undo2 />}
                tone={summary.withReturns > 0 ? 'warning' : 'default'}
              />
            </div>
          </Collapsible>

          <DataTable
            caption="Ventas del período"
            columns={columns}
            rows={sales.data ? rows : undefined}
            rowKey={(item) => item.key}
            rowLabel={(item) => `la venta ${item.key}`}
            loading={sales.loading || (loadingRange && !sales.data)}
            refreshing={sales.fetching && !sales.loading}
            error={sales.error}
            onRetry={sales.reload}
            operation="GetSalesQuery"
            {...table.tableProps}
            onRowOpen={openDetail}
            activeRowKey={detail?.open ? detail.item.key : null}
            rowActions={rowActions}
            footerLabel="Totales (sin anuladas)"
            empty={{
              title: loadingRange ? 'Corrija las fechas' : items.length === 0 ? 'No hay ventas en estas fechas' : 'No hay ventas con estos filtros',
              description: loadingRange
                ? 'La fecha «Desde» no puede ser posterior a «Hasta».'
                : items.length === 0
                  ? 'Pruebe con otras fechas o vea todas las ventas.'
                  : 'Pruebe con otros filtros o limpie los filtros.',
              icon: <Receipt />,
              action: (
                <Button variant="outline" onClick={clearFilters}>
                  Limpiar filtros
                </Button>
              ),
            }}
          />
        </TabPanel>

        <TabPanel id={VIEWS.returns} className="space-y-4">
          <FilterBar activeCount={activeFilters(filters, defaults, VIEWS.returns)} onClear={clearFilters}>
            <DateRangeField label="Fechas" value={range} onChange={(value) => table.setDateRange(value)} hint={rangeHint} />
            {sharedFilters}
            <SelectField label="Nota crédito-débito" value={filters.nota} onChange={(value) => table.setFilter('nota', value)} options={statusOptions(NOTE_STATES)} />
          </FilterBar>

          <Toolbar
            end={
              <>
                <Button variant="outline" leftIcon={<RefreshCw />} loading={returns.fetching && !returns.loading} onClick={reloadAll}>
                  Actualizar
                </Button>
                <Button variant="outline" leftIcon={<Download />} disabled={returnRows.length === 0} onClick={exportReturns}>
                  Exportar CSV
                </Button>
              </>
            }
          >
            {returns.data && (
              <span className="text-sm text-text-muted" data-testid="devoluciones-resumen">
                {formatNumber(returnRows.length)} {returnRows.length === 1 ? 'devolución' : 'devoluciones'} · reembolsado{' '}
                {formatMoney(returnRows.reduce((sum, item) => sum + item.row.refund, 0))}
              </span>
            )}
          </Toolbar>

          <DataTable
            caption="Devoluciones del período"
            columns={RETURN_COLUMNS}
            rows={returns.data ? returnRows : undefined}
            rowKey={(item) => item.key}
            rowLabel={(item) => `la devolución ${item.key}`}
            loading={returns.loading || (loadingRange && !returns.data)}
            refreshing={returns.fetching && !returns.loading}
            error={returns.error}
            onRetry={returns.reload}
            operation="GetSalesReturnsQuery"
            {...returnsTable.tableProps}
            onRowOpen={(item) => setReturnDetail({ item, open: true })}
            activeRowKey={returnDetail?.open ? returnDetail.item.key : null}
            rowActions={(item) => [
              { label: 'Ver detalle', icon: <Eye />, onSelect: () => setReturnDetail({ item, open: true }) },
              { label: 'Ver la venta', icon: <Receipt />, onSelect: () => openSale(item.row.invoiceNumber) },
            ]}
            empty={{
              title: returnItems.length === 0 ? 'Sin devoluciones en estas fechas' : 'No hay devoluciones con estos filtros',
              description:
                returnItems.length === 0
                  ? 'Elija una venta y use «Devolver productos»: vuelve el stock y, si tiene factura, se emite la nota crédito-débito.'
                  : 'Pruebe con otros filtros o limpie los filtros.',
              icon: <Undo2 />,
              action:
                returnItems.length === 0 && canReturn ? (
                  <Button variant="outline" leftIcon={<Undo2 />} onClick={() => askReturn(null)}>
                    Registrar devolución
                  </Button>
                ) : (
                  <Button variant="outline" onClick={clearFilters}>
                    Limpiar filtros
                  </Button>
                ),
            }}
          />
        </TabPanel>
      </Tabs>

      <SaleDetailPanel
        item={detailItem}
        open={detail?.open ?? false}
        onClose={closeDetail}
        fiscalEnabled={fiscalEnabled}
        actions={detailActions}
        branchLabel={branchLabel}
        onReprint={askReprint}
        onEmail={askEmail}
        onReturn={(item) => askReturn(item.key)}
        onVoid={askVoid}
        onOnlyCustomer={onlyCustomer}
        filteredByCustomer={detailItem !== null && filters.cliente === detailItem.row.customerCode}
      />

      <SidePanel
        open={returnDetail?.open ?? false}
        onClose={() => setReturnDetail((current) => (current?.open ? { ...current, open: false } : current))}
        title={returnItem ? `Devolución ${returnItem.key}` : 'Devolución'}
        description={returnItem ? formatDateTime(returnItem.row.returnedAt) : undefined}
        size="md"
        footer={
          returnItem && (
            <Button variant="outline" leftIcon={<Receipt />} fullWidth onClick={() => openSale(returnItem.row.invoiceNumber)}>
              Ver la venta {returnItem.row.invoiceNumber}
            </Button>
          )
        }
      >
        {returnItem && (
          <DetailList
            columns={1}
            items={[
              { label: 'Venta', value: returnItem.row.invoiceNumber },
              { label: 'Sucursal', value: branchLabel(returnItem.branch) },
              { label: 'Cliente', value: returnItem.row.customer },
              { label: 'Motivo', value: returnItem.row.reason },
              { label: 'Reembolso', value: formatMoney(returnItem.row.refund) },
              {
                label: 'Nota crédito-débito',
                value: returnItem.row.creditNote ? (
                  <span className="inline-flex flex-wrap items-center gap-2">
                    {returnItem.row.creditNote}
                    <StatusBadge status={returnItem.noteState} statuses={NOTE_STATES} />
                  </span>
                ) : (
                  'Sin nota: la venta no tenía factura del SIN.'
                ),
              },
            ]}
          />
        )}
      </SidePanel>

      <VoidSaleDialog key={`anular-${session_}`} target={voiding} onClose={() => setVoiding(null)} onVoided={reloadAll} />
      <ReturnDialog
        key={`devolver-${session_}`}
        target={returning}
        onClose={() => setReturning(null)}
        onReturned={() => {
          reloadAll();
          closeDetail();
          setView(VIEWS.returns);
        }}
      />
      <ReprintDialog key={`reimprimir-${session_}`} target={reprinting} onClose={() => setReprinting(null)} />
      <SendInvoiceDialog key={`correo-${session_}`} target={emailing} onClose={() => setEmailing(null)} />
    </Page>
  );
}
