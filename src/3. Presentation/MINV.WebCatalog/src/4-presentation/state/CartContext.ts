// Dos contextos, como en el armador: el estado del carrito (cambia con cada producto) y las acciones (estables). Así
// quien solo agrega al carrito no se vuelve a dibujar con cada cambio. `useCart()` los combina.

import { createContext } from 'react';
import type { CartAddResult, CartItem } from '@/1-domain/cart/types';
import type { Product } from '@/1-domain/catalog/types';
import type { CartAdjustment, CartLine, CartReview } from '@/2-application';

export interface CartStateApi {
  /** Lo guardado: SKU y cantidad de cada línea, en el orden en que se agregaron. */
  items: readonly CartItem[];
  /** Las líneas cruzadas con el catálogo fresco (nombre, precio, disponible y qué cambió). */
  lines: CartLine[];
  /** Carrito completo cruzado con el catálogo: total, ahorro, líneas que hay que ajustar y si se puede reservar. */
  review: CartReview;
  /** Unidades del carrito (el número del ícono de la cabecera). */
  count: number;
  /** Total en bolivianos de lo que se puede reservar. */
  total: number;
  /** El carrito queda guardado en este navegador (falso: se pierde al cerrar la pestaña). */
  persistent: boolean;
  has(sku: string): boolean;
  quantityOf(sku: string): number;
}

export interface CartActionsApi {
  /** Agrega sin superar lo disponible ni los topes. El resultado dice cuántas unidades entraron y por qué no más. */
  add(product: Product, quantity?: number): CartAddResult;
  /** Cambia la cantidad de una línea, acotada a lo disponible hoy (0 la quita). */
  setQuantity(sku: string, quantity: number): void;
  remove(sku: string): void;
  clear(): void;
  /** Ajusta a lo disponible todas las líneas que cambiaron (o solo `sku`). Devuelve qué cambió. */
  adjust(sku?: string): CartAdjustment;
}

export type CartApi = CartStateApi & CartActionsApi;

export const CartStateContext = createContext<CartStateApi | null>(null);
export const CartActionsContext = createContext<CartActionsApi | null>(null);
