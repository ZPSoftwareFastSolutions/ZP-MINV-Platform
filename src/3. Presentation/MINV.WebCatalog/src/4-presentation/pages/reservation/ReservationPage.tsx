// «Consultar mi reserva» (/reserva y /reserva/:numero): número + teléfono → estado (Reservada / Vendida / Cancelada /
// Vencida) con las piezas, el total y la sucursal de retiro; «Liberar mi reserva» con confirmación cuando sigue activa.
// La API exige el teléfono con el que se hizo la reserva (regla S-06); si no coincide, responde «no existe». Estado en
// memoria: nada de storage.

import { CircleAlert, LockOpen, MessageCircle, Search, TicketCheck } from 'lucide-react';
import { useEffect, useId, useRef, useState, type FormEvent } from 'react';
import { useParams } from 'react-router-dom';
import { isBolivianPhone } from '@/1-domain/storefront/contact';
import { asStorefrontError, describeStorefrontError, type StorefrontError } from '@/1-domain/storefront/errors';
import { isReservationActive, type Reservation } from '@/1-domain/storefront/types';
import { ROUTES } from '@/4-presentation/app/routes';
import { ReservationSummary } from '@/4-presentation/components/reservation/ReservationSummary';
import { whatsappReservationUrl } from '@/4-presentation/components/reservation/reservationText';
import { Breadcrumbs } from '@/4-presentation/components/ui/Breadcrumbs';
import { Button } from '@/4-presentation/components/ui/Button';
import { Card } from '@/4-presentation/components/ui/Card';
import { Container } from '@/4-presentation/components/ui/Container';
import { useDocumentTitle } from '@/4-presentation/hooks/useDocumentTitle';
import { useServices } from '@/4-presentation/hooks/useServices';
import { useStore } from '@/4-presentation/hooks/useStore';
import { useToast } from '@/4-presentation/hooks/useToast';

const FIELD =
  'h-11 w-full rounded-xl border border-border-control bg-surface px-3 text-sm text-text placeholder:text-text-faint transition-colors duration-200 hover:border-border-strong focus:border-accent focus:outline-none aria-invalid:border-danger';

const NUMBER_PATTERN = /^ARM-[A-Z0-9]+-\d{1,6}$/i;

type Phase = { kind: 'idle' } | { kind: 'loading' } | { kind: 'found'; reservation: Reservation } | { kind: 'error'; error: StorefrontError };

