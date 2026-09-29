// Puerto del catálogo: lo implementa la infraestructura (repositorio en memoria hidratado con la instantánea de la
// tienda, o con el mock de la V5) y lo consumen los casos de uso. Es síncrono a propósito: la red queda en
// ICatalogSource; una vez cargada la instantánea, las páginas calculan sin estados de carga.

import type { BuildPreset } from '@/1-domain/builder/types';
import type { Brand, Category, Product } from '@/1-domain/catalog/types';
import type { ReservationPolicy, StoreInfo } from '@/1-domain/storefront/types';

export interface ICatalogRepository {
  /** Empresa y sucursal de la tienda (de donde sale la disponibilidad y donde se retiran las reservas). */
  getStore(): StoreInfo;
  /** V7: plazos de una reserva publicados por el servidor (48 h y hasta 3 días si no los publica). */
  getReservationPolicy(): ReservationPolicy;
  getCategories(): readonly Category[];
  getBrands(): readonly Brand[];
  getProducts(): readonly Product[];
  getPresets(): readonly BuildPreset[];
  getProductBySku(sku: string): Product | undefined;
  getProductBySlug(slug: string): Product | undefined;
}
