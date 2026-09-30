// Módulo «Estado del SIAT» · pestaña «Eventos significativos» (`GetSignificantEventsQuery`): fuera de línea y
// contingencias manuales de los últimos 45 días (las fechas las filtra el servidor; estado, tipo y sucursal, la página),
// con su período, estado, documentos, plazo y código de recepción del SIN. Por fila: ver sus paquetes y transcribir las
// facturas manuales de una contingencia CAFC. Filtros en la dirección con el prefijo `e_`.

import { Download, FileText, Flag, Package, RefreshCw } from 'lucide-react';
import { useMemo, useRef, useState } from 'react';
import { useRpcQuery, useTableState } from '@/4-presentation/panel/hooks';
import { Button, DataTable, DateRangeField, FilterBar, SelectField, StatusBadge, Toolbar, statusOptions, useNotify, type DataTableColumn } from '@/4-presentation/panel/kit';
import { exportCsv, formatNumber, laPazToday, rangeError } from '@/4-presentation/panel/lib';
import {
  EVENT_CSV,
  EVENT_KINDS,
  EVENT_STATUSES,
  branchOptions,
  eventDeadline,
  eventFilters,
  eventPeriod,
  filterEvents,
  formatFiscalTime,
  isTranscribable,
  placeText,
  sentenceCase,
  type EventData,
} from './siat';

const COLUMNS: DataTableColumn<EventData>[] = [
  {
    id: 'evento',
    header: 'Evento',
    value: (row) => `${row.eventCode} · ${sentenceCase(row.description)}`,
    card: 'title',
    cell: (row) => (
      <span className="block min-w-48">
        <span className="block">
          {row.eventCode} · {sentenceCase(row.description)}
        </span>
        <span className="block text-xs font-normal text-text-muted">{row.cafc ? `CAFC ${row.cafc}` : 'Sin CAFC'}</span>
      </span>
    ),
  },
  { id: 'tipo', header: 'Tipo', value: (row) => row.kind, cell: (row) => <StatusBadge status={row.kind} statuses={EVENT_KINDS} /> },
  { id: 'lugar', header: 'Lugar', value: (row) => placeText(row), className: 'whitespace-nowrap' },
  { id: 'inicio', header: 'Período', value: (row) => row.startedAt, cell: (row) => <span className="block min-w-40">{eventPeriod(row)}</span> },
  { id: 'estado', header: 'Estado', value: (row) => row.status, cell: (row) => <StatusBadge status={row.status} statuses={EVENT_STATUSES} /> },
  { id: 'documentos', header: 'Documentos', align: 'end', value: (row) => row.documents },
  { id: 'plazo', header: 'Plazo', value: (row) => eventDeadline(row), sortable: false },
  { id: 'recepcion', header: 'Recepción del SIN', value: (row) => row.receptionCode },
];

export interface EventsTabProps {
  canTranscribe: boolean;
  onShowPackages: (event: EventData) => void;
  onTranscribe: (event: EventData) => void;
}

export function EventsTab({ canTranscribe, onShowPackages, onTranscribe }: EventsTabProps) {
  const notify = useNotify();
  const [today] = useState(() => laPazToday());
  const table = useTableState({ prefix: 'e_', filters: eventFilters(today), sort: { column: 'inicio', direction: 'desc' } });
  const filters = table.filters;
  const range = table.dateRange();
  const from = range.from ?? '2000-01-01';
  const to = range.to ?? (from > today ? from : today);
  const valid = rangeError(range) === null;
  const events = useRpcQuery('GetSignificantEventsQuery', { from, to }, { enabled: valid });
  const rows = useMemo(() => filterEvents(events.data ?? [], filters), [events.data, filters]);
  const branches = useMemo(() => branchOptions(events.data ?? []), [events.data]);

  // Guarda del menú «⋯»: el clic de una opción (en un portal) no debe llegar a la fila.
  const pickingAction = useRef(false);
  const fromMenu = (action: () => void) => () => {
    pickingAction.current = true;
    action();
    setTimeout(() => {
      pickingAction.current = false;
    }, 0);
  };

  const exportRows = () => {
    const file = exportCsv('eventos-significativos', EVENT_CSV, rows);
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(rows.length)} filas).`);
  };

  return (
    <div className="space-y-4">
      <FilterBar activeCount={table.activeFilterCount} onClear={table.clearFilters} title="Filtros de los eventos">
        <SelectField label="Estado del evento" allLabel="Todos los estados" value={filters.estado} onChange={(value) => table.setFilter('estado', value)} options={statusOptions(EVENT_STATUSES)} />
        <SelectField label="Tipo de evento" allLabel="Todos los tipos" value={filters.tipo} onChange={(value) => table.setFilter('tipo', value)} options={statusOptions(EVENT_KINDS)} />
        <SelectField label="Sucursal del evento" allLabel="Todas las sucursales" value={filters.sucursal} onChange={(value) => table.setFilter('sucursal', value)} options={branches} />
        <DateRangeField label="Fechas de inicio" value={range} onChange={(value) => table.setDateRange(value)} hint="Por defecto, los últimos 45 días." />
      </FilterBar>
      <Toolbar
        label="Acciones de los eventos"
        end={
          <>
            <Button variant="outline" leftIcon={<RefreshCw />} loading={events.fetching && !events.loading} onClick={events.reload}>
              Actualizar
            </Button>
            <Button variant="outline" leftIcon={<Download />} disabled={rows.length === 0} onClick={exportRows}>
              Exportar CSV
            </Button>
          </>
        }
      >
        {events.data && (
          <span className="text-sm text-text-muted" data-testid="eventos-resumen">
            {formatNumber(rows.length)} de {formatNumber(events.data.length)} eventos
          </span>
        )}
      </Toolbar>
      <DataTable
        caption="Eventos significativos"
        columns={COLUMNS}
        rows={events.data ? rows : undefined}
        rowKey={(row) => row.id}
        rowLabel={(row) => `el evento ${row.eventCode} de ${placeText(row)} del ${formatFiscalTime(row.startedAt)}`}
        loading={events.loading}
        refreshing={events.fetching && !events.loading}
        error={events.error}
        onRetry={events.reload}
        operation="GetSignificantEventsQuery"
        {...table.tableProps}
        onRowOpen={(row) => {
          if (!pickingAction.current) onShowPackages(row);
        }}
        rowActions={(row) => [
          { label: 'Ver sus paquetes', icon: <Package />, onSelect: fromMenu(() => onShowPackages(row)) },
          { label: 'Transcribir factura manual', icon: <FileText />, onSelect: fromMenu(() => onTranscribe(row)), hidden: !canTranscribe || !isTranscribable(row) },
        ]}
        empty={{
          title: (events.data ?? []).length === 0 ? 'Sin eventos significativos en estas fechas' : 'No hay eventos con estos filtros',
          description: 'Los eventos se abren solos al perder la conexión o al declarar una contingencia manual.',
          icon: <Flag />,
          action: (
            <Button variant="outline" onClick={table.clearFilters}>
              Limpiar filtros
            </Button>
          ),
        }}
      />
    </div>
  );
}
