// Casos de uso del catálogo: consultas puras sobre el puerto ICatalogRepository. Sin React.

import {
  buildCategoryTree,
  categoryChildren,
  categoryPath,
  categorySubtreeCodes,
  findCategoryBySlug,
  rootCategories,
  type CategoryNode,
} from '@/1-domain/catalog/categories';
import {
  countBy,
  filterProducts,
  priceRange,
  productsWithTag,
  relatedProducts,
  sortProducts,
  type ProductFilter,
} from '@/1-domain/catalog/products';
import type { Brand, Category, Condition, Product } from '@/1-domain/catalog/types';
import type { ICatalogRepository } from '@/1-domain/ports/ICatalogRepository';
import { normalizeText } from '@/shared/text';
import { PAGE_SIZE_DEFAULT } from '@/shared/constants';
import type { BrandFacet, CategoryFacet, ConditionFacet, SearchCatalogQuery, SearchCatalogResult } from './types';

const PAGE_SIZE_MAX = 96;
const CONDITION_ORDER: readonly Condition[] = ['Nuevo', 'Reacondicionado', 'Usado'];

export function getCategoryTree(repo: ICatalogRepository): CategoryNode[] {
  return buildCategoryTree(repo.getCategories());
}

export function getRootCategories(repo: ICatalogRepository): Category[] {
  return rootCategories(repo.getCategories());
}

export function getCategory(repo: ICatalogRepository, slug: string): Category | undefined {
  return findCategoryBySlug(repo.getCategories(), slug);
}

export function getCategoryByCode(repo: ICatalogRepository, code: string): Category | undefined {
  return repo.getCategories().find((category) => category.code === code);
}

/** Ruta raíz → categoría, para migas de pan. */
export function getCategoryPath(repo: ICatalogRepository, code: string): Category[] {
  return categoryPath(repo.getCategories(), code);
}

export function getBrands(repo: ICatalogRepository): readonly Brand[] {
  return repo.getBrands();
}

/** Convierte códigos (o nombres) de marca a los nombres que llevan los productos. */
export function resolveBrandNames(repo: ICatalogRepository, brands: readonly string[] | undefined): string[] {
  if (!brands || brands.length === 0) return [];
  const byCode = new Map(repo.getBrands().map((brand) => [normalizeText(brand.code), brand.name]));
  const byName = new Map(repo.getBrands().map((brand) => [normalizeText(brand.name), brand.name]));
  const names = new Set<string>();
  for (const value of brands) {
    const key = normalizeText(value);
    const name = byCode.get(key) ?? byName.get(key);
    if (name) names.add(name);
  }
  return [...names];
}

export function brandFacets(repo: ICatalogRepository, products: readonly Product[]): BrandFacet[] {
  const counts = countBy(products, (product) => product.brand);
  return repo
    .getBrands()
    .filter((brand) => counts.has(brand.name))
    .map((brand) => ({ code: brand.code, name: brand.name, count: counts.get(brand.name) as number }))
    .sort((a, b) => a.name.localeCompare(b.name, 'es'));
}

function conditionFacets(products: readonly Product[]): ConditionFacet[] {
  const counts = countBy(products, (product) => product.condition);
  return CONDITION_ORDER.filter((value) => counts.has(value)).map((value) => ({ value, count: counts.get(value) as number }));
}

function categoryFacets(repo: ICatalogRepository, parent: Category | undefined, products: readonly Product[]): CategoryFacet[] {
  const categories = repo.getCategories();
  const candidates = parent ? categoryChildren(categories, parent.code) : rootCategories(categories);
  return candidates
    .map((category) => {
      const codes = new Set(categorySubtreeCodes(categories, category.code));
      const count = products.filter((product) => codes.has(product.category)).length;
      return { code: category.code, name: category.name, slug: category.slug, icon: category.icon, count };
    })
    .filter((facet) => facet.count > 0);
}

/**
 * Búsqueda con filtros, facetas y paginación sobre el mock. Las facetas se calculan «sin su propio filtro»
 * (las marcas con todo salvo el filtro de marca, etc.) para que el usuario pueda combinar opciones.
 */
export function searchCatalog(repo: ICatalogRepository, query: SearchCatalogQuery = {}): SearchCatalogResult {
  const categories = repo.getCategories();
  const category = query.category ? findCategoryBySlug(categories, query.category) : undefined;
  const scopeCodes = category ? categorySubtreeCodes(categories, category.code) : undefined;
  const brandNames = resolveBrandNames(repo, query.brands);
  const sort = query.sort ?? 'relevancia';
  const pageSize = Math.min(PAGE_SIZE_MAX, Math.max(1, Math.trunc(query.pageSize ?? PAGE_SIZE_DEFAULT)));

  const scope: ProductFilter = { q: query.q, categories: scopeCodes, inStock: query.inStock, tags: query.tags };
  const price: ProductFilter = { minPrice: query.minPrice, maxPrice: query.maxPrice };
  const brands: ProductFilter = { brands: brandNames };
  const condition: ProductFilter = { condition: query.condition };

  const all = repo.getProducts();
  const inScope = filterProducts(all, scope);
  const matching = filterProducts(inScope, { ...price, ...brands, ...condition });
  const sorted = sortProducts(matching, sort, query.q);

  const pageCount = Math.max(1, Math.ceil(sorted.length / pageSize));
  const page = Math.min(pageCount, Math.max(1, Math.trunc(query.page ?? 1)));
  const start = (page - 1) * pageSize;

  return {
    query: { ...query, sort, page, pageSize },
    items: sorted.slice(start, start + pageSize),
    total: sorted.length,
    page,
    pageSize,
    pageCount,
    category,
    breadcrumbs: category ? categoryPath(categories, category.code) : [],
    facets: {
      brands: brandFacets(repo, filterProducts(inScope, { ...price, ...condition })),
      categories: categoryFacets(repo, category, inScope),
      priceRange: priceRange(filterProducts(inScope, { ...brands, ...condition })),
      conditions: conditionFacets(filterProducts(inScope, { ...price, ...brands })),
    },
  };
}

export function getFeaturedProducts(repo: ICatalogRepository, limit = 8): Product[] {
  return productsWithTag(repo.getProducts(), 'destacado', limit);
}

export function getOffers(repo: ICatalogRepository, limit = 8): Product[] {
  return productsWithTag(repo.getProducts(), 'oferta', limit);
}

export function getNewArrivals(repo: ICatalogRepository, limit = 8): Product[] {
  return productsWithTag(repo.getProducts(), 'nuevo', limit);
}

export function getProduct(repo: ICatalogRepository, slug: string): Product | undefined {
  return repo.getProductBySlug(slug);
}

export function getProductBySku(repo: ICatalogRepository, sku: string): Product | undefined {
  return repo.getProductBySku(sku);
}

export function getRelatedProducts(repo: ICatalogRepository, slug: string, limit = 4): Product[] {
  const product = repo.getProductBySlug(slug);
  if (!product) return [];
  return relatedProducts(repo.getProducts(), product, limit);
}
