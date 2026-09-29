// Confirmación de la reserva: el número bien visible (con «Copiar»), el estado, hasta cuándo se guarda, el detalle con el
// total, dónde se recoge, «Te enviamos un correo a …» SOLO si el servidor dejó el correo en cola, y los enlaces a «Mi
// reserva» y al catálogo. El foco va al título al aparecer.

import { BadgeCheck, CalendarClock, Check, Copy, Mail, MapPin, Search } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { ivaBreakdown } from '@/1-domain/catalog/money';
import type { Reservation, StoreInfo } from '@/1-domain/storefront/types';
import { ROUTES } from '@/4-presentation/app/routes';
import { ReservationKindBadge, ReservationStatusBadge } from '@/4-presentation/components/reservation/ReservationSummary';
import { branchName, formatDateTime, heldHoursText } from '@/4-presentation/components/reservation/reservationText';
import { Alert } from '@/4-presentation/components/ui/Alert';
import { Button } from '@/4-presentation/components/ui/Button';
import { Card } from '@/4-presentation/components/ui/Card';
import { Container } from '@/4-presentation/components/ui/Container';
import { formatMoney } from '@/shared/format';
import { productsLabel } from './checkoutText';

export interface CheckoutConfirmationProps {
  reservation: Reservation;
  store: StoreInfo;
  /** Correo al que el servidor envía la confirmación (el del formulario o el de la cuenta). */
  email: string | null;
  /** Reservó con su cuenta: «Mi reserva» lleva a «Mis reservas». */
  withAccount: boolean;
}

type CopyState = 'idle' | 'copied' | 'failed';

