// Módulo «Integraciones» · pestaña «Entregas» (`GetWebhookDeliveriesQuery`): cada intento de avisar a un webhook, para
// diagnosticar una integración. El webhook y cuántas entregas revisar se piden al servidor; el resultado (correcta o
// fallida), el evento, las fechas y la búsqueda se aplican en la página. Todo en la dirección; detalle lateral y
// exportar CSV. Solo lectura: el despachador reintenta solo (hasta 8 veces).

import { Download, Eye, Filter, History, RefreshCw } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useRpcQuery, useTableState } from '@/4-presentation/panel/hooks';
import {
  Button,
  DataTable,
  DateRangeField,
  DetailList,
  FilterBar,
  SearchField,
  SelectField,
  SidePanel,
  StatusBadge,
  Toolbar,
  statusOptions,
  useNotify,
  type DataTableColumn,
} from '@/4-presentation/panel/kit';
import { exportCsv, formatDateTime, formatNumber } from '@/4-presentation/panel/lib';
import {
  DELIVERY_FILTERS,
  DELIVERY_RESULTS,
  DELIVERY_TAKE_OPTIONS,
  deliveriesQueryOf,
  deliveryCsvColumns,
  deliveryKeys,
  deliveryResultOf,
  deliveryResultText,
  filterDeliveries,
  nameOf,
  optionsOf,
  type DeliveryRecord,
  type NamedOption,
} from './integrations';

export interface DeliveriesTabProps {
  events: readonly NamedOption[];
}