export function ReservationPage() {
  useDocumentTitle('Consultar mi reserva');
  const { numero = '' } = useParams();
  const { reservations } = useServices();
  const store = useStore();
  const toast = useToast();
  const ids = { number: useId(), phone: useId() };
  const [number, setNumber] = useState(numero.toUpperCase());
  const [phone, setPhone] = useState('');
  const [fieldErrors, setFieldErrors] = useState<{ number?: string; phone?: string }>({});
  const [phase, setPhase] = useState<Phase>({ kind: 'idle' });
  const [confirmRelease, setConfirmRelease] = useState(false);
  const [releasing, setReleasing] = useState(false);
  const cancelRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    if (confirmRelease) cancelRef.current?.focus();
  }, [confirmRelease]);

  const validate = () => {
    const errors: { number?: string; phone?: string } = {};
    if (!number.trim()) errors.number = 'Indicá el número de tu reserva (por ejemplo ARM-WEB-000001).';
    else if (!NUMBER_PATTERN.test(number.trim())) errors.number = 'El número tiene la forma ARM-WEB-000001.';
    if (!phone.trim()) errors.phone = 'Indicá el teléfono con el que hiciste la reserva.';
    else if (!isBolivianPhone(phone)) errors.phone = 'El teléfono debe tener 7 u 8 dígitos (Bolivia), con o sin +591.';
    setFieldErrors(errors);
    return Object.keys(errors).length === 0;
  };

  const lookup = async (event: FormEvent) => {
    event.preventDefault();
    if (!validate()) return;
    setPhase({ kind: 'loading' });
    setConfirmRelease(false);
    try {
      setPhase({ kind: 'found', reservation: await reservations.lookup(number, phone) });
    } catch (error) {
      setPhase({ kind: 'error', error: asStorefrontError(error) });
    }
  };

  const release = async () => {
    if (phase.kind !== 'found') return;
    setReleasing(true);
    try {
      const updated = await reservations.release(phase.reservation.number, phone);
      setPhase({ kind: 'found', reservation: updated });
      setConfirmRelease(false);
      toast.notify({ tone: 'success', title: 'Reserva liberada', description: `${updated.number}: las piezas vuelven a estar disponibles.` });
    } catch (error) {
      const failure = asStorefrontError(error);
      setConfirmRelease(false);
      toast.notify({ tone: 'danger', title: 'No pudimos liberar la reserva', description: describeStorefrontError(failure), duration: 8000 });
      // Si ya no estaba reservada (vendida, cancelada o vencida), la volvemos a consultar para mostrar su estado real.
      if (failure.kind === 'domain') {
        try {
          setPhase({ kind: 'found', reservation: await reservations.lookup(phase.reservation.number, phone) });
        } catch {
          /* se conserva la vista anterior */
        }
      }
    } finally {
      setReleasing(false);
    }
  };

  const found = phase.kind === 'found' ? phase.reservation : null;

  return (
    <Container className="py-8 lg:py-10">
      <Breadcrumbs items={[{ label: 'Inicio', to: ROUTES.home }, { label: 'Consultar mi reserva' }]} />
      <header className="mt-6 max-w-2xl animate-fade-up">
        <p className="mb-2 text-xs font-semibold uppercase tracking-[0.2em] text-accent">Reservas web</p>
        <h1 className="text-3xl sm:text-4xl">Consultar mi reserva</h1>
        <p className="mt-3 text-base text-text-muted">
          Ingresá el número que te dimos al reservar y el teléfono con el que la hiciste. Vas a ver si sigue reservada, hasta cuándo, y podés liberarla si ya
          no la necesitás.
        </p>
      </header>

      <div className="mt-8 grid gap-6 lg:grid-cols-[minmax(0,22rem)_minmax(0,1fr)] lg:items-start">
        <Card as="section" aria-labelledby={`${ids.number}-titulo`} padding="md" className="animate-fade-up">
          <h2 id={`${ids.number}-titulo`} className="flex items-center gap-2 text-xl">
            <Search aria-hidden="true" className="size-5 text-accent" />
            Tu reserva
          </h2>
          <form onSubmit={lookup} noValidate className="mt-4 space-y-4" aria-busy={phase.kind === 'loading' || undefined}>
            <div>
              <label htmlFor={ids.number} className="mb-1 block text-sm font-medium text-text">
                Número de reserva
              </label>
              <input
                id={ids.number}
                type="text"
                autoComplete="off"
                autoCapitalize="characters"
                spellCheck={false}
                placeholder="ARM-WEB-000001"
                value={number}
                onChange={(event) => setNumber(event.target.value.toUpperCase())}
                aria-invalid={fieldErrors.number ? true : undefined}
                aria-describedby={fieldErrors.number ? `${ids.number}-error` : undefined}
                className={`${FIELD} font-mono uppercase`}
              />
              {fieldErrors.number && (
                <p id={`${ids.number}-error`} className="mt-1 text-sm text-danger-text">
                  {fieldErrors.number}
                </p>
              )}
            </div>
            <div>
              <label htmlFor={ids.phone} className="mb-1 block text-sm font-medium text-text">
                Teléfono con el que reservaste
              </label>
              <input
                id={ids.phone}
                type="tel"
                inputMode="tel"
                autoComplete="tel"
                placeholder="+591 71234567"
                value={phone}
                onChange={(event) => setPhone(event.target.value)}
                aria-invalid={fieldErrors.phone ? true : undefined}
                aria-describedby={fieldErrors.phone ? `${ids.phone}-error` : undefined}
                className={FIELD}
              />
              {fieldErrors.phone && (
                <p id={`${ids.phone}-error`} className="mt-1 text-sm text-danger-text">
                  {fieldErrors.phone}
                </p>
              )}
            </div>
            <Button type="submit" variant="brand" fullWidth leftIcon={<Search />} loading={phase.kind === 'loading'}>
              Ver estado
            </Button>
          </form>
          <p className="mt-4 text-xs text-text-faint">Por tu privacidad solo mostramos la reserva si el número y el teléfono coinciden.</p>
        </Card>

        <section aria-live="polite" aria-labelledby="estado-titulo" className="min-w-0">
          <h2 id="estado-titulo" className="sr-only">
            Estado de la reserva
          </h2>
          {phase.kind === 'error' && (
            <div role="alert" className="flex items-start gap-3 rounded-card border border-danger/40 bg-danger-soft p-4 text-sm">
              <CircleAlert aria-hidden="true" className="mt-0.5 size-5 shrink-0 text-danger-text" />
              <div>
                <p className="font-semibold text-danger-text">{phase.error.kind === 'not_found' ? 'No encontramos esa reserva' : 'No pudimos consultar la reserva'}</p>
                <p className="mt-0.5 text-text">
                  {phase.error.kind === 'not_found'
                    ? 'Revisá el número y que el teléfono sea el mismo con el que reservaste.'
                    : describeStorefrontError(phase.error)}
                </p>
              </div>
            </div>
          )}

          {phase.kind === 'idle' && (
            <div className="rounded-card border border-dashed border-border-strong bg-surface/60 px-6 py-12 text-center">
              <span aria-hidden="true" className="mx-auto flex size-14 items-center justify-center rounded-2xl bg-surface-2 text-accent">
                <TicketCheck className="size-7" />
              </span>
              <p className="mt-4 font-display text-lg font-semibold text-text">Acá vas a ver el estado de tu reserva</p>
              <p className="mx-auto mt-1 max-w-md text-sm text-text-muted">
                Las reservas se guardan en {store.branch.name} y se confirman y pagan en la tienda. ¿Todavía no reservaste? Armá tu PC y reservala desde el
                armador.
              </p>
              <div className="mt-5">
                <Button to={ROUTES.builder} variant="outline">
                  Ir al armador
                </Button>
              </div>
            </div>
          )}

          {found && (
            <Card as="article" padding="md" className="animate-fade-up">
              <ReservationSummary reservation={found} store={store} />
              <div className="mt-5 flex flex-col gap-3 border-t border-border pt-4">
                {isReservationActive(found) ? (
                  confirmRelease ? (
                    <div role="alert" className="flex flex-col gap-3 rounded-xl border border-danger/40 bg-danger-soft p-3 text-sm sm:flex-row sm:items-center sm:justify-between">
                      <span className="text-text">¿Liberar la reserva {found.number}? Las piezas vuelven a estar disponibles para otros clientes.</span>
                      <div className="flex gap-2">
                        <Button ref={cancelRef} variant="ghost" onClick={() => setConfirmRelease(false)} disabled={releasing}>
                          No, conservarla
                        </Button>
                        <Button variant="danger" leftIcon={<LockOpen />} loading={releasing} onClick={release}>
                          Sí, liberar
                        </Button>
                      </div>
                    </div>
                  ) : (
                    <div className="flex flex-wrap gap-2">
                      <Button href={whatsappReservationUrl(found)} target="_blank" rel="noreferrer noopener" variant="accent" leftIcon={<MessageCircle />}>
                        Coordinar retiro por WhatsApp
                      </Button>
                      <Button variant="outline" leftIcon={<LockOpen />} onClick={() => setConfirmRelease(true)}>
                        Liberar mi reserva
                      </Button>
                    </div>
                  )
                ) : (
                  <p className="text-sm text-text-muted">
                    {found.status === 'Sold'
                      ? 'Esta reserva ya se confirmó y vendió en la tienda. ¡Gracias por tu compra!'
                      : found.status === 'Expired'
                        ? 'Esta reserva venció y las piezas volvieron a estar disponibles. Podés armar y reservar de nuevo cuando quieras.'
                        : 'Esta reserva está cancelada y las piezas volvieron a estar disponibles.'}
                  </p>
                )}
                {!isReservationActive(found) && (
                  <div>
                    <Button to={ROUTES.builder} variant="outline">
                      Armar otra PC
                    </Button>
                  </div>
                )}
              </div>
            </Card>
          )}
        </section>
      </div>
    </Container>
  );
}
