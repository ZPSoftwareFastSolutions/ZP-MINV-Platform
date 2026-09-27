import { describe, expect, it } from 'vitest';
import { PRODUCTS } from '@/3-infrastructure/data/catalog.data';
import { CATEGORIES } from '@/3-infrastructure/data/categories.data';
import {
  buildCategoryTree,
  categoryPath,
  categorySubtreeCodes,
  findCategoryBySlug,
  rootCategories,
} from './categories';
import { ivaBreakdown, isOnSale, savingAmount, savingPercent, sumMoney } from './money';
import { filterProducts, matchesQuery, priceRange, productsWithTag, relatedProducts, sortProducts } from './products';
import { stockLabel, stockStatus } from './stock';

const cpu5600 = PRODUCTS.find((product) => product.sku === 'CPU-AMD-5600')!;

describe('dinero', () => {
  it('desglosa el IVA incluido del 13 %', () => {
    const breakdown = ivaBreakdown(1130);
    expect(breakdown.total).toBe(1130);
    expect(breakdown.net).toBe(1000);
    expect(breakdown.iva).toBe(130);
  });

  it('calcula el ahorro de una oferta real del catálogo', () => {
    expect(isOnSale(cpu5600.price, cpu5600.listPrice)).toBe(true);
    expect(savingAmount(cpu5600.price, cpu5600.listPrice)).toBe(164.85);
    expect(savingPercent(cpu5600.price, cpu5600.listPrice)).toBe(13);
  });

  it('sin precio de lista no hay oferta ni ahorro', () => {
    expect(isOnSale(100, null)).toBe(false);
    expect(savingPercent(100, 100)).toBe(0);
    expect(sumMoney([0.1, 0.2])).toBe(0.3);
  });
});

describe('categorías', () => {
  it('arma el árbol con 10 raíces y las hijas de Componentes en orden', () => {
    const tree = buildCategoryTree(CATEGORIES);
    expect(tree).toHaveLength(10);
    expect(rootCategories(CATEGORIES).map((category) => category.code)).toEqual([
      'COMP', 'PC', 'MON', 'PER', 'CON', 'JUE', 'ACC', 'RED', 'CAB', 'SOFT',
    ]);
    const componentes = tree[0];
    expect(componentes.code).toBe('COMP');
    expect(componentes.children.map((child) => child.code)).toEqual(['CPU', 'GPU', 'MB', 'RAM', 'STO', 'PSU', 'CASE', 'COOL']);
  });

  it('resuelve la ruta raíz → hija y el subárbol', () => {
    expect(categoryPath(CATEGORIES, 'GPU').map((category) => category.name)).toEqual(['Componentes', 'Tarjetas de video']);
    expect(categorySubtreeCodes(CATEGORIES, 'CON')).toEqual(['CON', 'CPS', 'CXB', 'CNS']);
    expect(categorySubtreeCodes(CATEGORIES, 'MON')).toEqual(['MON']);
    expect(categoryPath(CATEGORIES, 'NO-EXISTE')).toEqual([]);
  });

  it('busca por slug', () => {
    expect(findCategoryBySlug(CATEGORIES, 'tarjetas-de-video')?.code).toBe('GPU');
    expect(findCategoryBySlug(CATEGORIES, 'nada')).toBeUndefined();
  });
});

describe('productos', () => {
  it('busca sin acentos ni mayúsculas y exige todas las palabras', () => {
    expect(matchesQuery(cpu5600, 'RYZEN 5')).toBe(true);
    expect(matchesQuery(cpu5600, 'ryzen intel')).toBe(false);
    expect(matchesQuery(cpu5600, '')).toBe(true);
    const liquida = filterProducts(PRODUCTS, { q: 'refrigeracion liquida' });
    expect(liquida.length).toBeGreaterThan(0);
    expect(liquida.every((product) => product.category === 'COOL')).toBe(true);
  });

  it('filtra por categoría (subárbol), marca, precio, condición y stock', () => {
    const subtree = categorySubtreeCodes(CATEGORIES, 'COMP');
    const componentes = filterProducts(PRODUCTS, { categories: subtree });
    expect(componentes).toHaveLength(63);

    const amd = filterProducts(PRODUCTS, { categories: ['CPU'], brands: ['AMD'] });
    expect(amd.length).toBeGreaterThan(0);
    expect(amd.every((product) => product.brand === 'AMD' && product.category === 'CPU')).toBe(true);

    const baratos = filterProducts(PRODUCTS, { minPrice: 100, maxPrice: 500 });
    expect(baratos.every((product) => product.price >= 100 && product.price <= 500)).toBe(true);

    expect(filterProducts(PRODUCTS, { condition: 'Reacondicionado' })).toHaveLength(1);
    expect(filterProducts(PRODUCTS, { inStock: true }).every((product) => product.stock > 0)).toBe(true);
    expect(filterProducts(PRODUCTS, { tags: ['destacado', 'oferta'] }).every((p) => p.tags.includes('oferta'))).toBe(true);
  });

  it('ordena por precio, nombre y relevancia', () => {
    const asc = sortProducts(PRODUCTS, 'precio-asc');
    expect(asc[0].price).toBe(79);
    expect(asc.at(-1)?.price).toBe(36099);
    const desc = sortProducts(PRODUCTS, 'precio-desc');
    expect(desc[0].price).toBe(36099);
    const nombre = sortProducts(PRODUCTS, 'nombre');
    expect(nombre[0].name.localeCompare(nombre[1].name, 'es', { sensitivity: 'base' })).toBeLessThanOrEqual(0);
    const relevantes = sortProducts(filterProducts(PRODUCTS, { q: 'ryzen 5' }), 'relevancia', 'ryzen 5');
    expect(relevantes[0].category).toBe('CPU');
    expect(sortProducts(PRODUCTS, 'relevancia')[0].popularity).toBe(10);
  });

  it('calcula rango de precios y listas por etiqueta', () => {
    expect(priceRange(PRODUCTS)).toEqual({ min: 79, max: 36099 });
    expect(priceRange([])).toEqual({ min: 0, max: 0 });
    const destacados = productsWithTag(PRODUCTS, 'destacado', 8);
    expect(destacados).toHaveLength(8);
    expect(destacados.every((product) => product.tags.includes('destacado'))).toBe(true);
    expect(destacados[0].popularity).toBeGreaterThanOrEqual(destacados[7].popularity);
  });

  it('sugiere productos relacionados de la misma categoría sin repetir el original', () => {
    const related = relatedProducts(PRODUCTS, cpu5600, 4);
    expect(related).toHaveLength(4);
    expect(related.every((product) => product.category === 'CPU' && product.sku !== cpu5600.sku)).toBe(true);
  });
});

describe('stock', () => {
  it('describe la disponibilidad', () => {
    expect(stockStatus({ stock: 0 })).toBe('agotado');
    expect(stockLabel({ stock: 0 })).toBe('Agotado');
    expect(stockLabel({ stock: 1 })).toBe('Última unidad');
    expect(stockLabel({ stock: 3 })).toBe('Últimas 3 unidades');
    expect(stockLabel({ stock: 20 })).toBe('En stock');
    expect(PRODUCTS.filter((product) => stockStatus(product) === 'agotado')).toHaveLength(8);
  });
});
