// Módulo «Integraciones» · pestaña «Webhooks» (`GetWebhooksQuery`): a qué direcciones avisa M-INV, de qué eventos, con
// cuántas entregas correctas y fallidas y el último error. Filtros por estado, evento y sucursal (en la dirección),
// detalle lateral, exportar CSV, «Ver sus entregas» (pestaña «Entregas» filtrada), «Rotar el secreto»
// (`RotateWebhookSecretCommand`: el secreto nuevo se muestra UNA vez y durante 24 horas cada aviso lleva las dos firmas)
// y «Desactivar» con confirmación (`DisableWebhookCommand`: deja de recibir avisos; el historial se conserva).

import { Download, Eye, Filter, History, Plus, PowerOff, RefreshCw, RotateCcw, Webhook } from 'lucide-react';
import { useMemo, useState } from 'react';
import { usePermissions, useRpcCommand, useRpcQuery, useTableState } from '@/4-presentation/panel/hooks';
import {
  Button,
  ConfirmDialog,
  DataTable,
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
  WEBHOOK_FILTERS,
  WEBHOOK_STATUSES,
  branchOptions,
  deliveriesText,
  filterWebhooks,
  nameOf,
  optionsOf,
  plainMessage,
  webhookCsvColumns,
  webhookStatusOf,
  type CreatedHook,
  type NamedOption,
  type WebhookRecord,
} from './integrations';

export interface WebhooksTabProps {
  events: readonly NamedOption[];
  canCreate: boolean;
  onCreate: () => void;
  /** Abre la pestaña «Entregas» filtrada por ese webhook. */
  onShowDeliveries: (webhookId: string) => void;
  /** El secreto nuevo después de rotarlo (la pantalla lo muestra una vez). */
  onRotated: (rotated: CreatedHook) => void;
}

type Pending = { hook: WebhookRecord; kind: 'rotar' | 'desactivar'; open: boolean };

