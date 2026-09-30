// Módulo «Catálogo» · pestañas «Categorías» y «Especificaciones», la estadística del tablero y la definición del módulo,
// con un servidor SIMULADO en la prueba: nueva categoría (código sugerido), subcategoría (con su madre), cambio de nombre,
// «Ver sus productos»; especificaciones con las heredadas, filtros, crear y modificar con los contenidos EXACTOS; quién ve
// cada botón. Ninguna prueba toca la red.

import { act, configure, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { WebApiError } from '@/1-domain/auth/errors';
import type { RpcOperationName, RpcResponseOf } from '@/4-presentation/app/contract';
import { renderPanel, signedInAs, type MockWeb, type StaffRole } from '@/test-utils';
import { buildRegistry, dashboardActions } from '../../registry';
import { PanelApp } from '../../shell/PanelApp';
import { CatalogStat } from './CatalogStat';
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
});

type Definition = RpcResponseOf<'GetSpecDefinitionsQuery'>[number];
type Product = RpcResponseOf<'GetCatalogQuery'>[number];

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

function product(sku: string, categoryCode: string, overrides: Partial<Product> = {}): Product {
  return {
    variantId: `v-${sku}`,
    sku,
    name: sku,
    description: null,
    categoryCode,
    category: categoryCode,
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

const OPTIONS: RpcResponseOf<'GetCatalogOptionsQuery'> = {
  categories: [
    { code: 'CPU', name: 'Procesadores' },
    { code: 'MON', name: 'Monitores' },
    { code: 'PER', name: 'Periféricos' },
  ],
  units: [{ code: 'UND', name: 'Unidad', allowsDecimals: false }],
  suppliers: [],
  taxRate: 13,
  priceListName: 'GENERAL',
  vatOnInvoicedAmount: true,
};

const CATALOG: RpcResponseOf<'GetCatalogQuery'> = [
  product('MON-1', 'MON', { hasImage: true }),
  product('MON-2', 'MON', { salePrice: 0 }),
  product('CPU-1', 'CPU', { salePrice: 1000, unitCost: 900 }),
];

const SOCKET = definition({ code: 'socket', name: 'Socket', categoryCode: 'CPU', categoryName: 'Procesadores', options: ['AM5', 'LGA1700'], compatibilityKey: 'cpu_socket' });
const SIZE = definition({ code: 'tamano', name: 'Tamaño', unit: 'pulgadas', dataType: 'Number', isRequired: true });
const PANEL = definition({ code: 'panel', name: 'Panel', options: ['IPS', 'VA'], sortOrder: 20 });
const ALL: Definition[] = [SOCKET, SIZE, PANEL];

type Handler = (payload: unknown) => unknown;

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
      case 'GetCatalogQuery':
        return reply(CATALOG.filter((row) => !body.categoryCode || row.categoryCode === body.categoryCode));
      case 'SearchTechProductsQuery':
        return reply([]);
      case 'GetSpecDefinitionsQuery':
        // Con una categoría, el servidor agrega las heredadas de sus madres (aquí: MON hereda el socket de CPU).
        return reply(body.categoryCode === null ? ALL : body.categoryCode === 'MON' ? [{ ...SOCKET, isInherited: true }, SIZE, PANEL] : ALL.filter((item) => item.categoryCode === body.categoryCode));
      case 'GetSpecFacetsQuery':
        return reply([]);
      case 'SaveCategoryCommand':
        return reply(String(body.code));
      case 'SaveSpecDefinitionCommand':
        return reply(String(body.code));
      default:
        return real(operation as never, payload as never, options);
    }
  });
  const payloads = (operation: RpcOperationName) => spy.mock.calls.filter(([name]) => name === operation).map(([, payload]) => payload);
  return { spy, payloads };
}

async function openCatalog(role: StaffRole = 'ADMIN', query = '', handlers: Partial<Record<RpcOperationName, Handler>> = {}) {
  const web = await signedInAs(role);
  const server = fakeServer(web, handlers);
  const view = await renderPanel(<PanelApp registry={REGISTRY} />, { web: web.services, route: `/panel/catalogo${query}`, path: '/panel/*' });
  return { web, server, location: view.location };
}

async function table(): Promise<HTMLElement> {
  const found = await screen.findByTestId('tabla', undefined, { timeout: 5000 });
  await waitFor(() => expect(within(found).queryAllByRole('row').filter((row) => row.hasAttribute('data-row-key')).length).toBeGreaterThan(0));
  return found;
}

