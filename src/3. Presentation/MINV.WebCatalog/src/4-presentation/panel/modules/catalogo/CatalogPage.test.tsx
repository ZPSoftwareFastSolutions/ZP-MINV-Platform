// Módulo «Catálogo» · pestaña «Productos» dentro del panel (con un registro PROPIO de la prueba: solo este módulo) y con un
// servidor de catálogo SIMULADO en la prueba: lista con marca, margen y disponible (pedidos exactos), filtros de la página
// y del servidor (categoría, condición, especificaciones), galería que pide solo las imágenes de la página, detalle con la
// ficha técnica, la guarda del menú «⋯», alta completa (producto → imagen → ficha, con los contenidos EXACTOS), edición con
// el reintento que corrige el MISMO producto, desactivar con confirmación, lo que ve cada rol, error con «Reintentar» y
// CSV. Ninguna prueba toca la red.

import { act, configure, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { WebApiError } from '@/1-domain/auth/errors';
import type { RpcOperationName, RpcResponseOf } from '@/4-presentation/app/contract';
import { renderPanel, signedInAs, type MockWeb, type StaffRole } from '@/test-utils';
import { buildRegistry } from '../../registry';
import { PanelApp } from '../../shell/PanelApp';
import catalogo from './module';

const REGISTRY = buildRegistry([{ source: '../modules/catalogo/module.tsx', definition: catalogo }]);

/** Con toda la suite en paralelo la primera pantalla tarda más: pruebas y esperas con más margen. */
const SLOW = { timeout: 30_000 };
configure({ asyncUtilTimeout: 5000 });

beforeAll(async () => {
  await import('./CatalogPage');
}, 30_000);

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

type Product = RpcResponseOf<'GetCatalogQuery'>[number];
type Tech = RpcResponseOf<'SearchTechProductsQuery'>[number];
type Definition = RpcResponseOf<'GetSpecDefinitionsQuery'>[number];

function product(overrides: Partial<Product>): Product {
  return {
    variantId: 'v',
    sku: 'SKU',
    name: 'Producto',
    description: null,
    categoryCode: 'MON',
    category: 'Monitores',
    unit: 'UND',
    supplierCode: null,
    supplier: null,
    salePrice: 100,
    unitCost: 50,
    minimum: 0,
    maximum: 0,
    barcode: null,
    isActive: true,
    hasImage: false,
    binCode: null,
    ...overrides,
  };
}

function tech(overrides: Partial<Tech>): Tech {
  return { sku: 'SKU', name: '', categoryCode: '', category: '', brand: null, price: 0, stock: 0, trackSerials: false, warrantyMonths: 0, keySpecs: '', platforms: [], imageId: null, serialKind: 'Serial', ...overrides };
}

function definition(overrides: Partial<Definition>): Definition {
  return {
    id: 'd',
    categoryCode: 'MON',
    categoryName: 'Monitores',
    code: 'x',
    name: 'X',
    unit: null,
    dataType: 'Option',
    isMultiValued: false,
    isFilterable: true,
    isRequired: false,
    compatibilityKey: null,
    sortOrder: 10,
    options: [],
    isInherited: false,
    ...overrides,
  };
}

const OPTIONS: RpcResponseOf<'GetCatalogOptionsQuery'> = {
  categories: [
    { code: 'CAB', name: 'Cables' },
    { code: 'CEL', name: 'Celulares' },
    { code: 'CON', name: 'Consolas' },
    { code: 'MON', name: 'Monitores' },
    { code: 'JUE', name: 'Videojuegos' },
  ],
  units: [
    { code: 'MTS', name: 'Metro', allowsDecimals: true },
    { code: 'UND', name: 'Unidad', allowsDecimals: false },
  ],
  suppliers: [{ code: 'PRV-01', name: 'Distribuidora Andina' }],
  taxRate: 13,
  priceListName: 'GENERAL',
  vatOnInvoicedAmount: true,
};

const MONITOR = product({
  variantId: 'v-mon',
  sku: 'MON-LG-27',
  name: 'Monitor LG 27GP850',
  salePrice: 3200,
  unitCost: 2200,
  hasImage: true,
  barcode: '8806091234567',
  binCode: 'CM-A1-01',
  supplierCode: 'PRV-01',
  supplier: 'Distribuidora Andina',
  minimum: 2,
  maximum: 10,
});

const CATALOG: RpcResponseOf<'GetCatalogQuery'> = [
  MONITOR,
  product({ variantId: 'v-ps5', sku: 'CON-PS5', name: 'Consola PlayStation 5', categoryCode: 'CON', category: 'Consolas', salePrice: 5500, unitCost: 4600, hasImage: true }),
  product({ variantId: 'v-fc25', sku: 'JUE-FC25', name: 'EA Sports FC 25', categoryCode: 'JUE', category: 'Videojuegos', salePrice: 450, unitCost: 300 }),
  product({ variantId: 'v-a55', sku: 'CEL-A55', name: 'Samsung Galaxy A55', categoryCode: 'CEL', category: 'Celulares', salePrice: 3100, unitCost: 2000 }),
  product({ variantId: 'v-hdmi', sku: 'CAB-HDMI', name: 'Cable HDMI 2 m', categoryCode: 'CAB', category: 'Cables', salePrice: 0, unitCost: 20, isActive: false }),
];

const TECH: RpcResponseOf<'SearchTechProductsQuery'> = [
  tech({ sku: 'MON-LG-27', brand: 'LG', stock: 5, trackSerials: true, warrantyMonths: 12, keySpecs: '27 pulgadas · IPS' }),
  tech({ sku: 'CON-PS5', brand: 'Sony', stock: 2, trackSerials: true, warrantyMonths: 12, platforms: ['PS5'] }),
  tech({ sku: 'JUE-FC25', brand: 'EA', stock: 10, platforms: ['PS5'] }),
  tech({ sku: 'CEL-A55', brand: 'Samsung', stock: 3, trackSerials: true, serialKind: 'Imei', warrantyMonths: 12 }),
];

const SIZE = definition({ code: 'tamano', name: 'Tamaño', unit: 'pulgadas', dataType: 'Number', isRequired: true, sortOrder: 10 });
const PANEL = definition({ code: 'panel', name: 'Panel', options: ['IPS', 'VA'], sortOrder: 20 });
const DEFINITIONS: Definition[] = [
  definition({ code: 'plataforma', name: 'Plataforma', categoryCode: 'CON', categoryName: 'Consolas', options: ['PS4', 'PS5', 'Xbox Series X'] }),
  definition({ code: 'condicion', name: 'Condición', categoryCode: 'JUE', categoryName: 'Videojuegos', options: ['Nuevo', 'Usado'] }),
  SIZE,
  PANEL,
];

const FACETS: RpcResponseOf<'GetSpecFacetsQuery'> = [
  { code: 'tamano', name: 'Tamaño', unit: 'pulgadas', dataType: 'Number', values: [{ value: '24', count: 2 }, { value: '27', count: 1 }], min: 24, max: 27 },
  { code: 'panel', name: 'Panel', unit: null, dataType: 'Option', values: [{ value: 'IPS', count: 3 }], min: null, max: null },
];

const MONITOR_TECH: RpcResponseOf<'GetProductTechQuery'> = {
  sku: 'MON-LG-27',
  name: 'Monitor LG 27GP850',
  category: 'Monitores',
  brand: 'LG',
  serialKind: 'Serial',
  serialsInStock: 5,
  trackSerials: true,
  warrantyMonths: 12,
  specs: [
    { code: 'tamano', name: 'Tamaño', unit: 'pulgadas', dataType: 'Number', values: ['27'], display: '27 pulgadas', compatibilityKey: null, isRequired: true },
    { code: 'panel', name: 'Panel', unit: null, dataType: 'Option', values: ['IPS'], display: 'IPS', compatibilityKey: null, isRequired: false },
  ],
};

type Handler = (payload: unknown) => unknown;
const COMMANDS: readonly string[] = ['SaveProductCommand', 'SetProductImageCommand', 'RemoveProductImageCommand', 'SaveProductTechCommand', 'SaveCategoryCommand'];

/** Servidor de catálogo en memoria: responde las operaciones del módulo y deja el resto (la sesión) al modo mock. */
function fakeServer(web: MockWeb, handlers: Partial<Record<RpcOperationName, Handler>> = {}) {
  const real = web.backend.rpc.call.bind(web.backend.rpc);
  const spy = vi.spyOn(web.backend.rpc, 'call').mockImplementation(async (operation, payload, options) => {
    const reply = (result: unknown) => ({ result: result as never, replayed: false, requestId: options?.requestId ?? 'prueba' });
    const custom = handlers[operation];
    if (custom) return reply(await custom(payload));
    const body = payload as Record<string, unknown>;
    switch (operation) {
      case 'GetCatalogOptionsQuery':
        return reply(OPTIONS);
      case 'GetCatalogQuery': {
        const filters = (body.specFilters as { code: string; values: string[] }[] | null) ?? [];
        return reply(
          CATALOG.filter(
            (row) =>
              (!body.categoryCode || row.categoryCode === body.categoryCode) &&
              filters.every((filter) => (filter.code === 'condicion' ? row.sku === 'JUE-FC25' && filter.values[0] === 'Usado' : row.sku === 'MON-LG-27' && filter.values[0] === '27')),
          ),
        );
      }
      case 'SearchTechProductsQuery':
        return reply(TECH);
      case 'GetSpecDefinitionsQuery':
        return reply(body.categoryCode === null ? DEFINITIONS : body.categoryCode === 'MON' ? [SIZE, PANEL] : []);
      case 'GetSpecFacetsQuery':
        return reply(body.categoryCode === 'MON' ? FACETS : []);
      case 'GetProductImagesQuery':
        return reply(((body.variantIds as string[] | null) ?? []).map((variantId) => ({ variantId, sku: variantId, content: 'QUJD', contentType: 'image/png' })));
      case 'GetProductTechQuery':
        return reply(body.sku === 'MON-LG-27' ? MONITOR_TECH : { ...MONITOR_TECH, sku: body.sku, specs: [], trackSerials: false, warrantyMonths: 0, serialsInStock: 0 });
      case 'GetWorkspaceQuery':
        return reply({ warehouseCode: 'CM-PRINCIPAL', warehouseName: 'Almacén principal', companyName: 'Tech Zone Gaming', today: '2026-09-29' });
      case 'GetBinsQuery':
        return reply([
          { code: 'CM-A1-01', zone: 'Zona A', warehouseCode: 'CM-PRINCIPAL' },
          { code: 'CM-GENERAL', zone: 'General', warehouseCode: 'CM-PRINCIPAL' },
        ]);
      case 'SaveProductCommand':
        return reply(String(body.sku));
      case 'SetProductImageCommand':
      case 'RemoveProductImageCommand':
        return reply(true);
      case 'SaveProductTechCommand':
        return reply(String(body.sku));
      default:
        return real(operation as never, payload as never, options);
    }
  });
  const payloads = (operation: RpcOperationName) => spy.mock.calls.filter(([name]) => name === operation).map(([, payload]) => payload);
  const order = () => spy.mock.calls.map(([name]) => name).filter((name) => COMMANDS.includes(name));
  return { spy, payloads, order };
}

async function openCatalog(role: StaffRole = 'ADMIN', query = '', handlers: Partial<Record<RpcOperationName, Handler>> = {}) {
  const web = await signedInAs(role);
  const server = fakeServer(web, handlers);
  const view = await renderPanel(<PanelApp registry={REGISTRY} />, { web: web.services, route: `/panel/catalogo${query}`, path: '/panel/*' });
  return { web, server, location: view.location };
}

async function productsTable(): Promise<HTMLElement> {
  const found = await screen.findByTestId('tabla', undefined, { timeout: 5000 });
  await waitFor(() => expect(within(found).queryAllByRole('row').filter((row) => row.hasAttribute('data-row-key')).length).toBeGreaterThan(0));
  return found;
}

function keys(table: HTMLElement): (string | null)[] {
  return within(table)
    .getAllByRole('row')
    .filter((row) => row.hasAttribute('data-row-key'))
    .map((row) => row.getAttribute('data-row-key'));
}

function rowOf(table: HTMLElement, key: string): HTMLElement {
  const row = within(table)
    .getAllByRole('row')
    .find((item) => item.getAttribute('data-row-key') === key);
  if (!row) throw new Error(`No está la fila ${key}`);
  return row;
}

async function openRowMenu(table: HTMLElement, key: string): Promise<HTMLElement> {
  fireEvent.click(within(rowOf(table, key)).getByRole('button', { name: /^Acciones de / }));
  return screen.findByTestId('menu-acciones');
}

function choose(scope: HTMLElement, name: RegExp | string, value: string) {
  fireEvent.change(within(scope).getByRole('combobox', { name }), { target: { value } });
}

function type(scope: HTMLElement, name: RegExp, value: string) {
  fireEvent.change(within(scope).getByLabelText(name), { target: { value } });
}

const ALL_ORDERED = ['CAB-HDMI', 'CON-PS5', 'JUE-FC25', 'MON-LG-27', 'CEL-A55'];

describe('Catálogo · lista de productos', SLOW, () => {
  it('muestra el catálogo con marca, precio, margen y disponible (pedidos exactos); el resumen está plegado', async () => {
    const { server } = await openCatalog();
    expect(await screen.findByRole('heading', { level: 1, name: 'Catálogo' }, { timeout: 5000 })).toBeInTheDocument();
    const table = await productsTable();
    expect(keys(table)).toEqual(ALL_ORDERED);
    expect(server.payloads('GetCatalogQuery')).toEqual([{ categoryCode: null, specFilters: null }]);
    expect(server.payloads('SearchTechProductsQuery')).toEqual([{ text: null, categoryCode: null, platform: null, filters: null, onlyInStock: false, max: 2000 }]);
    expect(server.payloads('GetCatalogOptionsQuery')).toEqual([{}]);
    expect(server.payloads('GetSpecDefinitionsQuery')).toEqual([{ categoryCode: null }]);
    expect(server.payloads('GetProductImagesQuery')).toEqual([]);
    await waitFor(() => expect(rowOf(table, 'CON-PS5')).toHaveTextContent('Sony'));
    expect(rowOf(table, 'CON-PS5')).toHaveTextContent('3,9 %');
    expect(rowOf(table, 'MON-LG-27')).toHaveTextContent('Bs 3.200,00');
    expect(rowOf(table, 'MON-LG-27')).toHaveTextContent('5 UND');
    expect(rowOf(table, 'CEL-A55')).toHaveTextContent('IMEI');
    expect(rowOf(table, 'CAB-HDMI')).toHaveTextContent('Sin precio');
    expect(rowOf(table, 'CAB-HDMI')).toHaveTextContent('Inactivo');
    expect(screen.getByTestId('catalogo-resumen')).toHaveTextContent('5 de 5 productos · 4 activos');
    expect(screen.queryByTestId('resumen-catalogo')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /Ver resumen del catálogo/ }));
    expect(await screen.findByTestId('resumen-catalogo')).toHaveTextContent(/Productos activos\s*4/);
  });

  it('filtra en la página con listas desplegables y búsqueda; los filtros quedan en la dirección', async () => {
    const { location } = await openCatalog();
    const table = await productsTable();
    await waitFor(() => expect(rowOf(table, 'CON-PS5')).toHaveTextContent('Sony'));
    choose(document.body, 'Marca', 'Sony');
    await waitFor(() => expect(location()).toBe('/panel/catalogo?marca=Sony'));
    await waitFor(() => expect(keys(table)).toEqual(['CON-PS5']));
    const clear = () => fireEvent.click(screen.getByRole('button', { name: 'Limpiar filtros' }));
    clear();
    choose(document.body, 'Estado', 'inactivo');
    await waitFor(() => expect(keys(table)).toEqual(['CAB-HDMI']));
    clear();
    choose(document.body, 'Imagen', 'con');
    await waitFor(() => expect(keys(table)).toEqual(['CON-PS5', 'MON-LG-27']));
    clear();
    choose(document.body, 'Precio', 'margen-bajo');
    await waitFor(() => expect(keys(table)).toEqual(['CON-PS5']));
    clear();
    choose(document.body, 'Serie o IMEI', 'imei');
    await waitFor(() => expect(keys(table)).toEqual(['CEL-A55']));
    clear();
    choose(document.body, 'Plataforma', 'PS5');
    await waitFor(() => expect(keys(table)).toEqual(['CON-PS5', 'JUE-FC25']));
    clear();
    const search = screen.getByRole('searchbox', { name: 'Buscar' });
    fireEvent.change(search, { target: { value: '8806091234567' } });
    fireEvent.keyDown(search, { key: 'Enter' });
    await waitFor(() => expect(location()).toBe('/panel/catalogo?q=8806091234567'));
    await waitFor(() => expect(keys(table)).toEqual(['MON-LG-27']));
  });

  it('la categoría (con sus subcategorías), sus especificaciones y la condición las filtra el SERVIDOR', async () => {
    const { server, location } = await openCatalog();
    const table = await productsTable();
    choose(document.body, 'Categoría', 'MON');
    await waitFor(() => expect(keys(table)).toEqual(['MON-LG-27']));
    expect(server.payloads('GetCatalogQuery')).toContainEqual({ categoryCode: 'MON', specFilters: null });
    expect(server.payloads('GetSpecFacetsQuery')).toEqual([{ categoryCode: 'MON' }]);
    const size = await screen.findByRole('combobox', { name: 'Tamaño' });
    expect(within(size).getByRole('option', { name: '27 pulgadas (1)' })).toBeInTheDocument();
    fireEvent.change(size, { target: { value: '27' } });
    await waitFor(() => expect(server.payloads('GetCatalogQuery')).toContainEqual({ categoryCode: 'MON', specFilters: [{ code: 'tamano', values: ['27'], min: null, max: null }] }));
    expect(location()).toBe('/panel/catalogo?categoria=MON&espec=tamano%3A27');
    await waitFor(() => expect(keys(table)).toEqual(['MON-LG-27']));
    // Cambiar la categoría borra las especificaciones elegidas.
    choose(document.body, 'Categoría', '');
    await waitFor(() => expect(location()).toBe('/panel/catalogo'));
    choose(document.body, 'Condición', 'Usado');
    await waitFor(() => expect(keys(table)).toEqual(['JUE-FC25']));
    expect(server.payloads('GetCatalogQuery')).toContainEqual({ categoryCode: null, specFilters: [{ code: 'condicion', values: ['Usado'], min: null, max: null }] });
  });

  it('la galería muestra las imágenes y las pide SOLO para los productos de la página que tienen imagen', async () => {
    const { server, location } = await openCatalog();
    await productsTable();
    fireEvent.click(screen.getByRole('button', { name: 'Galería' }));
    await waitFor(() => expect(location()).toBe('/panel/catalogo?vista=galeria'));
    const gallery = await screen.findByTestId('galeria');
    await waitFor(() => expect(within(gallery).getByAltText('Imagen de Monitor LG 27GP850')).toHaveAttribute('src', 'data:image/png;base64,QUJD'));
    expect(server.payloads('GetProductImagesQuery')).toEqual([{ variantIds: ['v-ps5', 'v-mon'] }]);
    expect(within(gallery).getAllByRole('listitem').map((item) => item.getAttribute('data-row-key'))).toEqual(ALL_ORDERED);
    choose(gallery, 'Ordenar por', 'precio-desc');
    await waitFor(() => expect(within(gallery).getAllByRole('listitem')[0]).toHaveAttribute('data-row-key', 'CON-PS5'));
    expect(location()).toBe('/panel/catalogo?vista=galeria&orden=precio&sentido=desc');
  });

  it('el detalle lateral muestra la imagen y la ficha técnica y lleva a la ficha con kardex', async () => {
    const { server, location } = await openCatalog();
    const table = await productsTable();
    fireEvent.click(within(rowOf(table, 'MON-LG-27')).getAllByRole('button')[0]);
    const panel = await screen.findByRole('dialog', { name: 'Monitor LG 27GP850' });
    await waitFor(() => expect(within(panel).getByTestId('ficha-tecnica')).toHaveTextContent('27 pulgadas'));
    expect(within(panel).getByTestId('ficha-tecnica')).toHaveTextContent('Número de serie (una por unidad)');
    expect(within(panel).getByTestId('ficha-tecnica')).toHaveTextContent('1 año de garantía');
    expect(await within(panel).findByAltText('Imagen de Monitor LG 27GP850')).toHaveAttribute('src', 'data:image/png;base64,QUJD');
    expect(within(panel).getByRole('link', { name: 'Ver ficha y kardex' })).toHaveAttribute('href', '/panel/stock?ficha=MON-LG-27');
    expect(server.payloads('GetProductTechQuery')).toEqual([{ sku: 'MON-LG-27' }]);
    expect(server.payloads('GetProductImagesQuery')).toEqual([{ variantIds: ['v-mon'] }]);
    expect(location()).toBe('/panel/catalogo?producto=MON-LG-27');
  });

  it('elegir una acción del menú «⋯» NO abre además el detalle de la fila (guarda del módulo)', async () => {
    const { location } = await openCatalog();
    const table = await productsTable();
    fireEvent.click(within(await openRowMenu(table, 'CON-PS5')).getByRole('menuitem', { name: 'Editar' }));
    expect(await screen.findByRole('dialog', { name: 'Editar producto' })).toBeInTheDocument();
    expect(screen.queryByRole('dialog', { name: 'Consola PlayStation 5' })).not.toBeInTheDocument();
    expect(location()).toBe('/panel/catalogo');
  });

  it('si la consulta falla muestra «Reintentar» y se recupera', async () => {
    let fail = true;
    await openCatalog('ADMIN', '', {
      GetCatalogQuery: () => {
        if (fail) {
          fail = false;
          throw new WebApiError({ kind: 'network', message: 'Sin conexión' });
        }
        return CATALOG;
      },
    });
    const error = await screen.findByTestId('estado-error', undefined, { timeout: 5000 });
    fireEvent.click(within(error).getByRole('button', { name: 'Reintentar' }));
    const table = await productsTable();
    expect(keys(table)).toEqual(ALL_ORDERED);
  });

  it('exporta los productos filtrados a CSV', async () => {
    const created: Blob[] = [];
    vi.spyOn(URL, 'createObjectURL').mockImplementation((blob) => {
      created.push(blob as Blob);
      return 'blob:prueba';
    });
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    await openCatalog('ADMIN', '?marca=LG');
    const table = await productsTable();
    await waitFor(() => expect(keys(table)).toEqual(['MON-LG-27']));
    fireEvent.click(screen.getByRole('button', { name: 'Exportar CSV' }));
    expect(await screen.findByText('Exportación lista')).toBeInTheDocument();
    const lines = (await created[0].text()).replace(/^﻿/, '').trim().split('\r\n');
    expect(lines[0]).toBe(
      '"SKU";"Producto";"Categoría";"Marca";"Unidad";"Proveedor";"Costo";"Precio (IVA incl.)";"Margen";"Mínimo";"Máximo";"Disponible";"Posición";"Código de barras";"Activo";"Imagen";"Serie";"Garantía (meses)";"Plataformas"',
    );
    expect(lines.slice(1)).toEqual(['"MON-LG-27";"Monitor LG 27GP850";"Monitores";"LG";"UND";"Distribuidora Andina";2200;3200;0,2098;2;10;5;"CM-A1-01";"8806091234567";"Sí";"Sí";"Número de serie";12;']);
  });
});

