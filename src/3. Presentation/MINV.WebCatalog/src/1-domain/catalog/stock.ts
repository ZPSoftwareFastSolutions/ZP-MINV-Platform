// Disponibilidad de un producto para la presentación («Últimas 3 unidades», «Agotado»). Sin reservas ni backend.

import type { Product } from './types';

/** Con esta cantidad o menos se avisa que quedan pocas unidades. */
export const LOW_STOCK_THRESHOLD = 3;

export type StockStatus = 'disponible' | 'ultimas' | 'agotado';

export function stockStatus(product: Pick<Product, 'stock'>): StockStatus {
  if (product.stock <= 0) return 'agotado';
  if (product.stock <= LOW_STOCK_THRESHOLD) return 'ultimas';
  return 'disponible';
}

export function isAvailable(product: Pick<Product, 'stock'>): boolean {
  return product.stock > 0;
}

/** Texto listo para mostrar junto al precio. */
export function stockLabel(product: Pick<Product, 'stock'>): string {
  switch (stockStatus(product)) {
    case 'agotado':
      return 'Agotado';
    case 'ultimas':
      return product.stock === 1 ? 'Última unidad' : `Últimas ${product.stock} unidades`;
    default:
      return 'En stock';
  }
}
