import clsx from 'clsx';

export type ButtonVariant = 'primary' | 'brand' | 'accent' | 'cta' | 'outline' | 'ghost' | 'subtle';
export type ButtonSize = 'sm' | 'md' | 'lg';

const VARIANTS: Record<ButtonVariant, string> = {
  primary: 'bg-primary text-white hover:bg-primary-hover',
  brand: 'bg-primary text-white ring-1 ring-accent/40 shadow-glow hover:bg-primary-hover hover:shadow-glow-accent',
  accent: 'bg-accent text-bg hover:bg-accent-hover',
  cta: 'bg-cta text-bg hover:bg-cta-hover',
  outline: 'border border-border-strong bg-transparent text-text hover:border-accent hover:text-accent-hover',
  ghost: 'bg-transparent text-text-muted hover:bg-surface-2 hover:text-text',
  subtle: 'border border-border bg-surface-2 text-text hover:border-border-strong hover:bg-surface-3',
};

const SIZES: Record<ButtonSize, string> = {
  sm: 'h-9 px-3 text-sm gap-1.5 [&_svg]:size-4',
  md: 'h-11 px-4 text-sm gap-2 [&_svg]:size-5',
  lg: 'h-12 px-6 text-base gap-2 [&_svg]:size-5',
};

/** Clases de un botón (para usarlas también en enlaces o elementos que no son `<Button>`). */
export function buttonClasses(options: {
  variant?: ButtonVariant;
  size?: ButtonSize;
  fullWidth?: boolean;
  className?: string;
}): string {
  return clsx(
    'inline-flex cursor-pointer select-none items-center justify-center whitespace-nowrap rounded-xl font-semibold',
    'transition-[background-color,border-color,color,box-shadow] duration-200',
    'disabled:cursor-not-allowed disabled:opacity-50 aria-disabled:cursor-not-allowed aria-disabled:opacity-50',
    VARIANTS[options.variant ?? 'primary'],
    SIZES[options.size ?? 'md'],
    options.fullWidth && 'w-full',
    options.className,
  );
}
