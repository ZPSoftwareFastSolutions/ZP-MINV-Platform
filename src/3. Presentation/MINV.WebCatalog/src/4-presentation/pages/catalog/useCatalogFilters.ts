// Enlaza los filtros del catálogo con la URL (useParams + useSearchParams). Cambiar un filtro reemplaza la entrada del
// historial y no mueve el scroll; cambiar de categoría agrega una entrada para que «Atrás» funcione como se espera.

import { useCallback, useMemo } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { catalogHref, parseCatalogFilters, type CatalogFilters } from './catalogFilters';

export interface CatalogFiltersApi {
  filters: CatalogFilters;
  /** Reemplaza todos los filtros (la página vuelve a 1 salvo que se indique). */
  apply: (next: CatalogFilters, options?: { push?: boolean }) => void;
  /** Cambia algunos filtros y vuelve a la primera página. */
  update: (patch: Partial<CatalogFilters>) => void;
  /** Ruta para unos filtros parcialmente distintos (enlaces de paginación y chips). */
  href: (patch: Partial<CatalogFilters>) => string;
}

export function useCatalogFilters(): CatalogFiltersApi {
  const { categoria } = useParams();
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const filters = useMemo(() => parseCatalogFilters(categoria, params), [categoria, params]);

  const apply = useCallback(
    (next: CatalogFilters, options: { push?: boolean } = {}) => {
      void navigate(catalogHref(next), { replace: !options.push, preventScrollReset: true });
    },
    [navigate],
  );

  const update = useCallback(
    (patch: Partial<CatalogFilters>) => {
      const next = { ...filters, ...patch, page: patch.page ?? 1 };
      apply(next, { push: patch.category !== undefined && patch.category !== filters.category });
    },
    [apply, filters],
  );

  const href = useCallback((patch: Partial<CatalogFilters>) => catalogHref({ ...filters, ...patch }), [filters]);

  return { filters, apply, update, href };
}
