// Casos de uso del carrito (V7) sobre el puerto ICartStore. Sin React, sin red y sin conocer el almacenamiento: guardan
// el carrito vigente en memoria, lo persisten por el puerto después de cada cambio y avisan a quien escucha (la
// presentación se suscribe con `useSyncExternalStore`). Si otra pestaña cambia el carrito, este se pone al día.
//
// Si guardar falla (almacenamiento lleno o bloqueado), el carrito SIGUE funcionando en memoria: `isPersistent()` pasa a
// falso para que la página avise que el carrito se pierde al cerrar la pestaña.

import { EMPTY_CART, addToCart, cartOf, clearCart, removeFromCart, sameCart, setCartQuantity } from '@/1-domain/cart/cart';
import type { Cart, CartAddResult } from '@/1-domain/cart/types';
import type { StockInfo } from '@/1-domain/catalog/stock';
import type { Product } from '@/1-domain/catalog/types';
import type { ICartStore } from '@/1-domain/ports/ICartStore';
import { adjustCart, reviewCart, type CartAdjustment, type CartReview, type ProductLookup } from './review';

export interface CartUseCases {
  /** Carrito vigente. La referencia solo cambia cuando cambia el carrito. */
  current(): Cart;
  /** Avisa después de cada cambio (propio o de otra pestaña). Devuelve cómo dejar de escuchar. */
  subscribe(listener: () => void): () => void;
  /** El carrito queda guardado entre visitas (falso: vive solo en memoria). */
  isPersistent(): boolean;
  /** Agrega unidades de un producto sin superar lo disponible ni los topes; el resultado dice cuánto entró. */
  add(product: Pick<Product, 'sku' | 'stock'>, quantity?: number): CartAddResult;
  /** Cambia la cantidad de una línea (0 la quita); con el producto, la acota a lo disponible. */
  setQuantity(sku: string, quantity: number, product?: StockInfo): Cart;
  remove(sku: string): Cart;
  clear(): Cart;
  /** El carrito cruzado con el catálogo fresco: líneas, total y qué se agotó o bajó. */
  review(lookup: ProductLookup): CartReview;
  /** Ajusta el carrito a lo que hoy se puede reservar (todas las líneas, o solo `sku`). */
  adjust(lookup: ProductLookup, sku?: string): CartAdjustment;
}

export function createCartUseCases(store: ICartStore): CartUseCases {
  let cart: Cart = cartOf(store.load());
  let saved = store.persistent;
  const listeners = new Set<() => void>();
  let stopWatching: (() => void) | null = null;

  const notify = () => {
    for (const listener of [...listeners]) listener();
  };

  /** Cambio propio: se guarda y se avisa. */
  const commit = (next: Cart): Cart => {
    if (sameCart(cart, next)) return cart;
    cart = next.items.length === 0 ? EMPTY_CART : next;
    saved = store.save(cart.items) && store.persistent;
    notify();
    return cart;
  };

  /** Cambio de otra pestaña: ya está guardado, solo se adopta y se avisa. */
  const adopt = (items: unknown) => {
    const next = cartOf(items);
    if (sameCart(cart, next)) return;
    cart = next;
    saved = store.persistent;
    notify();
  };

  return {
    current: () => cart,
    subscribe(listener) {
      listeners.add(listener);
      if (!stopWatching) stopWatching = store.subscribe(adopt);
      return () => {
        listeners.delete(listener);
        if (listeners.size === 0 && stopWatching) {
          stopWatching();
          stopWatching = null;
        }
      };
    },
    isPersistent: () => saved,
    add(product, quantity) {
      const result = addToCart(cart, product, quantity);
      return { ...result, cart: commit(result.cart) };
    },
    setQuantity: (sku, quantity, product) => commit(setCartQuantity(cart, sku, quantity, product)),
    remove: (sku) => commit(removeFromCart(cart, sku)),
    clear: () => commit(clearCart(cart)),
    review: (lookup) => reviewCart(cart, lookup),
    adjust(lookup, sku) {
      const adjustment = adjustCart(cart, lookup, sku);
      return { ...adjustment, cart: commit(adjustment.cart) };
    },
  };
}
