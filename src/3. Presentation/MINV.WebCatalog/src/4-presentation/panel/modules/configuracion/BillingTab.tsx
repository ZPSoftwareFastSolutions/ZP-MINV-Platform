// Módulo «Configuración» · pestaña «Facturación SIAT» (BillingSettingsView del escritorio y las acciones de configuración
// de «Estado SIAT»): el estado de la facturación con lo que falta para facturar, «Probar conexión», «Preparar SIAT» y
// «Sincronizar catálogos»; y en secciones PLEGABLES (se cargan al abrirlas): datos del Padrón y del sistema, conexión con
// el SIN por ambiente (el token nunca se muestra), sucursales del Padrón, puntos de venta con CUIS y CUFD, y las
// actividades económicas. Sin la licencia del módulo «Facturación SIAT» se ve todo, pero no se cambia nada.

import { Cable, CircleCheck, CircleX, CloudDownload, FileText, MapPin, Pencil, Rocket, ScrollText, Signal, Store, TriangleAlert } from 'lucide-react';
import { useState } from 'react';
import { usePermissions, useRpcCommand, type RpcQuery } from '@/4-presentation/panel/hooks';
import { Alert, Button, Collapsible, DetailList, ErrorState, LoadingState, Section, SelectField, StatusBadge, type AlertTone } from '@/4-presentation/panel/kit';
import { formatNumber } from '@/4-presentation/panel/lib';
import { ActivitiesList } from './ActivitiesList';
import { PadronBranches } from './PadronBranches';
import { PointsOfSale } from './PointsOfSale';
import { SiatProfileDialog } from './SiatProfileDialog';
import { SiatSettingsDialog } from './SiatSettingsDialog';
import {
  BILLING_STATES,
  ENDPOINT_FIELDS,
  PRODUCTION,
  TESTING,
  billingStateOf,
  billingStateText,
  clockText,
  environmentName,
  maintenanceText,
  plainMessage,
  profileOf,
  readinessChecks,
  syncText,
  tokenStatusText,
  type SiatSettingsData,
} from './settings';

export interface BillingTabProps {
  siat: RpcQuery<SiatSettingsData>;
  /** Nombre de la empresa (razón social propuesta). */
  companyName: string;
}

export function BillingTab({ siat, companyName }: BillingTabProps) {
  if (siat.error && !siat.data) return <ErrorState error={siat.error} operation="GetSiatSettingsQuery" onRetry={siat.reload} retrying={siat.fetching} />;
  if (!siat.data) return <LoadingState label="Cargando la configuración de la facturación…" rows={4} />;
  return <BillingSettings view={siat.data} reload={siat.reload} companyName={companyName} />;
}

interface ActionResult {
  tone: AlertTone;
  title: string;
  text: string;
  items?: readonly string[];
}

const ENVIRONMENT_OPTIONS = [
  { value: String(TESTING), label: environmentName(TESTING) },
  { value: String(PRODUCTION), label: environmentName(PRODUCTION) },
];

