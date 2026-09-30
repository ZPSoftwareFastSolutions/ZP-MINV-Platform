// Módulo «Órdenes de compra» dentro del panel real, con un registro que tiene SOLO este módulo (las pruebas no dependen
// de los demás módulos en construcción) y un servidor de compras SIMULADO en la prueba (el modo mock no atiende estas
// operaciones): lista con filtros en la dirección, resumen plegado, detalle, orden nueva con validación y el pedido exacto,
// aprobar y anular con confirmación (el menú «⋯» no abre el detalle), recibir con IMEI (y el error del servidor dentro
// del diálogo), desde el pedido sugerido, facturas de proveedores, estados de error y lo que ve cada rol. Ninguna prueba
// toca la red. La fecha queda fija: 29/09/2026 en La Paz.

import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { WebApiError } from '@/1-domain/auth/errors';
import { permissionName, type RpcOperationName, type RpcResponseOf } from '@/4-presentation/app/contract';
import { ROLE_PERMISSIONS, renderPanel, signedInAs, type MockWeb, type StaffRole } from '@/test-utils';
import { buildRegistry, dashboardActions, dashboardStats } from '../../registry';
import { PanelApp } from '../../shell/PanelApp';
import compras from './module';
import { PurchasesStat } from './PurchasesStat';

const REGISTRY = buildRegistry([{ source: '../modules/compras/module.tsx', definition: compras }]);
const NOW = new Date('2026-09-29T14:00:00Z');

beforeAll(async () => {
  await import('../../shell/PanelLayout');
  await import('./PurchaseOrdersPage');
}, 30_000);

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(NOW);
});

afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
});

// ---------------------------------------------------------------------------------------------------- servidor simulado

type Order = RpcResponseOf<'GetPurchaseOrdersQuery'>[number];

function order(overrides: Partial<Order>): Order {
  return {
    id: 'o0',
    number: 'OC-CM-000000',
    supplierCode: 'P001',
    supplier: 'Distribuidora Andina',
    orderDate: '2026-09-01',
    expectedDate: null,
    status: 'Draft',
    lines: 1,
    total: 100,
    receivedPercent: 0,
    notes: null,
    ...overrides,
  };
}

const ORDERS: Order[] = [
  order({ id: 'o3', number: 'OC-CM-000003', orderDate: '2026-09-25', expectedDate: '2026-10-02', status: 'Draft', lines: 2, total: 1500, notes: 'Campaña de fin de mes' }),
  order({ id: 'o2', number: 'OC-CB-000002', supplierCode: 'P002', supplier: 'Tecno Import', orderDate: '2026-09-20', expectedDate: '2026-09-22', status: 'PartiallyReceived', total: 3000, receivedPercent: 33 }),
  order({ id: 'o1', number: 'OC-CM-000001', orderDate: '2026-09-05', expectedDate: '2026-09-10', status: 'Received', total: 800, receivedPercent: 100 }),
];

const DETAILS: Record<string, RpcResponseOf<'GetPurchaseOrderQuery'>> = {
  o3: {
    order: ORDERS[0],
    receipts: [],
    lines: [
      { lineId: 'l31', sku: 'MOU-01', name: 'Mouse Logitech G502', unit: 'UND', quantity: 10, unitCost: 90, subtotal: 900, received: 0 },
      { lineId: 'l32', sku: 'TEC-01', name: 'Teclado mecánico', unit: 'UND', quantity: 5, unitCost: 120, subtotal: 600, received: 0 },
    ],
  },
  o2: {
    order: ORDERS[1],
    receipts: ['RC-CB-000001'],
    lines: [{ lineId: 'l21', sku: 'CEL-01', name: 'Celular Samsung A55', unit: 'UND', quantity: 3, unitCost: 1000, subtotal: 3000, received: 1 }],
  },
};

