// Navegación por categorías de escritorio (lg+): raíces con mega-menú de hijas, «Más» para las secundarias y
// accesos a Armá tu PC, PC armadas y Ofertas. Se abre con clic, teclado (Enter/Espacio) o al pasar el mouse;
// Escape, clic afuera y navegar la cierran.

import clsx from 'clsx';
import { BadgePercent, ChevronDown, Cpu, Sparkles } from 'lucide-react';
import { useEffect, useId, useRef, useState, type FocusEvent, type KeyboardEvent, type ReactNode } from 'react';
import { Link, NavLink, matchPath, useLocation } from 'react-router-dom';
import type { CategoryNode } from '@/1-domain/catalog/categories';
import { ROUTES } from '@/4-presentation/app/routes';
import { Button } from '@/4-presentation/components/ui/Button';
import { CategoryIcon } from '@/4-presentation/components/ui/CategoryIcon';
import { Container } from '@/4-presentation/components/ui/Container';
import { useBuilder } from '@/4-presentation/hooks/useBuilder';
import { useServices } from '@/4-presentation/hooks/useServices';
import { pluralize } from '@/shared/format';

/** Raíces con enlace directo en la barra; el resto va bajo «Más». */
const PRIMARY_ROOTS = ['COMP', 'PC', 'MON', 'PER', 'CON', 'JUE'];

const ITEM =
  'relative inline-flex h-12 cursor-pointer items-center gap-1 whitespace-nowrap px-2 xl:px-2.5 text-sm font-medium transition-colors duration-200 rounded-lg';
const ITEM_IDLE = 'text-text-muted hover:text-text';
const ITEM_ACTIVE = 'text-text after:absolute after:inset-x-2 after:bottom-0 after:h-0.5 after:rounded-full after:bg-accent';

const OPEN_DELAY_MS = 80;
const CLOSE_DELAY_MS = 160;

interface DropdownItemProps {
  label: string;
  active: boolean;
  panelClassName?: string;
  children: ReactNode;
}

function DropdownItem({ label, active, panelClassName, children }: DropdownItemProps) {
  const panelId = useId();
  const [open, setOpen] = useState(false);
  const wrapperRef = useRef<HTMLLIElement>(null);
  const buttonRef = useRef<HTMLButtonElement>(null);
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const location = useLocation();

  // Se cierra al navegar (ajuste de estado durante el render, sin efectos).
  const [lastKey, setLastKey] = useState(location.key);
  if (lastKey !== location.key) {
    setLastKey(location.key);
    setOpen(false);
  }

  useEffect(() => {
    if (!open) return;
    const onPointerDown = (event: PointerEvent) => {
      if (!wrapperRef.current?.contains(event.target as Node)) setOpen(false);
    };
    document.addEventListener('pointerdown', onPointerDown);
    return () => document.removeEventListener('pointerdown', onPointerDown);
  }, [open]);

  useEffect(
    () => () => {
      if (timer.current) clearTimeout(timer.current);
    },
    [],
  );

  const schedule = (next: boolean, delay: number) => {
    if (timer.current) clearTimeout(timer.current);
    timer.current = setTimeout(() => setOpen(next), delay);
  };

  const onKeyDown = (event: KeyboardEvent<HTMLLIElement>) => {
    if (event.key === 'Escape' && open) {
      event.stopPropagation();
      setOpen(false);
      buttonRef.current?.focus();
    }
  };

  const onBlur = (event: FocusEvent<HTMLLIElement>) => {
    if (!wrapperRef.current?.contains(event.relatedTarget as Node | null)) setOpen(false);
  };

  return (
    <li
      ref={wrapperRef}
      className="relative"
      onMouseEnter={() => schedule(true, OPEN_DELAY_MS)}
      onMouseLeave={() => schedule(false, CLOSE_DELAY_MS)}
      onKeyDown={onKeyDown}
      onBlur={onBlur}
    >
      <button
        ref={buttonRef}
        type="button"
        aria-expanded={open}
        aria-controls={panelId}
        aria-haspopup="true"
        onClick={() => setOpen((current) => !current)}
        className={clsx(ITEM, active || open ? ITEM_ACTIVE : ITEM_IDLE)}
      >
        {label}
        <ChevronDown aria-hidden="true" className={clsx('size-4 transition-transform duration-200', open && 'rotate-180')} />
      </button>
      {open && (
        <div
          id={panelId}
          className={clsx(
            'absolute top-full left-0 z-40 mt-1 rounded-card border border-border bg-surface-2 p-3 shadow-card animate-fade-up',
            panelClassName,
          )}
        >
          {children}
        </div>
      )}
    </li>
  );
}

