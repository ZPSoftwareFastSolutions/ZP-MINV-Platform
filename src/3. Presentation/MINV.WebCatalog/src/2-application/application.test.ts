import { describe, expect, it } from 'vitest';
import { slotForProduct } from '@/1-domain/builder/slots';
import { roundMoney } from '@/1-domain/catalog/money';
import { InMemoryCatalogRepository } from '@/3-infrastructure/InMemoryCatalogRepository';
import { normalizeText } from '@/shared/text';
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

describe('revisión de los casos de uso sobre los datos reales', () => {
  it('una categoría raíz abarca todo su subárbol y las facetas de hijas suman el total', () => {
    const componentes = useCases.searchCatalog({ category: 'componentes', pageSize: 96 });
    expect([...new Set(componentes.items.map((product) => product.category))].sort()).toEqual([
      'CASE', 'COOL', 'CPU', 'GPU', 'MB', 'PSU', 'RAM', 'STO',
    ]);
    expect(componentes.facets.categories.reduce((acc, facet) => acc + facet.count, 0)).toBe(componentes.total);
    const consolas = useCases.searchCatalog({ category: 'consolas', pageSize: 96 });
    expect(consolas.total).toBe(11);
    expect(consolas.facets.categories.map((facet) => facet.slug)).toEqual(['playstation', 'xbox', 'nintendo']);
    const procesadores = useCases.searchCatalog({ category: 'procesadores' });
    expect(procesadores.total).toBe(10);
    expect(procesadores.breadcrumbs.map((category) => category.code)).toEqual(['COMP', 'CPU']);
    expect(procesadores.facets.categories).toEqual([]);
    const inexistente = useCases.searchCatalog({ category: 'no-existe' });
    expect(inexistente.category).toBeUndefined();
    expect(inexistente.total).toBe(159);
  });

  it('la búsqueda ignora acentos y mayúsculas: «refrigeracion» encuentra «Refrigeración»', () => {
    const sinAcento = useCases.searchCatalog({ q: 'refrigeracion', pageSize: 96 });
    expect(sinAcento.total).toBe(6);
    expect(sinAcento.items.every((product) => product.category === 'COOL')).toBe(true);
    const conAcento = useCases.searchCatalog({ q: 'REFRIGERACIÓN LÍQUIDA', pageSize: 96 });
    expect(conAcento.total).toBeGreaterThan(0);
    expect(conAcento.items.every((product) => normalizeText(product.name).includes('liquida'))).toBe(true);
    expect(useCases.searchCatalog({ q: 'audifonos' }).total).toBe(useCases.searchCatalog({ q: 'Audífonos' }).total);
    expect(useCases.searchCatalog({ q: 'CPU-AMD-5600' }).items[0]?.sku).toBe('CPU-AMD-5600');
  });

  it('filtra por marca (código o nombre), precio inclusivo y condición; las facetas se calculan sin su propio filtro', () => {
    const porCodigo = useCases.searchCatalog({ brands: ['LIANLI'], pageSize: 96 });
    const porNombre = useCases.searchCatalog({ brands: ['Lian Li'], pageSize: 96 });
    expect(porCodigo.total).toBe(2);
    expect(porNombre.items.map((product) => product.sku)).toEqual(porCodigo.items.map((product) => product.sku));
    // Un código de marca desconocido se ignora (no filtra): la URL con una marca inexistente muestra todo el catálogo.
    expect(useCases.searchCatalog({ brands: ['MARCA-INEXISTENTE'] }).total).toBe(159);

    const gpus = useCases.searchCatalog({ category: 'tarjetas-de-video', pageSize: 96 });
    const minimo = gpus.facets.priceRange.min;
    const soloElMasBarato = useCases.searchCatalog({ category: 'tarjetas-de-video', maxPrice: minimo, pageSize: 96 });
    expect(soloElMasBarato.total).toBeGreaterThan(0);
    expect(soloElMasBarato.items.every((product) => product.price === minimo)).toBe(true);
    expect(soloElMasBarato.facets.priceRange).toEqual(gpus.facets.priceRange);
    expect(soloElMasBarato.facets.brands.reduce((acc, facet) => acc + facet.count, 0)).toBe(soloElMasBarato.total);
    expect(gpus.facets.conditions.reduce((acc, facet) => acc + facet.count, 0)).toBe(gpus.total);

    const reacondicionados = useCases.searchCatalog({ condition: 'Reacondicionado', pageSize: 96 });
    expect(reacondicionados.total).toBe(1);
    expect(reacondicionados.facets.conditions.map((facet) => facet.value)).toEqual(['Nuevo', 'Reacondicionado']);
  });

  it('pagina de a 24 y limita el tamaño de página a 96', () => {
    const grande = useCases.searchCatalog({ pageSize: 1000 });
    expect(grande.pageSize).toBe(96);
    expect(grande.pageCount).toBe(2);
    const segunda = useCases.searchCatalog({ page: 2 });
    expect(segunda.page).toBe(2);
    expect(segunda.items).toHaveLength(24);
    expect(segunda.items[0].sku).not.toBe(useCases.searchCatalog({ page: 1 }).items[0].sku);
    const total = Array.from({ length: segunda.pageCount }, (_, index) => useCases.searchCatalog({ page: index + 1 }).items.length).reduce(
      (acc, count) => acc + count,
      0,
    );
    expect(total).toBe(159);
  });

  it('cada armado sugerido suma precio × cantidad y sus líneas caen en la ranura de su categoría', () => {
    for (const detail of useCases.getPresets()) {
      const esperado = roundMoney(detail.lines.reduce((acc, line) => acc + line.product.price * line.quantity, 0));
      expect(detail.summary.total).toBe(esperado);
      expect(detail.summary.count).toBe(detail.lines.reduce((acc, line) => acc + line.quantity, 0));
      expect(detail.summary.savings).toBeGreaterThanOrEqual(0);
      for (const line of detail.lines) expect(slotForProduct(line.product)?.key).toBe(line.slot);
      expect(detail.lines.every((line) => line.product.stock > 0)).toBe(true);
    }
    const entrada = useCases.getPreset('arm-cm-000001')!;
    expect(entrada.summary.total).toBe(10421);
  });
});