const SUPPLIERS: RpcResponseOf<'GetSuppliersQuery'> = [
  { code: 'P001', name: 'Distribuidora Andina', taxId: '1020304', leadTimeDays: 3, contact: null, phone: null, email: null, isActive: true, products: 2, openOrders: 1, purchased: 800 },
  { code: 'P002', name: 'Tecno Import', taxId: null, leadTimeDays: 7, contact: null, phone: null, email: null, isActive: true, products: 1, openOrders: 1, purchased: 1000 },
  { code: 'P003', name: 'Proveedor inactivo', taxId: null, leadTimeDays: 5, contact: null, phone: null, email: null, isActive: false, products: 0, openOrders: 0, purchased: 0 },
];

function product(sku: string, name: string, supplierCode: string, unitCost: number): RpcResponseOf<'GetCatalogQuery'>[number] {
  return {
    barcode: null,
    binCode: null,
    category: 'Periféricos',
    categoryCode: 'PER',
    description: null,
    hasImage: false,
    isActive: true,
    maximum: 20,
    minimum: 8,
    name,
    salePrice: unitCost * 2,
    sku,
    supplier: null,
    supplierCode,
    unit: 'UND',
    unitCost,
    variantId: `v-${sku}`,
  };
}

const CATALOG = [product('MOU-01', 'Mouse Logitech G502', 'P001', 90), product('TEC-01', 'Teclado mecánico', 'P001', 120), product('CEL-01', 'Celular Samsung A55', 'P002', 1000)];

const OPTIONS: RpcResponseOf<'GetCatalogOptionsQuery'> = {
  categories: [],
  priceListName: 'General',
  suppliers: [],
  taxRate: 13,
  units: [{ code: 'UND', name: 'Unidad', allowsDecimals: false }],
  vatOnInvoicedAmount: false,
};

const TECH = [
  {
    sku: 'CEL-01',
    name: 'Celular Samsung A55',
    categoryCode: 'CEL',
    category: 'Celulares',
    brand: null,
    price: 2000,
    stock: 1,
    trackSerials: true,
    warrantyMonths: 12,
    keySpecs: '',
    platforms: [],
    imageId: null,
    serialKind: 'Imei',
  },
] as RpcResponseOf<'SearchTechProductsQuery'>;

const PENDING: RpcResponseOf<'GetReceiptsWithoutInvoiceQuery'> = [
  { receiptNumber: 'RC-CB-000001', receivedOn: '2026-09-21', supplierCode: 'P002', supplier: 'Tecno Import', total: 870 },
];

const INVOICES: RpcResponseOf<'GetSupplierInvoicesQuery'> = [
  {
    id: 'f1',
    number: '4521',
    supplierCode: 'P001',
    supplier: 'Distribuidora Andina',
    supplierNit: '1020304',
    invoiceDate: '2026-09-06',
    authorizationCode: 'CUF-ABC',
    totalAmount: 919.54,
    taxBase: 919.54,
    taxCredit: 119.54,
    status: 'Contabilizada',
    receiptNumber: 'RC-CM-000001',
    branchCode: 'CM',
  },
];

type Handler = (payload: unknown) => unknown;

function fakeServer(web: MockWeb, handlers: Partial<Record<RpcOperationName, Handler>> = {}) {
  const real = web.backend.rpc.call.bind(web.backend.rpc);
  const spy = vi.spyOn(web.backend.rpc, 'call').mockImplementation(async (operation, payload, options) => {
    const reply = (result: unknown) => ({ result: result as never, replayed: false, requestId: options?.requestId ?? 'prueba' });
    const custom = handlers[operation];
    if (custom) return reply(await custom(payload));
    switch (operation) {
      case 'GetPurchaseOrdersQuery':
        return reply(ORDERS);
      case 'GetPurchaseOrderQuery':
        return reply(DETAILS[(payload as { id: string }).id]);
      case 'GetSuppliersQuery':
        return reply(SUPPLIERS);
      case 'GetCatalogQuery':
        return reply(CATALOG);
      case 'GetCatalogOptionsQuery':
        return reply(OPTIONS);
      case 'SearchTechProductsQuery':
        return reply(TECH);
      case 'GetReceiptsWithoutInvoiceQuery':
        return reply(PENDING);
      case 'GetSupplierInvoicesQuery':
        return reply(INVOICES);
      default:
        return real(operation as never, payload as never, options);
    }
  });
  const payloads = (operation: RpcOperationName) => spy.mock.calls.filter(([name]) => name === operation).map(([, payload]) => payload);
  return { spy, payloads };
}

