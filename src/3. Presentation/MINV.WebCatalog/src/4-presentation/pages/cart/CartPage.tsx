// CARRITO (`/carrito`) · la tienda funciona como carrito de compras (V7): acá se ve lo elegido, se cambia la cantidad, se
// quita, se vacía (con confirmación) y se pasa a reservar (`/reservar`). El carrito guarda solo SKU y cantidad: el
// nombre, el precio y lo disponible salen del catálogo, que esta página vuelve a consultar al abrirse. Si algo se agotó
// o bajó por debajo de lo pedido, la línea lo marca y se ofrece ajustar; mientras haya algo por ajustar no se reserva.

import { ArrowLeft, ArrowRight, LoaderCircle, RefreshCw, ShoppingCart, Trash2 } from 'lucide-react';
import { useCallback, useEffect, useRef, useState } from 'react';
import { CART_LIMITS } from '@/1-domain/cart/cart';
import { ROUTES } from '@/4-presentation/app/routes';
import { adjustmentText, cartCountLabel, unitsLabel } from '@/4-presentation/components/cart/cartText';
import { Alert } from '@/4-presentation/components/ui/Alert';
import { Breadcrumbs } from '@/4-presentation/components/ui/Breadcrumbs';
import { Button } from '@/4-presentation/components/ui/Button';
import { Card } from '@/4-presentation/components/ui/Card';
import { Container } from '@/4-presentation/components/ui/Container';
import { EmptyState } from '@/4-presentation/components/ui/EmptyState';
import { useCart } from '@/4-presentation/hooks/useCart';
import { useDocumentMeta } from '@/4-presentation/hooks/useDocumentMeta';
import { useServices } from '@/4-presentation/hooks/useServices';
import { useStore } from '@/4-presentation/hooks/useStore';
import { useToast } from '@/4-presentation/hooks/useToast';
import { formatMoney } from '@/shared/format';
import { CartLineRow } from './CartLineRow';

/** Estado de la consulta de disponibilidad: en curso, recién consultada o fallida (se muestra la última conocida). */
type Freshness = 'checking' | 'fresh' | 'stale';