function keys(tableElement: HTMLElement): (string | null)[] {
  return within(tableElement)
    .getAllByRole('row')
    .filter((row) => row.hasAttribute('data-row-key'))
    .map((row) => row.getAttribute('data-row-key'));
}

function rowOf(tableElement: HTMLElement, key: string): HTMLElement {
  const row = within(tableElement)
    .getAllByRole('row')
    .find((item) => item.getAttribute('data-row-key') === key);
  if (!row) throw new Error(`No está la fila ${key}`);
  return row;
}

async function menuOf(tableElement: HTMLElement, key: string): Promise<HTMLElement> {
  fireEvent.click(within(rowOf(tableElement, key)).getByRole('button', { name: /^Acciones de / }));
  return screen.findByTestId('menu-acciones');
}

describe('Catálogo · categorías', SLOW, () => {
  it('lista las categorías con sus productos y especificaciones propias, y filtra (prefijo `c_` en la dirección)', async () => {
    const { location } = await openCatalog('ADMIN', '?pestana=categorias');
    const grid = await table();
    expect(keys(grid)).toEqual(['MON', 'PER', 'CPU']);
    // Nombre, código, productos propios, activos y especificaciones propias.
    expect(rowOf(grid, 'MON')).toHaveTextContent('MonitoresMON222');
    fireEvent.change(screen.getByRole('combobox', { name: 'Productos' }), { target: { value: 'sin' } });
    await waitFor(() => expect(keys(grid)).toEqual(['PER']));
    expect(location()).toBe('/panel/catalogo?pestana=categorias&c_productos=sin');
  });

  it('nueva categoría con el código sugerido del nombre, subcategoría con su madre y cambio de nombre (contenidos EXACTOS)', async () => {
    const { server } = await openCatalog('ADMIN', '?pestana=categorias');
    const grid = await table();
    fireEvent.click(screen.getByRole('button', { name: 'Nueva categoría' }));
    let dialog = await screen.findByRole('dialog', { name: 'Nueva categoría' });
    fireEvent.change(within(dialog).getByLabelText(/^Nombre/), { target: { value: 'Periféricos gamer' } });
    expect(within(dialog).getByLabelText(/^Código/)).toHaveValue('PER2');
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Crear categoría' }));
    });
    expect(await screen.findByText('Categoría creada')).toBeInTheDocument();
    expect(server.payloads('SaveCategoryCommand')).toEqual([{ code: 'PER2', name: 'Periféricos gamer', parentCode: null }]);

    fireEvent.click(within(await menuOf(grid, 'MON')).getByRole('menuitem', { name: 'Nueva subcategoría' }));
    dialog = await screen.findByRole('dialog', { name: 'Nueva subcategoría de Monitores' });
    expect(within(dialog).getByRole('combobox', { name: 'Categoría madre' })).toHaveValue('MON');
    fireEvent.change(within(dialog).getByLabelText(/^Nombre/), { target: { value: 'Monitores curvos' } });
    fireEvent.change(within(dialog).getByLabelText(/^Código/), { target: { value: 'mon-curvo' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Crear categoría' }));
    });
    expect(await screen.findByText('Subcategoría creada')).toBeInTheDocument();

    fireEvent.click(within(await menuOf(grid, 'MON')).getByRole('menuitem', { name: 'Cambiar el nombre' }));
    dialog = await screen.findByRole('dialog', { name: 'Cambiar el nombre de «Monitores»' });
    expect(within(dialog).queryByLabelText(/^Código/)).not.toBeInTheDocument();
    fireEvent.change(within(dialog).getByLabelText(/^Nombre/), { target: { value: 'Monitores y pantallas' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar nombre' }));
    });
    expect(await screen.findByText('Categoría actualizada')).toBeInTheDocument();
    expect(server.payloads('SaveCategoryCommand')).toEqual([
      { code: 'PER2', name: 'Periféricos gamer', parentCode: null },
      { code: 'MON-CURVO', name: 'Monitores curvos', parentCode: 'MON' },
      { code: 'MON', name: 'Monitores y pantallas', parentCode: null },
    ]);
  });

  it('un código repetido se avisa en el formulario y un rechazo del servidor se muestra dentro del diálogo', async () => {
    const { server } = await openCatalog('ADMIN', '?nuevo=categoria', {
      SaveCategoryCommand: () => {
        throw new WebApiError({ kind: 'domain', status: 422, message: 'El código de la categoría solo admite letras, números, guion y guion bajo (sin espacios).' });
      },
    });
    const dialog = await screen.findByRole('dialog', { name: 'Nueva categoría' }, { timeout: 5000 });
    fireEvent.change(within(dialog).getByLabelText(/^Nombre/), { target: { value: 'Monitores' } });
    fireEvent.change(within(dialog).getByLabelText(/^Código/), { target: { value: 'MON' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Crear categoría' }));
    expect(await within(dialog).findByText('Ya existe una categoría con el código MON.')).toBeInTheDocument();
    expect(server.payloads('SaveCategoryCommand')).toHaveLength(0);
    fireEvent.change(within(dialog).getByLabelText(/^Código/), { target: { value: 'MONX' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Crear categoría' }));
    });
    expect(await within(dialog).findByText(/solo admite letras, números/)).toBeInTheDocument();
  });

  it('«Ver sus productos» lleva a la pestaña «Productos» filtrada por la categoría (sin abrir el detalle)', async () => {
    const { location } = await openCatalog('ADMIN', '?pestana=categorias');
    const grid = await table();
    fireEvent.click(within(await menuOf(grid, 'MON')).getByRole('menuitem', { name: 'Ver sus productos' }));
    await waitFor(() => expect(location()).toBe('/panel/catalogo?categoria=MON'));
    expect(screen.queryByTestId('detalle-categoria')).not.toBeInTheDocument();
    const products = await table();
    await waitFor(() => expect(keys(products)).toEqual(['MON-1', 'MON-2']));
  });
});

describe('Catálogo · especificaciones', SLOW, () => {
  it('lista todas o las de una categoría CON las heredadas; filtra por tipo y uso', async () => {
    const { server, location } = await openCatalog('ADMIN', '?pestana=especificaciones');
    const grid = await table();
    expect(keys(grid)).toEqual(['MON|tamano', 'MON|panel', 'CPU|socket']);
    fireEvent.change(screen.getByRole('combobox', { name: 'Tipo' }), { target: { value: 'Number' } });
    await waitFor(() => expect(keys(grid)).toEqual(['MON|tamano']));
    fireEvent.click(screen.getByRole('button', { name: 'Limpiar filtros' }));
    fireEvent.change(screen.getByRole('combobox', { name: 'Categoría' }), { target: { value: 'MON' } });
    await waitFor(() => expect(server.payloads('GetSpecDefinitionsQuery')).toContainEqual({ categoryCode: 'MON' }));
    await waitFor(() => expect(keys(grid)).toEqual(['MON|tamano', 'MON|panel', 'CPU|socket']));
    expect(rowOf(grid, 'CPU|socket')).toHaveTextContent('Heredada');
    fireEvent.change(screen.getByRole('combobox', { name: 'Uso' }), { target: { value: 'armador' } });
    await waitFor(() => expect(keys(grid)).toEqual(['CPU|socket']));
    expect(location()).toBe('/panel/catalogo?pestana=especificaciones&e_categoria=MON&e_uso=armador');
  });

  it('nueva especificación con el código sugerido y sus opciones; modificar deja fijos el código y el tipo (contenidos EXACTOS)', async () => {
    const { server } = await openCatalog('ADMIN', '?pestana=especificaciones&e_categoria=MON');
    const grid = await table();
    fireEvent.click(screen.getByRole('button', { name: 'Nueva especificación' }));
    let dialog = await screen.findByRole('dialog', { name: 'Nueva especificación' });
    expect(within(dialog).getByRole('combobox', { name: /^Categoría/ })).toHaveValue('MON');
    fireEvent.change(within(dialog).getByLabelText(/^Nombre/), { target: { value: 'Tipo de conexión' } });
    expect(within(dialog).getByLabelText(/^Código/)).toHaveValue('tipo_de_conexion');
    fireEvent.click(within(dialog).getByRole('radio', { name: /^Opción/ }));
    fireEvent.click(within(dialog).getByRole('checkbox', { name: /Admite varios valores/ }));
    fireEvent.click(within(dialog).getByRole('button', { name: 'Crear especificación' }));
    expect(await within(dialog).findByText('Escriba al menos una opción (una por renglón).')).toBeInTheDocument();
    fireEvent.change(within(dialog).getByLabelText(/^Opciones/), { target: { value: 'HDMI\nDisplayPort\n\nUSB-C' } });
    fireEvent.change(within(dialog).getByRole('combobox', { name: /Clave del armador/ }), { target: { value: 'cpu_socket' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Crear especificación' }));
    });
    expect(await screen.findByText('Especificación creada')).toBeInTheDocument();
    expect(server.payloads('SaveSpecDefinitionCommand')).toEqual([
      {
        categoryCode: 'MON',
        code: 'tipo_de_conexion',
        name: 'Tipo de conexión',
        unit: null,
        dataType: 'Option',
        isMultiValued: true,
        isFilterable: true,
        isRequired: false,
        compatibilityKey: 'cpu_socket',
        sortOrder: 30,
        options: ['HDMI', 'DisplayPort', 'USB-C'],
      },
    ]);

    fireEvent.click(within(await menuOf(grid, 'MON|panel')).getByRole('menuitem', { name: 'Modificar' }));
    dialog = await screen.findByRole('dialog', { name: 'Modificar «Panel»' });
    expect(within(dialog).getByLabelText(/^Código/)).toBeDisabled();
    expect(within(dialog).getByRole('radio', { name: /^Opción/ })).toBeDisabled();
    fireEvent.change(within(dialog).getByLabelText(/^Opciones/), { target: { value: 'IPS\nVA\nOLED' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar cambios' }));
    });
    expect(await screen.findByText('Especificación guardada')).toBeInTheDocument();
    expect(server.payloads('SaveSpecDefinitionCommand')[1]).toEqual({
      categoryCode: 'MON',
      code: 'panel',
      name: 'Panel',
      unit: null,
      dataType: 'Option',
      isMultiValued: false,
      isFilterable: true,
      isRequired: false,
      compatibilityKey: null,
      sortOrder: 20,
      options: ['IPS', 'VA', 'OLED'],
    });
  });

  it('bodega administra especificaciones pero no crea categorías; consulta no ve ninguno de los dos', async () => {
    await openCatalog('BODEGA', '?pestana=especificaciones');
    await table();
    expect(screen.getByRole('button', { name: 'Nueva especificación' })).toBeInTheDocument();
    fireEvent.click(screen.getByRole('tab', { name: /Categorías/ }));
    await table();
    expect(screen.queryByRole('button', { name: 'Nueva categoría' })).not.toBeInTheDocument();
  });

  it('consulta ve las especificaciones sin «Modificar»', async () => {
    await openCatalog('CONSULTA', '?pestana=especificaciones');
    const grid = await table();
    expect(screen.queryByRole('button', { name: 'Nueva especificación' })).not.toBeInTheDocument();
    const menu = await menuOf(grid, 'MON|panel');
    expect(within(menu).getAllByRole('menuitem').map((item) => item.textContent)).toEqual(['Ver detalle']);
  });
});

describe('Catálogo · tablero y definición', SLOW, () => {
  it('la estadística «Estado del catálogo» consulta al abrirse y enlaza al catálogo filtrado', async () => {
    const web = await signedInAs('ADMIN');
    const server = fakeServer(web);
    await renderPanel(<CatalogStat />, { web: web.services });
    const stat = await screen.findByTestId('estado-del-catalogo');
    await waitFor(() => expect(stat).toHaveTextContent(/Productos activos\s*3/));
    expect(stat).toHaveTextContent(/Sin imagen\s*2/);
    expect(stat).toHaveTextContent(/Activos sin precio\s*1/);
    expect(stat).toHaveTextContent(/Margen bajo\s*1/);
    expect(within(stat).getByRole('link', { name: 'Ver los productos sin imagen' })).toHaveAttribute('href', '/panel/catalogo?imagen=sin');
    expect(server.payloads('GetCatalogQuery')).toEqual([{ categoryCode: null, specFilters: null }]);
  });

  it('la definición del módulo: sección, permisos, acciones del tablero por permiso y la estadística', () => {
    expect(catalogo).toMatchObject({ key: 'catalogo', section: 'inventario', order: 20, permissions: { all: ['inventory.stock.view'] } });
    expect(catalogo.actions?.map((action) => [action.key, action.to, action.permissions ?? null])).toEqual([
      ['nuevo', '?nuevo=producto', { all: ['catalog.manage'] }],
      ['buscar', '', null],
    ]);
    const labels = (permissions: string[]) => dashboardActions(REGISTRY, permissions).flatMap((group) => group.actions.map((item) => `${item.action.label} → ${item.to}`));
    expect(labels(['inventory.stock.view'])).toEqual(['Buscar en el catálogo → /panel/catalogo']);
    expect(labels(['inventory.stock.view', 'catalog.manage'])).toEqual(['Nuevo producto → /panel/catalogo?nuevo=producto', 'Buscar en el catálogo → /panel/catalogo']);
    expect(labels(['catalog.manage'])).toEqual([]);
    expect(catalogo.stats?.map((stat) => stat.title)).toEqual(['Estado del catálogo']);
  });
});
