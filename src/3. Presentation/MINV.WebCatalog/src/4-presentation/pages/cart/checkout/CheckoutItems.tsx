// Resumen de lo que se reserva: cada producto con su imagen, cantidad, precio y subtotal, lo que cambió de disponibilidad
// (del catálogo fresco o de lo que respondió la tienda al rechazar la reserva) y el total. Un artículo suelto («Reservar
// ahora») permite cambiar la cantidad acá mismo; el carrito se edita en su página.

import clsx from 'clsx';
import { CircleAlert, Pencil, TriangleAlert } from 'lucide-react';
import { Link } from 'react-router-dom';
import type { StockShortage } from '@/1-domain/storefront/types';
import type { CartLine, CartReview, CheckoutSource } from '@/2-application';
import { shortagesBySku } from '@/2-application';
import { ROUTES } from '@/4-presentation/app/routes';
import { lineIssueText, unitsLabel } from '@/4-presentation/components/cart/cartText';
import { Button } from '@/4-presentation/components/ui/Button';
import { ProductImage } from '@/4-presentation/components/ui/ProductImage';
import { QuantityStepper } from '@/4-presentation/components/ui/QuantityStepper';
import { formatMoney } from '@/shared/format';
import { shortageText } from './checkoutText';

export interface CheckoutItemsProps {
  source: CheckoutSource;
  review: CartReview;
  /** Faltantes que informó la tienda al rechazar la reserva (409). */
  shortages: readonly StockShortage[];
  branchName: string;
  /** Solo para un artículo suelto: cambia la cantidad. */
  onItemQuantity?: (quantity: number) => void;
  disabled?: boolean;
}

function LineRow({ line, shortage, editable, onQuantity, disabled }: { line: CartLine; shortage?: StockShortage; editable: boolean; onQuantity?: (quantity: number) => void; disabled?: boolean }) {
  const name = line.product?.shortName ?? line.sku;
  const issue = shortage ? shortageText(shortage) : lineIssueText(line);
  return (
    <li className="py-3" data-testid="reserva-linea" data-sku={line.sku} data-status={shortage ? 'shortage' : line.status}>
      <div className="flex gap-3">
        {line.product ? (
          <ProductImage src={line.product.image} alt="" padding="sm" className="w-14 shrink-0 self-start" />
        ) : (
          <div aria-hidden="true" className="aspect-square w-14 shrink-0 self-start rounded-xl border border-border bg-surface-2" />
        )}
        <div className="min-w-0 flex-1">
          <p className="text-sm leading-snug font-medium break-words text-text">
            {line.product ? (
              <Link to={ROUTES.product(line.product.slug)} className="-my-3 inline-block py-3 transition-colors duration-200 hover:text-accent-hover">
                {name}
              </Link>
            ) : (
              name
            )}
          </p>
          <p className="mt-0.5 text-xs text-text-faint">
            <span className="font-mono break-all">{line.sku}</span>
            {line.product && (
              <span>
                {' '}
                · {line.quantity} × {formatMoney(line.unitPrice)}
              </span>
            )}
          </p>
          {editable && line.product && line.status !== 'unavailable' && line.status !== 'sold_out' && (
            <QuantityStepper
              size="sm"
              className="mt-2"
              value={line.quantity}
              min={1}
              max={Math.max(1, line.max)}
              onChange={(next) => onQuantity?.(next)}
              label={`Cantidad de ${name}`}
              disabled={disabled}
            />
          )}
          {issue && (
            <p className={clsx('mt-1.5 flex items-start gap-1.5 text-xs font-semibold', shortage ? 'text-danger-text' : 'text-warning-text')} data-testid="reserva-linea-aviso">
              {shortage ? <CircleAlert aria-hidden="true" className="mt-px size-3.5 shrink-0" /> : <TriangleAlert aria-hidden="true" className="mt-px size-3.5 shrink-0" />}
              <span>{issue}</span>
            </p>
          )}
        </div>
        <p className="shrink-0 text-right text-sm font-semibold text-text tabular-nums">{line.counted ? formatMoney(line.subtotal) : '—'}</p>
      </div>
    </li>
  );
}

export function CheckoutItems({ source, review, shortages, branchName, onItemQuantity, disabled }: CheckoutItemsProps) {
  const bySku = shortagesBySku(shortages);
  return (
    <div>
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 id="reserva-resumen" className="font-display text-xl font-semibold text-text">
          {source === 'cart' ? 'Tu carrito' : 'Lo que reservás'}
        </h2>
        {source === 'cart' && (
          <Button to={ROUTES.cart} size="sm" variant="ghost" leftIcon={<Pencil />} disabled={disabled}>
            Editar carrito
          </Button>
        )}
      </div>
      <ul aria-labelledby="reserva-resumen" className="mt-2 divide-y divide-border">
        {review.lines.map((line) => (
          <LineRow
            key={line.sku}
            line={line}
            shortage={bySku.get(line.sku.toUpperCase())}
            editable={source === 'item'}
            onQuantity={onItemQuantity}
            disabled={disabled}
          />
        ))}
      </ul>
      <dl className="mt-2 space-y-1.5 border-t border-border pt-3 text-sm">
        <div className="flex items-center justify-between gap-3">
          <dt className="text-text-muted">Unidades</dt>
          <dd className="font-medium text-text tabular-nums">{unitsLabel(review.count)}</dd>
        </div>
        {review.savings > 0 && (
          <div className="flex items-center justify-between gap-3">
            <dt className="text-text-muted">Ahorrás</dt>
            <dd className="font-medium text-cta-hover tabular-nums">{formatMoney(review.savings)}</dd>
          </div>
        )}
        <div className="flex items-end justify-between gap-3">
          <dt className="text-base font-semibold text-text">Total</dt>
          <dd className="font-display text-2xl font-semibold text-text tabular-nums" data-testid="reserva-total">
            {formatMoney(review.total)}
          </dd>
        </div>
      </dl>
      <p className="mt-2 text-xs text-text-faint">Precios en bolivianos con IVA incluido. Retirás y pagás en {branchName}; no hay pagos en línea.</p>
    </div>
  );
}
