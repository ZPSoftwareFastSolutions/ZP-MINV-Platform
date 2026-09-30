// «Consultar mi reserva» (/reserva y /reserva/:numero): código de reserva O número de celular (al menos uno; los dos también
// valen) → estado (Reservada / Vendida / Cancelada / Vencida) con las piezas, el total y la sucursal de retiro; «Liberar mi
// reserva» con confirmación cuando sigue activa. V7 (regla S-06): con el código solo, la reserva llega con el contacto
// enmascarado; con el celular solo, la lista de las reservas hechas con ese celular para abrir una. Liberar SIEMPRE exige el
// código y el celular: si se buscó solo con el código, se pide el celular antes de liberar. Estado en memoria: nada de
// storage. V7: también las reservas de carrito (RES-WEB-…), con su tipo (Armado / Compra) y las horas reales que se guardan.

import { ArrowLeft, CircleAlert, EyeOff, LockOpen, MessageCircle, Search, TicketCheck } from 'lucide-react';
import { useEffect, useId, useRef, useState, type FormEvent } from 'react';
import { useParams } from 'react-router-dom';
import { formatBolivianPhone, isBolivianPhone } from '@/1-domain/storefront/contact';
import { asStorefrontError, describeStorefrontError, type StorefrontError } from '@/1-domain/storefront/errors';
import {
  LOOKUP_HINT,
  LOOKUP_PHONE_FORMAT,
  reservationLookupMode,
  validateReservationLookup,
  type ReservationLookupErrors,
  type ReservationLookupMode,
} from '@/1-domain/storefront/lookup';
import { isReservationActive, type Reservation } from '@/1-domain/storefront/types';
import { ROUTES } from '@/4-presentation/app/routes';
import { ReservationKindBadge, ReservationStatusBadge, ReservationSummary } from '@/4-presentation/components/reservation/ReservationSummary';
import { formatDateTime, whatsappReservationUrl } from '@/4-presentation/components/reservation/reservationText';
import { Breadcrumbs } from '@/4-presentation/components/ui/Breadcrumbs';
import { Button } from '@/4-presentation/components/ui/Button';
import { Card } from '@/4-presentation/components/ui/Card';
import { Container } from '@/4-presentation/components/ui/Container';
import { useDocumentTitle } from '@/4-presentation/hooks/useDocumentTitle';
import { useServices } from '@/4-presentation/hooks/useServices';
import { useStore } from '@/4-presentation/hooks/useStore';
import { useToast } from '@/4-presentation/hooks/useToast';
import { formatMoney } from '@/shared/format';

const FIELD =
  'h-11 w-full rounded-xl border border-border-control bg-surface px-3 text-sm text-text placeholder:text-text-faint transition-colors duration-200 hover:border-border-strong focus:border-accent focus:outline-none aria-invalid:border-danger';

/**
 * `found.phone`: el celular que ya se comprobó (se buscó con él); sin él, liberar lo pide. `found.list`: la lista de la
 * que se abrió (para volver a ella).
 */
type Phase =
  | { kind: 'idle' }
  | { kind: 'loading' }
  | { kind: 'found'; reservation: Reservation; phone?: string; list?: Reservation[] }
  | { kind: 'list'; reservations: Reservation[]; phone: string }
  | { kind: 'error'; error: StorefrontError; mode: ReservationLookupMode };

/** Ids de ayuda de un campo: la pista «Complete al menos uno» y los errores que tenga. */
function describedBy(...ids: (string | false | undefined)[]): string {
  return ids.filter(Boolean).join(' ');
}

