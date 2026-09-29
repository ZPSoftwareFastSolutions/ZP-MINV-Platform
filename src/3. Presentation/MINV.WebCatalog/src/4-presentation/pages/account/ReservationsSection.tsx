// «Mis reservas»: las reservas del cliente de la sesión (armados y carritos) con número, fecha, estado con color, total
// y hasta cuándo se guarda; filtro por estado con lista desplegable; detalle con sus productos; y «Liberar mi reserva»
// con confirmación. Estados de carga, vacío y error con «Reintentar». El servidor devuelve SOLO las reservas de la
// cuenta que ingresó: la web no envía ningún identificador de cliente (regla P-04). V7: cada reserva dice si es un
// armado o una compra (carrito) y cuántas horas se guarda de verdad.

import { CalendarClock, ChevronDown, LockOpen, MapPin, RotateCcw, ShoppingBag, TicketCheck } from 'lucide-react';
import { useEffect, useId, useRef, useState } from 'react';
import { asWebApiError, describeWebApiError } from '@/1-domain/auth/errors';
import { isReservationActive, type Reservation } from '@/1-domain/storefront/types';
import { ALL_STATUSES, AttemptKey, filterReservations, isReservationStatusFilter, RESERVATION_STATUS_OPTIONS, type ReservationStatusFilter } from '@/2-application';
import { ROUTES } from '@/4-presentation/app/routes';
import { ErrorState, LoadingState } from '@/4-presentation/components/feedback/AsyncState';
import { ReservationKindBadge, ReservationStatusBadge } from '@/4-presentation/components/reservation/ReservationSummary';
import { branchName, formatDateTime, heldHoursText, reservationItemsLabel } from '@/4-presentation/components/reservation/reservationText';
import { Button } from '@/4-presentation/components/ui/Button';
import { Card } from '@/4-presentation/components/ui/Card';
import { EmptyState } from '@/4-presentation/components/ui/EmptyState';
import { SelectField } from '@/4-presentation/components/ui/TextField';
import { useAsyncData } from '@/4-presentation/hooks/useAsyncData';
import { useAccount } from '@/4-presentation/hooks/useRpc';
import { useStore } from '@/4-presentation/hooks/useStore';
import { useToast } from '@/4-presentation/hooks/useToast';
import { formatMoney, pluralize } from '@/shared/format';

function heldUntilLabel(reservation: Reservation): string {
  if (reservation.status === 'Reserved') return 'Te la guardamos hasta';
  if (reservation.status === 'Sold') return 'Se guardaba hasta';
  return 'Se guardó hasta';
}

interface ReservationCardProps {
  reservation: Reservation;
  onReleased: (updated: Reservation) => void;
  /** La reserva ya no estaba activa en el servidor: hay que volver a leer la lista. */
  onStale: () => void;
}

