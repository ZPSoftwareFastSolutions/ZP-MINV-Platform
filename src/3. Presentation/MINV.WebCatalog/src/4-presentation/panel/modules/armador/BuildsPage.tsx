// Tecnología › Armador de PC (`/panel/armador`): la lista de COTIZACIONES (armados de PC del mostrador y de la tienda web)
// con filtros en la dirección (búsqueda, estado, vigencia, publicada, canal y fechas), tabla ordenable y paginada, detalle
// lateral, exportar CSV y las acciones de cada estado: Abrir en el armador, Vender en caja (`/panel/caja?reserva=…`),
// Reservar stock, Liberar reserva, Publicar o quitar de la tienda web y Anular. «Armar una PC» abre el armador paso a
// paso. El resumen (cotizaciones vigentes, reservas web, vendidos, borradores) queda plegado en «Ver resumen».

import clsx from 'clsx';
import { Ban, BarChart3, Cpu, Download, Eye, Globe, GlobeLock, LockOpen, Clock, Plus, RefreshCw, ShoppingCart, Store } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { ROUTES } from '@/4-presentation/app/routes';
import { usePermissions, useRpcCommand, useRpcQuery, useTableState } from '@/4-presentation/panel/hooks';
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
  statusOf,
  statusOptions,
  useNotify,
  type DataTableColumn,
} from '@/4-presentation/panel/kit';
import { exportCsv, formatMoney, formatNumber } from '@/4-presentation/panel/lib';
import { BuildPanel } from './BuildPanel';
import { BuildsOverview } from './BuildsOverview';
import { CancelDialog, ReleaseDialog, ReserveDialog, type BuildTarget } from './BuildDialogs';
import {
  BUILD_FILTERS,
  BUILD_STATES,
  CHANNELS,
  CSV_COLUMNS,
  PUBLISHED_FILTER,
  STATUS_OPTIONS,
  VALIDITY_FILTER,
  buildsRequest,
  builderPath,
  canCancelBuild,
  canPublishBuild,
  canReleaseBuild,
  canReserveBuild,
  canSellBuild,
  canUnpublishBuild,
  filterBuilds,
  piecesText,
  sellPath,
  stateLabel,
  toBuildItems,
  type BuildItem,
  type BuildRecord,
} from './builder';
import { useNow } from './useNow';

const COMPATIBILITY_TONES = { success: 'text-success-text', warning: 'text-warning-text', danger: 'text-danger-text' } as const;

const COLUMNS: DataTableColumn<BuildItem>[] = [
  { id: 'numero', header: 'Número', value: (item) => item.row.number, card: 'title', className: 'whitespace-nowrap tabular-nums' },
  {
    id: 'armado',
    header: 'Armado',
    value: (item) => item.row.name,
    className: 'min-w-56',
    cell: (item) => (
      <span className="block">
        <span className="block font-medium text-text">{item.row.name}</span>
        <span className="block text-xs text-text-muted">
          {item.client} · {piecesText(item.row.items)}
        </span>
      </span>
    ),
  },
  {
    id: 'canal',
    header: 'Canal',
    value: (item) => statusOf(CHANNELS, item.row.channel).label,
    cell: (item) => <StatusBadge status={item.row.channel} statuses={CHANNELS} icon={item.row.channel === 'Web' ? <Globe /> : <Store />} />,
  },
  {
    id: 'compatibilidad',
    header: 'Compatibilidad',
    value: (item) => item.compatibility.text,
    cell: (item) => <span className={clsx('font-semibold', COMPATIBILITY_TONES[item.compatibility.tone])}>{item.compatibility.text}</span>,
  },
  { id: 'vigencia', header: 'Vigencia o venta', value: (item) => item.validity, className: 'min-w-40' },
  { id: 'estado', header: 'Estado', value: (item) => stateLabel(item.state), cell: (item) => <StatusBadge status={item.state} statuses={BUILD_STATES} /> },
  {
    id: 'web',
    header: 'Web',
    value: (item) => item.row.publishedToWeb,
    cell: (item) => (item.row.publishedToWeb ? <StatusBadge tone="success" icon={<Globe />}>Publicado</StatusBadge> : <span className="text-text-faint">—</span>),
  },
  {
    id: 'total',
    header: 'Total',
    align: 'end',
    value: (item) => item.row.total,
    cell: (item) => formatMoney(item.row.total),
    footer: (rows) => formatMoney(rows.reduce((sum, item) => sum + item.row.total, 0)),
  },
];

