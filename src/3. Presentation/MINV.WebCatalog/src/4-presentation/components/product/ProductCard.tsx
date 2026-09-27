import clsx from 'clsx';
import { ArrowRight, Check, Plus } from 'lucide-react';
import { Link } from 'react-router-dom';
import { isBuildable } from '@/1-domain/builder/slots';
import { isOnSale, savingLabel } from '@/1-domain/catalog/money';
import { isAvailable } from '@/1-domain/catalog/stock';
import type { Product } from '@/1-domain/catalog/types';
import { ROUTES } from '@/4-presentation/app/routes';
import { Badge } from '@/4-presentation/components/ui/Badge';
import { Button } from '@/4-presentation/components/ui/Button';
import { Card } from '@/4-presentation/components/ui/Card';
import { PriceTag } from '@/4-presentation/components/ui/PriceTag';
import { ProductImage } from '@/4-presentation/components/ui/ProductImage';
import { Skeleton } from '@/4-presentation/components/ui/Skeleton';
import { useBuilder } from '@/4-presentation/hooks/useBuilder';
import { ProductBadges } from './ProductBadges';
import { StockIndicator } from './StockIndicator';

export interface ProductCardProps {
  product: Product;
  /** Carga la imagen sin lazy (tarjetas sobre el pliegue). */
  priority?: boolean;
  /** Muestra la categoría junto a la marca (por defecto sí). */
  showCategory?: boolean;
  className?: string;
}

/**
 * Tarjeta de producto: imagen en loseta, insignias, marca, nombre (2 líneas), 2 características, precio con lista
 * tachada, disponibilidad y «Agregar al armado» (ranura inferida). Toda la tarjeta enlaza al detalle.
 */
export function ProductCard({ product, priority = false, showCategory = true, className }: ProductCardProps) {
  const { add, isInBuild } = useBuilder();
  const available = isAvailable(product);
  const buildable = isBuildable(product);
  const inBuild = isInBuild(product.sku);
  const detail = ROUTES.product(product.slug);

  return (
    <Card as="article" padding="none" interactive className={clsx('group relative flex h-full flex-col', className)}>
      <div className="relative p-3 pb-0">
        <ProductImage src={product.image} alt={product.name} priority={priority} padding="md" imgClassName="transition-transform duration-300 group-hover:scale-[1.04]" />
        <ProductBadges product={product} className="absolute top-5 left-5" />
        {isOnSale(product.price, product.listPrice) && (
          <Badge tone="oferta" variant="solid" size="sm" className="absolute top-5 right-5">
            {savingLabel(product.price, product.listPrice)}
          </Badge>
        )}
      </div>

      <div className="flex flex-1 flex-col gap-2 p-4">
        <p className="truncate text-xs font-semibold uppercase tracking-wide text-text-faint">
          {product.brand}
          {showCategory && <span className="font-medium normal-case tracking-normal"> · {product.categoryName}</span>}
        </p>
        <h3 className="line-clamp-2 text-[0.9375rem] leading-snug font-medium text-text">
          <Link to={detail} className="rounded-sm after:absolute after:inset-0 after:rounded-card after:content-['']">
            {product.shortName}
          </Link>
        </h3>
        {product.highlights.length > 0 && (
          <ul className="space-y-0.5 text-xs text-text-muted">
            {product.highlights.slice(0, 2).map((highlight) => (
              <li key={highlight} className="line-clamp-1">
                {highlight}
              </li>
            ))}
          </ul>
        )}
        <div className="mt-auto flex flex-wrap items-end justify-between gap-x-3 gap-y-1 pt-2">
          <PriceTag price={product.price} listPrice={product.listPrice} size="md" showSaving={false} />
          <StockIndicator product={product} className="pb-1" />
        </div>
        {buildable ? (
          <Button
            type="button"
            variant={inBuild ? 'subtle' : 'primary'}
            fullWidth
            disabled={!available}
            leftIcon={inBuild ? <Check /> : <Plus />}
            aria-label={available ? `${inBuild ? 'En tu armado, agregar otra vez' : 'Agregar al armado'}: ${product.shortName}` : `Agotado: ${product.shortName}`}
            onClick={() => add(product)}
            className="relative z-10"
          >
            {!available ? 'Agotado' : inBuild ? 'En tu armado' : 'Agregar al armado'}
          </Button>
        ) : (
          <Button to={detail} variant="outline" fullWidth rightIcon={<ArrowRight />} className="relative z-10">
            Ver producto
          </Button>
        )}
      </div>
    </Card>
  );
}

/** Esqueleto con la misma silueta de la tarjeta (para estados de carga o suspensión). */
export function ProductCardSkeleton({ className }: { className?: string }) {
  return (
    <Card padding="none" className={clsx('flex h-full flex-col', className)} aria-hidden="true">
      <div className="p-3 pb-0">
        <Skeleton className="aspect-square w-full rounded-xl" />
      </div>
      <div className="flex flex-1 flex-col gap-3 p-4">
        <Skeleton className="h-3 w-1/2" />
        <Skeleton className="h-4 w-full" />
        <Skeleton className="h-4 w-4/5" />
        <Skeleton className="h-3 w-2/3" />
        <div className="mt-auto flex items-end justify-between pt-2">
          <Skeleton className="h-6 w-24" />
          <Skeleton className="h-3 w-16" />
        </div>
        <Skeleton className="h-11 w-full rounded-xl" />
      </div>
    </Card>
  );
}
