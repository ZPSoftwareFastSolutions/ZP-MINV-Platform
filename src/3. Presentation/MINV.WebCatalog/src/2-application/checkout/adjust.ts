// «Ajustar a lo disponible» en la reserva (V7): a cuánto llevar cada producto cuando la tienda rechazó la reserva por
// falta de stock (409, con lo que hay de verdad) o cuando el catálogo fresco ya muestra que algo bajó o se agotó. Nunca
// sube una cantidad: solo la baja (0 = quitarlo).

import { CART_LIMITS } from '@/1-domain/cart/cart';
import type { StockShortage } from '@/1-domain/storefront/types';
import type { CartReview } from '../cart/review';

export interface CheckoutAdjustment {
  sku: string;
  /** Nombre corto para el aviso («Monitor LG: de 3 a 1»). */
  name: string;
  from: number;
  /** 0 = quitarlo de la reserva. */
  to: number;
}

/** Faltantes por SKU (el servidor manda el SKU en mayúsculas). */
export function shortagesBySku(shortages: readonly StockShortage[]): Map<string, StockShortage> {
  return new Map(shortages.map((item) => [item.sku.toUpperCase(), item]));
}

/**
 * Cambios para poder reservar: lo que dijo el servidor (faltantes del 409) manda sobre el catálogo; si no hay faltante
 * para una línea, se usa lo sugerido por el catálogo fresco (bajó, se agotó o ya no está).
 */
export function checkoutAdjustments(review: CartReview, shortages: readonly StockShortage[] = []): CheckoutAdjustment[] {
  const bySku = shortagesBySku(shortages);
  const changes: CheckoutAdjustment[] = [];
  for (const line of review.lines) {
    const shortage = bySku.get(line.sku.toUpperCase());
    const fromServer = shortage ? Math.max(0, Math.floor(shortage.available)) : Number.POSITIVE_INFINITY;
    const target = Math.min(line.quantity, CART_LIMITS.maxQuantityPerLine, fromServer, line.status === 'ok' ? line.quantity : line.suggestedQuantity);
    if (target !== line.quantity) changes.push({ sku: line.sku, name: line.product?.shortName ?? shortage?.name ?? line.sku, from: line.quantity, to: target });
  }
  return changes;
}
