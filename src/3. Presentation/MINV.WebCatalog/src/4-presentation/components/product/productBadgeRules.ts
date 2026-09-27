import { isAvailable } from '@/1-domain/catalog/stock';
import type { Product } from '@/1-domain/catalog/types';
import type { BadgeTone } from '@/4-presentation/components/ui/Badge';
import { savingLabel } from '@/4-presentation/i18n/priceLabels';

export interface ProductBadgeDescriptor {
  key: string;
  tone: BadgeTone;
  label: string;
}

export interface ProductBadgeOptions {
  /** Cuántas insignias como máximo (todas por defecto). */
  max?: number;
  /**
   * Modo tarjeta: UNA sola insignia con prioridad agotado > oferta > nuevo > condición > destacado, y la de oferta
   * lleva el porcentaje («-13 %») en vez del texto «Oferta». Evita que las insignias se pisen en tarjetas angostas.
   */
  compact?: boolean;
}

/** Insignias que corresponden a un producto, en orden de prioridad (agotado, oferta, nuevo, condición, destacado). */
export function productBadges(product: Product, options: ProductBadgeOptions = {}): ProductBadgeDescriptor[] {
  const badges: ProductBadgeDescriptor[] = [];
  if (!isAvailable(product)) badges.push({ key: 'agotado', tone: 'agotado', label: 'Agotado' });
  if (product.tags.includes('oferta')) {
    const percent = options.compact ? savingLabel(product.price, product.listPrice) : '';
    badges.push({ key: 'oferta', tone: 'oferta', label: percent || 'Oferta' });
  }
  if (product.tags.includes('nuevo')) badges.push({ key: 'nuevo', tone: 'nuevo', label: 'Nuevo' });
  if (product.condition !== 'Nuevo') badges.push({ key: 'condicion', tone: 'condicion', label: product.condition });
  if (product.tags.includes('destacado')) badges.push({ key: 'destacado', tone: 'destacado', label: 'Destacado' });
  const max = options.compact ? 1 : (options.max ?? badges.length);
  return badges.slice(0, max);
}
