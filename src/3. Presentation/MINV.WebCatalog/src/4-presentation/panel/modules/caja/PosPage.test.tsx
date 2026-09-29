// Módulo «Caja» dentro del panel real (esqueleto, rutas y permisos) con un servidor SIMULADO en la prueba: el RPC de la
// sesión en memoria se espía (`vi.spyOn(web.backend.rpc, 'call')`) y responde las operaciones de la caja como el
// servidor; el resto (la sesión, la sucursal) lo atiende el modo mock. Ninguna prueba toca la red. Se prueban el turno
// (abrir y cerrar con arqueo), la lista con filtros, el lector de códigos, las series, el cobro con la forma EXACTA del
// contrato, la factura (comprador, NIT, envío al SIN, comprobante, PDF y correo), vender una reserva, los permisos,
// los errores del servidor, los atajos, la exportación, la estadística y los botones del tablero.

import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { WebApiError } from '@/1-domain/auth/errors';
import { permissionName, type RpcRequestOf, type RpcResponseOf } from '@/4-presentation/app/contract';
import { ROLE_PERMISSIONS, preloadPanel, renderPanel, signedInAs, type MockWeb, type StaffRole } from '@/test-utils';
import { buildRegistry, dashboardActions, dashboardStats } from '../../registry';
import { PanelApp } from '../../shell/PanelApp';
import caja from './module';
import { ShiftSalesStat } from './ShiftSalesStat';

const T = 30_000;
/** Solo la caja: las pruebas no dependen de los demás módulos, que se construyen aparte. */
const REGISTRY = buildRegistry([{ source: '../modules/caja/module.tsx', definition: caja }]);

beforeAll(async () => {
  await preloadPanel();
  await import('./PosPage');
}, 30_000);

afterEach(() => {
  vi.restoreAllMocks();
  document.documentElement.removeAttribute('data-imprimir');
});

// ==================================================================================================== el servidor simulado

type PosSession = NonNullable<RpcResponseOf<'GetPosStateQuery'>['session']>;
type BuildDetail = RpcResponseOf<'GetPcBuildQuery'>;

const SESSION: PosSession = {
  id: 'turno-1',
  registerCode: 'CAJA-CB-01',
  registerName: 'Caja 1',
  openedAt: '2026-09-29T12:05:00Z',
  openingCash: 500,
  tickets: 3,
  sales: 1250,
  cashSales: 750,
  expectedCash: 1250,
};

const FISCAL_OFF: RpcResponseOf<'GetPosFiscalStateQuery'> = {
  billingEnabled: false,
  ready: false,
  message: 'La facturación SIAT está desactivada: las ventas se registran sin documento fiscal.',
  mode: null,
  pointOfSaleCode: null,
  documentTypes: [],
  pendingHomologation: 0,
};

const FISCAL_ON: RpcResponseOf<'GetPosFiscalStateQuery'> = {
  billingEnabled: true,
  ready: true,
  message: '✔ Punto de venta 1 en línea con el SIN.',
  mode: 'Online',
  pointOfSaleCode: 1,
  documentTypes: [1, 2, 3, 4, 5].map((code) => ({ catalog: 'TIPO_DOCUMENTO_IDENTIDAD', code, description: String(code), isCurrent: true })),
  pendingHomologation: 0,
};

const sellable = (sku: string, name: string, categoryCode: string, category: string, price: number, available: number, barcodes: string[] = []) => ({
  variantId: `v-${sku}`,
  sku,
  name,
  categoryCode,
  category,
  unit: 'UND',
  allowsDecimals: false,
  price,
  available,
  barcodes,
});

const PRODUCTS: RpcResponseOf<'GetSellableProductsQuery'> = [
  sellable('MOU-LOG-G502', 'Mouse Logitech G502', 'MOU', 'Mouse', 450, 5, ['7501234500011']),
  sellable('CEL-SAM-A55', 'Celular Samsung A55', 'CEL', 'Celulares', 3200, 2),
  sellable('JUE-PS5-GT7', 'Juego Gran Turismo 7', 'JUE', 'Juegos', 399, 0),
  sellable('CON-SONY-PS5', 'Consola PlayStation 5', 'CON', 'Consolas', 4999, 3),
];

const tech = (sku: string, overrides: Partial<RpcResponseOf<'SearchTechProductsQuery'>[number]> = {}) => ({
  sku,
  name: sku,
  categoryCode: 'X',
  category: 'X',
  brand: null,
  price: 0,
  stock: 0,
  trackSerials: false,
  warrantyMonths: 0,
  keySpecs: '',
  platforms: [] as string[],
  imageId: null,
  serialKind: 'Serial' as const,
  ...overrides,
});

const TECH: RpcResponseOf<'SearchTechProductsQuery'> = [
  tech('MOU-LOG-G502', { brand: 'Logitech' }),
  tech('CEL-SAM-A55', { trackSerials: true, serialKind: 'Imei', warrantyMonths: 12 }),
  tech('JUE-PS5-GT7', { platforms: ['PS5'] }),
  tech('CON-SONY-PS5', { platforms: ['PS5'], trackSerials: false }),
];

/** Condición de cada producto (la filtra el servidor). */
const CONDITIONS: Record<string, string> = { 'CON-SONY-PS5': 'Nuevo', 'JUE-PS5-GT7': 'Usado' };

const spec = (code: string, options: string[]) => ({
  id: code,
  categoryCode: 'CON',
  categoryName: 'Consolas',
  code,
  name: code,
  unit: null,
  dataType: 'Option' as const,
  isMultiValued: false,
  isFilterable: true,
  isRequired: false,
  compatibilityKey: null,
  sortOrder: 1,
  options,
  isInherited: false,
});

const buildRow = (overrides: Partial<BuildDetail['build']>): BuildDetail['build'] => ({
  id: 'b-7',
  number: 'RES-CB-000007',
  name: 'Compra de Ana',
  branchCode: 'CB',
  customer: null,
  status: 'Reserved',
  validUntil: '2099-12-31',
  isExpired: false,
  total: 3500,
  items: 2,
  isCompatible: true,
  createdAt: '2026-09-28T14:00:00Z',
  invoiceNumber: null,
  quotedWithErrors: false,
  channel: 'Web',
  contactName: 'Ana Pérez',
  contactPhone: '71234567',
  contactEmail: 'ana@correo.example',
  reservedUntil: '2099-12-31T14:00:00Z',
  publishedToWeb: false,
  cancelReason: null,
  notes: null,
  reserved: 2,
  kind: 'Cart',
  buyerDocumentType: 5,
  buyerDocumentNumber: '1020304050',
  buyerComplement: null,
  buyerName: 'ACME SRL',
  ...overrides,
});

