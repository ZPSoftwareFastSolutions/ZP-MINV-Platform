// Textos del carrito (sin React): el aviso que se muestra al agregar un producto y cómo se enuncia lo que cambió en una
// línea. Voz de la tienda (voseo suave).

import { CART_LIMITS } from '@/1-domain/cart/cart';
import type { CartAddResult } from '@/1-domain/cart/types';
import { stockStatus } from '@/1-domain/catalog/stock';
import type { Product } from '@/1-domain/catalog/types';
import type { CartAdjustmentChange, CartLine } from '@/2-application';
import type { ToastTone } from '@/4-presentation/components/feedback/ToastContext';
import { pluralize } from '@/shared/format';

export interface CartNotice {
  tone: ToastTone;
  title: string;
  description: string;
  /** El aviso ofrece «Ver carrito». */
  showCart: boolean;
}

/** «1 unidad» · «3 unidades». */
export function unitsLabel(count: number): string {
  return pluralize(count, 'unidad', 'unidades');
}

/** «1 producto» · «3 productos» (unidades del carrito para el ícono de la cabecera). */
export function cartCountLabel(count: number): string {
  return pluralize(count, 'producto', 'productos');
}

/** Aviso después de «Agregar al carrito», según cuánto entró y por qué no entró más. */
export function addToCartNotice(product: Pick<Product, 'shortName'>, result: Pick<CartAddResult, 'added' | 'quantity' | 'limit'>): CartNotice {
  const name = product.shortName;
  const inCart = `${unitsLabel(result.quantity)} en tu carrito`;
  if (result.added > 0) {
    if (result.limit === 'stock') {
      return { tone: 'info', title: 'Agregado al carrito', description: `${name} · Agregamos ${unitsLabel(result.added)}: es todo lo disponible.`, showCart: true };
    }
    if (result.limit === 'line_limit') {
      return {
        tone: 'info',
        title: 'Agregado al carrito',
        description: `${name} · Agregamos ${unitsLabel(result.added)}: el máximo es ${CART_LIMITS.maxQuantityPerLine} por producto.`,
        showCart: true,
      };
    }
    return { tone: 'success', title: 'Agregado al carrito', description: `${name} · ${inCart}`, showCart: true };
  }
  switch (result.limit) {
    case 'stock':
      return { tone: 'info', title: 'Ya tenés todo lo disponible', description: `${name} · ${inCart}. No quedan más unidades.`, showCart: true };
    case 'line_limit':
      return {
        tone: 'info',
        title: 'Llegaste al máximo por producto',
        description: `${name} · Una reserva admite hasta ${CART_LIMITS.maxQuantityPerLine} unidades de un mismo producto.`,
        showCart: true,
      };
    case 'cart_full':
      return {
        tone: 'warning',
        title: 'Tu carrito está lleno',
        description: `Admite hasta ${CART_LIMITS.maxLines} productos distintos. Quitá alguno para agregar otro.`,
        showCart: true,
      };
    case 'unavailable':
      return { tone: 'warning', title: 'Sin unidades disponibles', description: `${name} se quedó sin unidades disponibles.`, showCart: false };
    default:
      return { tone: 'danger', title: 'No pudimos agregar este producto', description: 'Probá de nuevo desde la ficha del producto.', showCart: false };
  }
}

/** Qué le pasó a una línea del carrito que cambió (vacío si está bien). */
export function lineIssueText(line: Pick<CartLine, 'status' | 'available' | 'quantity' | 'product'>): string {
  switch (line.status) {
    case 'unavailable':
      return 'Este producto ya no está publicado en el catálogo.';
    case 'sold_out':
      return line.product && stockStatus(line.product) === 'reservado'
        ? 'Las unidades que quedan están reservadas por otros clientes.'
        : 'Se agotó desde que lo agregaste.';
    case 'reduced':
      return line.available === 1 ? `Pediste ${line.quantity} y queda 1 sola unidad.` : `Pediste ${line.quantity} y quedan ${line.available} unidades.`;
    default:
      return '';
  }
}

/** Resumen de un ajuste para el aviso: «Monitor LG: de 3 a 1 · SSD Kingston: lo quitamos». */
export function adjustmentText(changes: readonly CartAdjustmentChange[]): string {
  return changes.map((change) => (change.to === 0 ? `${change.name}: lo quitamos` : `${change.name}: de ${change.from} a ${change.to}`)).join(' · ');
}
