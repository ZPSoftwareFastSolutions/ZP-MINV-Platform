// Implementación del puerto ICatalogRepository en memoria. Se hidrata con una instantánea (la de la API de tienda, o la
// del mock de la V5): índices por SKU y por slug para las consultas síncronas de los casos de uso. Sin red ni
// almacenamiento.

import type { BuildPreset } from '@/1-domain/builder/types';
import type { Brand, Category, Product } from '@/1-domain/catalog/types';
import type { ICatalogRepository } from '@/1-domain/ports/ICatalogRepository';
import { DEFAULT_RESERVATION_POLICY, type CatalogSnapshot, type ReservationPolicy, type StoreInfo } from '@/1-domain/storefront/types';

export interface InMemoryCatalogData {
  store: StoreInfo;
  categories: readonly Category[];
  brands: readonly Brand[];
  products: readonly Product[];
  presets: readonly BuildPreset[];
  /** V7: plazos de la reserva (sin valor, 48 h y hasta 3 días). */
  reservationPolicy?: ReservationPolicy;
}

export class InMemoryCatalogRepository implements ICatalogRepository {
  private readonly store: StoreInfo;
  private readonly reservationPolicy: ReservationPolicy;
  private readonly categories: readonly Category[];
  private readonly brands: readonly Brand[];
  private readonly products: readonly Product[];
  private readonly presets: readonly BuildPreset[];
  private readonly bySku: ReadonlyMap<string, Product>;
  private readonly bySlug: ReadonlyMap<string, Product>;

  constructor(data: InMemoryCatalogData) {
    this.store = data.store;
    this.reservationPolicy = data.reservationPolicy ?? DEFAULT_RESERVATION_POLICY;
    this.categories = data.categories;
    this.brands = data.brands;
    this.products = data.products;
    this.presets = data.presets;
    this.bySku = new Map(this.products.map((product) => [product.sku, product]));
    this.bySlug = new Map(this.products.map((product) => [product.slug, product]));
  }

  /** Repositorio hidratado con la instantánea de la tienda (ICatalogSource.load()). */
  static fromSnapshot(snapshot: CatalogSnapshot): InMemoryCatalogRepository {
    return new InMemoryCatalogRepository({
      store: snapshot.store,
      categories: snapshot.categories,
      brands: snapshot.brands,
      products: snapshot.products,
      presets: snapshot.presets,
      reservationPolicy: snapshot.reservationPolicy,
    });
  }

  getStore(): StoreInfo {
    return this.store;
  }

  getReservationPolicy(): ReservationPolicy {
    return this.reservationPolicy;
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
