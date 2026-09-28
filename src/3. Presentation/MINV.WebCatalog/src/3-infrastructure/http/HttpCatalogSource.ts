// ICatalogSource sobre la API pública de tienda: instantánea (`/catalog`), ficha fresca (`/products/{slug}`) y
// armados publicados (`/presets`). Las imágenes quedan absolutas hacia la API.

import type { BuildPreset } from '@/1-domain/builder/types';
import type { Product } from '@/1-domain/catalog/types';
import type { ICatalogSource } from '@/1-domain/ports/ICatalogSource';
import { isStorefrontError } from '@/1-domain/storefront/errors';
import type { CatalogSnapshot } from '@/1-domain/storefront/types';
import {
  toCatalogSnapshot,
  toPreset,
  toProduct,
  type StorefrontCatalogDto,
  type StorefrontPresetDto,
  type StorefrontProductDto,
} from '@/2-application/storefront';
import type { StorefrontApi } from './api';

export class HttpCatalogSource implements ICatalogSource {
  private readonly api: StorefrontApi;

  constructor(api: StorefrontApi) {
    this.api = api;
  }

  async load(): Promise<CatalogSnapshot> {
    const { body } = await this.api.get<StorefrontCatalogDto>('/catalog');
    return toCatalogSnapshot(body, this.api.baseUrl);
  }

  async product(slug: string): Promise<Product | undefined> {
    try {
      const { body } = await this.api.get<StorefrontProductDto>(`/products/${encodeURIComponent(slug)}`);
      return toProduct(body, this.api.baseUrl);
    } catch (error) {
      if (isStorefrontError(error) && error.kind === 'not_found') return undefined;
      throw error;
    }
  }

  async presets(): Promise<BuildPreset[]> {
    const { body } = await this.api.get<StorefrontPresetDto[]>('/presets');
    return body.map(toPreset);
  }
}
