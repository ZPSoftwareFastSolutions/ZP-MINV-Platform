// Facturación › Estado del SIAT (SiatStatusView del escritorio): el tablero de la facturación con el SIN.
//   - Cabecera: empresa, NIT, ambiente (pruebas = sin valor legal), si la facturación está activa, hora del SIN y catálogos.
//   - Alertas del servidor con cuenta regresiva de los plazos (CUFD, eventos por registrar, facturas por transcribir…).
//   - Indicadores PLEGADOS («Ver indicadores del SIAT», P-10): puntos, emitidos hoy, por enviar, fuera de línea, eventos.
//   - Pestañas (`?pestana=`): «Puntos de venta» (una tarjeta por punto con su modo, CUIS y CUFD vigentes y sus acciones:
//     verificar la comunicación, pedir CUFD, pasar a fuera de línea, contingencia manual, terminarla y recuperar),
//     «Eventos significativos», «Paquetes» y «Talonarios CAFC»; la transcripción de facturas manuales (CAFC).
//   - La bitácora técnica del SIN, PLEGADA y solo para quien configura la facturación.
// `?transcribir=1` (botón del tablero) abre la transcripción de una factura manual.

import { Activity, CircleAlert, Cloud, FileText, Flag, Plus, RadioTower, RefreshCw, ScrollText, Settings, ShieldCheck, Timer, WifiOff, Zap } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { ROUTES } from '@/4-presentation/app/routes';
import { usePermissions, useRpcCommand, useRpcQuery, useTableState } from '@/4-presentation/panel/hooks';
import {
  Alert,
  Button,
  Collapsible,
  ConfirmDialog,
  EmptyState,
  ErrorState,
  FilterBar,
  LoadingState,
  Page,
  SelectField,
  StatCard,
  StatusBadge,
  TabPanel,
  Tabs,
  statusOptions,
  useNotify,
} from '@/4-presentation/panel/kit';
import { formatDateTime, formatMoney, formatNumber } from '@/4-presentation/panel/lib';
import { CafcTab } from './CafcTab';
import { EventsTab } from './EventsTab';
import { ManualContingencyDialog } from './ManualContingencyDialog';
import { PackagesTab } from './PackagesTab';
import { PointCard } from './PointCard';
import { RegisterCafcDialog } from './RegisterCafcDialog';
import { ServiceCalls } from './ServiceCalls';
import { ENVIRONMENT_TEST, MODES, alertTone, branchOptions, countdown, modesSummary, plainMessage, pointMode, pointTitle, type EventData, type PointData } from './siat';
import { TranscribeDialog } from './TranscribeDialog';
import { useNow } from './useNow';

const TABS = ['puntos', 'eventos', 'paquetes', 'talonarios'] as const;
type TabId = (typeof TABS)[number];

function tabOf(value: string | null): TabId {
  return TABS.includes(value as TabId) ? (value as TabId) : 'puntos';
}

/** Parámetro del botón del tablero «Transcribir factura manual». */
const TRANSCRIBE_PARAM = 'transcribir';

