// Módulo «Series» dentro del panel (con un registro PROPIO de la prueba: solo este módulo, así no depende de los demás
// módulos en construcción) y con un servidor de series SIMULADO en la prueba: la lista con el pedido exacto, los filtros
// del servidor y de la página en la dirección, el detalle con la trazabilidad y la garantía, registrar series (validación
// y comando exacto), dar destino (confirmación, comando exacto y error del servidor), «Consultar una serie», exportar,
// los estados de error, la estadística plegada y lo que ve cada rol. Ninguna prueba toca la red.

import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { WebApiError } from '@/1-domain/auth/errors';
import type { RpcOperationName, RpcResponseOf } from '@/4-presentation/app/contract';
import { ROLE_PERMISSIONS, renderPanel, signedInAs, type MockWeb, type StaffRole } from '@/test-utils';
import { buildRegistry, dashboardActions, dashboardStats } from '../../registry';
import { PanelApp } from '../../shell/PanelApp';
import series from './module';
import { SerialsByStatusStat } from './SerialsByStatusStat';

const REGISTRY = buildRegistry([{ source: '../modules/series/module.tsx', definition: series }]);

beforeAll(async () => {
  await import('./SerialsPage');
}, 30_000);

afterEach(() => {
  vi.restoreAllMocks();
});

type Serial = RpcResponseOf<'SearchSerialsQuery'>[number];
type Product = RpcResponseOf<'SearchTechProductsQuery'>[number];

function unit(overrides: Partial<Serial>): Serial {
  return {
    serial: 'SN-0001',
    kind: 'Serial',
    sku: 'NB-ASUS-01',
    product: 'Notebook ASUS TUF',
    status: 'InStock',
    branch: 'CM',
    warehouse: 'ALM-CM',
    receivedAt: '2026-09-01T14:00:00Z',
    soldAt: null,
    invoiceNumber: null,
    customer: null,
    warrantyUntil: null,
    ...overrides,
  };
}

function product(overrides: Partial<Product>): Product {
  return {
    sku: 'NB-ASUS-01',
    name: 'Notebook ASUS TUF',
    category: 'Notebooks',
    categoryCode: 'NOTEBOOKS',
    brand: 'ASUS',
    price: 9500,
    stock: 3,
    trackSerials: true,
    serialKind: 'Serial',
    warrantyMonths: 12,
    keySpecs: '',
    platforms: [],
    imageId: null,
    ...overrides,
  };
}

const SERIALS: Serial[] = [
  unit({ serial: 'SN-A100', status: 'InStock', receivedAt: '2026-09-25T14:00:00Z' }),
  unit({
    serial: '490154203237518',
    kind: 'Imei',
    sku: 'CEL-GALAXY',
    product: 'Celular Galaxy A55',
    status: 'Sold',
    branch: 'CB',
    warehouse: null,
    receivedAt: '2026-09-05T14:00:00Z',
    soldAt: '2026-09-10T15:00:00Z',
    invoiceNumber: 'F-CB-000045',
    customer: 'Mariana Céspedes',
    warrantyUntil: '2099-09-10',
  }),
  unit({ serial: 'SN-C300', status: 'InRma', warehouse: null, receivedAt: '2025-01-05T14:00:00Z', soldAt: '2025-01-10T15:00:00Z', invoiceNumber: 'F-CM-000010', customer: 'Luis Arce', warrantyUntil: '2000-01-10' }),
];

const PRODUCTS: Product[] = [
  product({ sku: 'NB-ASUS-01', name: 'Notebook ASUS TUF' }),
  product({ sku: 'CEL-GALAXY', name: 'Celular Galaxy A55', serialKind: 'Imei', stock: 5 }),
  product({ sku: 'MOUSE-01', name: 'Mouse gamer', trackSerials: false, stock: 10 }),
];