describe('Catálogo · alta, edición y estado de un producto', SLOW, () => {
  it('nuevo producto: valida por campo y envía producto → imagen → ficha técnica con los contenidos EXACTOS', async () => {
    vi.stubGlobal('createImageBitmap', async () => ({ width: 640, height: 480, close: () => undefined }));
    const { server, location } = await openCatalog('ADMIN', '?nuevo=producto');
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo producto' }, { timeout: 5000 });
    await waitFor(() => expect(location()).toBe('/panel/catalogo'));
    fireEvent.click(within(dialog).getByRole('button', { name: 'Crear producto' }));
    expect(await within(dialog).findByText('Indique el SKU.')).toBeInTheDocument();
    expect(within(dialog).getByText('Elija la categoría.')).toBeInTheDocument();
    expect(within(dialog).getByRole('tab', { name: 'Precios (revisar)' })).toBeInTheDocument();
    expect(server.payloads('SaveProductCommand')).toHaveLength(0);

    type(dialog, /^SKU/, 'mon-aoc-24');
    type(dialog, /^Nombre/, 'Monitor AOC 24G2');
    choose(dialog, /^Categoría/, 'MON');
    choose(dialog, /^Proveedor preferido/, 'PRV-01');

    fireEvent.click(within(dialog).getByRole('tab', { name: /^Precios/ }));
    type(dialog, /^Costo unitario/, '1.200');
    choose(dialog, /^Aplicar un margen/, '30');
    await waitFor(() => expect(within(dialog).getByLabelText(/^Precio de venta/)).toHaveValue('1.970,40'));
    expect(within(dialog).getByText(/^Margen 30 % · ganancia Bs 514,25 por unidad/)).toBeInTheDocument();

    fireEvent.click(within(dialog).getByRole('tab', { name: 'Imágenes' }));
    const file = new File([new Uint8Array([1, 2, 3])], 'aoc.png', { type: 'image/png' });
    fireEvent.change(within(dialog).getByLabelText('Archivo de la imagen del producto'), { target: { files: [file] } });
    expect(await within(dialog).findByTestId('imagen-pendiente')).toHaveTextContent('aoc.png');

    fireEvent.click(within(dialog).getByRole('tab', { name: 'Ficha técnica' }));
    const size = await within(dialog).findByLabelText(/^Tamaño \(pulgadas\)/);
    fireEvent.change(size, { target: { value: '23,8' } });
    choose(dialog, /^Panel/, 'VA');
    fireEvent.click(within(dialog).getByRole('switch', { name: /Lleva serie o IMEI/ }));
    type(dialog, /^Meses de garantía/, '24');

    fireEvent.click(within(dialog).getByRole('tab', { name: 'Stock mínimo' }));
    fireEvent.change(within(dialog).getByRole('textbox', { name: /^Stock mínimo/ }), { target: { value: '2' } });
    fireEvent.change(within(dialog).getByRole('textbox', { name: /^Stock máximo/ }), { target: { value: '8' } });

    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Crear producto' }));
    });
    expect(await screen.findByText('Producto creado')).toBeInTheDocument();
    expect(server.order()).toEqual(['SaveProductCommand', 'SetProductImageCommand', 'SaveProductTechCommand']);
    expect(server.payloads('SaveProductCommand')).toEqual([
      {
        originalSku: null,
        sku: 'MON-AOC-24',
        name: 'Monitor AOC 24G2',
        description: null,
        categoryCode: 'MON',
        unitCode: 'UND',
        supplierCode: 'PRV-01',
        minimum: 2,
        maximum: 8,
        unitCost: 1200,
        salePrice: 1970.4,
        barcode: null,
        isActive: true,
        binCode: null,
      },
    ]);
    expect(server.payloads('SetProductImageCommand')).toEqual([{ sku: 'MON-AOC-24', content: 'AQID', contentType: 'image/png', fileName: 'aoc.png' }]);
    expect(server.payloads('SaveProductTechCommand')).toEqual([
      {
        sku: 'MON-AOC-24',
        trackSerials: true,
        serialKind: 'Serial',
        warrantyMonths: 24,
        specs: [
          { code: 'tamano', values: ['23.8'] },
          { code: 'panel', values: ['VA'] },
        ],
      },
    ]);
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Nuevo producto' })).not.toBeInTheDocument());
  });

  it('editar (desde `?editar=SKU`): SKU fijo; si la ficha falla, el error queda en el diálogo y el reintento corrige el MISMO producto', async () => {
    let techAttempts = 0;
    const { server } = await openCatalog('ADMIN', '?editar=MON-LG-27', {
      SaveProductTechCommand: () => {
        techAttempts += 1;
        if (techAttempts === 1) throw new WebApiError({ kind: 'domain', status: 422, message: 'La opción VA no es de la especificación Panel.' });
        return 'MON-LG-27';
      },
    });
    const dialog = await screen.findByRole('dialog', { name: 'Editar producto' }, { timeout: 5000 });
    expect(within(dialog).getByLabelText(/^SKU/)).toBeDisabled();
    expect(within(dialog).getByLabelText(/^Nombre/)).toHaveValue('Monitor LG 27GP850');
    fireEvent.click(within(dialog).getByRole('tab', { name: /^Precios/ }));
    type(dialog, /^Precio de venta/, '3.300');
    fireEvent.click(within(dialog).getByRole('tab', { name: 'Ficha técnica' }));
    const panel = await within(dialog).findByRole('combobox', { name: /^Panel/ });
    await waitFor(() => expect(panel).toHaveValue('IPS'));
    fireEvent.change(panel, { target: { value: 'VA' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar cambios' }));
    });
    expect(await within(dialog).findByText('El producto MON-LG-27 se guardó, pero la ficha técnica no: La opción VA no es de la especificación Panel.')).toBeInTheDocument();
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar cambios' }));
    });
    expect(await screen.findByText('Producto actualizado')).toBeInTheDocument();
    expect(server.order()).toEqual(['SaveProductCommand', 'SaveProductTechCommand', 'SaveProductCommand', 'SaveProductTechCommand']);
    const expected = {
      originalSku: 'MON-LG-27',
      sku: 'MON-LG-27',
      name: 'Monitor LG 27GP850',
      description: null,
      categoryCode: 'MON',
      unitCode: 'UND',
      supplierCode: 'PRV-01',
      minimum: 2,
      maximum: 10,
      unitCost: 2200,
      salePrice: 3300,
      barcode: '8806091234567',
      isActive: true,
      binCode: 'CM-A1-01',
    };
    expect(server.payloads('SaveProductCommand')).toEqual([expected, expected]);
    expect(server.payloads('SaveProductTechCommand')[1]).toEqual({
      sku: 'MON-LG-27',
      trackSerials: true,
      serialKind: 'Serial',
      warrantyMonths: 12,
      specs: [
        { code: 'tamano', values: ['27'] },
        { code: 'panel', values: ['VA'] },
      ],
    });
    // Sin cambios en la imagen, no se envía ningún comando de imagen.
    expect(server.payloads('SetProductImageCommand')).toEqual([]);
    expect(server.payloads('RemoveProductImageCommand')).toEqual([]);
  });

  it('quitar la imagen envía `RemoveProductImageCommand` después de guardar el producto', async () => {
    const { server } = await openCatalog('ADMIN', '?editar=MON-LG-27');
    const dialog = await screen.findByRole('dialog', { name: 'Editar producto' }, { timeout: 5000 });
    fireEvent.click(within(dialog).getByRole('tab', { name: 'Imágenes' }));
    fireEvent.click(await within(dialog).findByRole('button', { name: 'Quitar imagen' }));
    expect(within(dialog).getByText('La imagen actual se quita al guardar.')).toBeInTheDocument();
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar cambios' }));
    });
    expect(await screen.findByText('Producto actualizado')).toBeInTheDocument();
    expect(server.order()).toEqual(['SaveProductCommand', 'RemoveProductImageCommand']);
    expect(server.payloads('RemoveProductImageCommand')).toEqual([{ sku: 'MON-LG-27' }]);
  });

  it('desactivar pide confirmación y reenvía el producto con isActive=false; activar es directo', async () => {
    const { server } = await openCatalog();
    const table = await productsTable();
    fireEvent.click(within(await openRowMenu(table, 'MON-LG-27')).getByRole('menuitem', { name: 'Desactivar' }));
    const confirm = await screen.findByRole('alertdialog', { name: '¿Desactivar «Monitor LG 27GP850»?' });
    expect(server.payloads('SaveProductCommand')).toHaveLength(0);
    await act(async () => {
      fireEvent.click(within(confirm).getByRole('button', { name: 'Desactivar' }));
    });
    expect(await screen.findByText('Producto desactivado')).toBeInTheDocument();
    expect(server.payloads('SaveProductCommand')).toEqual([
      {
        originalSku: 'MON-LG-27',
        sku: 'MON-LG-27',
        name: 'Monitor LG 27GP850',
        description: null,
        categoryCode: 'MON',
        unitCode: 'UND',
        supplierCode: 'PRV-01',
        minimum: 2,
        maximum: 10,
        unitCost: 2200,
        salePrice: 3200,
        barcode: '8806091234567',
        isActive: false,
        binCode: 'CM-A1-01',
      },
    ]);
    fireEvent.click(within(await openRowMenu(table, 'CAB-HDMI')).getByRole('menuitem', { name: 'Activar' }));
    expect(await screen.findByText('Producto activado')).toBeInTheDocument();
    expect((server.payloads('SaveProductCommand')[1] as { isActive: boolean; sku: string }).isActive).toBe(true);
  });
});

