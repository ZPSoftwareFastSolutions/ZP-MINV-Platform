import clsx from 'clsx';
import { Minus, Plus } from 'lucide-react';
import { useId } from 'react';

export interface QuantityStepperProps {
  value: number;
  onChange: (next: number) => void;
  min?: number;
  max?: number;
  /** Nombre accesible («Cantidad de SSD Kingston NV3 1 TB»). */
  label: string;
  size?: 'sm' | 'md';
  disabled?: boolean;
  className?: string;
}

/** Selector de cantidad con botones −/+ y campo numérico accesible. */
export function QuantityStepper({ value, onChange, min = 1, max = 10, label, size = 'md', disabled = false, className }: QuantityStepperProps) {
  const inputId = useId();
  const clamp = (next: number) => Math.min(max, Math.max(min, Math.trunc(next)));
  const button = clsx(
    'inline-flex cursor-pointer items-center justify-center text-text-muted transition-colors duration-200 hover:bg-surface-3 hover:text-text disabled:cursor-not-allowed disabled:opacity-40',
    size === 'sm' ? 'size-9 [&_svg]:size-4' : 'size-11 [&_svg]:size-4',
  );
  return (
    <div role="group" aria-label={label} className={clsx('inline-flex items-center overflow-hidden rounded-xl border border-border bg-surface-2', className)}>
      <button type="button" className={button} aria-label="Quitar una unidad" disabled={disabled || value <= min} onClick={() => onChange(clamp(value - 1))}>
        <Minus aria-hidden="true" />
      </button>
      <label htmlFor={inputId} className="sr-only">
        {label}
      </label>
      <input
        id={inputId}
        type="number"
        inputMode="numeric"
        min={min}
        max={max}
        value={value}
        disabled={disabled}
        onChange={(event) => {
          const next = Number(event.target.value);
          if (Number.isFinite(next)) onChange(clamp(next));
        }}
        className={clsx(
          'w-10 bg-transparent text-center text-sm font-semibold text-text tabular-nums outline-none [appearance:textfield] [&::-webkit-inner-spin-button]:appearance-none [&::-webkit-outer-spin-button]:appearance-none',
          size === 'sm' ? 'h-9' : 'h-11',
        )}
      />
      <button type="button" className={button} aria-label="Agregar una unidad" disabled={disabled || value >= max} onClick={() => onChange(clamp(value + 1))}>
        <Plus aria-hidden="true" />
      </button>
    </div>
  );
}
