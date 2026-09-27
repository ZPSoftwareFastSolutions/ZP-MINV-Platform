// Selectores y constantes puras de la portada (sin React): qué productos protagonizan el hero, cómo se agrupan las
// consolas por plataforma, cómo se describe cada nivel de armado y validaciones locales del boletín.

import type { BuildLine, PresetTier, SlotKey } from '@/1-domain/builder/types';
import { savingPercent } from '@/1-domain/catalog/money';
import type { Product } from '@/1-domain/catalog/types';
import type { CatalogUseCases, PresetDetail } from '@/2-application';
import type { BadgeTone } from '@/4-presentation/components/ui/Badge';

/** Categorías (slug) de las que sale, en orden, cada pieza protagonista del hero. */
const HERO_CATEGORY_SLUGS = ['tarjetas-de-video', 'playstation', 'procesadores'] as const;

/** Tres productos disponibles y populares para la composición del hero (con relleno desde los destacados). */
export function pickHeroProducts(catalog: CatalogUseCases): Product[] {
  const picked: Product[] = [];
  const seen = new Set<string>();
  for (const slug of HERO_CATEGORY_SLUGS) {
    const { items } = catalog.searchCatalog({ category: slug, inStock: true, sort: 'relevancia', pageSize: 1 });
    const product = items[0];
    if (product && !seen.has(product.sku)) {
      picked.push(product);
      seen.add(product.sku);
    }
  }
  for (const product of catalog.getFeaturedProducts(12)) {
    if (picked.length >= 3) break;
    if (!seen.has(product.sku) && product.stock > 0) {
      picked.push(product);
      seen.add(product.sku);
    }
  }
  return picked.slice(0, 3);
}

/** Plataformas destacadas en «Consolas y juegos»; `match` son los valores de la especificación «plataforma». */
export interface PlatformFilter {
  id: string;
  label: string;
  match: readonly string[];
}

export const PLATFORM_FILTERS: readonly PlatformFilter[] = [
  { id: 'ps5', label: 'PS5', match: ['PS5'] },
  { id: 'xbox', label: 'Xbox Series X|S', match: ['Xbox Series X', 'Xbox Series S'] },
  { id: 'switch', label: 'Nintendo Switch', match: ['Nintendo Switch'] },
  { id: 'switch2', label: 'Nintendo Switch 2', match: ['Nintendo Switch 2'] },
];

/** Valor de la especificación «plataforma» de un producto (consolas, juegos y accesorios), o undefined. */
export function productPlatform(product: Product): string | undefined {
  return product.specs.find((spec) => spec.key === 'plataforma')?.text;
}

export function matchesPlatform(product: Product, filter: PlatformFilter | undefined): boolean {
  if (!filter) return true;
  const platform = productPlatform(product);
  return platform !== undefined && filter.match.includes(platform);
}

/** Etiqueta, tono de insignia y descripción corta de cada nivel de armado sugerido. */
export const TIER_META: Record<PresetTier, { label: string; tone: BadgeTone; blurb: string }> = {
  entrada: { label: 'Entrada', tone: 'nuevo', blurb: 'Para jugar en 1080p con buena tasa de cuadros sin gastar de más.' },
  media: { label: 'Gama media', tone: 'destacado', blurb: '1440p fluido hoy y margen para actualizar mañana.' },
  alta: { label: 'Gama alta', tone: 'oferta', blurb: '1440p a tope y 4K con trazado de rayos activado.' },
  entusiasta: { label: 'Entusiasta', tone: 'aviso', blurb: 'Lo mejor en cada ranura, sin concesiones.' },
  creador: { label: 'Creadores', tone: 'exito', blurb: 'Streaming, edición y render sin esperas.' },
  oficina: { label: 'Oficina', tone: 'neutral', blurb: 'Silenciosa, eficiente y lista para trabajar.' },
};

/** Ranuras que resumen un armado en su tarjeta (procesador, tarjeta de video y memoria). */
export const PRESET_HIGHLIGHT_SLOTS: readonly SlotKey[] = ['cpu', 'gpu', 'ram'];

/** Primera línea de una ranura del armado (undefined si la ranura está vacía, p. ej. sin tarjeta de video). */
export function presetLineForSlot(detail: PresetDetail, slot: SlotKey): BuildLine | undefined {
  return detail.summary.slots.find((entry) => entry.slot.key === slot)?.lines[0];
}

/** Mayor porcentaje de ahorro entre las ofertas (0 si no hay ofertas). */
export function maxSavingPercent(products: readonly Product[]): number {
  return products.reduce((max, product) => Math.max(max, savingPercent(product.price, product.listPrice)), 0);
}

/** Validación local y mínima del correo del boletín (no se envía a ningún servicio). */
export function isValidEmail(value: string): boolean {
  return /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/.test(value.trim());
}