describe('Catálogo · permisos', SLOW, () => {
  it('quien solo consulta el stock ve el catálogo pero no crea ni edita', async () => {
    await openCatalog('CONSULTA', '?nuevo=producto');
    const table = await productsTable();
    expect(screen.queryByRole('button', { name: 'Nuevo producto' })).not.toBeInTheDocument();
    expect(screen.queryByRole('dialog', { name: 'Nuevo producto' })).not.toBeInTheDocument();
    const menu = await openRowMenu(table, 'MON-LG-27');
    expect(within(menu).getAllByRole('menuitem').map((item) => item.textContent)).toEqual(['Ver detalle', 'Ver ficha y kardex']);
  });

  it('bodega (fichas técnicas sin `catalog.manage`) edita SOLO la ficha técnica', async () => {
    const { server } = await openCatalog('BODEGA');
    const table = await productsTable();
    expect(screen.queryByRole('button', { name: 'Nuevo producto' })).not.toBeInTheDocument();
    fireEvent.click(within(await openRowMenu(table, 'MON-LG-27')).getByRole('menuitem', { name: 'Editar ficha técnica' }));
    const dialog = await screen.findByRole('dialog', { name: 'Ficha técnica' });
    expect(within(dialog).queryByRole('tab')).not.toBeInTheDocument();
    const warranty = await within(dialog).findByLabelText(/^Meses de garantía/);
    fireEvent.change(warranty, { target: { value: '18' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar ficha técnica' }));
    });
    expect(await screen.findByText('Ficha técnica guardada')).toBeInTheDocument();
    expect(server.order()).toEqual(['SaveProductTechCommand']);
    expect(server.payloads('SaveProductTechCommand')).toEqual([
      {
        sku: 'MON-LG-27',
        trackSerials: true,
        serialKind: 'Serial',
        warrantyMonths: 18,
        specs: [
          { code: 'tamano', values: ['27'] },
          { code: 'panel', values: ['IPS'] },
        ],
      },
    ]);
  });
});
