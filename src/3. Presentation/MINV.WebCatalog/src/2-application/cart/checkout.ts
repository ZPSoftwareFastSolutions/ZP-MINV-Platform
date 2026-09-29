// Qué se va a reservar en `/reservar` (la página es `4-presentation/pages/cart/CheckoutPage.tsx`; aquí queda el contrato):
//   · `/reservar?sku=<SKU>&cantidad=<n>`  «Reservar ahora»: ESE solo artículo. No pasa por el carrito ni lo modifica.
//   · `/reservar`                          el carrito completo.
// La dirección solo lleva el SKU y la cantidad (nunca datos de la persona) y lo que trae se valida como cualquier dato
// de afuera: un SKU raro o una cantidad fuera de rango no llegan a la reserva.

import { CART_LIMITS, normalizeQuantity, normalizeSku } from '@/1-domain/cart/cart';
import type { Cart, CartItem } from '@/1-domain/cart/types';
import { reviewCart, type CartReview, type ProductLookup } from './review';

export const CART_PATH = '/carrito';
export const CHECKOUT_PATH = '/reservar';

/** Nombres de los parámetros de `/reservar` para un artículo suelto. */
export const CHECKOUT_PARAMS = { sku: 'sku', quantity: 'cantidad' } as const;

/** `/reservar?sku=MON-LG-27GS75Q&cantidad=1`: la reserva directa de un solo artículo. */
export function checkoutItemPath(sku: string, quantity = 1): string {
  const params = new URLSearchParams();
  params.set(CHECKOUT_PARAMS.sku, sku);
  params.set(CHECKOUT_PARAMS.quantity, String(normalizeQuantity(quantity) || 1));
  return `${CHECKOUT_PATH}?${params.toString()}`;
}

/** Verdadero si la dirección pide un artículo suelto (trae `sku`, sirva o no). */
export function isSingleItemCheckout(search: string | URLSearchParams): boolean {
  return new URLSearchParams(search).has(CHECKOUT_PARAMS.sku);
}

/**
 * El artículo suelto que pide la dirección, o null si no trae un SKU válido. Sin `cantidad` (o con una que no es un
 * número entero positivo) vale 1; más de 16 se acota a 16.
 */
export function parseCheckoutItem(search: string | URLSearchParams): CartItem | null {
  const params = new URLSearchParams(search);
  const sku = normalizeSku(params.get(CHECKOUT_PARAMS.sku));
  if (!sku) return null;
  const raw = params.get(CHECKOUT_PARAMS.quantity)?.trim() ?? '';
  const quantity = /^\d+$/.test(raw) ? Math.min(CART_LIMITS.maxQuantityPerLine, Math.max(1, Number(raw))) : 1;
  return { sku, quantity };
}

export type CheckoutSource = 'item' | 'cart';

/** Lo que se va a reservar, ya cruzado con el catálogo fresco. */
export interface CheckoutSelection {
  /** `item`: un artículo suelto («Reservar ahora»); `cart`: el carrito. */
  source: CheckoutSource;
  /** Líneas a reservar (SKU y cantidad). Vacío si el artículo de la dirección no sirve o el carrito está vacío. */
  items: CartItem[];
  /** Las mismas líneas con nombre, precio, disponibilidad y total. */
  review: CartReview;
}

/**
 * Decide qué se reserva: el artículo de la dirección si trae `sku`; si no, el carrito. Un artículo suelto NO toca el
 * carrito: al terminar la reserva, el carrito solo se vacía cuando `source` es `cart`.
 */
export function checkoutSelection(search: string | URLSearchParams, cart: Cart, lookup: ProductLookup): CheckoutSelection {
  if (isSingleItemCheckout(search)) {
    const item = parseCheckoutItem(search);
    const items = item ? [item] : [];
    return { source: 'item', items, review: reviewCart({ items }, lookup) };
  }
  return { source: 'cart', items: [...cart.items], review: reviewCart(cart, lookup) };
}
