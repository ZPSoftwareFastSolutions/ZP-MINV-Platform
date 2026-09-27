// Reglas puras de filtrado, búsqueda y ordenamiento de productos. Sin React ni datos concretos.

import { normalizeText, tokenize } from '@/shared/text';
import type { Condition, Product, ProductTag } from './types';

export type ProductSort = 'relevancia' | 'precio-asc' | 'precio-desc' | 'nombre';

export const PRODUCT_SORTS: readonly { value: ProductSort; label: string }[] = [
  { value: 'relevancia', label: 'Más relevantes' },
  { value: 'precio-asc', label: 'Menor precio' },
  { value: 'precio-desc', label: 'Mayor precio' },
  { value: 'nombre', label: 'Nombre (A-Z)' },
];

export function isProductSort(value: string | null | undefined): value is ProductSort {
  return PRODUCT_SORTS.some((sort) => sort.value === value);
}

export interface ProductFilter {
  /** Texto de búsqueda libre (se normaliza sin acentos; todas las palabras deben aparecer). */
  q?: string;
  /** Códigos de categoría admitidos (la categoría elegida y su subárbol, ya resueltos). */
  categories?: readonly string[];
  /** Nombres de marca tal como figuran en `Product.brand`. */
  brands?: readonly string[];
  minPrice?: number;
  maxPrice?: number;
  condition?: Condition;
  /** Solo productos con stock. */
  inStock?: boolean;
  /** Todas las etiquetas indicadas deben estar presentes. */
  tags?: readonly ProductTag[];
}

const haystackCache = new WeakMap<Product, string>();

/** Texto normalizado donde se busca: nombre, marca, categoría, SKU y textos de las especificaciones. */
export function productHaystack(product: Product): string {
  let haystack = haystackCache.get(product);
  if (!haystack) {
    haystack = normalizeText(
      [
        product.name,
        product.shortName,
        product.brand,
        product.categoryName,
        product.categoryPath,
        product.sku,
        ...product.highlights,
        ...product.specs.map((spec) => `${spec.label} ${spec.text}`),
      ].join(' '),
    );
    haystackCache.set(product, haystack);
  }
  return haystack;
}

/** Verdadero si todas las palabras de la consulta aparecen en el producto (sin acentos ni mayúsculas). */
export function matchesQuery(product: Product, query: string | undefined): boolean {
  const tokens = tokenize(query ?? '');
  if (tokens.length === 0) return true;
  const haystack = productHaystack(product);
  return tokens.every((token) => haystack.includes(token));
}

/** Puntaje de relevancia: coincidencias en el nombre valen más que en la marca o las especificaciones. */
export function searchScore(product: Product, query: string | undefined): number {
  const tokens = tokenize(query ?? '');
  if (tokens.length === 0) return product.popularity;
  const name = normalizeText(product.name);
  const brand = normalizeText(product.brand);
  const category = normalizeText(product.categoryName);
  let score = 0;
  for (const token of tokens) {
    if (name.startsWith(token)) score += 6;
    else if (name.includes(token)) score += 4;
    if (brand.includes(token)) score += 3;
    if (category.includes(token)) score += 2;
    if (productHaystack(product).includes(token)) score += 1;
  }
  return score * 10 + product.popularity;
}

export function filterProducts(products: readonly Product[], filter: ProductFilter): Product[] {
  const categories = filter.categories && filter.categories.length > 0 ? new Set(filter.categories) : null;
  const brands = filter.brands && filter.brands.length > 0 ? new Set(filter.brands.map(normalizeText)) : null;
  const tags = filter.tags ?? [];
  return products.filter((product) => {
    if (categories && !categories.has(product.category)) return false;
    if (brands && !brands.has(normalizeText(product.brand))) return false;
    if (filter.minPrice != null && product.price < filter.minPrice) return false;
    if (filter.maxPrice != null && product.price > filter.maxPrice) return false;
    if (filter.condition && product.condition !== filter.condition) return false;
    if (filter.inStock && product.stock <= 0) return false;
    if (tags.some((tag) => !product.tags.includes(tag))) return false;
    return matchesQuery(product, filter.q);
  });
}

const collator = new Intl.Collator('es', { sensitivity: 'base', numeric: true });

/** Devuelve una copia ordenada. Con «relevancia» y sin consulta, ordena por popularidad. */
export function sortProducts(products: readonly Product[], sort: ProductSort, query?: string): Product[] {
  const copy = [...products];
  switch (sort) {
    case 'precio-asc':
      return copy.sort((a, b) => a.price - b.price || collator.compare(a.name, b.name));
    case 'precio-desc':
      return copy.sort((a, b) => b.price - a.price || collator.compare(a.name, b.name));
    case 'nombre':
      return copy.sort((a, b) => collator.compare(a.name, b.name));
    default: {
      const scores = new Map(copy.map((product) => [product.sku, searchScore(product, query)]));
      return copy.sort(
        (a, b) =>
          (scores.get(b.sku) as number) - (scores.get(a.sku) as number) ||
          b.popularity - a.popularity ||
          collator.compare(a.name, b.name),
      );
    }
  }
}

export interface PriceRange {
  min: number;
  max: number;
}

export function priceRange(products: readonly Product[]): PriceRange {
  if (products.length === 0) return { min: 0, max: 0 };
  let min = Number.POSITIVE_INFINITY;
  let max = 0;
  for (const product of products) {
    if (product.price < min) min = product.price;
    if (product.price > max) max = product.price;
  }
  return { min, max };
}

/** Cuenta productos por una clave (marca, categoría, condición…). */
export function countBy(products: readonly Product[], key: (product: Product) => string): Map<string, number> {
  const counts = new Map<string, number>();
  for (const product of products) {
    const value = key(product);
    counts.set(value, (counts.get(value) ?? 0) + 1);
  }
  return counts;
}

function byPopularity(a: Product, b: Product): number {
  return b.popularity - a.popularity || collator.compare(a.name, b.name);
}

/** Productos con la etiqueta indicada, del más popular al menos popular. */
export function productsWithTag(products: readonly Product[], tag: ProductTag, limit?: number): Product[] {
  const result = products.filter((product) => product.tags.includes(tag)).sort(byPopularity);
  return limit == null ? result : result.slice(0, limit);
}

/** Productos parecidos: misma categoría primero (por popularidad), luego misma marca. Nunca el mismo producto. */
export function relatedProducts(products: readonly Product[], product: Product, limit: number): Product[] {
  const sameCategory = products
    .filter((candidate) => candidate.sku !== product.sku && candidate.category === product.category)
    .sort(byPopularity);
  if (sameCategory.length >= limit) return sameCategory.slice(0, limit);
  const chosen = new Set(sameCategory.map((candidate) => candidate.sku));
  const sameBrand = products
    .filter((candidate) => candidate.sku !== product.sku && !chosen.has(candidate.sku) && candidate.brand === product.brand)
    .sort(byPopularity);
  return [...sameCategory, ...sameBrand].slice(0, limit);
}
