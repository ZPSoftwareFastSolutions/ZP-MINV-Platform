// Reglas puras del carrito (V7): agregar, cambiar cantidad, quitar y vaciar, con los topes del contrato de reservas
// (16 unidades por producto y 20 productos distintos, regla S-05) y sin superar NUNCA lo disponible del producto.
// Todas las funciones devuelven un carrito nuevo (no modifican el que reciben); si nada cambia, devuelven el mismo.

import type { StockInfo } from '@/1-domain/catalog/stock';
import type { Product } from '@/1-domain/catalog/types';
import { RESERVATION_LIMITS } from '@/1-domain/storefront/types';
import type { Cart, CartAddResult, CartItem } from './types';

/** Topes del carrito: los mismos que la tienda exige al reservar (no se puede armar un carrito que después rebote). */
export const CART_LIMITS = {
  /** Productos distintos. */
  maxLines: RESERVATION_LIMITS.maxLines,
  /** Unidades de un mismo producto. */
  maxQuantityPerLine: RESERVATION_LIMITS.maxQuantityPerLine,
  /** Largo máximo de un SKU (el mismo del servidor). */
  skuMaxLength: 40,
} as const;

export const EMPTY_CART: Cart = { items: [] };

/** Un SKU del catálogo: empieza con letra o número y sigue con letras, números, guion o guion bajo. */
const SKU_PATTERN = /^[A-Z0-9][A-Z0-9_-]*$/;

/** SKU en mayúsculas y sin espacios alrededor, o null si no tiene forma de SKU (texto raro, vacío o demasiado largo). */
export function normalizeSku(value: unknown): string | null {
  if (typeof value !== 'string') return null;
  const sku = value.trim().toUpperCase();
  if (sku.length === 0 || sku.length > CART_LIMITS.skuMaxLength) return null;
  return SKU_PATTERN.test(sku) ? sku : null;
}

/** Cantidad entera de 1 a 16; 0 si el valor no sirve (texto, decimal menor que 1, negativo, infinito…). */
export function normalizeQuantity(value: unknown): number {
  if (typeof value !== 'number' || !Number.isFinite(value)) return 0;
  const quantity = Math.trunc(value);
  if (quantity < 1) return 0;
  return Math.min(quantity, CART_LIMITS.maxQuantityPerLine);
}

/**
 * Líneas válidas a partir de un dato de origen dudoso (lo guardado en el navegador o lo que llega de otra pestaña):
 * descarta lo que no tiene SKU o cantidad válidos, une los SKU repetidos y respeta los topes. Nunca lanza.
 */
export function sanitizeCartItems(raw: unknown): CartItem[] {
  if (!Array.isArray(raw)) return [];
  const bySku = new Map<string, number>();
  for (const entry of raw) {
    if (entry === null || typeof entry !== 'object') continue;
    const candidate = entry as { sku?: unknown; quantity?: unknown };
    const sku = normalizeSku(candidate.sku);
    const quantity = normalizeQuantity(candidate.quantity);
    if (!sku || quantity === 0) continue;
    const previous = bySku.get(sku);
    if (previous === undefined && bySku.size >= CART_LIMITS.maxLines) continue;
    bySku.set(sku, Math.min(CART_LIMITS.maxQuantityPerLine, (previous ?? 0) + quantity));
  }
  return [...bySku].map(([sku, quantity]) => ({ sku, quantity }));
}

/** Carrito válido a partir de unas líneas cualesquiera. */
export function cartOf(items: unknown): Cart {
  const clean = sanitizeCartItems(items);
  return clean.length === 0 ? EMPTY_CART : { items: clean };
}

/** Unidades que admite el carrito de un producto: lo disponible, con el tope de 16 (0 si no queda nada). */
export function maxCartQuantity(product: StockInfo): number {
  if (!Number.isFinite(product.stock)) return 0;
  return Math.max(0, Math.min(CART_LIMITS.maxQuantityPerLine, Math.trunc(product.stock)));
}

export function quantityInCart(cart: Cart, sku: string): number {
  return cart.items.find((item) => item.sku === sku)?.quantity ?? 0;
}

export function isInCart(cart: Cart, sku: string): boolean {
  return cart.items.some((item) => item.sku === sku);
}

/** Unidades del carrito (suma de cantidades): el número del ícono de la cabecera. */
export function cartCount(cart: Cart): number {
  return cart.items.reduce((total, item) => total + item.quantity, 0);
}

/** Dos carritos con las mismas líneas, en el mismo orden y con las mismas cantidades. */
export function sameCart(a: Cart, b: Cart): boolean {
  if (a === b) return true;
  if (a.items.length !== b.items.length) return false;
  return a.items.every((item, index) => item.sku === b.items[index].sku && item.quantity === b.items[index].quantity);
}

/**
 * Agrega unidades de un producto. Si ya está, suma a su línea. Entra lo que quepa: nunca más de lo disponible ni de 16
 * por producto, ni más de 20 productos distintos; `limit` dice por qué entró menos de lo pedido.
 */
export function addToCart(cart: Cart, product: Pick<Product, 'sku' | 'stock'>, quantity = 1): CartAddResult {
  const sku = normalizeSku(product.sku);
  if (!sku) return { cart, added: 0, quantity: 0, limit: 'invalid' };

  const current = quantityInCart(cart, sku);
  const max = maxCartQuantity(product);
  if (max === 0) return { cart, added: 0, quantity: current, limit: 'unavailable' };
  if (current === 0 && cart.items.length >= CART_LIMITS.maxLines) return { cart, added: 0, quantity: 0, limit: 'cart_full' };

  const wanted = Number.isFinite(quantity) ? Math.max(1, Math.trunc(quantity)) : 1;
  const next = Math.min(max, current + wanted);
  const added = Math.max(0, next - current);
  // Entró menos de lo pedido: lo frenó lo disponible o el tope por producto (el que sea más chico).
  const limit = added < wanted ? (max < CART_LIMITS.maxQuantityPerLine ? 'stock' : 'line_limit') : null;
  if (added === 0) return { cart, added: 0, quantity: current, limit };

  const items = current === 0 ? [...cart.items, { sku, quantity: next }] : cart.items.map((item) => (item.sku === sku ? { sku, quantity: next } : item));
  return { cart: { items }, added, quantity: next, limit };
}

/**
 * Cambia la cantidad de una línea que ya está en el carrito, acotada a [0, tope]: el tope es 16 o, si se pasa el
 * producto, lo disponible. Con 0 (o si ya no queda nada disponible) la línea se quita. Un SKU que no está no se agrega.
 */
export function setCartQuantity(cart: Cart, sku: string, quantity: number, product?: StockInfo): Cart {
  const line = cart.items.find((item) => item.sku === sku);
  if (!line || !Number.isFinite(quantity)) return cart;
  const max = product ? maxCartQuantity(product) : CART_LIMITS.maxQuantityPerLine;
  const next = Math.min(max, Math.max(0, Math.trunc(quantity)));
  if (next === 0) return removeFromCart(cart, sku);
  if (next === line.quantity) return cart;
  return { items: cart.items.map((item) => (item.sku === sku ? { sku, quantity: next } : item)) };
}

export function removeFromCart(cart: Cart, sku: string): Cart {
  if (!isInCart(cart, sku)) return cart;
  const items = cart.items.filter((item) => item.sku !== sku);
  return items.length === 0 ? EMPTY_CART : { items };
}

export function clearCart(cart: Cart): Cart {
  return cart.items.length === 0 ? cart : EMPTY_CART;
}
