import { useContext } from 'react';
import { CartActionsContext, CartStateContext, type CartActionsApi, type CartApi, type CartStateApi } from '@/4-presentation/state/CartContext';

function required<T>(value: T | null, hook: string): T {
  if (!value) throw new Error(`${hook} debe usarse dentro de <CartProvider>.`);
  return value;
}

/** Estado del carrito (líneas, unidades, total, qué cambió). Se vuelve a dibujar con cada cambio del carrito. */
export function useCartState(): CartStateApi {
  return required(useContext(CartStateContext), 'useCartState()');
}

/** Acciones del carrito (add, setQuantity, remove, clear, adjust): no cambian con el contenido del carrito. */
export function useCartActions(): CartActionsApi {
  return required(useContext(CartActionsContext), 'useCartActions()');
}

/**
 * Carrito completo: `const { lines, count, total, review, persistent, has, quantityOf, add, setQuantity, remove, clear,
 * adjust } = useCart()`. Para agregar CON el aviso «Agregado al carrito» se usa `useAddToCart()`.
 */
export function useCart(): CartApi {
  return { ...useCartState(), ...useCartActions() };
}
