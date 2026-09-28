import clsx from 'clsx';
import { stockLabel, stockStatus, type StockInfo, type StockStatus } from '@/1-domain/catalog/stock';

const DOT: Record<StockStatus, string> = {
  disponible: 'bg-success',
  ultimas: 'bg-warning',
  reservado: 'bg-accent',
  agotado: 'bg-danger',
};

const TEXT: Record<StockStatus, string> = {
  disponible: 'text-success-text',
  ultimas: 'text-warning-text',
  reservado: 'text-accent-hover',
  agotado: 'text-danger-text',
};

/**
 * Punto de color + texto («Disponible (12)», «Últimas 3 unidades», «Reservado», «Agotado»). El color nunca va solo.
 * «Reservado» (V6): no queda disponible pero hay unidades reservadas que pueden liberarse (regla S-03).
 */
export function StockIndicator({ product, className }: { product: StockInfo; className?: string }) {
  const status = stockStatus(product);
  return (
    <span className={clsx('inline-flex items-center gap-1.5 whitespace-nowrap text-xs font-medium', TEXT[status], className)} data-stock={status}>
      <span aria-hidden="true" className={clsx('size-2 shrink-0 rounded-full', DOT[status])} />
      {stockLabel(product)}
    </span>
  );
}
