// Ventas › Reservas: TODAS las reservas de la tienda web y del mostrador, de compra (carritos RES-…) y de armado (ARM-…),
// en una lista profesional: filtros en la dirección (tipo, canal, estado, vence, sucursal, fechas y búsqueda por número,
// nombre o teléfono), tabla ordenable y paginada con el plazo resaltado si vence en menos de 6 horas y el estado del correo,
// detalle lateral (productos, datos de factura, notas, bitácora y correos), exportar CSV y las acciones:
//   · Vender en caja   → enlace a la caja con la reserva cargada (`/panel/caja?reserva=<NÚMERO>`).
//   · Liberar          → `ReleasePcBuildReservationCommand` con motivo y confirmación.
//   · Reenviar correo  → `ResendReservationMailCommand` (a otro correo, si se indica).
//   · Copiar teléfono y Abrir WhatsApp (enlace armado solo con los dígitos del teléfono).
//   · Nueva reserva en mostrador → `ReserveCartCommand` (también desde el tablero: `?nueva=1`).
// Las estadísticas quedan plegadas en «Ver reservas activas y su valor» (se cargan al abrir, regla P-10).

import clsx from 'clsx';
import { CalendarClock, Copy, Cpu, Download, Eye, Globe, LockOpen, MailPlus, MessageCircle, Plus, RefreshCw, ShoppingCart, Store, TriangleAlert } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { ROUTES } from '@/4-presentation/app/routes';
import { usePermissions, useRpcQuery, useTableState } from '@/4-presentation/panel/hooks';
import {
  Alert,
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
  statusOf,
  statusOptions,
  useNotify,
  type DataTableColumn,
} from '@/4-presentation/panel/kit';
import { exportCsv, formatMoney, formatNumber, toDate } from '@/4-presentation/panel/lib';
import { ActiveReservationsSummary } from './ActiveReservationsStat';
import { NewReservationDialog } from './NewReservationDialog';
import { ReleaseDialog, type ReleaseTarget } from './ReleaseDialog';
import { ReservationPanel } from './ReservationPanel';
import { ResendMailDialog, type ResendTarget } from './ResendMailDialog';
import {
  CHANNELS,
  CSV_COLUMNS,
  DUE_OPTIONS,
  KINDS,
  MAIL_STATES,
  RESERVATION_FILTERS,
  STATES,
  branchOptions,
  buildsRequest,
  canBeSold,
  filterReservations,
  listSummary,
  sellPath,
  stateLabel,
  toReservationItems,
  type BuildRecord,
  type ReservationItem,
} from './reservations';
import { useNow } from './useNow';

/** Correos que se leen para mostrar el estado del correo de cada reserva (los más recientes). */
const MAIL_TAKE = 500;
/** Parámetro de la dirección que abre la reserva en mostrador (botón del tablero). */
const NEW_PARAM = 'nueva';

const HOLD_CLASSES = { normal: 'text-text', soon: 'font-semibold text-warning-text', expired: 'font-semibold text-danger-text', none: 'text-text-muted' } as const;

