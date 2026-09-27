import { describe, expect, it } from 'vitest';
import {
  activeFilterCount,
  catalogFiltersToParams,
  catalogHref,
  catalogQuery,
  clearedFilters,
  EMPTY_FILTERS,
  hasActiveFilters,
  parseCatalogFilters,
  toggleBrand,
  toggleTag,
} from './catalogFilters';

describe('filtros del catálogo en la URL', () => {
  it('lee todos los parámetros y normaliza las marcas a código en mayúsculas', () => {
    const params = new URLSearchParams('q= rtx 5070 &marca=asus&marca=MSI,asus&min=500&max=2000&condicion=nuevo&stock=1&tags=oferta,nuevo&orden=precio-asc&pagina=3&vista=lista');
    const filters = parseCatalogFilters('tarjetas-de-video', params);
    expect(filters).toEqual({
      category: 'tarjetas-de-video',
      q: 'rtx 5070',
      brands: ['ASUS', 'MSI'],
      minPrice: 500,
      maxPrice: 2000,
      condition: 'Nuevo',
      inStock: true,
      tags: ['oferta', 'nuevo'],
      sort: 'precio-asc',
      page: 3,
      view: 'lista',
    });
  });

  it('vuelve a los valores por defecto ante parámetros inválidos y ordena el rango de precio', () => {
    const params = new URLSearchParams('min=3000&max=100&condicion=roto&orden=raro&pagina=-2&vista=mosaico&tags=x&stock=si');
    const filters = parseCatalogFilters(undefined, params);
    expect(filters.category).toBeUndefined();
    expect(filters.minPrice).toBe(100);
    expect(filters.maxPrice).toBe(3000);
    expect(filters.condition).toBeUndefined();
    expect(filters.sort).toBe('relevancia');
    expect(filters.page).toBe(1);
    expect(filters.view).toBe('grilla');
    expect(filters.tags).toEqual([]);
    expect(filters.inStock).toBe(false);
  });

  it('acepta importes con formato boliviano («1.500,50»)', () => {
    const filters = parseCatalogFilters(undefined, new URLSearchParams('min=1.500,50&max=2.049'));
    expect(filters.minPrice).toBe(1500.5);
    expect(filters.maxPrice).toBe(2049);
  });

  it('escribe solo lo distinto del defecto, con marcas repetidas y en orden fijo', () => {
    const params = catalogFiltersToParams({
      ...EMPTY_FILTERS,
      q: 'ddr5',
      brands: ['CORSAIR', 'KINGSTON'],
      maxPrice: 900,
      inStock: true,
      sort: 'nombre',
      page: 2,
    });
    expect(params.toString()).toBe('q=ddr5&marca=CORSAIR&marca=KINGSTON&max=900&stock=1&orden=nombre&pagina=2');
    expect(catalogFiltersToParams(EMPTY_FILTERS).toString()).toBe('');
  });

  it('ida y vuelta: leer lo escrito devuelve los mismos filtros', () => {
    const original = {
      ...EMPTY_FILTERS,
      category: 'procesadores',
      brands: ['AMD'],
      minPrice: 800,
      condition: 'Reacondicionado' as const,
      tags: ['oferta' as const],
      view: 'lista' as const,
    };
    const href = catalogHref(original);
    expect(href).toBe('/catalogo/procesadores?marca=AMD&min=800&condicion=reacondicionado&tags=oferta&vista=lista');
    const [path, query] = href.split('?');
    expect(path).toBe('/catalogo/procesadores');
    expect(parseCatalogFilters('procesadores', new URLSearchParams(query))).toEqual(original);
  });

  it('sin categoría ni filtros la ruta es /catalogo', () => {
    expect(catalogHref(EMPTY_FILTERS)).toBe('/catalogo');
    expect(catalogHref({ ...EMPTY_FILTERS, q: 'rtx 5070' })).toBe('/catalogo?q=rtx+5070');
  });

  it('traduce los filtros a la consulta de la aplicación omitiendo lo vacío', () => {
    expect(catalogQuery({ ...EMPTY_FILTERS, category: 'monitores', page: 2 })).toEqual({
      q: undefined,
      category: 'monitores',
      brands: undefined,
      minPrice: undefined,
      maxPrice: undefined,
      condition: undefined,
      inStock: undefined,
      tags: undefined,
      sort: 'relevancia',
      page: 2,
      pageSize: 12,
    });
    expect(catalogQuery({ ...EMPTY_FILTERS, brands: ['ASUS'], inStock: true, tags: ['nuevo'] }, 24)).toMatchObject({
      brands: ['ASUS'],
      inStock: true,
      tags: ['nuevo'],
      pageSize: 24,
    });
  });

  it('cuenta los filtros activos sin la categoría ni la búsqueda', () => {
    expect(activeFilterCount(EMPTY_FILTERS)).toBe(0);
    expect(activeFilterCount({ ...EMPTY_FILTERS, category: 'monitores', q: 'lg' })).toBe(0);
    expect(hasActiveFilters({ ...EMPTY_FILTERS, q: 'lg' })).toBe(true);
    expect(activeFilterCount({ ...EMPTY_FILTERS, brands: ['LG', 'AOC'], minPrice: 100, condition: 'Nuevo', inStock: true, tags: ['oferta'] })).toBe(6);
  });

  it('limpiar conserva la categoría, el orden y la vista', () => {
    const cleared = clearedFilters({ ...EMPTY_FILTERS, category: 'teclados', q: 'rgb', brands: ['LOGITECH'], sort: 'nombre', view: 'lista', page: 4 });
    expect(cleared).toEqual({ ...EMPTY_FILTERS, category: 'teclados', sort: 'nombre', view: 'lista' });
  });

  it('alterna marcas y etiquetas volviendo a la primera página', () => {
    const withBrand = toggleBrand({ ...EMPTY_FILTERS, page: 3 }, 'ASUS');
    expect(withBrand.brands).toEqual(['ASUS']);
    expect(withBrand.page).toBe(1);
    expect(toggleBrand(withBrand, 'ASUS').brands).toEqual([]);
    const withTags = toggleTag(toggleTag(EMPTY_FILTERS, 'nuevo'), 'oferta');
    expect(withTags.tags).toEqual(['oferta', 'nuevo']);
    expect(toggleTag(withTags, 'nuevo').tags).toEqual(['oferta']);
  });
});
