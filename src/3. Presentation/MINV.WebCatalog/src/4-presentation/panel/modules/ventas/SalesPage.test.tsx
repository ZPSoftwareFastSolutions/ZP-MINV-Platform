// Módulo «Ventas» dentro del panel real (registro del proyecto), con un servidor de ventas SIMULADO en la prueba (el modo
// mock no atiende estas operaciones): lista de hoy con factura del SIN y devoluciones, filtros en la dirección, fechas que
// se piden al servidor, `?cliente=` (todas las ventas del cliente), detalle lateral, anular (motivo y confirmación, error
// del servidor en el diálogo), devolución guiada (cantidades, series, confirmación y el pedido EXACTO del contrato),
// reimprimir (PDF e impresión), enviar la factura por correo, pestaña «Devoluciones», resumen plegado, estados de error y
// lo que ve cada rol. También las estadísticas plegadas y los botones del tablero. Ninguna prueba toca la red.
//
// El panel se arma con un registro PROPIO de la prueba (solo este módulo, como `InicioPage.test.tsx`): así las pruebas no
// dependen de los demás módulos, que otros equipos construyen a la vez.

import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { WebApiError } from '@/1-domain/auth/errors';
import { permissionName, type RpcOperationName, type RpcResponseOf } from '@/4-presentation/app/contract';
import { ROLE_PERMISSIONS, renderPanel, signedInAs, type MockWeb, type StaffRole } from '@/test-utils';
import { addDays, laPazToday } from '../../lib';
import { buildRegistry, dashboardActions, dashboardStats } from '../../registry';
import { PanelApp } from '../../shell/PanelApp';
import ventas from './module';
import { PaymentMethodsStat } from './PaymentMethodsStat';
import { SalesByDayStat } from './SalesByDayStat';

const REGISTRY = buildRegistry([{ source: '../modules/ventas/module.tsx', definition: ventas }]);

beforeAll(async () => {
  await import('./SalesPage');
}, 30_000);

afterEach(() => {
  vi.restoreAllMocks();
  document.body.className = '';
});

// ---------------------------------------------------------------------------------------------------- datos de prueba

const TODAY = laPazToday();
const NOW = new Date().toISOString();

type Sale = RpcResponseOf<'GetSalesQuery'>[number];

function sale(overrides: Partial<Sale>): Sale {
  return {
    invoiceNumber: 'F-CM-000101',
    orderNumber: 'PV-CM-000101',
    issuedAt: NOW,
    date: TODAY,
    customerCode: 'CF',
    customer: 'Consumidor final',
    cashier: 'Diego Flores',
    paymentMethod: 'Efectivo',
    items: 2,
    total: 350,
    tax: 45.5,
    status: 'Issued',
    voidReason: null,
    ...overrides,
  };
}

const SALES: Sale[] = [
  sale({}),
  sale({ invoiceNumber: 'F-CM-000102', orderNumber: 'PV-CM-000102', customerCode: 'C0002', customer: 'Mariana Céspedes', cashier: 'Carla Rojas', paymentMethod: 'Tarjeta', total: 8200, tax: 1066 }),
  sale({ invoiceNumber: 'F-CB-000007', orderNumber: 'PV-CB-000007', customerCode: 'WEB-000003', customer: 'Luis Arce', paymentMethod: 'QR', items: 1, total: 120, tax: 15.6 }),
  sale({ invoiceNumber: 'F-CM-000100', orderNumber: 'PV-CM-000100', customerCode: 'C0002', customer: 'Mariana Céspedes', cashier: 'Carla Rojas', items: 1, total: 500, tax: 65, status: 'Voided', voidReason: 'Venta duplicada' }),
];

const FISCAL: RpcResponseOf<'GetSalesFiscalStatusQuery'> = [
  { invoiceNumber: 'F-CM-000102', documentId: 'doc-102', number: 55, status: 'Valid', isReverted: false, emissionType: 1, cuf: 'ABCDEF1234567890ABCDEF', canVoid: true, canCreditNote: true, voidDeadline: '2026-10-09T23:59:59' },
  { invoiceNumber: 'F-CB-000007', documentId: 'doc-007', number: 56, status: 'Offline', isReverted: false, emissionType: 2, cuf: '1122334455667788', canVoid: true, canCreditNote: false, voidDeadline: '2026-10-09T23:59:59' },
];

const RETURNS: RpcResponseOf<'GetSalesReturnsQuery'> = [
  { number: 'DV-CB-000001', invoiceNumber: 'F-CB-000007', returnedAt: NOW, customer: 'Luis Arce', reason: 'Cambio de producto', refund: 60, creditNote: 'Nota N° 9', creditNoteStatus: 'Valid' },
];

const LINES: Record<string, RpcResponseOf<'GetSaleLinesQuery'>> = {
  'F-CM-000101': [
    { sku: 'MOU-LOG-G502', name: 'Mouse Logitech G502', quantity: 2, unit: 'u', unitPrice: 150, discountPercent: 0, amount: 300, serials: null },
    { sku: 'CBL-USB-C', name: 'Cable USB-C', quantity: 1, unit: 'u', unitPrice: 50, discountPercent: 0, amount: 50, serials: null },
  ],
  'F-CM-000102': [
    { sku: 'GPU-RTX4070S', name: 'Tarjeta de video RTX 4070 Super', quantity: 2, unit: 'u', unitPrice: 4000, discountPercent: 0, amount: 8000, serials: ['SN-4070-A1', 'SN-4070-A2'] },
    { sku: 'CBL-HDMI-2M', name: 'Cable HDMI 2 m', quantity: 1, unit: 'u', unitPrice: 200, discountPercent: 0, amount: 200, serials: null },
  ],
};