function BillingSettings({ view, reload, companyName }: { view: SiatSettingsData; reload: () => void; companyName: string }) {
  const { canRun } = usePermissions();
  const locked = !view.moduleActive;
  const canSave = canRun('SaveSiatSettingsCommand');
  const canProfile = canRun('SaveSiatProfileCommand');
  const canBranch = canRun('SaveSiatBranchCommand');
  const canCheck = canRun('CheckSiatCommunicationCommand');
  const canPrepare = canRun('PrepareSiatCommand');
  const canSync = canRun('SyncSiatCatalogsCommand');

  const [result, setResult] = useState<ActionResult | null>(null);
  const [editing, setEditing] = useState(false);
  const [dataOpen, setDataOpen] = useState(true);
  const [profileEnvironment, setProfileEnvironment] = useState(String(view.environment === PRODUCTION ? PRODUCTION : TESTING));
  const [editingProfile, setEditingProfile] = useState<number | null>(null);
  // Después de preparar o sincronizar, los puntos y las actividades se vuelven a leer.
  const [version, setVersion] = useState(0);

  const check = useRpcCommand('CheckSiatCommunicationCommand', { notifyError: false });
  const prepare = useRpcCommand('PrepareSiatCommand', { notifyError: false, success: 'Facturación preparada' });
  const sync = useRpcCommand('SyncSiatCatalogsCommand', { notifyError: false, success: 'Catálogos sincronizados' });

  const runCheck = async () => {
    const outcome = await check.run({ pointOfSaleId: null });
    setResult(outcome.ok ? { tone: 'success', title: 'Comunicación con el SIN', text: plainMessage(outcome.result) } : { tone: 'danger', title: 'Sin comunicación con el SIN', text: outcome.message });
  };
  const runPrepare = async () => {
    const outcome = await prepare.run({});
    if (!outcome.ok) {
      setResult({ tone: 'danger', title: 'No se pudo preparar la facturación', text: outcome.message });
      return;
    }
    setResult({ tone: outcome.result.messages.length > 0 ? 'info' : 'success', title: 'Facturación preparada', text: maintenanceText(outcome.result), items: outcome.result.messages });
    reload();
    setVersion((value) => value + 1);
  };
  const runSync = async () => {
    const outcome = await sync.run({ catalog: null });
    if (!outcome.ok) {
      setResult({ tone: 'danger', title: 'No se pudieron sincronizar los catálogos', text: outcome.message });
      return;
    }
    setResult({ tone: outcome.result.errors.length > 0 ? 'warning' : 'success', title: 'Catálogos sincronizados', text: syncText(outcome.result), items: outcome.result.errors });
    reload();
    setVersion((value) => value + 1);
  };

  const profile = profileOf(view, Number(profileEnvironment));
  const checks = readinessChecks(view);

  return (
    <div className="space-y-5">
      <Section
        title="Estado de la facturación"
        description={billingStateText(view)}
        actions={
          <>
            {canCheck && (
              <Button variant="outline" leftIcon={<Signal />} loading={check.sending} disabled={locked} onClick={() => void runCheck()}>
                Probar conexión
              </Button>
            )}
            {canPrepare && (
              <Button variant="outline" leftIcon={<Rocket />} loading={prepare.sending} disabled={locked} onClick={() => void runPrepare()}>
                Preparar SIAT
              </Button>
            )}
            {canSync && (
              <Button variant="outline" leftIcon={<CloudDownload />} loading={sync.sending} disabled={locked} onClick={() => void runSync()}>
                Sincronizar catálogos
              </Button>
            )}
          </>
        }
      >
        <div className="space-y-4">
          <div className="flex flex-wrap items-center gap-2">
            <StatusBadge status={billingStateOf(view)} statuses={BILLING_STATES} />
            <StatusBadge tone={view.environment === PRODUCTION ? 'danger' : 'info'}>{environmentName(view.environment)}</StatusBadge>
          </div>
          {locked && (
            <Alert tone="warning" title="Módulo sin licencia">
              La empresa no tiene habilitado el módulo «Facturación SIAT (computarizada en línea)»: puede revisar la configuración, pero no guardarla, activarla
              ni hablar con el SIN.
            </Alert>
          )}
          <ul className="space-y-1.5" aria-label="Lo que hace falta para facturar" data-testid="lista-para-facturar">
            {checks.map((item) => (
              <li key={item.key} className="flex items-start gap-2 text-sm">
                {item.ok ? <CircleCheck aria-hidden="true" className="mt-0.5 size-4 shrink-0 text-success-text" /> : <CircleX aria-hidden="true" className="mt-0.5 size-4 shrink-0 text-warning-text" />}
                <span>
                  <span className="sr-only">{item.ok ? 'Listo: ' : 'Falta: '}</span>
                  {item.label}
                </span>
              </li>
            ))}
          </ul>
          <p className="text-sm text-text-muted">{clockText(view)}</p>
          {result && (
            <Alert tone={result.tone} title={result.title}>
              <p>{result.text}</p>
              {result.items && result.items.length > 0 && (
                <ul className="mt-1 list-disc pl-5">
                  {result.items.map((item) => (
                    <li key={item}>{item}</li>
                  ))}
                </ul>
              )}
            </Alert>
          )}
        </div>
      </Section>

      <Collapsible
        label="Datos del Padrón y del sistema"
        description="NIT, razón social, código de sistema, ambiente, leyendas y si la facturación está activa."
        icon={<FileText />}
        open={dataOpen}
        onOpenChange={setDataOpen}
      >
        <div className="space-y-4">
          <DetailList
            items={[
              { label: 'NIT', value: view.nit === null ? null : String(view.nit) },
              { label: 'Razón social', value: view.businessName },
              { label: 'Código de sistema', value: view.systemCode && <span className="font-mono">{view.systemCode}</span> },
              { label: 'Ambiente', value: environmentName(view.environment) },
              { label: 'Facturación', value: view.isEnabled ? 'Activa: las ventas emiten factura' : 'Desactivada' },
              { label: 'Leyenda en línea', value: view.onlineLegend, wide: true },
              { label: 'Leyenda fuera de línea', value: view.offlineLegend, wide: true },
            ]}
          />
          {canSave && (
            <Button leftIcon={<Pencil />} disabled={locked} onClick={() => setEditing(true)}>
              {view.configured ? 'Cambiar los datos' : 'Cargar los datos del Padrón'}
            </Button>
          )}
        </div>
      </Collapsible>

      <Collapsible label="Conexión con el SIN" description="Direcciones de los servicios, consulta por QR, tiempo de espera y token delegado de cada ambiente." icon={<Cable />}>
        <div className="space-y-4">
          <SelectField
            label="Ambiente de la conexión"
            allLabel={false}
            value={profileEnvironment}
            onChange={(value) => setProfileEnvironment(value || String(TESTING))}
            options={ENVIRONMENT_OPTIONS}
            className="max-w-sm"
          />
          {profile ? (
            <DetailList
              columns={1}
              items={[
                ...ENDPOINT_FIELDS.map((field) => ({ label: field.label, value: <span className="font-mono text-xs break-all">{profile.endpoints[field.key]}</span> })),
                { label: 'Namespace', value: <span className="font-mono text-xs break-all">{profile.endpoints.namespace}</span> },
                { label: 'Consulta por QR', value: <span className="font-mono text-xs break-all">{profile.qrBaseUrl}</span> },
                { label: 'Tiempo de espera', value: `${formatNumber(profile.timeoutSeconds)} segundos` },
              ]}
            />
          ) : (
            <p className="text-sm text-text-muted">Todavía no hay una conexión guardada para este ambiente.</p>
          )}
          <div className="flex flex-wrap items-center gap-2" data-testid="estado-del-token">
            <StatusBadge tone={profile?.hasToken ? 'success' : 'warning'}>{profile?.hasToken ? 'Token guardado' : 'Sin token'}</StatusBadge>
            <span className="text-sm text-text-muted">{tokenStatusText(profile)}</span>
          </div>
          {canProfile && (
            <Button leftIcon={<Pencil />} disabled={locked} onClick={() => setEditingProfile(Number(profileEnvironment))}>
              {profile ? 'Cambiar la conexión' : 'Cargar la conexión'}
            </Button>
          )}
        </div>
      </Collapsible>

      <Collapsible
        label="Sucursales del Padrón"
        description={`El código de cada sucursal en el Padrón (0 = casa matriz), su municipio y su teléfono · ${formatNumber(view.branches.filter((branch) => branch.siatCode === null).length)} sin código.`}
        icon={<MapPin />}
      >
        <PadronBranches branches={view.branches} canSave={canBranch} locked={locked} onSaved={reload} />
      </Collapsible>

      <Collapsible label="Puntos de venta, CUIS y CUFD" description="Registrar puntos en el SIN, pedir sus códigos, vincular cajas y cerrarlos." icon={<Store />}>
        <PointsOfSale key={`puntos-${version}`} view={view} locked={locked} />
      </Collapsible>

      <Collapsible label="Actividades económicas del SIN" description="Las de la empresa, tal como llegan en la sincronización de catálogos." icon={<ScrollText />}>
        <ActivitiesList key={`actividades-${version}`} />
      </Collapsible>

      {!view.configured && (
        <p className="flex items-start gap-2 text-sm text-text-muted">
          <TriangleAlert aria-hidden="true" className="mt-0.5 size-4 shrink-0" />
          Orden sugerido: datos del Padrón → conexión con su token → código 0 de la casa matriz → «Preparar SIAT» → sincronizar catálogos → activar la facturación.
        </p>
      )}

      <SiatSettingsDialog open={editing} view={view} companyName={companyName} onClose={() => setEditing(false)} onSaved={reload} />
      <SiatProfileDialog environment={editingProfile} view={view} onClose={() => setEditingProfile(null)} onSaved={reload} />
    </div>
  );
}
