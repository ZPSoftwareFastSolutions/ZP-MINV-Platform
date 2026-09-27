// Puerto del catálogo: lo implementa la infraestructura (mock en memoria) y lo consumen los casos de uso.
// Es síncrono a propósito: los datos son un mock local embebido; no hay red ni backend.

import type { BuildPreset } from '@/1-domain/builder/types';
import type { Brand, Category, Product } from '@/1-domain/catalog/types';

export interface ICatalogRepository {
  getCategories(): readonly Category[];
  getBrands(): readonly Brand[];
  getProducts(): readonly Product[];
  getPresets(): readonly BuildPreset[];
  getProductBySku(sku: string): Product | undefined;
  getProductBySlug(slug: string): Product | undefined;
}
