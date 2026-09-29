// Administración › Actividad (módulo de EJEMPLO del panel: el modelo a copiar). Quién hizo qué, cuándo y con qué
// resultado: la auditoría del servidor (`GetActivityQuery`), con filtros en la dirección (búsqueda, usuario, resultado,
// fechas y cuántos registros traer), tabla ordenable y paginada, detalle lateral, exportar CSV y un comando
// (`ResetUserPasswordCommand`: contraseña temporal y desbloqueo, solo para quien administra usuarios).
//
// Patrón de toda lista del panel:
//   1. `useTableState`  → filtros, orden y página en la dirección (se puede recargar o compartir el enlace).
//   2. `useRpcQuery`    → el pedido al servidor con lo que filtra el servidor (aquí, `take`); se repite solo si cambia.
//   3. `useMemo`        → lo que se filtra en la página (usuario, resultado, fechas y búsqueda).
//   4. `<Page>` → `<FilterBar>` → `<Toolbar>` → `<DataTable>` → `<SidePanel>` (y los diálogos de los comandos).

import { Download, Eye, Filter, History, KeyRound, RefreshCw } from 'lucide-react';
import { useId, useMemo, useState } from 'react';
import { usePermissions, useRpcQuery, useTableState } from '@/4-presentation/panel/hooks';
import {
  Button,
  DataTable,
  DateRangeField,
  DetailList,
  FilterBar,
  Page,
  SearchField,
  SelectField,
  SidePanel,
  StatusBadge,
  Toolbar,
  statusOptions,
  useNotify,
  type DataTableColumn,
} from '@/4-presentation/panel/kit';
import { exportCsv, formatDateLong, formatDateTime, formatNumber, formatTime } from '@/4-presentation/panel/lib';
import {
  ACTIVITY_FILTERS,
  CSV_COLUMNS,
  OUTCOMES,
  SYSTEM_USER,
  TAKE_OPTIONS,
  detailFields,
  filterActivity,
  outcomeLabel,
  takeOf,
  toActivityItems,
  userOptions,
  type ActivityItem,
} from './activity';
import { ResetPasswordDialog, type ResetTarget } from './ResetPasswordDialog';

/** Columnas de la tabla: fuera del componente (así no se vuelven a crear ni a ordenar en cada dibujo). */
const COLUMNS: DataTableColumn<ActivityItem>[] = [
  {
    id: 'fecha',
    header: 'Fecha y hora',
    value: (item) => new Date(item.record.occurredAt),
    cell: (item) => formatDateTime(item.record.occurredAt),
    className: 'whitespace-nowrap',
  },
  {
    id: 'usuario',
    header: 'Usuario',
    value: (item) => item.who,
    cell: (item) => (
      <span className="block min-w-36">
        <span className="block">{item.who}</span>
        {item.email && item.email !== item.who && <span className="block text-xs text-text-muted">{item.email}</span>}
      </span>
    ),
  },
  { id: 'accion', header: 'Acción', value: (item) => item.actionText, card: 'title', className: 'min-w-48' },
  {
    id: 'resultado',
    header: 'Resultado',
    value: (item) => outcomeLabel(item.record.outcome),
    cell: (item) => <StatusBadge status={item.record.outcome} statuses={OUTCOMES} />,
  },
  {
    id: 'detalle',
    header: 'Detalle',
    value: (item) => item.summary,
    sortable: false,
    cell: (item) => <span className="line-clamp-2 block max-w-md min-w-48 text-text-muted">{item.summary || '—'}</span>,
  },
];

