import { useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import type { CartAddResult } from '@/1-domain/cart/types';
import type { Product } from '@/1-domain/catalog/types';
import { ROUTES } from '@/4-presentation/app/routes';
import { addToCartNotice } from '@/4-presentation/components/cart/cartText';
import { useCartActions } from './useCart';
import { useToast } from './useToast';

/** Grupo de los avisos de «agregado»: varios productos seguidos muestran un solo aviso (el último), no una pila. */
const ADD_TOAST_GROUP = 'carrito:agregar';

/**
 * «Agregar al carrito» con su aviso: `const addToCart = useAddToCart(); addToCart(product, 2)`. Muestra «Agregado al
 * carrito» con «Ver carrito» (o por qué no se pudo agregar más) y devuelve el resultado. Debe usarse dentro del
 * enrutador (el aviso lleva a `/carrito`).
 */
export function useAddToCart(): (product: Product, quantity?: number) => CartAddResult {
  const { add } = useCartActions();
  const toast = useToast();
  const navigate = useNavigate();
  return useCallback(
    (product, quantity) => {
      const result = add(product, quantity);
      const notice = addToCartNotice(product, result);
      toast.notify({
        tone: notice.tone,
        group: ADD_TOAST_GROUP,
        title: notice.title,
        description: notice.description,
        action: notice.showCart ? { label: 'Ver carrito', onClick: () => void navigate(ROUTES.cart) } : undefined,
      });
      return result;
    },
    [add, toast, navigate],
  );
}
