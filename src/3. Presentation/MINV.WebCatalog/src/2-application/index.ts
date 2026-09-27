// Fachada de la capa de aplicación: `createCatalogUseCases(repo)` enlaza cada caso de uso con el puerto.

import type { BuildSummary } from '@/1-domain/builder/build';
import type { BuildLine, BuildSlot, SlotKey } from '@/1-domain/builder/types';
import type { CategoryNode } from '@/1-domain/catalog/categories';
import type { Brand, Category, Product } from '@/1-domain/catalog/types';
import type { ICatalogRepository } from '@/1-domain/ports/ICatalogRepository';
import * as builder from './builder/queries';
import * as catalog from './catalog/queries';
import type {
  PresetDetail,
  SearchCatalogQuery,
  SearchCatalogResult,
  SlotCandidatesQuery,
  SlotCandidatesResult,
} from './catalog/types';

export type * from './catalog/types';

export interface CatalogUseCases {
  getCategoryTree(): CategoryNode[];
  getCategories(): readonly Category[];
  getRootCategories(): Category[];
  getCategory(slug: string): Category | undefined;
  getCategoryByCode(code: string): Category | undefined;
  getCategoryPath(code: string): Category[];
  getBrands(): readonly Brand[];
  searchCatalog(query?: SearchCatalogQuery): SearchCatalogResult;
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
    getCategoryTree: () => catalog.getCategoryTree(repo),
    getCategories: () => repo.getCategories(),
    getRootCategories: () => catalog.getRootCategories(repo),
    getCategory: (slug) => catalog.getCategory(repo, slug),
    getCategoryByCode: (code) => catalog.getCategoryByCode(repo, code),
    getCategoryPath: (code) => catalog.getCategoryPath(repo, code),
    getBrands: () => catalog.getBrands(repo),
    searchCatalog: (query) => catalog.searchCatalog(repo, query),
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