export function ActivityPage() {
  const notify = useNotify();
  const { canRun } = usePermissions();
  const table = useTableState({ filters: ACTIVITY_FILTERS, sort: { column: 'fecha', direction: 'desc' } });
  const filters = table.filters;
  const take = takeOf(filters.registros);

  // Lo que filtra el SERVIDOR va en el pedido (cambiarlo vuelve a consultar); el resto se filtra en la página.
  const activity = useRpcQuery('GetActivityQuery', { take });
  const items = useMemo(() => toActivityItems(activity.data ?? []), [activity.data]);
  const rows = useMemo(() => filterActivity(items, filters), [items, filters]);
  const users = useMemo(() => userOptions(items), [items]);

  // Detalle abierto (se guarda la fila y si está abierto: al cerrar, el panel se desliza con su contenido).
  const [detail, setDetail] = useState<{ item: ActivityItem; open: boolean } | null>(null);
  const [resetting, setResetting] = useState<ResetTarget | null>(null);
  const canReset = canRun('ResetUserPasswordCommand');
  const recordedTitleId = useId();

  const openDetail = (item: ActivityItem) => setDetail({ item, open: true });
  const onlyThisUser = (item: ActivityItem) => {
    table.setFilter('usuario', item.userValue);
    setDetail((current) => (current ? { ...current, open: false } : current));
  };
  const askReset = (item: ActivityItem) => {
    if (item.email) setResetting({ email: item.email, name: item.who });
  };

  const exportRows = () => {
    const file = exportCsv('actividad', CSV_COLUMNS, rows);
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(rows.length)} filas).`);
  };

  const rejected = rows.filter((row) => row.record.outcome !== 'Succeeded').length;
  const item = detail?.item;
  const recorded = item ? detailFields(item.record.details) : [];

  return (
    <Page title="Actividad" description="Quién hizo qué, cuándo y con qué resultado. Cada operación del sistema queda registrada y no se puede borrar.">
      <FilterBar activeCount={table.activeFilterCount} onClear={table.clearFilters}>
        <SearchField label="Buscar" placeholder="Acción, usuario o detalle" value={filters.q} onChange={(q) => table.setFilter('q', q)} />
        <SelectField label="Usuario" value={filters.usuario} onChange={(value) => table.setFilter('usuario', value)} options={users} />
        <SelectField label="Resultado" value={filters.resultado} onChange={(value) => table.setFilter('resultado', value)} options={statusOptions(OUTCOMES)} />
        <SelectField
          label="Registros a revisar"
          allLabel={false}
          value={filters.registros}
          onChange={(value) => table.setFilter('registros', value || ACTIVITY_FILTERS.registros)}
          options={TAKE_OPTIONS}
          hint="Los más recientes; los filtros se aplican sobre ellos."
        />
        <DateRangeField label="Fechas" value={table.dateRange()} onChange={(range) => table.setDateRange(range)} />
      </FilterBar>

      <Toolbar
        end={
          <>
            <Button variant="outline" leftIcon={<RefreshCw />} loading={activity.fetching && !activity.loading} onClick={activity.reload}>
              Actualizar
            </Button>
            <Button variant="outline" leftIcon={<Download />} disabled={rows.length === 0} onClick={exportRows}>
              Exportar CSV
            </Button>
          </>
        }
      >
        {activity.data && (
          <span className="text-sm text-text-muted" data-testid="actividad-resumen">
            {formatNumber(rows.length)} de {formatNumber(items.length)} registros · {formatNumber(rejected)} rechazados o con error
          </span>
        )}
      </Toolbar>

      <DataTable
        caption="Actividad del sistema"
        columns={COLUMNS}
        rows={activity.data ? rows : undefined}
        rowKey={(row) => row.key}
        rowLabel={(row) => `la operación «${row.actionText}» de ${row.who}`}
        loading={activity.loading}
        refreshing={activity.fetching && !activity.loading}
        error={activity.error}
        onRetry={activity.reload}
        operation="GetActivityQuery"
        {...table.tableProps}
        onRowOpen={openDetail}
        activeRowKey={detail?.open ? detail.item.key : null}
        rowActions={(row) => [
          { label: 'Ver detalle', icon: <Eye />, onSelect: () => openDetail(row) },
          { label: 'Ver solo este usuario', icon: <Filter />, onSelect: () => onlyThisUser(row), hidden: filters.usuario === row.userValue },
          {
            label: 'Asignar contraseña temporal',
            icon: <KeyRound />,
            onSelect: () => askReset(row),
            hidden: !canReset || row.userValue === SYSTEM_USER,
          },
        ]}
        empty={{
          title: items.length === 0 ? 'Todavía no hay actividad registrada' : 'No hay actividad con estos filtros',
          description: items.length === 0 ? undefined : 'Pruebe con otras fechas, revise más registros o limpie los filtros.',
          icon: <History />,
          action:
            items.length === 0 ? undefined : (
              <Button variant="outline" onClick={table.clearFilters}>
                Limpiar filtros
              </Button>
            ),
        }}
      />

      <SidePanel
        open={detail?.open ?? false}
        onClose={() => setDetail((current) => (current?.open ? { ...current, open: false } : current))}
        title={item ? item.actionText : 'Operación'}
        description={item ? formatDateTime(item.record.occurredAt) : undefined}
        headerExtra={item && <StatusBadge status={item.record.outcome} statuses={OUTCOMES} />}
        footer={
          item && (
            <div className="flex flex-col gap-2">
              {item.userValue !== filters.usuario && (
                <Button variant="outline" leftIcon={<Filter />} fullWidth onClick={() => onlyThisUser(item)}>
                  Ver solo la actividad de {item.who}
                </Button>
              )}
              {canReset && item.email && (
                <Button leftIcon={<KeyRound />} fullWidth onClick={() => askReset(item)}>
                  Asignar contraseña temporal
                </Button>
              )}
            </div>
          )
        }
      >
        {item && (
          <div className="space-y-5">
            <DetailList
              items={[
                { label: 'Fecha', value: formatDateLong(item.record.occurredAt) },
                { label: 'Hora', value: formatTime(item.record.occurredAt) },
                { label: 'Usuario', value: item.who },
                { label: 'Correo', value: item.email },
                { label: 'Resultado', value: <StatusBadge status={item.record.outcome} statuses={OUTCOMES} /> },
                { label: 'Código de la operación', value: item.record.action },
                { label: 'Detalle', value: item.summary, wide: true },
              ]}
            />
            {recorded.length > 0 && (
              <section aria-labelledby={recordedTitleId}>
                <h3 id={recordedTitleId} className="text-sm font-semibold text-text">
                  Datos registrados
                </h3>
                <DetailList className="mt-2" items={recorded} />
              </section>
            )}
          </div>
        )}
      </SidePanel>

      <ResetPasswordDialog target={resetting} onClose={() => setResetting(null)} />
    </Page>
  );
}
