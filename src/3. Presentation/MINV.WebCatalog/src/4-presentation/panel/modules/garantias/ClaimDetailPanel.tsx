// Módulo «Garantías» · DETALLE lateral de un caso (`GetWarrantyClaimQuery`): resumen (equipo, cliente, cobertura, garantía
// derivada de la venta, proveedor, resolución, reemplazo y la falla reportada) y la bitácora. Al pie, SOLO los pasos que el
// servidor permite desde el estado actual (`nextStatuses`, regla T-05) y solo para quien gestiona casos; además «Agregar
// nota» (caso abierto), «Imprimir la orden de servicio», «Ver la serie» y las ventas del cliente. La consulta la hace la
// pantalla (así la recarga después de un comando).

import { CircleCheck, History, NotebookPen, PackageCheck, Printer, Receipt, ScanBarcode, Stethoscope, Truck, UserRoundCheck, CircleX } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { ROUTES } from '@/4-presentation/app/routes';
import type { RpcQuery } from '@/4-presentation/panel/hooks';
import { Button, DetailList, EmptyState, SidePanel, StatusBadge, TabPanel, Tabs } from '@/4-presentation/panel/kit';
import { formatDateTime } from '@/4-presentation/panel/lib';
import {
  CLAIM_STATES,
  COVERAGES,
  claimTimeline,
  coverageOf,
  customerSalesPath,
  daysText,
  eventTitle,
  nextSteps,
  saleLine,
  seriesPath,
  warrantyLine,
  type ClaimDetailData,
  type ClaimRecord,
  type ClaimStatusCode,
  type NextStep,
} from './claims';

/** Qué puede hacer la sesión. */
export interface ClaimAbilities {
  /** Avanzar el estado (`service.rma.manage`). */
  move: boolean;
  /** Entregar la unidad de reemplazo (`service.rma.manage`). */
  replace: boolean;
  /** Agregar notas (`service.rma.open`). */
  note: boolean;
  /** Ver las ventas del cliente (`sales.view`). */
  sales: boolean;
}

type PanelTab = 'resumen' | 'bitacora';

export interface ClaimDetailPanelProps {
  number: string | null;
  open: boolean;
  onClose: () => void;
  /** La fila de la lista, si está cargada (se muestra mientras llega el detalle). */
  item: ClaimRecord | null;
  detail: RpcQuery<ClaimDetailData>;
  abilities: ClaimAbilities;
  /** «Sucursal CM · Casa Matriz». */
  branchLabel: (code: string) => string;
  onStep: (claim: ClaimRecord, step: NextStep) => void;
  onNote: (claim: ClaimRecord) => void;
  onPrint: (number: string) => void;
}

const STEP_ICONS: Readonly<Record<ClaimStatusCode, ReactNode>> = {
  Received: <CircleCheck />,
  Diagnosing: <Stethoscope />,
  SentToSupplier: <Truck />,
  Repaired: <CircleCheck />,
  Replaced: <PackageCheck />,
  Rejected: <CircleX />,
  Delivered: <UserRoundCheck />,
};