const item = (sku: string, name: string, quantity: number, unitPrice: number) => ({ slot: null, sku, name, quantity, unitPrice, subtotal: quantity * unitPrice, stock: 5, imageId: null, keySpecs: [] });

const detailOf = (row: BuildDetail['build'], items = [item('CEL-SAM-A55', 'Celular Samsung A55', 1, 3100), item('MOU-LOG-G502', 'Mouse Logitech G502', 1, 400)]): BuildDetail => ({
  build: row,
  check: { items: [], issues: [], isCompatible: true, estimatedDrawW: 0, recommendedPsuW: 0, psuW: null, total: row.total },
  quotedItems: items,
  history: null,
});

const SALE: RpcResponseOf<'CheckoutCommand'> = {
  invoiceNumber: 'F-CB-000124',
  orderNumber: 'PV-CB-000124',
  issuedAt: '2026-09-29T14:30:00Z',
  total: 810,
  tax: 105.3,
  change: 90,
  customer: 'Consumidor final',
  paymentMethod: 'Efectivo',
  lines: [{ description: 'Mouse Logitech G502', quantity: 2, unitPrice: 405, amount: 810, serialsText: null, warrantyUntil: null }],
  fiscalDocumentId: null,
  fiscalNumber: null,
  cuf: null,
  fiscalStatus: null,
};

const FISCAL_SALE: RpcResponseOf<'CheckoutCommand'> = { ...SALE, change: 0, fiscalDocumentId: 'doc-1', fiscalNumber: 45, cuf: 'CUF-PENDIENTE', fiscalStatus: 'Pending' };

const FISCAL_ROW: RpcResponseOf<'DispatchFiscalDocumentsCommand'>['documents'][number] = {
  branchCode: 'CB',
  branchId: 'b-cb',
  buyerDocument: '1020304050',
  buyerName: 'ACME SRL',
  canCreditNote: true,
  canRevert: false,
  canVoid: true,
  cuf: 'CUF-VALIDO-123',
  emissionType: 1,
  id: 'doc-1',
  isReverted: false,
  issuedAt: '2026-09-29T10:30:00.000',
  kind: 'Invoice',
  lastSiatCode: 908,
  number: 45,
  pointOfSaleCode: 1,
  saleNumber: 'F-CB-000124',
  status: 'Valid',
  total: 810,
  voidDeadline: '2026-10-09T23:59:59',
};

const PRINT_MODEL: RpcResponseOf<'GetFiscalPrintModelQuery'> = {
  title: 'FACTURA',
  subtitle: '(Con Derecho A Crédito Fiscal)',
  issuerName: 'TECH ZONE GAMING S.R.L.',
  issuerNit: 1020304050,
  branchLabel: 'Sucursal 1',
  pointOfSaleCode: 1,
  address: 'Av. Heroínas 123',
  phone: null,
  municipality: 'Cochabamba',
  number: 45,
  cuf: 'CUF-VALIDO-123',
  issuedAt: '2026-09-29T10:30:00.000',
  buyerName: 'ACME SRL',
  buyerDocument: '1020304050',
  customerCode: 'CLI-0042',
  lines: [{ productCode: 'MOU-LOG-G502', description: 'Mouse Logitech G502', unit: 'UND', quantity: 2, unitPrice: 405, discount: 0, subtotal: 810, transactionCode: null, serialsText: null, warrantyUntil: null }],
  subtotal: 810,
  discount: 0,
  total: 810,
  giftCard: 0,
  amountToPay: 810,
  taxBase: 810,
  amountInWords: 'OCHOCIENTOS DIEZ 00/100 BOLIVIANOS',
  paymentMethod: 'Efectivo',
  cashier: 'Diego Flores',
  legends: [],
  qrUrl: 'verificacion-de-prueba',
  isTest: true,
  isVoided: false,
  isOffline: false,
  original: null,
  returnedTotal: null,
  creditDebitAmount: null,
  saleNumber: 'F-CB-000124',
};

interface FakeServer {
  session: PosSession | null;
  fiscal: RpcResponseOf<'GetPosFiscalStateQuery'>;
  sale: RpcResponseOf<'CheckoutCommand'>;
  builds: Record<string, BuildDetail>;
  /** Fallas a devolver por operación (la primera vez que se pide). */
  fail: Partial<Record<string, WebApiError>>;
}

function newServer(): FakeServer {
  return { session: { ...SESSION }, fiscal: FISCAL_OFF, sale: SALE, builds: {}, fail: {} };
}

function posState(server: FakeServer): RpcResponseOf<'GetPosStateQuery'> {
  return {
    registers: [
      { code: 'CAJA-CB-01', name: 'Caja 1', opensCashDrawer: false },
      { code: 'CAJA-CB-02', name: 'Caja 2', opensCashDrawer: false },
    ],
    paymentMethods: [
      { code: 'EFECTIVO', name: 'Efectivo', opensCashDrawer: true },
      { code: 'QR', name: 'QR simple', opensCashDrawer: false },
      { code: 'TARJETA', name: 'Tarjeta de débito', opensCashDrawer: false },
    ],
    customers: [
      { code: 'CF', name: 'Consumidor final', opensCashDrawer: false },
      { code: 'CLI-0042', name: 'Ana Pérez', opensCashDrawer: false },
    ],
    session: server.session,
    taxRate: 13,
    companyName: 'Tech Zone Gaming S.R.L.',
    taxId: '1020304050',
    branchName: 'Sucursal Cochabamba',
    suggestedRegister: 'CAJA-CB-02',
    vatOnInvoicedAmount: true,
  };
}

