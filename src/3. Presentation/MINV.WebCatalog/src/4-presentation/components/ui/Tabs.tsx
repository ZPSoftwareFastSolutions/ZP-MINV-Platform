import clsx from 'clsx';
import { useRef, type KeyboardEvent, type ReactNode } from 'react';

export interface TabItem<T extends string = string> {
  id: T;
  label: string;
  /** Etiqueta corta para pantallas angostas (la completa queda para lectores de pantalla). */
  shortLabel?: string;
  icon?: ReactNode;
  count?: number;
  disabled?: boolean;
}

export interface TabsProps<T extends string = string> {
  tabs: readonly TabItem<T>[];
  value: T;
  onChange: (id: T) => void;
  /** Nombre accesible del grupo («Secciones del producto»). */
  label: string;
  variant?: 'underline' | 'pills';
  /** Las pestañas se reparten el ancho (en vez de desplazarse) en pantallas angostas. */
  stretch?: boolean;
  className?: string;
}

/** Pestañas accesibles (role=tablist, flechas para moverse, Home/End). Cada panel usa `<TabPanel id activeId>`. */
export function Tabs<T extends string = string>({ tabs, value, onChange, label, variant = 'underline', stretch = false, className }: TabsProps<T>) {
  const listRef = useRef<HTMLDivElement>(null);

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    const enabled = tabs.filter((tab) => !tab.disabled);
    const index = enabled.findIndex((tab) => tab.id === value);
    if (index < 0) return;
    let next = index;
    if (event.key === 'ArrowRight') next = (index + 1) % enabled.length;
    else if (event.key === 'ArrowLeft') next = (index - 1 + enabled.length) % enabled.length;
    else if (event.key === 'Home') next = 0;
    else if (event.key === 'End') next = enabled.length - 1;
    else return;
    event.preventDefault();
    const target = enabled[next];
    if (!target) return;
    onChange(target.id);
    listRef.current?.querySelector<HTMLButtonElement>(`[data-tab-id="${target.id}"]`)?.focus();
  };

  return (
    <div
      ref={listRef}
      role="tablist"
      aria-label={label}
      onKeyDown={onKeyDown}
      className={clsx(
        'flex gap-1 overflow-x-auto scrollbar-none',
        variant === 'underline' ? 'border-b border-border' : 'rounded-xl bg-surface-2 p-1',
        className,
      )}
    >
      {tabs.map((tab) => {
        const selected = tab.id === value;
        return (
          <button
            key={tab.id}
            type="button"
            role="tab"
            id={`tab-${tab.id}`}
            data-tab-id={tab.id}
            aria-selected={selected}
            aria-controls={`panel-${tab.id}`}
            tabIndex={selected ? 0 : -1}
            disabled={tab.disabled}
            onClick={() => onChange(tab.id)}
            className={clsx(
              'inline-flex h-11 shrink-0 cursor-pointer items-center justify-center gap-2 whitespace-nowrap px-4 text-sm font-semibold transition-colors duration-200 [&_svg]:size-4',
              'disabled:cursor-not-allowed disabled:opacity-50',
              stretch && 'max-sm:flex-1 max-sm:px-2',
              variant === 'underline'
                ? clsx('-mb-px border-b-2', selected ? 'border-accent text-text' : 'border-transparent text-text-muted hover:text-text')
                : clsx('rounded-lg', selected ? 'bg-surface-3 text-text shadow-card' : 'text-text-muted hover:text-text'),
            )}
          >
            {tab.icon && (
              <span aria-hidden="true" className="inline-flex">
                {tab.icon}
              </span>
            )}
            {tab.shortLabel ? (
              <>
                <span className="sm:hidden" aria-hidden="true">
                  {tab.shortLabel}
                </span>
                <span className="max-sm:sr-only">{tab.label}</span>
              </>
            ) : (
              tab.label
            )}
            {tab.count != null && <span className="rounded-full bg-surface-3 px-1.5 text-xs tabular-nums text-text-muted">{tab.count}</span>}
          </button>
        );
      })}
    </div>
  );
}

/** Panel de una pestaña. Queda montado con `hidden` para que `aria-controls` apunte siempre a un elemento real. */
export function TabPanel<T extends string = string>({ id, activeId, children, className }: { id: T; activeId: T; children: ReactNode; className?: string }) {
  return (
    <div role="tabpanel" id={`panel-${id}`} aria-labelledby={`tab-${id}`} tabIndex={0} hidden={id !== activeId} className={className}>
      {children}
    </div>
  );
}
