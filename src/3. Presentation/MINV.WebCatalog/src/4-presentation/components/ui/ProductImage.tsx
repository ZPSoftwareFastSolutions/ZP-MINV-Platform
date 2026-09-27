import clsx from 'clsx';
import { ImageOff } from 'lucide-react';
import { useState } from 'react';

export interface ProductImageProps {
  src: string;
  alt: string;
  /** `priority` carga sin lazy (imágenes sobre el pliegue). */
  priority?: boolean;
  padding?: 'none' | 'sm' | 'md' | 'lg';
  className?: string;
  imgClassName?: string;
}

const PADDING = { none: 'p-0', sm: 'p-2', md: 'p-4', lg: 'p-6' } as const;

/**
 * Loseta de imagen de producto: las ilustraciones (360×360) comparten fondo azul oscuro con cuadrícula, así que
 * la loseta usa el token `tile` del mismo tono, esquinas redondeadas, borde sutil y un esqueleto mientras carga.
 */
export function ProductImage({ src, alt, priority = false, padding = 'md', className, imgClassName }: ProductImageProps) {
  const [state, setState] = useState<'loading' | 'ready' | 'error'>('loading');
  return (
    <div className={clsx('relative aspect-square overflow-hidden rounded-xl border border-border bg-tile', PADDING[padding], className)}>
      {state === 'loading' && <div aria-hidden="true" className="absolute inset-0 animate-pulse bg-surface-3" />}
      {state === 'error' ? (
        <div role="img" aria-label={alt} className="flex h-full w-full items-center justify-center text-text-faint">
          <ImageOff aria-hidden="true" className="size-10" />
        </div>
      ) : (
        <img
          src={src}
          alt={alt}
          width={360}
          height={360}
          loading={priority ? 'eager' : 'lazy'}
          decoding="async"
          fetchPriority={priority ? 'high' : undefined}
          onLoad={() => setState('ready')}
          onError={() => setState('error')}
          className={clsx(
            'relative h-full w-full object-contain transition-opacity duration-300',
            state === 'ready' ? 'opacity-100' : 'opacity-0',
            imgClassName,
          )}
        />
      )}
    </div>
  );
}
