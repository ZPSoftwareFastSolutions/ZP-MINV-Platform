// Disponibilidad de un producto para la presentación. Desde la V6 «disponible» = existencias − reservado en la sucursal
// de la tienda (regla S-03): si no queda disponible pero hay unidades reservadas, el estado es «reservado» (puede
// volver a estar disponible si la reserva vence o se libera), y si no hay nada, «agotado».

import type { Product } from './types';

/** Con esta cantidad o menos se avisa que quedan pocas unidades. */
export const LOW_STOCK_THRESHOLD = 3;

export type StockStatus = 'disponible' | 'ultimas' | 'reservado' | 'agotado';

/** Lo que hace falta para decidir la disponibilidad (`reserved` es opcional para las reglas que solo miran el stock). */
export type StockInfo = Pick<Product, 'stock'> & Partial<Pick<Product, 'reserved'>>;

export function stockStatus(product: StockInfo): StockStatus {
  if (product.stock <= 0) return (product.reserved ?? 0) > 0 ? 'reservado' : 'agotado';
  if (product.stock <= LOW_STOCK_THRESHOLD) return 'ultimas';
  return 'disponible';
}

export function isAvailable(product: StockInfo): boolean {
  return product.stock > 0;
}

/** Texto listo para mostrar junto al precio («Disponible (12)», «Últimas 3 unidades», «Reservado», «Agotado»). */
export function stockLabel(product: StockInfo): string {
  switch (stockStatus(product)) {
    case 'agotado':
      return 'Agotado';
    case 'reservado':
      return 'Reservado';
    case 'ultimas':
      return product.stock === 1 ? 'Última unidad' : `Últimas ${product.stock} unidades`;
    default:
      return `Disponible (${Math.trunc(product.stock)})`;
  }
}

/** Texto corto para un botón deshabilitado («Agotado» o «Reservado»). */
export function unavailableLabel(product: StockInfo): string {
  return stockStatus(product) === 'reservado' ? 'Reservado' : 'Agotado';
}

/** «2 unidades reservadas» cuando hay reservas (vacío si no las hay): aclara por qué disponible < existencias. */
export function reservedLabel(product: StockInfo): string {
  const reserved = Math.trunc(product.reserved ?? 0);
  if (reserved <= 0) return '';
  return reserved === 1 ? '1 unidad reservada' : `${reserved} unidades reservadas`;
}