/** Responde como el servidor las operaciones de la caja; lo demás lo atiende el modo mock. */
function install(web: MockWeb, server: FakeServer) {
  const real = web.backend.rpc.call.bind(web.backend.rpc);
  return vi.spyOn(web.backend.rpc, 'call').mockImplementation(async (operation, payload, options) => {
    const reply = (result: unknown) => ({ result, replayed: false, requestId: options?.requestId ?? 'prueba' }) as never;
    const failure = server.fail[operation];
    if (failure) {
      delete server.fail[operation];
      throw failure;
    }
    switch (operation) {
      case 'GetPosStateQuery':
        return reply(posState(server));
      case 'GetPosFiscalStateQuery':
        return reply(server.fiscal);
      case 'GetSellableProductsQuery':
        return reply(PRODUCTS);
      case 'SearchTechProductsQuery': {
        const request = payload as RpcRequestOf<'SearchTechProductsQuery'>;
        const condition = request.filters?.find((filter) => filter.code === 'condicion')?.values?.[0];
        return reply(condition ? TECH.filter((row) => CONDITIONS[row.sku] === condition) : TECH);
      }
      case 'GetStockReservationsQuery':
        return reply([{ sku: 'CON-SONY-PS5', reserved: 1 }]);
      case 'GetSpecDefinitionsQuery':
        return reply([spec('plataforma', ['PS5', 'PC']), spec('condicion', ['Nuevo', 'Usado'])]);
      case 'GetProductLookupQuery':
        return reply([{ variantId: 'v-old', sku: 'MOU-VIEJO', name: 'Mouse viejo', category: 'Mouse', unit: 'UND', allowsDecimals: false, isActive: false, barcodes: ['111'], primaryBin: null }]);
      case 'GetAvailableSerialsQuery': {
        const serial = (value: string) => ({ serial: value, kind: 'Imei', sku: 'CEL-SAM-A55', product: 'Celular', status: 'InStock', branch: 'CB', warehouse: 'Depósito CB', receivedAt: '2026-09-01T12:00:00Z', soldAt: null, invoiceNumber: null, customer: null, warrantyUntil: null });
        return reply((payload as { sku: string }).sku === 'CEL-SAM-A55' ? [serial('356938035643809'), serial('356938035643817')] : []);
      }
      case 'OpenPosSessionCommand': {
        const request = payload as RpcRequestOf<'OpenPosSessionCommand'>;
        server.session = { ...SESSION, registerCode: request.registerCode, registerName: 'Caja 2', openingCash: request.openingCash, tickets: 0, sales: 0, cashSales: 0, expectedCash: request.openingCash };
        return reply('turno-2');
      }
      case 'ClosePosSessionCommand': {
        const request = payload as RpcRequestOf<'ClosePosSessionCommand'>;
        const difference = request.countedCash - (server.session?.expectedCash ?? 0);
        server.session = null;
        return reply(difference);
      }
      case 'CheckoutCommand':
      case 'SellPcBuildCommand':
        return reply(server.sale);
      case 'DispatchFiscalDocumentsCommand':
        return reply({ sent: 1, valid: 1, rejected: 0, wentOffline: 0, documents: [FISCAL_ROW], messages: [] });
      case 'GetFiscalPrintModelQuery':
        return reply(PRINT_MODEL);
      case 'RenderFiscalDocumentQuery':
        return reply({ fileName: 'factura-45.pdf', contentType: 'application/pdf', content: btoa('%PDF-1.4 de prueba') });
      case 'SendFiscalDocumentEmailCommand':
        return reply(`✔ Factura enviada a ${(payload as { email: string }).email}`);
      case 'FindFiscalBuyerQuery':
        return reply({ found: true, customerCode: 'CLI-0042', name: 'ACME SRL', email: null, documentType: 5, documentNumber: '1020304050', complement: null, nitValid: null });
      case 'VerifyNitCommand':
        return reply({ nit: (payload as { nit: number }).nit, isValid: true, siatCode: 986, description: 'NIT ACTIVO', checked: true });
      case 'GetPcBuildQuery': {
        const found = server.builds[(payload as { number: string }).number];
        if (!found) throw new WebApiError({ kind: 'not_found', status: 404, message: 'La reserva no existe.' });
        return reply(found);
      }
      case 'GetPcBuildsQuery':
        return reply(Object.values(server.builds).map((detail) => detail.build));
      default:
        return real(operation, payload as never, options);
    }
  });
}

interface Opened {
  web: MockWeb;
  server: FakeServer;
  location: () => string;
  /** Lo que la pantalla pidió de una operación, en orden. */
  sent: (operation: string) => unknown[];
}

async function openCaja(options: { role?: StaffRole; query?: string; setup?: (server: FakeServer) => void; web?: MockWeb } = {}): Promise<Opened> {
  const web = options.web ?? (await signedInAs(options.role ?? 'CAJERO'));
  const server = newServer();
  options.setup?.(server);
  const call = install(web, server);
  const view = await renderPanel(<PanelApp registry={REGISTRY} />, { web: web.services, route: `/panel/caja${options.query ?? ''}`, path: '/panel/*' });
  return { web, server, location: view.location, sent: (operation) => call.mock.calls.filter(([name]) => name === operation).map(([, payload]) => payload) };
}

async function productTable(): Promise<HTMLElement> {
  const found = await screen.findByTestId('tabla', undefined, { timeout: 10_000 });
  await waitFor(() => expect(within(found).queryAllByRole('row').filter((row) => row.hasAttribute('data-row-key')).length).toBeGreaterThan(0), { timeout: 10_000 });
  return found;
}

function dataRows(table: HTMLElement): HTMLElement[] {
  return within(table)
    .getAllByRole('row')
    .filter((row) => row.hasAttribute('data-row-key'));
}

async function add(name: string) {
  await productTable();
  const button = await screen.findByRole('button', { name: new RegExp(`^(Agregar|Elegir IMEI|Elegir serie): ${name}$`) });
  await waitFor(() => expect(button).toBeEnabled());
  fireEvent.click(button);
}

function cartLines(): HTMLElement[] {
  return screen.queryAllByTestId('linea-venta');
}

/** F4 (con el turno ya leído del servidor: antes, la caja todavía no sabe si puede cobrar). */
async function openCheckout(title = 'Cobrar la venta'): Promise<HTMLElement> {
  await screen.findByTestId('turno-resumen');
  fireEvent.keyDown(document.body, { key: 'F4' });
  return screen.findByRole('dialog', { name: title });
}

// ==================================================================================================== pruebas