export function CheckoutConfirmation({ reservation, store, email, withAccount }: CheckoutConfirmationProps) {
  const headingRef = useRef<HTMLHeadingElement>(null);
  const [copy, setCopy] = useState<CopyState>('idle');
  const count = reservation.lines.reduce((total, line) => total + line.quantity, 0);
  const hours = heldHoursText(reservation);
  const breakdown = ivaBreakdown(reservation.total);

  useEffect(() => {
    headingRef.current?.focus();
  }, []);

  const copyNumber = async () => {
    try {
      await navigator.clipboard.writeText(reservation.number);
      setCopy('copied');
    } catch {
      setCopy('failed');
    }
  };

  return (
    <Container className="py-6 sm:py-8 lg:py-10">
      <div className="mx-auto max-w-3xl" data-testid="reserva-confirmada">
        <div className="flex items-start gap-3">
          <span aria-hidden="true" className="mt-1 flex size-10 shrink-0 items-center justify-center rounded-xl bg-success-soft text-success-text">
            <BadgeCheck className="size-6" />
          </span>
          <div className="min-w-0">
            <h1 ref={headingRef} tabIndex={-1} className="text-3xl outline-none sm:text-4xl">
              Tu reserva quedó registrada
            </h1>
            <p className="mt-2 text-base text-text-muted">
              {reservation.replayed
                ? 'Esta reserva ya estaba registrada con los mismos datos: no se reservó dos veces.'
                : `Te guardamos ${reservation.kind === 'build' ? 'las piezas' : count === 1 ? 'el producto' : 'los productos'} a tu nombre. Pasá por la tienda antes del vencimiento: pagás al retirar.`}
            </p>
          </div>
        </div>

        <Card as="section" aria-labelledby="confirmacion-numero-titulo" padding="md" className="mt-6">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <h2 id="confirmacion-numero-titulo" className="text-xs font-semibold uppercase tracking-wide text-text-faint">
              Número de reserva
            </h2>
            <div className="flex flex-wrap items-center gap-2">
              <ReservationKindBadge reservation={reservation} />
              <ReservationStatusBadge reservation={reservation} />
            </div>
          </div>
          <div className="mt-2 flex flex-wrap items-center gap-3">
            <p className="font-display text-3xl font-semibold tracking-tight break-all text-text sm:text-4xl" data-testid="confirmacion-numero">
              {reservation.number}
            </p>
            <Button variant="outline" size="sm" leftIcon={copy === 'copied' ? <Check /> : <Copy />} onClick={() => void copyNumber()} aria-label={`Copiar el número ${reservation.number}`}>
              {copy === 'copied' ? 'Copiado' : 'Copiar'}
            </Button>
          </div>
          <p role="status" aria-live="polite" className="mt-1 min-h-5 text-xs text-text-muted">
            {copy === 'copied' ? 'Número copiado.' : copy === 'failed' ? 'No pudimos copiarlo: seleccioná el número y copialo a mano.' : ''}
          </p>

          <dl className="mt-3 grid gap-3 text-sm sm:grid-cols-2">
            <div className="flex items-start gap-2.5">
              <CalendarClock aria-hidden="true" className="mt-0.5 size-4 shrink-0 text-accent" />
              <div className="min-w-0">
                <dt className="text-xs text-text-faint">Te la guardamos hasta</dt>
                <dd className="font-medium text-text" data-testid="confirmacion-vence">
                  {formatDateTime(reservation.reservedUntil)}
                  {hours && <span className="block text-xs font-normal text-text-muted">({hours} desde que reservaste)</span>}
                </dd>
              </div>
            </div>
            <div className="flex items-start gap-2.5">
              <MapPin aria-hidden="true" className="mt-0.5 size-4 shrink-0 text-accent" />
              <div className="min-w-0">
                <dt className="text-xs text-text-faint">Retirás y pagás en</dt>
                <dd className="font-medium text-text">{branchName(reservation, store)}</dd>
              </div>
            </div>
            <div className="min-w-0 sm:col-span-2">
              <dt className="text-xs text-text-faint">A nombre de</dt>
              <dd className="font-medium break-words text-text">{reservation.contactName}</dd>
            </div>
          </dl>

          <div className="mt-4">
            {reservation.mailQueued && email ? (
              <Alert tone="success" title={`Te enviamos un correo a ${email}`}>
                Con el código y el detalle de tu reserva. Si no lo ves en unos minutos, revisá la carpeta de correo no deseado.
              </Alert>
            ) : (
              <Alert tone="info" title="Guardá este número">
                {withAccount
                  ? 'También la ves en «Mis reservas» de tu cuenta.'
                  : 'Con el número y tu teléfono consultás o liberás tu reserva cuando quieras.'}
              </Alert>
            )}
          </div>
        </Card>

        <Card as="section" aria-labelledby="confirmacion-detalle" padding="md" className="mt-4">
          <h2 id="confirmacion-detalle" className="font-display text-xl font-semibold text-text">
            Detalle
          </h2>
          <table className="mt-3 w-full text-sm">
            <caption className="sr-only">Productos de la reserva {reservation.number}</caption>
            <thead>
              <tr className="text-left text-[0.6875rem] font-semibold uppercase tracking-wide text-text-faint">
                <th scope="col" className="pb-2 font-semibold">
                  Producto
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
              {reservation.lines.map((line, index) => (
                <tr key={`${line.sku}-${index}`}>
                  <td className="py-2.5 pr-3">
                    <p className="leading-snug break-words text-text">{line.name}</p>
                    <p className="text-xs text-text-faint">
                      <span className="font-mono break-all">{line.sku}</span>
                      {line.quantity > 1 && (
                        <span>
                          {' '}
                          · {line.quantity} × {formatMoney(line.unitPrice)}
                        </span>
                      )}
                    </p>
                  </td>
                  <td className="py-2.5 text-center text-text-muted tabular-nums">{line.quantity}</td>
                  <td className="py-2.5 text-right font-medium whitespace-nowrap text-text tabular-nums">{formatMoney(line.subtotal)}</td>
                </tr>
              ))}
            </tbody>
            <tfoot>
              <tr className="border-t border-border">
                <th scope="row" colSpan={2} className="pt-3 text-left font-medium text-text-muted">
                  Total ({productsLabel(count)}) · IVA incluido {formatMoney(breakdown.iva)}
                </th>
                <td className="pt-3 text-right font-display text-xl font-semibold whitespace-nowrap text-text tabular-nums" data-testid="confirmacion-total">
                  {formatMoney(reservation.total)}
                </td>
              </tr>
            </tfoot>
          </table>
          {reservation.notes && (
            <p className="mt-3 text-sm text-text-muted">
              <span className="font-medium text-text">Notas:</span> {reservation.notes}
            </p>
          )}
        </Card>

        <div className="mt-6 flex flex-wrap gap-3">
          <Button to={withAccount ? ROUTES.accountSection('reservas') : ROUTES.reservation(reservation.number)} variant="brand" leftIcon={<Search />}>
            Mi reserva
          </Button>
          <Button to={ROUTES.catalog} variant="outline">
            Seguir en el catálogo
          </Button>
        </div>
        {!withAccount && (
          <p className="mt-4 flex items-start gap-2 text-sm text-text-muted">
            <Mail aria-hidden="true" className="mt-0.5 size-4 shrink-0 text-accent" />
            <span>¿Querés ver todas tus reservas en un solo lugar? Creá una cuenta con el mismo correo la próxima vez.</span>
          </p>
        )}
      </div>
    </Container>
  );
}
