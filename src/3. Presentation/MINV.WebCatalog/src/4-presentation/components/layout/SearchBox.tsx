import clsx from 'clsx';
import { Search, X } from 'lucide-react';
import { useId, useState, type FormEvent } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { ROUTES, SEARCH_PARAM } from '@/4-presentation/app/routes';

export interface SearchBoxProps {
  /** Se llama después de enviar (para cerrar el campo en móvil). */
  onSubmitted?: () => void;
  autoFocus?: boolean;
  size?: 'md' | 'lg';
  className?: string;
}

/**
 * Buscador visual: campo controlado que navega a /catalogo?q=… al enviar (el filtrado lo hace la página del
 * catálogo sobre el mock). Se sincroniza con el parámetro `q` de la URL cuando cambia.
 */
export function SearchBox({ onSubmitted, autoFocus = false, size = 'md', className }: SearchBoxProps) {
  const inputId = useId();
  const navigate = useNavigate();
  const location = useLocation();
  const urlQuery = location.pathname.startsWith(ROUTES.catalog) ? (new URLSearchParams(location.search).get(SEARCH_PARAM) ?? '') : '';
  const [value, setValue] = useState(urlQuery);
  const [syncedQuery, setSyncedQuery] = useState(urlQuery);
  if (syncedQuery !== urlQuery) {
    setSyncedQuery(urlQuery);
    setValue(urlQuery);
  }

  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    navigate(ROUTES.search(value));
    onSubmitted?.();
  };

  return (
    <form role="search" onSubmit={submit} className={clsx('relative flex w-full items-center', className)}>
      <label htmlFor={inputId} className="sr-only">
        Buscar productos
      </label>
      <Search aria-hidden="true" className="pointer-events-none absolute left-3.5 size-5 text-text-faint" />
      <input
        id={inputId}
        type="search"
        name={SEARCH_PARAM}
        value={value}
        onChange={(event) => setValue(event.target.value)}
        autoFocus={autoFocus}
        autoComplete="off"
        enterKeyHint="search"
        placeholder="Buscá procesadores, tarjetas de video, consolas…"
        className={clsx(
          'w-full rounded-xl border border-border bg-surface-2 pl-11 text-base text-text placeholder:text-text-faint',
          'transition-colors duration-200 hover:border-border-strong focus:border-accent focus:outline-none',
          '[&::-webkit-search-cancel-button]:hidden',
          size === 'lg' ? 'h-12' : 'h-11',
          value ? 'pr-24' : 'pr-14',
        )}
      />
      <div className="absolute right-1.5 flex items-center gap-0.5">
        {value && (
          <button
            type="button"
            aria-label="Borrar búsqueda"
            onClick={() => setValue('')}
            className="flex size-9 cursor-pointer items-center justify-center rounded-lg text-text-muted transition-colors duration-200 hover:bg-surface-3 hover:text-text"
          >
            <X aria-hidden="true" className="size-4" />
          </button>
        )}
        <button
          type="submit"
          aria-label="Buscar"
          className="flex h-9 cursor-pointer items-center justify-center rounded-lg bg-primary px-3 text-sm font-semibold text-white transition-colors duration-200 hover:bg-primary-hover"
        >
          <Search aria-hidden="true" className="size-4 sm:hidden" />
          <span className="hidden sm:inline">Buscar</span>
        </button>
      </div>
    </form>
  );
}
