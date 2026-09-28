// Puerto de ORIGEN del catálogo (V6): de dónde sale la instantánea que hidrata el repositorio en memoria y cómo se
// consulta la disponibilidad fresca de una ficha. Lo implementa la infraestructura (HTTP contra /storefront/v1, o el mock
// de la V5 con VITE_API_URL=mock). Falla con StorefrontError.

import type { BuildPreset } from '@/1-domain/builder/types';
import type { Product } from '@/1-domain/catalog/types';
import type { CatalogSnapshot } from '@/1-domain/storefront/types';

export interface ICatalogSource {
  /** Instantánea completa del catálogo (empresa, sucursal, categorías, marcas, productos y armados publicados). */
  load(): Promise<CatalogSnapshot>;
  /** Un producto con `stock`/`reserved` al momento de la consulta; undefined si ya no está publicado. */
  product(slug: string): Promise<Product | undefined>;
  /** Armados sugeridos publicados (el mismo arreglo que trae la instantánea). */
  presets(): Promise<BuildPreset[]>;
}
