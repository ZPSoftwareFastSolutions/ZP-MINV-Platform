// Módulo «Series» · DETALLE lateral de una unidad: su garantía (derivada de la venta, regla T-04), dónde está y de dónde
// vino, a quién se vendió, sus casos de garantía y su línea de tiempo (cada hecho con su sucursal, documento y usuario).
// Las consultas (`GetSerialTraceQuery` y `GetWarrantyStatusQuery`) las hace la pantalla, así las recarga después de un
// comando. Al pie, las acciones que la sesión puede usar: abrir o ver su caso de garantía, dar destino, ver solo ese
// producto y ver las ventas del cliente.

import { Filter, History, PackageX, Receipt, ShieldPlus } from 'lucide-react';
import { useState } from 'react';
import { ROUTES } from '@/4-presentation/app/routes';
import type { RpcQuery } from '@/4-presentation/panel/hooks';
import { Button, DetailList, EmptyState, SidePanel, StatusBadge, TabPanel, Tabs } from '@/4-presentation/panel/kit';
import { describePanelError, formatDate, formatDateTime } from '@/4-presentation/panel/lib';
import {
  CLAIM_STATES,
  EVENT_ACTIONS,
  SERIAL_KINDS,
  SERIAL_STATES,
  canDisposeOf,
  canOpenClaimFor,
  claimPath,
  customerSalesPath,
  eventDetail,
  locationOf,
  openClaimOf,
  openClaimPath,
  originText,
  timeline,
  traceWarranty,
  type SerialRecord,
  type SerialTraceData,
  type WarrantyInfo,
} from './serials';

/** La unidad abierta: la serie y, si se conoce, su producto (una serie puede repetirse en dos productos). */
export interface SerialTarget {
  serial: string;
  sku: string | null;
}

/** Qué puede hacer la sesión. */
export interface SerialAbilities {
  openClaim: boolean;
  dispose: boolean;
  sales: boolean;
}

type PanelTab = 'resumen' | 'trazabilidad' | 'casos';

export interface SerialDetailPanelProps {
  target: SerialTarget | null;
  open: boolean;
  onClose: () => void;
  trace: RpcQuery<SerialTraceData>;
  warranty: RpcQuery<WarrantyInfo>;
  abilities: SerialAbilities;
  onDispose: (row: SerialRecord) => void;
  onOnlyProduct: (sku: string) => void;
  /** La lista ya muestra solo ese producto. */
  productFiltered: (sku: string) => boolean;
}

const WARRANTY_BOX = {
  success: 'border-success/40 bg-success-soft',
  danger: 'border-danger/40 bg-danger-soft',
  info: 'border-accent/40 bg-accent-soft',
  neutral: 'border-border bg-surface-2/60',
} as const;

