import clsx from 'clsx';
import { Link } from 'react-router-dom';
import { ROUTES } from '@/4-presentation/app/routes';
import { STORE } from '@/shared/constants';

export interface LogoProps {
  className?: string;
  /** Solo el ícono, sin el texto «TECH ZONE». */
  compact?: boolean;
  /** Oculta el texto en pantallas muy angostas (menos de 480 px): deja lugar a los botones de la cabecera. */
  compactOnNarrow?: boolean;
}

/** Ícono + wordmark «TECH ZONE» con el degradado de marca. */
export function Logo({ className, compact = false, compactOnNarrow = false }: LogoProps) {
  return (
    <Link to={ROUTES.home} aria-label={`${STORE.legalName}, ir al inicio`} className={clsx('flex shrink-0 items-center gap-2.5 rounded-lg', className)}>
      <img src="/images/logo-256.png" alt="" width={40} height={40} className="size-10 rounded-xl" />
      {!compact && (
        <span className={clsx('flex flex-col leading-none', compactOnNarrow && 'max-[30rem]:hidden')}>
          <span className="font-display text-lg font-bold tracking-tight text-gradient-brand">{STORE.wordmark}</span>
          <span className="text-[0.625rem] font-semibold uppercase tracking-[0.32em] text-text-muted">Gaming</span>
        </span>
      )}
    </Link>
  );
}
