// Contratos de entrada y salida de los casos de uso del catálogo (sin React).

import type { BuildLine, BuildPreset, BuildSlot } from '@/1-domain/builder/types';
import type { BuildSummary } from '@/1-domain/builder/build';
import type { Category, Condition, Product, ProductTag } from '@/1-domain/catalog/types';
import type { PriceRange, ProductSort } from '@/1-domain/catalog/products';

export interface SearchCatalogQuery {
  /** Texto libre. */
  q?: string;
  /** Slug de la categoría (raíz o hija); incluye su subárbol. */
  category?: string;
  /** Códigos de marca (`Brand.code`); también se aceptan nombres. */
  brands?: readonly string[];
  minPrice?: number;
  maxPrice?: number;
  condition?: Condition;
  inStock?: boolean;
  tags?: readonly ProductTag[];
  sort?: ProductSort;
  /** Página 1-based. */
  page?: number;
  pageSize?: number;
}

export interface BrandFacet {
  code: string;
  name: string;
  count: number;
}

export interface CategoryFacet {
  code: string;
  name: string;
  slug: string;
  icon: string;
  count: number;
}

export interface ConditionFacet {
  value: Condition;
  count: number;
}

export interface CatalogFacets {
  /** Marcas presentes en el resultado (con todos los filtros salvo el de marca), ordenadas por nombre. */
  brands: BrandFacet[];
  /** Hijas de la categoría elegida (o raíces si no hay categoría) con cuántos productos del resultado tienen. */
  categories: CategoryFacet[];
  /** Precio mínimo y máximo del resultado sin aplicar el filtro de precio. */
  priceRange: PriceRange;
  conditions: ConditionFacet[];
}

export interface SearchCatalogResult {
  /** Parámetros efectivamente aplicados (página ajustada, orden por defecto, etc.). */
  query: Required<Pick<SearchCatalogQuery, 'sort' | 'page' | 'pageSize'>> & SearchCatalogQuery;
  /** Productos de la página actual. */
  items: Product[];
  /** Total de productos que cumplen los filtros (todas las páginas). */
  total: number;
  page: number;
  pageSize: number;
  pageCount: number;
  /** Categoría elegida (si el slug existe). */
  category?: Category;
  /** Ruta raíz → categoría elegida (vacía sin categoría). */
  breadcrumbs: Category[];
  facets: CatalogFacets;
}

export interface SlotCandidatesQuery {
  q?: string;
  brands?: readonly string[];
  inStock?: boolean;
  sort?: ProductSort;
}

export interface SlotCandidatesResult {
  slot: BuildSlot;
  items: Product[];
  total: number;
  facets: { brands: BrandFacet[]; priceRange: PriceRange };
}

export interface PresetDetail {
  preset: BuildPreset;
  /** Líneas con el producto resuelto desde el catálogo. */
  lines: BuildLine[];
  summary: BuildSummary;
  /** SKUs del armado que ya no existen en el catálogo (se omiten). */
  missingSkus: string[];
}
