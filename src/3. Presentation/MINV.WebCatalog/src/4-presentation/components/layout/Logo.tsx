import clsx from 'clsx';
import { Link } from 'react-router-dom';
import { ROUTES } from '@/4-presentation/app/routes';
import { STORE } from '@/shared/constants';

/** Ícono + wordmark «TECH ZONE» con el degradado de marca. */
export function Logo({ className, compact = false }: { className?: string; compact?: boolean }) {
  return (
    <Link to={ROUTES.home} aria-label={`${STORE.legalName}, ir al inicio`} className={clsx('flex shrink-0 items-center gap-2.5 rounded-lg', className)}>
      <img src="/images/logo-256.png" alt="" width={40} height={40} className="size-10 rounded-xl" />
      {!compact && (
        <span className="flex flex-col leading-none">
          <span className="font-display text-lg font-bold tracking-tight text-gradient-brand">{STORE.wordmark}</span>
          <span className="text-[0.625rem] font-semibold uppercase tracking-[0.32em] text-text-muted">Gaming</span>
        </span>
      )}
    </Link>
  );
}
