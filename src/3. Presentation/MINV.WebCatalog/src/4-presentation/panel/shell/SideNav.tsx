// Menú del panel: el buscador de pantallas y las secciones plegables con los módulos que la sesión puede ver (los demás
// no aparecen; una sección sin módulos visibles tampoco). Se usa en la columna lateral (desde 1024 px) y en el cajón del
// teléfono. Qué secciones están plegadas lo decide el esqueleto (se recuerda mientras la página está abierta; nada se
// guarda en el navegador).
//
// Buscador: filtra por título, módulo, sección y descripción (sin acentos). Enter abre el primer resultado, ↓ pasa a la
// lista, Escape borra.

import clsx from 'clsx';
import { ChevronDown, Search, X } from 'lucide-react';
import { useId, useMemo, useRef, useState, type KeyboardEvent } from 'react';
import { NavLink, useNavigate } from 'react-router-dom';
import { usePermissions } from '../hooks';
import { HOME_MODULE_KEY, menuSections, panelScreens, searchScreens, usePanelRegistry } from '../registry';

const LINK =
  'flex min-h-11 items-center gap-3 rounded-xl px-3 text-sm font-medium text-text-muted transition-colors duration-200 hover:bg-surface-2 hover:text-text [&_svg]:size-5 [&_svg]:shrink-0';
const ACTIVE = 'bg-primary-soft text-text hover:bg-primary-soft [&_svg]:text-primary-text';
const MAX_RESULTS = 12;

export interface SideNavProps {
  /** Claves de las secciones plegadas. */
  collapsed: ReadonlySet<string>;
  onToggleSection: (key: string) => void;
}

export function SideNav({ collapsed, onToggleSection }: SideNavProps) {
  const registry = usePanelRegistry();
  const { permissions } = usePermissions();
  const navigate = useNavigate();
  const searchId = useId();
  const resultsId = useId();
  const [query, setQuery] = useState('');
  const resultsRef = useRef<HTMLUListElement>(null);

  const sections = useMemo(() => menuSections(registry, permissions), [registry, permissions]);
  const screens = useMemo(() => panelScreens(registry, permissions), [registry, permissions]);
  const results = useMemo(() => searchScreens(screens, query).slice(0, MAX_RESULTS), [screens, query]);
  const searching = query.trim().length > 0;

  const go = (to: string) => {
    setQuery('');
    navigate(to);
  };

  const onSearchKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'Enter') {
      event.preventDefault();
      if (results[0]) go(results[0].to);
    } else if (event.key === 'Escape' && query) {
      // Borra la búsqueda sin cerrar el cajón que la contiene.
      event.preventDefault();
      event.stopPropagation();
      setQuery('');
    } else if (event.key === 'ArrowDown' && results.length > 0) {
      event.preventDefault();
      resultsRef.current?.querySelector<HTMLElement>('a')?.focus();
    }
  };

  const onResultsKeyDown = (event: KeyboardEvent<HTMLUListElement>) => {
    const links = Array.from(resultsRef.current?.querySelectorAll<HTMLElement>('a') ?? []);
    const index = links.indexOf(document.activeElement as HTMLElement);
    if (event.key === 'ArrowDown' && index < links.length - 1) {
      event.preventDefault();
      links[index + 1]?.focus();
    } else if (event.key === 'ArrowUp') {
      event.preventDefault();
      if (index <= 0) document.getElementById(searchId)?.focus();
      else links[index - 1]?.focus();
    }
  };

  return (
    <nav aria-label="Menú del panel" className="flex min-w-0 flex-col gap-4">
      <div className="relative">
        <label htmlFor={searchId} className="sr-only">
          Buscar una pantalla
        </label>
        <Search aria-hidden="true" className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-text-muted" />
        <input
          id={searchId}
          type="search"
          role="searchbox"
          autoComplete="off"
          spellCheck={false}
          enterKeyHint="go"
          placeholder="Buscar pantalla…"
          value={query}
          onChange={(event) => setQuery(event.target.value)}
          onKeyDown={onSearchKeyDown}
          aria-controls={searching ? resultsId : undefined}
          className="min-h-11 w-full rounded-xl border border-border-control bg-surface-2 py-2 pr-11 pl-10 text-sm text-text placeholder:text-text-faint [&::-webkit-search-cancel-button]:appearance-none"
        />
        {query && (
          <button
            type="button"
            aria-label="Borrar la búsqueda"
            title="Borrar la búsqueda"
            onClick={() => setQuery('')}
            className="absolute inset-y-0 right-0 flex w-11 cursor-pointer items-center justify-center rounded-xl text-text-muted hover:text-text"
          >
            <X aria-hidden="true" className="size-4" />
          </button>
        )}
      </div>

      {searching ? (
        <div>
          <p role="status" className="px-1 pb-2 text-xs text-text-faint">
            {results.length === 0 ? `Sin resultados para «${query.trim()}».` : results.length === 1 ? '1 pantalla encontrada' : `${results.length} pantallas encontradas`}
          </p>
          {results.length > 0 && (
            <ul id={resultsId} ref={resultsRef} aria-label="Pantallas encontradas" className="space-y-0.5" onKeyDown={onResultsKeyDown}>
              {results.map((screen) => {
                const Icon = screen.module.icon;
                return (
                  <li key={screen.id}>
                    <NavLink
                      to={screen.to}
                      end
                      onClick={() => setQuery('')}
                      className={({ isActive }) => clsx(LINK, 'py-1.5', isActive && ACTIVE)}
                    >
                      <Icon aria-hidden="true" />
                      <span className="min-w-0">
                        <span className="block truncate text-text">{screen.title}</span>
                        <span className="block truncate text-xs text-text-faint">{screen.context}</span>
                      </span>
                    </NavLink>
                  </li>
                );
              })}
            </ul>
          )}
        </div>
      ) : (
        <ul className="space-y-3">
          {sections.map(({ section, modules }) => {
            const open = !collapsed.has(section.key);
            const listId = `${resultsId}-${section.key}`;
            const SectionIcon = section.icon;
            return (
              <li key={section.key}>
                <button
                  type="button"
                  aria-expanded={open}
                  aria-controls={open ? listId : undefined}
                  onClick={() => onToggleSection(section.key)}
                  className="flex min-h-11 w-full cursor-pointer items-center gap-2 rounded-lg px-3 text-left text-xs font-semibold uppercase tracking-wider text-text-faint transition-colors duration-200 hover:bg-surface-2 hover:text-text"
                >
                  <SectionIcon aria-hidden="true" className="size-4 shrink-0" />
                  <span className="min-w-0 flex-1 truncate">{section.title}</span>
                  <ChevronDown aria-hidden="true" className={clsx('size-4 shrink-0 transition-transform duration-200', !open && '-rotate-90')} />
                </button>
                {open && (
                  <ul id={listId} className="mt-0.5 space-y-0.5">
                    {modules.map((module) => {
                      const Icon = module.icon;
                      return (
                        <li key={module.key}>
                          <NavLink to={module.basePath} end={module.key === HOME_MODULE_KEY} className={({ isActive }) => clsx(LINK, isActive && ACTIVE)}>
                            <Icon aria-hidden="true" />
                            <span className="min-w-0 truncate">{module.title}</span>
                          </NavLink>
                        </li>
                      );
                    })}
                  </ul>
                )}
              </li>
            );
          })}
        </ul>
      )}
    </nav>
  );
}
