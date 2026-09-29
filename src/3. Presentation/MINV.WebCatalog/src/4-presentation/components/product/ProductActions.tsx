// Acciones de compra de una tarjeta o fila de producto (V7): la tienda funciona como carrito de compras, así que TODO
// producto con disponibilidad (también consolas, juegos y portátiles) ofrece «Agregar al carrito» y «Reservar ahora».
// «Reservar ahora» lleva directo a la reserva de ESE solo artículo (`/reservar?sku=…&cantidad=1`): no pasa por el carrito
// ni por «Armá tu PC». Las piezas que ocupan una ranura del armador conservan «Agregar al armado» como acción secundaria.

import clsx from 'clsx';
import { Check, Plus, ShoppingCart, Zap } from 'lucide-react';
import { isBuildable } from '@/1-domain/builder/slots';
import { isAvailable, unavailableLabel } from '@/1-domain/catalog/stock';
import type { Product } from '@/1-domain/catalog/types';
import { ROUTES } from '@/4-presentation/app/routes';
import { unitsLabel } from '@/4-presentation/components/cart/cartText';
import { Button } from '@/4-presentation/components/ui/Button';
import { useAddToCart } from '@/4-presentation/hooks/useAddToCart';
import { useBuildActions, useBuildState } from '@/4-presentation/hooks/useBuilder';
import { useCartState } from '@/4-presentation/hooks/useCart';

export interface ProductActionsProps {
  product: Product;
  className?: string;
}

export function ProductActions({ product, className }: ProductActionsProps) {
  const addToCart = useAddToCart();
  const { quantityOf } = useCartState();
  const { add: addToBuild } = useBuildActions();
  const { isInBuild } = useBuildState();
  const available = isAvailable(product);
  const buildable = isBuildable(product);
  const inBuild = buildable && isInBuild(product.sku);
  const inCart = quantityOf(product.sku);

  // `relative z-10`: los botones quedan por encima del enlace que cubre toda la tarjeta.
  if (!available) {
    const label = unavailableLabel(product);
    return (
      <div className={clsx('relative z-10 flex flex-col gap-2', className)} data-testid="acciones-producto">
        <Button type="button" variant="subtle" fullWidth disabled aria-label={`${label}: ${product.shortName}`}>
          {label}
        </Button>
      </div>
    );
  }

  return (
    <div className={clsx('relative z-10 flex flex-col gap-2', className)} data-testid="acciones-producto">
      <Button
        type="button"
        variant="primary"
        fullWidth
        leftIcon={<ShoppingCart />}
        aria-label={`Agregar al carrito: ${product.shortName}${inCart > 0 ? ` (${unitsLabel(inCart)} en tu carrito)` : ''}`}
        onClick={() => addToCart(product)}
        className="relative max-sm:px-2"
      >
        Agregar al carrito
        {/* En la esquina (como el ícono de la cabecera): avisar cuántas hay no cambia el alto del botón ni mueve la grilla. */}
        {inCart > 0 && (
          <span
            aria-hidden="true"
            className="absolute -top-1.5 -right-1.5 flex h-5 min-w-5 items-center justify-center rounded-full bg-cta px-1 font-display text-xs font-bold text-bg"
            data-testid="en-carrito"
          >
            {inCart}
          </span>
        )}
      </Button>
      <Button to={ROUTES.checkoutItem(product.sku)} variant="outline" fullWidth leftIcon={<Zap />} aria-label={`Reservar ahora: ${product.shortName}`} className="max-sm:px-2">
        Reservar ahora
      </Button>
      {buildable && (
        <Button
          type="button"
          variant="ghost"
          size="sm"
          fullWidth
          leftIcon={inBuild ? <Check /> : <Plus />}
          aria-label={`${inBuild ? 'En tu armado, agregar otra vez' : 'Agregar al armado'}: ${product.shortName}`}
          onClick={() => addToBuild(product)}
        >
          {inBuild ? 'En tu armado' : 'Agregar al armado'}
        </Button>
      )}
    </div>
  );
}