export function DeliveriesTab({ events }: DeliveriesTabProps) {
  const notify = useNotify();
  const table = useTableState({ filters: DELIVERY_FILTERS, sort: { column: 'fecha', direction: 'desc' } });
  const filters = table.filters;
  const hooks = useRpcQuery('GetWebhooksQuery', {});
  const deliveries = useRpcQuery('GetWebhookDeliveriesQuery', deliveriesQueryOf(filters));
  const all = useMemo(() => deliveries.data ?? [], [deliveries.data]);
  const keys = useMemo(() => deliveryKeys(all), [all]);
  const rows = useMemo(() => filterDeliveries(all, filters, events), [all, filters, events]);
  const [detail, setDetail] = useState<{ delivery: DeliveryRecord; open: boolean } | null>(null);
  const hookOptions = (hooks.data ?? []).map((hook) => ({ value: hook.id, label: hook.description ? `${hook.description} · ${hook.url}` : hook.url }));

  const columns = useMemo<DataTableColumn<DeliveryRecord>[]>(
    () => [
      { id: 'fecha', header: 'Fecha y hora', value: (delivery) => new Date(delivery.attemptedAt), cell: (delivery) => formatDateTime(delivery.attemptedAt), className: 'whitespace-nowrap' },
      { id: 'evento', header: 'Evento', value: (delivery) => nameOf(events, delivery.eventType), card: 'title', className: 'min-w-44' },
      { id: 'direccion', header: 'Dirección', value: (delivery) => delivery.url, cell: (delivery) => <span className="block max-w-xs font-mono text-xs break-all">{delivery.url}</span> },
      { id: 'intento', header: 'Intento', align: 'end', value: (delivery) => delivery.attempt },
      {
        id: 'resultado',
        header: 'Resultado',
        value: (delivery) => deliveryResultText(delivery),
        cell: (delivery) => (
          <span className="block min-w-36">
            <StatusBadge tone={delivery.succeeded ? 'success' : 'danger'}>{deliveryResultText(delivery)}</StatusBadge>
            {delivery.error && <span className="mt-1 line-clamp-2 block text-xs text-text-muted">{delivery.error}</span>}
          </span>
        ),
      },
      { id: 'duracion', header: 'Duración', align: 'end', value: (delivery) => delivery.durationMs, cell: (delivery) => `${formatNumber(delivery.durationMs)} ms` },
    ],
    [events],
  );

  const exportRows = () => {
    const file = exportCsv('entregas-webhooks', deliveryCsvColumns(events), rows);
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(rows.length)} filas).`);
  };

  const failed = rows.filter((row) => !row.succeeded).length;
  const current = detail?.delivery ?? null;

  return (
    <div className="space-y-5">
      <FilterBar activeCount={table.activeFilterCount} onClear={table.clearFilters}>
        <SearchField label="Buscar" placeholder="Dirección, error o evento" value={filters.q} onChange={(q) => table.setFilter('q', q)} />
        <SelectField label="Resultado" allLabel="Todos" value={filters.resultado} onChange={(value) => table.setFilter('resultado', value)} options={statusOptions(DELIVERY_RESULTS)} />
        <SelectField label="Evento" allLabel="Todos los eventos" value={filters.evento} onChange={(value) => table.setFilter('evento', value)} options={optionsOf(events)} />
        <SelectField label="Webhook" allLabel="Todos los webhooks" value={filters.webhook} onChange={(value) => table.setFilter('webhook', value)} options={hookOptions} />
        <SelectField
          label="Entregas a revisar"
          allLabel={false}
          value={filters.registros}
          onChange={(value) => table.setFilter('registros', value || DELIVERY_FILTERS.registros)}
          options={DELIVERY_TAKE_OPTIONS}
          hint="Las más recientes; los demás filtros se aplican sobre ellas."
        />
        <DateRangeField label="Fechas" value={table.dateRange()} onChange={(range) => table.setDateRange(range)} />
      </FilterBar>

      <Toolbar
        end={
          <>
            <Button variant="outline" leftIcon={<RefreshCw />} loading={deliveries.fetching && !deliveries.loading} onClick={deliveries.reload}>
              Actualizar
            </Button>
            <Button variant="outline" leftIcon={<Download />} disabled={rows.length === 0} onClick={exportRows}>
              Exportar CSV
            </Button>
          </>
        }
      >
        {deliveries.data && (
          <span className="text-sm text-text-muted" data-testid="entregas-resumen">
            {formatNumber(rows.length)} de {formatNumber(all.length)} entregas · {formatNumber(failed)} fallidas
          </span>
        )}
      </Toolbar>

      <DataTable
        caption="Entregas de los webhooks"
        columns={columns}
        rows={deliveries.data ? rows : undefined}
        rowKey={(delivery) => keys.get(delivery) ?? delivery.attemptedAt}
        rowLabel={(delivery) => `la entrega de ${nameOf(events, delivery.eventType)} del ${formatDateTime(delivery.attemptedAt)}`}
        loading={deliveries.loading}
        refreshing={deliveries.fetching && !deliveries.loading}
        error={deliveries.error}
        onRetry={deliveries.reload}
        operation="GetWebhookDeliveriesQuery"
        {...table.tableProps}
        onRowOpen={(delivery) => setDetail({ delivery, open: true })}
        activeRowKey={detail?.open ? (keys.get(detail.delivery) ?? null) : null}
        rowActions={(delivery) => [{ label: 'Ver detalle', icon: <Eye />, onSelect: () => setDetail({ delivery, open: true }) }]}
        empty={{
          title: all.length === 0 ? 'Todavía no hay entregas' : 'No hay entregas con estos filtros',
          description: all.length === 0 ? 'Aparecen cuando M-INV avisa a un webhook de un evento.' : 'Pruebe con otras fechas, otro evento o revise más entregas.',
          icon: <History />,
          action:
            all.length === 0 ? undefined : (
              <Button variant="outline" leftIcon={<Filter />} onClick={table.clearFilters}>
                Limpiar filtros
              </Button>
            ),
        }}
      />

      <SidePanel
        open={detail?.open ?? false}
        onClose={() => setDetail((value) => (value?.open ? { ...value, open: false } : value))}
        title={current ? nameOf(events, current.eventType) : 'Entrega'}
        description={current ? formatDateTime(current.attemptedAt) : undefined}
        headerExtra={current && <StatusBadge status={deliveryResultOf(current)} statuses={DELIVERY_RESULTS} />}
      >
        {current && (
          <DetailList
            items={[
              { label: 'Dirección', value: <span className="font-mono text-xs break-all">{current.url}</span>, wide: true },
              { label: 'Evento', value: nameOf(events, current.eventType) },
              { label: 'Código del evento', value: current.eventType },
              { label: 'Intento', value: `${formatNumber(current.attempt)} de 8` },
              { label: 'Resultado', value: deliveryResultText(current) },
              { label: 'Duración', value: `${formatNumber(current.durationMs)} ms` },
              { label: 'Error', value: current.error, wide: true },
            ]}
          />
        )}
      </SidePanel>
    </div>
  );
}
