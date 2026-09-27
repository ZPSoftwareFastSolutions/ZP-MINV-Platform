// Implementación del puerto ICatalogRepository sobre el mock generado (*.data.ts). Sin red ni almacenamiento.

import type { BuildPreset } from '@/1-domain/builder/types';
import type { Brand, Category, Product } from '@/1-domain/catalog/types';
import type { ICatalogRepository } from '@/1-domain/ports/ICatalogRepository';
import { BRANDS } from './data/brands.data';
import { PRODUCTS } from './data/catalog.data';
import { CATEGORIES } from './data/categories.data';
import { PRESETS } from './data/presets.data';

export interface InMemoryCatalogData {
  categories: readonly Category[];
  brands: readonly Brand[];
  products: readonly Product[];
  presets: readonly BuildPreset[];
}

export class InMemoryCatalogRepository implements ICatalogRepository {
  private readonly categories: readonly Category[];
  private readonly brands: readonly Brand[];
  private readonly products: readonly Product[];
  private readonly presets: readonly BuildPreset[];
  private readonly bySku: ReadonlyMap<string, Product>;
  private readonly bySlug: ReadonlyMap<string, Product>;

  constructor(data: Partial<InMemoryCatalogData> = {}) {
    this.categories = data.categories ?? CATEGORIES;
    this.brands = data.brands ?? BRANDS;
    this.products = data.products ?? PRODUCTS;
    this.presets = data.presets ?? PRESETS;
    this.bySku = new Map(this.products.map((product) => [product.sku, product]));
    this.bySlug = new Map(this.products.map((product) => [product.slug, product]));
  }

  getCategories(): readonly Category[] {
    return this.categories;
  }

  getBrands(): readonly Brand[] {
    return this.brands;
  }

  getProducts(): readonly Product[] {
    return this.products;
  }

  getPresets(): readonly BuildPreset[] {
    return this.presets;
  }

  getProductBySku(sku: string): Product | undefined {
    return this.bySku.get(sku);
  }

  getProductBySlug(slug: string): Product | undefined {
    return this.bySlug.get(slug);
  }
}