export function CartPage() {
  useDocumentMeta({ title: 'Carrito', description: 'Tu carrito de compras en Tech Zone Gaming: revisá los productos, ajustá las cantidades y reservá para retirar en la tienda.' });
  const cart = useCart();
  const { refresh } = useServices();
  const { branch } = useStore();
  const toast = useToast();
  const headingRef = useRef<HTMLHeadingElement>(null);
  const clearButtonRef = useRef<HTMLButtonElement>(null);
  const [freshness, setFreshness] = useState<Freshness>('checking');
  const [confirmClear, setConfirmClear] = useState(false);
  const { lines, review, count, total, persistent } = cart;
  const isEmpty = lines.length === 0;

  // La pantalla de reserva se descarga aparte: se adelanta al abrir el carrito para que «Reservar» abra al instante.
  useEffect(() => {
    void import('./CheckoutPage').catch(() => undefined);
  }, []);

  // Disponibilidad fresca al abrir el carrito (y con «Actualizar»): se vuelve a pedir el catálogo a la tienda.
  useEffect(() => {
    let active = true;
    void refresh().then((updated) => {
      if (active) setFreshness(updated ? 'fresh' : 'stale');
    });
    return () => {
      active = false;
    };
  }, [refresh]);

  const checkAgain = useCallback(() => {
    setFreshness('checking');
    void refresh().then((updated) => setFreshness(updated ? 'fresh' : 'stale'));
  }, [refresh]);

  /** Después de quitar una línea su botón desaparece: el foco vuelve al título para no perderse. */
  const focusHeading = () => {
    requestAnimationFrame(() => headingRef.current?.focus());
  };

  const remove = (sku: string, name: string) => {
    cart.remove(sku);
    toast.notify({ tone: 'info', group: 'carrito:cambio', title: 'Quitado del carrito', description: name });
    focusHeading();
  };

  const adjust = (sku?: string) => {
    const { changes } = cart.adjust(sku);
    if (changes.length === 0) return;
    toast.notify({ tone: 'success', group: 'carrito:cambio', title: 'Carrito ajustado a lo disponible', description: adjustmentText(changes) });
    focusHeading();
  };

  const clear = () => {
    cart.clear();
    setConfirmClear(false);
    toast.notify({ tone: 'info', group: 'carrito:cambio', title: 'Vaciaste el carrito' });
    focusHeading();
  };

  const cancelClear = () => {
    setConfirmClear(false);
    requestAnimationFrame(() => clearButtonRef.current?.focus());
  };

  return (
    <Container className="py-6 sm:py-8 lg:py-10">
      <Breadcrumbs items={[{ label: 'Carrito' }]} />
      <header className="mt-4">
        <h1 ref={headingRef} tabIndex={-1} className="text-3xl outline-none sm:text-4xl">
          Carrito
        </h1>
        <p className="mt-2 text-sm text-text-muted" data-testid="carrito-resumen">
          {isEmpty ? 'Todavía no agregaste productos.' : `${cartCountLabel(count)} · Reservás ahora y pagás al retirar en la tienda.`}
        </p>
      </header>

      {isEmpty ? (
        <EmptyState
          className="mt-8"
          icon={<ShoppingCart />}
          title="Tu carrito está vacío"
          description="Elegí lo que necesites del catálogo: un solo producto o varios. Los reservás en un paso y los retirás en la tienda."
        >
          <Button to={ROUTES.catalog} variant="brand" rightIcon={<ArrowRight />}>
            Ver el catálogo
          </Button>
          <Button to={ROUTES.offers} variant="outline">
            Ver ofertas
          </Button>
        </EmptyState>
      ) : (
        <div className="mt-6 grid gap-6 lg:grid-cols-[minmax(0,1fr)_22rem] lg:items-start lg:gap-8">
          <section aria-labelledby="carrito-productos" className="min-w-0">
            <div className="flex flex-wrap items-center justify-between gap-x-4 gap-y-2">
              <h2 id="carrito-productos" className="font-display text-xl font-semibold text-text">
                Productos
              </h2>
              <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-text-muted" data-testid="carrito-disponibilidad" data-freshness={freshness}>
                {freshness === 'checking' ? (
                  <span className="inline-flex min-h-9 items-center gap-1.5" role="status">
                    <LoaderCircle aria-hidden="true" className="size-4 animate-spin" />
                    Consultando disponibilidad…
                  </span>
                ) : (
                  <>
                    <span role="status">{freshness === 'fresh' ? 'Disponibilidad recién consultada' : 'Disponibilidad sin actualizar'}</span>
                    <Button size="sm" variant="ghost" leftIcon={<RefreshCw />} onClick={checkAgain}>
                      Actualizar
                    </Button>
                  </>
                )}
              </div>
            </div>

            {freshness === 'stale' && (
              <Alert tone="warning" title="No pudimos consultar la disponibilidad" className="mt-3">
                Te mostramos la última que conocemos. Revisá tu conexión y tocá «Actualizar»; al reservar, la tienda vuelve a comprobar el stock.
              </Alert>
            )}

            {!persistent && (
              <Alert tone="info" title="Tu carrito no se está guardando" className="mt-3">
                Este navegador no nos deja guardar el carrito (puede estar lleno o en modo privado). Funciona igual, pero se pierde al cerrar la pestaña.
              </Alert>
            )}

            {review.issues.length > 0 && (
              <Alert
                tone="warning"
                className="mt-3"
                title={review.issues.length === 1 ? 'Un producto cambió de disponibilidad' : `${review.issues.length} productos cambiaron de disponibilidad`}
                actions={
                  <Button size="sm" variant="primary" onClick={() => adjust()}>
                    Ajustar carrito
                  </Button>
                }
              >
                Ajustá las cantidades a lo que hay hoy (o quitá lo que se agotó) para poder reservar.
              </Alert>
            )}

            <ul aria-labelledby="carrito-productos" className="mt-4 flex flex-col gap-3">
              {lines.map((line) => (
                <CartLineRow
                  key={line.sku}
                  line={line}
                  onQuantityChange={(quantity) => cart.setQuantity(line.sku, quantity)}
                  onRemove={() => remove(line.sku, line.product?.shortName ?? line.sku)}
                  onAdjust={() => adjust(line.sku)}
                />
              ))}
            </ul>
            <p className="mt-3 text-xs text-text-faint">
              Hasta {CART_LIMITS.maxLines} productos distintos y {CART_LIMITS.maxQuantityPerLine} unidades de cada uno por reserva.
            </p>
          </section>

          <Card as="section" aria-labelledby="carrito-total" elevated padding="md" className="lg:sticky lg:top-32">
            <h2 id="carrito-total" className="font-display text-xl font-semibold text-text">
              Resumen
            </h2>
            <dl className="mt-4 space-y-2 text-sm">
              <div className="flex items-center justify-between gap-3">
                <dt className="text-text-muted">Unidades</dt>
                <dd className="font-medium text-text tabular-nums">{unitsLabel(count)}</dd>
              </div>
              {review.savings > 0 && (
                <div className="flex items-center justify-between gap-3">
                  <dt className="text-text-muted">Ahorrás</dt>
                  <dd className="font-medium text-cta-hover tabular-nums">{formatMoney(review.savings)}</dd>
                </div>
              )}
              <div className="flex items-end justify-between gap-3 border-t border-border pt-3">
                <dt className="text-base font-semibold text-text">Total</dt>
                <dd className="font-display text-2xl font-semibold text-text tabular-nums" data-testid="carrito-total-importe">
                  {formatMoney(total)}
                </dd>
              </div>
            </dl>
            <p className="mt-2 text-xs text-text-faint">Precios en bolivianos con IVA incluido. Retirás y pagás en {branch.name}.</p>

            <div className="mt-5 flex flex-col gap-2">
              <Button to={ROUTES.checkout} size="lg" variant="brand" fullWidth rightIcon={<ArrowRight />} disabled={!review.reservable} aria-describedby={review.reservable ? undefined : 'carrito-bloqueo'}>
                Reservar
              </Button>
              {!review.reservable && (
                <p id="carrito-bloqueo" className="text-xs text-warning-text">
                  Ajustá el carrito a lo disponible para poder reservar.
                </p>
              )}
              <Button to={ROUTES.catalog} variant="outline" fullWidth leftIcon={<ArrowLeft />}>
                Seguir comprando
              </Button>
              {confirmClear ? (
                <div className="rounded-xl border border-danger/40 bg-danger-soft p-3 text-sm" role="alertdialog" aria-labelledby="carrito-vaciar-pregunta" data-testid="carrito-vaciar-confirmar">
                  <p id="carrito-vaciar-pregunta" className="font-semibold text-text">
                    ¿Vaciar el carrito?
                  </p>
                  <p className="mt-0.5 text-text-muted">{count === 1 ? 'Se quita el producto que elegiste.' : `Se quitan los ${count} productos que elegiste.`} No se puede deshacer.</p>
                  <div className="mt-3 flex flex-wrap gap-2">
                    <Button size="sm" variant="ghost" autoFocus onClick={cancelClear}>
                      No, conservar
                    </Button>
                    <Button size="sm" variant="danger" onClick={clear}>
                      Sí, vaciar
                    </Button>
                  </div>
                </div>
              ) : (
                <Button ref={clearButtonRef} variant="ghost" fullWidth leftIcon={<Trash2 />} onClick={() => setConfirmClear(true)}>
                  Vaciar carrito
                </Button>
              )}
            </div>
          </Card>
        </div>
      )}
    </Container>
  );
}
