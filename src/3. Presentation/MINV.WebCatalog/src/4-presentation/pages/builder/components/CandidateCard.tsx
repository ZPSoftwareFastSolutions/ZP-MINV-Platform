// Tarjeta compacta de un candidato a una ranura: imagen, nombre, 2-3 especificaciones clave, precio, disponibilidad y la
// acción («Elegir»/«Quitar» en ranuras simples; «Agregar» + cantidad en las múltiples, con lo disponible como tope).

import clsx from 'clsx';
import { Check, Plus, Trash2 } from 'lucide-react';
import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { maxQuantityFor } from '@/1-domain/builder/build';
import type { BuildSlot } from '@/1-domain/builder/types';
import { isAvailable, unavailableLabel } from '@/1-domain/catalog/stock';
import type { Product } from '@/1-domain/catalog/types';
import { ROUTES } from '@/4-presentation/app/routes';
import { ProductBadges } from '@/4-presentation/components/product/ProductBadges';
import { StockIndicator } from '@/4-presentation/components/product/StockIndicator';
import { Button } from '@/4-presentation/components/ui/Button';
import { IconButton } from '@/4-presentation/components/ui/IconButton';
import { PriceTag } from '@/4-presentation/components/ui/PriceTag';
import { ProductImage } from '@/4-presentation/components/ui/ProductImage';
import { QuantityStepper } from '@/4-presentation/components/ui/QuantityStepper';
import { keySpecsFor } from '../builderSteps';

export interface CandidateCardProps {
  product: Product;
  slot: BuildSlot;
  /** Unidades ya en el armado (0 si no está). */
  quantity: number;
  onChoose: (product: Product) => void;
  onRemove: (sku: string) => void;
  onQuantity: (sku: string, quantity: number) => void;
}

export function CandidateCard({ product, slot, quantity, onChoose, onRemove, onQuantity }: CandidateCardProps) {
  const selected = quantity > 0;
  const available = isAvailable(product);
  const specs = keySpecsFor(product, slot.key);

  // Todos los controles miden 44 px (objetivo táctil): el armador se usa sobre todo desde el celular.
  let action: ReactNode;
  if (!available && !selected) {
    action = (
      <Button variant="subtle" disabled fullWidth aria-label={`${unavailableLabel(product)}: ${product.shortName}`}>
        {unavailableLabel(product)}
      </Button>
    );
  } else if (slot.multiple) {
    action = selected ? (
      <div className="flex items-center gap-2 sm:justify-end">
        <QuantityStepper value={quantity} max={maxQuantityFor(product)} onChange={(next) => onQuantity(product.sku, next)} label={`Cantidad de ${product.shortName}`} />
        <IconButton variant="subtle" label={`Quitar ${product.shortName} del armado`} icon={<Trash2 />} onClick={() => onRemove(product.sku)} />
      </div>
    ) : (
      <Button variant="primary" fullWidth leftIcon={<Plus />} aria-label={`Agregar ${product.shortName} al armado`} onClick={() => onChoose(product)}>
        Agregar
      </Button>
    );
  } else {
    action = selected ? (
      <Button variant="subtle" fullWidth leftIcon={<Trash2 />} aria-label={`Quitar ${product.shortName} del armado`} onClick={() => onRemove(product.sku)}>
        Quitar
      </Button>
    ) : (
      <Button variant="primary" fullWidth leftIcon={<Check />} aria-label={`Elegir ${product.shortName}`} onClick={() => onChoose(product)}>
        Elegir
      </Button>
    );
  }

  return (
    <li
      aria-current={selected ? 'true' : undefined}
      className={clsx(
        'grid grid-cols-[4rem_minmax(0,1fr)] gap-x-3 gap-y-3 rounded-xl border bg-surface-2 p-3 transition-[border-color,box-shadow] duration-200 sm:grid-cols-[5rem_minmax(0,1fr)_auto] sm:items-center sm:gap-x-4',
        selected ? 'border-accent/60 shadow-glow-accent' : 'border-border hover:border-border-strong',
      )}
    >
      <ProductImage src={product.image} alt="" padding="sm" className="w-16 sm:w-20" />
      <div className="min-w-0">
        <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
          <p className="text-[0.6875rem] font-semibold uppercase tracking-wide text-text-faint">{product.brand}</p>
          <ProductBadges product={product} max={2} variant="soft" />
          {selected && (
            <span className="inline-flex items-center gap-1 text-[0.6875rem] font-semibold uppercase tracking-wide text-accent-hover">
              <Check aria-hidden="true" className="size-3.5" />
              {slot.multiple ? 'En tu armado' : 'Elegida'}
            </span>
          )}
        </div>
        <p className="mt-1 line-clamp-2 text-sm leading-snug font-medium text-text">
          <Link to={ROUTES.product(product.slug)} className="rounded-sm transition-colors duration-200 hover:text-accent-hover">
            {product.shortName}
          </Link>
        </p>
        {specs.length > 0 && (
          <dl className="mt-1.5 flex flex-wrap gap-x-4 gap-y-1 text-xs">
            {specs.map((spec) => (
              <div key={spec.key} className="flex min-w-0 items-baseline gap-1">
                <dt className="shrink-0 text-text-faint">{spec.label}</dt>
                <dd className="truncate font-medium text-text-muted">{spec.text}</dd>
              </div>
            ))}
          </dl>
        )}
        <div className="mt-2 flex flex-wrap items-center justify-between gap-x-3 gap-y-1">
          <PriceTag price={product.price} listPrice={product.listPrice} size="sm" />
          <StockIndicator product={product} />
        </div>
      </div>
      <div className="col-span-2 sm:col-span-1 sm:min-w-44 sm:justify-self-end">{action}</div>
    </li>
  );
}