export function SiatPage() {
  const notify = useNotify();
  const { can, canRun } = usePermissions();
  const now = useNow();
  const [params, setParams] = useSearchParams();
  const tab = tabOf(params.get('pestana'));
  const status = useRpcQuery('GetSiatStatusQuery', {});
  const data = status.data;
  const pointsTable = useTableState({ prefix: 'pv_', filters: { sucursal: '', modo: '' } });
  const points = useMemo(
    () =>
      (data?.points ?? []).filter(
        (point) => (!pointsTable.filters.sucursal || point.branchCode === pointsTable.filters.sucursal) && (!pointsTable.filters.modo || pointMode(point) === pointsTable.filters.modo),
      ),
    [data, pointsTable.filters],
  );
  const pointBranches = useMemo(() => branchOptions(data?.points ?? []), [data]);

  const setTab = (next: TabId) =>
    setParams(
      (previous) => {
        const updated = new URLSearchParams(previous);
        if (next === 'puntos') updated.delete('pestana');
        else updated.set('pestana', next);
        return updated;
      },
      { replace: true },
    );

  // Comandos de la cabecera y de las tarjetas (el error se avisa solo; el éxito, con el mensaje del servidor).
  const work = useRpcCommand('RunSiatWorkCommand', { errorTitle: 'No se pudo ejecutar el trabajo de facturación' });
  const check = useRpcCommand('CheckSiatCommunicationCommand', { errorTitle: 'No se pudo verificar la comunicación' });
  const cufd = useRpcCommand('RequestCufdCommand', { errorTitle: 'No se pudo pedir el CUFD' });
  const recover = useRpcCommand('RecoverPointOfSaleCommand', { errorTitle: 'No se pudo recuperar el punto de venta' });
  const goOffline = useRpcCommand('GoOfflineCommand', { notifyError: false });
  const endContingency = useRpcCommand('EndContingencyCommand', { notifyError: false });
  const busy = check.sending || cufd.sending || recover.sending || goOffline.sending || endContingency.sending;

  const canWork = canRun('RunSiatWorkCommand');
  const canCheck = canRun('CheckSiatCommunicationCommand');
  const canCufd = canRun('RequestCufdCommand');
  const canContingency = canRun('GoOfflineCommand') && canRun('EndContingencyCommand') && canRun('RecoverPointOfSaleCommand') && canRun('StartManualContingencyCommand');
  const canTranscribe = canRun('TranscribeManualInvoiceCommand');
  const canRegisterCafc = canRun('RegisterContingencyCodeCommand');
  const canConfigure = can('billing.configure');
  const canSeeCalls = canRun('GetSiatServiceCallsQuery');

  const runWork = async () => {
    const outcome = await work.run({ maintain: true });
    if (!outcome.ok) return;
    const { dispatch, maintenance } = outcome.result;
    const extra = maintenance ? ` · recuperados ${maintenance.recovered} · paquetes validados ${maintenance.packagesValidated}` : '';
    notify.success('Trabajo de facturación ejecutado', `${dispatch.sent} enviados · ${dispatch.valid} válidos · ${dispatch.rejected} rechazados${extra}`);
    status.reload();
  };

  const checkAll = async () => {
    const outcome = await check.run({ pointOfSaleId: null });
    if (!outcome.ok) return;
    notify.success('Comunicación con el SIN', plainMessage(outcome.result));
    status.reload();
  };

  const pointCommand = async (point: PointData, title: string, run: () => Promise<{ ok: true; result: string } | { ok: false }>) => {
    const outcome = await run();
    if (!outcome.ok) return;
    notify.success(title, `${point.branchCode} · punto ${point.code}: ${plainMessage(outcome.result)}`);
    status.reload();
  };

  // Diálogos (cada uno se monta de nuevo en cada apertura).
  const [dialogSession, setDialogSession] = useState(0);
  const [manual, setManual] = useState<PointData | null>(null);
  const [offlineTarget, setOfflineTarget] = useState<PointData | null>(null);
  const [endTarget, setEndTarget] = useState<PointData | null>(null);
  const [cafcOpen, setCafcOpen] = useState(false);
  const [cafcVersion, setCafcVersion] = useState(0);
  const [transcribe, setTranscribe] = useState<{ open: boolean; eventId: string | null }>({ open: false, eventId: null });
  const openTranscribe = (event: EventData | null) => {
    setDialogSession((count) => count + 1);
    setTranscribe({ open: true, eventId: event?.id ?? null });
  };

  // `?transcribir=1` (botón del tablero): abre la transcripción una vez y quita el parámetro de la dirección.
  const transcribeRequested = params.get(TRANSCRIBE_PARAM) === '1';
  const [handledTranscribe, setHandledTranscribe] = useState(false);
  if (transcribeRequested && !handledTranscribe) {
    setHandledTranscribe(true);
    if (canTranscribe) {
      setDialogSession((count) => count + 1);
      setTranscribe({ open: true, eventId: null });
    }
  }
  useEffect(() => {
    if (!transcribeRequested) return;
    setParams(
      (previous) => {
        const next = new URLSearchParams(previous);
        next.delete(TRANSCRIBE_PARAM);
        return next;
      },
      { replace: true },
    );
  }, [transcribeRequested, setParams]);

  const showPackages = (event: EventData) =>
    setParams(
      (previous) => {
        const next = new URLSearchParams(previous);
        next.set('pestana', 'paquetes');
        next.set('p_evento', event.id);
        return next;
      },
      { replace: true },
    );

  const summary = data ? modesSummary(data.points) : null;
  const isTest = data?.environment === ENVIRONMENT_TEST;

  let header;
  if (status.error && !data) header = <ErrorState error={status.error} operation="GetSiatStatusQuery" onRetry={status.reload} retrying={status.fetching} />;
  else if (!data) header = <LoadingState label="Cargando el estado del SIAT…" rows={2} />;
  else if (!data.configured)
    header = (
      <Alert
        tone="warning"
        title="La facturación SIAT todavía no está configurada"
        actions={
          canConfigure && (
            <Button leftIcon={<Settings />} to={ROUTES.panelModule('configuracion?pestana=facturacion')}>
              Configurar la facturación
            </Button>
          )
        }
      >
        Cargue el NIT, el código del sistema y el token del SIN; después registre los puntos de venta.
      </Alert>
    );
  else
    header = (
      <section aria-label="Datos de la facturación" className="flex flex-wrap items-center justify-between gap-3 rounded-card border border-border bg-surface p-4" data-testid="siat-cabecera">
        <div className="min-w-0 space-y-1">
          <p className="font-display text-lg font-semibold">
            {data.businessName ?? 'Empresa'} · NIT {data.nit ?? '—'}
          </p>
          <p className="text-sm text-text-muted">
            {data.clockSyncedAt ? `Hora del SIN sincronizada el ${formatDateTime(data.clockSyncedAt)}` : 'La hora del SIN todavía no se sincronizó'}
            {' · '}
            {data.lastCatalogSync ? `catálogos del ${formatDateTime(data.lastCatalogSync)}` : 'catálogos sin sincronizar'}
            {data.tokenValidUntil ? ` · token vigente hasta el ${formatDateTime(data.tokenValidUntil)}` : ''}
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <StatusBadge tone={data.enabled ? 'success' : 'warning'}>{data.enabled ? 'Facturación activa' : 'Facturación desactivada'}</StatusBadge>
          <StatusBadge tone={isTest ? 'warning' : 'info'}>{isTest ? 'Ambiente de pruebas: sin valor legal' : 'Producción: con valor legal'}</StatusBadge>
          {!data.hasToken && <StatusBadge tone="danger">Sin token del SIN</StatusBadge>}
        </div>
      </section>
    );

  return (
    <Page
      title="Estado del SIAT"
      description="Conexión con el SIN, puntos de venta, plazos y contingencias. Las facturas se envían solas; aquí se ve qué falta y se actúa."
      actions={
        <>
          <Button variant="outline" leftIcon={<RefreshCw />} loading={status.fetching && !status.loading} onClick={status.reload}>
            Actualizar
          </Button>
          {canCheck && (
            <Button variant="outline" leftIcon={<RadioTower />} loading={check.sending} onClick={() => void checkAll()}>
              Verificar comunicación
            </Button>
          )}
          {canConfigure && (
            <Button variant="outline" leftIcon={<Settings />} to={ROUTES.panelModule('configuracion?pestana=facturacion')}>
              Configurar
            </Button>
          )}
          {canRegisterCafc && (
            <Button
              variant="outline"
              leftIcon={<Plus />}
              onClick={() => {
                setDialogSession((count) => count + 1);
                setCafcOpen(true);
              }}
            >
              Registrar talonario CAFC
            </Button>
          )}
          {canTranscribe && (
            <Button variant="outline" leftIcon={<FileText />} onClick={() => openTranscribe(null)}>
              Transcribir factura manual
            </Button>
          )}
          {canWork && (
            <Button leftIcon={<Zap />} loading={work.sending} onClick={() => void runWork()}>
              Procesar ahora
            </Button>
          )}
        </>
      }
    >
      {header}

      {data && data.alerts.length > 0 && (
        <section aria-label="Alertas de la facturación" className="space-y-2" data-testid="siat-alertas">
          {data.alerts.map((alert, index) => (
            <Alert key={`${alert.title}-${index}`} tone={alertTone(alert.severity)} title={alert.title}>
              <p>{alert.detail}</p>
              {alert.deadline && (
                <p className="mt-1 flex items-center gap-1 font-medium">
                  <Timer aria-hidden="true" className="size-4" />
                  {countdown(alert.deadline, now)} · {formatDateTime(alert.deadline)}
                </p>
              )}
            </Alert>
          ))}
        </section>
      )}

      {data && summary && (
        <Collapsible label="Ver indicadores del SIAT" openLabel="Ocultar indicadores del SIAT" icon={<Activity />} description="Ambiente, puntos de venta, emitidos hoy, pendientes y eventos abiertos.">
          <div className="grid grid-cols-[repeat(auto-fit,minmax(11rem,1fr))] gap-3" data-testid="siat-indicadores">
            <StatCard label="Ambiente" value={isTest ? 'Pruebas' : 'Producción'} hint={isTest ? 'Sin valor legal' : 'Con valor legal'} icon={<ShieldCheck />} />
            <StatCard
              label="Puntos de venta"
              value={summary.active === 0 ? 'Sin puntos' : summary.notOnline === 0 ? 'En línea' : `${summary.notOnline} sin conexión`}
              hint={summary.text || 'Registre los puntos de venta en Configuración'}
              tone={summary.notOnline > 0 ? 'warning' : 'success'}
              icon={<Cloud />}
            />
            <StatCard label="Emitidos hoy" value={formatNumber(data.documentsToday)} hint={formatMoney(data.billedToday)} icon={<FileText />} />
            <StatCard label="Por enviar" value={formatNumber(data.pendingDocuments)} hint={data.pendingDocuments === 0 ? 'Nada por enviar' : 'Se envían solos'} tone={data.pendingDocuments > 0 ? 'warning' : 'default'} icon={<CircleAlert />} />
            <StatCard label="Fuera de línea" value={formatNumber(data.offlineDocuments)} hint={data.offlineDocuments === 0 ? 'Ninguno' : 'Se envían al volver la conexión'} tone={data.offlineDocuments > 0 ? 'warning' : 'default'} icon={<WifiOff />} />
            <StatCard label="Eventos abiertos" value={formatNumber(data.openEvents)} hint={data.openEvents === 0 ? 'Sin contingencias' : 'Contingencias en curso'} tone={data.openEvents > 0 ? 'danger' : 'default'} icon={<Flag />} />
          </div>
        </Collapsible>
      )}

      <Tabs
        label="Secciones del SIAT"
        value={tab}
        onChange={setTab}
        tabs={[
          { id: 'puntos', label: 'Puntos de venta', count: data?.points.length },
          { id: 'eventos', label: 'Eventos significativos' },
          { id: 'paquetes', label: 'Paquetes' },
          { id: 'talonarios', label: 'Talonarios CAFC' },
        ]}
      >
        <TabPanel id="puntos">
          <div className="space-y-4">
            <FilterBar activeCount={pointsTable.activeFilterCount} onClear={pointsTable.clearFilters} title="Filtros de los puntos de venta">
              <SelectField label="Sucursal" allLabel="Todas las sucursales" value={pointsTable.filters.sucursal} onChange={(value) => pointsTable.setFilter('sucursal', value)} options={pointBranches} />
              <SelectField label="Modo" allLabel="Todos los modos" value={pointsTable.filters.modo} onChange={(value) => pointsTable.setFilter('modo', value)} options={statusOptions(MODES)} />
            </FilterBar>
            {data && points.length === 0 ? (
              <EmptyState
                icon={<RadioTower />}
                title={data.points.length === 0 ? 'Todavía no hay puntos de venta del SIN' : 'Ningún punto de venta con estos filtros'}
                description={data.points.length === 0 ? 'Regístrelos en Administración › Configuración › Facturación.' : 'Pruebe con otros filtros.'}
              >
                {data.points.length > 0 && (
                  <Button variant="outline" onClick={pointsTable.clearFilters}>
                    Limpiar filtros
                  </Button>
                )}
              </EmptyState>
            ) : (
              <div className="grid grid-cols-1 gap-4 xl:grid-cols-2" data-testid="puntos-de-venta">
                {points.map((point) => (
                  <PointCard
                    key={point.id}
                    point={point}
                    now={now}
                    can={{ check: canCheck, requestCufd: canCufd, contingency: canContingency }}
                    busy={busy}
                    onCheck={() => void pointCommand(point, 'Comunicación con el SIN', () => check.run({ pointOfSaleId: point.id }))}
                    onRequestCufd={() => void pointCommand(point, 'CUFD solicitado', () => cufd.run({ pointOfSaleId: point.id }))}
                    onRecover={() => void pointCommand(point, 'Recuperación', () => recover.run({ pointOfSaleId: point.id }))}
                    onGoOffline={() => setOfflineTarget(point)}
                    onEnd={() => setEndTarget(point)}
                    onStartManual={() => {
                      setDialogSession((count) => count + 1);
                      setManual(point);
                    }}
                  />
                ))}
              </div>
            )}
          </div>
        </TabPanel>
        <TabPanel id="eventos">
          <EventsTab canTranscribe={canTranscribe} onShowPackages={showPackages} onTranscribe={(event) => openTranscribe(event)} />
        </TabPanel>
        <TabPanel id="paquetes">
          <PackagesTab />
        </TabPanel>
        <TabPanel id="talonarios">
          <CafcTab
            key={cafcVersion}
            canRegister={canRegisterCafc}
            onRegister={() => {
              setDialogSession((count) => count + 1);
              setCafcOpen(true);
            }}
          />
        </TabPanel>
      </Tabs>

      {canSeeCalls && (
        <Collapsible label="Ver la bitácora técnica del SIN" openLabel="Ocultar la bitácora técnica del SIN" icon={<ScrollText />} description="Cada llamada al SIN con lo enviado y lo respondido (sin el token).">
          <ServiceCalls />
        </Collapsible>
      )}

      <ManualContingencyDialog key={`manual-${dialogSession}`} target={manual} onClose={() => setManual(null)} onDone={status.reload} />
      <RegisterCafcDialog
        key={`cafc-${dialogSession}`}
        open={cafcOpen}
        onClose={() => setCafcOpen(false)}
        onDone={() => {
          setCafcVersion((count) => count + 1);
          status.reload();
        }}
      />
      <TranscribeDialog
        key={`transcribir-${dialogSession}`}
        open={transcribe.open}
        initialEventId={transcribe.eventId}
        onClose={() => setTranscribe({ open: false, eventId: null })}
        onDone={status.reload}
      />
      <ConfirmDialog
        open={offlineTarget !== null}
        onClose={() => {
          setOfflineTarget(null);
          goOffline.reset();
        }}
        title={offlineTarget ? `¿Pasar a fuera de línea el ${pointTitle(offlineTarget).toLocaleLowerCase('es')}?` : 'Pasar a fuera de línea'}
        message="Deja de enviar en línea: la caja sigue facturando (tipo de emisión 2) y las facturas se envían solas en paquetes al volver la conexión."
        confirmLabel="Pasar a fuera de línea"
        error={goOffline.errorText}
        onConfirm={async () => {
          if (!offlineTarget) return false;
          const point = offlineTarget;
          const outcome = await goOffline.run({ pointOfSaleId: point.id, eventCode: null });
          if (outcome.ok) {
            notify.warning('Fuera de línea', `${point.branchCode} · punto ${point.code}: ${plainMessage(outcome.result)}`);
            status.reload();
          }
          return outcome;
        }}
      />
      <ConfirmDialog
        open={endTarget !== null}
        onClose={() => {
          setEndTarget(null);
          endContingency.reset();
        }}
        title={endTarget ? `¿Terminar la contingencia del ${pointTitle(endTarget).toLocaleLowerCase('es')}?` : 'Terminar la contingencia'}
        message={
          endTarget
            ? `Se cierra el evento con la hora actual y empieza la recuperación: CUFD nuevo → registro del evento en el SIN → envío de los paquetes. Si no hay comunicación, queda recuperando y se reintenta solo.${endTarget.mode === 'ManualContingency' ? ' Después transcriba las facturas manuales del talonario CAFC (72 h desde el fin).' : ''}`
            : ''
        }
        confirmLabel="Terminar contingencia"
        error={endContingency.errorText}
        onConfirm={async () => {
          if (!endTarget) return false;
          const point = endTarget;
          const outcome = await endContingency.run({ pointOfSaleId: point.id, endedAt: null });
          if (outcome.ok) {
            notify.success('Contingencia terminada', `${point.branchCode} · punto ${point.code}: ${plainMessage(outcome.result)}`);
            status.reload();
          }
          return outcome;
        }}
      />
    </Page>
  );
}
