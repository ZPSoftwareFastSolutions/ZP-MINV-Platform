import clsx from 'clsx';

export interface SkeletonProps {
  className?: string;
}

/** Bloque de carga (aria-hidden). Combinar con clases de tamaño: `<Skeleton className="h-4 w-32" />`. */
export function Skeleton({ className }: SkeletonProps) {
  return <div aria-hidden="true" className={clsx('animate-pulse rounded-lg bg-surface-3', className)} />;
}

export function SkeletonText({ lines = 3, className }: { lines?: number; className?: string }) {
  return (
    <div aria-hidden="true" className={clsx('space-y-2', className)}>
      {Array.from({ length: lines }, (_, index) => (
        <Skeleton key={index} className={clsx('h-3.5', index === lines - 1 ? 'w-2/3' : 'w-full')} />
      ))}
    </div>
  );
}
