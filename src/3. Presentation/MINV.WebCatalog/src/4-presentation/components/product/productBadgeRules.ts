import { isAvailable } from '@/1-domain/catalog/stock';
import type { Product } from '@/1-domain/catalog/types';
import type { BadgeTone } from '@/4-presentation/components/ui/Badge';

export interface ProductBadgeDescriptor {
  key: string;
  tone: BadgeTone;
  label: string;
}

/** Insignias que corresponden a un producto, en orden de prioridad (agotado, oferta, nuevo, destacado, condición). */
export function productBadges(product: Product, options: { max?: number } = {}): ProductBadgeDescriptor[] {
  const badges: ProductBadgeDescriptor[] = [];
  if (!isAvailable(product)) badges.push({ key: 'agotado', tone: 'agotado', label: 'Agotado' });
  if (product.tags.includes('oferta')) badges.push({ key: 'oferta', tone: 'oferta', label: 'Oferta' });
  if (product.tags.includes('nuevo')) badges.push({ key: 'nuevo', tone: 'nuevo', label: 'Nuevo' });
  if (product.tags.includes('destacado')) badges.push({ key: 'destacado', tone: 'destacado', label: 'Destacado' });
  if (product.condition !== 'Nuevo') badges.push({ key: 'condicion', tone: 'condicion', label: product.condition });
  return badges.slice(0, options.max ?? badges.length);
}
