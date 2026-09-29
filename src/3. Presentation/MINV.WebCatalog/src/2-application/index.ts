// Fachada de la capa de aplicación: `createCatalogUseCases(repo)` enlaza cada caso de uso con el puerto.

import type { BuildSummary } from '@/1-domain/builder/build';
import type { BuildLine, BuildSlot, SlotKey } from '@/1-domain/builder/types';
import type { CategoryNode } from '@/1-domain/catalog/categories';
import type { Brand, Category, Product } from '@/1-domain/catalog/types';
import type { ICatalogRepository } from '@/1-domain/ports/ICatalogRepository';
import type { StoreInfo } from '@/1-domain/storefront/types';
import * as builder from './builder/queries';
import { getCategoryFilterTree } from './catalog/filterTree';
import * as catalog from './catalog/queries';
import type {
  CatalogStats,
  CategoryFilterTree,
  CategoryFilterTreeQuery,
  PresetDetail,
  SearchCatalogQuery,
  SearchCatalogResult,
  SlotCandidatesQuery,
  SlotCandidatesResult,
} from './catalog/types';

export type * from './catalog/types';
export type { ReservationUseCases, ReserveBuildInput } from './storefront/reservations';
export { createReservationUseCases, newIdempotencyKey } from './storefront/reservations';
// V7 · sesión web y cuenta del cliente.
export * from './auth';

export interface CatalogUseCases {
  /** Empresa y sucursal de la tienda (la disponibilidad y el retiro son de esa sucursal). */
  getStore(): StoreInfo;
  getCategoryTree(): CategoryNode[];
  getCategories(): readonly Category[];
  getRootCategories(): Category[];
  getCategory(slug: string): Category | undefined;
  getCategoryPath(code: string): Category[];
  /** Categorías raíz con más productos primero. */
  getPopularCategories(limit?: number): Category[];
  getBrands(): readonly Brand[];
  getBrandByName(name: string): Brand | undefined;
  /** Marcas con más productos primero; `exclude` deja fuera una marca (la de la propia tienda). */
  getTopBrands(limit?: number, exclude?: string): Brand[];
  getCatalogStats(): CatalogStats;
  searchCatalog(query?: SearchCatalogQuery): SearchCatalogResult;
  /** Árbol de categorías con conteos para el panel de filtros (una pasada sobre los productos). */
  getCategoryFilterTree(query?: CategoryFilterTreeQuery): CategoryFilterTree;
  /** Consolas, videojuegos y accesorios de consola por popularidad. */
  getConsoleProducts(limit?: number): Product[];
  getFeaturedProducts(limit?: number): Product[];
  getOffers(limit?: number): Product[];
  getNewArrivals(limit?: number): Product[];
  getProduct(slug: string): Product | undefined;
  getProductBySku(sku: string): Product | undefined;
  getRelatedProducts(slug: string, limit?: number): Product[];
  getBuildSlots(): readonly BuildSlot[];
  getSlotCandidates(slot: SlotKey, query?: SlotCandidatesQuery): SlotCandidatesResult;
  getPresets(): PresetDetail[];
  getPreset(id: string): PresetDetail | undefined;
  buildSummary(lines: readonly BuildLine[]): BuildSummary;
}

export function createCatalogUseCases(repo: ICatalogRepository): CatalogUseCases {
  return {
    getStore: () => repo.getStore(),
    getCategoryTree: () => catalog.getCategoryTree(repo),
    getCategories: () => repo.getCategories(),
    getRootCategories: () => catalog.getRootCategories(repo),
    getCategory: (slug) => catalog.getCategory(repo, slug),
    getCategoryPath: (code) => catalog.getCategoryPath(repo, code),
    getPopularCategories: (limit) => catalog.getPopularCategories(repo, limit),
    getBrands: () => catalog.getBrands(repo),
    getBrandByName: (name) => catalog.getBrandByName(repo, name),
    getTopBrands: (limit, exclude) => catalog.getTopBrands(repo, limit, exclude),
    getCatalogStats: () => catalog.getCatalogStats(repo),
    searchCatalog: (query) => catalog.searchCatalog(repo, query),
    getCategoryFilterTree: (query) => getCategoryFilterTree(repo, query),
    getConsoleProducts: (limit) => catalog.getConsoleProducts(repo, limit),
    getFeaturedProducts: (limit) => catalog.getFeaturedProducts(repo, limit),
    getOffers: (limit) => catalog.getOffers(repo, limit),
    getNewArrivals: (limit) => catalog.getNewArrivals(repo, limit),
    getProduct: (slug) => catalog.getProduct(repo, slug),
    getProductBySku: (sku) => catalog.getProductBySku(repo, sku),
    getRelatedProducts: (slug, limit) => catalog.getRelatedProducts(repo, slug, limit),
    getBuildSlots: () => builder.getBuildSlots(),
    getSlotCandidates: (slot, query) => builder.getSlotCandidates(repo, slot, query),
    getPresets: () => builder.getPresets(repo),
    getPreset: (id) => builder.getPreset(repo, id),
    buildSummary: (lines) => builder.buildSummary(lines),
  };
}
