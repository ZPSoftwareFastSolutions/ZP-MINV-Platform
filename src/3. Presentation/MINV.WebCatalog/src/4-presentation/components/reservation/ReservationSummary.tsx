// Resumen de una reserva tal como la devuelve la tienda: estado, número, vigencia, piezas a precios congelados, total
// y sucursal de retiro. Lo comparten la confirmación del armador y la página «Consultar mi reserva».

import { CalendarClock, MapPin, MessageCircle, Search, TriangleAlert } from 'lucide-react';
import type { StoreInfo, Reservation, ReservationStatus } from '@/1-domain/storefront/types';
import { ivaBreakdown } from '@/1-domain/catalog/money';
import { ROUTES } from '@/4-presentation/app/routes';
import { Badge, type BadgeTone } from '@/4-presentation/components/ui/Badge';
import { Button } from '@/4-presentation/components/ui/Button';
import { formatMoney, pluralize } from '@/shared/format';
import { branchName, formatDateTime, whatsappReservationUrl } from './reservationText';

const STATUS_TONE: Record<ReservationStatus, BadgeTone> = {
  Reserved: 'exito',
  Sold: 'nuevo',
  Cancelled: 'neutral',
  Expired: 'agotado',
};

export function ReservationStatusBadge({ reservation, size = 'md' }: { reservation: Pick<Reservation, 'status' | 'statusText'>; size?: 'sm' | 'md' }) {
  return (
    <Badge tone={STATUS_TONE[reservation.status]} variant="solid" size={size}>
      {reservation.statusText}
    </Badge>
  );
}

export interface ReservationSummaryProps {
  reservation: Reservation;
  store?: StoreInfo;
  /** Muestra los enlaces «Consultar mi reserva» y WhatsApp (en la confirmación del armador). */
  withLinks?: boolean;
}

export function ReservationSummary({ reservation, store, withLinks = false }: ReservationSummaryProps) {
  const breakdown = ivaBreakdown(reservation.total);
  const count = reservation.lines.reduce((acc, line) => acc + line.quantity, 0);
  const active = reservation.status === 'Reserved';

  return (
    <div className="space-y-5" data-testid="resumen-reserva">
      <div className="rounded-xl border border-border bg-surface-2 p-4">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <p className="text-xs font-semibold uppercase tracking-wide text-text-faint">Número de reserva</p>
          <ReservationStatusBadge reservation={reservation} />
        </div>
        <p className="mt-1 font-display text-2xl font-semibold tracking-tight text-text">
          <span className="sr-only">Reserva </span>
          <span data-testid="numero-reserva">{reservation.number}</span>
        </p>
        <dl className="mt-3 space-y-2 text-sm">
          <div className="flex items-start gap-2.5 text-text-muted">
            <CalendarClock aria-hidden="true" className="mt-0.5 size-4 shrink-0 text-accent" />
            <dt className="shrink-0">{active ? 'Vence:' : 'Vencía:'}</dt>
            <dd className="font-medium text-text">{formatDateTime(reservation.reservedUntil)}</dd>
          </div>
          <div className="flex items-start gap-2.5 text-text-muted">
            <MapPin aria-hidden="true" className="mt-0.5 size-4 shrink-0 text-accent" />
            <dt className="shrink-0">Retiro:</dt>
            <dd className="font-medium text-text">{branchName(reservation, store)}</dd>
          </div>
          <div className="flex items-start gap-2.5 text-text-muted">
            <dt className="shrink-0">A nombre de:</dt>
            <dd className="font-medium text-text">{reservation.contactName}</dd>
          </div>
          {reservation.notes && (
            <div className="flex items-start gap-2.5 text-text-muted">
              <dt className="shrink-0">Notas:</dt>
              <dd className="text-text">{reservation.notes}</dd>
            </div>
          )}
          {reservation.cancelReason && (
            <div className="flex items-start gap-2.5 text-text-muted">
              <dt className="shrink-0">Motivo:</dt>
              <dd className="text-text">{reservation.cancelReason}</dd>
            </div>
          )}
        </dl>
      </div>

      {reservation.hasCompatibilityWarnings && (
        <p className="flex items-start gap-2 rounded-xl border border-warning/40 bg-warning-soft p-3 text-sm text-warning-text">
          <TriangleAlert aria-hidden="true" className="mt-0.5 size-4 shrink-0" />
          <span>La ficha técnica encontró posibles incompatibilidades entre las piezas; un técnico de la tienda las revisa antes de confirmar.</span>
        </p>
      )}

      <table className="w-full text-sm">
        <caption className="sr-only">Piezas de la reserva {reservation.number}</caption>
        <thead>
          <tr className="text-left text-[0.6875rem] font-semibold uppercase tracking-wide text-text-faint">
            <th scope="col" className="pb-2 font-semibold">
              Pieza
            </th>
            <th scope="col" className="pb-2 text-center font-semibold">
              Cant.
            </th>
            <th scope="col" className="pb-2 text-right font-semibold">
              Subtotal
            </th>
          </tr>
        </thead>
        <tbody className="divide-y divide-border">
          {reservation.lines.map((line) => (
            <tr key={`${line.sku}-${line.slot}`}>
              <td className="py-2.5 pr-3">
                <p className="line-clamp-2 leading-snug text-text">{line.name}</p>
                <p className="text-xs text-text-faint">
                  <span className="font-mono">{line.sku}</span>
                  {line.quantity > 1 && <span> · {line.quantity} × {formatMoney(line.unitPrice)}</span>}
                </p>
              </td>
              <td className="py-2.5 text-center text-text-muted tabular-nums">{line.quantity}</td>
              <td className="py-2.5 text-right font-medium text-text tabular-nums">{formatMoney(line.subtotal)}</td>
            </tr>
          ))}
        </tbody>
        <tfoot>
          <tr className="border-t border-border">
            <th scope="row" colSpan={2} className="pt-3 text-left font-medium text-text-muted">
              Total ({pluralize(count, 'pieza', 'piezas')}) · IVA incluido {formatMoney(breakdown.iva)}
            </th>
            <td className="pt-3 text-right font-display text-xl font-semibold text-text tabular-nums">{formatMoney(reservation.total)}</td>
          </tr>
        </tfoot>
      </table>

      {withLinks && (
        <div className="flex flex-wrap gap-2">
          <Button to={ROUTES.reservation(reservation.number)} variant="outline" leftIcon={<Search />}>
            Consultar mi reserva
          </Button>
          <Button href={whatsappReservationUrl(reservation)} target="_blank" rel="noreferrer noopener" variant="accent" leftIcon={<MessageCircle />}>
            Avisar por WhatsApp
          </Button>
        </div>
      )}
    </div>
  );
}
