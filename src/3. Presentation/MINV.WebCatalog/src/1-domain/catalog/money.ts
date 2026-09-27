// Reglas de dinero del catálogo: precios en bolivianos con IVA incluido (13 %, informativo), ofertas, ahorro y cuotas
// informativas. Solo números: los textos («-13 %», «Bs 2.049,00») los arma la presentación con shared/format.

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
  return listPrice != null && listPrice > price ? roundMoney(listPrice - price) : 0;
}

/** Porcentaje entero de ahorro (0 si no hay oferta): 1.099 sobre 1.263,85 → 13. */
export function savingPercent(price: number, listPrice: number | null | undefined): number {
  if (listPrice == null || listPrice <= price) return 0;
  return Math.round((1 - price / listPrice) * 100);
}

/** Cuotas informativas con tarjeta de bancos asociados (sin interés, solo presentación): hasta 12. */
export const INSTALLMENTS_MAX = 12;

/** Importe de cada una de `months` cuotas iguales de un precio (redondeado a centavos; 0 si no aplica). */
export function installmentAmount(price: number, months = INSTALLMENTS_MAX): number {
  if (!Number.isFinite(price) || price <= 0 || !Number.isFinite(months) || months < 1) return 0;
  return roundMoney(price / Math.trunc(months));
}
