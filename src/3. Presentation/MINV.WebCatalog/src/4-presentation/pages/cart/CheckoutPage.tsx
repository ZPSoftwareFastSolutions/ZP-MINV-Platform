// RESERVA (`/reservar`, V7): la MISMA pantalla para un artículo suelto («Reservar ahora», `?sku=…&cantidad=…`) y para el
// carrito completo (`/reservar` sin `sku`). Pide los datos del cliente y, al confirmar, el servidor guarda los productos
// y le envía por correo el código de la reserva con su detalle.
//
//   · Sin sesión: nombre, teléfono o WhatsApp, correo, «¿Cuándo pasás a recogerlo?», datos para la factura (opcional) y
//     notas; reserva por la tienda pública con `kind = "cart"` y una `Idempotency-Key` por intento.
//   · Con una cuenta de cliente: los datos salen de su cuenta (solo lectura) y reserva por RPC
//     (`CreateMyReservationCommand`); solo elige los días y deja notas.
//   · Falta de stock (409): marca qué productos y cuánto hay, ofrece «Ajustar a lo disponible» y NO pierde lo escrito.
//   · 400/422 (campo por campo cuando se puede), 429 y red caída: lo escrito queda; el reintento por red viaja con la
//     MISMA llave (no se reserva dos veces) y cualquier cambio estrena otra.
//   · Al confirmar se quitan del carrito los productos reservados; un artículo suelto no toca el carrito.

import { ArrowRight, ClipboardList, LoaderCircle, LogIn, RefreshCw, UserPlus, Wand2 } from 'lucide-react';
import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { asWebApiError, describeWebApiError } from '@/1-domain/auth/errors';
import type { CustomerAccount } from '@/1-domain/account/types';
import {
  BUYER_FIELDS,
  checkoutFields,
  firstInvalidField,
  toCheckoutContact,
  toCheckoutNotes,
  toHoldDays,
  toReservationBuyer,
  type CheckoutField,
  type CheckoutFormErrors,
  type CheckoutMode,
} from '@/1-domain/storefront/checkoutForm';
import type { Reservation } from '@/1-domain/storefront/types';
import {
  checkoutAdjustments,
  checkoutSelection,
  contentFingerprint,
  IdempotentAttempt,
  restrictToFields,
  toCheckoutFailure,
  type CheckoutFailure,
} from '@/2-application';
import { ROUTES } from '@/4-presentation/app/routes';
import { ErrorState, LoadingState } from '@/4-presentation/components/feedback/AsyncState';
import { Alert } from '@/4-presentation/components/ui/Alert';
import { Breadcrumbs } from '@/4-presentation/components/ui/Breadcrumbs';
import { Button } from '@/4-presentation/components/ui/Button';
import { Card } from '@/4-presentation/components/ui/Card';
import { Container } from '@/4-presentation/components/ui/Container';
import { EmptyState } from '@/4-presentation/components/ui/EmptyState';
import { useCart } from '@/4-presentation/hooks/useCart';
import { useDocumentMeta } from '@/4-presentation/hooks/useDocumentMeta';
import { useReservationPolicy } from '@/4-presentation/hooks/useReservationPolicy';
import { useOptionalAccount } from '@/4-presentation/hooks/useRpc';
import { useServices } from '@/4-presentation/hooks/useServices';
import { useSession } from '@/4-presentation/hooks/useSession';
import { useStore } from '@/4-presentation/hooks/useStore';
import { useToast } from '@/4-presentation/hooks/useToast';
import { CheckoutConfirmation } from './checkout/CheckoutConfirmation';
import { AccountContact, BuyerSection, GuestContactFields, HoldDaysField, NotesField } from './checkout/CheckoutFields';
import { CheckoutItems } from './checkout/CheckoutItems';
import { checkoutAdjustmentText } from './checkout/checkoutText';
import { useCheckoutForm } from './checkout/useCheckoutForm';

/** Disponibilidad al abrir la página: en consulta, recién consultada o sin actualizar (se muestra la última conocida). */
type Freshness = 'checking' | 'fresh' | 'stale';

type AccountState = { status: 'loading' } | { status: 'ready'; account: CustomerAccount } | { status: 'error'; message: string };

interface Confirmed {
  reservation: Reservation;
  email: string | null;
  withAccount: boolean;
}