async function openPurchases(role: StaffRole = 'ADMIN', query = '', handlers: Partial<Record<RpcOperationName, Handler>> = {}) {
  const web = await signedInAs(role);
  const server = fakeServer(web, handlers);
  const view = await renderPanel(<PanelApp registry={REGISTRY} />, { web: web.services, route: `/panel/compras${query}`, path: '/panel/*' });
  return { web, server, location: view.location };
}

async function ordersTable(): Promise<HTMLElement> {
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

// ---------------------------------------------------------------------------------------------------- lista

describe('Órdenes de compra · lista', () => {
  it('muestra las órdenes (más recientes primero) con la entrega vencida y el resumen plegado; pedidos exactos', async () => {
    const { server } = await openPurchases();
    expect(await screen.findByRole('heading', { level: 1, name: 'Órdenes de compra' }, { timeout: 5000 })).toBeInTheDocument();
    const table = await ordersTable();
    expect(keys(table)).toEqual(['o3', 'o2', 'o1']);
    expect(server.payloads('GetPurchaseOrdersQuery')).toEqual([{ status: null }]);
    expect(server.payloads('GetSuppliersQuery')).toEqual([{}]);
    expect(rowOf(table, 'o2')).toHaveTextContent('Vencida');
    expect(rowOf(table, 'o2')).toHaveTextContent('Recibida en parte');
    expect(rowOf(table, 'o3')).not.toHaveTextContent('Vencida');
    expect(screen.getByTestId('ordenes-resumen')).toHaveTextContent('3 de 3 órdenes · 1 por recibir');
    expect(screen.queryByTestId('resumen-compras')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /Ver resumen de las compras/ }));
    const summary = await screen.findByTestId('resumen-compras');
    expect(summary).toHaveTextContent('Borradores1');
    expect(summary).toHaveTextContent('Por recibir1');
  });

  it('filtra con listas desplegables (también `?proveedor=` de la dirección) y búsqueda', async () => {
    const { location } = await openPurchases('ADMIN', '?proveedor=P002');
    const table = await ordersTable();
    await waitFor(() => expect(keys(table)).toEqual(['o2']));
    expect(screen.getByRole('combobox', { name: 'Proveedor' })).toHaveValue('P002');
    fireEvent.click(screen.getByRole('button', { name: 'Limpiar filtros' }));
    await waitFor(() => expect(keys(table)).toEqual(['o3', 'o2', 'o1']));
    fireEvent.change(screen.getByRole('combobox', { name: 'Estado' }), { target: { value: 'por-recibir' } });
    await waitFor(() => expect(location()).toBe('/panel/compras?estado=por-recibir'));
    await waitFor(() => expect(keys(table)).toEqual(['o2']));
    fireEvent.click(screen.getByRole('button', { name: 'Limpiar filtros' }));
    fireEvent.change(screen.getByRole('combobox', { name: 'Sucursal' }), { target: { value: 'CM' } });
    await waitFor(() => expect(keys(table)).toEqual(['o3', 'o1']));
    fireEvent.click(screen.getByRole('button', { name: 'Limpiar filtros' }));
    const search = screen.getByRole('searchbox', { name: 'Buscar' });
    fireEvent.change(search, { target: { value: 'campaña' } });
    fireEvent.keyDown(search, { key: 'Enter' });
    await waitFor(() => expect(keys(table)).toEqual(['o3']));
  });

  it('el detalle muestra lo pedido, lo recibido y lo pendiente, y lleva a la factura de cada recepción', async () => {
    const { server } = await openPurchases();
    const table = await ordersTable();
    fireEvent.click(within(rowOf(table, 'o2')).getAllByRole('button')[0]);
    const panel = await screen.findByRole('dialog', { name: 'Orden OC-CB-000002' });
    const lines = await within(panel).findByTestId('lineas-orden');
    expect(lines).toHaveTextContent('Celular Samsung A55');
    expect(lines).toHaveTextContent('recibido 1 · pendiente 2');
    expect(within(panel).getByText('Entrega vencida')).toBeInTheDocument();
    expect(within(panel).getByRole('link', { name: 'Factura del proveedor' })).toHaveAttribute('href', '/panel/compras?pestana=facturas&f_q=RC-CB-000001');
    expect(within(panel).getByRole('link', { name: 'Ver el proveedor' })).toHaveAttribute('href', '/panel/proveedores?q=P002');
    expect(server.payloads('GetPurchaseOrderQuery')).toEqual([{ id: 'o2' }]);
  });

  it('si la consulta falla muestra «Reintentar» y se recupera', async () => {
    let fail = true;
    await openPurchases('ADMIN', '', {
      GetPurchaseOrdersQuery: () => {
        if (fail) {
          fail = false;
          throw new WebApiError({ kind: 'network', message: 'Sin conexión' });
        }
        return ORDERS;
      },
    });
    const error = await screen.findByTestId('estado-error', undefined, { timeout: 5000 });
    fireEvent.click(within(error).getByRole('button', { name: 'Reintentar' }));
    await ordersTable();
  });
});