describe('Caja · turno', () => {
  it(
    'sin turno abierto ofrece «Abrir caja»: valida el fondo, abre la caja sugerida y muestra el resumen del turno',
    async () => {
      const { sent } = await openCaja({ setup: (server) => (server.session = null) });
      expect(await screen.findByText('La caja está cerrada', undefined, { timeout: 10_000 })).toBeInTheDocument();
      expect(screen.getByRole('heading', { level: 1, name: 'Caja' })).toBeInTheDocument();
      expect(document.title).toBe('Caja · Panel · Tech Zone Gaming');
      fireEvent.click(within(screen.getByTestId('turno')).getByRole('button', { name: 'Abrir caja' }));
      const dialog = await screen.findByRole('dialog', { name: 'Abrir la caja' });
      expect(within(dialog).getByRole('combobox', { name: 'Caja' })).toHaveValue('CAJA-CB-02');
      fireEvent.click(within(dialog).getByRole('button', { name: 'Abrir caja' }));
      expect(await within(dialog).findByText('Indique el efectivo con el que abre la caja (0 o más).')).toBeInTheDocument();
      fireEvent.click(within(dialog).getByRole('button', { name: 'Bs 200,00' }));
      await act(async () => {
        fireEvent.click(within(dialog).getByRole('button', { name: 'Abrir caja' }));
      });
      expect(await screen.findByText('Caja abierta con un fondo de Bs 200,00')).toBeInTheDocument();
      expect(sent('OpenPosSessionCommand')).toEqual([{ registerCode: 'CAJA-CB-02', openingCash: 200 }]);
      expect(await screen.findByTestId('turno-resumen')).toHaveTextContent('0 ventas por Bs 0,00 · efectivo esperado Bs 200,00');
    },
    T,
  );

  it(
    'cierra la caja con el arqueo (esperado, contado, diferencia) y confirmación',
    async () => {
      const { sent } = await openCaja();
      expect(await screen.findByTestId('turno-resumen', undefined, { timeout: 10_000 })).toHaveTextContent('3 ventas por Bs 1.250,00 · efectivo esperado Bs 1.250,00');
      fireEvent.click(screen.getByRole('button', { name: 'Cerrar caja' }));
      const dialog = await screen.findByRole('alertdialog', { name: '¿Cerrar la caja?' });
      expect(within(dialog).getByTestId('arqueo')).toHaveTextContent('Efectivo esperadoBs 1.250,00');
      const confirm = within(dialog).getByRole('button', { name: 'Cerrar caja' });
      expect(confirm).toBeDisabled();
      fireEvent.change(within(dialog).getByLabelText(/^Efectivo contado/), { target: { value: '1200' } });
      expect(within(dialog).getByText('Faltante de Bs 50,00.')).toBeInTheDocument();
      await act(async () => {
        fireEvent.click(confirm);
      });
      expect(sent('ClosePosSessionCommand')).toEqual([{ sessionId: 'turno-1', countedCash: 1200 }]);
      expect(await screen.findByText('La caja está cerrada')).toBeInTheDocument();
      // El aviso con la diferencia que devolvió el servidor.
      await waitFor(() => expect(screen.queryByRole('alertdialog', { name: '¿Cerrar la caja?' })).not.toBeInTheDocument());
      expect(screen.getByText('Faltante de Bs 50,00.')).toBeInTheDocument();
    },
    T,
  );

  it(
    'si el estado de la caja no carga, muestra el error con «Reintentar» y se recupera',
    async () => {
      await openCaja({ setup: (server) => (server.fail.GetPosStateQuery = new WebApiError({ kind: 'network', message: 'Sin conexión' })) });
      const error = await screen.findByTestId('estado-error', undefined, { timeout: 10_000 });
      expect(error).toHaveTextContent('No se pudo leer el estado de la caja');
      fireEvent.click(within(error).getByRole('button', { name: 'Reintentar' }));
      expect(await screen.findByTestId('turno-resumen')).toBeInTheDocument();
    },
    T,
  );
});