/** Tecnología › Armador de PC: lista de cotizaciones. */
export function BuildsPage() {
  const navigate = useNavigate();
  const notify = useNotify();
  const { canRun } = usePermissions();
  const table = useTableState({ filters: BUILD_FILTERS });
  const filters = table.filters;
  const now = useNow();

  // Lo que filtra el SERVIDOR (solo armados, estado y canal) va en el pedido; el resto se filtra en la página.
  const builds = useRpcQuery('GetPcBuildsQuery', buildsRequest(filters));
  const items = useMemo(() => toBuildItems(builds.data ?? [], now), [builds.data, now]);
  const rows = useMemo(() => filterBuilds(items, filters), [items, filters]);

  const allowed = {
    sell: canRun('SellPcBuildCommand'),
    reserve: canRun('ReservePcBuildCommand'),
    release: canRun('ReleasePcBuildReservationCommand'),
    publish: canRun('PublishPcBuildCommand'),
    cancel: canRun('CancelPcBuildCommand'),
  };

  const [detail, setDetail] = useState<{ number: string; open: boolean } | null>(null);
  const opened = detail?.open ? detail.number : null;
  const detailQuery = useRpcQuery('GetPcBuildQuery', { number: detail?.number ?? '' }, { enabled: opened !== null, keepPreviousData: false });
  const detailRow = detail ? (items.find((item) => item.row.number === detail.number)?.row ?? null) : null;

  const [reserving, setReserving] = useState<BuildTarget | null>(null);
  const [releasing, setReleasing] = useState<BuildTarget | null>(null);
  const [cancelling, setCancelling] = useState<BuildTarget | null>(null);

  const publish = useRpcCommand('PublishPcBuildCommand', {
    success: (row, payload) => (payload.published ? `${row.number} publicado en la tienda web` : `${row.number} retirado de la tienda web`),
    errorTitle: 'No se pudo cambiar la publicación',
  });

  const refresh = () => {
    builds.reload();
    if (opened) detailQuery.reload();
  };

  const openDetail = (item: BuildItem) => setDetail({ number: item.row.number, open: true });
  const togglePublish = async (row: BuildRecord) => {
    const outcome = await publish.run({ number: row.number, published: !row.publishedToWeb });
    if (outcome.ok) refresh();
  };

  const exportRows = () => {
    const file = exportCsv('armados', CSV_COLUMNS, rows);
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(rows.length)} filas).`);
  };

  const total = rows.reduce((sum, item) => sum + item.row.total, 0);

  return (
    <Page
      title="Armador de PC"
      description="Cotizaciones de PCs armadas: del mostrador y las que los clientes reservan en la tienda web. Abra una para ver sus piezas, cotícela, reserve el stock o cóbrela en la caja."
      actions={
        <Button leftIcon={<Plus />} to={ROUTES.panelModule(builderPath(null))}>
          Armar una PC
        </Button>
      }
    >
      <FilterBar activeCount={table.activeFilterCount} onClear={table.clearFilters}>
        <SearchField label="Buscar" placeholder="Número, armado o cliente" value={filters.q} onChange={(q) => table.setFilter('q', q)} />
        <SelectField label="Estado" value={filters.estado} onChange={(value) => table.setFilter('estado', value)} options={STATUS_OPTIONS} />
        <SelectField label="Vigencia" allLabel="Cualquiera" value={filters.vigencia} onChange={(value) => table.setFilter('vigencia', value)} options={VALIDITY_FILTER} />
        <SelectField label="Publicado en la web" value={filters.publicada} onChange={(value) => table.setFilter('publicada', value)} options={PUBLISHED_FILTER} />
        <SelectField label="Canal" value={filters.canal} onChange={(value) => table.setFilter('canal', value)} options={statusOptions(CHANNELS)} />
        <DateRangeField label="Fecha de creación" value={table.dateRange()} onChange={(range) => table.setDateRange(range)} />
      </FilterBar>

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
          <span className="text-sm text-text-muted" data-testid="armados-resumen">
            {rows.length === 1 ? '1 armado' : `${formatNumber(rows.length)} armados`} · {formatMoney(total)}
          </span>
        )}
      </Toolbar>

      <DataTable
        caption="Armados de PC"
        columns={COLUMNS}
        rows={builds.data ? rows : undefined}
        rowKey={(item) => item.row.number}
        rowLabel={(item) => `la cotización ${item.row.number}`}
        loading={builds.loading}
        refreshing={builds.fetching && !builds.loading}
        error={builds.error}
        onRetry={builds.reload}
        operation="GetPcBuildsQuery"
        {...table.tableProps}
        onRowOpen={openDetail}
        activeRowKey={detail?.open ? detail.number : null}
        rowActions={(item) => {
          const row = item.row;
          const target = { number: row.number, name: row.name, total: row.total };
          return [
            { label: 'Abrir en el armador', icon: <Cpu />, onSelect: () => navigate(ROUTES.panelModule(builderPath(row.number))) },
            { label: 'Ver detalle', icon: <Eye />, onSelect: () => openDetail(item) },
            { label: 'Vender en caja', icon: <ShoppingCart />, onSelect: () => navigate(ROUTES.panelModule(sellPath(row.number))), hidden: !allowed.sell || !canSellBuild(row) },
            { label: 'Reservar stock', icon: <Clock />, onSelect: () => setReserving(target), hidden: !allowed.reserve || !canReserveBuild(row) },
            { label: 'Publicar en la web', icon: <Globe />, onSelect: () => void togglePublish(row), hidden: !allowed.publish || !canPublishBuild(row) },
            { label: 'Quitar de la web', icon: <GlobeLock />, onSelect: () => void togglePublish(row), hidden: !allowed.publish || !canUnpublishBuild(row) },
            { label: 'Liberar reserva', icon: <LockOpen />, tone: 'danger', onSelect: () => setReleasing(target), hidden: !allowed.release || !canReleaseBuild(row) },
            { label: 'Anular armado', icon: <Ban />, tone: 'danger', onSelect: () => setCancelling(target), hidden: !allowed.cancel || !canCancelBuild(row) },
          ];
        }}
        empty={{
          title: items.length === 0 ? 'Todavía no hay armados' : 'No hay armados con estos filtros',
          description: items.length === 0 ? 'Arme una PC y cotícela para el cliente: aparecerá aquí.' : 'Pruebe con otro estado, otras fechas o limpie los filtros.',
          icon: <Cpu />,
          action:
            items.length === 0 ? (
              <Button leftIcon={<Plus />} to={ROUTES.panelModule(builderPath(null))}>
                Armar una PC
              </Button>
            ) : (
              <Button variant="outline" onClick={table.clearFilters}>
                Limpiar filtros
              </Button>
            ),
        }}
      />

      <Collapsible
        label="Ver resumen de los armados"
        openLabel="Ocultar resumen de los armados"
        icon={<BarChart3 />}
        description="Cotizaciones vigentes, reservas web activas, vendidos y borradores. Se carga al abrir."
      >
        <BuildsOverview />
      </Collapsible>

      <BuildPanel
        number={detail?.number ?? null}
        open={detail?.open ?? false}
        onClose={() => setDetail((current) => (current?.open ? { ...current, open: false } : current))}
        row={detailRow}
        detail={detailQuery}
        now={now}
        canSell={allowed.sell}
      />

      <ReserveDialog target={reserving} onClose={() => setReserving(null)} onReserved={refresh} />
      <ReleaseDialog target={releasing} onClose={() => setReleasing(null)} onReleased={refresh} />
      <CancelDialog target={cancelling} onClose={() => setCancelling(null)} onCancelled={refresh} />
    </Page>
  );
}