function traceOf(row: Serial): RpcResponseOf<'GetSerialTraceQuery'> {
  return {
    serial: row,
    supplier: 'Distribuidora Andina S.R.L.',
    receiptNumber: 'R-CM-000010',
    warrantyMonths: 12,
    inWarranty: row.warrantyUntil === '2099-09-10',
    events: [
      { occurredAt: row.receivedAt ?? '2026-09-01T14:00:00Z', action: 'Received', branch: row.branch, documentNumber: 'R-CM-000010', note: null, user: 'Bruno Mamani' },
      ...(row.soldAt ? [{ occurredAt: row.soldAt, action: 'Sold' as const, branch: row.branch, documentNumber: row.invoiceNumber, note: null, user: 'Diego Flores' }] : []),
      ...(row.status === 'InRma'
        ? [{ occurredAt: '2026-09-20T15:00:00Z', action: 'RmaReceived' as const, branch: 'CM', documentNumber: 'RMA-CM-000007', note: 'Reparación con cargo: No enciende', user: 'Carla Rojas' }]
        : []),
    ],
    claims:
      row.status === 'InRma'
        ? [
            {
              id: '00000000-0000-0000-0000-000000000007',
              number: 'RMA-CM-000007',
              branchCode: 'CM',
              serial: row.serial,
              sku: row.sku,
              product: row.product,
              customer: 'Luis Arce',
              issue: 'No enciende',
              status: 'Replaced',
              isInWarranty: false,
              warrantyUntil: row.warrantyUntil,
              receivedAt: '2026-09-20T15:00:00Z',
              closedAt: null,
              supplier: null,
              resolution: 'Reemplazo por falla de fábrica',
              replacementSerial: 'SN-A999',
              daysOpen: 9,
            },
          ]
        : [],
  };
}

function warrantyOf(row: Serial): RpcResponseOf<'GetWarrantyStatusQuery'> {
  return {
    serial: row.serial,
    sku: row.sku,
    product: row.product,
    status: row.status,
    soldOn: row.soldAt ? row.soldAt.slice(0, 10) : null,
    invoiceNumber: row.invoiceNumber,
    customerCode: row.customer ? 'C0002' : null,
    customer: row.customer,
    warrantyMonths: 12,
    warrantyUntil: row.warrantyUntil,
    inWarranty: row.warrantyUntil === '2099-09-10',
    openClaim: null,
  };
}

type Handler = (payload: never) => unknown;

/** Servidor de series en memoria: responde las operaciones del módulo y deja el resto al modo mock. */
function fakeServer(web: MockWeb, handlers: Partial<Record<RpcOperationName, Handler>> = {}) {
  const real = web.backend.rpc.call.bind(web.backend.rpc);
  const spy = vi.spyOn(web.backend.rpc, 'call').mockImplementation(async (operation, payload, sendOptions) => {
    const reply = (result: unknown) => ({ result: result as never, replayed: false, requestId: sendOptions?.requestId ?? 'prueba' });
    const custom = handlers[operation];
    if (custom) return reply(await custom(payload as never));
    const find = (serial: string) => SERIALS.find((row) => row.serial === serial.toUpperCase() || row.serial === serial);
    switch (operation) {
      case 'SearchSerialsQuery': {
        const request = payload as { status: string | null; sku: string | null; text: string | null };
        return reply(
          SERIALS.filter(
            (row) =>
              (!request.status || row.status === request.status) &&
              (!request.sku || row.sku === request.sku) &&
              (!request.text || row.serial.includes(request.text.toUpperCase()) || row.sku.includes(request.text.toUpperCase())),
          ),
        );
      }
      case 'SearchTechProductsQuery':
        return reply(PRODUCTS);
      case 'GetSerialTraceQuery': {
        const row = find((payload as { serial: string }).serial);
        if (!row) throw new WebApiError({ kind: 'not_found', status: 404, message: 'La serie no está registrada.' });
        return reply(traceOf(row));
      }
      case 'GetWarrantyStatusQuery': {
        const row = find((payload as { serial: string }).serial);
        if (!row) throw new WebApiError({ kind: 'not_found', status: 404, message: `La serie ${(payload as { serial: string }).serial} no está registrada.` });
        return reply(warrantyOf(row));
      }
      case 'GetSerialSummaryQuery':
        return reply({ total: 40, inStock: 25, inStockProducts: 6, sold: 10, soldInWarranty: 8, inRmaOrReturned: 3, out: 2 });
      case 'RegisterStockSerialsCommand':
        return reply('✔ 2 serie(s) registrada(s) para NB-ASUS-01: quedan 1 unidad(es) sin serie en la sucursal.');
      case 'DisposeSerialCommand':
        return reply('✔ Serie SN-C300: dada de baja.');
      default:
        return real(operation as never, payload as never, sendOptions);
    }
  });
  const payloads = (operation: RpcOperationName) => spy.mock.calls.filter(([name]) => name === operation).map(([, payload]) => payload);
  return { spy, payloads };
}