describe('Caja · productos y venta', () => {
  it(
    'lista lo que se vende (disponible sin lo reservado) y filtra con listas desplegables de las opciones del servidor',
    async () => {
      const { location, sent } = await openCaja();
      const table = await productTable();
      expect(dataRows(table)).toHaveLength(4);
      expect(screen.getByTestId('productos-resumen')).toHaveTextContent('4 de 4 productos · 3 con stock');
      expect(within(table).getByText('3 UND (reservado 1)')).toBeInTheDocument();
      expect(within(table).getByText('Agotado')).toBeInTheDocument();

      const category = screen.getByRole('combobox', { name: 'Categoría' });
      expect(within(category).getByRole('option', { name: 'Consolas (1)' })).toBeInTheDocument();
      fireEvent.change(category, { target: { value: 'JUE' } });
      await waitFor(() => expect(location()).toBe('/panel/caja?categoria=JUE'));
      await waitFor(() => expect(dataRows(table)).toHaveLength(1));

      fireEvent.change(category, { target: { value: '' } });
      fireEvent.change(screen.getByRole('combobox', { name: 'Plataforma' }), { target: { value: 'PS5' } });
      await waitFor(() => expect(dataRows(table)).toHaveLength(2));

      fireEvent.change(screen.getByRole('combobox', { name: 'Condición' }), { target: { value: 'Nuevo' } });
      await waitFor(() => expect(dataRows(table).map((row) => row.getAttribute('data-row-key'))).toEqual(['CON-SONY-PS5']));
      expect(sent('SearchTechProductsQuery')).toContainEqual({
        text: null,
        categoryCode: null,
        platform: null,
        filters: [{ code: 'condicion', values: ['Nuevo'], min: null, max: null }],
        onlyInStock: false,
        max: 2000,
      });
      fireEvent.click(screen.getByRole('button', { name: 'Limpiar filtros' }));
      await waitFor(() => expect(location()).toBe('/panel/caja'));
    },
    T,
  );

  it(
    'el lector de códigos agrega con Enter (F2 lleva al buscador) y un código que no está a la venta se explica',
    async () => {
      await openCaja();
      await productTable();
      const search = screen.getByRole('searchbox', { name: /Buscar o escanear/ });
      fireEvent.keyDown(document.body, { key: 'F2' });
      expect(search).toHaveFocus();
      for (let scan = 0; scan < 2; scan += 1) {
        fireEvent.change(search, { target: { value: '7501234500011' } });
        fireEvent.keyDown(search, { key: 'Enter' });
      }
      await waitFor(() => expect(cartLines()).toHaveLength(1));
      expect(search).toHaveValue('');
      expect(screen.getByLabelText('Cantidad de Mouse Logitech G502')).toHaveValue('2');
      expect(screen.getByTestId('venta-total')).toHaveTextContent('Bs 900,00');

      fireEvent.change(search, { target: { value: 'MOU-VIEJO' } });
      fireEvent.keyDown(search, { key: 'Enter' });
      expect(await screen.findByTestId('codigo-no-encontrado')).toHaveTextContent('«Mouse viejo» (MOU-VIEJO) está inactivo y no se vende.');
    },
    T,
  );

  it(
    'un producto con IMEI se agrega eligiendo la unidad disponible (escaneada o marcada)',
    async () => {
      const { sent } = await openCaja();
      await add('Celular Samsung A55');
      const dialog = await screen.findByRole('dialog', { name: 'IMEI de Celular Samsung A55' });
      const scan = within(dialog).getByRole('searchbox', { name: 'Escanear o buscar el IMEI' });
      await within(dialog).findByRole('checkbox', { name: '356938035643809' });
      fireEvent.change(scan, { target: { value: '356938035643809' } });
      fireEvent.keyDown(scan, { key: 'Enter' });
      expect(within(dialog).getByTestId('series-elegidas')).toHaveTextContent('1 unidad elegida');
      fireEvent.click(within(dialog).getByRole('button', { name: 'Agregar a la venta' }));
      await waitFor(() => expect(cartLines()).toHaveLength(1));
      expect(cartLines()[0]).toHaveTextContent('IMEI: 356938035643809');
      expect(sent('GetAvailableSerialsQuery')).toEqual([{ sku: 'CEL-SAM-A55', warehouseCode: null }]);
    },
    T,
  );

  it(
    'lo agotado no se agrega y la venta avisa (y no cobra) si pide más de lo disponible',
    async () => {
      await openCaja();
      await productTable();
      expect(screen.getByRole('button', { name: 'Agregar: Juego Gran Turismo 7' })).toBeDisabled();
      await add('Mouse Logitech G502');
      fireEvent.change(screen.getByLabelText('Cantidad de Mouse Logitech G502'), { target: { value: '6' } });
      expect(await screen.findByText('Supera lo disponible: hay 5.')).toBeInTheDocument();
      await screen.findByTestId('turno-resumen');
      fireEvent.keyDown(document.body, { key: 'F4' });
      expect(await screen.findByText('Mouse Logitech G502: pide 6 y hay 5. Baje la cantidad o libere la reserva.')).toBeInTheDocument();
      expect(screen.queryByRole('dialog', { name: 'Cobrar la venta' })).not.toBeInTheDocument();
    },
    T,
  );

  it(
    'cobra en efectivo: valida el monto recibido, envía `CheckoutCommand` exacto, muestra el vuelto y deja lista la siguiente venta',
    async () => {
      const { sent } = await openCaja();
      await add('Mouse Logitech G502');
      await add('Mouse Logitech G502');
      fireEvent.change(screen.getByRole('combobox', { name: 'Descuento de Mouse Logitech G502' }), { target: { value: '10' } });
      await waitFor(() => expect(screen.getByTestId('venta-total')).toHaveTextContent('Bs 810,00'));

      const dialog = await openCheckout();
      expect(within(dialog).getByTestId('cobro-total')).toHaveTextContent('Bs 810,00');
      const cash = within(dialog).getByLabelText(/^Efectivo recibido/);
      fireEvent.change(cash, { target: { value: '800' } });
      fireEvent.click(within(dialog).getByRole('button', { name: 'Cobrar Bs 810,00' }));
      expect(await within(dialog).findByText('Recibió Bs 800,00 y el total es Bs 810,00.')).toBeInTheDocument();
      expect(sent('CheckoutCommand')).toEqual([]);

      fireEvent.change(cash, { target: { value: '900' } });
      expect(within(dialog).getByTestId('vuelto')).toHaveTextContent('Vuelto: Bs 90,00');
      await act(async () => {
        fireEvent.click(within(dialog).getByRole('button', { name: 'Cobrar Bs 810,00' }));
      });
      expect(sent('CheckoutCommand')).toEqual([
        {
          customerCode: 'CF',
          paymentMethodCode: 'EFECTIVO',
          lines: [{ sku: 'MOU-LOG-G502', quantity: 2, discountPercent: 10, serials: null }],
          cashReceived: 900,
          paymentReference: null,
          buyer: null,
          cardNumber: null,
        },
      ]);
      const result = await screen.findByRole('dialog', { name: 'Venta F-CB-000124 cobrada' });
      expect(within(result).getByTestId('vuelto-entregar')).toHaveTextContent('Entregue vuelto de Bs 90,00');
      expect(sent('DispatchFiscalDocumentsCommand')).toEqual([]);
      fireEvent.click(within(result).getByRole('button', { name: 'Siguiente venta' }));
      await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Venta F-CB-000124 cobrada' })).not.toBeInTheDocument());
      expect(cartLines()).toHaveLength(0);
    },
    T,
  );

  it(
    'un medio que no es efectivo exige la referencia; el error del servidor se muestra en el cobro sin cerrarlo',
    async () => {
      const { sent } = await openCaja({
        setup: (server) =>
          (server.fail.CheckoutCommand = new WebApiError({ kind: 'domain', status: 422, code: 'stock.insufficient', message: 'No hay stock suficiente de MOU-LOG-G502: disponible 0.' })),
      });
      await add('Mouse Logitech G502');
      const dialog = await openCheckout();
      fireEvent.change(within(dialog).getByRole('combobox', { name: 'Medio de pago' }), { target: { value: 'QR' } });
      fireEvent.click(within(dialog).getByRole('button', { name: 'Cobrar Bs 450,00' }));
      expect(await within(dialog).findByText('El pago con QR simple exige el número de operación, voucher o referencia.')).toBeInTheDocument();
      fireEvent.change(within(dialog).getByLabelText(/^Referencia del pago/), { target: { value: 'QR-778' } });
      await act(async () => {
        fireEvent.click(within(dialog).getByRole('button', { name: 'Cobrar Bs 450,00' }));
      });
      expect(await within(dialog).findByText('No hay stock suficiente de MOU-LOG-G502: disponible 0.')).toBeInTheDocument();
      expect(screen.getByRole('dialog', { name: 'Cobrar la venta' })).toBeInTheDocument();
      expect(sent('CheckoutCommand')).toEqual([
        { customerCode: 'CF', paymentMethodCode: 'QR', lines: [{ sku: 'MOU-LOG-G502', quantity: 1, discountPercent: 0, serials: null }], cashReceived: null, paymentReference: 'QR-778', buyer: null, cardNumber: null },
      ]);
    },
    T,
  );

  it(
    'exporta la lista de productos a CSV',
    async () => {
      const created: Blob[] = [];
      vi.spyOn(URL, 'createObjectURL').mockImplementation((blob) => {
        created.push(blob as Blob);
        return 'blob:prueba';
      });
      vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
      vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
      await openCaja({ query: '?categoria=MOU' });
      await productTable();
      fireEvent.click(screen.getByRole('button', { name: 'Exportar CSV' }));
      expect(await screen.findByText(/^Se descargó productos-caja-\d{4}-\d{2}-\d{2}\.csv \(1 filas\)\.$/)).toBeInTheDocument();
      const lines = (await created[0].text()).replace(/^﻿/, '').trim().split('\r\n');
      expect(lines[1]).toBe('"MOU-LOG-G502";"Mouse Logitech G502";"Mouse";"Logitech";450;5;0;"UND";"No";;;"7501234500011"');
    },
    T,
  );
});