function ReservationCard({ reservation, onReleased, onStale }: ReservationCardProps) {
  const account = useAccount();
  const store = useStore();
  const toast = useToast();
  const detailId = useId();
  const [open, setOpen] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const [releasing, setReleasing] = useState(false);
  const [attempt] = useState(() => new AttemptKey());
  const keepRef = useRef<HTMLButtonElement>(null);
  const count = reservation.lines.reduce((total, line) => total + line.quantity, 0);
  const active = isReservationActive(reservation);
  const hours = heldHoursText(reservation);

  useEffect(() => {
    if (confirming) keepRef.current?.focus();
  }, [confirming]);

  const release = async () => {
    setReleasing(true);
    try {
      const updated = await attempt.run((requestId) => account.release(reservation.number, { requestId }));
      setConfirming(false);
      onReleased(updated);
      toast.notify({ tone: 'success', title: 'Reserva liberada', description: `${updated.number}: los productos vuelven a estar disponibles.` });
    } catch (error) {
      const failure = asWebApiError(error);
      // Red caída: la confirmación sigue abierta para reintentar con el mismo pedido.
      if (failure.kind !== 'network') setConfirming(false);
      toast.notify({ tone: 'danger', title: 'No pudimos liberar la reserva', description: describeWebApiError(failure), duration: 8000 });
      // Ya no estaba reservada (se vendió, venció o se canceló): se muestra su estado real.
      if (failure.kind === 'domain' || failure.kind === 'not_found' || failure.kind === 'concurrency') onStale();
    } finally {
      setReleasing(false);
    }
  };

  return (
    <Card as="li" padding="md" data-testid="reserva" data-status={reservation.status}>
      <div className="flex flex-wrap items-start justify-between gap-x-4 gap-y-2">
        <div className="min-w-0">
          <p className="text-xs font-semibold uppercase tracking-wide text-text-faint">Reserva</p>
          <h3 className="font-display text-xl font-semibold tracking-tight text-text">
            <span className="break-all" data-testid="reserva-numero">
              {reservation.number}
            </span>
          </h3>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <ReservationKindBadge reservation={reservation} />
          <ReservationStatusBadge reservation={reservation} />
        </div>
      </div>

      <dl className="mt-3 grid gap-x-6 gap-y-2 text-sm sm:grid-cols-3">
        <div>
          <dt className="text-xs text-text-faint">Fecha</dt>
          <dd className="font-medium text-text">{formatDateTime(reservation.createdAt)}</dd>
        </div>
        <div>
          <dt className="text-xs text-text-faint">{heldUntilLabel(reservation)}</dt>
          <dd className="flex items-start gap-1.5 font-medium text-text">
            <CalendarClock aria-hidden="true" className="mt-0.5 size-4 shrink-0 text-accent" />
            <span>
              {formatDateTime(reservation.reservedUntil)}
              {hours && (
                <span className="block text-xs font-normal text-text-muted" data-testid="reserva-horas">
                  Se guarda {hours}
                </span>
              )}
            </span>
          </dd>
        </div>
        <div>
          <dt className="text-xs text-text-faint">Total ({reservationItemsLabel(reservation.kind, count)})</dt>
          <dd className="font-display text-lg font-semibold text-text tabular-nums">{formatMoney(reservation.total)}</dd>
        </div>
      </dl>

      <div className="mt-4 flex flex-wrap gap-2 border-t border-border pt-4">
        <Button
          variant="subtle"
          aria-expanded={open}
          aria-controls={detailId}
          rightIcon={<ChevronDown className={open ? 'rotate-180 transition-transform duration-200' : 'transition-transform duration-200'} />}
          onClick={() => setOpen((current) => !current)}
        >
          {open ? 'Ocultar detalle' : 'Ver detalle'}
          <span className="sr-only"> de la reserva {reservation.number}</span>
        </Button>
        {active && !confirming && (
          <Button variant="outline" leftIcon={<LockOpen />} onClick={() => setConfirming(true)}>
            Liberar mi reserva
            <span className="sr-only"> {reservation.number}</span>
          </Button>
        )}
      </div>

      {active && confirming && (
        <div role="alert" className="mt-3 flex flex-col gap-3 rounded-xl border border-danger/40 bg-danger-soft p-3 text-sm sm:flex-row sm:items-center sm:justify-between">
          <span className="text-text">¿Liberar la reserva {reservation.number}? Los productos vuelven a estar disponibles para otros clientes.</span>
          <div className="flex flex-wrap gap-2">
            <Button ref={keepRef} variant="ghost" onClick={() => setConfirming(false)} disabled={releasing}>
              No, conservarla
            </Button>
            <Button variant="danger" leftIcon={<LockOpen />} loading={releasing} onClick={release}>
              Sí, liberar
            </Button>
          </div>
        </div>
      )}

      <div id={detailId} hidden={!open} className="mt-4">
        {open && (
          <div className="space-y-4" data-testid="reserva-detalle">
            <table className="w-full text-sm">
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
                      <p className="leading-snug text-text">{line.name}</p>
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
            </table>
            <dl className="space-y-2 text-sm">
              <div className="flex items-start gap-2 text-text-muted">
                <MapPin aria-hidden="true" className="mt-0.5 size-4 shrink-0 text-accent" />
                <dt className="shrink-0">Retiro:</dt>
                <dd className="font-medium text-text">{branchName(reservation, store)}</dd>
              </div>
              {reservation.notes && (
                <div className="flex items-start gap-2 text-text-muted">
                  <dt className="shrink-0">Notas:</dt>
                  <dd className="text-text">{reservation.notes}</dd>
                </div>
              )}
              {reservation.cancelReason && (
                <div className="flex items-start gap-2 text-text-muted">
                  <dt className="shrink-0">Motivo:</dt>
                  <dd className="text-text">{reservation.cancelReason}</dd>
                </div>
              )}
            </dl>
            {active && <p className="text-sm text-text-muted">La reserva se confirma y paga en la tienda. Pasá antes de la fecha indicada.</p>}
          </div>
        )}
      </div>
    </Card>
  );
}

