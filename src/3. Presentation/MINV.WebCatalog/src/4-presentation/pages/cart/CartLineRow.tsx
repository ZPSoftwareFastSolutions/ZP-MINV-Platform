// Una línea del carrito: imagen, marca, nombre (enlace a la ficha), precio unitario, disponibilidad de hoy, cantidad
// (más/menos con lo disponible como tope), subtotal y «Quitar». Si el producto se agotó, bajó por debajo de lo pedido o
// ya no está publicado, la línea lo dice y ofrece el ajuste (nunca se corrige sola).

import clsx from 'clsx';
import { Trash2, TriangleAlert } from 'lucide-react';
import { Link } from 'react-router-dom';
import type { CartLine } from '@/2-application';
import { ROUTES } from '@/4-presentation/app/routes';
import { lineIssueText, unitsLabel } from '@/4-presentation/components/cart/cartText';
import { StockIndicator } from '@/4-presentation/components/product/StockIndicator';
import { Button } from '@/4-presentation/components/ui/Button';
import { IconButton } from '@/4-presentation/components/ui/IconButton';
import { PriceTag } from '@/4-presentation/components/ui/PriceTag';
import { ProductImage } from '@/4-presentation/components/ui/ProductImage';
import { QuantityStepper } from '@/4-presentation/components/ui/QuantityStepper';
import { formatMoney } from '@/shared/format';

export interface CartLineRowProps {
  line: CartLine;
  onQuantityChange: (quantity: number) => void;
  onRemove: () => void;
  /** Lleva la línea a lo disponible hoy (o la quita si no queda nada). */
  onAdjust: () => void;
}

export function CartLineRow({ line, onQuantityChange, onRemove, onAdjust }: CartLineRowProps) {
  const { product } = line;
  const name = product?.shortName ?? line.sku;
  const issue = lineIssueText(line);
  const reservable = line.status === 'ok' || line.status === 'reduced';

  return (
    <li
      data-testid="linea-carrito"
      data-sku={line.sku}
      data-status={line.status}
      className={clsx('rounded-card border bg-surface p-3 shadow-card sm:p-4', issue ? 'border-warning/50' : 'border-border')}
    >
      <div className="flex gap-3 sm:gap-4">
        {product ? (
          <ProductImage src={product.image} alt="" padding="sm" className="w-20 shrink-0 self-start sm:w-24" />
        ) : (
          <div aria-hidden="true" className="aspect-square w-20 shrink-0 self-start rounded-xl border border-border bg-surface-2 sm:w-24" />
        )}
        <div className="flex min-w-0 flex-1 flex-col gap-1">
          {product && (
            <p className="truncate text-xs font-semibold uppercase tracking-wide text-text-faint">
              {product.brand}
              <span className="font-medium normal-case tracking-normal"> · {product.categoryName}</span>
            </p>
          )}
          <h3 className="text-[0.9375rem] leading-snug font-medium break-words text-text">
            {product ? (
              // El relleno con margen negativo agranda la zona táctil a 44 px sin mover el resto de la línea.
              <Link to={ROUTES.product(product.slug)} className="-my-3 inline-block rounded-sm py-3 transition-colors duration-200 hover:text-accent-hover">
                {name}
              </Link>
            ) : (
              name
            )}
          </h3>
          {product && (
            <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
              <PriceTag price={product.price} listPrice={product.listPrice} size="sm" showSaving={false} />
              <StockIndicator product={product} />
            </div>
          )}
        </div>
      </div>

      {issue && (
        <div className="mt-3 flex flex-wrap items-center justify-between gap-x-3 gap-y-2 rounded-xl border border-warning/40 bg-warning-soft p-3 text-sm" data-testid="linea-aviso">
          <p className="flex min-w-0 flex-1 items-start gap-2 text-text">
            <TriangleAlert aria-hidden="true" className="mt-0.5 size-4 shrink-0 text-warning-text" />
            <span>{issue}</span>
          </p>
          <Button size="sm" variant="outline" onClick={onAdjust} aria-label={line.suggestedQuantity > 0 ? `Ajustar ${name} a ${unitsLabel(line.suggestedQuantity)}` : `Quitar ${name} del carrito`}>
            {line.suggestedQuantity > 0 ? `Ajustar a ${line.suggestedQuantity}` : 'Quitar'}
          </Button>
        </div>
      )}

      <div className="mt-3 flex flex-wrap items-center justify-between gap-x-4 gap-y-2">
        <div className="flex items-center gap-2">
          {reservable ? (
            <QuantityStepper size="sm" value={line.quantity} min={1} max={Math.max(1, line.max)} onChange={onQuantityChange} label={`Cantidad de ${name}`} />
          ) : (
            <span className="text-sm text-text-muted">Pediste {unitsLabel(line.quantity)}</span>
          )}
          <IconButton size="sm" label={`Quitar ${name} del carrito`} icon={<Trash2 />} onClick={onRemove} />
        </div>
        <p className="text-right text-sm text-text-muted">
          {line.counted ? (
            <>
              <span className="sr-only">Subtotal de {name}: </span>
              <span aria-hidden="true">Subtotal </span>
              <span className="font-display text-base font-semibold text-text tabular-nums" data-testid="linea-subtotal">
                {formatMoney(line.subtotal)}
              </span>
            </>
          ) : (
            <span data-testid="linea-subtotal">No entra en el total</span>
          )}
        </p>
      </div>
    </li>
  );
}
