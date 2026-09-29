// Módulo «Configuración» · puntos de venta del SIN (`GetSiatStatusQuery`), con las acciones de configuración de «Estado
// SIAT» del escritorio: registrar un punto, solicitar CUIS y CUFD, probar la comunicación, vincular una caja y cerrar un
// punto en el SIN (DEFINITIVO: doble confirmación en un solo diálogo). Filtros en la dirección (prefijo `pv_`; por
// defecto, los abiertos), detalle lateral y exportar CSV. Las contingencias (fuera de línea, CAFC, recuperación) son de
// Facturación › Estado del SIAT.

import { Ban, Download, Eye, Filter, KeyRound, Link2, Plus, RefreshCw, Signal, Store, Ticket } from 'lucide-react';
import { useMemo, useState } from 'react';
import { usePermissions, useRpcCommand, useRpcQuery, useTableState } from '@/4-presentation/panel/hooks';
import {
  Button,
  Checkbox,
  ConfirmDialog,
  DataTable,
  DetailList,
  FilterBar,
  SearchField,
  SelectField,
  SidePanel,
  StatusBadge,
  Toolbar,
  useNotify,
  type DataTableColumn,
} from '@/4-presentation/panel/kit';
import { exportCsv, formatDateTime, formatNumber } from '@/4-presentation/panel/lib';
import { LinkRegisterDialog, RegisterPointDialog } from './PointDialogs';
import {
  POINT_CSV_COLUMNS,
  POINT_FILTERS,
  POINT_MODE_OPTIONS,
  POINT_OPEN_OPTIONS,
  POINT_STATES,
  filterPoints,
  mappedBranches,
  plainMessage,
  pointName,
  pointStateLabel,
  pointStateOf,
  validityText,
  type PointRecord,
  type SiatSettingsData,
} from './settings';

export interface PointsOfSaleProps {
  view: SiatSettingsData;
  /** Sin la licencia de facturación: se ve, pero no se cambia. */
  locked: boolean;
}

