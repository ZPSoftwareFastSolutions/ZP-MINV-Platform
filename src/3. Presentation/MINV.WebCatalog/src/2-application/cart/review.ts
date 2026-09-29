// Cruza el carrito (solo SKU y cantidad) con el catálogo FRESCO: de ahí salen el nombre, el precio y lo disponible de
// cada línea, y qué cambió desde que la persona armó su carrito:
//   · `sold_out`     se agotó (o lo que queda está reservado): hay que quitarlo.
//   · `reduced`      bajó lo disponible por debajo de lo pedido: hay que ajustar la cantidad.
//   · `unavailable`  el producto ya no está publicado en el catálogo: hay que quitarlo.
// El carrito NO se corrige solo: la página marca cada línea y ofrece ajustar; `adjustCart` aplica el ajuste.

import { cartCount, cartOf, maxCartQuantity } from '@/1-domain/cart/cart';
import { fromCents, lineCents, sumCents } from '@/1-domain/cart/totals';
import type { Cart, CartItem } from '@/1-domain/cart/types';
import { savingAmount } from '@/1-domain/catalog/money';
import type { Product } from '@/1-domain/catalog/types';

/** De dónde sale el producto de un SKU (el catálogo de la instantánea fresca). */
export type ProductLookup = (sku: string) => Product | undefined;

export type CartLineStatus = 'ok' | 'reduced' | 'sold_out' | 'unavailable';

/** Una línea del carrito con todo lo que la página necesita para dibujarla. */
export interface CartLine {
  sku: string;
  /** Unidades que pidió la persona (lo guardado). */
  quantity: number;
  /** Producto fresco del catálogo; null si ya no está publicado. */
  product: Product | null;
  status: CartLineStatus;
  /** Unidades disponibles ahora en la tienda (0 si se agotó o ya no está). */
  available: number;
  /** Máximo que admite la línea: lo disponible con el tope de 16. */
  max: number;
  /** Cantidad a la que hay que llevar la línea para poder reservar (0 = quitarla). Igual a `quantity` si está bien. */
  suggestedQuantity: number;
  /** Precio unitario de hoy (Bs, IVA incluido); 0 si el producto ya no está. */
  unitPrice: number;
  /** Precio × unidades pedidas (Bs). */
  subtotal: number;
  /** La línea entra al total (todas menos las agotadas y las que ya no están). */
  counted: boolean;
}

export interface CartReview {
  lines: CartLine[];
  /** Unidades pedidas en todo el carrito. */
  count: number;
  /** Total en bolivianos de las líneas que se pueden reservar (suma exacta de centavos). */
  total: number;
  totalCents: number;
  /** Ahorro frente a los precios de lista. */
  savings: number;
  /** Líneas que cambiaron y hay que ajustar o quitar. */
  issues: CartLine[];
  /** Hay líneas y ninguna necesita ajuste: se puede pasar a reservar. */
  reservable: boolean;
}

/** Un cambio aplicado al ajustar el carrito. */
export interface CartAdjustmentChange {
  sku: string;
  name: string;
  from: number;
  /** 0 = la línea se quitó. */
  to: number;
  reason: Exclude<CartLineStatus, 'ok'>;
}

export interface CartAdjustment {
  cart: Cart;
  changes: CartAdjustmentChange[];
}

function reviewLine(item: CartItem, product: Product | undefined): CartLine {
  if (!product) {
    return { sku: item.sku, quantity: item.quantity, product: null, status: 'unavailable', available: 0, max: 0, suggestedQuantity: 0, unitPrice: 0, subtotal: 0, counted: false };
  }
  const available = Math.max(0, Number.isFinite(product.stock) ? Math.trunc(product.stock) : 0);
  const max = maxCartQuantity(product);
  const status: CartLineStatus = max === 0 ? 'sold_out' : item.quantity > max ? 'reduced' : 'ok';
  return {
    sku: item.sku,
    quantity: item.quantity,
    product,
    status,
    available,
    max,
    suggestedQuantity: Math.min(item.quantity, max),
    unitPrice: product.price,
    subtotal: fromCents(lineCents(product.price, item.quantity)),
    counted: status !== 'sold_out',
  };
}

/** Carrito cruzado con el catálogo: líneas, total y lo que cambió. */
export function reviewCart(cart: Cart, lookup: ProductLookup): CartReview {
  const lines = cart.items.map((item) => reviewLine(item, lookup(item.sku)));
  const counted = lines.filter((line) => line.counted);
  const totalCents = sumCents(counted.map((line) => lineCents(line.unitPrice, line.quantity)));
  const savingsCents = sumCents(counted.map((line) => (line.product ? lineCents(savingAmount(line.product.price, line.product.listPrice), line.quantity) : 0)));
  const issues = lines.filter((line) => line.status !== 'ok');
  return {
    lines,
    count: cartCount(cart),
    total: fromCents(totalCents),
    totalCents,
    savings: fromCents(savingsCents),
    issues,
    reservable: lines.length > 0 && issues.length === 0,
  };
}

function changeOf(line: CartLine): CartAdjustmentChange {
  return { sku: line.sku, name: line.product?.shortName ?? line.sku, from: line.quantity, to: line.suggestedQuantity, reason: line.status as CartAdjustmentChange['reason'] };
}

/**
 * Lleva el carrito a lo que hoy se puede reservar: baja las cantidades que superan lo disponible y quita lo agotado y
 * lo que ya no está publicado. Con `onlySku` ajusta solo esa línea. Devuelve el carrito nuevo y qué cambió.
 */
export function adjustCart(cart: Cart, lookup: ProductLookup, onlySku?: string): CartAdjustment {
  const review = reviewCart(cart, lookup);
  const targets = review.issues.filter((line) => onlySku === undefined || line.sku === onlySku);
  if (targets.length === 0) return { cart, changes: [] };
  const suggested = new Map(targets.map((line) => [line.sku, line.suggestedQuantity]));
  const items = cart.items
    .map((item) => (suggested.has(item.sku) ? { sku: item.sku, quantity: suggested.get(item.sku) ?? 0 } : item))
    .filter((item) => item.quantity > 0);
  return { cart: cartOf(items), changes: targets.map(changeOf) };
}