export function ReservationsSection() {
  const account = useAccount();
  const { state, reload, reloading, update } = useAsyncData((signal) => account.reservations({ signal }));
  const [status, setStatus] = useState<ReservationStatusFilter>(ALL_STATUSES);

  if (state.status === 'loading') return <LoadingState label="Cargando tus reservas…" />;
  if (state.status === 'error') {
    return <ErrorState title="No pudimos cargar tus reservas" message={describeWebApiError(state.error)} retrying={reloading} onRetry={reload} />;
  }

  const all = state.data;
  if (all.length === 0) {
    return (
      <EmptyState icon={<ShoppingBag />} title="Todavía no tenés reservas" description="Cuando reserves un producto o un armado con tu cuenta, lo vas a ver acá con su estado.">
        <Button to={ROUTES.catalog} variant="brand">
          Ver el catálogo
        </Button>
        <Button to={ROUTES.builder} variant="outline">
          Armá tu PC
        </Button>
      </EmptyState>
    );
  }

  const visible = filterReservations(all, status);
  const options = RESERVATION_STATUS_OPTIONS.map((option) => ({
    value: option.value,
    label: `${option.label} (${filterReservations(all, option.value).length})`,
  }));

  return (
    <section aria-labelledby="reservas-titulo">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h2 id="reservas-titulo" className="flex items-center gap-2 text-2xl">
            <TicketCheck aria-hidden="true" className="size-6 text-accent" />
            Mis reservas
          </h2>
          <p className="mt-1 text-sm text-text-muted" aria-live="polite" data-testid="reservas-conteo">
            {status === ALL_STATUSES ? pluralize(all.length, 'reserva', 'reservas') : `${visible.length} de ${pluralize(all.length, 'reserva', 'reservas')}`}
          </p>
        </div>
        <div className="flex w-full flex-wrap items-end gap-2 sm:w-auto">
          <SelectField
            label="Estado"
            options={options}
            value={status}
            onChange={(event) => {
              if (isReservationStatusFilter(event.target.value)) setStatus(event.target.value);
            }}
            className="min-w-44 flex-1 sm:flex-none"
          />
          <Button variant="ghost" leftIcon={<RotateCcw />} loading={reloading} onClick={reload}>
            Actualizar
          </Button>
        </div>
      </div>

      {visible.length === 0 ? (
        <EmptyState size="sm" className="mt-6 rounded-card border border-dashed border-border-strong" title="No tenés reservas con ese estado" description="Probá con otro estado o mirá todas.">
          <Button variant="outline" onClick={() => setStatus(ALL_STATUSES)}>
            Ver todas
          </Button>
        </EmptyState>
      ) : (
        <ul className="mt-6 space-y-4">
          {visible.map((reservation) => (
            <ReservationCard
              key={reservation.number}
              reservation={reservation}
              onReleased={(updated) => update((current) => current.map((item) => (item.number === updated.number ? updated : item)))}
              onStale={reload}
            />
          ))}
        </ul>
      )}
    </section>
  );
}