// ---------------------------------------------------------------------------------------------------- comandos

describe('Órdenes de compra · comandos', () => {
  it('orden nueva: valida y envía el pedido exacto; al crearla abre su detalle', async () => {
    const createdRow = order({ id: 'o4', number: 'OC-CM-000004', orderDate: '2026-09-29', expectedDate: '2026-10-02', total: 900, lines: 1 });
    const { server } = await openPurchases('ADMIN', '', { CreatePurchaseOrderCommand: () => createdRow, GetPurchaseOrderQuery: () => ({ order: createdRow, lines: [], receipts: [] }) });
    await ordersTable();
    fireEvent.click(screen.getByRole('button', { name: 'Nueva orden' }));
    const dialog = await screen.findByRole('dialog', { name: 'Nueva orden de compra' });
    // Solo se ofrecen los proveedores activos.
    const supplier = within(dialog).getByRole('combobox', { name: 'Proveedor' });
    expect(within(supplier).getAllByRole('option').map((option) => option.textContent)).toEqual(['Elija el proveedor', 'Distribuidora Andina (P001)', 'Tecno Import (P002)']);
    fireEvent.click(within(dialog).getByRole('button', { name: 'Crear en borrador' }));
    expect(await within(dialog).findByText('Elija el proveedor.')).toBeInTheDocument();
    expect(within(dialog).getByText('Agregue al menos un producto a la orden.')).toBeInTheDocument();
    expect(server.payloads('CreatePurchaseOrderCommand')).toHaveLength(0);

    fireEvent.change(supplier, { target: { value: 'P001' } });
    await waitFor(() => expect(within(dialog).getByRole('combobox', { name: 'Agregar producto' })).toBeEnabled());
    fireEvent.change(within(dialog).getByRole('combobox', { name: 'Agregar producto' }), { target: { value: 'Mouse' } });
    fireEvent.click(await within(dialog).findByRole('option', { name: /^Mouse Logitech G502/ }));
    const line = await within(dialog).findByTestId('linea-MOU-01');
    expect(within(line).getByLabelText(/^Cantidad/)).toHaveValue('12');
    fireEvent.change(within(line).getByLabelText(/^Cantidad/), { target: { value: '10' } });
    fireEvent.change(within(dialog).getByLabelText(/^Notas/), { target: { value: 'Reposición' } });
    expect(within(dialog).getByTestId('total-orden')).toHaveTextContent('Total Bs 900,00');
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Crear en borrador' }));
    });
    expect(await screen.findByText('Orden OC-CM-000004 creada en borrador')).toBeInTheDocument();
    expect(server.payloads('CreatePurchaseOrderCommand')).toEqual([{ supplierCode: 'P001', expectedDate: null, notes: 'Reposición', lines: [{ sku: 'MOU-01', quantity: 10, unitCost: 90 }] }]);
    expect(await screen.findByRole('dialog', { name: 'Orden OC-CM-000004' })).toBeInTheDocument();
  });

  it('aprobar desde el menú «⋯» pide confirmación, no abre el detalle y envía el pedido exacto', async () => {
    const { server } = await openPurchases('ADMIN', '', { ApprovePurchaseOrderCommand: () => 'OC-CM-000003' });
    const table = await ordersTable();
    fireEvent.click(within(await openRowMenu(table, 'o3')).getByRole('menuitem', { name: 'Aprobar' }));
    const confirm = await screen.findByRole('alertdialog', { name: '¿Aprobar la orden OC-CM-000003?' });
    expect(screen.queryByRole('dialog', { name: 'Orden OC-CM-000003' })).not.toBeInTheDocument();
    expect(server.payloads('ApprovePurchaseOrderCommand')).toHaveLength(0);
    await act(async () => {
      fireEvent.click(within(confirm).getByRole('button', { name: 'Aprobar' }));
    });
    expect(await screen.findByText('Orden aprobada')).toBeInTheDocument();
    expect(server.payloads('ApprovePurchaseOrderCommand')).toEqual([{ id: 'o3' }]);
  });

  it('anular pide confirmación y muestra el error del servidor dentro del diálogo', async () => {
    const { server } = await openPurchases('ADMIN', '', {
      CancelPurchaseOrderCommand: () => {
        throw new WebApiError({ kind: 'domain', status: 422, message: 'La orden OC-CM-000003 ya tiene recepciones.' });
      },
    });
    const table = await ordersTable();
    const menu = await openRowMenu(table, 'o3');
    // Una orden recibida en parte no se anula: solo se ofrece en borradores y aprobadas.
    fireEvent.click(within(menu).getByRole('menuitem', { name: 'Anular' }));
    const confirm = await screen.findByRole('alertdialog', { name: '¿Anular la orden OC-CM-000003?' });
    await act(async () => {
      fireEvent.click(within(confirm).getByRole('button', { name: 'Anular orden' }));
    });
    expect(await within(confirm).findByText('La orden OC-CM-000003 ya tiene recepciones.')).toBeInTheDocument();
    expect(server.payloads('CancelPurchaseOrderCommand')).toEqual([{ id: 'o3' }]);
    fireEvent.click(within(confirm).getByRole('button', { name: 'Cancelar' }));
    await waitFor(() => expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument());
    const partial = await openRowMenu(table, 'o2');
    expect(within(partial).queryByRole('menuitem', { name: 'Anular' })).not.toBeInTheDocument();
  });

  it('recibir: pide un IMEI por unidad pendiente, muestra el error del servidor y envía el pedido exacto', async () => {
    let attempts = 0;
    const { server } = await openPurchases('ADMIN', '', {
      ReceivePurchaseOrderCommand: () => {
        attempts += 1;
        if (attempts === 1) throw new WebApiError({ kind: 'domain', status: 422, message: 'El IMEI 356938035643809 ya está en stock.' });
        return { orderNumber: 'OC-CB-000002', receiptNumber: 'RC-CB-000002', lines: 1, total: 2000, journalNumber: 'AS-CB-000010' };
      },
    });
    const table = await ordersTable();
    fireEvent.click(within(await openRowMenu(table, 'o2')).getByRole('menuitem', { name: 'Recibir mercadería' }));
    const dialog = await screen.findByRole('dialog', { name: 'Recibir OC-CB-000002' });
    expect(await within(dialog).findByTestId('pendiente-recepcion')).toHaveTextContent('Celular Samsung A55');
    const imei = within(dialog).getByLabelText(/^IMEI de Celular Samsung A55/);
    fireEvent.change(imei, { target: { value: '356938035643809' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Recibir mercadería' }));
    expect(await within(dialog).findByText('Escriba 2 IMEI (una por unidad): hay 1.')).toBeInTheDocument();
    expect(server.payloads('ReceivePurchaseOrderCommand')).toHaveLength(0);

    fireEvent.change(imei, { target: { value: '356938035643809\n356938035643817' } });
    fireEvent.change(within(dialog).getByLabelText(/^Factura o remisión del proveedor/), { target: { value: 'REM-77' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Recibir mercadería' }));
    });
    expect(await within(dialog).findByText('El IMEI 356938035643809 ya está en stock.')).toBeInTheDocument();
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Recibir mercadería' }));
    });
    expect(await screen.findByText('Recepción RC-CB-000002 registrada')).toBeInTheDocument();
    const expected = { id: 'o2', supplierDocument: 'REM-77', serials: [{ sku: 'CEL-01', serials: ['356938035643809', '356938035643817'] }] };
    expect(server.payloads('ReceivePurchaseOrderCommand')).toEqual([expected, expected]);
    expect(server.payloads('SearchTechProductsQuery')).toEqual([{ text: null, categoryCode: null, platform: null, filters: null, onlyInStock: false, max: 2000 }]);
  });

  it('desde el pedido sugerido: confirma, crea los borradores y muestra solo los borradores', async () => {
    const { server, location } = await openPurchases('ADMIN', '', { CreateSuggestedPurchaseOrdersCommand: () => ['OC-CM-000005', 'OC-CM-000006'] });
    await ordersTable();
    fireEvent.click(screen.getByRole('button', { name: 'Desde el pedido sugerido' }));
    const confirm = await screen.findByRole('alertdialog', { name: '¿Crear órdenes desde el pedido sugerido?' });
    expect(within(confirm).getByRole('link', { name: 'Revisar antes el pedido sugerido' })).toHaveAttribute('href', '/panel/pedido');
    await act(async () => {
      fireEvent.click(within(confirm).getByRole('button', { name: 'Crear órdenes' }));
    });
    expect(await screen.findByText('2 órdenes creadas en borrador')).toBeInTheDocument();
    expect(server.payloads('CreateSuggestedPurchaseOrdersCommand')).toEqual([{}]);
    await waitFor(() => expect(location()).toBe('/panel/compras?estado=Draft'));
  });

  it('desde el tablero (?nueva=1&proveedor=P002) abre la orden nueva con el proveedor elegido', async () => {
    const { location } = await openPurchases('ADMIN', '?nueva=1&proveedor=P002');
    const dialog = await screen.findByRole('dialog', { name: 'Nueva orden de compra' }, { timeout: 5000 });
    await waitFor(() => expect(within(dialog).getByRole('combobox', { name: 'Proveedor' })).toHaveValue('P002'));
    await waitFor(() => expect(location()).toBe('/panel/compras?proveedor=P002'));
  });
});