const RETURNABLE: Record<string, RpcResponseOf<'GetReturnableLinesQuery'>> = {
  'F-CM-000102': [
    { sku: 'GPU-RTX4070S', name: 'Tarjeta de video RTX 4070 Super', unit: 'u', sold: 2, returned: 0, unitPrice: 4000, discountPercent: 0 },
    { sku: 'CBL-HDMI-2M', name: 'Cable HDMI 2 m', unit: 'u', sold: 1, returned: 0, unitPrice: 200, discountPercent: 0 },
  ],
};

/** Series que siguen vendidas (la A2 ya volvió en garantía). */
const SOLD_SERIALS: RpcResponseOf<'SearchSerialsQuery'> = [
  { serial: 'SN-4070-A1', sku: 'GPU-RTX4070S', product: 'Tarjeta de video RTX 4070 Super', kind: 'Serial', status: 'Sold', branch: 'CM', warehouse: null, customer: 'Mariana Céspedes', invoiceNumber: 'F-CM-000102', receivedAt: null, soldAt: NOW, warrantyUntil: null },
];

const PRINT_MODEL: RpcResponseOf<'GetFiscalPrintModelQuery'> = {
  title: 'FACTURA',
  subtitle: '(Con Derecho a Crédito Fiscal)',
  issuerName: 'TECH ZONE GAMING S.R.L.',
  issuerNit: 1020304050,
  number: 55,
  cuf: 'ABCDEF1234567890ABCDEF',
  branchLabel: 'Casa matriz',
  pointOfSaleCode: 0,
  address: 'Av. 16 de Julio',
  municipality: 'La Paz',
  phone: '2-2123456',
  issuedAt: '2026-09-29T10:32:05.123',
  buyerName: 'CESPEDES',
  buyerDocument: '4455667',
  customerCode: 'C0002',
  lines: [
    { productCode: 'GPU-RTX4070S', description: 'Tarjeta de video RTX 4070 Super', quantity: 2, unit: 'UNIDAD', unitPrice: 4000, discount: 0, subtotal: 8000, serialsText: 'S/N: SN-4070-A1, SN-4070-A2', transactionCode: null, warrantyUntil: '2027-09-29' },
  ],
  subtotal: 8200,
  discount: 0,
  total: 8200,
  giftCard: 0,
  amountToPay: 8200,
  taxBase: 8200,
  amountInWords: 'Ocho mil doscientos 00/100 Bolivianos',
  legends: ['Ley N° 453: Tienes derecho a recibir información sobre las características de los productos.'],
  isTest: true,
  isVoided: false,
  isOffline: false,
  original: null,
  creditDebitAmount: null,
  returnedTotal: null,
  saleNumber: 'F-CM-000102',
  paymentMethod: 'Tarjeta',
  cashier: 'Carla Rojas',
  qrUrl: 'sin-enlace',
};

type Handler = (payload: unknown) => unknown;

interface FakeOptions {
  moduleActive?: boolean;
  /** Respuestas propias por operación (pueden lanzar un WebApiError). */
  handlers?: Partial<Record<RpcOperationName, Handler>>;
}

/** Servidor de ventas en memoria: responde las operaciones del módulo y deja el resto al modo mock. */
function fakeServer(web: MockWeb, options: FakeOptions = {}) {
  const real = web.backend.rpc.call.bind(web.backend.rpc);
  const spy = vi.spyOn(web.backend.rpc, 'call').mockImplementation(async (operation, payload, sendOptions) => {
    const reply = (result: unknown) => ({ result: result as never, replayed: false, requestId: sendOptions?.requestId ?? 'prueba' });
    const custom = options.handlers?.[operation];
    if (custom) return reply(await custom(payload));
    const invoice = (payload as unknown as { invoiceNumber?: string }).invoiceNumber ?? '';
    switch (operation) {
      case 'GetBillingAccessQuery':
        return reply({ moduleActive: options.moduleActive ?? true, configured: true, enabled: true, environment: 2 });
      case 'GetSalesQuery':
        return reply(SALES);
      case 'GetSalesFiscalStatusQuery':
        return reply(FISCAL);
      case 'GetSalesReturnsQuery':
        return reply(RETURNS);
      case 'GetSaleLinesQuery':
        return reply(LINES[invoice] ?? []);
      case 'GetReturnableLinesQuery':
        if (!RETURNABLE[invoice]) throw new WebApiError({ kind: 'not_found', status: 404, message: `La venta ${invoice} no existe (o es de una sucursal que no es suya).` });
        return reply(RETURNABLE[invoice]);
      case 'GetPosStateQuery':
        return reply({
          branchName: 'Casa matriz',
          companyName: 'Tech Zone Gaming S.R.L.',
          customers: [],
          paymentMethods: [
            { code: 'EFECTIVO', name: 'Efectivo', opensCashDrawer: true },
            { code: 'TARJETA', name: 'Tarjeta', opensCashDrawer: false },
          ],
          registers: [],
          session: null,
          suggestedRegister: null,
          taxId: null,
          taxRate: 13,
          vatOnInvoicedAmount: true,
        });
      case 'SearchSerialsQuery':
        return reply(SOLD_SERIALS);
      case 'GetFiscalPrintModelQuery':
        return reply(PRINT_MODEL);
      default:
        // Lo que no es del módulo lo atiende el modo mock (la sesión, el cambio de sucursal…).
        return real(operation as never, payload as never, sendOptions);
    }
  });
  const payloads = (operation: RpcOperationName) => spy.mock.calls.filter(([name]) => name === operation).map(([, payload]) => payload);
  return { spy, payloads };
}

