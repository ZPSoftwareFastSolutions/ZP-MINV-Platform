import clsx from 'clsx';
import type { HTMLAttributes } from 'react';

type ContainerTag = 'div' | 'section' | 'nav' | 'header' | 'footer' | 'main' | 'article';

export interface ContainerProps extends HTMLAttributes<HTMLElement> {
  as?: ContainerTag;
  /** `default` = max-w-7xl (todo el sitio) · `narrow` = max-w-3xl (textos) · `wide` = pantalla completa con margen. */
  size?: 'default' | 'narrow' | 'wide';
}

const SIZES = {
  default: 'max-w-7xl',
  narrow: 'max-w-3xl',
  wide: 'max-w-none',
} as const;

/** Contenedor único del sitio: ancho máximo consistente y margen lateral de 16 px en móvil. */
export function Container({ as: Tag = 'div', size = 'default', className, ...props }: ContainerProps) {
  return <Tag className={clsx('mx-auto w-full px-4 sm:px-6 lg:px-8', SIZES[size], className)} {...props} />;
}
