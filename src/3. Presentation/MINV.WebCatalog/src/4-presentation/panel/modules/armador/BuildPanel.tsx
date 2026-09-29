// Módulo «Armador de PC» · DETALLE lateral de un armado desde la lista (`GetPcBuildQuery`): resumen (estado, canal,
// cliente o contacto de la reserva web, vigencia, plazo de la reserva y cierre), piezas a sus precios cotizados con la
// disponibilidad, compatibilidad según el servidor y bitácora. Al pie: «Abrir en el armador» y «Vender en caja». La
// consulta la hace la lista (así la recarga después de un comando).

import { Cpu, History, ShoppingCart } from 'lucide-react';
import { useState } from 'react';
import { ROUTES } from '@/4-presentation/app/routes';
import type { RpcQuery } from '@/4-presentation/panel/hooks';
import { Button, DetailList, EmptyState, SidePanel, StatusBadge, TabPanel, Tabs } from '@/4-presentation/panel/kit';
import { formatDateTime, formatMoney, formatQuantity } from '@/4-presentation/panel/lib';
import { IssueList } from './IssueList';
import {
  BUILD_STATES,
  CHANNELS,
  buildNote,
  buildState,
  builderPath,
  canSellBuild,
  compatibilityText,
  historyAction,
  sellPath,
  slotLabel,
  validityText,
  type BuildDetailData,
  type BuildRecord,
} from './builder';

type PanelTab = 'resumen' | 'piezas' | 'compatibilidad' | 'bitacora';

export interface BuildPanelProps {
  number: string | null;
  open: boolean;
  onClose: () => void;
  /** La fila de la lista (se ve mientras llega el detalle). */
  row: BuildRecord | null;
  detail: RpcQuery<BuildDetailData>;
  now: Date;
  /** ¿La sesión puede cobrar en la caja? */
  canSell: boolean;
}

const COMPATIBILITY_TONES = { success: 'text-success-text', warning: 'text-warning-text', danger: 'text-danger-text' } as const;

export function BuildPanel({ number, open, onClose, row: listRow, detail, now, canSell }: BuildPanelProps) {
  const [tabState, setTabState] = useState<{ number: string | null; tab: PanelTab }>({ number, tab: 'resumen' });
  const tab = tabState.number === number ? tabState.tab : 'resumen';
  const data = detail.data;
  const row = data?.build ?? listRow;
  const state = row ? buildState(row, now) : null;
  const items = data?.quotedItems ?? [];
  const history = data?.history ?? [];
  const compatibility = row ? compatibilityText(row) : null;

  return (
    <SidePanel
      open={open}
      onClose={onClose}
      title={number ? `Armado ${number}` : 'Armado'}
      description={row?.name}
      headerExtra={state && <StatusBadge status={state} statuses={BUILD_STATES} />}
      loading={detail.loading}
      error={detail.error}
      onRetry={detail.reload}
      footer={
        row && (
          <div className="flex flex-col gap-2">
            <Button to={ROUTES.panelModule(builderPath(row.number))} leftIcon={<Cpu />} fullWidth>
              Abrir en el armador
            </Button>
            {canSell && canSellBuild(row) && (
              <Button variant="outline" to={ROUTES.panelModule(sellPath(row.number))} leftIcon={<ShoppingCart />} fullWidth>
                Vender en caja
              </Button>
            )}
          </div>
        )
      }
    >
      {row && (
        <Tabs
          label="Secciones del armado"
          value={tab}
          onChange={(next) => setTabState({ number, tab: next })}
          tabs={[
            { id: 'resumen', label: 'Resumen' },
            { id: 'piezas', label: 'Piezas', count: items.length },
            { id: 'compatibilidad', label: 'Compatibilidad', count: data ? data.check.issues.length : undefined },
            { id: 'bitacora', label: 'Bitácora', count: history.length },
          ]}
        >
          <TabPanel id="resumen" className="space-y-4">
            <p className="text-sm text-text-muted">{buildNote(row, now)}</p>
            <DetailList
              items={[
                { label: 'Canal', value: <StatusBadge status={row.channel} statuses={CHANNELS} /> },
                { label: 'Sucursal', value: row.branchCode },
                { label: 'Cliente', value: row.customer },
                { label: 'Contacto de la reserva', value: row.contactName },
                { label: 'Teléfono', value: row.contactPhone },
                { label: 'Correo', value: row.contactEmail },
                { label: 'Creado', value: formatDateTime(row.createdAt) },
                { label: 'Vigencia o venta', value: validityText(row) },
                { label: 'Reservado hasta', value: row.status === 'Reserved' && row.reservedUntil ? formatDateTime(row.reservedUntil) : null },
                { label: 'Unidades reservadas', value: row.status === 'Reserved' ? formatQuantity(row.reserved) : null },
                { label: 'Compatibilidad', value: compatibility && <span className={COMPATIBILITY_TONES[compatibility.tone]}>{compatibility.text}</span> },
                { label: 'Publicado en la web', value: row.publishedToWeb ? 'Sí, como armado sugerido' : 'No' },
                { label: 'Piezas', value: formatQuantity(row.items) },
                { label: 'Total', value: formatMoney(row.total) },
                { label: 'Motivo del cierre', value: row.cancelReason },
                { label: 'Notas del cliente', value: row.notes, wide: true },
              ]}
            />
          </TabPanel>

          <TabPanel id="piezas">
            {items.length === 0 ? (
              <EmptyState size="sm" title="Sin piezas" />
            ) : (
              <ul className="divide-y divide-border" aria-label="Piezas del armado">
                {items.map((item, index) => (
                  <li key={`${item.slot ?? ''}-${item.sku}-${index}`} className="flex justify-between gap-3 py-3">
                    <span className="min-w-0">
                      <span className="block text-xs font-semibold tracking-wide text-text-faint uppercase">{slotLabel(item.slot)}</span>
                      <span className="block font-medium break-words text-text">{item.name}</span>
                      <span className="block text-xs text-text-muted">
                        {item.sku} · {formatQuantity(item.quantity)} × {formatMoney(item.unitPrice)} · {item.stock > 0 ? `stock ${formatQuantity(item.stock)}` : 'sin stock'}
                      </span>
                    </span>
                    <span className="shrink-0 font-semibold text-text tabular-nums">{formatMoney(item.subtotal)}</span>
                  </li>
                ))}
                <li className="flex justify-between gap-3 py-3 font-semibold">
                  <span>{row.status === 'Draft' ? 'Total (lista de precios vigente)' : 'Total (precios cotizados)'}</span>
                  <span className="tabular-nums">{formatMoney(row.total)}</span>
                </li>
              </ul>
            )}
          </TabPanel>

          <TabPanel id="compatibilidad">
            {data && <IssueList check={data.check} accepted={row.quotedWithErrors} />}
          </TabPanel>

          <TabPanel id="bitacora">
            {history.length === 0 ? (
              <EmptyState size="sm" icon={<History />} title="Sin hechos registrados" />
            ) : (
              <ol className="space-y-3" aria-label="Bitácora del armado">
                {history.map((event, index) => (
                  <li key={`${event.occurredAt}-${index}`} className="rounded-xl border border-border bg-surface-2/60 p-3 text-sm">
                    <p className="flex flex-wrap items-baseline justify-between gap-x-3">
                      <span className="font-semibold text-text">{historyAction(event.action)}</span>
                      <span className="text-xs text-text-muted tabular-nums">{formatDateTime(event.occurredAt)}</span>
                    </p>
                    {event.detail && <p className="mt-0.5 text-text">{event.detail}</p>}
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
