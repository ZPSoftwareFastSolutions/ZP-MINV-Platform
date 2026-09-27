// Pruebas de los selectores puros de la portada contra el mock real (sin React).

import { describe, expect, it } from 'vitest';
import type { Product } from '@/1-domain/catalog/types';
import { createCatalogUseCases } from '@/2-application';
import { InMemoryCatalogRepository } from '@/3-infrastructure/InMemoryCatalogRepository';
import {
  PLATFORM_FILTERS,
  PRESET_HIGHLIGHT_SLOTS,
  TIER_META,
  isValidEmail,
  matchesPlatform,
  maxSavingPercent,
  pickHeroProducts,
  presetHighlights,
  productPlatforms,
} from './homeSelectors';

const catalog = createCatalogUseCases(new InMemoryCatalogRepository());

function filter(id: string) {
  return PLATFORM_FILTERS.find((candidate) => candidate.id === id);
}

describe('selectores de la portada', () => {
  it('elige tres productos protagonistas distintos, con stock y de categorías diferentes', () => {
    const hero = pickHeroProducts(catalog);
    expect(hero).toHaveLength(3);
    expect(new Set(hero.map((product) => product.sku)).size).toBe(3);
    expect(hero.every((product) => product.stock > 0)).toBe(true);
    expect(hero.map((product) => product.category)).toEqual(['GPU', 'CPS', 'CPU']);
  });

  it('completa el hero con destacados cuando una categoría no tiene stock', () => {
    const products = new InMemoryCatalogRepository()
      .getProducts()
      .map((product): Product => (product.category === 'GPU' ? { ...product, stock: 0 } : product));
    const hero = pickHeroProducts(createCatalogUseCases(new InMemoryCatalogRepository({ products })));
    expect(hero).toHaveLength(3);
    expect(hero.some((product) => product.category === 'GPU')).toBe(false);
  });

  it('lee la plataforma de consolas y juegos y las plataformas multivalor de los accesorios', () => {
    const ps5 = catalog.getProductBySku('CON-SNY-PS5SLIM');
    const dualsense = catalog.searchCatalog({ q: 'DualSense', pageSize: 5 }).items[0];
    expect(ps5 && productPlatforms(ps5)).toEqual(['PS5']);
    expect(dualsense && productPlatforms(dualsense)).toEqual(expect.arrayContaining(['PS5']));
    expect(dualsense && productPlatforms(dualsense).length).toBeGreaterThan(1);
  });

  it('filtra por plataforma con coincidencia exacta: Switch no incluye Switch 2 y Xbox cubre X y S', () => {
    const consoles = catalog.searchCatalog({ category: 'consolas', pageSize: 96 }).items;
    const switch1 = consoles.filter((product) => matchesPlatform(product, filter('switch')));
    const switch2 = consoles.filter((product) => matchesPlatform(product, filter('switch2')));
    const xbox = consoles.filter((product) => matchesPlatform(product, filter('xbox')));
    expect(switch1.map((product) => product.sku).sort()).toEqual(['CON-NIN-SWLITE', 'CON-NIN-SWOLED']);
    expect(switch2.map((product) => product.sku).sort()).toEqual(['CON-NIN-SW2', 'CON-NIN-SW2-MKW']);
    expect(xbox).toHaveLength(3);
    expect(consoles.every((product) => matchesPlatform(product, undefined))).toBe(true);
    const cpu = catalog.searchCatalog({ category: 'procesadores', pageSize: 1 }).items[0];
    expect(cpu && matchesPlatform(cpu, filter('ps5'))).toBe(false);
  });

  it('resume cada armado con procesador, tarjeta de video y memoria (la tarjeta puede faltar)', () => {
    const presets = catalog.getPresets();
    expect(presets.length).toBeGreaterThan(0);
    for (const detail of presets) {
      const highlights = presetHighlights(detail);
      expect(highlights.map((entry) => entry.slot.key)).toEqual([...PRESET_HIGHLIGHT_SLOTS]);
      expect(highlights[0].line?.product.category).toBe('CPU');
      expect(highlights[2].line?.product.category).toBe('RAM');
      expect(TIER_META[detail.preset.tier].label).toBeTruthy();
    }
    expect(presets.some((detail) => presetHighlights(detail)[1].line === undefined)).toBe(true);
  });

  it('calcula el mayor ahorro de las ofertas y 0 sin ofertas', () => {
    const offers = catalog.getOffers(200);
    expect(maxSavingPercent(offers)).toBeGreaterThan(0);
    expect(maxSavingPercent([])).toBe(0);
    expect(maxSavingPercent(offers.map((product) => ({ ...product, listPrice: null })))).toBe(0);
  });

  it('valida el correo del boletín de forma mínima y local', () => {
    expect(isValidEmail('vos@correo.com')).toBe(true);
    expect(isValidEmail('  vos@correo.bo ')).toBe(true);
    expect(isValidEmail('')).toBe(false);
    expect(isValidEmail('sin-arroba.com')).toBe(false);
    expect(isValidEmail('vos@sin-dominio')).toBe(false);
    expect(isValidEmail('con espacio@correo.com')).toBe(false);
  });
});
