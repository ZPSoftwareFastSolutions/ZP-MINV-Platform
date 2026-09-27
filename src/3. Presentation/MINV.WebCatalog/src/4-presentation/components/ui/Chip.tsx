import clsx from 'clsx';
import { X } from 'lucide-react';
import type { ButtonHTMLAttributes, ReactNode } from 'react';

export interface ChipProps extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'children'> {
  /** Filtro activo (aria-pressed). */
  selected?: boolean;
  /** Conteo a la derecha («ASUS 14»). */
  count?: number;
  icon?: ReactNode;
  /** Muestra una «x» y llama a `onRemove` (filtros aplicados). */
  onRemove?: () => void;
  size?: 'sm' | 'md';
  children: ReactNode;
}

/** Chip seleccionable para filtros (categoría, marca, condición). */
export function Chip({ selected = false, count, icon, onRemove, size = 'md', className, children, type = 'button', ...props }: ChipProps) {
  return (
    <button
      type={type}
      aria-pressed={onRemove ? undefined : selected}
      className={clsx(
        'inline-flex cursor-pointer items-center gap-1.5 whitespace-nowrap rounded-full border font-medium transition-colors duration-200',
        'disabled:cursor-not-allowed disabled:opacity-50',
        size === 'sm' ? 'h-9 px-3 text-sm [&_svg]:size-4' : 'h-11 px-4 text-sm [&_svg]:size-4',
        selected
          ? 'border-accent/60 bg-accent-soft text-accent-hover hover:border-accent'
          : 'border-border bg-surface-2 text-text-muted hover:border-border-strong hover:text-text',
        className,
      )}
      {...props}
    >
      {icon && (
        <span aria-hidden="true" className="inline-flex">
          {icon}
        </span>
      )}
      <span>{children}</span>
      {count != null && (
        <span className={clsx('rounded-full px-1.5 text-xs tabular-nums', selected ? 'bg-accent/20' : 'bg-surface-3')}>{count}</span>
      )}
      {onRemove && (
        <span
          role="presentation"
          className="-mr-1 inline-flex size-5 items-center justify-center rounded-full hover:bg-surface-3"
          onClick={(event) => {
            event.stopPropagation();
            onRemove();
          }}
        >
          <X aria-hidden="true" className="size-3.5" />
          <span className="sr-only">Quitar filtro</span>
        </span>
      )}
    </button>
  );
}