async function openSales(role: StaffRole = 'ADMIN', query = '', options: FakeOptions = {}) {
  const web = await signedInAs(role);
  const server = fakeServer(web, options);
  const view = await renderPanel(<PanelApp registry={REGISTRY} />, { web: web.services, route: `/panel/ventas${query}`, path: '/panel/*' });
  return { web, server, location: view.location };
}

async function salesTable(): Promise<HTMLElement> {
  const tables = await screen.findAllByTestId('tabla', undefined, { timeout: 5000 });
  const found = tables[0];
  await waitFor(() => expect(within(found).queryAllByRole('row').filter((row) => row.hasAttribute('data-row-key')).length).toBeGreaterThan(0));
  return found;
}

function dataRows(table: HTMLElement): HTMLElement[] {
  return within(table)
    .getAllByRole('row')
    .filter((row) => row.hasAttribute('data-row-key'));
}

function rowOf(table: HTMLElement, key: string): HTMLElement {
  const row = dataRows(table).find((item) => item.getAttribute('data-row-key') === key);
  if (!row) throw new Error(`No está la fila ${key}`);
  return row;
}

async function openRowMenu(table: HTMLElement, key: string): Promise<HTMLElement> {
  fireEvent.click(within(rowOf(table, key)).getByRole('button', { name: /^Acciones de / }));
  return screen.findByTestId('menu-acciones');
}

// ---------------------------------------------------------------------------------------------------- lista

