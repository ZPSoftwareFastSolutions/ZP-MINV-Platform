// Reglas de dinero del catálogo: precios en bolivianos con IVA incluido (13 %, informativo), ofertas y ahorro.

import { formatMoney, formatPercent } from '@/shared/format';

/** IVA de Bolivia; los precios del catálogo ya lo incluyen. */
export const IVA_RATE = 0.13;

export interface PriceBreakdown {
  /** Precio final (el que paga el cliente). */
  total: number;
  /** Precio sin IVA. */
  net: number;
  /** IVA incluido en el total. */
  iva: number;
}

export function roundMoney(value: number): number {
  return Math.round((value + Number.EPSILON) * 100) / 100;
}

export function sumMoney(values: readonly number[]): number {
  return roundMoney(values.reduce((acc, value) => acc + value, 0));
}

/** Desglose informativo de un precio con IVA incluido. */
export function ivaBreakdown(priceWithIva: number): PriceBreakdown {
  const net = roundMoney(priceWithIva / (1 + IVA_RATE));
  return { total: roundMoney(priceWithIva), net, iva: roundMoney(priceWithIva - net) };
}

/** Un producto está en oferta cuando tiene precio de lista mayor al precio de venta. */
export function isOnSale(price: number, listPrice: number | null | undefined): boolean {
  return listPrice != null && listPrice > price;
}

/** Cuánto se ahorra respecto del precio de lista (0 si no hay oferta). */
export function savingAmount(price: number, listPrice: number | null | undefined): number {
  return isOnSale(price, listPrice) ? roundMoney((listPrice as number) - price) : 0;
}

/** Porcentaje entero de ahorro (0 si no hay oferta): 1.099 sobre 1.263,85 → 13. */
export function savingPercent(price: number, listPrice: number | null | undefined): number {
  if (!isOnSale(price, listPrice)) return 0;
  return Math.round((1 - price / (listPrice as number)) * 100);
}

/** «-13 %» listo para una insignia de oferta, o cadena vacía si no hay ahorro. */
export function savingLabel(price: number, listPrice: number | null | undefined): string {
  const percent = savingPercent(price, listPrice);
  return percent > 0 ? `-${formatPercent(percent)}` : '';
}

/** Alias del formato de dinero para que el dominio no dependa de la ruta de `shared` en cada import. */
export const money = formatMoney;
