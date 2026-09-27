// Reglas puras sobre el árbol de categorías (raíz → hijas). Sin React ni datos concretos.

import type { Category, CategoryCode } from './types';

export interface CategoryNode extends Category {
  children: CategoryNode[];
}

/** Raíces con sus hijas, conservando el orden del catálogo. Las hijas huérfanas se ignoran. */
export function buildCategoryTree(categories: readonly Category[]): CategoryNode[] {
  const nodes = new Map<CategoryCode, CategoryNode>();
  for (const category of categories) nodes.set(category.code, { ...category, children: [] });
  const roots: CategoryNode[] = [];
  for (const node of nodes.values()) {
    if (node.parent === null) {
      roots.push(node);
    } else {
      nodes.get(node.parent)?.children.push(node);
    }
  }
  return roots;
}

export function rootCategories(categories: readonly Category[]): Category[] {
  return categories.filter((category) => category.parent === null);
}

export function findCategoryBySlug(categories: readonly Category[], slug: string): Category | undefined {
  return categories.find((category) => category.slug === slug);
}

export function findCategoryByCode(categories: readonly Category[], code: CategoryCode): Category | undefined {
  return categories.find((category) => category.code === code);
}

export function categoryChildren(categories: readonly Category[], code: CategoryCode): Category[] {
  return categories.filter((category) => category.parent === code);
}

/** Ruta de la raíz a la categoría (inclusive): [Componentes, Procesadores]. Vacía si el código no existe. */
export function categoryPath(categories: readonly Category[], code: CategoryCode): Category[] {
  const path: Category[] = [];
  let current = findCategoryByCode(categories, code);
  const visited = new Set<CategoryCode>();
  while (current && !visited.has(current.code)) {
    visited.add(current.code);
    path.unshift(current);
    current = current.parent === null ? undefined : findCategoryByCode(categories, current.parent);
  }
  return path;
}

/** La categoría y todas sus descendientes (códigos): COMP → [COMP, CPU, GPU, …]. */
export function categorySubtreeCodes(categories: readonly Category[], code: CategoryCode): CategoryCode[] {
  const result: CategoryCode[] = [];
  const queue: CategoryCode[] = [code];
  const seen = new Set<CategoryCode>();
  for (let current = queue.shift(); current !== undefined; current = queue.shift()) {
    if (seen.has(current)) continue;
    seen.add(current);
    result.push(current);
    for (const child of categoryChildren(categories, current)) queue.push(child.code);
  }
  return result;
}