/** Datos de la cuenta del cliente (solo con una sesión de cliente y sus servicios listos). */
function useCustomerAccount(enabled: boolean): { state: AccountState; retry: () => void } {
  const account = useOptionalAccount();
  const [state, setState] = useState<AccountState>({ status: 'loading' });
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    if (!enabled || !account) return;
    const controller = new AbortController();
    account.load({ signal: controller.signal }).then(
      (data) => {
        if (!controller.signal.aborted) setState({ status: 'ready', account: data });
      },
      (error: unknown) => {
        if (!controller.signal.aborted) setState({ status: 'error', message: describeWebApiError(asWebApiError(error)) });
      },
    );
    return () => controller.abort();
  }, [enabled, account, attempt]);

  const retry = useCallback(() => {
    setState({ status: 'loading' });
    setAttempt((current) => current + 1);
  }, []);
  return { state, retry };
}

export function CheckoutPage() {
  useDocumentMeta({ title: 'Reservar', description: 'Reservá tus productos en Tech Zone Gaming: dejá tus datos, elegí cuándo pasás y recibí el código de tu reserva por correo.' });
  const location = useLocation();
  const navigate = useNavigate();
  const cart = useCart();
  const { catalog, reservations, refresh } = useServices();
  const policy = useReservationPolicy();
  const store = useStore();
  const toast = useToast();
  const { status: sessionStatus, session } = useSession();
  const accountUseCases = useOptionalAccount();
  const customer = session?.kind === 'customer';
  const mode: CheckoutMode = customer ? 'account' : 'guest';
  const rules = useMemo(() => ({ mode, policy }), [mode, policy]);
  const form = useCheckoutForm(rules);
  const { state: accountState, retry: retryAccount } = useCustomerAccount(customer);

  const [freshness, setFreshness] = useState<Freshness>('checking');
  const [sending, setSending] = useState(false);
  const [failure, setFailure] = useState<CheckoutFailure | null>(null);
  const [buyerOpen, setBuyerOpen] = useState(false);
  const [confirmed, setConfirmed] = useState<Confirmed | null>(null);
  const [attempt] = useState(() => new IdempotentAttempt());
  /** Campo al que hay que llevar el foco en cuanto se dibuje (la sección de factura puede estar abriéndose). */
  const pendingFocus = useRef<CheckoutField | null>(null);
  const [focusRequest, setFocusRequest] = useState(0);
  const fields = useRef(new Map<CheckoutField, HTMLElement>());
  const alertRef = useRef<HTMLDivElement>(null);

  const selection = useMemo(
    () => checkoutSelection(location.search, { items: cart.items }, (sku) => catalog.getProductBySku(sku)),
    [location.search, cart.items, catalog],
  );
  const { source, items, review } = selection;
  const here = `${location.pathname}${location.search}`;

  // Disponibilidad fresca al abrir (la tienda vuelve a comprobar el stock al reservar).
  useEffect(() => {
    let active = true;
    void refresh().then((updated) => {
      if (active) setFreshness(updated ? 'fresh' : 'stale');
    });
    return () => {
      active = false;
    };
  }, [refresh]);

  // El foco va al campo con error cuando ya está visible (la sección de factura puede estar abriéndose).
  useEffect(() => {
    const field = pendingFocus.current;
    if (!field) return;
    pendingFocus.current = null;
    fields.current.get(field)?.focus();
  }, [focusRequest, buyerOpen]);

  const fieldRef = useCallback(
    (field: CheckoutField) => (element: HTMLElement | null) => {
      if (element) fields.current.set(field, element);
      else fields.current.delete(field);
    },
    [],
  );

  const focusAlert = () => requestAnimationFrame(() => alertRef.current?.focus());

  const showFieldErrors = (errors: CheckoutFormErrors) => {
    const first = firstInvalidField(errors);
    if (!first) return;
    if (BUYER_FIELDS.some((field) => errors[field])) setBuyerOpen(true);
    pendingFocus.current = first;
    setFocusRequest((current) => current + 1);
  };

  /** Quita del carrito lo que se reservó (solo si la reserva salió del carrito). */
  const removeReserved = (reserved: readonly { sku: string; quantity: number }[]) => {
    if (source !== 'cart') return;
    for (const item of reserved) {
      const left = cart.quantityOf(item.sku) - item.quantity;
      if (left > 0) cart.setQuantity(item.sku, left);
      else cart.remove(item.sku);
    }
  };

  const submit = async (event?: FormEvent) => {
    event?.preventDefault();
    if (sending) return;
    const errors = form.validateAll();
    if (Object.keys(errors).length > 0) {
      showFieldErrors(errors);
      return;
    }
    if (!review.reservable || items.length === 0) {
      focusAlert();
      return;
    }
    if (mode === 'account' && (!accountUseCases || accountState.status !== 'ready')) return;
    setSending(true);
    setFailure(null);
    const holdDays = toHoldDays(form.values);
    const notes = toCheckoutNotes(form.values);
    const lines = items.map((item) => ({ sku: item.sku, quantity: item.quantity }));
    try {
      let reservation: Reservation;
      let email: string | null;
      if (mode === 'account' && accountUseCases && accountState.status === 'ready') {
        const input = { items: lines, holdDays, notes };
        reservation = await attempt.run(contentFingerprint({ via: 'account', ...input }), (requestId) => accountUseCases.reserve(input, { requestId }));
        email = accountState.account.email;
      } else {
        const input = { items: lines, contact: toCheckoutContact(form.values), holdDays, buyer: toReservationBuyer(form.values), notes };
        reservation = await attempt.run(contentFingerprint({ via: 'guest', ...input }), (idempotencyKey) => reservations.reserveCart({ ...input, idempotencyKey }));
        email = input.contact.email ?? null;
      }
      removeReserved(lines);
      setConfirmed({ reservation, email, withAccount: mode === 'account' });
      // Lo reservado ya no está disponible para otros: el catálogo se pone al día.
      void refresh();
    } catch (error) {
      // Los errores de campos que esta pantalla no muestra (con cuenta no hay nombre ni teléfono) van como mensajes.
      const problem = restrictToFields(toCheckoutFailure(error), checkoutFields(mode));
      setFailure(problem);
      form.setServerErrors(problem.fieldErrors);
      if (problem.kind === 'insufficient_stock') {
        setFreshness('checking');
        void refresh().then((updated) => setFreshness(updated ? 'fresh' : 'stale'));
      }
      if (Object.keys(problem.fieldErrors).length > 0) showFieldErrors(problem.fieldErrors);
      else focusAlert();
    } finally {
      setSending(false);
    }
  };

  /** «Ajustar a lo disponible»: baja las cantidades a lo que hay (lo que dijo la tienda manda) sin tocar lo escrito. */
  const adjust = () => {
    const changes = checkoutAdjustments(review, failure?.shortages ?? []);
    if (changes.length === 0) return;
    if (source === 'cart') {
      for (const change of changes) {
        if (change.to > 0) cart.setQuantity(change.sku, change.to);
        else cart.remove(change.sku);
      }
    } else {
      const change = changes[0];
      if (change.to > 0) navigate(ROUTES.checkoutItem(change.sku, change.to), { replace: true });
    }
    setFailure((current) => (current?.kind === 'insufficient_stock' ? null : current));
    toast.notify({ tone: 'success', group: 'reserva:ajuste', title: 'Ajustamos tu reserva a lo disponible', description: checkoutAdjustmentText(changes) });
  };

  const checkAgain = () => {
    setFreshness('checking');
    void refresh().then((updated) => setFreshness(updated ? 'fresh' : 'stale'));
  };

  if (confirmed) return <CheckoutConfirmation reservation={confirmed.reservation} store={store} email={confirmed.email} withAccount={confirmed.withAccount} />;

  const itemProduct = source === 'item' ? review.lines[0]?.product : null;
  const breadcrumbs =
    source === 'cart'
      ? [{ label: 'Carrito', to: ROUTES.cart }, { label: 'Reservar' }]
      : itemProduct
        ? [{ label: itemProduct.shortName, to: ROUTES.product(itemProduct.slug) }, { label: 'Reservar' }]
        : [{ label: 'Reservar' }];
  const itemGone = source === 'item' && review.lines.length > 0 && review.lines.every((line) => line.status === 'unavailable');

  if (items.length === 0 || itemGone) {
    return (
      <Container className="py-6 sm:py-8 lg:py-10">
        <Breadcrumbs items={breadcrumbs} />
        <h1 className="mt-4 text-3xl sm:text-4xl">Reservar</h1>
        <EmptyState
          className="mt-8"
          icon={<ClipboardList />}
          title={source === 'cart' ? 'Tu carrito está vacío' : 'No encontramos ese producto'}
          description={
            source === 'cart'
              ? 'Agregá productos al carrito y volvé para reservarlos. Los retirás y pagás en la tienda.'
              : 'El producto que querías reservar ya no está en el catálogo. Mirá otros productos o volvé a tu carrito.'
          }
        >
          <Button to={ROUTES.catalog} variant="brand" rightIcon={<ArrowRight />}>
            Ver el catálogo
          </Button>
          {source === 'item' && (
            <Button to={ROUTES.cart} variant="outline">
              Ir al carrito
            </Button>
          )}
        </EmptyState>
      </Container>
    );
  }

  const adjustable = checkoutAdjustments(review, failure?.shortages ?? []);
  const itemSoldOut = source === 'item' && adjustable.length > 0 && adjustable.every((change) => change.to === 0);
  const sessionPending = sessionStatus === 'loading';
  const accountBlocked = mode === 'account' && accountState.status !== 'ready';
  const blocked = !review.reservable || sessionPending || accountBlocked;

  return (
    <Container className="py-6 sm:py-8 lg:py-10">
      <Breadcrumbs items={breadcrumbs} compactOnMobile />
      <header className="mt-4 max-w-3xl">
        <h1 className="text-3xl sm:text-4xl">Reservar</h1>
        <p className="mt-2 text-sm text-text-muted">
          {customer ? 'Revisá tus datos y elegí cuándo pasás.' : 'Dejá tus datos y elegí cuándo pasás.'} Te guardamos{' '}
          {source === 'cart' && review.count !== 1 ? 'los productos' : 'el producto'} en {store.branch.name} y te enviamos el código de la reserva con su detalle.
          Pagás al retirar.
        </p>
      </header>

      <div className="mt-6 grid gap-6 lg:grid-cols-[minmax(0,1fr)_24rem] lg:items-start lg:gap-8">
        <Card as="section" aria-labelledby="reserva-resumen" elevated padding="md" className="min-w-0 lg:sticky lg:top-32 lg:col-start-2 lg:row-start-1">
          <CheckoutItems
            source={source}
            review={review}
            shortages={failure?.shortages ?? []}
            branchName={store.branch.name}
            disabled={sending}
            onItemQuantity={(quantity) => {
              const line = review.lines[0];
              if (line) navigate(ROUTES.checkoutItem(line.sku, quantity), { replace: true });
            }}
          />
          <div className="mt-3 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-text-muted" data-testid="reserva-disponibilidad" data-freshness={freshness}>
            {freshness === 'checking' ? (
              <span className="inline-flex min-h-9 items-center gap-1.5" role="status">
                <LoaderCircle aria-hidden="true" className="size-4 animate-spin" />
                Consultando disponibilidad…
              </span>
            ) : (
              <>
                <span role="status">{freshness === 'fresh' ? 'Disponibilidad recién consultada' : 'Disponibilidad sin actualizar'}</span>
                <Button size="sm" variant="ghost" leftIcon={<RefreshCw />} onClick={checkAgain} disabled={sending}>
                  Actualizar
                </Button>
              </>
            )}
          </div>
        </Card>

        <form onSubmit={(event) => void submit(event)} noValidate aria-busy={sending || undefined} aria-labelledby="reserva-datos" className="min-w-0 space-y-6 lg:col-start-1 lg:row-start-1">
          {failure ? (
            <Alert
              ref={alertRef}
              tone="danger"
              title={failure.title}
              actions={
                failure.kind === 'insufficient_stock' ? (
                  itemSoldOut ? (
                    <Button size="sm" variant="outline" to={ROUTES.catalog}>
                      Ver otros productos
                    </Button>
                  ) : (
                    <Button size="sm" variant="primary" leftIcon={<Wand2 />} onClick={adjust}>
                      Ajustar a lo disponible
                    </Button>
                  )
                ) : failure.kind === 'network' || failure.kind === 'rate_limited' || failure.kind === 'unavailable' || failure.kind === 'retry' ? (
                  <Button size="sm" variant="outline" leftIcon={<RefreshCw />} onClick={() => void submit()} loading={sending}>
                    Reintentar
                  </Button>
                ) : failure.kind === 'session' ? (
                  <Button size="sm" variant="outline" to={ROUTES.loginReturning(here)} leftIcon={<LogIn />}>
                    Ingresar
                  </Button>
                ) : undefined
              }
            >
              <p>{itemSoldOut ? 'Ya no quedan unidades de este producto. No se reservó nada.' : failure.message}</p>
              {failure.messages.length > 0 && (
                <ul className="mt-1 list-disc pl-5 text-text-muted">
                  {failure.messages.map((message) => (
                    <li key={message}>{message}</li>
                  ))}
                </ul>
              )}
            </Alert>
          ) : (
            !review.reservable && (
              <Alert
                ref={alertRef}
                tone="warning"
                title={review.issues.length === 1 ? 'Un producto cambió de disponibilidad' : `${review.issues.length} productos cambiaron de disponibilidad`}
                actions={
                  itemSoldOut ? (
                    <Button size="sm" variant="outline" to={ROUTES.catalog}>
                      Ver otros productos
                    </Button>
                  ) : (
                    <Button size="sm" variant="primary" leftIcon={<Wand2 />} onClick={adjust}>
                      Ajustar a lo disponible
                    </Button>
                  )
                }
              >
                {itemSoldOut ? 'Ya no quedan unidades de este producto.' : 'Ajustá las cantidades a lo que hay hoy para poder reservar. Tus datos quedan como están.'}
              </Alert>
            )
          )}

          <Card as="section" aria-labelledby="reserva-datos" padding="md" className="space-y-5">
            <h2 id="reserva-datos" className="font-display text-xl font-semibold text-text">
              Tus datos
            </h2>
            {sessionPending ? (
              <LoadingState label="Comprobando tu sesión…" rows={1} />
            ) : customer ? (
              accountState.status === 'ready' ? (
                <AccountContact account={accountState.account} />
              ) : accountState.status === 'error' ? (
                <ErrorState title="No pudimos cargar los datos de tu cuenta" message={accountState.message} onRetry={retryAccount} />
              ) : (
                <LoadingState label="Cargando los datos de tu cuenta…" rows={1} />
              )
            ) : (
              <>
                {!session && (
                  <div className="flex flex-col gap-3 rounded-xl border border-accent/40 bg-accent-soft p-3 text-sm sm:flex-row sm:items-center sm:justify-between" data-testid="reserva-invitacion">
                    <p className="min-w-0 text-text sm:flex-1">
                      <span className="font-semibold">¿Tenés cuenta? Ingresá y seguí tus reservas.</span>{' '}
                      <span className="text-text-muted">No hace falta: podés reservar igual con tus datos.</span>
                    </p>
                    <div className="flex flex-wrap gap-2">
                      <Button to={ROUTES.loginReturning(here)} size="sm" variant="outline" leftIcon={<LogIn />}>
                        Ingresar
                      </Button>
                      <Button to={ROUTES.registerReturning(here)} size="sm" variant="ghost" leftIcon={<UserPlus />}>
                        Crear cuenta
                      </Button>
                    </div>
                  </div>
                )}
                <GuestContactFields form={form} fieldRef={fieldRef} disabled={sending} />
                <BuyerSection form={form} fieldRef={fieldRef} disabled={sending} open={buyerOpen} onToggle={() => setBuyerOpen((current) => !current)} />
              </>
            )}
          </Card>

          <Card as="section" aria-labelledby="reserva-retiro" padding="md" className="space-y-5">
            <h2 id="reserva-retiro" className="font-display text-xl font-semibold text-text">
              Retiro
            </h2>
            <HoldDaysField form={form} fieldRef={fieldRef} disabled={sending} policy={policy} branchName={store.branch.name} />
            <NotesField form={form} fieldRef={fieldRef} disabled={sending} />
          </Card>

          <div className="flex flex-col gap-2">
            <Button type="submit" size="lg" variant="brand" fullWidth loading={sending} disabled={blocked} aria-describedby={blocked ? 'reserva-bloqueo' : undefined}>
              {sending ? 'Reservando…' : 'Confirmar reserva'}
            </Button>
            {blocked && !sending && (
              <p id="reserva-bloqueo" className="text-xs text-warning-text">
                {!review.reservable ? 'Ajustá la reserva a lo disponible para poder confirmar.' : 'Esperá un momento: estamos preparando tus datos.'}
              </p>
            )}
            {sending && (
              <p role="status" className="flex items-center gap-2 text-sm text-text-muted">
                <LoaderCircle aria-hidden="true" className="size-4 animate-spin" />
                Enviando la reserva a la tienda…
              </p>
            )}
            <p className="text-xs text-text-faint">La reserva no es un pago: la confirmás y pagás en la tienda al retirar.</p>
          </div>
        </form>
      </div>
    </Container>
  );
}
