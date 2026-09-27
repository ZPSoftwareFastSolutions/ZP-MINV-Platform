// Árbol de categorías con conteos para el panel de filtros del catálogo: todas las raíces con cuántos productos del
// alcance actual (búsqueda, stock y etiquetas) tienen, y las hijas de la raíz activa. Una sola pasada sobre los
// productos (sin volver a ejecutar búsquedas completas por cada nivel).

import { buildCategoryTree, categoryPath, categorySubtreeCodes, findCategoryBySlug } from '@/1-domain/catalog/categories';
import { filterProducts } from '@/1-domain/catalog/products';
import type { Category } from '@/1-domain/catalog/types';
import type { ICatalogRepository } from '@/1-domain/ports/ICatalogRepository';
import type { CategoryFilterNode, CategoryFilterTree, CategoryFilterTreeQuery } from './types';

export function getCategoryFilterTree(repo: ICatalogRepository, query: CategoryFilterTreeQuery = {}): CategoryFilterTree {
  const categories = repo.getCategories();
  const selected = query.category ? findCategoryBySlug(categories, query.category) : undefined;
  const path: Category[] = selected ? categoryPath(categories, selected.code) : [];
  const activeRoot = path[0];

  const inScope = filterProducts(repo.getProducts(), { q: query.q, inStock: query.inStock, tags: query.tags });
  const countByCode = new Map<string, number>();
  for (const product of inScope) countByCode.set(product.category, (countByCode.get(product.category) ?? 0) + 1);
  const subtreeCount = (code: string) => categorySubtreeCodes(categories, code).reduce((acc, item) => acc + (countByCode.get(item) ?? 0), 0);

  const roots = buildCategoryTree(categories)
    .map<CategoryFilterNode>((root) => {
      const isActiveRoot = root.code === activeRoot?.code;
      const children = isActiveRoot
        ? root.children
            .map<CategoryFilterNode>((child) => ({
              code: child.code,
              name: child.name,
              slug: child.slug,
              icon: child.icon,
              count: subtreeCount(child.code),
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
        count: subtreeCount(root.code),
        selected: root.code === selected?.code,
        children,
      };
    })
    .filter((root) => root.count > 0 || root.code === activeRoot?.code);

  return { roots, total: inScope.length, selected, path };
}