export function SerialDetailPanel({ target, open, onClose, trace, warranty, abilities, onDispose, onOnlyProduct, productFiltered }: SerialDetailPanelProps) {
  const key = target ? `${target.sku ?? ''}|${target.serial}` : null;
  // La pestaña vuelve a «Resumen» al abrir otra unidad.
  const [tabState, setTabState] = useState<{ key: string | null; tab: PanelTab }>({ key, tab: 'resumen' });
  const tab = tabState.key === key ? tabState.tab : 'resumen';

  const data = trace.data;
  const row = data?.serial ?? null;
  const info = warranty.data;
  const events = data ? timeline(data.events) : [];
  const claims = data?.claims ?? [];
  const summary = data ? traceWarranty(data) : null;
  const origin = data ? originText(data) : '';
  const openClaim = row ? openClaimOf(row.serial, info, data) : null;

  const footer = row && (
    <div className="flex flex-col gap-2">
      {openClaim ? (
        <Button variant="outline" leftIcon={<ShieldPlus />} fullWidth to={ROUTES.panelModule(claimPath(openClaim))}>
          Ver el caso {openClaim}
        </Button>
      ) : (
        abilities.openClaim &&
        canOpenClaimFor(row.status) && (
          <Button leftIcon={<ShieldPlus />} fullWidth to={ROUTES.panelModule(openClaimPath(row.serial, row.sku))}>
            Abrir caso de garantía
          </Button>
        )
      )}
      {!productFiltered(row.sku) && (
        <Button variant="outline" leftIcon={<Filter />} fullWidth onClick={() => onOnlyProduct(row.sku)}>
          Ver solo las series de este producto
        </Button>
      )}
      {abilities.sales && info?.customerCode && (
        <Button variant="outline" leftIcon={<Receipt />} fullWidth to={ROUTES.panelModule(customerSalesPath(info.customerCode))}>
          Ver las ventas de {info.customer ?? info.customerCode}
        </Button>
      )}
      {abilities.dispose && canDisposeOf(row.status) && (
        <Button variant="danger" leftIcon={<PackageX />} fullWidth onClick={() => onDispose(row)}>
          Dar destino
        </Button>
      )}
    </div>
  );

  return (
    <SidePanel
      open={open}
      onClose={onClose}
      title={target ? `Serie ${target.serial}` : 'Serie'}
      description={row ? `${row.product} · ${row.sku}` : undefined}
      headerExtra={row && <StatusBadge status={row.status} statuses={SERIAL_STATES} />}
      loading={trace.loading}
      error={trace.error}
      onRetry={trace.reload}
      footer={footer}
    >
      {row && data && summary && (
        <Tabs
          label="Secciones de la unidad"
          value={tab}
          onChange={(next) => setTabState({ key, tab: next })}
          tabs={[
            { id: 'resumen', label: 'Resumen' },
            { id: 'trazabilidad', label: 'Trazabilidad', count: events.length },
            { id: 'casos', label: 'Garantías', count: claims.length },
          ]}
        >
          <TabPanel id="resumen" className="space-y-5">
            <section aria-label="Garantía" className={`rounded-xl border p-3 ${WARRANTY_BOX[summary.tone]}`} data-testid="garantia-de-la-unidad">
              <p className="font-semibold text-text">{summary.title}</p>
              <p className="mt-0.5 text-sm text-text-muted">{summary.detail}</p>
              {openClaim && <p className="mt-1 text-sm font-medium text-warning-text">Tiene el caso de garantía {openClaim} abierto.</p>}
            </section>
            {warranty.error && (
              <p className="text-sm text-text-muted" role="status">
                No se pudo leer la venta vigente: {describePanelError(warranty.error, { operation: 'GetWarrantyStatusQuery' })}
              </p>
            )}
            <DetailList
              items={[
                { label: 'Producto', value: row.product, wide: true },
                { label: 'SKU', value: row.sku },
                { label: 'Tipo', value: <StatusBadge status={row.kind} statuses={SERIAL_KINDS} /> },
                { label: 'Estado', value: <StatusBadge status={row.status} statuses={SERIAL_STATES} /> },
                { label: 'Dónde está', value: locationOf(row) || null },
                { label: 'Ingresó', value: row.receivedAt ? formatDateTime(row.receivedAt) : null },
                { label: 'Origen', value: origin || null },
                { label: 'Vendida', value: row.soldAt ? formatDateTime(row.soldAt) : null },
                { label: 'Venta o factura', value: row.invoiceNumber },
                { label: 'Cliente', value: row.customer ?? info?.customer ?? (row.soldAt ? 'Cliente de otra sucursal' : null) },
                { label: 'Garantía hasta', value: row.warrantyUntil ? formatDate(row.warrantyUntil) : null },
              ]}
            />
          </TabPanel>

          <TabPanel id="trazabilidad">
            {events.length === 0 ? (
              <EmptyState size="sm" icon={<History />} title="Sin hechos registrados" />
            ) : (
              <ol className="space-y-3" aria-label="Línea de tiempo de la unidad">
                {events.map((event, index) => {
                  const detail = eventDetail(event);
                  return (
                    <li key={`${event.occurredAt}-${index}`} className="rounded-xl border border-border bg-surface-2/60 p-3 text-sm">
                      <p className="flex flex-wrap items-center justify-between gap-2">
                        <StatusBadge status={event.action} statuses={EVENT_ACTIONS} />
                        <span className="text-xs text-text-muted tabular-nums">{formatDateTime(event.occurredAt)}</span>
                      </p>
                      {detail && <p className="mt-1 text-xs text-text-muted">{detail}</p>}
                      {event.note && <p className="mt-1 text-text">{event.note}</p>}
                    </li>
                  );
                })}
              </ol>
            )}
          </TabPanel>

          <TabPanel id="casos">
            {claims.length === 0 ? (
              <EmptyState size="sm" icon={<ShieldPlus />} title="Sin casos de garantía" description="La unidad nunca volvió por garantía." />
            ) : (
              <ul className="space-y-3" aria-label="Casos de garantía de la unidad">
                {claims.map((claim) => (
                  <li key={claim.id} className="space-y-1 rounded-xl border border-border bg-surface-2/60 p-3 text-sm">
                    <p className="flex flex-wrap items-center justify-between gap-2">
                      <span className="font-semibold text-text">{claim.number}</span>
                      <StatusBadge status={claim.status} statuses={CLAIM_STATES} />
                    </p>
                    <p className="text-text">{claim.issue}</p>
                    <p className="text-xs text-text-muted">
                      Recibido el {formatDateTime(claim.receivedAt)} · {claim.isInWarranty ? 'en garantía' : 'con cargo'}
                      {claim.serial !== row.serial && ` · esta unidad fue el reemplazo de ${claim.serial}`}
                    </p>
                    <Button variant="outline" leftIcon={<ShieldPlus />} to={ROUTES.panelModule(claimPath(claim.number))}>
                      Ver el caso
                    </Button>
                  </li>
                ))}
              </ul>
            )}
          </TabPanel>
        </Tabs>
      )}
    </SidePanel>
  );
}
