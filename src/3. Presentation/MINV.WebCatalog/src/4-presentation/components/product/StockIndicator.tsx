import clsx from 'clsx';
import { stockLabel, stockStatus, type StockStatus } from '@/1-domain/catalog/stock';
import type { Product } from '@/1-domain/catalog/types';

const DOT: Record<StockStatus, string> = {
  disponible: 'bg-success',
  ultimas: 'bg-warning',
  agotado: 'bg-danger',
};

const TEXT: Record<StockStatus, string> = {
  disponible: 'text-success-text',
  ultimas: 'text-warning-text',
  agotado: 'text-danger-text',
};

/** Punto de color + texto («En stock», «Últimas 3 unidades», «Agotado»). El color nunca va solo. */
export function StockIndicator({ product, className }: { product: Pick<Product, 'stock'>; className?: string }) {
  const status = stockStatus(product);
  return (
    <span className={clsx('inline-flex items-center gap-1.5 whitespace-nowrap text-xs font-medium', TEXT[status], className)}>
      <span aria-hidden="true" className={clsx('size-2 shrink-0 rounded-full', DOT[status])} />
      {stockLabel(product)}
    </span>
  );
}