describe('Ventas · lista', () => {
  it('carga las ventas de HOY con su factura del SIN y sus devoluciones (pedidos exactos) y los totales al pie', async () => {
    const { server } = await openSales();
    expect(await screen.findByRole('heading', { level: 1, name: 'Ventas' }, { timeout: 5000 })).toBeInTheDocument();
    const table = await salesTable();
    expect(dataRows(table).map((row) => row.getAttribute('data-row-key'))).toHaveLength(4);
    expect(server.payloads('GetSalesQuery')).toEqual([{ from: TODAY, to: TODAY }]);
    expect(server.payloads('GetSalesFiscalStatusQuery')).toEqual([{ from: TODAY, to: TODAY }]);
    expect(server.payloads('GetSalesReturnsQuery')).toEqual([{ from: TODAY, to: TODAY }]);
    expect(server.payloads('GetBillingAccessQuery')).toEqual([{}]);

    expect(rowOf(table, 'F-CM-000102')).toHaveTextContent('N° 55Válida');
    expect(rowOf(table, 'F-CB-000007')).toHaveTextContent('Con devolución');
    expect(rowOf(table, 'F-CB-000007')).toHaveTextContent('Cliente web');
    expect(rowOf(table, 'F-CM-000100')).toHaveTextContent('Anulada');
    expect(rowOf(table, 'F-CM-000101')).toHaveTextContent('Sin factura');
    // Totales al pie: lo vendido sin las anuladas.
    expect(within(table).getByTestId('tabla-totales')).toHaveTextContent('Bs 8.670,00');
    expect(screen.getByTestId('ventas-resumen')).toHaveTextContent('4 de 4 ventas · vendido Bs 8.670,00 · 1 anulada');
    // Nada de estadísticas a la vista al entrar: el resumen está plegado.
    expect(screen.getByRole('button', { name: /Ver resumen del período/ })).toHaveAttribute('aria-expanded', 'false');
    expect(screen.queryByTestId('resumen-periodo')).not.toBeInTheDocument();
  });

  it('filtra con listas desplegables y búsqueda (en la página) y los filtros quedan en la dirección', async () => {
    const { location } = await openSales();
    const table = await salesTable();
    fireEvent.change(screen.getByRole('combobox', { name: 'Estado' }), { target: { value: 'anulada' } });
    await waitFor(() => expect(location()).toBe('/panel/ventas?estado=anulada'));
    await waitFor(() => expect(dataRows(table).map((row) => row.getAttribute('data-row-key'))).toEqual(['F-CM-000100']));

    fireEvent.click(screen.getByRole('button', { name: 'Limpiar filtros' }));
    await waitFor(() => expect(location()).toBe('/panel/ventas'));
    fireEvent.change(screen.getByRole('combobox', { name: 'Cajero' }), { target: { value: 'Carla Rojas' } });
    fireEvent.change(screen.getByRole('combobox', { name: 'Medio de pago' }), { target: { value: 'Tarjeta' } });
    await waitFor(() => expect(dataRows(table).map((row) => row.getAttribute('data-row-key'))).toEqual(['F-CM-000102']));
    expect(screen.getByTestId('filtros-activos')).toHaveTextContent('2 activos');

    fireEvent.click(screen.getByRole('button', { name: 'Limpiar filtros' }));
    fireEvent.change(screen.getByRole('combobox', { name: 'Factura del SIN' }), { target: { value: 'SinFactura' } });
    await waitFor(() => expect(dataRows(table).map((row) => row.getAttribute('data-row-key')).sort()).toEqual(['F-CM-000100', 'F-CM-000101']));

    fireEvent.click(screen.getByRole('button', { name: 'Limpiar filtros' }));
    fireEvent.change(screen.getByRole('combobox', { name: 'Sucursal' }), { target: { value: 'CB' } });
    await waitFor(() => expect(dataRows(table).map((row) => row.getAttribute('data-row-key'))).toEqual(['F-CB-000007']));

    fireEvent.click(screen.getByRole('button', { name: 'Limpiar filtros' }));
    const search = screen.getByRole('searchbox', { name: 'Buscar' });
    fireEvent.change(search, { target: { value: 'cespedes' } });
    fireEvent.keyDown(search, { key: 'Enter' });
    await waitFor(() => expect(location()).toBe('/panel/ventas?q=cespedes'));
    await waitFor(() => expect(dataRows(table).map((row) => row.getAttribute('data-row-key')).sort()).toEqual(['F-CM-000100', 'F-CM-000102']));
  });

  it('las fechas se piden al servidor: «Últimos 7 días» y «Todas» (desde el principio hasta hoy)', async () => {
    const { server, location } = await openSales();
    await salesTable();
    const dates = screen.getAllByRole('group').find((group) => group.tagName === 'FIELDSET' && within(group).queryByText('Fechas'));
    fireEvent.click(within(dates!).getByRole('button', { name: 'Últimos 7 días' }));
    await waitFor(() => expect(server.payloads('GetSalesQuery')).toContainEqual({ from: addDays(TODAY, -6), to: TODAY }));
    expect(location()).toBe(`/panel/ventas?desde=${addDays(TODAY, -6)}`);
    fireEvent.click(within(dates!).getByRole('button', { name: 'Todas' }));
    await waitFor(() => expect(server.payloads('GetSalesQuery')).toContainEqual({ from: '2000-01-01', to: TODAY }));
    expect(server.payloads('GetSalesReturnsQuery')).toContainEqual({ from: '2000-01-01', to: TODAY });
  });

  it('«Ver sus ventas» (desde Clientes: ?cliente= sin fechas) muestra TODAS las ventas del cliente', async () => {
    const { server, location } = await openSales('ADMIN', '?cliente=C0002');
    const table = await salesTable();
    await waitFor(() => expect(dataRows(table).map((row) => row.getAttribute('data-row-key')).sort()).toEqual(['F-CM-000100', 'F-CM-000102']));
    expect(server.payloads('GetSalesQuery')).toEqual([{ from: '2000-01-01', to: TODAY }]);
    await waitFor(() => expect(location()).toBe('/panel/ventas?cliente=C0002&desde=&hasta='));
    expect(screen.getByRole('combobox', { name: 'Cliente' })).toHaveValue('Mariana Céspedes');
  });

  it('si la consulta falla muestra el error con «Reintentar» y se recupera', async () => {
    let fail = true;
    await openSales('ADMIN', '', {
      handlers: {
        GetSalesQuery: () => {
          if (fail) {
            fail = false;
            throw new WebApiError({ kind: 'network', message: 'Sin conexión' });
          }
          return SALES;
        },
      },
    });
    const error = await screen.findByTestId('estado-error', undefined, { timeout: 5000 });
    expect(error).toHaveTextContent('No se pudo cargar la información');
    fireEvent.click(within(error).getByRole('button', { name: 'Reintentar' }));
    await salesTable();
  });

  it('el resumen del período se abre a pedido (plegado al entrar)', async () => {
    await openSales();
    await salesTable();
    fireEvent.click(screen.getByRole('button', { name: /Ver resumen del período/ }));
    const summary = await screen.findByTestId('resumen-periodo');
    expect(summary).toHaveTextContent('VendidoBs 8.670,00');
    expect(summary).toHaveTextContent('Anuladas1');
    expect(summary).toHaveTextContent('Con devolución1');
  });

  it('sin el módulo de facturación no hay columna ni filtro de la factura del SIN', async () => {
    await openSales('ADMIN', '', { moduleActive: false });
    const table = await salesTable();
    await waitFor(() => expect(within(table).queryByRole('columnheader', { name: /Factura del SIN/ })).not.toBeInTheDocument());
    expect(screen.queryByRole('combobox', { name: 'Factura del SIN' })).not.toBeInTheDocument();
  });

  it('exporta las ventas filtradas a CSV', async () => {
    const created: Blob[] = [];
    vi.spyOn(URL, 'createObjectURL').mockImplementation((blob) => {
      created.push(blob as Blob);
      return 'blob:prueba';
    });
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    await openSales('ADMIN', '?estado=anulada');
    await salesTable();
    fireEvent.click(screen.getByRole('button', { name: 'Exportar CSV' }));
    expect(await screen.findByText('Exportación lista')).toBeInTheDocument();
    const lines = (await created[0].text()).replace(/^﻿/, '').trim().split('\r\n');
    expect(lines[0]).toContain('"Venta";"Pedido";"Fecha y hora";"Sucursal"');
    expect(lines).toHaveLength(2);
    expect(lines[1]).toContain('"F-CM-000100"');
    expect(lines[1]).toContain('"Venta duplicada"');
  });
});