function ChildLink({ node }: { node: CategoryNode }) {
  return (
    <Link to={ROUTES.category(node.slug)} className="flex items-center gap-3 rounded-xl p-2.5 transition-colors duration-200 hover:bg-surface-3">
      <span className="flex size-10 shrink-0 items-center justify-center rounded-lg bg-surface-3 text-accent">
        <CategoryIcon name={node.icon} className="size-5" />
      </span>
      <span className="min-w-0">
        <span className="block truncate text-sm font-medium text-text">{node.name}</span>
        <span className="block text-xs text-text-faint">{pluralize(node.productCount, 'producto', 'productos')}</span>
      </span>
    </Link>
  );
}

function MegaMenu({ root }: { root: CategoryNode }) {
  return (
    <div className="grid w-[min(52rem,calc(100vw-4rem))] grid-cols-[1fr_15rem] gap-4">
      <div>
        <ul className="grid grid-cols-2 gap-1 xl:grid-cols-3">
          {root.children.map((child) => (
            <li key={child.code}>
              <ChildLink node={child} />
            </li>
          ))}
        </ul>
        <Link
          to={ROUTES.category(root.slug)}
          className="mt-2 inline-flex h-10 items-center rounded-lg px-2.5 text-sm font-semibold text-accent transition-colors duration-200 hover:text-accent-hover"
        >
          Ver todo en {root.name} →
        </Link>
      </div>
      <div className="flex flex-col justify-between rounded-xl border border-primary/40 bg-primary-soft/60 p-4">
        <div>
          <p className="font-display text-lg font-semibold text-text">Armá tu PC</p>
          <p className="mt-1 text-sm text-text-muted">Elegí pieza por pieza y mirá el total al instante. Sin compromiso.</p>
        </div>
        <Button to={ROUTES.builder} variant="accent" size="sm" leftIcon={<Cpu />} className="mt-4 self-start">
          Empezar
        </Button>
      </div>
    </div>
  );
}

export function CategoryNav() {
  const { catalog } = useServices();
  const { count } = useBuilder();
  const location = useLocation();
  const tree = catalog.getCategoryTree();
  const primary = PRIMARY_ROOTS.map((code) => tree.find((node) => node.code === code)).filter((node): node is CategoryNode => node !== undefined);
  const secondary = tree.filter((node) => !PRIMARY_ROOTS.includes(node.code));

  const match = matchPath('/catalogo/:categoria', location.pathname);
  const current = match?.params.categoria ? catalog.getCategory(match.params.categoria) : undefined;
  const activeRoot = current ? catalog.getCategoryPath(current.code)[0]?.code : undefined;
  const offersActive = location.pathname === ROUTES.catalog && new URLSearchParams(location.search).get('tags') === 'oferta';
  const presetsActive = location.pathname === ROUTES.builder && location.hash === '#armados';

  return (
    <nav aria-label="Categorías" className="hidden border-t border-border/60 lg:block">
      <Container>
        <ul className="flex min-w-0 items-center gap-0.5">
          {primary.map((root) =>
            root.children.length > 0 ? (
              <DropdownItem key={root.code} label={root.name} active={activeRoot === root.code}>
                <MegaMenu root={root} />
              </DropdownItem>
            ) : (
              <li key={root.code}>
                <NavLink to={ROUTES.category(root.slug)} className={({ isActive }) => clsx(ITEM, isActive ? ITEM_ACTIVE : ITEM_IDLE)}>
                  {root.name}
                </NavLink>
              </li>
            ),
          )}
          <DropdownItem label="Más" active={secondary.some((root) => root.code === activeRoot)} panelClassName="w-72">
            <ul className="flex flex-col gap-0.5">
              {secondary.map((root) => (
                <li key={root.code}>
                  <ChildLink node={root} />
                </li>
              ))}
            </ul>
          </DropdownItem>

          <li className="ml-auto">
            <NavLink
              to={ROUTES.builder}
              end
              className={({ isActive }) => clsx(ITEM, isActive && !presetsActive ? ITEM_ACTIVE : ITEM_IDLE, 'text-accent-hover hover:text-accent-hover')}
            >
              <Cpu aria-hidden="true" className="size-4" />
              Armá tu PC
              {count > 0 && (
                <span className="ml-0.5 rounded-full bg-accent px-1.5 font-display text-xs font-bold text-bg" aria-label={`${count} piezas en tu armado`}>
                  {count}
                </span>
              )}
            </NavLink>
          </li>
          <li className="hidden xl:block">
            <Link to={ROUTES.presets} className={clsx(ITEM, presetsActive ? ITEM_ACTIVE : ITEM_IDLE)} aria-current={presetsActive ? 'page' : undefined}>
              <Sparkles aria-hidden="true" className="size-4" />
              PC armadas
            </Link>
          </li>
          <li>
            <Link
              to={ROUTES.offers}
              className={clsx(ITEM, offersActive ? ITEM_ACTIVE : ITEM_IDLE, 'text-cta-hover hover:text-cta-hover')}
              aria-current={offersActive ? 'page' : undefined}
            >
              <BadgePercent aria-hidden="true" className="size-4" />
              Ofertas
            </Link>
          </li>
        </ul>
      </Container>
    </nav>
  );
}