/** Columnas de la tabla: fuera del componente (el plazo y el estado ya vienen calculados en cada fila). */
const COLUMNS: DataTableColumn<ReservationItem>[] = [
  {
    id: 'numero',
    header: 'Número',
    value: (item) => item.row.number,
    card: 'title',
    className: 'whitespace-nowrap',
    cell: (item) => (
      <span className="block">
        <span className="block tabular-nums">{item.row.number}</span>
        <span className="block max-w-56 truncate text-xs font-normal text-text-muted">{item.row.name}</span>
      </span>
    ),
  },
  { id: 'tipo', header: 'Tipo', value: (item) => statusOf(KINDS, item.row.kind).label, cell: (item) => <StatusBadge status={item.row.kind} statuses={KINDS} /> },
  {
    id: 'canal',
    header: 'Canal',
    value: (item) => statusOf(CHANNELS, item.row.channel).label,
    cell: (item) => <StatusBadge status={item.row.channel} statuses={CHANNELS} icon={item.row.channel === 'Web' ? <Globe /> : <Store />} />,
  },
  { id: 'cliente', header: 'Cliente', value: (item) => item.client, className: 'min-w-40' },
  { id: 'telefono', header: 'Teléfono', value: (item) => item.phone, className: 'whitespace-nowrap' },
  {
    id: 'total',
    header: 'Total',
    align: 'end',
    value: (item) => item.row.total,
    cell: (item) => formatMoney(item.row.total),
    footer: (rows) => formatMoney(rows.reduce((sum, item) => sum + item.row.total, 0)),
  },
  {
    id: 'plazo',
    header: 'Reservado hasta',
    value: (item) => toDate(item.row.reservedUntil),
    className: 'min-w-44',
    cell: (item) => (
      <span className={clsx('inline-flex items-start gap-1.5', HOLD_CLASSES[item.hold.tone])} data-plazo={item.hold.tone}>
        {(item.hold.tone === 'soon' || item.hold.tone === 'expired') && <TriangleAlert aria-hidden="true" className="mt-0.5 size-4 shrink-0" />}
        {item.hold.text}
      </span>
    ),
  },
  { id: 'estado', header: 'Estado', value: (item) => stateLabel(item.state), cell: (item) => <StatusBadge status={item.state} statuses={STATES} /> },
  {
    id: 'correo',
    header: 'Correo',
    value: (item) => statusOf(MAIL_STATES, item.mailState).label,
    cell: (item) => <StatusBadge status={item.mailState} statuses={MAIL_STATES} />,
  },
];

