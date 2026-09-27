// Casos de uso del armador: ranuras, candidatos por ranura, armados sugeridos y resumen. Sin React.

import { summarizeBuild, type BuildSummary } from '@/1-domain/builder/build';
import { BUILD_SLOTS, slotByKey } from '@/1-domain/builder/slots';
import type { BuildLine, BuildSlot, SlotKey } from '@/1-domain/builder/types';
import { filterProducts, priceRange, sortProducts } from '@/1-domain/catalog/products';
import type { ICatalogRepository } from '@/1-domain/ports/ICatalogRepository';
import { brandFacets, resolveBrandNames } from '@/2-application/catalog/queries';
import type { PresetDetail, SlotCandidatesQuery, SlotCandidatesResult } from '@/2-application/catalog/types';

export function getBuildSlots(): readonly BuildSlot[] {
  return BUILD_SLOTS;
}

/** Productos que pueden ocupar una ranura, con búsqueda, marcas y orden (por defecto, los más populares). */
export function getSlotCandidates(
  repo: ICatalogRepository,
  slot: SlotKey,
  query: SlotCandidatesQuery = {},
): SlotCandidatesResult {
  const definition = slotByKey(slot);
  const inScope = filterProducts(repo.getProducts(), {
    categories: definition.categories,
    q: query.q,
    inStock: query.inStock,
  });
  const matching = filterProducts(inScope, { brands: resolveBrandNames(repo, query.brands) });
  return {
    slot: definition,
    items: sortProducts(matching, query.sort ?? 'relevancia', query.q),
    total: matching.length,
    facets: { brands: brandFacets(repo, inScope), priceRange: priceRange(inScope) },
  };
}

function resolvePreset(repo: ICatalogRepository, id: string): PresetDetail | undefined {
  const preset = repo.getPresets().find((candidate) => candidate.id === id);
  if (!preset) return undefined;
  const lines: BuildLine[] = [];
  const missingSkus: string[] = [];
  for (const line of preset.lines) {
    const product = repo.getProductBySku(line.sku);
    if (product) lines.push({ slot: line.slot, product, quantity: line.quantity });
    else missingSkus.push(line.sku);
  }
  const summary = summarizeBuild(lines);
  return { preset, lines: summary.lines, summary, missingSkus };
}

/** Armados sugeridos con sus productos resueltos, en el orden del catálogo. */
export function getPresets(repo: ICatalogRepository): PresetDetail[] {
  return repo
    .getPresets()
    .map((preset) => resolvePreset(repo, preset.id))
    .filter((detail): detail is PresetDetail => detail !== undefined);
}

export function getPreset(repo: ICatalogRepository, id: string): PresetDetail | undefined {
  return resolvePreset(repo, id);
}

export function buildSummary(lines: readonly BuildLine[]): BuildSummary {
  return summarizeBuild(lines);
}