export function PointsOfSale({ view, locked }: PointsOfSaleProps) {
  const notify = useNotify();
  const { canRun, session } = usePermissions();
  const canCuis = canRun('RequestCuisCommand');
  const canCufd = canRun('RequestCufdCommand');
  const canCheck = canRun('CheckSiatCommunicationCommand');
  const canLink = canRun('LinkPointOfSaleRegisterCommand');
  const canClose = canRun('CloseSiatPointOfSaleCommand');
  const canRegister = canRun('RegisterSiatPointOfSaleCommand');
  const canRegisters = canRun('GetPosStateQuery');

  const status = useRpcQuery('GetSiatStatusQuery', {});
  const pos = useRpcQuery('GetPosStateQuery', {}, { enabled: canRegisters });
  const registers = canRegisters ? (pos.data?.registers ?? null) : null;
  const table = useTableState({ prefix: 'pv_', filters: POINT_FILTERS, sort: { column: 'sucursal', direction: 'asc' } });
  const filters = table.filters;
  const points = useMemo(() => status.data?.points ?? [], [status.data]);
  const rows = useMemo(() => filterPoints(points, filters), [points, filters]);
  const [now] = useState(() => new Date());

  const [detail, setDetail] = useState<{ point: PointRecord; open: boolean } | null>(null);
  const [registering, setRegistering] = useState(false);
  const [linking, setLinking] = useState<PointRecord | null>(null);
  const [closing, setClosing] = useState<{ point: PointRecord; open: boolean } | null>(null);
  const [understood, setUnderstood] = useState(false);

  const done = (title: string) => (message: string) => plainMessage(message) || title;
  const cuis = useRpcCommand('RequestCuisCommand', { success: done('CUIS solicitado'), errorTitle: 'No se pudo solicitar el CUIS', onSuccess: () => status.reload() });
  const cufd = useRpcCommand('RequestCufdCommand', { success: done('CUFD solicitado'), errorTitle: 'No se pudo solicitar el CUFD', onSuccess: () => status.reload() });
  const check = useRpcCommand('CheckSiatCommunicationCommand', { success: done('Comunicación exitosa'), errorTitle: 'Sin comunicación con el SIN', onSuccess: () => status.reload() });
  const close = useRpcCommand('CloseSiatPointOfSaleCommand', { notifyError: false, success: done('Punto de venta cerrado') });
  const busy = cuis.sending || cufd.sending || check.sending;

  const columns = useMemo<DataTableColumn<PointRecord>[]>(
    () => [
      {
        id: 'sucursal',
        header: 'Sucursal',
        value: (point) => `${point.branchCode} ${String(point.code).padStart(4, '0')}`,
        cell: (point) => (
          <span className="block min-w-32">
            <span className="block">{point.branchCode}</span>
            <span className="block text-xs text-text-muted">Padrón {point.siatBranchCode}</span>
          </span>
        ),
      },
      { id: 'punto', header: 'Punto de venta', value: (point) => pointName(point), card: 'title', className: 'min-w-40' },
      { id: 'caja', header: 'Caja', value: (point) => point.registerCode },
      { id: 'estado', header: 'Estado', value: (point) => pointStateLabel(point), cell: (point) => <StatusBadge status={pointStateOf(point)} statuses={POINT_STATES} /> },
      { id: 'cufd', header: 'CUFD', value: (point) => (point.cufdValidUntil ? new Date(point.cufdValidUntil) : null), cell: (point) => validityText(point.cufdValidUntil, now), className: 'whitespace-nowrap' },
      { id: 'pendientes', header: 'Pendientes', align: 'end', value: (point) => point.pendingDocuments + point.offlineDocuments },
    ],
    [now],
  );

  const current = detail ? (points.find((point) => point.id === detail.point.id) ?? detail.point) : null;
  const closeTarget = closing?.point ?? null;
  const closeDetail = () => setDetail((value) => (value?.open ? { ...value, open: false } : value));
  const askClose = (point: PointRecord) => {
    close.reset();
    setUnderstood(false);
    closeDetail();
    setClosing({ point, open: true });
  };
  const askLink = (point: PointRecord) => {
    closeDetail();
    setLinking(point);
  };

  const exportRows = () => {
    const file = exportCsv('puntos-de-venta-siat', POINT_CSV_COLUMNS, rows);
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(rows.length)} filas).`);
  };

  const branchCodes = [...new Set(points.map((point) => point.branchCode))].sort((a, b) => a.localeCompare(b, 'es'));
  const open = points.filter((point) => !point.isClosed);
  const online = open.filter((point) => point.mode === 'Online').length;

  return (
    <div className="space-y-4">
      <FilterBar activeCount={table.activeFilterCount} onClear={table.clearFilters}>
        <SearchField label="Buscar punto" placeholder="Nombre, caja o error" value={filters.q} onChange={(q) => table.setFilter('q', q)} />
        <SelectField label="Sucursal" allLabel="Todas las sucursales" value={filters.sucursal} onChange={(value) => table.setFilter('sucursal', value)} options={branchCodes.map((code) => ({ value: code, label: code }))} />
        <SelectField label="Modo" allLabel="Todos los modos" value={filters.modo} onChange={(value) => table.setFilter('modo', value)} options={POINT_MODE_OPTIONS} />
        <SelectField label="Abiertos o cerrados" allLabel="Todos" value={filters.estado} onChange={(value) => table.setFilter('estado', value)} options={POINT_OPEN_OPTIONS} />
      </FilterBar>

      <Toolbar
        label="Acciones de los puntos de venta"
        end={
          <>
            {canRegister && (
              <Button leftIcon={<Plus />} disabled={locked} onClick={() => setRegistering(true)}>
                Registrar punto de venta
              </Button>
            )}
            <Button variant="outline" leftIcon={<RefreshCw />} loading={status.fetching && !status.loading} onClick={status.reload}>
              Actualizar
            </Button>
            <Button variant="outline" leftIcon={<Download />} disabled={rows.length === 0} onClick={exportRows}>
              Exportar CSV
            </Button>
          </>
        }
      >
        {status.data && (
          <span className="text-sm text-text-muted" data-testid="puntos-resumen">
            {formatNumber(rows.length)} de {formatNumber(points.length)} puntos · {formatNumber(online)} de {formatNumber(open.length)} abiertos en línea
          </span>
        )}
      </Toolbar>

      <DataTable
        caption="Puntos de venta del SIN"
        columns={columns}
        rows={status.data ? rows : undefined}
        rowKey={(point) => point.id}
        rowLabel={(point) => `${point.branchCode} · ${pointName(point)}`}
        loading={status.loading}
        refreshing={(status.fetching && !status.loading) || busy}
        error={status.error}
        onRetry={status.reload}
        operation="GetSiatStatusQuery"
        {...table.tableProps}
        onRowOpen={(point) => setDetail({ point, open: true })}
        activeRowKey={detail?.open ? detail.point.id : null}
        rowActions={(point) => [
          { label: 'Ver detalle', icon: <Eye />, onSelect: () => setDetail({ point, open: true }) },
          { label: 'Solicitar CUIS', icon: <KeyRound />, onSelect: () => void cuis.run({ pointOfSaleId: point.id }), hidden: !canCuis || point.isClosed, disabled: busy || locked },
          { label: 'Solicitar CUFD', icon: <Ticket />, onSelect: () => void cufd.run({ pointOfSaleId: point.id }), hidden: !canCufd || point.isClosed, disabled: busy || locked },
          { label: 'Probar la comunicación', icon: <Signal />, onSelect: () => void check.run({ pointOfSaleId: point.id }), hidden: !canCheck || point.isClosed, disabled: busy || locked },
          { label: 'Vincular una caja', icon: <Link2 />, onSelect: () => askLink(point), hidden: !canLink || point.isClosed, disabled: locked },
          { label: 'Cerrar en el SIN', icon: <Ban />, tone: 'danger', onSelect: () => askClose(point), hidden: !canClose || point.isClosed || point.code === 0, disabled: locked },
        ]}
        empty={{
          title: points.length === 0 ? 'Todavía no hay puntos de venta' : 'No hay puntos de venta con estos filtros',
          description: points.length === 0 ? 'Use «Preparar SIAT» (crea el punto 0 de cada sucursal) o registre un punto de venta.' : 'Pruebe con otra sucursal o modo, o vea también los cerrados.',
          icon: <Store />,
          action:
            points.length === 0 ? undefined : (
              <Button variant="outline" leftIcon={<Filter />} onClick={table.clearFilters}>
                Limpiar filtros
              </Button>
            ),
        }}
      />

      <SidePanel
        open={detail?.open ?? false}
        onClose={closeDetail}
        title={current ? pointName(current) : 'Punto de venta'}
        description={current ? `${current.branchCode} · ${current.branchName}` : undefined}
        headerExtra={current && <StatusBadge status={pointStateOf(current)} statuses={POINT_STATES} />}
        footer={
          current &&
          !current.isClosed && (
            <div className="flex flex-col gap-2">
              {canCufd && (
                <Button leftIcon={<Ticket />} fullWidth loading={cufd.sending} disabled={busy || locked} onClick={() => void cufd.run({ pointOfSaleId: current.id })}>
                  Solicitar CUFD
                </Button>
              )}
              {canCuis && (
                <Button variant="outline" leftIcon={<KeyRound />} fullWidth loading={cuis.sending} disabled={busy || locked} onClick={() => void cuis.run({ pointOfSaleId: current.id })}>
                  Solicitar CUIS
                </Button>
              )}
              {canCheck && (
                <Button variant="outline" leftIcon={<Signal />} fullWidth loading={check.sending} disabled={busy || locked} onClick={() => void check.run({ pointOfSaleId: current.id })}>
                  Probar la comunicación
                </Button>
              )}
              {canLink && (
                <Button variant="outline" leftIcon={<Link2 />} fullWidth disabled={locked} onClick={() => askLink(current)}>
                  Vincular una caja
                </Button>
              )}
              {canClose && current.code > 0 && (
                <Button variant="danger" leftIcon={<Ban />} fullWidth disabled={locked} onClick={() => askClose(current)}>
                  Cerrar en el SIN
                </Button>
              )}
            </div>
          )
        }
      >
        {current && (
          <DetailList
            items={[
              { label: 'Sucursal del Padrón', value: formatNumber(current.siatBranchCode) },
              { label: 'Ambiente', value: current.environment === 1 ? 'Producción' : 'Pruebas y piloto' },
              { label: 'Caja vinculada', value: current.registerCode ?? 'Sin caja' },
              { label: 'Estado', value: `${pointStateLabel(current)} desde el ${formatDateTime(current.modeSince)}` },
              { label: 'CUIS', value: validityText(current.cuisValidUntil, now) },
              { label: 'CUFD', value: validityText(current.cufdValidUntil, now) },
              { label: 'CUFD obtenido', value: current.cufdObtainedAt ? formatDateTime(current.cufdObtainedAt) : null },
              { label: 'Último contacto con el SIN', value: current.lastContactAt ? formatDateTime(current.lastContactAt) : null },
              { label: 'Documentos por enviar', value: formatNumber(current.pendingDocuments) },
              { label: 'Emitidos fuera de línea', value: formatNumber(current.offlineDocuments) },
              { label: 'Próximo reintento', value: current.retryAt ? formatDateTime(current.retryAt) : null },
              { label: 'Evento abierto', value: current.openEvent?.description ?? null, wide: true },
              { label: 'Último error', value: current.lastError, wide: true },
            ]}
          />
        )}
      </SidePanel>

      <RegisterPointDialog
        open={registering}
        branches={mappedBranches(view)}
        registers={registers}
        activeBranchId={session?.access.activeBranchId ?? null}
        onClose={() => setRegistering(false)}
        onSaved={status.reload}
      />
      <LinkRegisterDialog point={linking} registers={registers} onClose={() => setLinking(null)} onSaved={status.reload} />
      <ConfirmDialog
        open={closing?.open ?? false}
        onClose={() => setClosing((value) => (value ? { ...value, open: false } : value))}
        tone="danger"
        title={closeTarget ? `¿Cerrar el punto ${closeTarget.code} en el SIN?` : ''}
        message={
          closeTarget
            ? `El cierre es DEFINITIVO: el punto ${closeTarget.code} · ${closeTarget.name} de ${closeTarget.branchCode} no podrá volver a emitir y su número no se vuelve a usar. Sus documentos siguen consultables.`
            : ''
        }
        confirmLabel="Cerrar definitivamente"
        confirmDisabled={!understood}
        onConfirm={async () => {
          if (!closeTarget) return false;
          const outcome = await close.run({ pointOfSaleId: closeTarget.id });
          if (outcome.ok) status.reload();
          return outcome;
        }}
        error={close.errorText}
      >
        <Checkbox label="Entiendo que el cierre es definitivo" checked={understood} onChange={setUnderstood} />
      </ConfirmDialog>
    </div>
  );
}