describe('Caja · factura del SIN', () => {
  /** Cobra dos mouse con NIT, con la facturación encendida (busca al comprador y verifica el NIT). */
  async function sellWithInvoice(opened: Opened) {
    await add('Mouse Logitech G502');
    await add('Mouse Logitech G502');
    fireEvent.change(screen.getByRole('combobox', { name: 'Descuento de Mouse Logitech G502' }), { target: { value: '10' } });
    const dialog = await openCheckout();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Cobrar Bs 810,00' }));
    expect(await within(dialog).findByText(/^Toda venta facturada lleva el documento del comprador/)).toBeInTheDocument();

    fireEvent.change(within(dialog).getByRole('combobox', { name: 'Tipo de documento' }), { target: { value: '5' } });
    const number = within(dialog).getByRole('textbox', { name: /^Número de documento/ });
    fireEvent.change(number, { target: { value: '1020304050' } });
    await act(async () => {
      fireEvent.blur(number);
    });
    expect(await within(dialog).findByText('Cliente CLI-0042: ya compró antes.')).toBeInTheDocument();
    expect(within(dialog).getByRole('textbox', { name: /^Nombre o razón social/ })).toHaveValue('ACME SRL');
    expect(within(dialog).getByRole('combobox', { name: 'Cliente' })).toHaveValue('Ana Pérez');
    expect(opened.sent('FindFiscalBuyerQuery')).toEqual([{ documentType: 5, documentNumber: '1020304050', complement: null }]);

    return dialog;
  }

  it(
    'pide el documento del comprador, busca si ya compró, verifica el NIT y envía la factura al SIN',
    async () => {
      const opened = await openCaja({ setup: (server) => ((server.fiscal = FISCAL_ON), (server.sale = FISCAL_SALE)) });
      expect(await screen.findByText('Facturación en línea · punto de venta 1', undefined, { timeout: 10_000 })).toBeInTheDocument();
      const dialog = await sellWithInvoice(opened);
      await act(async () => {
        fireEvent.click(within(dialog).getByRole('button', { name: 'Verificar NIT' }));
      });
      expect(await within(dialog).findByText('NIT activo en el Padrón · NIT ACTIVO')).toBeInTheDocument();
      expect(opened.sent('VerifyNitCommand')).toEqual([{ nit: 1020304050, customerCode: 'CLI-0042' }]);
      fireEvent.change(within(dialog).getByRole('textbox', { name: /^Correo para enviar la factura/ }), { target: { value: 'acme@correo.example' } });
      await act(async () => {
        fireEvent.click(within(dialog).getByRole('button', { name: 'Cobrar Bs 810,00' }));
      });
      expect(opened.sent('CheckoutCommand')).toEqual([
        {
          customerCode: 'CLI-0042',
          paymentMethodCode: 'EFECTIVO',
          lines: [{ sku: 'MOU-LOG-G502', quantity: 2, discountPercent: 10, serials: null }],
          cashReceived: null,
          paymentReference: null,
          buyer: { documentType: 5, documentNumber: '1020304050', complement: null, name: 'ACME SRL', email: 'acme@correo.example', exceptionRequested: false },
          cardNumber: null,
        },
      ]);
      const result = await screen.findByRole('dialog', { name: 'Venta F-CB-000124 cobrada' });
      expect(await within(result).findByText('Factura N° 45 válida')).toBeInTheDocument();
      expect(within(result).getByTestId('factura-sin')).toHaveTextContent('CUF-VALIDO-123');
      expect(opened.sent('DispatchFiscalDocumentsCommand')).toEqual([{ documentId: 'doc-1', max: 50 }]);
    },
    T,
  );

  it(
    'imprime el comprobante (vista imprimible del navegador), descarga el PDF y envía la factura por correo',
    async () => {
      const print = vi.spyOn(window, 'print').mockImplementation(() => undefined);
      vi.spyOn(URL, 'createObjectURL').mockImplementation(() => 'blob:factura');
      vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
      vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
      const opened = await openCaja({ setup: (server) => ((server.fiscal = FISCAL_ON), (server.sale = FISCAL_SALE)) });
      const dialog = await sellWithInvoice(opened);
      fireEvent.change(within(dialog).getByRole('textbox', { name: /^Correo para enviar la factura/ }), { target: { value: 'acme@correo.example' } });
      await act(async () => {
        fireEvent.click(within(dialog).getByRole('button', { name: 'Cobrar Bs 810,00' }));
      });
      const result = await screen.findByRole('dialog', { name: 'Venta F-CB-000124 cobrada' });
      await within(result).findByText('Factura N° 45 válida');

      const printButton = within(result).getByRole('button', { name: 'Imprimir comprobante' });
      await waitFor(() => expect(printButton).toBeEnabled());
      fireEvent.click(printButton);
      const sheet = await screen.findByTestId('comprobante-impresion');
      expect(print).toHaveBeenCalledTimes(1);
      expect(document.documentElement).toHaveAttribute('data-imprimir', 'caja');
      expect(sheet).toHaveTextContent('SIN VALOR LEGAL · ambiente de pruebas del SIN');
      expect(sheet).toHaveTextContent('CUF-VALIDO-123');
      expect(opened.sent('GetFiscalPrintModelQuery')).toEqual([{ documentId: 'doc-1' }]);
      act(() => {
        window.dispatchEvent(new Event('afterprint'));
      });
      await waitFor(() => expect(screen.queryByTestId('comprobante-impresion')).not.toBeInTheDocument());
      expect(document.documentElement).not.toHaveAttribute('data-imprimir');

      await act(async () => {
        fireEvent.click(within(result).getByRole('button', { name: 'Descargar PDF' }));
      });
      expect(await screen.findByText('Se descargó factura-45.pdf.')).toBeInTheDocument();
      expect(opened.sent('RenderFiscalDocumentQuery')).toEqual([{ documentId: 'doc-1', format: 'Pdf', columns: 48 }]);

      fireEvent.click(within(result).getByRole('button', { name: 'Enviar por correo' }));
      const email = await screen.findByRole('dialog', { name: 'Enviar la factura N° 45 por correo' });
      expect(within(email).getByRole('textbox', { name: /^Correo del comprador/ })).toHaveValue('acme@correo.example');
      await act(async () => {
        fireEvent.click(within(email).getByRole('button', { name: 'Enviar' }));
      });
      expect(await screen.findByText('Factura enviada a acme@correo.example')).toBeInTheDocument();
      expect(opened.sent('SendFiscalDocumentEmailCommand')).toEqual([{ documentId: 'doc-1', email: 'acme@correo.example' }]);
    },
    T,
  );

  it(
    'sin `billing.issue` no ofrece verificar el NIT ni envía la factura (se envía sola)',
    async () => {
      const web = await signedInAs('CAJERO');
      const real = await web.backend.session.current();
      vi.spyOn(web.backend.session, 'current').mockResolvedValue({ ...real!, permissions: real!.permissions.filter((code) => code !== 'billing.issue') });
      const opened = await openCaja({ web, setup: (server) => ((server.fiscal = FISCAL_ON), (server.sale = FISCAL_SALE)) });
      await add('Mouse Logitech G502');
      const dialog = await openCheckout();
      fireEvent.change(within(dialog).getByRole('combobox', { name: 'Tipo de documento' }), { target: { value: '5' } });
      const number = within(dialog).getByRole('textbox', { name: /^Número de documento/ });
      fireEvent.change(number, { target: { value: '1020304050' } });
      fireEvent.blur(number);
      expect(within(dialog).queryByRole('button', { name: 'Verificar NIT' })).not.toBeInTheDocument();
      await act(async () => {
        fireEvent.click(within(dialog).getByRole('button', { name: 'Cobrar Bs 450,00' }));
      });
      const result = await screen.findByRole('dialog', { name: 'Venta F-CB-000124 cobrada' });
      expect(within(result).getByText('La factura se envía al SIN automáticamente en los próximos segundos.')).toBeInTheDocument();
      expect(within(result).queryByRole('button', { name: 'Enviar por correo' })).not.toBeInTheDocument();
      expect(opened.sent('FindFiscalBuyerQuery')).toEqual([]);
      expect(opened.sent('DispatchFiscalDocumentsCommand')).toEqual([]);
    },
    T,
  );
});

