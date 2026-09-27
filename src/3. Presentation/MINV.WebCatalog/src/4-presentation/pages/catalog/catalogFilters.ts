// Estado de los filtros del catálogo. Vive en la URL para que cualquier combinación se pueda compartir:
//   /catalogo/:categoria?q=&marca=&marca=&min=&max=&condicion=&stock=1&tags=&orden=&pagina=&vista=
// Funciones puras (sin React): leer la URL, escribirla y traducirla a la consulta de la capa de aplicación.

import { isProductSort, type ProductSort } from '@/1-domain/catalog/products';
import type { Condition, ProductTag } from '@/1-domain/catalog/types';
import type { SearchCatalogQuery } from '@/2-application';
import { ROUTES } from '@/4-presentation/app/routes';
import { normalizeText } from '@/shared/text';

/** Productos por página del catálogo. */
export const CATALOG_PAGE_SIZE = 12;

export type CatalogView = 'grilla' | 'lista';

export interface CatalogFilters {
  /** Slug de la categoría de la ruta (raíz o hija). */
  category?: string;
  /** Búsqueda libre (dentro de la categoría si hay una). */
  q?: string;
  /** Códigos de marca (`Brand.code`). */
  brands: string[];
  minPrice?: number;
  maxPrice?: number;
  condition?: Condition;
  /** Solo productos con stock. */
  inStock: boolean;
  tags: ProductTag[];
  sort: ProductSort;
  /** Página 1-based. */
  page: number;
  view: CatalogView;
}

/** Nombres de los parámetros de la URL (en español, como el resto del sitio). */
export const CATALOG_PARAMS = {
  q: 'q',
  brand: 'marca',
  min: 'min',
  max: 'max',
  condition: 'condicion',
  stock: 'stock',
  tags: 'tags',
  sort: 'orden',
  page: 'pagina',
  view: 'vista',
} as const;

export const EMPTY_FILTERS: Readonly<CatalogFilters> = {
  brands: [],
  inStock: false,
  tags: [],
  sort: 'relevancia',
  page: 1,
  view: 'grilla',
};

const CONDITIONS: readonly Condition[] = ['Nuevo', 'Reacondicionado', 'Usado'];
const TAGS: readonly ProductTag[] = ['destacado', 'oferta', 'nuevo'];

function uniqueBy<T>(values: readonly T[], key: (value: T) => string): T[] {
  const seen = new Set<string>();
  return values.filter((value) => {
    const k = key(value);
    if (seen.has(k)) return false;
    seen.add(k);
    return true;
  });
}

/** Lista de valores de un parámetro repetido («marca=ASUS&marca=MSI») o separado por comas («marca=ASUS,MSI»). */
function parseList(values: readonly string[]): string[] {
  return uniqueBy(
    values.flatMap((value) => value.split(',')).map((value) => value.trim()).filter((value) => value.length > 0),
    normalizeText,
  );
}

/** Importe de la URL («1500», «1500,50», «1.500»); undefined si no es un número válido. */
function parseMoney(value: string | null): number | undefined {
  if (value == null) return undefined;
  const cleaned = value.trim().replace(/\.(?=\d{3}(\D|$))/g, '').replace(',', '.');
  if (cleaned === '') return undefined;
  const parsed = Number(cleaned);
  return Number.isFinite(parsed) && parsed >= 0 ? Math.round(parsed * 100) / 100 : undefined;
}

function parseCondition(value: string | null): Condition | undefined {
  if (!value) return undefined;
  const key = normalizeText(value);
  return CONDITIONS.find((condition) => normalizeText(condition) === key);
}

function parseTags(values: readonly string[]): ProductTag[] {
  const wanted = new Set(parseList(values).map(normalizeText));
  return TAGS.filter((tag) => wanted.has(tag));
}

function parsePage(value: string | null): number {
  const parsed = Number.parseInt(value ?? '', 10);
  return Number.isInteger(parsed) && parsed >= 1 ? parsed : 1;
}

