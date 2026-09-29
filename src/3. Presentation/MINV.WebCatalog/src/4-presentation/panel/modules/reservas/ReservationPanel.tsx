// Módulo «Reservas» · DETALLE lateral de una reserva (`GetPcBuildQuery` y su cola de correos `GetOutgoingMailsQuery`):
// resumen (tipo, canal, contacto con Copiar teléfono y Abrir WhatsApp, plazo, total, cierre y notas), datos para la
// factura, productos con su disponibilidad, bitácora y correos. Al pie, las acciones que la sesión puede usar: Vender en
// caja, Reenviar correo y Liberar reserva. Las consultas las hace la pantalla (así las recarga después de un comando).

import { Copy, History, Mail, MailPlus, MessageCircle, ShoppingCart, LockOpen } from 'lucide-react';
import { useState } from 'react';
import { ROUTES } from '@/4-presentation/app/routes';
import type { RpcQuery } from '@/4-presentation/panel/hooks';
import { Button, DetailList, EmptyState, ErrorState, SidePanel, StatusBadge, TabPanel, Tabs } from '@/4-presentation/panel/kit';
import { formatDate, formatDateTime, formatMoney, formatQuantity } from '@/4-presentation/panel/lib';
import {
  CHANNELS,
  KINDS,
  MAIL_STATES,
  STATES,
  attemptsText,
  availabilityText,
  canBeSold,
  documentTypeLabel,
  formatPhone,
  historyAction,
  holdInfo,
  reservationState,
  sellPath,
  slotLabel,
  whatsappUrl,
  type BuildDetailData,
  type BuildRecord,
  type MailRecord,
  type ReservationItem,
} from './reservations';

type PanelTab = 'resumen' | 'productos' | 'bitacora' | 'correo';

export interface ReservationPanelProps {
  /** Número de la reserva abierta (null = ninguna). */
  number: string | null;
  open: boolean;
  onClose: () => void;
  /** La fila de la lista, si está cargada (su estado de correo se usa mientras llegan los correos). */
  item: ReservationItem | null;
  detail: RpcQuery<BuildDetailData>;
  mails: RpcQuery<MailRecord[]>;
  now: Date;
  /** Qué acciones puede usar la sesión. */
  allowed: { sell: boolean; release: boolean; resend: boolean };
  onRelease: (row: BuildRecord) => void;
  onResend: (row: BuildRecord) => void;
  onCopyPhone: (phone: string) => void;
}

const HOLD_TONES = { normal: 'text-text', soon: 'font-semibold text-warning-text', expired: 'font-semibold text-danger-text', none: 'text-text' } as const;
const AVAILABILITY_TONES = { accent: 'text-primary-text', success: 'text-success-text', danger: 'text-danger-text' } as const;