describe('Caja · vender una reserva', () => {
  it(
    'carga la reserva de la dirección: precio congelado, datos de factura precargados, series y `SellPcBuildCommand` exacto',
    async () => {
      const opened = await openCaja({
        query: '?reserva=res-cb-000007',
        setup: (server) => {
          server.fiscal = FISCAL_ON;
          server.sale = { ...FISCAL_SALE, total: 3500 };
          server.builds['RES-CB-000007'] = detailOf(buildRow({}));
        },
      });
      const banner = await screen.findByTestId('reserva-en-venta', undefined, { timeout: 10_000 });
      expect(banner).toHaveTextContent('Reserva RES-CB-000007');
      expect(banner).toHaveTextContent('al cobrar se consume la reserva');
      expect(opened.sent('GetPcBuildQuery')).toContainEqual({ number: 'RES-CB-000007' });
      expect(cartLines()).toHaveLength(2);
      expect(cartLines()[0]).toHaveTextContent('Bs 3.100,00 por UND · precio congelado');
      expect(screen.getByTestId('venta-total')).toHaveTextContent('Bs 3.500,00');
      // Con la reserva cargada no se mezclan otros productos.
      expect(screen.getByRole('button', { name: 'Agregar: Consola PlayStation 5' })).toBeDisabled();

      await screen.findByTestId('turno-resumen');
      fireEvent.keyDown(document.body, { key: 'F4' });
      expect(await screen.findByText('Celular Samsung A55: Elija 1 IMEI (tiene 0).')).toBeInTheDocument();
      fireEvent.click(screen.getByRole('button', { name: 'Elegir las unidades' }));
      const serials = await screen.findByRole('dialog', { name: 'IMEI de Celular Samsung A55' });
      fireEvent.click(await within(serials).findByRole('checkbox', { name: '356938035643809' }));
      expect(within(serials).getByTestId('series-elegidas')).toHaveTextContent('Elegidas 1 de 1');
      fireEvent.click(within(serials).getByRole('button', { name: 'Usar estas unidades' }));
      await waitFor(() => expect(screen.queryByRole('dialog', { name: 'IMEI de Celular Samsung A55' })).not.toBeInTheDocument());

      const dialog = await openCheckout('Cobrar la reserva RES-CB-000007');
      expect(within(dialog).getByRole('textbox', { name: /^Número de documento/ })).toHaveValue('1020304050');
      await act(async () => {
        fireEvent.click(within(dialog).getByRole('button', { name: 'Cobrar Bs 3.500,00' }));
      });
      expect(opened.sent('SellPcBuildCommand')).toEqual([
        {
          number: 'RES-CB-000007',
          paymentMethodCode: 'EFECTIVO',
          serials: [{ sku: 'CEL-SAM-A55', serials: ['356938035643809'] }],
          cashReceived: null,
          paymentReference: null,
          buyer: { documentType: 5, documentNumber: '1020304050', complement: null, name: 'ACME SRL', email: 'ana@correo.example', exceptionRequested: false },
          cardNumber: null,
          customerCode: 'CF',
        },
      ]);
      expect(opened.sent('CheckoutCommand')).toEqual([]);
      const result = await screen.findByRole('dialog', { name: 'Venta F-CB-000124 cobrada' });
      expect(result).toHaveTextContent('Se cobró la reserva RES-CB-000007: la reserva se consumió con la venta.');
      await waitFor(() => expect(opened.location()).toBe('/panel/caja'));
    },
    T,
  );

  it(
    'una reserva que ya se vendió no se carga: se explica y se puede quitar',
    async () => {
      const { location } = await openCaja({
        query: '?reserva=RES-CB-000009',
        setup: (server) => (server.builds['RES-CB-000009'] = detailOf(buildRow({ number: 'RES-CB-000009', status: 'Sold', invoiceNumber: 'F-CB-000100' }))),
      });
      expect(await screen.findByText('La reserva RES-CB-000009 ya se vendió (venta F-CB-000100).', undefined, { timeout: 10_000 })).toBeInTheDocument();
      expect(cartLines()).toHaveLength(0);
      fireEvent.click(screen.getByRole('button', { name: 'Quitar' }));
      await waitFor(() => expect(location()).toBe('/panel/caja'));
    },
    T,
  );

  it(
    '«Vender una reserva» lista solo las vigentes y carga la elegida',
    async () => {
      const { location, sent } = await openCaja({
        query: '?elegir=reserva',
        setup: (server) => {
          server.builds['RES-CB-000007'] = detailOf(buildRow({}));
          server.builds['RES-CB-000009'] = detailOf(buildRow({ number: 'RES-CB-000009', status: 'Sold' }));
        },
      });
      const dialog = await screen.findByRole('dialog', { name: 'Vender una reserva o cotización' }, { timeout: 10_000 });
      expect(await within(dialog).findByTestId('reservas-resumen')).toHaveTextContent('1 de 1 vigentes');
      expect(sent('GetPcBuildsQuery')).toEqual([{ status: null, channel: null, kind: null }]);
      fireEvent.click(within(dialog).getByRole('button', { name: /^RES-CB-000007/ }));
      await waitFor(() => expect(location()).toBe('/panel/caja?reserva=RES-CB-000007'));
      expect(await screen.findByTestId('reserva-en-venta')).toHaveTextContent('Reserva RES-CB-000007');
    },
    T,
  );
});

