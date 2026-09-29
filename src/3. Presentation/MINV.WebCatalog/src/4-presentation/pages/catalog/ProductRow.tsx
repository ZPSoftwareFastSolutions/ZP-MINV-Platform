// Fila de la vista en lista del catálogo: más información que la tarjeta (tres características, hasta cuatro
// especificaciones principales) con precio, disponibilidad y las acciones de compra (V7: «Agregar al carrito» y
// «Reservar ahora»; «Agregar al armado» en las piezas del armador). Toda la fila enlaza al detalle.

import clsx from 'clsx';
import { Link } from 'react-router-dom';
import type { Product } from '@/1-domain/catalog/types';
import { ROUTES } from '@/4-presentation/app/routes';
import { ProductActions } from '@/4-presentation/components/product/ProductActions';
import { ProductBadges } from '@/4-presentation/components/product/ProductBadges';
import { StockIndicator } from '@/4-presentation/components/product/StockIndicator';
import { Card } from '@/4-presentation/components/ui/Card';
import { PriceTag } from '@/4-presentation/components/ui/PriceTag';
import { ProductImage } from '@/4-presentation/components/ui/ProductImage';

export interface ProductRowProps {
  product: Product;
  priority?: boolean;
  className?: string;
}

const MAX_SPECS = 4;

export function ProductRow({ product, priority = false, className }: ProductRowProps) {
  const detail = ROUTES.product(product.slug);
  const specs = product.specs.filter((spec) => spec.filterable && spec.key !== 'condicion').slice(0, MAX_SPECS);

  return (
    <Card as="article" padding="none" interactive className={clsx('group relative flex gap-3 p-3 sm:gap-5 sm:p-4', className)}>
      <ProductImage
        src={product.image}
        alt={product.name}
        priority={priority}
        padding="sm"
        className="w-24 shrink-0 self-start sm:w-36 lg:w-40"
        imgClassName="transition-transform duration-300 group-hover:scale-[1.04]"
      />

      <div className="flex min-w-0 flex-1 flex-col gap-3 md:flex-row md:gap-6">
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-2">
            <p className="truncate text-xs font-semibold uppercase tracking-wide text-text-faint">
              {product.brand}
              <span className="font-medium normal-case tracking-normal"> · {product.categoryName}</span>
            </p>
            <ProductBadges product={product} variant="soft" max={3} />
          </div>
          <h3 className="mt-1.5 text-base leading-snug font-medium text-text">
            <Link to={detail} className="rounded-sm after:absolute after:inset-0 after:rounded-card after:content-['']">
              {product.name}
            </Link>
          </h3>
          {product.highlights.length > 0 && (
            <ul className="mt-2 space-y-0.5 text-sm text-text-muted">
              {product.highlights.slice(0, 3).map((highlight) => (
                <li key={highlight} className="line-clamp-1">
                  {highlight}
                </li>
              ))}
            </ul>
          )}
          {specs.length > 0 && (
            <dl className="mt-3 flex flex-wrap gap-1.5">
              {specs.map((spec) => (
                <div key={spec.key} className="inline-flex max-w-full items-baseline gap-1 rounded-md border border-border bg-surface-2 px-2 py-1 text-xs">
                  <dt className="shrink-0 text-text-faint">{spec.label}:</dt>
                  <dd className="truncate font-medium text-text">{spec.text}</dd>
                </div>
              ))}
            </dl>
          )}
        </div>

        <div className="flex flex-wrap items-end justify-between gap-3 md:w-48 md:shrink-0 md:flex-col md:items-end md:justify-start">
          <div className="flex flex-col items-start gap-1 md:items-end">
            <PriceTag price={product.price} listPrice={product.listPrice} size="lg" align="start" className="md:items-end md:text-right" />
            <StockIndicator product={product} />
          </div>
          <ProductActions product={product} className="w-full sm:w-56 md:w-full" />
        </div>
      </div>
    </Card>
  );
}