async function openSeries(role: StaffRole = 'ADMIN', query = '', handlers: Partial<Record<RpcOperationName, Handler>> = {}) {
  const web = await signedInAs(role);
  const server = fakeServer(web, handlers);
  const view = await renderPanel(<PanelApp registry={REGISTRY} />, { web: web.services, route: `/panel/series${query}`, path: '/panel/*' });
  return { web, server, location: view.location };
}

async function seriesTable(): Promise<HTMLElement> {
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

const IMEI_KEY = 'CEL-GALAXY|490154203237518';

describe('Series · lista', () => {
  it('muestra las series con el pedido exacto, lo más reciente primero, y no carga el resumen hasta abrirlo', async () => {
    const { server } = await openSeries();
    expect(await screen.findByRole('heading', { level: 1, name: 'Series' }, { timeout: 5000 })).toBeInTheDocument();
    const table = await seriesTable();
    expect(keys(table)).toEqual(['NB-ASUS-01|SN-A100', IMEI_KEY, 'NB-ASUS-01|SN-C300']);
    expect(server.payloads('SearchSerialsQuery')).toEqual([{ text: null, status: null, sku: null, max: 1000 }]);
    expect(server.payloads('SearchTechProductsQuery')).toEqual([{ text: null, categoryCode: null, platform: null, filters: null, onlyInStock: false, max: 2000 }]);
    expect(screen.getByTestId('series-resumen')).toHaveTextContent('3 de 3 series');
    expect(rowOf(table, IMEI_KEY)).toHaveTextContent('IMEI');
    expect(rowOf(table, IMEI_KEY)).toHaveTextContent('Mariana Céspedes');
    expect(rowOf(table, IMEI_KEY)).toHaveTextContent('Hasta 10/09/2099');
    expect(rowOf(table, 'NB-ASUS-01|SN-C300')).toHaveTextContent('Venció el 10/01/2000');
    expect(rowOf(table, 'NB-ASUS-01|SN-C300')).toHaveTextContent('En garantía (RMA)');

    // Resumen plegado: no consulta nada hasta abrirlo.
    expect(server.payloads('GetSerialSummaryQuery')).toEqual([]);
    const toggle = screen.getByRole('button', { name: /Ver resumen de las unidades/ });
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    fireEvent.click(toggle);
    const stat = await screen.findByTestId('series-por-estado');
    await waitFor(() => expect(stat).toHaveTextContent('En stock25de 6 productos'));
    expect(server.payloads('GetSerialSummaryQuery')).toEqual([{}]);
  });

  it('el estado, la búsqueda, el producto y la cantidad se piden al servidor y quedan en la dirección', async () => {
    const { server, location } = await openSeries();
    const table = await seriesTable();
    fireEvent.change(screen.getByRole('combobox', { name: 'Estado' }), { target: { value: 'InRma' } });
    await waitFor(() => expect(location()).toBe('/panel/series?estado=InRma'));
    await waitFor(() => expect(keys(table)).toEqual(['NB-ASUS-01|SN-C300']));
    expect(server.payloads('SearchSerialsQuery').at(-1)).toEqual({ text: null, status: 'InRma', sku: null, max: 1000 });

    fireEvent.click(screen.getByRole('button', { name: 'Limpiar filtros' }));
    await waitFor(() => expect(location()).toBe('/panel/series'));

    const search = screen.getByRole('searchbox', { name: 'Buscar' });
    fireEvent.change(search, { target: { value: 'sn-a1' } });
    fireEvent.keyDown(search, { key: 'Enter' });
    await waitFor(() => expect(location()).toBe('/panel/series?q=sn-a1'));
    await waitFor(() => expect(keys(table)).toEqual(['NB-ASUS-01|SN-A100']));
    expect(server.payloads('SearchSerialsQuery').at(-1)).toEqual({ text: 'sn-a1', status: null, sku: null, max: 1000 });
    fireEvent.click(screen.getByRole('button', { name: 'Limpiar filtros' }));

    // Producto: lista con búsqueda de los que llevan serie o IMEI.
    const productField = screen.getByRole('combobox', { name: 'Producto' });
    fireEvent.change(productField, { target: { value: 'galaxy' } });
    fireEvent.click(await screen.findByRole('option', { name: /Celular Galaxy A55/ }));
    await waitFor(() => expect(location()).toBe('/panel/series?producto=CEL-GALAXY'));
    await waitFor(() => expect(keys(table)).toEqual([IMEI_KEY]));
    expect(server.payloads('SearchSerialsQuery').at(-1)).toEqual({ text: null, status: null, sku: 'CEL-GALAXY', max: 1000 });

    fireEvent.change(screen.getByRole('combobox', { name: 'Series a revisar' }), { target: { value: '5000' } });
    await waitFor(() => expect(server.payloads('SearchSerialsQuery').at(-1)).toEqual({ text: null, status: null, sku: 'CEL-GALAXY', max: 5000 }));
  });

  it('la sucursal, el tipo y la garantía se filtran en la página (sin volver a consultar)', async () => {
    const { server, location } = await openSeries();
    const table = await seriesTable();
    fireEvent.change(screen.getByRole('combobox', { name: 'Sucursal' }), { target: { value: 'CB' } });
    await waitFor(() => expect(keys(table)).toEqual([IMEI_KEY]));
    fireEvent.change(screen.getByRole('combobox', { name: 'Sucursal' }), { target: { value: '' } });
    fireEvent.change(screen.getByRole('combobox', { name: 'Tipo' }), { target: { value: 'Serial' } });
    await waitFor(() => expect(keys(table)).toEqual(['NB-ASUS-01|SN-A100', 'NB-ASUS-01|SN-C300']));
    fireEvent.change(screen.getByRole('combobox', { name: 'Garantía' }), { target: { value: 'vencida' } });
    await waitFor(() => expect(location()).toBe('/panel/series?tipo=Serial&garantia=vencida'));
    await waitFor(() => expect(keys(table)).toEqual(['NB-ASUS-01|SN-C300']));
    expect(screen.getByTestId('series-resumen')).toHaveTextContent('1 de 3 series');
    expect(screen.getByTestId('filtros-activos')).toHaveTextContent('2 activos');
    expect(server.payloads('SearchSerialsQuery')).toHaveLength(1);
  });

  it('si la consulta falla muestra el error con «Reintentar» y se recupera', async () => {
    const web = await signedInAs('ADMIN');
    let fail = true;
    fakeServer(web, {
      SearchSerialsQuery: () => {
        if (fail) {
          fail = false;
          throw new WebApiError({ kind: 'network', message: 'Sin conexión' });
        }
        return SERIALS;
      },
    });
    await renderPanel(<PanelApp registry={REGISTRY} />, { web: web.services, route: '/panel/series', path: '/panel/*' });
    const error = await screen.findByTestId('estado-error', undefined, { timeout: 5000 });
    expect(error).toHaveTextContent('No se pudo cargar la información');
    fireEvent.click(within(error).getByRole('button', { name: 'Reintentar' }));
    await seriesTable();
  });

  it('exporta las filas filtradas a CSV con el aviso del archivo', async () => {
    const created: Blob[] = [];
    vi.spyOn(URL, 'createObjectURL').mockImplementation((blob) => {
      created.push(blob as Blob);
      return 'blob:prueba';
    });
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    await openSeries('ADMIN', '?tipo=Imei');
    const table = await seriesTable();
    await waitFor(() => expect(keys(table)).toEqual([IMEI_KEY]));
    fireEvent.click(screen.getByRole('button', { name: 'Exportar CSV' }));
    expect(await screen.findByText('Exportación lista')).toBeInTheDocument();
    const lines = (await created[0].text()).replace(/^﻿/, '').trim().split('\r\n');
    expect(lines[0]).toBe('"Serie o IMEI";"Tipo";"SKU";"Producto";"Estado";"Sucursal";"Almacén";"Ingresó";"Vendida";"Factura";"Cliente";"Garantía hasta";"Garantía"');
    expect(lines).toHaveLength(2);
    expect(lines[1]).toContain('"IMEI"');
    expect(lines[1]).toContain('"Garantía vigente"');
  });
});

describe('Series · detalle', () => {
  it('abre la trazabilidad y la garantía de una unidad vendida (pedidos exactos) con «Abrir caso de garantía»', async () => {
    const { server } = await openSeries();
    const table = await seriesTable();
    fireEvent.click(within(rowOf(table, IMEI_KEY)).getAllByRole('button')[0]);
    const panel = await screen.findByRole('dialog', { name: 'Serie 490154203237518' });
    expect(await within(panel).findByTestId('garantia-de-la-unidad')).toHaveTextContent('En garantía hasta el 10/09/2099');
    expect(server.payloads('GetSerialTraceQuery')).toEqual([{ serial: '490154203237518', sku: 'CEL-GALAXY' }]);
    expect(server.payloads('GetWarrantyStatusQuery')).toEqual([{ serial: '490154203237518', sku: 'CEL-GALAXY' }]);
    expect(panel).toHaveTextContent('Proveedor Distribuidora Andina S.R.L. · recepción R-CM-000010');
    await waitFor(() =>
      expect(within(panel).getByRole('link', { name: 'Abrir caso de garantía' })).toHaveAttribute('href', '/panel/garantias?abrir=1&serie=490154203237518&sku=CEL-GALAXY'),
    );
    expect(within(panel).getByRole('link', { name: 'Ver las ventas de Mariana Céspedes' })).toHaveAttribute('href', '/panel/ventas?cliente=C0002');
    expect(within(panel).queryByRole('button', { name: 'Dar destino' })).not.toBeInTheDocument();

    fireEvent.click(within(panel).getByRole('tab', { name: /^Trazabilidad/ }));
    const timeline = within(panel).getByRole('list', { name: 'Línea de tiempo de la unidad' });
    expect(within(timeline).getAllByRole('listitem').map((item) => item.textContent?.slice(0, 16))).toEqual(['Vendida10/09/202', 'Ingresó al stock']);
  });

  it('una unidad en garantía muestra su caso y «Dar destino»; «Ver solo este producto» filtra la lista', async () => {
    const { location } = await openSeries();
    const table = await seriesTable();
    fireEvent.click(within(rowOf(table, 'NB-ASUS-01|SN-C300')).getAllByRole('button')[0]);
    const panel = await screen.findByRole('dialog', { name: 'Serie SN-C300' });
    expect(await within(panel).findByTestId('garantia-de-la-unidad')).toHaveTextContent('Garantía vencida el 10/01/2000');
    expect(within(panel).getByRole('button', { name: 'Dar destino' })).toBeInTheDocument();
    expect(within(panel).queryByRole('link', { name: 'Abrir caso de garantía' })).not.toBeInTheDocument();
    fireEvent.click(within(panel).getByRole('tab', { name: /^Garantías/ }));
    expect(within(panel).getByRole('link', { name: 'Ver el caso' })).toHaveAttribute('href', '/panel/garantias?ver=RMA-CM-000007');
    fireEvent.click(within(panel).getByRole('button', { name: 'Ver solo las series de este producto' }));
    await waitFor(() => expect(location()).toBe('/panel/series?producto=NB-ASUS-01'));
  });

  it('`?ver=<serie>` (desde Garantías) abre el detalle de esa unidad; al cerrarlo se quita de la dirección', async () => {
    const { server, location } = await openSeries('ADMIN', '?q=SN-C300&producto=NB-ASUS-01&ver=SN-C300');
    const panel = await screen.findByRole('dialog', { name: 'Serie SN-C300' }, { timeout: 5000 });
    expect(await within(panel).findByTestId('garantia-de-la-unidad')).toHaveTextContent('Garantía vencida el 10/01/2000');
    expect(server.payloads('GetSerialTraceQuery')).toEqual([{ serial: 'SN-C300', sku: 'NB-ASUS-01' }]);
    // Sigue abierto (el panel se cierra al navegar: la dirección no cambia mientras está abierto).
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(screen.getByRole('dialog', { name: 'Serie SN-C300' })).toBeInTheDocument();
    expect(location()).toBe('/panel/series?q=SN-C300&producto=NB-ASUS-01&ver=SN-C300');
    fireEvent.click(within(panel).getByRole('button', { name: 'Cerrar' }));
    await waitFor(() => expect(location()).toBe('/panel/series?q=SN-C300&producto=NB-ASUS-01'));
  });
});

describe('Series · comandos', () => {
  it('registrar series: valida, avisa las series mal escritas y envía el comando exacto', async () => {
    const { server } = await openSeries('BODEGA');
    await seriesTable();
    fireEvent.click(screen.getByRole('button', { name: 'Registrar series de stock' }));
    const dialog = await screen.findByRole('dialog', { name: 'Registrar series de unidades en stock' });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Registrar series' }));
    expect(await within(dialog).findByText('Elija el producto.')).toBeInTheDocument();
    expect(within(dialog).getByText('Escanee, escriba o pegue al menos una serie.')).toBeInTheDocument();
    expect(server.payloads('RegisterStockSerialsCommand')).toEqual([]);

    // Producto con IMEI: un IMEI mal escrito se marca antes de enviar.
    const productField = within(dialog).getByRole('combobox', { name: 'Producto' });
    await waitFor(() => expect(productField).not.toBeDisabled());
    fireEvent.change(productField, { target: { value: 'galaxy' } });
    fireEvent.click(await within(dialog).findByRole('option', { name: /Celular Galaxy A55/ }));
    const serials = within(dialog).getByRole('textbox', { name: /^Series o IMEI/ });
    fireEvent.change(serials, { target: { value: '49-015420-323751-8\n490154203237519' } });
    expect(within(dialog).getByText('El IMEI «490154203237519» no es válido: debe tener 15 dígitos y el dígito verificador correcto.')).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Registrar series' }));
    expect(await within(dialog).findByText('Corrija las series marcadas.')).toBeInTheDocument();
    expect(server.payloads('RegisterStockSerialsCommand')).toEqual([]);

    // Producto con número de serie: una por línea (como las deja el lector de códigos).
    fireEvent.change(productField, { target: { value: 'asus' } });
    fireEvent.click(await within(dialog).findByRole('option', { name: /Notebook ASUS TUF/ }));
    fireEvent.change(serials, { target: { value: 'sn-n1\nsn-n2\n' } });
    expect(within(dialog).getByTestId('series-listas')).toHaveTextContent('2 series listas para registrar.');
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Registrar series' }));
    });
    await waitFor(() => expect(server.payloads('RegisterStockSerialsCommand')).toEqual([{ sku: 'NB-ASUS-01', serials: ['SN-N1', 'SN-N2'], note: 'Inventario inicial de series' }]));
    expect(await screen.findByText('Series registradas')).toBeInTheDocument();
    expect(screen.getByText('2 serie(s) registrada(s) para NB-ASUS-01: quedan 1 unidad(es) sin serie en la sucursal.')).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Registrar series de unidades en stock' })).not.toBeInTheDocument());
    expect(server.payloads('SearchSerialsQuery')).toHaveLength(2);
  });

  it('el error del servidor al registrar se muestra dentro del diálogo, sin cerrarlo', async () => {
    await openSeries('ADMIN', '', {
      RegisterStockSerialsCommand: () => {
        throw new WebApiError({ kind: 'domain', status: 422, message: 'NB-ASUS-01 tiene 1 unidad(es) en stock sin serie en la sucursal: no se pueden registrar 2 series.' });
      },
    });
    await seriesTable();
    fireEvent.click(screen.getByRole('button', { name: 'Registrar series de stock' }));
    const dialog = await screen.findByRole('dialog', { name: 'Registrar series de unidades en stock' });
    const productField = within(dialog).getByRole('combobox', { name: 'Producto' });
    await waitFor(() => expect(productField).not.toBeDisabled());
    fireEvent.change(productField, { target: { value: 'asus' } });
    fireEvent.click(await within(dialog).findByRole('option', { name: /Notebook ASUS TUF/ }));
    fireEvent.change(within(dialog).getByRole('textbox', { name: /^Series o IMEI/ }), { target: { value: 'X1\nX2' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Registrar series' }));
    });
    expect(await within(dialog).findByTestId('error-del-servidor')).toHaveTextContent('no se pueden registrar 2 series');
  });

  it('dar destino: pide el motivo, confirma y envía el comando exacto; recarga la lista', async () => {
    const { server } = await openSeries('GERENCIA');
    const table = await seriesTable();
    const menu = await openRowMenu(table, 'NB-ASUS-01|SN-C300');
    fireEvent.click(within(menu).getByRole('menuitem', { name: 'Dar destino' }));
    const dialog = await screen.findByRole('alertdialog', { name: '¿Dar destino a la serie SN-C300?' });
    expect(dialog).toHaveTextContent('No se puede deshacer.');
    fireEvent.click(within(dialog).getByRole('button', { name: 'Registrar destino' }));
    expect(await within(dialog).findByText('Elija el motivo o escriba uno.')).toBeInTheDocument();
    expect(server.payloads('DisposeSerialCommand')).toEqual([]);

    fireEvent.click(within(dialog).getByRole('radio', { name: 'Dar de baja' }));
    fireEvent.change(within(dialog).getByRole('combobox', { name: 'Motivo' }), { target: { value: 'otro' } });
    fireEvent.change(within(dialog).getByRole('textbox', { name: 'Escriba el motivo' }), { target: { value: 'Placa quemada' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Registrar destino' }));
    });
    await waitFor(() => expect(server.payloads('DisposeSerialCommand')).toEqual([{ serial: 'SN-C300', disposal: 'Scrap', reason: 'Placa quemada', sku: 'NB-ASUS-01' }]));
    expect(await screen.findByText('Destino registrado')).toBeInTheDocument();
    expect(screen.getByText('Serie SN-C300: dada de baja.')).toBeInTheDocument();
    await waitFor(() => expect(server.payloads('SearchSerialsQuery')).toHaveLength(2));
  });

  it('si el servidor no deja dar destino, el motivo se ve en la confirmación', async () => {
    await openSeries('ADMIN', '', {
      DisposeSerialCommand: () => {
        throw new WebApiError({ kind: 'domain', status: 422, message: 'La serie SN-C300 está en el caso RMA-CM-000007: resuélvala desde el caso RMA.' });
      },
    });
    const table = await seriesTable();
    fireEvent.click(within(await openRowMenu(table, 'NB-ASUS-01|SN-C300')).getByRole('menuitem', { name: 'Dar destino' }));
    const dialog = await screen.findByRole('alertdialog');
    fireEvent.change(within(dialog).getByRole('combobox', { name: 'Motivo' }), { target: { value: 'Equipo irreparable' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Registrar destino' }));
    });
    expect(await within(dialog).findByTestId('error-del-servidor')).toHaveTextContent('resuélvala desde el caso RMA');
    expect(screen.getByRole('alertdialog')).toBeInTheDocument();
  });
});

describe('Series · consultar una serie', () => {
  it('`?consultar=1` (tablero) abre la consulta: muestra la garantía y lleva a la trazabilidad', async () => {
    const { server, location } = await openSeries('VENTAS', '?consultar=1');
    const dialog = await screen.findByRole('dialog', { name: 'Consultar una serie' }, { timeout: 5000 });
    await waitFor(() => expect(location()).toBe('/panel/series'));
    const field = within(dialog).getByRole('textbox', { name: /^Serie o IMEI/ });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Consultar' }));
    expect(await within(dialog).findByText('Escanee o escriba la serie o el IMEI.')).toBeInTheDocument();

    fireEvent.change(field, { target: { value: 'NO-EXISTE' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Consultar' }));
    expect(await within(dialog).findByText('No se encontró la unidad')).toBeInTheDocument();

    fireEvent.change(field, { target: { value: '490154203237518' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Consultar' }));
    const result = await within(dialog).findByTestId('resultado-consulta');
    expect(result).toHaveTextContent('En garantía hasta el 10/09/2099');
    expect(result).toHaveTextContent('F-CB-000045');
    expect(server.payloads('GetWarrantyStatusQuery')).toEqual([
      { serial: 'NO-EXISTE', sku: null },
      { serial: '490154203237518', sku: null },
    ]);
    // Ventas puede abrir casos de garantía.
    expect(within(result).getByRole('link', { name: 'Abrir caso de garantía' })).toHaveAttribute('href', '/panel/garantias?abrir=1&serie=490154203237518&sku=CEL-GALAXY');

    fireEvent.click(within(result).getByRole('button', { name: 'Ver la trazabilidad' }));
    expect(await screen.findByRole('dialog', { name: 'Serie 490154203237518' })).toBeInTheDocument();
    await waitFor(() => expect(server.payloads('GetSerialTraceQuery')).toEqual([{ serial: '490154203237518', sku: 'CEL-GALAXY' }]));
  });
});

describe('Series · permisos y tablero', () => {
  it('consulta (solo ver series) no ve registrar, dar destino ni abrir casos', async () => {
    await openSeries('CONSULTA');
    const table = await seriesTable();
    expect(screen.queryByRole('button', { name: 'Registrar series de stock' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Consultar una serie' })).toBeInTheDocument();
    const menu = await openRowMenu(table, 'NB-ASUS-01|SN-C300');
    expect(within(menu).getByRole('menuitem', { name: 'Ver detalle' })).toBeInTheDocument();
    expect(within(menu).queryByRole('menuitem', { name: 'Dar destino' })).not.toBeInTheDocument();
    fireEvent.keyDown(menu, { key: 'Escape' });
    const sold = await openRowMenu(table, IMEI_KEY);
    expect(within(sold).queryByRole('menuitem', { name: 'Abrir caso de garantía' })).not.toBeInTheDocument();
  });

  it('ventas abre casos de garantía pero no registra series ni da destino', async () => {
    await openSeries('VENTAS');
    const table = await seriesTable();
    expect(screen.queryByRole('button', { name: 'Registrar series de stock' })).not.toBeInTheDocument();
    const menu = await openRowMenu(table, IMEI_KEY);
    expect(within(menu).getByRole('menuitem', { name: 'Abrir caso de garantía' })).toBeInTheDocument();
    fireEvent.keyDown(menu, { key: 'Escape' });
    const rma = await openRowMenu(table, 'NB-ASUS-01|SN-C300');
    expect(within(rma).queryByRole('menuitem', { name: 'Dar destino' })).not.toBeInTheDocument();
  });

  it('el tablero ofrece «Consultar una serie» y la estadística «Unidades por estado» a quien ve series', () => {
    const offered = (role: StaffRole) => dashboardActions(REGISTRY, ROLE_PERMISSIONS[role]).flatMap((group) => group.actions.map((item) => item.to));
    expect(offered('CONSULTA')).toEqual(['/panel/series?consultar=1']);
    expect(dashboardStats(REGISTRY, ROLE_PERMISSIONS.BODEGA).map((item) => item.stat.title)).toEqual(['Unidades por estado']);
  });

  it('la estadística resume todas las series con su propio estado de error', async () => {
    const web = await signedInAs('GERENCIA');
    fakeServer(web);
    await renderPanel(<SerialsByStatusStat />, { web: web.services });
    const stat = await screen.findByTestId('series-por-estado');
    await waitFor(() => expect(stat).toHaveTextContent('Vendidas con garantía vigente8de 10 vendidas'));
    expect(stat).toHaveTextContent('Unidades por estado (40 en total)');
    expect(within(stat).getByRole('link', { name: 'Ver las unidades en garantía' })).toHaveAttribute('href', '/panel/series?estado=InRma');
  });

  it('si la estadística falla, muestra su error con «Reintentar»', async () => {
    const web = await signedInAs('ADMIN');
    vi.spyOn(web.backend.rpc, 'call').mockRejectedValue(new WebApiError({ kind: 'server', status: 500, message: 'Falla' }));
    await renderPanel(<SerialsByStatusStat />, { web: web.services });
    expect(await screen.findByTestId('estado-error')).toHaveTextContent('No se pudo cargar la información');
    expect(screen.getByRole('button', { name: 'Reintentar' })).toBeInTheDocument();
  });
});