export function ReservationPanel({ number, open, onClose, item, detail, mails, now, allowed, onRelease, onResend, onCopyPhone }: ReservationPanelProps) {
  // La pestaña vuelve a «Resumen» al abrir otra reserva.
  const [tabState, setTabState] = useState<{ number: string | null; tab: PanelTab }>({ number, tab: 'resumen' });
  const tab = tabState.number === number ? tabState.tab : 'resumen';

  const data = detail.data;
  const row = data?.build ?? item?.row ?? null;
  const state = row ? reservationState(row, now) : null;
  const hold = row ? holdInfo(row, now) : null;
  const lines = data?.quotedItems ?? [];
  const history = data?.history ?? [];
  const mailList = mails.data ?? [];
  const phone = formatPhone(row?.contactPhone);
  const whatsapp = whatsappUrl(row?.contactPhone);
  const showAvailability = row?.status === 'Reserved' || row?.status === 'Quoted';

  const footer = row && (
    <div className="flex flex-col gap-2">
      {allowed.sell && canBeSold(row) && (
        <Button to={ROUTES.panelModule(sellPath(row.number))} leftIcon={<ShoppingCart />} fullWidth>
          Vender en caja
        </Button>
      )}
      {allowed.resend && state === 'reservada' && (
        <Button variant="outline" leftIcon={<MailPlus />} fullWidth onClick={() => onResend(row)}>
          Reenviar correo
        </Button>
      )}
      {allowed.release && row.status === 'Reserved' && (
        <Button variant="danger" leftIcon={<LockOpen />} fullWidth onClick={() => onRelease(row)}>
          Liberar reserva
        </Button>
      )}
    </div>
  );

  return (
    <SidePanel
      open={open}
      onClose={onClose}
      title={number ? `Reserva ${number}` : 'Reserva'}
      description={row?.name}
      headerExtra={state && <StatusBadge status={state} statuses={STATES} />}
      loading={detail.loading}
      error={detail.error}
      onRetry={detail.reload}
      footer={footer}
    >
      {row && (
        <Tabs
          label="Secciones de la reserva"
          value={tab}
          onChange={(next) => setTabState({ number, tab: next })}
          tabs={[
            { id: 'resumen', label: 'Resumen' },
            { id: 'productos', label: 'Productos', count: lines.length },
            { id: 'bitacora', label: 'Bitácora', count: history.length },
            { id: 'correo', label: 'Correo', count: mails.data ? mailList.length : undefined },
          ]}
        >
          <TabPanel id="resumen" className="space-y-5">
            <DetailList
              items={[
                { label: 'Tipo', value: <StatusBadge status={row.kind} statuses={KINDS} /> },
                { label: 'Canal', value: <StatusBadge status={row.channel} statuses={CHANNELS} /> },
                { label: 'Sucursal', value: row.branchCode },
                { label: 'Cliente registrado', value: row.customer },
                { label: 'Quién la recoge', value: row.contactName },
                {
                  label: 'Teléfono',
                  value: phone && (
                    <span className="flex flex-col gap-2">
                      <span className="tabular-nums">{phone}</span>
                      <span className="flex flex-wrap gap-2">
                        <Button variant="outline" leftIcon={<Copy />} onClick={() => onCopyPhone(phone)}>
                          Copiar teléfono
                        </Button>
                        {whatsapp && (
                          <Button variant="outline" leftIcon={<MessageCircle />} href={whatsapp} target="_blank" rel="noopener noreferrer">
                            Abrir WhatsApp
                          </Button>
                        )}
                      </span>
                    </span>
                  ),
                  wide: true,
                },
                { label: 'Correo de contacto', value: row.contactEmail },
                { label: 'Creada', value: formatDateTime(row.createdAt) },
                { label: 'Reservado hasta', value: hold && <span className={HOLD_TONES[hold.tone]}>{hold.text}</span> },
                { label: 'Precios vigentes hasta', value: formatDate(row.validUntil) },
                { label: 'Total', value: formatMoney(row.total) },
                { label: 'Unidades reservadas', value: row.status === 'Reserved' ? formatQuantity(row.reserved) : null },
                { label: 'Venta', value: row.invoiceNumber },
                { label: 'Motivo del cierre', value: row.cancelReason },
                { label: 'Notas del cliente', value: row.notes, wide: true },
              ]}
            />
            <section aria-label="Datos para la factura" className="rounded-xl border border-border bg-surface-2/60 p-3">
              <h3 className="text-sm font-semibold text-text">Datos para la factura</h3>
              {row.buyerDocumentType != null ? (
                <DetailList
                  className="mt-2"
                  items={[
                    { label: 'Documento', value: documentTypeLabel(row.buyerDocumentType) },
                    { label: 'Número', value: row.buyerDocumentNumber },
                    { label: 'Complemento', value: row.buyerComplement },
                    { label: 'Nombre o razón social', value: row.buyerName },
                  ]}
                />
              ) : (
                <p className="mt-1 text-sm text-text-muted">No dejó datos de factura: el cajero los pide al cobrar.</p>
              )}
            </section>
          </TabPanel>

          <TabPanel id="productos">
            {lines.length === 0 ? (
              <EmptyState size="sm" title="Sin productos" description="La reserva no tiene líneas." />
            ) : (
              <ul className="divide-y divide-border" aria-label="Productos de la reserva">
                {lines.map((line, index) => {
                  const availability = availabilityText(line, row.status === 'Reserved');
                  return (
                    <li key={`${line.slot ?? ''}-${line.sku}-${index}`} className="py-3">
                      <div className="flex justify-between gap-3">
                        <span className="min-w-0">
                          <span className="block font-medium break-words text-text">{line.name}</span>
                          <span className="block text-xs text-text-muted">
                            {row.kind === 'Build' ? `${slotLabel(line.slot)} · ` : ''}
                            {line.sku} · {formatQuantity(line.quantity)} × {formatMoney(line.unitPrice)}
                          </span>
                        </span>
                        <span className="shrink-0 font-semibold text-text tabular-nums">{formatMoney(line.subtotal)}</span>
                      </div>
                      {showAvailability && <p className={`mt-1 text-xs font-medium ${AVAILABILITY_TONES[availability.tone]}`}>{availability.text}</p>}
                    </li>
                  );
                })}
                <li className="flex justify-between gap-3 py-3 font-semibold">
                  <span>Total (precios congelados)</span>
                  <span className="tabular-nums">{formatMoney(row.total)}</span>
                </li>
              </ul>
            )}
          </TabPanel>

          <TabPanel id="bitacora">
            {history.length === 0 ? (
              <EmptyState size="sm" icon={<History />} title="Sin hechos registrados" />
            ) : (
              <ol className="space-y-3" aria-label="Bitácora de la reserva">
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

          <TabPanel id="correo" className="space-y-3">
            {mails.error ? (
              <ErrorState error={mails.error} operation="GetOutgoingMailsQuery" onRetry={mails.reload} retrying={mails.fetching} />
            ) : !mails.data ? (
              <p className="text-sm text-text-muted" role="status">
                Cargando los correos…
              </p>
            ) : mailList.length === 0 ? (
              <EmptyState
                size="sm"
                icon={<Mail />}
                title="No hay correos de esta reserva"
                description={row.contactEmail ? 'Puede reenviar la confirmación mientras la reserva esté vigente.' : 'La reserva no dejó correo de contacto.'}
              />
            ) : (
              <ul className="space-y-3" aria-label="Correos de la reserva">
                {mailList.map((mail) => (
                  <li key={mail.id} className="space-y-1 rounded-xl border border-border bg-surface-2/60 p-3 text-sm">
                    <p className="flex flex-wrap items-center justify-between gap-2">
                      <span className="font-semibold break-all text-text">{mail.recipient}</span>
                      <StatusBadge status={mail.status} statuses={MAIL_STATES} />
                    </p>
                    <p className="text-xs text-text-muted">
                      {mail.kindText} · {attemptsText(mail)}
                    </p>
                    <p className="text-xs text-text-muted">
                      Pedido el {formatDateTime(mail.requestedAt)}
                      {mail.lastAttemptAt && ` · último intento ${formatDateTime(mail.lastAttemptAt)}`}
                      {mail.completedAt && ` · cerrado ${formatDateTime(mail.completedAt)}`}
                      {mail.nextAttemptAt && ` · próximo intento ${formatDateTime(mail.nextAttemptAt)}`}
                    </p>
                    {mail.lastError && <p className="text-xs text-danger-text">Último error: {mail.lastError}</p>}
                  </li>
                ))}
              </ul>
            )}
            {allowed.resend && state === 'reservada' && (
              <Button variant="outline" leftIcon={<MailPlus />} onClick={() => onResend(row)}>
                Reenviar correo
              </Button>
            )}
          </TabPanel>
        </Tabs>
      )}
    </SidePanel>
  );
}
