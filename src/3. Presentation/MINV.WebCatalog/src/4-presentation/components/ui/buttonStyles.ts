import clsx from 'clsx';

/**
 * Criterio de color de los botones (una sola semántica en todo el sitio):
 * - `primary` / `brand` (violeta): la acción de conversión de cada pantalla (agregar al armado, cargar un armado, armar).
 * - `accent` (cian): acciones secundarias positivas (filtros, enlaces, chips seleccionados). No compite con el CTA violeta.
 * - `cta` (rosa): SOLO promociones y ofertas.
 * - `danger` (rojo): confirmaciones destructivas («Sí, vaciar», «Sí, reemplazar»).
 * - `outline` / `ghost` / `subtle`: acciones neutras y de navegación.
 */
export type ButtonVariant = 'primary' | 'brand' | 'accent' | 'cta' | 'danger' | 'outline' | 'ghost' | 'subtle';
export type ButtonSize = 'sm' | 'md' | 'lg';

const VARIANTS: Record<ButtonVariant, string> = {
  primary: 'bg-primary text-white hover:bg-primary-hover',
  brand: 'bg-primary text-white ring-1 ring-accent/40 shadow-glow hover:bg-primary-hover hover:shadow-glow-accent',
  accent: 'bg-accent text-bg hover:bg-accent-hover',
  cta: 'bg-cta text-bg hover:bg-cta-hover',
  danger: 'bg-danger text-white hover:bg-danger/85',
  outline: 'border border-border-strong bg-transparent text-text hover:border-accent hover:text-accent-hover',
  ghost: 'bg-transparent text-text-muted hover:bg-surface-2 hover:text-text',
  subtle: 'border border-border bg-surface-2 text-text hover:border-border-strong hover:bg-surface-3',
};

/* Alturas mínimas (no fijas): un botón de ancho completo cuyo texto se parte en dos líneas crece en vez de recortarse. */
const SIZES: Record<ButtonSize, string> = {
  sm: 'min-h-9 px-3 py-1.5 text-sm gap-1.5 [&_svg]:size-4 max-sm:min-h-11',
  md: 'min-h-11 px-4 py-2 text-sm gap-2 [&_svg]:size-5',
  lg: 'min-h-12 px-6 py-2.5 text-base gap-2 [&_svg]:size-5',
};

/** Clases de un botón (para usarlas también en enlaces o elementos que no son `<Button>`). */
export function buttonClasses(options: {
  variant?: ButtonVariant;
  size?: ButtonSize;
  fullWidth?: boolean;
  className?: string;
}): string {
  return clsx(
    'inline-flex cursor-pointer select-none items-center justify-center rounded-xl font-semibold leading-tight',
    'transition-[background-color,border-color,color,box-shadow] duration-200 [&_svg]:shrink-0',
    'disabled:cursor-not-allowed disabled:opacity-50 aria-disabled:cursor-not-allowed aria-disabled:opacity-50',
    VARIANTS[options.variant ?? 'primary'],
    SIZES[options.size ?? 'md'],
    // A ancho completo el texto puede partirse (centrado); en línea se mantiene en una sola línea.
    options.fullWidth ? 'w-full whitespace-normal text-center' : 'whitespace-nowrap',
    options.className,
  );
}
