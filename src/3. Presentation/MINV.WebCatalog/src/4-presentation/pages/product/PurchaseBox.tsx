// Bloque de compra de la ficha: precio grande con lista tachada y ahorro, IVA incluido, disponibilidad FRESCA (V6: la
// ficha consulta `GET /products/{slug}` al abrirse y muestra disponible y reservado), garantía, ranura del armador («Va
// en: Tarjeta de video») y beneficios.
//
// V7 · la tienda funciona como carrito de compras: TODO producto con disponibilidad (también consolas, juegos,
// portátiles y lo que antes solo ofrecía «Consultar por WhatsApp») tiene cantidad (con lo disponible como tope),
// «Agregar al carrito» y «Reservar ahora». «Reservar ahora» lleva directo a la reserva de ESE solo artículo
// (`/reservar?sku=…&cantidad=…`), sin pasar por el carrito ni por «Armá tu PC». Debajo quedan, como opciones
// secundarias, el armador (para las piezas que ocupan una ranura) o la consulta por WhatsApp (para el resto).

import { ArrowRight, Check, CreditCard, LoaderCircle, MessageCircle, PcCase, Plus, ScanBarcode, ShieldCheck, ShoppingCart, Store, Truck, Zap } from 'lucide-react';
import { useId, useState } from 'react';
import { Link } from 'react-router-dom';
import { maxQuantityFor } from '@/1-domain/builder/build';
import { slotForProduct } from '@/1-domain/builder/slots';
import { maxCartQuantity } from '@/1-domain/cart/cart';
import { savingAmount } from '@/1-domain/catalog/money';
import { isAvailable, reservedLabel, stockStatus, unavailableLabel } from '@/1-domain/catalog/stock';
import type { Product } from '@/1-domain/catalog/types';
import { ROUTES } from '@/4-presentation/app/routes';
import { unitsLabel } from '@/4-presentation/components/cart/cartText';
import { StockIndicator } from '@/4-presentation/components/product/StockIndicator';
import { Button } from '@/4-presentation/components/ui/Button';
import { Card } from '@/4-presentation/components/ui/Card';
import { CategoryIcon } from '@/4-presentation/components/ui/CategoryIcon';
import { PriceTag } from '@/4-presentation/components/ui/PriceTag';
import { QuantityStepper } from '@/4-presentation/components/ui/QuantityStepper';
import { useAddToCart } from '@/4-presentation/hooks/useAddToCart';
import { useBuilder } from '@/4-presentation/hooks/useBuilder';
import { useCartState } from '@/4-presentation/hooks/useCart';
import { useFreshProduct } from '@/4-presentation/hooks/useFreshProduct';
import { useStore } from '@/4-presentation/hooks/useStore';
import { STORE } from '@/shared/constants';
import { formatMoney } from '@/shared/format';
import { warrantyLabel } from './productInfo';

export interface PurchaseBoxProps {
  product: Product;
  /** Enlace a la categoría del producto (para los que no se arman: «Ver más …»). */
  categoryHref: string;
}