/** Lee los filtros desde la ruta (`:categoria`) y la cadena de consulta. Los valores inválidos vuelven al valor por defecto. */
export function parseCatalogFilters(category: string | undefined, params: URLSearchParams): CatalogFilters {
  const q = params.get(CATALOG_PARAMS.q)?.trim() || undefined;
  let minPrice = parseMoney(params.get(CATALOG_PARAMS.min));
  let maxPrice = parseMoney(params.get(CATALOG_PARAMS.max));
  if (minPrice != null && maxPrice != null && minPrice > maxPrice) [minPrice, maxPrice] = [maxPrice, minPrice];
  const sort = params.get(CATALOG_PARAMS.sort);
  return {
    category: category?.trim() || undefined,
    q,
    brands: parseList(params.getAll(CATALOG_PARAMS.brand)).map((code) => code.toUpperCase()),
    minPrice,
    maxPrice,
    condition: parseCondition(params.get(CATALOG_PARAMS.condition)),
    inStock: params.get(CATALOG_PARAMS.stock) === '1',
    tags: parseTags(params.getAll(CATALOG_PARAMS.tags)),
    sort: isProductSort(sort) ? sort : EMPTY_FILTERS.sort,
    page: parsePage(params.get(CATALOG_PARAMS.page)),
    view: params.get(CATALOG_PARAMS.view) === 'lista' ? 'lista' : 'grilla',
  };
}

/** Cadena de consulta con solo los valores distintos del defecto, en un orden fijo (URLs estables y compartibles). */
export function catalogFiltersToParams(filters: CatalogFilters): URLSearchParams {
  const params = new URLSearchParams();
  if (filters.q) params.set(CATALOG_PARAMS.q, filters.q);
  for (const brand of filters.brands) params.append(CATALOG_PARAMS.brand, brand);
  if (filters.minPrice != null) params.set(CATALOG_PARAMS.min, String(filters.minPrice));
  if (filters.maxPrice != null) params.set(CATALOG_PARAMS.max, String(filters.maxPrice));
  if (filters.condition) params.set(CATALOG_PARAMS.condition, filters.condition.toLowerCase());
  if (filters.inStock) params.set(CATALOG_PARAMS.stock, '1');
  for (const tag of filters.tags) params.append(CATALOG_PARAMS.tags, tag);
  if (filters.sort !== EMPTY_FILTERS.sort) params.set(CATALOG_PARAMS.sort, filters.sort);
  if (filters.page > 1) params.set(CATALOG_PARAMS.page, String(filters.page));
  if (filters.view !== EMPTY_FILTERS.view) params.set(CATALOG_PARAMS.view, filters.view);
  return params;
}

/** Ruta completa del catálogo para unos filtros: «/catalogo/tarjetas-de-video?marca=ASUS&orden=precio-asc». */
export function catalogHref(filters: CatalogFilters): string {
  const path = filters.category ? ROUTES.category(filters.category) : ROUTES.catalog;
  const query = catalogFiltersToParams(filters).toString();
  return query ? `${path}?${query}` : path;
}

/** Consulta para `searchCatalog` a partir de los filtros de la URL. */
export function catalogQuery(filters: CatalogFilters, pageSize = CATALOG_PAGE_SIZE): SearchCatalogQuery {
  return {
    q: filters.q,
    category: filters.category,
    brands: filters.brands.length > 0 ? filters.brands : undefined,
    minPrice: filters.minPrice,
    maxPrice: filters.maxPrice,
    condition: filters.condition,
    inStock: filters.inStock || undefined,
    tags: filters.tags.length > 0 ? filters.tags : undefined,
    sort: filters.sort,
    page: filters.page,
    pageSize,
  };
}

/** Cantidad de filtros activos para la insignia del botón «Filtros» (la categoría y la búsqueda no cuentan: son el contexto). */
export function activeFilterCount(filters: CatalogFilters): number {
  return (
    filters.brands.length +
    (filters.minPrice != null || filters.maxPrice != null ? 1 : 0) +
    (filters.condition ? 1 : 0) +
    (filters.inStock ? 1 : 0) +
    filters.tags.length
  );
}

/** Verdadero si hay algo que limpiar (filtros o búsqueda). */
export function hasActiveFilters(filters: CatalogFilters): boolean {
  return activeFilterCount(filters) > 0 || Boolean(filters.q);
}

/** Quita filtros y búsqueda conservando la categoría, el orden y la vista. */
export function clearedFilters(filters: CatalogFilters): CatalogFilters {
  return { ...EMPTY_FILTERS, category: filters.category, sort: filters.sort, view: filters.view };
}

export function toggleBrand(filters: CatalogFilters, code: string): CatalogFilters {
  const has = filters.brands.includes(code);
  return { ...filters, brands: has ? filters.brands.filter((brand) => brand !== code) : [...filters.brands, code], page: 1 };
}

export function toggleTag(filters: CatalogFilters, tag: ProductTag): CatalogFilters {
  const has = filters.tags.includes(tag);
  return { ...filters, tags: has ? filters.tags.filter((value) => value !== tag) : TAGS.filter((value) => value === tag || filters.tags.includes(value)), page: 1 };
}