// ---------------------------------------------------------------------------------------------------- facturas

describe('Órdenes de compra · facturas de proveedores', () => {
  it('lista las recepciones sin factura y las del mes, y registra una factura con el pedido exacto', async () => {
    const { server } = await openPurchases('ADMIN', '?pestana=facturas', {
      RegisterSupplierInvoiceCommand: () => '✔ Factura 889 de Tecno Import registrada (recepción RC-CB-000001): base Bs 1000.00, crédito fiscal Bs 130.00.',
    });
    expect(await screen.findByRole('tab', { name: /Facturas de proveedores/, selected: true }, { timeout: 5000 })).toBeInTheDocument();
    const tables = await screen.findAllByTestId('tabla');
    await waitFor(() => expect(within(tables[0]).getAllByRole('row').some((row) => row.getAttribute('data-row-key') === 'RC-CB-000001')).toBe(true));
    await waitFor(() => expect(within(tables[1]).getAllByRole('row').some((row) => row.getAttribute('data-row-key') === 'f1')).toBe(true));
    expect(server.payloads('GetSupplierInvoicesQuery')).toEqual([{ from: '2026-09-01', to: '2026-09-29' }]);

    fireEvent.click(within(await openRowMenu(tables[0], 'RC-CB-000001')).getByRole('menuitem', { name: 'Registrar factura' }));
    const dialog = await screen.findByRole('dialog', { name: 'Factura del proveedor · recepción RC-CB-000001' });
    expect(within(dialog).getByLabelText(/^Importe total/)).toHaveValue('1.000,00');
    fireEvent.click(within(dialog).getByRole('button', { name: 'Registrar factura' }));
    expect(await within(dialog).findByText('Indique el número de la factura del proveedor.')).toBeInTheDocument();
    expect(server.payloads('RegisterSupplierInvoiceCommand')).toHaveLength(0);
    fireEvent.change(within(dialog).getByLabelText(/^Número de factura/), { target: { value: '889' } });
    fireEvent.change(within(dialog).getByLabelText(/^CUF o código de autorización/), { target: { value: 'CUF-889' } });
    fireEvent.change(within(dialog).getByRole('combobox', { name: 'Tipo de compra' }), { target: { value: '3' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Registrar factura' }));
    });
    expect(await screen.findByText('Factura del proveedor registrada')).toBeInTheDocument();
    expect(server.payloads('RegisterSupplierInvoiceCommand')).toEqual([
      {
        receiptNumber: 'RC-CB-000001',
        invoiceNumber: '889',
        authorizationCode: 'CUF-889',
        invoiceDate: '2026-09-21',
        totalAmount: 1000,
        discounts: 0,
        notSubjectToVat: 0,
        purchaseType: 3,
        controlCode: null,
      },
    ]);
  });
});

// ---------------------------------------------------------------------------------------------------- roles

describe('Órdenes de compra · lo que ve cada rol', () => {
  it('la gerencia (sin registrar movimientos de bodega) aprueba y anula, pero no recibe', async () => {
    await openPurchases('GERENCIA');
    const table = await ordersTable();
    const menu = await openRowMenu(table, 'o2');
    expect(within(menu).queryByRole('menuitem', { name: 'Recibir mercadería' })).not.toBeInTheDocument();
    fireEvent.keyDown(menu, { key: 'Escape' });
    await waitFor(() => expect(screen.queryByTestId('menu-acciones')).not.toBeInTheDocument());
    const draft = await openRowMenu(table, 'o3');
    expect(within(draft).getByRole('menuitem', { name: 'Aprobar' })).toBeInTheDocument();
  });

  it('ventas (sin gestionar compras) ve «No tiene acceso a esta pantalla»', async () => {
    await openPurchases('VENTAS');
    const denied = await screen.findByTestId('sin-acceso', undefined, { timeout: 5000 });
    expect(denied).toHaveTextContent(permissionName('purchasing.manage'));
  });

  it('ofrece al tablero «Nueva orden de compra» y «Recibir mercadería» (esta, solo a quien registra en bodega)', () => {
    expect(REGISTRY.problems).toEqual([]);
    const offered = (role: StaffRole) => dashboardActions(REGISTRY, ROLE_PERMISSIONS[role]).flatMap((group) => group.actions.map((item) => item.to));
    expect(offered('BODEGA')).toEqual(['/panel/compras?nueva=1', '/panel/compras?estado=por-recibir']);
    expect(offered('GERENCIA')).toEqual(['/panel/compras?nueva=1']);
    expect(offered('CAJERO')).toEqual([]);
    expect(dashboardStats(REGISTRY, ROLE_PERMISSIONS.BODEGA).map((item) => item.stat.title)).toEqual(['Compras en curso']);
  });

  it('la estadística del tablero resume las compras en curso y enlaza a las órdenes por recibir', async () => {
    const web = await signedInAs('BODEGA');
    fakeServer(web);
    await renderPanel(<PurchasesStat />, { web: web.services });
    const stat = await screen.findByTestId('resumen-compras');
    await waitFor(() => expect(stat).toHaveTextContent('Borradores1'));
    expect(stat).toHaveTextContent('Por recibir1');
    expect(screen.getByRole('link', { name: 'Ver las órdenes por recibir' })).toHaveAttribute('href', '/panel/compras?estado=por-recibir');
  });
});