describe('Caja · permisos, estadística y tablero', () => {
  it(
    'un rol sin el permiso de la caja ve «No tiene acceso a esta pantalla» con el permiso que falta',
    async () => {
      const { sent } = await openCaja({ role: 'GERENCIA' });
      expect(await screen.findByTestId('sin-acceso', undefined, { timeout: 10_000 })).toHaveTextContent(permissionName('sales.pos.operate'));
      expect(sent('GetPosStateQuery')).toEqual([]);
    },
    T,
  );

  it(
    'la estadística «Ventas de mi turno» muestra el turno abierto o avisa que no hay',
    async () => {
      const web = await signedInAs('CAJERO');
      const server = newServer();
      install(web, server);
      const view = await renderPanel(<ShiftSalesStat />, { web: web.services });
      const stat = await screen.findByTestId('ventas-de-mi-turno');
      await waitFor(() => expect(stat).toHaveTextContent('Ventas3'));
      expect(stat).toHaveTextContent('Total vendidoBs 1.250,00');
      expect(stat).toHaveTextContent('Efectivo esperado en la cajaBs 1.250,00');
      view.unmount();

      server.session = null;
      await renderPanel(<ShiftSalesStat />, { web: web.services });
      expect(await screen.findByText(/^No tiene un turno de caja abierto/)).toBeInTheDocument();
    },
    T,
  );

  it('el módulo es válido y ofrece al tablero «Ir a la caja», «Vender una reserva» y «Ventas de mi turno» solo a quien vende', () => {
    expect(REGISTRY.problems).toEqual([]);
    const actions = (role: StaffRole) => dashboardActions(REGISTRY, ROLE_PERMISSIONS[role]).flatMap((group) => group.actions.map((item) => `${item.action.label} → ${item.to}`));
    expect(actions('CAJERO')).toEqual(['Ir a la caja → /panel/caja', 'Vender una reserva → /panel/caja?elegir=reserva']);
    expect(actions('VENTAS')).toEqual(['Ir a la caja → /panel/caja', 'Vender una reserva → /panel/caja?elegir=reserva']);
    expect(actions('GERENCIA')).toEqual([]);
    expect(actions('BODEGA')).toEqual([]);
    expect(dashboardStats(REGISTRY, ROLE_PERMISSIONS.CAJERO).map((item) => item.stat.title)).toEqual(['Ventas de mi turno']);
    expect(dashboardStats(REGISTRY, ROLE_PERMISSIONS.CONSULTA)).toEqual([]);
  });

  it(
    'no se pierde la venta: salir de la caja con productos en la venta pide confirmación',
    async () => {
      const { location } = await openCaja();
      await add('Mouse Logitech G502');
      await waitFor(() => expect(cartLines()).toHaveLength(1));
      fireEvent.click(screen.getAllByRole('link', { name: 'Panel del personal, ir al inicio' })[0]);
      const dialog = await screen.findByRole('alertdialog', { name: '¿Salir de la caja?' });
      expect(dialog).toHaveTextContent('La venta en curso tiene 1 producto · 1 unidad.');
      fireEvent.click(within(dialog).getByRole('button', { name: 'Seguir en la caja' }));
      await waitFor(() => expect(screen.queryByRole('alertdialog', { name: '¿Salir de la caja?' })).not.toBeInTheDocument());
      expect(location()).toBe('/panel/caja');
      expect(cartLines()).toHaveLength(1);

      fireEvent.click(screen.getAllByRole('link', { name: 'Panel del personal, ir al inicio' })[0]);
      fireEvent.click(within(await screen.findByRole('alertdialog', { name: '¿Salir de la caja?' })).getByRole('button', { name: 'Salir y descartar la venta' }));
      await waitFor(() => expect(location()).toBe('/panel'));
    },
    T,
  );
});
