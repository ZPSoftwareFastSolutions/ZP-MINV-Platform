import clsx from 'clsx';
import type { Product } from '@/1-domain/catalog/types';
import { Badge } from '@/4-presentation/components/ui/Badge';
import { productBadges } from './productBadgeRules';

export interface ProductBadgesProps {
  product: Product;
  /** Cuántas insignias mostrar como máximo (2 en tarjetas). */
  max?: number;
  variant?: 'solid' | 'soft';
  className?: string;
}

/** Insignias de un producto (agotado, oferta, nuevo, destacado, condición). */
export function ProductBadges({ product, max = 2, variant = 'solid', className }: ProductBadgesProps) {
  const badges = productBadges(product, { max });
  if (badges.length === 0) return null;
  return (
    <div className={clsx('flex flex-wrap gap-1', className)}>
      {badges.map((badge) => (
        <Badge key={badge.key} tone={badge.tone} variant={variant} size="sm">
          {badge.label}
        </Badge>
      ))}
    </div>
  );
}
