// Dinero del carrito con aritmética EXACTA de centavos: cada precio se pasa a centavos enteros, se multiplica y se suma
// en enteros, y recién al final se vuelve a bolivianos. Así 3 × Bs 0,10 da Bs 0,30 (y no 0,30000000000000004) y el
// total del carrito coincide al centavo con el que calcula la tienda al reservar.

/** Bolivianos → centavos enteros (Bs 1.099,99 → 109999). Un valor que no es un número cuenta como 0. */
export function toCents(amount: number): number {
  if (!Number.isFinite(amount)) return 0;
  // Los precios llegan con dos decimales: el redondeo solo absorbe el error binario (1.099,99 × 100 = 109.998,99999…).
  const cents = Math.round((Math.abs(amount) + Number.EPSILON) * 100);
  return amount < 0 ? -cents : cents;
}

/** Centavos enteros → bolivianos (109999 → 1.099,99). */
export function fromCents(cents: number): number {
  return Math.trunc(cents) / 100;
}

/** Subtotal de una línea en centavos: precio unitario × unidades. */
export function lineCents(unitPrice: number, quantity: number): number {
  if (!Number.isFinite(quantity) || quantity <= 0) return 0;
  return toCents(unitPrice) * Math.trunc(quantity);
}

/** Subtotal de una línea en bolivianos. */
export function lineSubtotal(unitPrice: number, quantity: number): number {
  return fromCents(lineCents(unitPrice, quantity));
}

/** Suma exacta de importes en centavos. */
export function sumCents(values: readonly number[]): number {
  return values.reduce((total, value) => total + Math.trunc(value), 0);
}
