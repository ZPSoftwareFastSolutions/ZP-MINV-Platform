import { describe, expect, it } from 'vitest';
import { InMemoryCatalogRepository } from '@/3-infrastructure/InMemoryCatalogRepository';
import { createCatalogUseCases } from './index';

const useCases = createCatalogUseCases(new InMemoryCatalogRepository());

describe('casos de uso del catálogo', () => {
  it('expone el árbol, las raíces, las categorías por slug y la ruta', () => {
    expect(useCases.getCategoryTree()).toHaveLength(10);
    expect(useCases.getRootCategories().map((category) => category.slug)).toContain('componentes');
    expect(useCases.getCategory('procesadores')?.code).toBe('CPU');
    expect(useCases.getCategoryPath('CPU').map((category) => category.slug)).toEqual(['componentes', 'procesadores']);
    expect(useCases.getBrands()).toHaveLength(40);
  });

  it('busca todo el catálogo paginado con orden por defecto', () => {
    const result = useCases.searchCatalog();
    expect(result.total).toBe(159);
    expect(result.items).toHaveLength(24);
    expect(result.pageCount).toBe(7);
    expect(result.query.sort).toBe('relevancia');
    expect(result.breadcrumbs).toEqual([]);
    expect(result.facets.categories.map((facet) => facet.code)).toHaveLength(10);
    expect(result.facets.priceRange).toEqual({ min: 79, max: 36099 });
  });

  it('busca dentro de una categoría raíz (con su subárbol) y devuelve las hijas como facetas', () => {
    const result = useCases.searchCatalog({ category: 'componentes', pageSize: 100 });
    expect(result.total).toBe(63);
    expect(result.category?.code).toBe('COMP');
    expect(result.breadcrumbs.map((category) => category.code)).toEqual(['COMP']);
    expect(result.facets.categories.map((facet) => facet.code)).toEqual(['CPU', 'GPU', 'MB', 'RAM', 'STO', 'PSU', 'CASE', 'COOL']);
    expect(result.facets.categories.find((facet) => facet.code === 'GPU')?.count).toBe(12);
  });

  it('filtra por marca con códigos y calcula las facetas de marca sin su propio filtro', () => {
    const all = useCases.searchCatalog({ category: 'tarjetas-de-video', pageSize: 100 });
    const asus = useCases.searchCatalog({ category: 'tarjetas-de-video', brands: ['ASUS'], pageSize: 100 });
    expect(asus.total).toBeGreaterThan(0);
    expect(asus.items.every((product) => product.brand === 'ASUS')).toBe(true);
    expect(asus.facets.brands).toEqual(all.facets.brands);
    expect(asus.facets.brands.reduce((acc, facet) => acc + facet.count, 0)).toBe(all.total);
    const coolerMaster = useCases.searchCatalog({ brands: ['COOLERMASTER'], pageSize: 100 });
    expect(coolerMaster.items.every((product) => product.brand === 'Cooler Master')).toBe(true);
  });

  it('combina texto, precio, stock y etiquetas; ajusta la página fuera de rango', () => {
    const ofertas = useCases.searchCatalog({ tags: ['oferta'], pageSize: 100 });
    expect(ofertas.total).toBe(27);
    const baratas = useCases.searchCatalog({ q: 'ssd', maxPrice: 900, inStock: true, sort: 'precio-asc', pageSize: 100 });
    expect(baratas.items.length).toBeGreaterThan(0);
    expect(baratas.items.every((product) => product.price <= 900 && product.stock > 0)).toBe(true);
    expect(baratas.items[0].price).toBeLessThanOrEqual(baratas.items.at(-1)!.price);
    const fuera = useCases.searchCatalog({ page: 99, pageSize: 50 });
    expect(fuera.page).toBe(fuera.pageCount);
    expect(fuera.items.length).toBeGreaterThan(0);
    expect(useCases.searchCatalog({ q: 'zzzz-nada' }).total).toBe(0);
  });

  it('devuelve destacados, ofertas y novedades limitados', () => {
    expect(useCases.getFeaturedProducts(6)).toHaveLength(6);
    expect(useCases.getOffers(4).every((product) => product.listPrice !== null)).toBe(true);
    expect(useCases.getNewArrivals(5).every((product) => product.tags.includes('nuevo'))).toBe(true);
    expect(useCases.getFeaturedProducts()).toHaveLength(8);
  });

  it('resuelve un producto por slug y sus relacionados', () => {
    const product = useCases.getProduct('cpu-amd-5600');
    expect(product?.sku).toBe('CPU-AMD-5600');
    expect(useCases.getProduct('no-existe')).toBeUndefined();
    const related = useCases.getRelatedProducts('cpu-amd-5600', 3);
    expect(related).toHaveLength(3);
    expect(related.every((candidate) => candidate.slug !== 'cpu-amd-5600')).toBe(true);
    expect(useCases.getRelatedProducts('no-existe')).toEqual([]);
  });
});

describe('casos de uso del armador', () => {
  it('lista candidatos por ranura con búsqueda, marcas y orden', () => {
    const cpus = useCases.getSlotCandidates('cpu');
    expect(cpus.total).toBe(10);
    expect(cpus.slot.label).toBe('Procesador');
    expect(cpus.items.every((product) => product.category === 'CPU')).toBe(true);
    expect(cpus.items[0].popularity).toBeGreaterThanOrEqual(cpus.items.at(-1)!.popularity);
    expect(cpus.facets.brands.map((facet) => facet.name)).toEqual(['AMD', 'Intel']);

    const intel = useCases.getSlotCandidates('cpu', { brands: ['INTEL'], sort: 'precio-desc' });
    expect(intel.items.every((product) => product.brand === 'Intel')).toBe(true);
    expect(intel.items[0].price).toBeGreaterThanOrEqual(intel.items.at(-1)!.price);

    const perifericos = useCases.getSlotCandidates('peripherals', { q: 'teclado' });
    expect(perifericos.items.length).toBeGreaterThan(0);
    expect(perifericos.items.every((product) => product.category === 'KEY')).toBe(true);
  });

  it('resuelve los 6 armados sugeridos con sus productos y totales', () => {
    const presets = useCases.getPresets();
    expect(presets).toHaveLength(6);
    for (const detail of presets) {
      expect(detail.missingSkus).toEqual([]);
      expect(detail.lines.length).toBe(detail.preset.lines.length);
      expect(detail.summary.total).toBeGreaterThan(0);
      expect(detail.summary.progress.complete).toBe(true);
    }
    const entrada = useCases.getPreset('arm-cm-000001');
    expect(entrada?.preset.tier).toBe('entrada');
    expect(entrada?.lines.map((line) => line.slot)).toEqual(['cpu', 'motherboard', 'ram', 'gpu', 'storage', 'psu', 'case', 'software', 'software']);
    expect(useCases.getPreset('nada')).toBeUndefined();
  });

  it('resume líneas arbitrarias', () => {
    const cpu = useCases.getProductBySku('CPU-INT-14400F')!;
    const summary = useCases.buildSummary([{ slot: 'cpu', product: cpu, quantity: 1 }]);
    expect(summary.total).toBe(cpu.price);
    expect(summary.missing.map((slot) => slot.key)).toEqual(['motherboard', 'ram', 'storage', 'psu', 'case']);
    expect(useCases.getBuildSlots()).toHaveLength(11);
  });
});
