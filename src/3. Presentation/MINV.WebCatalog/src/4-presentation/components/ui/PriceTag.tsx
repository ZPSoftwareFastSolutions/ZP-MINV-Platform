import clsx from 'clsx';
import { isOnSale, savingLabel } from '@/1-domain/catalog/money';
import { formatMoney, formatNumber } from '@/shared/format';
import { Badge } from './Badge';

export interface PriceTagProps {
  price: number;
  /** Precio de lista tachado (oferta). */
  listPrice?: number | null;
  size?: 'sm' | 'md' | 'lg' | 'xl';
  /** Muestra la insignia «-13 %» junto al precio de lista. */
  showSaving?: boolean;
  /** Agrega la leyenda «IVA incluido». */
  showTax?: boolean;
  align?: 'start' | 'end';
  className?: string;
}

const SIZES = {
  sm: 'text-base',
  md: 'text-xl',
  lg: 'text-2xl',
  xl: 'text-3xl sm:text-4xl',
} as const;

/** Precio en bolivianos con lista tachada y porcentaje de ahorro. Texto accesible completo para lectores de pantalla. */
export function PriceTag({ price, listPrice, size = 'md', showSaving = true, showTax = false, align = 'start', className }: PriceTagProps) {
  const onSale = isOnSale(price, listPrice);
  return (
    <div className={clsx('flex flex-col', align === 'end' ? 'items-end text-right' : 'items-start', className)}>
      {onSale && (
        <div className="flex items-center gap-2 text-xs">
          <s className="text-text-faint tabular-nums">
            <span className="sr-only">Antes </span>
            {formatMoney(listPrice as number)}
          </s>
          {showSaving && (
            <Badge tone="oferta" size="sm">
              {savingLabel(price, listPrice)}
            </Badge>
          )}
        </div>
      )}
      <p className={clsx('font-display font-semibold leading-tight text-text tabular-nums', SIZES[size])}>
        <span className="sr-only">{`Precio: ${formatMoney(price)}`}</span>
        <span aria-hidden="true">
          <span className="mr-1 text-[0.65em] font-medium text-text-muted">Bs</span>
          {formatNumber(price, { decimals: 2 })}
        </span>
      </p>
      {showTax && <p className="text-xs text-text-faint">IVA incluido</p>}
    </div>
  );
}
