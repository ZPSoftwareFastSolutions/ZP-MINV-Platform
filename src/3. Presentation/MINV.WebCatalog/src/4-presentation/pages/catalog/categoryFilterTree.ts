// Árbol de categorías con conteos para el panel de filtros: todas las raíces con cuántos productos del alcance actual
// (búsqueda, stock y etiquetas) tienen, y las hijas de la raíz activa. Se apoya en las facetas de `searchCatalog`.

import type { Category } from '@/1-domain/catalog/types';
import type { CatalogUseCases } from '@/2-application';
import { catalogQuery, type CatalogFilters } from './catalogFilters';

export interface CategoryFilterNode {
  code: string;
  name: string;
  slug: string;
  icon: string;
  count: number;
  selected: boolean;
  /** Solo la raíz activa trae sus hijas. */
  children: CategoryFilterNode[];
}

export interface CategoryFilterTree {
  roots: CategoryFilterNode[];
  /** Productos del alcance sin filtrar por categoría (para «Todas las categorías»). */
  total: number;
  selected?: Category;
  /** Raíz → categoría elegida. */
  path: Category[];
}

export function buildCategoryFilterTree(catalog: CatalogUseCases, filters: CatalogFilters): CategoryFilterTree {
  const base = { ...catalogQuery(filters, 1), page: 1, category: undefined };
  const selected = filters.category ? catalog.getCategory(filters.category) : undefined;
  const path = selected ? catalog.getCategoryPath(selected.code) : [];
  const activeRoot = path[0];

  const rootResult = catalog.searchCatalog(base);
  const rootCounts = new Map(rootResult.facets.categories.map((facet) => [facet.code, facet.count]));
  const childCounts = activeRoot
    ? new Map(catalog.searchCatalog({ ...base, category: activeRoot.slug }).facets.categories.map((facet) => [facet.code, facet.count]))
    : new Map<string, number>();

  const roots = catalog
    .getCategoryTree()
    .map<CategoryFilterNode>((root) => {
      const isActiveRoot = root.code === activeRoot?.code;
      const children = isActiveRoot
        ? root.children
            .map<CategoryFilterNode>((child) => ({
              code: child.code,
              name: child.name,
              slug: child.slug,
              icon: child.icon,
              count: childCounts.get(child.code) ?? 0,
              selected: child.code === selected?.code,
              children: [],
            }))
            .filter((child) => child.count > 0 || child.selected)
        : [];
      return {
        code: root.code,
        name: root.name,
        slug: root.slug,
        icon: root.icon,
        count: rootCounts.get(root.code) ?? 0,
        selected: root.code === selected?.code,
        children,
      };
    })
    .filter((root) => root.count > 0 || root.code === activeRoot?.code);

  return { roots, total: rootResult.total, selected, path };
}