export function WebhooksTab({ events, canCreate, onCreate, onShowDeliveries, onRotated }: WebhooksTabProps) {
  const notify = useNotify();
  const { canRun } = usePermissions();
  const canRotate = canRun('RotateWebhookSecretCommand');
  const canDisable = canRun('DisableWebhookCommand');
  const canDeliveries = canRun('GetWebhookDeliveriesQuery');
  const hooks = useRpcQuery('GetWebhooksQuery', {});
  const table = useTableState({ filters: WEBHOOK_FILTERS, sort: { column: 'direccion', direction: 'asc' } });
  const filters = table.filters;
  const all = useMemo(() => hooks.data ?? [], [hooks.data]);
  const rows = useMemo(() => filterWebhooks(all, filters), [all, filters]);
  const [detail, setDetail] = useState<{ hook: WebhookRecord; open: boolean } | null>(null);
  const [pending, setPending] = useState<Pending | null>(null);
  const rotate = useRpcCommand('RotateWebhookSecretCommand', { notifyError: false, success: 'Secreto rotado' });
  const disable = useRpcCommand('DisableWebhookCommand', { notifyError: false, success: (message) => plainMessage(message) || 'Webhook desactivado' });

  const columns = useMemo<DataTableColumn<WebhookRecord>[]>(
    () => [
      {
        id: 'direccion',
        header: 'Dirección',
        value: (hook) => hook.url,
        card: 'title',
        cell: (hook) => (
          <span className="block max-w-sm min-w-48">
            <span className="block font-mono text-xs break-all">{hook.url}</span>
            {hook.description && <span className="block text-xs font-normal text-text-muted">{hook.description}</span>}
          </span>
        ),
      },
      {
        id: 'eventos',
        header: 'Eventos',
        value: (hook) => hook.events.length,
        cell: (hook) => (
          <span className="line-clamp-2 block max-w-xs min-w-40 text-text-muted" title={hook.events.map((event) => nameOf(events, event)).join(' · ')}>
            {hook.events.length === 1 ? nameOf(events, hook.events[0]) : `${formatNumber(hook.events.length)} eventos`}
          </span>
        ),
      },
      { id: 'sucursal', header: 'Sucursal', value: (hook) => hook.branch ?? 'Todas' },
      { id: 'entregas', header: 'Entregas', value: (hook) => hook.failed, cell: (hook) => <span className="whitespace-nowrap">{deliveriesText(hook)}</span> },
      {
        id: 'intento',
        header: 'Último intento',
        value: (hook) => (hook.lastAttemptAt ? new Date(hook.lastAttemptAt) : null),
        cell: (hook) => (hook.lastAttemptAt ? formatDateTime(hook.lastAttemptAt) : 'Sin entregas todavía'),
        className: 'whitespace-nowrap',
      },
      {
        id: 'estado',
        header: 'Estado',
        value: (hook) => WEBHOOK_STATUSES[webhookStatusOf(hook)].label,
        cell: (hook) => <StatusBadge status={webhookStatusOf(hook)} statuses={WEBHOOK_STATUSES} />,
      },
    ],
    [events],
  );

  const current = detail ? (all.find((hook) => hook.id === detail.hook.id) ?? detail.hook) : null;
  const target = pending?.hook ?? null;
  const ask = (hook: WebhookRecord, kind: Pending['kind']) => {
    rotate.reset();
    disable.reset();
    setDetail((value) => (value?.open ? { ...value, open: false } : value));
    setPending({ hook, kind, open: true });
  };

  const exportRows = () => {
    const file = exportCsv('webhooks', webhookCsvColumns(events), rows);
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(rows.length)} filas).`);
  };

  const isRotation = pending?.kind === 'rotar';

  return (
    <div className="space-y-5">
      <FilterBar activeCount={table.activeFilterCount} onClear={table.clearFilters}>
        <SearchField label="Buscar" placeholder="Dirección, descripción o error" value={filters.q} onChange={(q) => table.setFilter('q', q)} />
        <SelectField label="Estado" allLabel="Todos los estados" value={filters.estado} onChange={(value) => table.setFilter('estado', value)} options={statusOptions(WEBHOOK_STATUSES)} />
        <SelectField label="Evento" allLabel="Todos los eventos" value={filters.evento} onChange={(value) => table.setFilter('evento', value)} options={optionsOf(events)} />
        <SelectField
          label="Sucursal"
          allLabel="Todas"
          value={filters.sucursal}
          onChange={(value) => table.setFilter('sucursal', value)}
          options={branchOptions(all.map((hook) => hook.branch), 'Toda la empresa (sin sucursal)')}
        />
      </FilterBar>

      <Toolbar
        end={
          <>
            <Button variant="outline" leftIcon={<RefreshCw />} loading={hooks.fetching && !hooks.loading} onClick={hooks.reload}>
              Actualizar
            </Button>
            <Button variant="outline" leftIcon={<Download />} disabled={rows.length === 0} onClick={exportRows}>
              Exportar CSV
            </Button>
          </>
        }
      >
        {hooks.data && (
          <span className="text-sm text-text-muted" data-testid="webhooks-resumen">
            {formatNumber(rows.length)} de {formatNumber(all.length)} webhooks · {formatNumber(all.filter((hook) => hook.isActive).length)} activos
          </span>
        )}
      </Toolbar>

      <DataTable
        caption="Webhooks"
        columns={columns}
        rows={hooks.data ? rows : undefined}
        rowKey={(hook) => hook.id}
        rowLabel={(hook) => `el webhook ${hook.url}`}
        loading={hooks.loading}
        refreshing={hooks.fetching && !hooks.loading}
        error={hooks.error}
        onRetry={hooks.reload}
        operation="GetWebhooksQuery"
        {...table.tableProps}
        onRowOpen={(hook) => setDetail({ hook, open: true })}
        activeRowKey={detail?.open ? detail.hook.id : null}
        rowActions={(hook) => [
          { label: 'Ver detalle', icon: <Eye />, onSelect: () => setDetail({ hook, open: true }) },
          { label: 'Ver sus entregas', icon: <History />, onSelect: () => onShowDeliveries(hook.id), hidden: !canDeliveries },
          { label: 'Rotar el secreto', icon: <RotateCcw />, onSelect: () => ask(hook, 'rotar'), hidden: !canRotate || !hook.isActive },
          { label: 'Desactivar', icon: <PowerOff />, tone: 'danger', onSelect: () => ask(hook, 'desactivar'), hidden: !canDisable || !hook.isActive },
        ]}
        empty={{
          title: all.length === 0 ? 'Todavía no hay webhooks' : 'No hay webhooks con estos filtros',
          description: all.length === 0 ? 'Registre uno para que su sistema reciba avisos de ventas, compras o reservas.' : 'Pruebe con otro estado, evento o sucursal.',
          icon: <Webhook />,
          action:
            all.length === 0 ? (
              canCreate && (
                <Button leftIcon={<Plus />} onClick={onCreate}>
                  Nuevo webhook
                </Button>
              )
            ) : (
              <Button variant="outline" leftIcon={<Filter />} onClick={table.clearFilters}>
                Limpiar filtros
              </Button>
            ),
        }}
      />

      <SidePanel
        open={detail?.open ?? false}
        onClose={() => setDetail((value) => (value?.open ? { ...value, open: false } : value))}
        title={current?.description || 'Webhook'}
        description={current?.url}
        headerExtra={current && <StatusBadge status={webhookStatusOf(current)} statuses={WEBHOOK_STATUSES} />}
        footer={
          current && (
            <div className="flex flex-col gap-2">
              {canDeliveries && (
                <Button variant="outline" leftIcon={<History />} fullWidth onClick={() => onShowDeliveries(current.id)}>
                  Ver sus entregas
                </Button>
              )}
              {canRotate && current.isActive && (
                <Button variant="outline" leftIcon={<RotateCcw />} fullWidth onClick={() => ask(current, 'rotar')}>
                  Rotar el secreto
                </Button>
              )}
              {canDisable && current.isActive && (
                <Button variant="danger" leftIcon={<PowerOff />} fullWidth onClick={() => ask(current, 'desactivar')}>
                  Desactivar
                </Button>
              )}
            </div>
          )
        }
      >
        {current && (
          <DetailList
            items={[
              { label: 'Dirección', value: <span className="font-mono text-xs break-all">{current.url}</span>, wide: true },
              { label: 'Estado', value: WEBHOOK_STATUSES[webhookStatusOf(current)].label },
              { label: 'Sucursal', value: current.branch ?? 'Toda la empresa' },
              { label: 'Registrado', value: formatDateTime(current.createdAt) },
              { label: 'Versión del secreto', value: formatNumber(current.secretVersion) },
              { label: 'Entregas correctas', value: formatNumber(current.delivered) },
              { label: 'Entregas fallidas', value: formatNumber(current.failed) },
              { label: 'Último intento', value: current.lastAttemptAt ? formatDateTime(current.lastAttemptAt) : 'Sin entregas todavía' },
              { label: 'Último error', value: current.lastError, wide: true },
              {
                label: 'Eventos',
                wide: true,
                value: (
                  <ul className="list-disc space-y-1 pl-5">
                    {current.events.map((event) => (
                      <li key={event}>{nameOf(events, event)}</li>
                    ))}
                  </ul>
                ),
              },
            ]}
          />
        )}
      </SidePanel>

      <ConfirmDialog
        open={pending?.open ?? false}
        onClose={() => setPending((value) => (value ? { ...value, open: false } : value))}
        tone={isRotation ? 'primary' : 'danger'}
        title={isRotation ? '¿Rotar el secreto del webhook?' : '¿Desactivar el webhook?'}
        message={
          target
            ? isRotation
              ? `Se genera un secreto nuevo para ${target.url}. Durante 24 horas cada aviso lleva las dos firmas (la anterior y la nueva) para que su sistema cambie sin cortes.`
              : `${target.url} deja de recibir avisos. Su historial de entregas se conserva.`
            : ''
        }
        confirmLabel={isRotation ? 'Rotar el secreto' : 'Desactivar'}
        onConfirm={async () => {
          if (!target) return false;
          if (isRotation) {
            const outcome = await rotate.run({ id: target.id });
            if (outcome.ok) {
              hooks.reload();
              onRotated(outcome.result);
            }
            return outcome;
          }
          const outcome = await disable.run({ id: target.id });
          if (outcome.ok) hooks.reload();
          return outcome;
        }}
        error={isRotation ? rotate.errorText : disable.errorText}
      />
    </div>
  );
}
