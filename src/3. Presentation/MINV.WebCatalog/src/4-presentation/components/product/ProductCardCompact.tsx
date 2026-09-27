import clsx from 'clsx';
import { Trash2 } from 'lucide-react';
import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';
import type { Product } from '@/1-domain/catalog/types';
import { ROUTES } from '@/4-presentation/app/routes';
import { IconButton } from '@/4-presentation/components/ui/IconButton';
import { PriceTag } from '@/4-presentation/components/ui/PriceTag';
import { ProductImage } from '@/4-presentation/components/ui/ProductImage';
import { QuantityStepper } from '@/4-presentation/components/ui/QuantityStepper';
import { formatMoney } from '@/shared/format';
import { StockIndicator } from './StockIndicator';

export interface ProductCardCompactProps {
  product: Product;
  /** Texto pequeño sobre el nombre (ranura «Procesador», categoría o marca). */
  subtitle?: string;
  /** Con `quantity` y `onQuantityChange` muestra el selector de cantidad. */
  quantity?: number;
  onQuantityChange?: (quantity: number) => void;
  /** Muestra el botón «Quitar». */
  onRemove?: () => void;
  /** Acción a la derecha (botón «Elegir» del armador, por ejemplo). */
  action?: ReactNode;
  /** Resalta la fila (pieza elegida). */
  selected?: boolean;
  /** Muestra precio × cantidad en vez del unitario. */
  showLineTotal?: boolean;
  linkToProduct?: boolean;
  className?: string;
}

/** Fila compacta de producto para el cajón «Mi armado» y las listas del armador. */
export function ProductCardCompact({
  product,
  subtitle,
  quantity,
  onQuantityChange,
  onRemove,
  action,
  selected = false,
  showLineTotal = false,
  linkToProduct = true,
  className,
}: ProductCardCompactProps) {
  const total = showLineTotal && quantity != null ? product.price * quantity : null;
  const name = linkToProduct ? (
    <Link to={ROUTES.product(product.slug)} className="rounded-sm transition-colors duration-200 hover:text-accent-hover">
      {product.shortName}
    </Link>
  ) : (
    product.shortName
  );
  return (
    <div
      className={clsx(
        'flex gap-3 rounded-xl border bg-surface-2 p-3 transition-colors duration-200',
        selected ? 'border-accent/60 shadow-glow-accent' : 'border-border',
        className,
      )}
    >
      <ProductImage src={product.image} alt="" padding="sm" className="w-16 shrink-0 sm:w-20" />
      <div className="flex min-w-0 flex-1 flex-col gap-1">
        {subtitle && <p className="truncate text-[0.6875rem] font-semibold uppercase tracking-wide text-text-faint">{subtitle}</p>}
        <p className="line-clamp-2 text-sm leading-snug font-medium text-text">{name}</p>
        <div className="mt-auto flex flex-wrap items-center justify-between gap-x-3 gap-y-1">
          <PriceTag price={product.price} listPrice={product.listPrice} size="sm" showSaving={false} />
          <StockIndicator product={product} />
        </div>
        {(quantity != null && onQuantityChange) || onRemove || total != null ? (
          <div className="mt-1 flex items-center justify-between gap-2">
            <div className="flex items-center gap-2">
              {quantity != null && onQuantityChange && (
                <QuantityStepper size="sm" value={quantity} onChange={onQuantityChange} label={`Cantidad de ${product.shortName}`} />
              )}
              {quantity != null && !onQuantityChange && <span className="text-sm text-text-muted">× {quantity}</span>}
              {onRemove && <IconButton size="sm" label={`Quitar ${product.shortName} del armado`} icon={<Trash2 />} onClick={onRemove} />}
            </div>
            {total != null && quantity != null && quantity > 1 && (
              <span className="text-sm font-semibold text-text tabular-nums">{formatMoney(total)}</span>
            )}
          </div>
        ) : null}
      </div>
      {action && <div className="flex shrink-0 items-center">{action}</div>}
    </div>
  );
}
