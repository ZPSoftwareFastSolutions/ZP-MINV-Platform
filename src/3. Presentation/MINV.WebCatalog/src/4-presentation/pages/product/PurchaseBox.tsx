// Bloque de compra de la ficha: precio grande con lista tachada y ahorro, IVA incluido, disponibilidad FRESCA (V6: la
// ficha consulta `GET /products/{slug}` al abrirse y muestra disponible y reservado), garantía, ranura del armador («Va
// en: Tarjeta de video»), cantidad (solo en ranuras múltiples, con lo disponible como tope), «Agregar al armado», «Ver mi
// armado» y beneficios. Todo el estado del armado vive en memoria (useBuilder).

import { ArrowRight, Check, CreditCard, LoaderCircle, MessageCircle, PcCase, Plus, ScanBarcode, ShieldCheck, Store, Truck } from 'lucide-react';
import { useId, useState } from 'react';
import { maxQuantityFor } from '@/1-domain/builder/build';
import { slotForProduct } from '@/1-domain/builder/slots';
import { savingAmount } from '@/1-domain/catalog/money';
import { isAvailable, reservedLabel, stockStatus, unavailableLabel } from '@/1-domain/catalog/stock';
import type { Product } from '@/1-domain/catalog/types';
import { StockIndicator } from '@/4-presentation/components/product/StockIndicator';
import { Button } from '@/4-presentation/components/ui/Button';
import { Card } from '@/4-presentation/components/ui/Card';
import { CategoryIcon } from '@/4-presentation/components/ui/CategoryIcon';
import { PriceTag } from '@/4-presentation/components/ui/PriceTag';
import { QuantityStepper } from '@/4-presentation/components/ui/QuantityStepper';
import { useBuilder } from '@/4-presentation/hooks/useBuilder';
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
  const { add, isInBuild, openDrawer, count } = useBuilder();
  const { branch } = useStore();
  // Disponibilidad recién consultada a la tienda (regla S-07); mientras llega, la de la instantánea.
  const { product, refreshing, fresh } = useFreshProduct(snapshotProduct);
  const headingId = useId();
  const quantityId = useId();
  const slot = slotForProduct(product);
  const available = isAvailable(product);
  const status = stockStatus(product);
  const inBuild = isInBuild(product.sku);
  const saving = savingAmount(product.price, product.listPrice);
  const maxQuantity = maxQuantityFor(product);
  const [quantity, setQuantity] = useState(1);
  const reserved = reservedLabel(product);

  const addLabel = !available ? unavailableLabel(product) : inBuild ? (slot?.multiple ? 'Agregar otra vez' : 'En tu armado') : 'Agregar al armado';

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

      <div className="mt-5 flex flex-col gap-3">
        {slot ? (
          <>
            {slot.multiple && available && (
              <div className="flex items-center justify-between gap-3">
                <span id={quantityId} className="text-sm font-medium text-text-muted">
                  Cantidad
                </span>
                <QuantityStepper value={Math.min(quantity, maxQuantity)} onChange={setQuantity} min={1} max={maxQuantity} label={`Cantidad de ${product.shortName}`} />
              </div>
            )}
            <Button
              size="lg"
              variant={inBuild && !slot.multiple ? 'subtle' : 'brand'}
              fullWidth
              disabled={!available}
              leftIcon={inBuild && !slot.multiple ? <Check /> : <Plus />}
              onClick={() => add(product, slot.key, slot.multiple ? Math.min(quantity, maxQuantity) : undefined)}
            >
              {addLabel}
            </Button>
            <Button size="lg" variant="outline" fullWidth leftIcon={<PcCase />} onClick={openDrawer}>
              Ver mi armado{count > 0 ? ` (${count})` : ''}
            </Button>
          </>
        ) : (
          <>
            <p className="rounded-xl border border-border bg-surface px-3 py-2.5 text-sm text-text-muted">
              Este producto no ocupa una ranura del armador «Armá tu PC»: se compra por separado.
            </p>
            <Button href={STORE.whatsappUrl} target="_blank" rel="noreferrer noopener" size="lg" variant="brand" fullWidth leftIcon={<MessageCircle />}>
              Consultar por WhatsApp
            </Button>
            <Button to={categoryHref} size="lg" variant="outline" fullWidth rightIcon={<ArrowRight />}>
              Ver más de {product.categoryName}
            </Button>
          </>
        )}
        {!available && (
          <p className="text-sm text-text-muted">
            {status === 'reservado'
              ? 'Las unidades que quedan están reservadas por otros clientes; si una reserva se libera o vence, vuelven a estar disponibles. '
              : 'Sin stock por ahora. '}
            <a href={STORE.whatsappUrl} target="_blank" rel="noreferrer noopener" className="font-semibold text-accent transition-colors duration-200 hover:text-accent-hover">
              Consultanos por WhatsApp
            </a>{' '}
            para avisarte cuando vuelva.
          </p>
        )}
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