export function ClaimDetailPanel({ number, open, onClose, item, detail, abilities, branchLabel, onStep, onNote, onPrint }: ClaimDetailPanelProps) {
  // La pestaña vuelve a «Resumen» al abrir otro caso.
  const [tabState, setTabState] = useState<{ number: string | null; tab: PanelTab }>({ number, tab: 'resumen' });
  const tab = tabState.number === number ? tabState.tab : 'resumen';

  const data = detail.data;
  const claim = data?.claim ?? item;
  const events = data ? claimTimeline(data.events) : [];
  const steps = data && abilities.move ? nextSteps(data).filter((step) => step.next !== 'Replaced' || claim?.replacementSerial || abilities.replace) : [];
  const warranty = data ? warrantyLine(data.warranty) : null;
  const sale = data ? saleLine(data) : '';
  const isOpen = claim ? claim.status !== 'Delivered' : false;

  const footer = claim && data && (
    <div className="flex flex-col gap-2">
      {steps.map((step) => (
        <Button
          key={step.next}
          variant={step.kind === 'danger' ? 'danger' : step.kind === 'outline' ? 'outline' : 'primary'}
          leftIcon={STEP_ICONS[step.next]}
          fullWidth
          onClick={() => onStep(claim, step)}
        >
          {step.label}
        </Button>
      ))}
      <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
        <Button variant="outline" leftIcon={<Printer />} fullWidth onClick={() => onPrint(claim.number)}>
          Orden de servicio
        </Button>
        {abilities.note && isOpen && (
          <Button variant="outline" leftIcon={<NotebookPen />} fullWidth onClick={() => onNote(claim)}>
            Agregar nota
          </Button>
        )}
      </div>
    </div>
  );

  return (
    <SidePanel
      open={open}
      onClose={onClose}
      title={number ? `Caso ${number}` : 'Caso'}
      description={claim ? `${claim.product} · ${claim.serial}` : undefined}
      headerExtra={claim && <StatusBadge status={claim.status} statuses={CLAIM_STATES} />}
      loading={detail.loading}
      error={detail.error}
      onRetry={detail.reload}
      footer={footer}
    >
      {claim && data && warranty && (
        <Tabs
          label="Secciones del caso"
          value={tab}
          onChange={(next) => setTabState({ number, tab: next })}
          tabs={[
            { id: 'resumen', label: 'Resumen' },
            { id: 'bitacora', label: 'Bitácora', count: events.length },
          ]}
        >
          <TabPanel id="resumen" className="space-y-5">
            <div className="flex flex-wrap items-center gap-2">
              <StatusBadge status={coverageOf(claim)} statuses={COVERAGES} />
              <StatusBadge tone={warranty.tone}>{warranty.text}</StatusBadge>
            </div>
            <section aria-label="Falla reportada" className="rounded-xl border border-border bg-surface-2/60 p-3">
              <h3 className="text-xs font-semibold tracking-wide text-text-faint uppercase">Falla reportada</h3>
              <p className="mt-1 whitespace-pre-line text-text">{claim.issue}</p>
            </section>
            <DetailList
              items={[
                { label: 'Cliente', value: claim.customer },
                { label: 'Sucursal', value: branchLabel(claim.branchCode) },
                { label: 'Serie o IMEI', value: <span className="font-mono break-all">{claim.serial}</span> },
                { label: 'SKU', value: claim.sku },
                { label: 'Producto', value: claim.product, wide: true },
                { label: 'Recibido', value: formatDateTime(claim.receivedAt) },
                { label: 'Tiempo', value: daysText(claim) },
                { label: 'Venta', value: sale ? sale.charAt(0).toUpperCase() + sale.slice(1) : null, wide: true },
                { label: 'Proveedor', value: claim.supplier },
                { label: 'Unidad de reemplazo', value: claim.replacementSerial && <span className="font-mono break-all">{claim.replacementSerial}</span> },
                { label: 'Resolución', value: claim.resolution, wide: true },
              ]}
            />
            <div className="flex flex-col gap-2 sm:flex-row sm:flex-wrap">
              <Button variant="outline" leftIcon={<ScanBarcode />} to={ROUTES.panelModule(seriesPath(claim.serial, claim.sku))}>
                Ver la serie
              </Button>
              {abilities.sales && data.warranty.customerCode && (
                <Button variant="outline" leftIcon={<Receipt />} to={ROUTES.panelModule(customerSalesPath(data.warranty.customerCode))}>
                  Ver las ventas del cliente
                </Button>
              )}
            </div>
          </TabPanel>

          <TabPanel id="bitacora">
            {events.length === 0 ? (
              <EmptyState size="sm" icon={<History />} title="Sin hechos registrados" />
            ) : (
              <ol className="space-y-3" aria-label="Bitácora del caso">
                {events.map((event, index) => (
                  <li key={`${event.occurredAt}-${index}`} className="rounded-xl border border-border bg-surface-2/60 p-3 text-sm">
                    <p className="flex flex-wrap items-baseline justify-between gap-x-3">
                      <span className="font-semibold text-text">{eventTitle(event)}</span>
                      <span className="text-xs text-text-muted tabular-nums">{formatDateTime(event.occurredAt)}</span>
                    </p>
                    {event.note && <p className="mt-0.5 text-text">{event.note}</p>}
                    <p className="mt-0.5 text-xs text-text-muted">{event.user}</p>
                  </li>
                ))}
              </ol>
            )}
          </TabPanel>
        </Tabs>
      )}
    </SidePanel>
  );
}
