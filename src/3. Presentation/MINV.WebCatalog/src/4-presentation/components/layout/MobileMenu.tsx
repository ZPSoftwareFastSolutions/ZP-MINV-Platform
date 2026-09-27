// Menú móvil en un cajón izquierdo: accesos rápidos, categorías en acordeón y contacto. Se cierra al navegar.

import clsx from 'clsx';
import { BadgePercent, ChevronDown, Cpu, MessageCircle, Phone, Sparkles } from 'lucide-react';
import { useState } from 'react';
import { Link, NavLink } from 'react-router-dom';
import type { CategoryNode } from '@/1-domain/catalog/categories';
import { ROUTES } from '@/4-presentation/app/routes';
import { Button } from '@/4-presentation/components/ui/Button';
import { CategoryIcon } from '@/4-presentation/components/ui/CategoryIcon';
import { Drawer } from '@/4-presentation/components/ui/Drawer';
import { useBuilder } from '@/4-presentation/hooks/useBuilder';
import { useServices } from '@/4-presentation/hooks/useServices';
import { STORE } from '@/shared/constants';

const LINK = 'flex min-h-12 items-center gap-3 rounded-xl px-3 text-base font-medium transition-colors duration-200';
const LINK_IDLE = 'text-text hover:bg-surface-2';
const LINK_ACTIVE = 'bg-surface-2 text-accent-hover';

function AccordionRoot({ node, open, onToggle }: { node: CategoryNode; open: boolean; onToggle: () => void }) {
  const panelId = `menu-${node.code}`;
  return (
    <li>
      <button
        type="button"
        aria-expanded={open}
        aria-controls={panelId}
        onClick={onToggle}
        className={clsx(LINK, 'w-full cursor-pointer', open ? 'text-text' : LINK_IDLE)}
      >
        <span className="flex size-9 items-center justify-center rounded-lg bg-surface-2 text-accent">
          <CategoryIcon name={node.icon} className="size-5" />
        </span>
        <span className="flex-1 text-left">{node.name}</span>
        <ChevronDown aria-hidden="true" className={clsx('size-5 text-text-faint transition-transform duration-200', open && 'rotate-180')} />
      </button>
      {open && (
        <ul id={panelId} className="mt-1 mb-2 ml-6 space-y-0.5 border-l border-border pl-3">
          <li>
            <NavLink to={ROUTES.category(node.slug)} end className={({ isActive }) => clsx(LINK, 'min-h-11 text-sm font-semibold', isActive ? LINK_ACTIVE : LINK_IDLE)}>
              Ver todo en {node.name}
            </NavLink>
          </li>
          {node.children.map((child) => (
            <li key={child.code}>
              <NavLink to={ROUTES.category(child.slug)} className={({ isActive }) => clsx(LINK, 'min-h-11 text-sm', isActive ? LINK_ACTIVE : LINK_IDLE)}>
                <span className="flex-1">{child.name}</span>
                <span className="text-xs text-text-faint tabular-nums">{child.productCount}</span>
              </NavLink>
            </li>
          ))}
        </ul>
      )}
    </li>
  );
}

export function MobileMenu({ open, onClose }: { open: boolean; onClose: () => void }) {
  const { catalog } = useServices();
  const { count } = useBuilder();
  const [expanded, setExpanded] = useState<string | null>(null);
  const tree = catalog.getCategoryTree();

  return (
    <Drawer open={open} onClose={onClose} title="Menú" side="left" size="sm">
      <div className="space-y-6">
        <div className="space-y-2">
          <Button to={ROUTES.builder} variant="brand" fullWidth leftIcon={<Cpu />}>
            Armá tu PC
            {count > 0 && (
              <span className="ml-1 rounded-full bg-white/20 px-2 text-xs">
                {count}
                <span className="sr-only">{count === 1 ? ' pieza en tu armado' : ' piezas en tu armado'}</span>
              </span>
            )}
          </Button>
          <div className="grid grid-cols-2 gap-2">
            <Link to={ROUTES.presets} className={clsx(LINK, LINK_IDLE, 'justify-center border border-border text-sm')}>
              <Sparkles aria-hidden="true" className="size-4 text-accent" />
              PC armadas
            </Link>
            <Link to={ROUTES.offers} className={clsx(LINK, LINK_IDLE, 'justify-center border border-border text-sm text-cta-hover')}>
              <BadgePercent aria-hidden="true" className="size-4" />
              Ofertas
            </Link>
          </div>
        </div>

        <nav aria-label="Categorías">
          <p className="mb-2 px-3 text-xs font-semibold uppercase tracking-[0.2em] text-text-faint">Categorías</p>
          <ul className="space-y-0.5">
            {tree.map((node) =>
              node.children.length > 0 ? (
                <AccordionRoot
                  key={node.code}
                  node={node}
                  open={expanded === node.code}
                  onToggle={() => setExpanded((current) => (current === node.code ? null : node.code))}
                />
              ) : (
                <li key={node.code}>
                  <NavLink to={ROUTES.category(node.slug)} className={({ isActive }) => clsx(LINK, isActive ? LINK_ACTIVE : LINK_IDLE)}>
                    <span className="flex size-9 items-center justify-center rounded-lg bg-surface-2 text-accent">
                      <CategoryIcon name={node.icon} className="size-5" />
                    </span>
                    <span className="flex-1">{node.name}</span>
                    <span className="text-xs text-text-faint tabular-nums">{node.productCount}</span>
                  </NavLink>
                </li>
              ),
            )}
          </ul>
        </nav>

        <div className="rounded-xl border border-border bg-surface-2 p-4 text-sm">
          <p className="font-semibold text-text">¿Te ayudamos a elegir?</p>
          <p className="mt-1 text-text-muted">{STORE.hours}</p>
          <div className="mt-3 flex flex-col gap-2">
            <a href={STORE.whatsappUrl} target="_blank" rel="noreferrer noopener" className={clsx(LINK, LINK_IDLE, '-mx-3 min-h-11 text-sm')}>
              <MessageCircle aria-hidden="true" className="size-5 text-success" />
              WhatsApp {STORE.whatsapp}
            </a>
            <a href={`tel:${STORE.phone.replace(/\s+/g, '')}`} className={clsx(LINK, LINK_IDLE, '-mx-3 min-h-11 text-sm')}>
              <Phone aria-hidden="true" className="size-5 text-accent" />
              {STORE.phone}
            </a>
          </div>
        </div>
      </div>
    </Drawer>
  );
}