// ---------------------------------------------------------------------------------------------------- detalle y comandos

describe('Ventas · detalle y comandos', () => {
  it('el detalle lateral muestra los productos con sus series, el pago, la factura del SIN y las devoluciones', async () => {
    const { server } = await openSales();
    const table = await salesTable();
    fireEvent.click(within(rowOf(table, 'F-CM-000102')).getAllByRole('button')[0]);
    const panel = await screen.findByRole('dialog', { name: 'Venta F-CM-000102' });
    const lines = await within(panel).findByTestId('lineas-venta');
    expect(lines).toHaveTextContent('Series: SN-4070-A1, SN-4070-A2');
    expect(server.payloads('GetSaleLinesQuery')).toEqual([{ invoiceNumber: 'F-CM-000102' }]);
    expect(panel).toHaveTextContent('Tarjeta');
    expect(panel).toHaveTextContent('N° 55');
    expect(panel).toHaveTextContent('El SIN la recibió y la validó.');
    expect(panel).toHaveTextContent('09/10/2026 23:59');
    expect(within(panel).getByRole('link', { name: 'Ver el cliente' })).toHaveAttribute('href', '/panel/clientes?q=C0002');
    fireEvent.click(within(panel).getByRole('button', { name: 'Cerrar' }));

    fireEvent.click(within(rowOf(table, 'F-CB-000007')).getAllByRole('button')[0]);
    const other = await screen.findByRole('dialog', { name: 'Venta F-CB-000007' });
    expect(within(other).getByTestId('devoluciones-venta')).toHaveTextContent('DV-CB-000001');
    expect(other).toHaveTextContent('Fuera de línea');
    // Con devoluciones no se anula entera (se explica).
    expect(other).toHaveTextContent('La venta tiene devoluciones registradas: no se puede anular entera.');
  });

  it('anular: pide el motivo, confirma, muestra el error del servidor en el diálogo y envía VoidSaleCommand exacto', async () => {
    let attempts = 0;
    const { server } = await openSales('ADMIN', '', {
      handlers: {
        VoidSaleCommand: () => {
          attempts += 1;
          if (attempts === 1) throw new WebApiError({ kind: 'domain', status: 422, message: 'La caja de la venta está cerrada.' });
          return '✔ Factura F-CM-000101 anulada: el stock volvió y se registró el asiento inverso.';
        },
      },
    });
    const table = await salesTable();
    const before = server.payloads('GetSalesQuery').length;
    fireEvent.click(within(await openRowMenu(table, 'F-CM-000101')).getByRole('menuitem', { name: 'Anular la venta' }));
    const dialog = await screen.findByRole('alertdialog', { name: '¿Anular la venta F-CM-000101?' });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Anular venta' }));
    expect(await within(dialog).findByText('Elija el motivo o escriba uno.')).toBeInTheDocument();
    expect(server.payloads('VoidSaleCommand')).toHaveLength(0);

    fireEvent.change(within(dialog).getByRole('combobox', { name: 'Motivo de la anulación' }), { target: { value: '_otro' } });
    fireEvent.change(within(dialog).getByLabelText(/^Escriba el motivo/), { target: { value: 'Cliente arrepentido' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Anular venta' }));
    });
    expect(await within(dialog).findByText('La caja de la venta está cerrada.')).toBeInTheDocument();
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Anular venta' }));
    });
    expect(await screen.findByText('Venta anulada')).toBeInTheDocument();
    expect(server.payloads('VoidSaleCommand')).toEqual([
      { invoiceNumber: 'F-CM-000101', reason: 'Cliente arrepentido' },
      { invoiceNumber: 'F-CM-000101', reason: 'Cliente arrepentido' },
    ]);
    await waitFor(() => expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument());
    // La lista se vuelve a pedir.
    await waitFor(() => expect(server.payloads('GetSalesQuery').length).toBeGreaterThan(before));
  });

  it('una venta con factura del SIN activa no se anula aquí: lo explica y lleva a Documentos', async () => {
    const { server } = await openSales();
    const table = await salesTable();
    fireEvent.click(within(await openRowMenu(table, 'F-CM-000102')).getByRole('menuitem', { name: 'Anular la venta' }));
    const dialog = await screen.findByRole('dialog', { name: 'Anular la venta F-CM-000102' });
    expect(dialog).toHaveTextContent('Esta venta tiene factura del SIN');
    expect(dialog).toHaveTextContent('La factura N° 55 de la venta F-CM-000102 se anula ante el SIN');
    expect(within(dialog).getByRole('link', { name: 'Ir a Documentos fiscales' })).toHaveAttribute('href', '/panel/documentos');
    expect(within(dialog).queryByRole('button', { name: 'Anular venta' })).not.toBeInTheDocument();
    expect(server.payloads('VoidSaleCommand')).toHaveLength(0);
  });

  it('devolución guiada: cantidades y series, validación, confirmación, pedido EXACTO y envío de la nota al SIN', async () => {
    const { server, location } = await openSales('ADMIN', '', {
      handlers: {
        CreateSalesReturnCommand: () => ({
          number: 'DV-CM-000002',
          refund: 4200,
          creditNoteId: 'nota-1',
          creditNoteNumber: 10,
          creditNoteStatus: 'Pending',
          message: '✔ Devolución DV-CM-000002 registrada: reembolso Bs 4.200,00 (Efectivo); por falla: la mercadería quedó en garantía.',
        }),
        DispatchFiscalDocumentsCommand: () => ({ sent: 1, valid: 1, rejected: 0, wentOffline: 0, documents: [], messages: [] }),
      },
    });
    const table = await salesTable();
    fireEvent.click(within(await openRowMenu(table, 'F-CM-000102')).getByRole('menuitem', { name: 'Devolver productos' }));
    const dialog = await screen.findByRole('dialog', { name: 'Devolución de la venta F-CM-000102' });
    const gpu = await within(dialog).findByTestId('devolver-GPU-RTX4070S', undefined, { timeout: 5000 });
    // La serie que ya no está vendida no se puede marcar.
    await waitFor(() => expect(within(gpu).getByRole('checkbox', { name: 'SN-4070-A2' })).toBeDisabled());
    expect(server.payloads('SearchSerialsQuery')).toEqual([{ text: null, status: 'Sold', sku: 'GPU-RTX4070S', max: 2000 }]);

    fireEvent.click(within(dialog).getByRole('button', { name: 'Revisar la devolución' }));
    expect(await within(dialog).findByText('Indique qué productos se devuelven: una cantidad o las series.')).toBeInTheDocument();
    expect(within(dialog).getByText('Elija el motivo o escriba uno.')).toBeInTheDocument();

    const cable = within(dialog).getByTestId('devolver-CBL-HDMI-2M');
    fireEvent.change(within(cable).getByLabelText(/^Cantidad a devolver/), { target: { value: '3' } });
    expect(await within(cable).findByText('Puede devolver hasta 1 u.')).toBeInTheDocument();
    fireEvent.change(within(cable).getByLabelText(/^Cantidad a devolver/), { target: { value: '1' } });
    fireEvent.click(within(gpu).getByRole('checkbox', { name: 'SN-4070-A1' }));
    fireEvent.change(within(dialog).getByRole('combobox', { name: 'Motivo de la devolución' }), { target: { value: 'Producto con falla' } });
    // Un producto con falla sugiere la devolución POR FALLA.
    expect(within(dialog).getByRole('checkbox', { name: 'Devolución por falla' })).toBeChecked();
    expect(within(dialog).getByRole('combobox', { name: 'Se reembolsa con' })).toHaveValue('EFECTIVO');
    expect(within(dialog).getByTestId('reembolso-estimado')).toHaveTextContent('Bs 4.200,00');

    fireEvent.click(within(dialog).getByRole('button', { name: 'Revisar la devolución' }));
    expect(await within(dialog).findByText('Revise antes de registrar: no se puede deshacer')).toBeInTheDocument();
    expect(within(dialog).getByRole('list', { name: 'Productos que se devuelven' })).toHaveTextContent('Series: SN-4070-A1');
    expect(server.payloads('CreateSalesReturnCommand')).toHaveLength(0);
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Registrar devolución' }));
    });
    expect(await screen.findByText('Devolución DV-CM-000002 registrada')).toBeInTheDocument();
    expect(server.payloads('CreateSalesReturnCommand')).toEqual([
      {
        invoiceNumber: 'F-CM-000102',
        reason: 'Producto con falla',
        refundPaymentMethodCode: 'EFECTIVO',
        lines: [
          { sku: 'GPU-RTX4070S', quantity: 1, serials: ['SN-4070-A1'] },
          { sku: 'CBL-HDMI-2M', quantity: 1, serials: null },
        ],
        defective: true,
      },
    ]);
    expect(server.payloads('DispatchFiscalDocumentsCommand')).toEqual([{ documentId: 'nota-1', max: 50 }]);
    // Queda a la vista la pestaña «Devoluciones».
    await waitFor(() => expect(location()).toBe('/panel/ventas?vista=devoluciones'));
  });

  it('desde el tablero (?devolver=1) pide el número de la venta y avisa si no existe', async () => {
    const { server, location } = await openSales('ADMIN', '?devolver=1');
    const dialog = await screen.findByRole('dialog', { name: 'Registrar una devolución' }, { timeout: 5000 });
    await waitFor(() => expect(location()).toBe('/panel/ventas'));
    fireEvent.click(within(dialog).getByRole('button', { name: 'Buscar la venta' }));
    expect(await within(dialog).findByText('Escriba el número de la venta.')).toBeInTheDocument();
    fireEvent.change(within(dialog).getByLabelText(/^Número de la venta/), { target: { value: ' f-cm-000999 ' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Buscar la venta' }));
    expect(await within(dialog).findByText(/La venta F-CM-000999 no existe/)).toBeInTheDocument();
    expect(server.payloads('GetReturnableLinesQuery')).toEqual([{ invoiceNumber: 'F-CM-000999' }]);
    fireEvent.click(within(dialog).getByRole('button', { name: 'Buscar otra venta' }));
    fireEvent.change(within(dialog).getByLabelText(/^Número de la venta/), { target: { value: 'F-CM-000102' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Buscar la venta' }));
    expect(await within(dialog).findByTestId('devolver-CBL-HDMI-2M', undefined, { timeout: 5000 })).toBeInTheDocument();
  });

  it('reimprimir: vista de la factura, «Descargar PDF» (con la entrega registrada) e «Imprimir» solo la factura', async () => {
    const created: Blob[] = [];
    vi.spyOn(URL, 'createObjectURL').mockImplementation((blob) => {
      created.push(blob as Blob);
      return 'blob:prueba';
    });
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    const print = vi.spyOn(window, 'print').mockImplementation(() => undefined);
    const { server } = await openSales('ADMIN', '', {
      handlers: {
        RenderFiscalDocumentQuery: () => ({ fileName: 'factura-55.pdf', contentType: 'application/pdf', content: btoa('%PDF-1.4 prueba') }),
        RecordFiscalDeliveryCommand: () => '✔ Entrega registrada.',
      },
    });
    const table = await salesTable();
    fireEvent.click(within(await openRowMenu(table, 'F-CM-000102')).getByRole('menuitem', { name: 'Reimprimir la factura' }));
    const dialog = await screen.findByRole('dialog', { name: 'Reimprimir la factura N° 55' });
    expect(await within(dialog).findByText('TECH ZONE GAMING S.R.L.')).toBeInTheDocument();
    expect(within(dialog).getByTestId('factura-avisos')).toHaveTextContent('SIN VALOR LEGAL');
    expect(dialog).toHaveTextContent('29/09/2026 10:32');
    expect(server.payloads('GetFiscalPrintModelQuery')).toEqual([{ documentId: 'doc-102' }]);
    // El área imprimible (oculta en la pantalla) y la regla que oculta lo demás al imprimir.
    expect(screen.getByTestId('area-imprimible')).toHaveTextContent('Ocho mil doscientos 00/100 Bolivianos');
    expect(document.body.className).toContain('print:');

    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Descargar PDF' }));
    });
    expect(await screen.findByText('PDF listo')).toBeInTheDocument();
    expect(screen.getByText('Se descargó factura-55.pdf.')).toBeInTheDocument();
    expect(await created[0].text()).toBe('%PDF-1.4 prueba');
    expect(server.payloads('RenderFiscalDocumentQuery')).toEqual([{ documentId: 'doc-102', format: 'Pdf', columns: 48 }]);
    await waitFor(() => expect(server.payloads('RecordFiscalDeliveryCommand')).toEqual([{ documentId: 'doc-102', channel: 'Pdf', recipient: null }]));

    fireEvent.click(within(dialog).getByRole('button', { name: 'Imprimir' }));
    expect(print).toHaveBeenCalledTimes(1);
    fireEvent.click(within(dialog).getAllByRole('button', { name: 'Cerrar' })[0]);
    await waitFor(() => expect(screen.queryByTestId('area-imprimible')).not.toBeInTheDocument());
    expect(document.body.className).not.toContain('print:');
  });

  it('enviar la factura por correo: valida el correo y, vacío, usa el del comprador (null)', async () => {
    const { server } = await openSales('ADMIN', '', {
      handlers: { SendFiscalDocumentEmailCommand: () => '✔ Factura N° 55 enviada a mariana@correo.example (XML y PDF).' },
    });
    const table = await salesTable();
    fireEvent.click(within(await openRowMenu(table, 'F-CM-000102')).getByRole('menuitem', { name: 'Enviar la factura por correo' }));
    const dialog = await screen.findByRole('dialog', { name: 'Enviar la factura N° 55 por correo' });
    const email = within(dialog).getByLabelText(/^Correo/);
    fireEvent.change(email, { target: { value: 'correo-malo' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Enviar' }));
    expect(await within(dialog).findByText('Escriba un correo válido, por ejemplo nombre@dominio.com.')).toBeInTheDocument();
    fireEvent.change(email, { target: { value: '' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Enviar' }));
    });
    expect(await screen.findByText('Factura enviada por correo')).toBeInTheDocument();
    expect(screen.getByText('Factura N° 55 enviada a mariana@correo.example (XML y PDF).')).toBeInTheDocument();
    expect(server.payloads('SendFiscalDocumentEmailCommand')).toEqual([{ documentId: 'doc-102', email: null }]);
  });
});

// ---------------------------------------------------------------------------------------------------- devoluciones

describe('Ventas · pestaña «Devoluciones»', () => {
  it('lista las devoluciones con su nota, filtra por la nota, abre el detalle y exporta', async () => {
    const created: Blob[] = [];
    vi.spyOn(URL, 'createObjectURL').mockImplementation((blob) => {
      created.push(blob as Blob);
      return 'blob:prueba';
    });
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    const { location } = await openSales('ADMIN', '?vista=devoluciones');
    expect(await screen.findByRole('tab', { name: /Devoluciones/, selected: true }, { timeout: 5000 })).toBeInTheDocument();
    const table = await salesTable();
    expect(rowOf(table, 'DV-CB-000001')).toHaveTextContent('Nota N° 9Válida');
    expect(screen.getByTestId('devoluciones-resumen')).toHaveTextContent('1 devolución · reembolsado Bs 60,00');

    fireEvent.click(within(rowOf(table, 'DV-CB-000001')).getAllByRole('button')[0]);
    const panel = await screen.findByRole('dialog', { name: 'Devolución DV-CB-000001' });
    expect(panel).toHaveTextContent('Cambio de producto');
    fireEvent.click(within(panel).getByRole('button', { name: 'Cerrar' }));

    fireEvent.click(screen.getByRole('button', { name: 'Exportar CSV' }));
    expect(await screen.findByText('Exportación lista')).toBeInTheDocument();
    const lines = (await created[0].text()).replace(/^﻿/, '').trim().split('\r\n');
    expect(lines[0]).toBe('"Devolución";"Fecha y hora";"Sucursal";"Venta";"Cliente";"Motivo";"Reembolso";"Nota crédito-débito";"Estado de la nota"');

    fireEvent.change(screen.getByRole('combobox', { name: 'Nota crédito-débito' }), { target: { value: 'SinNota' } });
    await waitFor(() => expect(location()).toBe('/panel/ventas?vista=devoluciones&nota=SinNota'));
    expect(await within(screen.getAllByTestId('tabla')[0]).findByText('No hay devoluciones con estos filtros')).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------------------------------- roles

describe('Ventas · lo que ve cada rol', () => {
  it('la gerencia ve las ventas y reimprime, pero no anula ni devuelve (no opera la caja) ni envía facturas', async () => {
    await openSales('GERENCIA');
    const table = await salesTable();
    expect(screen.queryByRole('button', { name: 'Registrar devolución' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Nueva venta' })).not.toBeInTheDocument();
    const menu = await openRowMenu(table, 'F-CM-000102');
    expect(within(menu).getByRole('menuitem', { name: 'Reimprimir la factura' })).toBeInTheDocument();
    expect(within(menu).queryByRole('menuitem', { name: 'Enviar la factura por correo' })).not.toBeInTheDocument();
    expect(within(menu).queryByRole('menuitem', { name: 'Devolver productos' })).not.toBeInTheDocument();
    expect(within(menu).queryByRole('menuitem', { name: 'Anular la venta' })).not.toBeInTheDocument();
  });

  it('ventas anula y envía facturas, pero no registra devoluciones (le falta anular documentos)', async () => {
    await openSales('VENTAS');
    const table = await salesTable();
    expect(screen.queryByRole('button', { name: 'Registrar devolución' })).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Nueva venta' })).toHaveAttribute('href', '/panel/caja');
    const menu = await openRowMenu(table, 'F-CM-000101');
    expect(within(menu).getByRole('menuitem', { name: 'Anular la venta' })).toBeInTheDocument();
    expect(within(menu).queryByRole('menuitem', { name: 'Devolver productos' })).not.toBeInTheDocument();
  });

  it('un rol sin «ver ventas» ve «No tiene acceso a esta pantalla» con el permiso que falta', async () => {
    await openSales('CONSULTA');
    expect(await screen.findByTestId('sin-acceso', undefined, { timeout: 5000 })).toHaveTextContent(`Falta el permiso «${permissionName('sales.view')}»`);
    expect(screen.queryByTestId('tabla')).not.toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------------------------------- tablero

describe('Ventas · tablero', () => {
  it('«Ventas por día (7 días)» consulta los últimos 7 días al abrirse y enlaza a la lista', async () => {
    const web = await signedInAs('ADMIN');
    const server = fakeServer(web);
    await renderPanel(<SalesByDayStat />, { web: web.services });
    const stat = await screen.findByTestId('ventas-por-dia');
    const from = addDays(TODAY, -6);
    await waitFor(() => expect(within(stat).getByRole('link', { name: 'Ver las ventas de estos 7 días' })).toHaveAttribute('href', `/panel/ventas?desde=${from}&hasta=${TODAY}`));
    expect(server.payloads('GetSalesQuery')).toEqual([{ from, to: TODAY }]);
    expect(stat).toHaveTextContent('Vendido en 7 díasBs 8.670,00');
  });

  it('«Medios de pago» agrupa lo cobrado sin las anuladas', async () => {
    const web = await signedInAs('ADMIN');
    fakeServer(web);
    await renderPanel(<PaymentMethodsStat />, { web: web.services });
    const stat = await screen.findByTestId('medios-de-pago', undefined, { timeout: 5000 });
    expect(stat).toHaveTextContent('Tarjeta1 ventaBs 8.200,00');
    // La anulada (Bs 500 en efectivo) no cuenta.
    expect(stat).toHaveTextContent('Efectivo1 ventaBs 350,00');
    expect(stat).toHaveTextContent('QR1 ventaBs 120,00');
  });

  it('el módulo es válido y ofrece al tablero «Ventas de hoy» y, a quien puede devolver, «Registrar una devolución»', () => {
    expect(REGISTRY.problems).toEqual([]);
    const offered = (role: StaffRole) => dashboardActions(REGISTRY, ROLE_PERMISSIONS[role]).flatMap((group) => group.actions.map((item) => [item.action.label, item.to]));
    expect(offered('ADMIN')).toEqual([
      ['Ventas de hoy', '/panel/ventas'],
      ['Registrar una devolución', '/panel/ventas?devolver=1'],
    ]);
    // Ventas y caja no pueden devolver (les falta anular documentos); la gerencia no opera la caja.
    expect(offered('VENTAS')).toEqual([['Ventas de hoy', '/panel/ventas']]);
    expect(offered('GERENCIA')).toEqual([['Ventas de hoy', '/panel/ventas']]);
    expect(offered('BODEGA')).toEqual([]);
    expect(dashboardStats(REGISTRY, ROLE_PERMISSIONS.CAJERO).map((item) => item.stat.title)).toEqual(['Ventas por día (7 días)', 'Medios de pago']);
    expect(dashboardStats(REGISTRY, ROLE_PERMISSIONS.CONSULTA)).toEqual([]);
  });
});
