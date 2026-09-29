// Carrito de compras de la tienda (V7). El estado vive en los casos de uso (`CartUseCases`, fuera de React): el
// proveedor se suscribe a ellos y cruza el carrito con el catálogo de la instantánea vigente, así cada refresco del
// catálogo (cada 60 s, o al volver a la pestaña) pone al día precios y disponibilidad de las líneas. El carrito NO se
// corrige solo: si algo se agotó o bajó, la página del carrito lo marca y ofrece ajustar.
//
// A diferencia del armado («Armá tu PC», en memoria), el carrito SÍ se conserva entre visitas: los casos de uso lo
// guardan por el puerto ICartStore (solo SKU y cantidad) y lo sincronizan entre pestañas.

import { useCallback, useMemo, useSyncExternalStore, type ReactNode } from 'react';
import { isInCart, quantityInCart } from '@/1-domain/cart/cart';
import { reviewCart, type CartUseCases } from '@/2-application';
import { useServices } from '@/4-presentation/hooks/useServices';
import { CartActionsContext, CartStateContext, type CartActionsApi, type CartStateApi } from './CartContext';

export interface CartProviderProps {
  /** Casos de uso del carrito ya armados sobre su almacén (`createCartServices()` del contenedor). */
  cart: CartUseCases;
  children: ReactNode;
}

export function CartProvider({ cart: useCases, children }: CartProviderProps) {
  const { catalog } = useServices();
  const cart = useSyncExternalStore(useCases.subscribe, useCases.current);
  const persistent = useSyncExternalStore(useCases.subscribe, useCases.isPersistent);

  const lookup = useCallback((sku: string) => catalog.getProductBySku(sku), [catalog]);

  const review = useMemo(() => reviewCart(cart, lookup), [cart, lookup]);

  const stateApi = useMemo<CartStateApi>(
    () => ({
      items: cart.items,
      lines: review.lines,
      review,
      count: review.count,
      total: review.total,
      persistent,
      has: (sku) => isInCart(cart, sku),
      quantityOf: (sku) => quantityInCart(cart, sku),
    }),
    [cart, review, persistent],
  );

  const actionsApi = useMemo<CartActionsApi>(
    () => ({
      add: (product, quantity) => useCases.add(product, quantity),
      setQuantity: (sku, quantity) => {
        useCases.setQuantity(sku, quantity, lookup(sku));
      },
      remove: (sku) => {
        useCases.remove(sku);
      },
      clear: () => {
        useCases.clear();
      },
      adjust: (sku) => useCases.adjust(lookup, sku),
    }),
    [useCases, lookup],
  );

  return (
    <CartActionsContext.Provider value={actionsApi}>
      <CartStateContext.Provider value={stateApi}>{children}</CartStateContext.Provider>
    </CartActionsContext.Provider>
  );
}
