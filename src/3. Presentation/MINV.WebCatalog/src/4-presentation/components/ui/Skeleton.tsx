import clsx from 'clsx';

export interface SkeletonProps {
  className?: string;
}

/** Bloque de carga (aria-hidden). Combinar con clases de tamaño: `<Skeleton className="h-4 w-32" />`. */
export function Skeleton({ className }: SkeletonProps) {
  return <div aria-hidden="true" className={clsx('animate-pulse rounded-lg bg-surface-3', className)} />;
}