export function PurchaseBox({ product: snapshotProduct, categoryHref }: PurchaseBoxProps) {
  const { add: addToBuild, isInBuild, openDrawer, count } = useBuilder();
  const addToCart = useAddToCart();
  const { quantityOf } = useCartState();
  const { branch } = useStore();
  // Disponibilidad recién consultada a la tienda (regla S-07); mientras llega, la de la instantánea.
  const { product, refreshing, fresh } = useFreshProduct(snapshotProduct);
  const headingId = useId();
  const slot = slotForProduct(product);
  const available = isAvailable(product);
  const status = stockStatus(product);
  const inBuild = isInBuild(product.sku);
  const inCart = quantityOf(product.sku);
  const saving = savingAmount(product.price, product.listPrice);
  // Tope de la cantidad: lo disponible, hasta 16 por producto (el armador admite hasta 10 de una misma pieza).
  const maxQuantity = Math.max(1, maxCartQuantity(product));
  const [wanted, setWanted] = useState(1);
  const quantity = Math.min(wanted, maxQuantity);
  const reserved = reservedLabel(product);

  const buildLabel = inBuild ? (slot?.multiple ? 'Agregar otra vez al armado' : 'En tu armado') : 'Agregar al armado';

  return (
    <Card as="section" aria-labelledby={headingId} elevated padding="md" className="animate-fade-up">
      <h2 id={headingId} className="sr-only">
        Comprar {product.shortName}
      </h2>

      <div className="flex flex-wrap items-end justify-between gap-x-4 gap-y-2">
        <PriceTag price={product.price} listPrice={product.listPrice} size="xl" showTax />
        {saving > 0 && <p className="text-sm font-semibold text-cta-hover">Ahorrás {formatMoney(saving)}</p>}
      </div>

      <dl className="mt-4 space-y-2.5 text-sm">
        <div className="flex flex-wrap items-center gap-x-2.5 gap-y-1" data-testid="disponibilidad-ficha" data-fresh={fresh ? 'true' : 'false'}>
          <dt className="sr-only">Disponibilidad</dt>
          <dd className="flex flex-wrap items-center gap-x-2.5 gap-y-1">
            <StockIndicator product={product} className="text-sm" />
            {reserved && <span className="text-xs text-text-muted">{reserved}</span>}
            {refreshing && (
              <span className="inline-flex items-center gap-1 text-xs text-text-faint" role="status">
                <LoaderCircle aria-hidden="true" className="size-3.5 animate-spin" />
                Consultando disponibilidad…
              </span>
            )}
          </dd>
        </div>
        <div className="flex items-center gap-2.5 text-text-muted">
          <ShieldCheck aria-hidden="true" className="size-5 shrink-0 text-accent" />
          <dt className="sr-only">Garantía</dt>
          <dd>{warrantyLabel(product.warrantyMonths)}</dd>
        </div>
        {slot && (
          <div className="flex items-center gap-2.5 text-text-muted">
            <CategoryIcon name={slot.icon} className="size-5 shrink-0 text-accent" />
            <dt>Va en:</dt>
            <dd className="font-semibold text-text">{slot.label}</dd>
          </div>
        )}
        {product.serialized && (
          <div className="flex items-center gap-2.5 text-text-muted">
            <ScanBarcode aria-hidden="true" className="size-5 shrink-0 text-accent" />
            <dt className="sr-only">Trazabilidad</dt>
            <dd>Se entrega con su número de serie registrado</dd>
          </div>
        )}
      </dl>

      <div className="mt-5 flex flex-col gap-3" data-testid="acciones-ficha">
        {available ? (
          <>
            {maxQuantity > 1 && (
              <div className="flex items-center justify-between gap-3">
                <span aria-hidden="true" className="text-sm font-medium text-text-muted">
                  Cantidad
                </span>
                <QuantityStepper value={quantity} onChange={setWanted} min={1} max={maxQuantity} label={`Cantidad de ${product.shortName}`} />
              </div>
            )}
            <Button size="lg" variant="brand" fullWidth leftIcon={<ShoppingCart />} onClick={() => addToCart(product, quantity)}>
              Agregar al carrito
            </Button>
            <Button size="lg" variant="accent" fullWidth leftIcon={<Zap />} to={ROUTES.checkoutItem(product.sku, quantity)}>
              Reservar ahora
            </Button>
            {inCart > 0 && (
              <p className="text-sm text-text-muted" data-testid="en-carrito-ficha">
                Tenés {unitsLabel(inCart)} en tu carrito.{' '}
                <Link to={ROUTES.cart} className="inline-flex min-h-6 items-center rounded-sm font-semibold text-accent transition-colors duration-200 hover:text-accent-hover">
                  Ver carrito
                </Link>
              </p>
            )}
          </>
        ) : (
          <>
            <Button size="lg" variant="subtle" fullWidth disabled>
              {unavailableLabel(product)}
            </Button>
            <p className="text-sm text-text-muted">
              {status === 'reservado'
                ? 'Las unidades que quedan están reservadas por otros clientes; si una reserva se libera o vence, vuelven a estar disponibles. '
                : 'Sin stock por ahora. '}
              <a href={STORE.whatsappUrl} target="_blank" rel="noreferrer noopener" className="font-semibold text-accent transition-colors duration-200 hover:text-accent-hover">
                Consultanos por WhatsApp
              </a>{' '}
              para avisarte cuando vuelva.
            </p>
          </>
        )}

        <div className="mt-1 flex flex-col gap-3 border-t border-border pt-4">
          {slot ? (
            <>
              <p className="text-xs font-semibold uppercase tracking-wide text-text-faint">¿Estás armando una PC?</p>
              {available && (
                <Button
                  variant={inBuild && !slot.multiple ? 'subtle' : 'outline'}
                  fullWidth
                  leftIcon={inBuild && !slot.multiple ? <Check /> : <Plus />}
                  onClick={() => addToBuild(product, slot.key, slot.multiple ? Math.min(quantity, maxQuantityFor(product)) : undefined)}
                >
                  {buildLabel}
                </Button>
              )}
              <Button variant="ghost" fullWidth leftIcon={<PcCase />} onClick={openDrawer}>
                Ver mi armado{count > 0 ? ` (${count})` : ''}
              </Button>
            </>
          ) : (
            <>
              <p className="text-xs font-semibold uppercase tracking-wide text-text-faint">¿Tenés dudas?</p>
              <Button href={STORE.whatsappUrl} target="_blank" rel="noreferrer noopener" variant="outline" fullWidth leftIcon={<MessageCircle />}>
                Consultar por WhatsApp
              </Button>
              <Button to={categoryHref} variant="ghost" fullWidth rightIcon={<ArrowRight />}>
                Ver más de {product.categoryName}
              </Button>
            </>
          )}
        </div>
      </div>

      <ul className="mt-5 space-y-2.5 border-t border-border pt-4 text-sm text-text-muted">
        <li className="flex items-start gap-2.5">
          <Truck aria-hidden="true" className="mt-0.5 size-5 shrink-0 text-accent" />
          <span>Envíos a todo el país</span>
        </li>
        <li className="flex items-start gap-2.5">
          <CreditCard aria-hidden="true" className="mt-0.5 size-5 shrink-0 text-accent" />
          <span>Pagá con QR, tarjeta o transferencia</span>
        </li>
        <li className="flex items-start gap-2.5">
          <Store aria-hidden="true" className="mt-0.5 size-5 shrink-0 text-accent" />
          <span>Reservá y retirá en {branch.name}: la reserva se confirma y paga en la tienda</span>
        </li>
      </ul>
    </Card>
  );
}
