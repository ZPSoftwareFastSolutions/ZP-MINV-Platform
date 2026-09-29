// Acceso de la cabecera. Sin sesión: el botón «Ingresar» (visible en escritorio y en móvil). Con sesión: el nombre de
// la persona con un menú (personal: «Ir al panel»; cliente: «Mi cuenta» y «Mis reservas»; ambos: «Cambiar contraseña» y
// «Cerrar sesión»). Menú accesible: botón con `aria-haspopup`, flechas para moverse, Inicio/Fin, Escape cierra y
// devuelve el foco, y se cierra al salir con Tab, al hacer clic afuera y al navegar.

import clsx from 'clsx';
import { ChevronDown, LogIn, LogOut } from 'lucide-react';
import { useEffect, useId, useRef, useState, type KeyboardEvent } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { initialsOf } from '@/1-domain/auth/permissions';
import { ROUTES } from '@/4-presentation/app/routes';
import { Button } from '@/4-presentation/components/ui/Button';
import { Skeleton } from '@/4-presentation/components/ui/Skeleton';
import { useLogout } from '@/4-presentation/hooks/useLogout';
import { useSession } from '@/4-presentation/hooks/useSession';
import { sessionLinks } from './sessionLinks';

const ITEM =
  'flex min-h-11 w-full cursor-pointer items-center gap-3 rounded-lg px-3 text-left text-sm font-medium text-text transition-colors duration-200 hover:bg-surface-3 focus-visible:bg-surface-3 [&_svg]:size-4 [&_svg]:shrink-0 [&_svg]:text-accent';

export function UserMenu({ className }: { className?: string }) {
  const { status, session } = useSession();
  const { logout, leaving } = useLogout();
  const location = useLocation();
  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);
  const buttonRef = useRef<HTMLButtonElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);
  const buttonId = useId();
  const menuId = useId();

  // Se cierra al navegar.
  const [lastKey, setLastKey] = useState(location.key);
  if (lastKey !== location.key) {
    setLastKey(location.key);
    setOpen(false);
  }

  // Al abrir, el foco va a la primera opción; un clic fuera lo cierra.
  useEffect(() => {
    if (!open) return;
    menuRef.current?.querySelector<HTMLElement>('[role="menuitem"]')?.focus();
    const onPointerDown = (event: PointerEvent) => {
      if (!rootRef.current?.contains(event.target as Node)) setOpen(false);
    };
    document.addEventListener('pointerdown', onPointerDown);
    return () => document.removeEventListener('pointerdown', onPointerDown);
  }, [open]);

  if (status === 'loading') return <Skeleton className={clsx('h-11 w-24 rounded-xl', className)} />;

  if (!session) {
    return (
      <Button to={ROUTES.login} variant="outline" leftIcon={<LogIn />} className={clsx('max-sm:px-3 max-sm:[&_svg]:hidden', className)}>
        Ingresar
      </Button>
    );
  }

  const links = sessionLinks(session);

  const close = (restoreFocus: boolean) => {
    setOpen(false);
    if (restoreFocus) buttonRef.current?.focus();
  };

  const onMenuKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    const items = Array.from(menuRef.current?.querySelectorAll<HTMLElement>('[role="menuitem"]') ?? []);
    const index = items.indexOf(document.activeElement as HTMLElement);
    let next = -1;
    if (event.key === 'ArrowDown') next = (index + 1) % items.length;
    else if (event.key === 'ArrowUp') next = (index - 1 + items.length) % items.length;
    else if (event.key === 'Home') next = 0;
    else if (event.key === 'End') next = items.length - 1;
    else if (event.key === 'Escape') {
      event.preventDefault();
      event.stopPropagation();
      close(true);
      return;
    } else if (event.key === 'Tab') {
      setOpen(false);
      return;
    } else return;
    event.preventDefault();
    items[next]?.focus();
  };

  return (
    <div ref={rootRef} className={clsx('relative', className)}>
      <button
        ref={buttonRef}
        id={buttonId}
        type="button"
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={open ? menuId : undefined}
        aria-label={`Cuenta de ${session.displayName}`}
        title={session.displayName}
        onClick={() => setOpen((current) => !current)}
        onKeyDown={(event) => {
          if (event.key === 'ArrowDown' && !open) {
            event.preventDefault();
            setOpen(true);
          }
        }}
        className="flex min-h-11 max-w-56 cursor-pointer items-center gap-2 rounded-xl border border-border bg-surface-2 py-1 pr-2 pl-1.5 text-sm font-semibold text-text transition-colors duration-200 hover:border-border-strong hover:bg-surface-3"
      >
        <span aria-hidden="true" className="flex size-8 shrink-0 items-center justify-center rounded-lg bg-primary-soft font-display text-xs font-bold text-primary-text">
          {initialsOf(session)}
        </span>
        <span className="min-w-0 truncate max-lg:hidden" data-testid="usuario-nombre">
          {session.displayName}
        </span>
        <ChevronDown aria-hidden="true" className={clsx('size-4 shrink-0 text-text-muted transition-transform duration-200', open && 'rotate-180')} />
      </button>

      {open && (
        <div
          ref={menuRef}
          id={menuId}
          role="menu"
          aria-labelledby={buttonId}
          onKeyDown={onMenuKeyDown}
          className="absolute right-0 z-40 mt-2 w-64 max-w-[calc(100vw-2rem)] rounded-card border border-border bg-surface-2 p-2 shadow-card"
        >
          <div className="border-b border-border px-3 pt-1 pb-3">
            <p className="truncate text-sm font-semibold text-text">{session.displayName}</p>
            <p className="truncate text-xs text-text-muted">{session.email}</p>
          </div>
          <div className="mt-2 space-y-0.5">
            {links.map(({ id, label, to, icon: Icon }) => (
              <Link key={id} to={to} role="menuitem" tabIndex={-1} className={ITEM} onClick={() => setOpen(false)}>
                <Icon aria-hidden="true" />
                {label}
              </Link>
            ))}
            <button
              type="button"
              role="menuitem"
              tabIndex={-1}
              disabled={leaving}
              className={clsx(ITEM, 'disabled:cursor-not-allowed disabled:opacity-50')}
              onClick={() => {
                close(false);
                void logout();
              }}
            >
              <LogOut aria-hidden="true" />
              Cerrar sesión
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