/** Ventas › Reservas (`/panel/reservas`). */
export function ReservationsPage() {
  const navigate = useNavigate();
  const notify = useNotify();
  const { can, canRun } = usePermissions();
  const [searchParams, setSearchParams] = useSearchParams();
  const table = useTableState({ filters: RESERVATION_FILTERS, sort: { column: 'plazo', direction: 'desc' } });
  const filters = table.filters;
  const now = useNow();

  // Lo que filtra el SERVIDOR va en el pedido (tipo, canal y estado); el resto se filtra en la página.
  const builds = useRpcQuery('GetPcBuildsQuery', buildsRequest(filters));
  const mails = useRpcQuery('GetOutgoingMailsQuery', { status: null, number: null, take: MAIL_TAKE });
  const items = useMemo(() => toReservationItems(builds.data ?? [], mails.data ?? null, now), [builds.data, mails.data, now]);
  const rows = useMemo(() => filterReservations(items, filters, now), [items, filters, now]);
  const branches = useMemo(() => branchOptions(items), [items]);
  const summary = listSummary(rows);

  const allowed = { sell: canRun('SellPcBuildCommand'), release: canRun('ReleasePcBuildReservationCommand'), resend: canRun('ResendReservationMailCommand') };
  const canCreate = canRun('ReserveCartCommand') && canRun('GetSellableProductsQuery');

  // Detalle abierto (se guarda el número y si está abierto: al cerrar, el panel se desliza con su contenido).
  const [detail, setDetail] = useState<{ number: string; open: boolean } | null>(null);
  const opened = detail?.open ? detail.number : null;
  const detailQuery = useRpcQuery('GetPcBuildQuery', { number: detail?.number ?? '' }, { enabled: opened !== null, keepPreviousData: false });
  const detailMails = useRpcQuery('GetOutgoingMailsQuery', { status: null, number: detail?.number ?? null, take: 50 }, { enabled: opened !== null, keepPreviousData: false });
  const detailItem = detail ? (items.find((item) => item.row.number === detail.number) ?? null) : null;

  const [releasing, setReleasing] = useState<ReleaseTarget | null>(null);
  const [resending, setResending] = useState<ResendTarget | null>(null);
  const [creating, setCreating] = useState(() => canCreate && searchParams.get(NEW_PARAM) !== null);

  // «Nueva reserva en mostrador» del tablero llega como `?nueva=1`: abre el formulario y se quita de la dirección.
  useEffect(() => {
    if (searchParams.get(NEW_PARAM) === null) return;
    if (canCreate) setCreating(true);
    setSearchParams(
      (current) => {
        const next = new URLSearchParams(current);
        next.delete(NEW_PARAM);
        return next;
      },
      { replace: true },
    );
  }, [searchParams, setSearchParams, canCreate]);

  const openDetail = (item: ReservationItem) => setDetail({ number: item.row.number, open: true });
  const closeDetail = () => setDetail((current) => (current?.open ? { ...current, open: false } : current));

  /** Vuelve a leer la lista, los correos y el detalle abierto (después de un comando o con «Actualizar»). */
  const refresh = () => {
    builds.reload();
    mails.reload();
    if (opened) {
      detailQuery.reload();
      detailMails.reload();
    }
  };

  const askRelease = (row: BuildRecord) => setReleasing({ number: row.number, name: row.name, total: row.total });
  const askResend = (row: BuildRecord) => setResending({ number: row.number, contactEmail: row.contactEmail });

  const copyPhone = async (phone: string) => {
    try {
      if (!navigator.clipboard) throw new Error('Sin portapapeles');
      await navigator.clipboard.writeText(phone);
      notify.success('Teléfono copiado', 'Péguelo para llamar o escribirle al cliente.');
    } catch {
      notify.warning('No se pudo copiar el teléfono', 'Cópielo a mano desde el detalle de la reserva.');
    }
  };

  const openWhatsApp = (url: string) => {
    window.open(url, '_blank', 'noopener,noreferrer');
  };

  const exportRows = () => {
    const file = exportCsv('reservas', CSV_COLUMNS, rows);
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(rows.length)} filas).`);
  };

  return (
    <Page
      title="Reservas"
      description="Reservas de la tienda web y del mostrador, de compra y de armado. Cóbrelas en la caja, libérelas o reenvíe la confirmación al cliente."
      actions={
        <>
          {can('sales.view') && can('inventory.stock.view') && (
            <Button variant="outline" leftIcon={<Cpu />} to={ROUTES.panelModule('armador/nuevo')}>
              Armar una PC
            </Button>
          )}
          {canCreate && (
            <Button leftIcon={<Plus />} onClick={() => setCreating(true)}>
              Nueva reserva en mostrador
            </Button>
          )}
        </>
      }
    >
      <FilterBar activeCount={table.activeFilterCount} onClear={table.clearFilters}>
        <SearchField label="Buscar" placeholder="Número, nombre o teléfono" value={filters.q} onChange={(q) => table.setFilter('q', q)} />
        <SelectField label="Tipo" value={filters.tipo} onChange={(value) => table.setFilter('tipo', value)} options={statusOptions(KINDS)} />
        <SelectField label="Canal" value={filters.canal} onChange={(value) => table.setFilter('canal', value)} options={statusOptions(CHANNELS)} />
        <SelectField label="Estado" value={filters.estado} onChange={(value) => table.setFilter('estado', value)} options={statusOptions(STATES)} />
        <SelectField label="Vence" allLabel="Cualquier día" value={filters.vence} onChange={(value) => table.setFilter('vence', value)} options={DUE_OPTIONS} />
        {(branches.length > 1 || filters.sucursal !== '') && (
          <SelectField label="Sucursal" allLabel="Todas las sucursales" value={filters.sucursal} onChange={(value) => table.setFilter('sucursal', value)} options={branches} />
        )}
        <DateRangeField label="Fecha de creación" value={table.dateRange()} onChange={(range) => table.setDateRange(range)} />
      </FilterBar>

      {mails.error && (
        <Alert
          tone="warning"
          title="No se pudo leer el estado de los correos"
          actions={
            <Button variant="outline" onClick={mails.reload}>
              Reintentar
            </Button>
          }
        >
          La lista de reservas funciona igual; la columna «Correo» queda sin datos.
        </Alert>
      )}

      <Toolbar
        end={
          <>
            <Button variant="outline" leftIcon={<RefreshCw />} loading={builds.fetching && !builds.loading} onClick={refresh}>
              Actualizar
            </Button>
            <Button variant="outline" leftIcon={<Download />} disabled={rows.length === 0} onClick={exportRows}>
              Exportar CSV
            </Button>
          </>
        }
      >
        {builds.data && (
          <span className="text-sm text-text-muted" data-testid="reservas-resumen">
            {summary.count === 1 ? '1 reserva' : `${formatNumber(summary.count)} reservas`} · {formatMoney(summary.total)}
            {summary.soon > 0 && (
              <span className="font-semibold text-warning-text">
                {' '}
                · {summary.soon === 1 ? '1 vence' : `${formatNumber(summary.soon)} vencen`} en menos de 6 h
              </span>
            )}
          </span>
        )}
      </Toolbar>

      <DataTable
        caption="Reservas"
        columns={COLUMNS}
        rows={builds.data ? rows : undefined}
        rowKey={(item) => item.row.number}
        rowLabel={(item) => `la reserva ${item.row.number}`}
        loading={builds.loading}
        refreshing={builds.fetching && !builds.loading}
        error={builds.error}
        onRetry={builds.reload}
        operation="GetPcBuildsQuery"
        {...table.tableProps}
        onRowOpen={openDetail}
        activeRowKey={detail?.open ? detail.number : null}
        rowActions={(item) => [
          { label: 'Ver detalle', icon: <Eye />, onSelect: () => openDetail(item) },
          {
            label: 'Vender en caja',
            icon: <ShoppingCart />,
            onSelect: () => navigate(ROUTES.panelModule(sellPath(item.row.number))),
            hidden: !allowed.sell || !canBeSold(item.row),
          },
          { label: 'Reenviar correo', icon: <MailPlus />, onSelect: () => askResend(item.row), hidden: !allowed.resend || item.state !== 'reservada' },
          { label: 'Copiar teléfono', icon: <Copy />, onSelect: () => void copyPhone(item.phone ?? ''), hidden: !item.phone },
          { label: 'Abrir WhatsApp', icon: <MessageCircle />, onSelect: () => openWhatsApp(item.whatsapp ?? ''), hidden: !item.whatsapp },
          { label: 'Liberar reserva', icon: <LockOpen />, tone: 'danger', onSelect: () => askRelease(item.row), hidden: !allowed.release || item.row.status !== 'Reserved' },
        ]}
        empty={{
          title: items.length === 0 ? 'Todavía no hay reservas' : 'No hay reservas con estos filtros',
          description: items.length === 0 ? 'Las reservas de la tienda web y las del mostrador aparecen aquí.' : 'Pruebe con otras fechas, otro estado o limpie los filtros.',
          icon: <CalendarClock />,
          action:
            items.length === 0 ? (
              canCreate && (
                <Button leftIcon={<Plus />} onClick={() => setCreating(true)}>
                  Nueva reserva en mostrador
                </Button>
              )
            ) : (
              <Button variant="outline" onClick={table.clearFilters}>
                Limpiar filtros
              </Button>
            ),
        }}
      />

      <Collapsible
        label="Ver reservas activas y su valor"
        openLabel="Ocultar reservas activas y su valor"
        icon={<CalendarClock />}
        description="Cuántas reservas vigentes hay, cuánto suman y cuántas vencen pronto. Se carga al abrir."
      >
        <ActiveReservationsSummary showLink={false} />
      </Collapsible>

      <ReservationPanel
        number={detail?.number ?? null}
        open={detail?.open ?? false}
        onClose={closeDetail}
        item={detailItem}
        detail={detailQuery}
        mails={detailMails}
        now={now}
        allowed={allowed}
        onRelease={askRelease}
        onResend={askResend}
        onCopyPhone={(phone) => void copyPhone(phone)}
      />

      <ReleaseDialog target={releasing} onClose={() => setReleasing(null)} onReleased={refresh} />
      <ResendMailDialog
        target={resending}
        onClose={() => setResending(null)}
        onQueued={() => {
          mails.reload();
          if (opened) detailMails.reload();
        }}
      />
      {canCreate && (
        <NewReservationDialog
          open={creating}
          onClose={() => setCreating(false)}
          onCreated={(row) => {
            refresh();
            setDetail({ number: row.number, open: true });
          }}
        />
      )}
    </Page>
  );
}