export function ReservationPage() {
  useDocumentTitle('Consultar mi reserva');
  const { numero = '' } = useParams();
  const { reservations } = useServices();
  const store = useStore();
  const toast = useToast();
  const ids = { number: useId(), phone: useId(), hint: useId(), releasePhone: useId() };
  const [number, setNumber] = useState(numero.toUpperCase());
  const [phone, setPhone] = useState('');
  const [fieldErrors, setFieldErrors] = useState<ReservationLookupErrors>({});
  const [phase, setPhase] = useState<Phase>({ kind: 'idle' });
  const [confirmRelease, setConfirmRelease] = useState(false);
  const [releasing, setReleasing] = useState(false);
  const [releasePhone, setReleasePhone] = useState('');
  const [releasePhoneError, setReleasePhoneError] = useState<string | null>(null);
  const cancelRef = useRef<HTMLButtonElement>(null);
  const releasePhoneRef = useRef<HTMLInputElement>(null);
  const knownPhone = phase.kind === 'found' ? phase.phone : undefined;

  useEffect(() => {
    if (!confirmRelease) return;
    if (knownPhone) cancelRef.current?.focus();
    else releasePhoneRef.current?.focus();
  }, [confirmRelease, knownPhone]);

  const askRelease = (open: boolean) => {
    setConfirmRelease(open);
    setReleasePhone('');
    setReleasePhoneError(null);
  };

  const lookup = async (event: FormEvent) => {
    event.preventDefault();
    const input = { number, phone };
    const errors = validateReservationLookup(input);
    setFieldErrors(errors);
    if (Object.keys(errors).length > 0) return;
    const mode = reservationLookupMode(input);
    const typedPhone = phone.trim();
    setPhase({ kind: 'loading' });
    askRelease(false);
    try {
      if (mode === 'code') {
        setPhase({ kind: 'found', reservation: await reservations.lookup(number, typedPhone || undefined), phone: typedPhone || undefined });
      } else {
        setPhase({ kind: 'list', reservations: await reservations.lookupByPhone(typedPhone), phone: typedPhone });
      }
    } catch (error) {
      setPhase({ kind: 'error', error: asStorefrontError(error), mode });
    }
  };

  const release = async () => {
    if (phase.kind !== 'found') return;
    // Liberar exige el código Y el celular (regla S-06): el que ya se comprobó o el que se pide ahora.
    const releaseWith = phase.phone ?? releasePhone.trim();
    if (!phase.phone) {
      if (!releaseWith) {
        setReleasePhoneError('Indicá el número de celular con el que hiciste la reserva.');
        return;
      }
      if (!isBolivianPhone(releaseWith)) {
        setReleasePhoneError(LOOKUP_PHONE_FORMAT);
        return;
      }
    }
    setReleasing(true);
    try {
      const updated = await reservations.release(phase.reservation.number, releaseWith);
      setPhase({ kind: 'found', reservation: updated, phone: releaseWith, list: phase.list });
      askRelease(false);
      toast.notify({ tone: 'success', title: 'Reserva liberada', description: `${updated.number}: ${updated.kind === 'cart' ? 'los productos vuelven' : 'las piezas vuelven'} a estar disponibles.` });
    } catch (error) {
      const failure = asStorefrontError(error);
      if (failure.kind === 'not_found' && !phase.phone) {
        // El celular escrito no es el de la reserva: la confirmación sigue abierta para corregirlo.
        setReleasePhoneError('Ese celular no coincide con el de la reserva. Revisalo e intentá de nuevo.');
        return;
      }
      askRelease(false);
      toast.notify({ tone: 'danger', title: 'No pudimos liberar la reserva', description: describeStorefrontError(failure), duration: 8000 });
      // Si ya no estaba reservada (vendida, cancelada o vencida), la volvemos a consultar para mostrar su estado real.
      if (failure.kind === 'domain') {
        try {
          setPhase({ kind: 'found', reservation: await reservations.lookup(phase.reservation.number, releaseWith), phone: releaseWith, list: phase.list });
        } catch {
          /* se conserva la vista anterior */
        }
      }
    } finally {
      setReleasing(false);
    }
  };

  const openFromList = (reservation: Reservation, list: Reservation[], listPhone: string) => {
    askRelease(false);
    setPhase({ kind: 'found', reservation, phone: listPhone, list });
  };

  const backToList = () => {
    if (phase.kind !== 'found' || !phase.list) return;
    askRelease(false);
    setPhase({ kind: 'list', reservations: phase.list, phone: phase.phone ?? '' });
  };

  const found = phase.kind === 'found' ? phase.reservation : null;

  return (
    <Container className="py-8 lg:py-10">
      <Breadcrumbs items={[{ label: 'Inicio', to: ROUTES.home }, { label: 'Consultar mi reserva' }]} />
      <header className="mt-6 max-w-2xl animate-fade-up">
        <p className="mb-2 text-xs font-semibold uppercase tracking-[0.2em] text-accent">Reservas web</p>
        <h1 className="text-3xl sm:text-4xl">Consultar mi reserva</h1>
        <p className="mt-3 text-base text-text-muted">
          Buscala con el código que te dimos al reservar o con el número de celular con el que la hiciste. Vas a ver si sigue reservada, hasta cuándo, y
          podés liberarla si ya no la necesitás.
        </p>
      </header>

      <div className="mt-8 grid gap-6 lg:grid-cols-[minmax(0,22rem)_minmax(0,1fr)] lg:items-start">
        <Card as="section" aria-labelledby={`${ids.number}-titulo`} padding="md" className="animate-fade-up">
          <h2 id={`${ids.number}-titulo`} className="flex items-center gap-2 text-xl">
            <Search aria-hidden="true" className="size-5 text-accent" />
            Tu reserva
          </h2>
          <form onSubmit={lookup} noValidate className="mt-4 space-y-4" aria-busy={phase.kind === 'loading' || undefined}>
            <p id={ids.hint} className="text-sm text-text-muted">
              {LOOKUP_HINT}
            </p>
            <div>
              <label htmlFor={ids.number} className="mb-1 block text-sm font-medium text-text">
                Código de reserva
              </label>
              <input
                id={ids.number}
                type="text"
                autoComplete="off"
                autoCapitalize="characters"
                spellCheck={false}
                placeholder="RES-WEB-000001"
                value={number}
                onChange={(event) => setNumber(event.target.value.toUpperCase())}
                aria-invalid={fieldErrors.number || fieldErrors.form ? true : undefined}
                aria-describedby={describedBy(ids.hint, fieldErrors.number && `${ids.number}-error`, fieldErrors.form && `${ids.hint}-error`)}
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
                Número de celular
              </label>
              <input
                id={ids.phone}
                type="tel"
                inputMode="tel"
                autoComplete="tel"
                placeholder="+591 71234567"
                value={phone}
                onChange={(event) => setPhone(event.target.value)}
                aria-invalid={fieldErrors.phone || fieldErrors.form ? true : undefined}
                aria-describedby={describedBy(ids.hint, fieldErrors.phone && `${ids.phone}-error`, fieldErrors.form && `${ids.hint}-error`)}
                className={FIELD}
              />
              {fieldErrors.phone && (
                <p id={`${ids.phone}-error`} className="mt-1 text-sm text-danger-text">
                  {fieldErrors.phone}
                </p>
              )}
            </div>
            {fieldErrors.form && (
              <p id={`${ids.hint}-error`} className="text-sm text-danger-text">
                {fieldErrors.form}
              </p>
            )}
            <Button type="submit" variant="brand" fullWidth leftIcon={<Search />} loading={phase.kind === 'loading'}>
              Buscar reserva
            </Button>
          </form>
          <p className="mt-4 text-xs text-text-faint">
            Por tu privacidad, si buscás con uno solo de los dos datos ocultamos parte del nombre y del contacto. Para liberar una reserva te pedimos el código y
            el celular.
          </p>
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
                    ? 'Revisá el código y, si también escribiste el celular, que sea el mismo con el que reservaste.'
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
                Las reservas se guardan en {store.branch.name} y se confirman y pagan en la tienda. ¿Todavía no reservaste? Elegí tus productos en el
                catálogo o armá tu PC.
              </p>
              <div className="mt-5 flex flex-wrap justify-center gap-3">
                <Button to={ROUTES.catalog} variant="outline">
                  Ver el catálogo
                </Button>
                <Button to={ROUTES.builder} variant="outline">
                  Ir al armador
                </Button>
              </div>
            </div>
          )}

          {phase.kind === 'list' && (
            <Card as="article" padding="md" className="animate-fade-up" data-testid="lista-reservas">
              <h3 className="font-display text-lg font-semibold text-text">Reservas con el celular {formatBolivianPhone(phase.phone)}</h3>
              {phase.reservations.length === 0 ? (
                <div role="status" className="mt-3 text-sm text-text-muted">
                  <p className="font-semibold text-text">No encontramos reservas con ese celular</p>
                  <p className="mt-0.5">Revisá el número o buscá con el código de reserva. Mostramos las reservas de los últimos 90 días.</p>
                </div>
              ) : (
                <>
                  <p className="mt-1 text-sm text-text-muted">Elegí una para ver el detalle. Mostramos las más recientes de los últimos 90 días.</p>
                  <ul className="mt-4 divide-y divide-border">
                    {phase.reservations.map((item) => (
                      <li key={item.number} className="flex flex-wrap items-center justify-between gap-3 py-3">
                        <div className="min-w-0">
                          <p className="font-mono font-semibold text-text">{item.number}</p>
                          <p className="text-xs text-text-muted">
                            {formatDateTime(item.createdAt)} · {formatMoney(item.total)}
                          </p>
                          <div className="mt-1 flex flex-wrap gap-2">
                            <ReservationKindBadge reservation={item} size="sm" />
                            <ReservationStatusBadge reservation={item} size="sm" />
                          </div>
                        </div>
                        <Button
                          variant="outline"
                          leftIcon={<Search />}
                          aria-label={`Ver la reserva ${item.number}`}
                          onClick={() => openFromList(item, phase.reservations, phase.phone)}
                        >
                          Ver
                        </Button>
                      </li>
                    ))}
                  </ul>
                </>
              )}
            </Card>
          )}

          {found && (
            <Card as="article" padding="md" className="animate-fade-up">
              {phase.kind === 'found' && phase.list && (
                <Button variant="ghost" leftIcon={<ArrowLeft />} className="mb-3" onClick={backToList}>
                  Volver a la lista
                </Button>
              )}
              <ReservationSummary reservation={found} store={store} />
              {(found.maskedPhone || found.maskedEmail || found.masked) && (
                <div className="mt-4 space-y-1 text-sm text-text-muted" data-testid="contacto-enmascarado">
                  {found.maskedPhone && (
                    <p>
                      Celular: <span className="font-mono text-text">{found.maskedPhone}</span>
                      {found.maskedEmail && (
                        <>
                          {' '}
                          · Correo: <span className="font-mono text-text">{found.maskedEmail}</span>
                        </>
                      )}
                    </p>
                  )}
                  {found.masked && (
                    <p className="flex items-start gap-2 text-xs">
                      <EyeOff aria-hidden="true" className="mt-0.5 size-4 shrink-0" />
                      <span>Por tu privacidad ocultamos parte del nombre y las notas. Para verlos completos, buscá con el código y el celular.</span>
                    </p>
                  )}
                </div>
              )}
              <div className="mt-5 flex flex-col gap-3 border-t border-border pt-4">
                {isReservationActive(found) ? (
                  confirmRelease ? (
                    <div role="alert" className="flex flex-col gap-3 rounded-xl border border-danger/40 bg-danger-soft p-3 text-sm sm:flex-row sm:flex-wrap sm:items-center sm:justify-between">
                      <span className="text-text">
                        ¿Liberar la reserva {found.number}? {found.kind === 'cart' ? 'Los productos vuelven' : 'Las piezas vuelven'} a estar disponibles para otros clientes.
                      </span>
                      {!knownPhone && (
                        <div className="w-full">
                          <label htmlFor={ids.releasePhone} className="mb-1 block text-sm font-medium text-text">
                            Número de celular con el que reservaste
                          </label>
                          <input
                            ref={releasePhoneRef}
                            id={ids.releasePhone}
                            type="tel"
                            inputMode="tel"
                            autoComplete="tel"
                            placeholder="+591 71234567"
                            value={releasePhone}
                            onChange={(event) => {
                              setReleasePhone(event.target.value);
                              setReleasePhoneError(null);
                            }}
                            aria-invalid={releasePhoneError ? true : undefined}
                            aria-describedby={releasePhoneError ? `${ids.releasePhone}-error` : undefined}
                            className={FIELD}
                          />
                          {releasePhoneError && (
                            <p id={`${ids.releasePhone}-error`} className="mt-1 text-sm text-danger-text">
                              {releasePhoneError}
                            </p>
                          )}
                        </div>
                      )}
                      <div className="flex gap-2">
                        <Button ref={cancelRef} variant="ghost" onClick={() => askRelease(false)} disabled={releasing}>
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
                      <Button variant="outline" leftIcon={<LockOpen />} onClick={() => askRelease(true)}>
                        Liberar mi reserva
                      </Button>
                    </div>
                  )
                ) : (
                  <p className="text-sm text-text-muted">
                    {found.status === 'Sold'
                      ? 'Esta reserva ya se confirmó y vendió en la tienda. ¡Gracias por tu compra!'
                      : found.status === 'Expired'
                        ? `Esta reserva venció y ${found.kind === 'cart' ? 'los productos volvieron' : 'las piezas volvieron'} a estar disponibles. Podés reservar de nuevo cuando quieras.`
                        : `Esta reserva está cancelada y ${found.kind === 'cart' ? 'los productos volvieron' : 'las piezas volvieron'} a estar disponibles.`}
                  </p>
                )}
                {!isReservationActive(found) && (
                  <div>
                    {found.kind === 'cart' ? (
                      <Button to={ROUTES.catalog} variant="outline">
                        Ver el catálogo
                      </Button>
                    ) : (
                      <Button to={ROUTES.builder} variant="outline">
                        Armar otra PC
                      </Button>
                    )}
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
