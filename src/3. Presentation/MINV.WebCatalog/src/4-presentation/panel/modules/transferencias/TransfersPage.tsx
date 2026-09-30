// Sucursales › Transferencias (paquete M7): lo mismo que «Transferencias» del escritorio (TransfersView +
// TransfersViewModel) y más. Lista de `GetTransfersQuery` con filtros en la dirección (búsqueda; estado Pendiente,
// Despachada, Recibida o Anulada; origen; destino; lo que espera a mis sucursales; fechas; cuántas revisar), tabla
// ordenable, detalle lateral con líneas, series, faltantes y la BITÁCORA, y las acciones según el lado (regla B-03):
// el origen solicita (`CreateTransferCommand`: origen = sucursal activa, destino en lista desplegable, productos y
// series), despacha (`DispatchTransferCommand`) y anula (`CancelTransferCommand`, con motivo); el destino recibe
// (`ReceiveTransferCommand`, con faltantes y su motivo). Los indicadores del escritorio van PLEGADOS (regla P-10).

import { ArrowRightLeft, BarChart3, Ban, Download, Eye, PackageCheck, Plus, RefreshCw, Truck } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { usePermissions, useRpcQuery, useTableState } from '@/4-presentation/panel/hooks';
import {
  Button,
  Collapsible,
  DataTable,
  DateRangeField,
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
import { exportCsv, formatDateTime, formatMoney, formatNumber, formatQuantity, laPazToday, sortRows, type SortState } from '@/4-presentation/panel/lib';
import { CancelTransferDialog } from './CancelTransferDialog';
import { DispatchTransferDialog } from './DispatchTransferDialog';
import { NewTransferDialog } from './NewTransferDialog';
import { ReceiveTransferDialog } from './ReceiveTransferDialog';
import { TransferDetailPanel } from './TransferDetailPanel';
import { TransfersOverview } from './TransfersOverview';
import { useRowMenuGuard } from './rowGuard';
import {
  TAKE_OPTIONS,
  TRANSFERS_CSV,
  TRANSFER_FILTERS,
  TRANSFER_STATES,
  WAITING_OPTIONS,
  branchOptions,
  filterTransfers,
  plainMessage,
  takeOf,
  toTransferItems,
  transferStateLabel,
  transfersSummary,
  type TransferItem,
  type TransferOutcome,
} from './transfers';

const COLUMNS: DataTableColumn<TransferItem>[] = [
  { id: 'numero', header: 'Número', value: (item) => item.row.number, card: 'title', className: 'whitespace-nowrap' },
  {
    id: 'ruta',
    header: 'Ruta',
    value: (item) => item.route,
    cell: (item) => (
      <span className="block min-w-44">
        <span className="block font-medium">{item.route}</span>
        <span className="block text-xs text-text-muted">
          {item.row.fromWarehouse} → {item.row.toWarehouse} · {item.row.toBranch}
        </span>
      </span>
    ),
  },
  {
    id: 'estado',
    header: 'Estado',
    value: (item) => transferStateLabel(item.row.status),
    cell: (item) => (
      <span className="flex flex-col items-start gap-1">
        <StatusBadge status={item.row.status} statuses={TRANSFER_STATES} />
        {item.row.canDispatch && <span className="text-xs font-semibold text-warning-text">Por despachar aquí</span>}
        {item.row.canReceive && <span className="text-xs font-semibold text-accent-hover">Por recibir aquí</span>}
      </span>
    ),
  },
  { id: 'solicitada', header: 'Solicitada', value: (item) => new Date(item.row.requestedAt), cell: (item) => formatDateTime(item.row.requestedAt), className: 'whitespace-nowrap' },
  { id: 'productos', header: 'Productos', align: 'end', value: (item) => item.row.lines },
  { id: 'cantidad', header: 'Cantidad', align: 'end', value: (item) => item.row.quantity, cell: (item) => formatQuantity(item.row.quantity) },
  {
    id: 'valor',
    header: 'Valor',
    align: 'end',
    value: (item) => (item.row.status === 'Pending' ? null : item.row.value),
    cell: (item) => (item.row.status === 'Pending' ? '—' : formatMoney(item.row.value)),
    footer: (rows) => formatMoney(rows.reduce((sum, item) => sum + (item.row.status === 'Pending' ? 0 : item.row.value), 0)),
  },
  { id: 'faltante', header: 'Faltante', align: 'end', value: (item) => item.row.shortage, cell: (item) => (item.row.shortage > 0 ? formatQuantity(item.row.shortage) : '—') },
];

function inTableOrder(list: readonly TransferItem[], sort: SortState | null): readonly TransferItem[] {
  const column = sort ? COLUMNS.find((item) => item.id === sort.column) : undefined;
  return sort && column?.value ? sortRows(list, column.value, sort.direction) : list;
}

interface Shown<T> {
  item: T;
  open: boolean;
}

export function TransfersPage() {
  const notify = useNotify();
  const { canRun, session } = usePermissions();
  const [today] = useState(() => laPazToday());
  const table = useTableState({ filters: TRANSFER_FILTERS, sort: { column: 'solicitada', direction: 'desc' } });
  const filters = table.filters;
  const [params, setParams] = useSearchParams();

  const transfers = useRpcQuery('GetTransfersQuery', { status: null, take: takeOf(filters.registros) });
  const branches = useRpcQuery('GetBranchesQuery', {});
  const items = useMemo(() => toTransferItems(transfers.data ?? []), [transfers.data]);
  const rows = useMemo(() => filterTransfers(items, filters), [items, filters]);
  const branchChoices = useMemo(() => branchOptions(branches.data ?? [], items), [branches.data, items]);
  const summary = useMemo(() => transfersSummary(transfers.data ?? [], today), [transfers.data, today]);

  const canCreate = canRun('CreateTransferCommand');
  const canDispatch = canRun('DispatchTransferCommand');
  const canReceive = canRun('ReceiveTransferCommand');
  const canCancel = canRun('CancelTransferCommand');
  const { fromMenu, guardOpen } = useRowMenuGuard();

  const [detail, setDetail] = useState<Shown<TransferItem> | null>(null);
  const [dialogSession, setDialogSession] = useState(0);
  const [creating, setCreating] = useState(false);
  const [dispatching, setDispatching] = useState<TransferItem | null>(null);
  const [receiving, setReceiving] = useState<TransferItem | null>(null);
  const [cancelling, setCancelling] = useState<TransferItem | null>(null);

  const detailItem = detail ? (items.find((item) => item.key === detail.item.key) ?? detail.item) : null;
  const openDetail = (item: TransferItem) => setDetail({ item, open: true });
  const closeDetail = () => setDetail((current) => (current?.open ? { ...current, open: false } : current));
  const next = () => setDialogSession((count) => count + 1);
  const askCreate = () => {
    next();
    setCreating(true);
  };
  const askDispatch = (item: TransferItem) => {
    next();
    setDispatching(item);
  };
  const askReceive = (item: TransferItem) => {
    next();
    setReceiving(item);
  };
  const askCancel = (item: TransferItem) => {
    next();
    setCancelling(item);
  };

  // `?nueva=1` (tablero «Nueva transferencia»): abre la transferencia nueva una vez y se quita de la dirección.
  const newParam = params.get('nueva');
  const [handledNew, setHandledNew] = useState<string | null>(null);
  if (newParam !== handledNew) {
    setHandledNew(newParam);
    if (newParam !== null && canCreate) {
      next();
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

  const changed = () => {
    transfers.reload();
    branches.reload();
  };
  // La transferencia recién solicitada se abre en el detalle cuando llega a la lista.
  const [openWhenListed, setOpenWhenListed] = useState<string | null>(null);
  if (openWhenListed) {
    const listed = items.find((item) => item.key === openWhenListed);
    if (listed) {
      setOpenWhenListed(null);
      setDetail({ item: listed, open: true });
    }
  }
  const created = (outcome: TransferOutcome) => {
    notify.success('Transferencia solicitada', plainMessage(outcome.message));
    setOpenWhenListed(outcome.id);
    changed();
  };

  const exportRows = () => {
    const file = exportCsv('transferencias', TRANSFERS_CSV, inTableOrder(rows, table.sort));
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(rows.length)} filas).`);
  };

  const rowActions = (item: TransferItem) => [
    { label: 'Ver detalle', icon: <Eye />, onSelect: fromMenu(() => openDetail(item)) },
    { label: 'Recibir', icon: <PackageCheck />, onSelect: fromMenu(() => askReceive(item)), hidden: !canReceive || !item.row.canReceive },
    { label: 'Despachar', icon: <Truck />, onSelect: fromMenu(() => askDispatch(item)), hidden: !canDispatch || !item.row.canDispatch },
    { label: 'Anular', icon: <Ban />, tone: 'danger' as const, onSelect: fromMenu(() => askCancel(item)), hidden: !canCancel || !item.row.canCancel },
  ];

  const toDispatch = rows.filter((item) => item.row.canDispatch).length;
  const toReceive = rows.filter((item) => item.row.canReceive).length;

  return (
    <Page
      title="Transferencias"
      description="Mercadería entre sucursales: el origen solicita, despacha y anula; el destino recibe y registra los faltantes. Nada se borra: todo queda en la bitácora."
      actions={
        canCreate && (
          <Button leftIcon={<Plus />} onClick={askCreate}>
            Nueva transferencia
          </Button>
        )
      }
    >
      <FilterBar activeCount={table.activeFilterCount} onClear={table.clearFilters}>
        <SearchField label="Buscar" placeholder="Número, sucursal, almacén o notas" value={filters.q} onChange={(q) => table.setFilter('q', q)} />
        <SelectField label="Estado" value={filters.estado} onChange={(value) => table.setFilter('estado', value)} options={statusOptions(TRANSFER_STATES)} />
        <SelectField label="Origen" allLabel="Todas las sucursales" value={filters.origen} onChange={(value) => table.setFilter('origen', value)} options={branchChoices} />
        <SelectField label="Destino" allLabel="Todas las sucursales" value={filters.destino} onChange={(value) => table.setFilter('destino', value)} options={branchChoices} />
        <SelectField label="Pendiente en mis sucursales" value={filters.pendiente} onChange={(value) => table.setFilter('pendiente', value)} options={WAITING_OPTIONS} />
        <SelectField
          label="Transferencias a revisar"
          allLabel={false}
          value={filters.registros}
          onChange={(value) => table.setFilter('registros', value || TRANSFER_FILTERS.registros)}
          options={TAKE_OPTIONS}
          hint="Las más recientes; los filtros se aplican sobre ellas."
        />
        <DateRangeField label="Fecha de la solicitud" value={table.dateRange()} onChange={(range) => table.setDateRange(range)} />
      </FilterBar>

      <Toolbar
        end={
          <>
            <Button variant="outline" leftIcon={<RefreshCw />} loading={transfers.fetching && !transfers.loading} onClick={transfers.reload}>
              Actualizar
            </Button>
            <Button variant="outline" leftIcon={<Download />} disabled={rows.length === 0} onClick={exportRows}>
              Exportar CSV
            </Button>
          </>
        }
      >
        {transfers.data && (
          <span className="text-sm text-text-muted" data-testid="transferencias-resumen">
            {formatNumber(rows.length)} de {formatNumber(items.length)} transferencias · {formatNumber(toDispatch)} por despachar y {formatNumber(toReceive)} por recibir en
            mis sucursales
          </span>
        )}
      </Toolbar>

      <Collapsible
        label="Ver resumen de las transferencias"
        openLabel="Ocultar resumen de las transferencias"
        icon={<BarChart3 />}
        description="Pendientes de despacho, en tránsito, recibidas y faltantes del mes."
      >
        <TransfersOverview summary={transfers.data ? summary : null} loading={!transfers.data} />
      </Collapsible>

      <DataTable
        caption="Transferencias entre sucursales"
        columns={COLUMNS}
        rows={transfers.data ? rows : undefined}
        rowKey={(item) => item.key}
        rowLabel={(item) => `la transferencia ${item.row.number} (${item.route})`}
        loading={transfers.loading}
        refreshing={transfers.fetching && !transfers.loading}
        error={transfers.error}
        onRetry={transfers.reload}
        operation="GetTransfersQuery"
        {...table.tableProps}
        onRowOpen={guardOpen(openDetail)}
        activeRowKey={detail?.open ? detail.item.key : null}
        rowActions={rowActions}
        empty={{
          title: items.length === 0 ? 'Todavía no hay transferencias' : 'Ninguna transferencia coincide con los filtros',
          description: items.length === 0 ? 'Solicite mercadería de otra sucursal con «Nueva transferencia».' : 'Pruebe con otro estado, sucursal o fechas.',
          icon: <ArrowRightLeft />,
          action:
            items.length === 0 ? (
              canCreate && (
                <Button leftIcon={<Plus />} onClick={askCreate}>
                  Nueva transferencia
                </Button>
              )
            ) : (
              <Button variant="outline" onClick={table.clearFilters}>
                Limpiar filtros
              </Button>
            ),
        }}
      />

      <TransferDetailPanel
        item={detailItem}
        open={detail?.open ?? false}
        onClose={closeDetail}
        canManage={canDispatch || canReceive || canCancel}
        onDispatch={askDispatch}
        onReceive={askReceive}
        onCancel={askCancel}
      />

      <NewTransferDialog
        key={`nueva-${dialogSession}`}
        open={creating}
        branches={branches.data ?? []}
        activeBranchId={session?.access.activeBranchId ?? null}
        onClose={() => setCreating(false)}
        onCreated={created}
      />
      <DispatchTransferDialog key={`despacho-${dialogSession}`} target={dispatching} onClose={() => setDispatching(null)} onDone={changed} />
      <ReceiveTransferDialog key={`recepcion-${dialogSession}`} target={receiving} onClose={() => setReceiving(null)} onDone={changed} />
      <CancelTransferDialog key={`anulacion-${dialogSession}`} target={cancelling} onClose={() => setCancelling(null)} onDone={changed} />
    </Page>
  );
}
