// Módulo «Estado del SIAT» dentro del panel real, con un registro que tiene SOLO este módulo. El servidor de la
// facturación se simula en la prueba (el modo mock no atiende estas operaciones): cabecera, alertas con cuenta regresiva,
// indicadores plegados, tarjetas de los puntos de venta con sus acciones y confirmaciones, contingencia manual,
// eventos, paquetes, talonarios CAFC, transcripción de facturas manuales (forma exacta del contrato), la bitácora técnica
// plegada, errores del servidor, permisos por rol, la estadística y el tablero. Ninguna prueba toca la red. La hora queda
// fija: 29/09/2026 10:00 en La Paz.

import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { WebApiError } from '@/1-domain/auth/errors';
import type { RpcResponseOf } from '@/4-presentation/app/contract';
import { renderPanel, signedInAs, type MockWeb, type StaffRole } from '@/test-utils';
import { buildRegistry, dashboardActions, dashboardStats } from '../../registry';
import { PanelApp } from '../../shell/PanelApp';
import siat from './module';
import type { CafcData, CatalogItemData, EventData, PackageData, PointData, ProductData, ServiceCallData, SiatStatusData } from './siat';
import { SiatStatusStat } from './SiatStatusStat';

const REGISTRY = buildRegistry([{ source: '../modules/siat/module.tsx', definition: siat }]);

/** 10:00 de La Paz del 29/09/2026. */
const NOW = new Date('2026-09-29T14:00:00Z');
const hours = (value: number) => new Date(NOW.getTime() + value * 3_600_000).toISOString();

beforeAll(async () => {
  await import('../../shell/PanelLayout');
  await import('./SiatPage');
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

function point(overrides: Partial<PointData>): PointData {
  return {
    branchCode: 'CM',
    branchId: 'b-cm',
    branchName: 'Casa matriz',
    code: 1,
    cufdObtainedAt: hours(-4),
    cufdValidUntil: hours(20),
    cuisValidUntil: hours(24 * 200),
    environment: 2,
    id: 'pv-cm-1',
    isClosed: false,
    lastContactAt: hours(-0.1),
    lastError: null,
    mode: 'Online',
    modeSince: hours(-48),
    name: 'Caja principal',
    offlineDocuments: 0,
    openEvent: null,
    pendingDocuments: 0,
    registerCode: 'CAJA01',
    retryAt: null,
    siatBranchCode: 0,
    ...overrides,
  };
}

function event(overrides: Partial<EventData>): EventData {
  return {
    branchCode: 'CM',
    branchId: 'b-cm',
    cafc: null,
    description: 'CORTE DEL SERVICIO DE INTERNET',
    documents: 0,
    endedAt: null,
    eventCode: 1,
    id: 'ev-x',
    kind: 'Offline',
    pointOfSaleCode: 1,
    receptionCode: null,
    registrationDeadline: null,
    startedAt: '2026-09-29T08:00:00',
    status: 'Open',
    transcriptionDeadline: null,
    ...overrides,
  };
}

const EVENTS: EventData[] = [
  event({ id: 'ev-cb', branchCode: 'CB', branchId: 'b-cb', pointOfSaleCode: 2, documents: 3, registrationDeadline: '2026-10-01T08:00:00' }),
  event({ id: 'ev-sc', branchCode: 'SC', branchId: 'b-sc', kind: 'ManualCafc', eventCode: 5, description: 'CORTE DE SUMINISTRO DE ENERGIA ELECTRICA', cafc: 'CAFC-SC-1', startedAt: '2026-09-29T07:30:00' }),
  event({ id: 'ev-cm', startedAt: '2026-09-20T10:00:00', endedAt: '2026-09-20T11:00:00', status: 'Reconciled', documents: 12, receptionCode: 'EV-777' }),
  event({ id: 'ev-viejo', startedAt: '2026-07-01T10:00:00', status: 'Reconciled' }),
];

const PACKAGES: PackageData[] = [
  {
    branchCode: 'CM',
    cafc: null,
    documentSector: 1,
    documents: 12,
    eventId: 'ev-cm',
    id: 'pk-1',
    lastSiatCode: 908,
    messages: null,
    pointOfSaleCode: 1,
    receptionCode: 'PK-1',
    sentAt: '2026-09-20T15:05:00Z',
    status: 'Validated',
    validatedAt: '2026-09-20T15:10:00Z',
  },
  {
    branchCode: 'CB',
    cafc: null,
    documentSector: 1,
    documents: 2,
    eventId: 'ev-otro',
    id: 'pk-2',
    lastSiatCode: 904,
    messages: 'Factura 44: NIT inválido',
    pointOfSaleCode: 2,
    receptionCode: 'PK-2',
    sentAt: '2026-09-25T15:05:00Z',
    status: 'Observed',
    validatedAt: null,
  },
];

const CAFCS: CafcData[] = [
  { branchCode: 'CM', code: 'CAFC-CM-1', documentSector: 1, id: 'c-cm', isActive: true, numberFrom: 1, numberTo: 100, used: 3, validUntil: '2026-12-31' },
  { branchCode: 'SC', code: 'CAFC-SC-1', documentSector: 1, id: 'c-sc', isActive: true, numberFrom: 1, numberTo: 50, used: 10, validUntil: null },
  { branchCode: 'CB', code: 'CAFC-CB-NC', documentSector: 24, id: 'c-cb', isActive: false, numberFrom: 1, numberTo: 20, used: 0, validUntil: null },
];

const EVENT_CATALOG: CatalogItemData[] = [
  { catalog: 'EVENTOS_SIGNIFICATIVOS', code: 1, description: 'CORTE DEL SERVICIO DE INTERNET', isCurrent: true },
  { catalog: 'EVENTOS_SIGNIFICATIVOS', code: 5, description: 'CORTE DE SUMINISTRO DE ENERGIA ELECTRICA', isCurrent: true },
  { catalog: 'EVENTOS_SIGNIFICATIVOS', code: 6, description: 'VIRUS INFORMATICO O FALLA DE SOFTWARE', isCurrent: true },
];

const PRODUCTS: ProductData[] = [
  { allowsDecimals: false, available: 4, barcodes: [], category: 'Periféricos', categoryCode: 'PER', name: 'Mouse gamer', price: 350, sku: 'MOU-01', unit: 'UND', variantId: 'v-1' },
  { allowsDecimals: false, available: 2, barcodes: [], category: 'Video', categoryCode: 'GPU', name: 'Tarjeta de video', price: 5200, sku: 'GPU-01', unit: 'UND', variantId: 'v-2' },
];

const CALLS: ServiceCallData[] = [
  { durationMs: 120, error: null, httpStatus: 200, occurredAt: hours(-1), operation: 'verificarComunicacion', pointOfSaleCode: 1, requestBody: '<verificar/>', resource: 'Operaciones', responseBody: '<ok/>', siatCode: 926, succeeded: true },
  { durationMs: 30000, error: 'Tiempo de espera agotado', httpStatus: null, occurredAt: hours(-2), operation: 'recepcionFactura', pointOfSaleCode: 2, requestBody: '<factura/>', resource: 'Compra venta', responseBody: null, siatCode: null, succeeded: false },
];

function status(overrides: Partial<SiatStatusData> = {}): SiatStatusData {
  return {
    alerts: [
      { deadline: hours(-1), detail: 'La caja no puede facturar en línea: pida un CUFD nuevo.', severity: 'danger', title: 'CUFD vencido · SC · punto 1' },
      { deadline: hours(5.5), detail: 'Registre el evento fuera de línea en el SIN antes del plazo.', severity: 'warning', title: 'Evento por registrar · CB · punto 2' },
    ],
    billedToday: 12500,
    businessName: 'Tech Zone Gaming S.R.L.',
    clockSyncedAt: hours(-3),
    configured: true,
    documentsToday: 14,
    enabled: true,
    environment: 2,
    hasToken: true,
    lastCatalogSync: hours(-5),
    nit: 1020304050,
    offlineDocuments: 3,
    openEvents: 2,
    pendingDocuments: 1,
    points: [
      point({}),
      point({
        id: 'pv-cb-2',
        branchCode: 'CB',
        branchId: 'b-cb',
        branchName: 'Cochabamba',
        code: 2,
        name: 'Caja Cochabamba',
        mode: 'Offline',
        modeSince: hours(-2),
        offlineDocuments: 3,
        pendingDocuments: 1,
        cufdValidUntil: hours(1),
        lastError: 'Sin respuesta del SIN',
        openEvent: EVENTS[0],
      }),
      point({
        id: 'pv-sc-1',
        branchCode: 'SC',
        branchId: 'b-sc',
        branchName: 'Santa Cruz',
        name: 'Caja Santa Cruz',
        mode: 'ManualContingency',
        modeSince: hours(-6),
        cufdValidUntil: hours(-1),
        openEvent: EVENTS[1],
      }),
      point({ id: 'pv-cm-3', code: 3, name: 'Caja antigua', isClosed: true }),
    ],
    tokenValidUntil: '2027-01-31',
    ...overrides,
  };
}

function settings(): RpcResponseOf<'GetSiatSettingsQuery'> {
  return {
    branches: [
      { branchCode: 'CM', branchId: 'b-cm', branchName: 'Casa matriz', municipality: 'La Paz', phone: null, siatCode: 0 },
      { branchCode: 'CB', branchId: 'b-cb', branchName: 'Cochabamba', municipality: 'Cochabamba', phone: null, siatCode: 1 },
      { branchCode: 'SC', branchId: 'b-sc', branchName: 'Santa Cruz', municipality: 'Santa Cruz', phone: null, siatCode: null },
    ],
    businessName: 'Tech Zone Gaming S.R.L.',
    clockOffsetMs: 0,
    clockSyncedAt: hours(-3),
    configured: true,
    environment: 2,
    isEnabled: true,
    mail: null,
    moduleActive: true,
    nit: 1020304050,
    offlineLegend: 'Leyenda fuera de línea',
    onlineLegend: 'Leyenda en línea',
    profiles: [],
    systemCode: 'SIS-1',
  };
}

type Payload = Record<string, unknown>;

class SiatServer {
  status = status();
  calls: { operation: string; payload: Payload }[] = [];
  failures = new Map<string, WebApiError>();

  payloads(operation: string): Payload[] {
    return this.calls.filter((call) => call.operation === operation).map((call) => call.payload);
  }

  handle(operation: string, payload: Payload): { handled: true; result: unknown } | { handled: false } {
    const known = [
      'GetSiatStatusQuery',
      'CheckSiatCommunicationCommand',
      'RequestCufdCommand',
      'GoOfflineCommand',
      'RecoverPointOfSaleCommand',
      'EndContingencyCommand',
      'StartManualContingencyCommand',
      'RunSiatWorkCommand',
      'GetSignificantEventsQuery',
      'GetFiscalPackagesQuery',
      'GetContingencyCodesQuery',
      'RegisterContingencyCodeCommand',
      'GetSiatCatalogQuery',
      'GetSiatSettingsQuery',
      'GetSiatServiceCallsQuery',
      'TranscribeManualInvoiceCommand',
      'GetPosStateQuery',
      'GetHomologationQuery',
      'GetSellableProductsQuery',
    ];
    if (!known.includes(operation)) return { handled: false };
    this.calls.push({ operation, payload });
    const failure = this.failures.get(operation);
    if (failure) {
      this.failures.delete(operation);
      throw failure;
    }
    return { handled: true, result: this.execute(operation, payload) };
  }

  private setMode(id: unknown, mode: PointData['mode']) {
    this.status = { ...this.status, points: this.status.points.map((row) => (row.id === id ? { ...row, mode } : row)) };
  }

  private execute(operation: string, payload: Payload): unknown {
    switch (operation) {
      case 'GetSiatStatusQuery':
        return this.status;
      case 'CheckSiatCommunicationCommand':
        return '✔ Hay comunicación con el SIN (código 926).';
      case 'RequestCufdCommand':
        return '✔ CUFD vigente hasta el 30/09/2026 10:00.';
      case 'GoOfflineCommand':
        this.setMode(payload.pointOfSaleId, 'Offline');
        return '⚠ El punto de venta pasó a fuera de línea.';
      case 'RecoverPointOfSaleCommand':
        this.setMode(payload.pointOfSaleId, 'Online');
        return '✔ Punto recuperado: 3 documentos enviados en 1 paquete.';
      case 'EndContingencyCommand':
        this.setMode(payload.pointOfSaleId, 'Recovering');
        return '✔ Contingencia terminada: se recupera la conexión.';
      case 'StartManualContingencyCommand':
        this.setMode(payload.pointOfSaleId, 'ManualContingency');
        return '⚠ Contingencia manual declarada con el CAFC CAFC-CM-1.';
      case 'RunSiatWorkCommand':
        return {
          dispatch: { sent: 4, valid: 3, rejected: 1, wentOffline: 0, documents: [], messages: [] },
          maintenance: { cuisRequested: 0, cufdRequested: 1, recovered: 1, packagesValidated: 2, documentsSent: 4, messages: [] },
        };
      case 'GetSignificantEventsQuery':
        return EVENTS.filter((row) => row.startedAt.slice(0, 10) >= String(payload.from) && row.startedAt.slice(0, 10) <= String(payload.to));
      case 'GetFiscalPackagesQuery':
        return PACKAGES.filter((row) => payload.eventId == null || row.eventId === payload.eventId);
      case 'GetContingencyCodesQuery':
        return CAFCS;
      case 'RegisterContingencyCodeCommand':
        return '✔ Talonario CAFC-CB-2 registrado para CB.';
      case 'GetSiatCatalogQuery':
        return payload.catalog === 'EVENTOS_SIGNIFICATIVOS' ? EVENT_CATALOG : [];
      case 'GetSiatSettingsQuery':
        return settings();
      case 'GetSiatServiceCallsQuery':
        return CALLS;
      case 'TranscribeManualInvoiceCommand':
        return { ...PACKAGES[0], id: 'doc-manual', number: payload.number };
      case 'GetPosStateQuery':
        return {
          branchName: 'Santa Cruz',
          companyName: 'Tech Zone Gaming S.R.L.',
          customers: [],
          paymentMethods: [
            { code: 'QR', name: 'Pago QR', opensCashDrawer: false },
            { code: 'EFECTIVO', name: 'Efectivo', opensCashDrawer: true },
          ],
          registers: [],
          session: null,
          suggestedRegister: null,
          taxId: null,
          taxRate: 0.13,
          vatOnInvoicedAmount: true,
        };
      case 'GetHomologationQuery':
        return {
          activities: [],
          paymentMethods: [{ code: 'TARJETA', name: 'Tarjeta', paymentMethodId: 'pm-1', sinCode: 2, sinDescription: 'TARJETA' }],
          pendingProducts: 0,
          products: [],
          sinPaymentMethods: [],
          sinUnits: [],
          units: [],
        };
      case 'GetSellableProductsQuery':
        return PRODUCTS;
      default:
        throw new Error(`Operación no simulada: ${operation}`);
    }
  }
}

function serve(web: MockWeb, server: SiatServer) {
  const real = web.backend.rpc.call.bind(web.backend.rpc);
  vi.spyOn(web.backend.rpc, 'call').mockImplementation(async (operation, payload, options) => {
    const answer = server.handle(operation, payload as Payload);
    if (answer.handled) return { result: answer.result as never, replayed: false, requestId: options?.requestId ?? 'prueba' };
    return real(operation, payload, options);
  });
}

async function openSiat(role: StaffRole = 'ADMIN', query = '', server = new SiatServer()) {
  const web = await signedInAs(role);
  serve(web, server);
  const view = await renderPanel(<PanelApp registry={REGISTRY} />, { web: web.services, route: `/panel/siat${query}`, path: '/panel/*' });
  return { web, server, location: view.location };
}

async function card(testId: string): Promise<HTMLElement> {
  return screen.findByTestId(testId, undefined, { timeout: 5000 });
}

async function table(): Promise<HTMLElement> {
  const found = await screen.findByTestId('tabla', undefined, { timeout: 5000 });
  await waitFor(() => expect(within(found).queryAllByRole('row').length).toBeGreaterThan(1));
  return found;
}

function dataRows(tableElement: HTMLElement): HTMLElement[] {
  return within(tableElement)
    .getAllByRole('row')
    .filter((row) => row.hasAttribute('data-row-key'));
}

// ---------------------------------------------------------------------------------------------------- tablero del SIAT

describe('Estado del SIAT · tablero y puntos de venta', () => {
  it('cabecera, alertas con cuenta regresiva, indicadores plegados y una tarjeta por punto de venta', async () => {
    const { server } = await openSiat();
    expect(await screen.findByRole('heading', { level: 1, name: 'Estado del SIAT' }, { timeout: 5000 })).toBeInTheDocument();
    const header = await screen.findByTestId('siat-cabecera');
    expect(header).toHaveTextContent('Tech Zone Gaming S.R.L. · NIT 1020304050');
    expect(header).toHaveTextContent('Ambiente de pruebas: sin valor legal');
    const alerts = screen.getByTestId('siat-alertas');
    expect(alerts).toHaveTextContent('CUFD vencido · SC · punto 1');
    expect(alerts).toHaveTextContent('venció hace 1 h 0 min');
    expect(alerts).toHaveTextContent('vence en 5 h 30 min · 29/09/2026 15:30');
    expect(server.payloads('GetSiatStatusQuery')).toEqual([{}]);
    expect(screen.getByRole('button', { name: /Ver indicadores del SIAT/ })).toHaveAttribute('aria-expanded', 'false');
    expect(screen.queryByTestId('siat-indicadores')).not.toBeInTheDocument();

    const online = await card('punto-CM-1');
    expect(online).toHaveTextContent('Punto 1 · Caja principal');
    expect(online).toHaveTextContent('En línea');
    expect(within(online).getByRole('button', { name: 'Pasar a fuera de línea' })).toBeInTheDocument();
    expect(within(online).queryByRole('button', { name: 'Recuperar ahora' })).not.toBeInTheDocument();
    const offline = await card('punto-CB-2');
    expect(offline).toHaveTextContent('Fuera de línea');
    expect(offline).toHaveTextContent('1 por enviar · 3 fuera de línea');
    expect(offline).toHaveTextContent('Sin respuesta del SIN');
    expect(within(offline).getByRole('link', { name: 'Ver los documentos fuera de línea' })).toHaveAttribute(
      'href',
      '/panel/documentos-fiscales?sucursal=CB&punto=CB-2&estado=Offline',
    );
    expect(within(offline).getByRole('button', { name: 'Recuperar ahora' })).toBeInTheDocument();
    expect(within(await card('punto-SC-1')).getByRole('button', { name: 'Terminar contingencia' })).toBeInTheDocument();
    expect(await card('punto-CM-3')).toHaveTextContent('Cerrado en el SIN');

    fireEvent.click(screen.getByRole('button', { name: /Ver indicadores del SIAT/ }));
    const indicators = await screen.findByTestId('siat-indicadores');
    expect(indicators).toHaveTextContent('2 sin conexión');
    expect(indicators).toHaveTextContent('Emitidos hoy14');
  });

  it('filtra los puntos por sucursal y modo (en la dirección)', async () => {
    const { location } = await openSiat();
    await card('punto-CM-1');
    fireEvent.change(screen.getByRole('combobox', { name: 'Modo' }), { target: { value: 'Offline' } });
    await waitFor(() => expect(location()).toBe('/panel/siat?pv_modo=Offline'));
    expect(within(screen.getByTestId('puntos-de-venta')).getAllByRole('article')).toHaveLength(1);
    expect(screen.getByTestId('punto-CB-2')).toBeInTheDocument();
  });

  it('verificar la comunicación y pedir un CUFD nuevo envían el punto; «Procesar ahora» resume el trabajo', async () => {
    const { server } = await openSiat();
    const online = await card('punto-CM-1');
    await act(async () => {
      fireEvent.click(within(online).getByRole('button', { name: 'Verificar comunicación' }));
    });
    expect(server.payloads('CheckSiatCommunicationCommand')).toEqual([{ pointOfSaleId: 'pv-cm-1' }]);
    expect(await screen.findByText('CM · punto 1: Hay comunicación con el SIN (código 926).')).toBeInTheDocument();
    await act(async () => {
      fireEvent.click(within(online).getByRole('button', { name: 'Pedir CUFD nuevo' }));
    });
    expect(server.payloads('RequestCufdCommand')).toEqual([{ pointOfSaleId: 'pv-cm-1' }]);
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Procesar ahora' }));
    });
    expect(server.payloads('RunSiatWorkCommand')).toEqual([{ maintain: true }]);
    expect(await screen.findByText('4 enviados · 3 válidos · 1 rechazados · recuperados 1 · paquetes validados 2')).toBeInTheDocument();
  });

  it('pasar a fuera de línea pide confirmación y envía el punto; recuperar un punto fuera de línea', async () => {
    const { server } = await openSiat();
    fireEvent.click(within(await card('punto-CM-1')).getByRole('button', { name: 'Pasar a fuera de línea' }));
    const dialog = await screen.findByRole('alertdialog', { name: '¿Pasar a fuera de línea el punto 1 · caja principal?' });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Pasar a fuera de línea' }));
    });
    expect(server.payloads('GoOfflineCommand')).toEqual([{ pointOfSaleId: 'pv-cm-1', eventCode: null }]);
    await waitFor(() => expect(screen.getByTestId('punto-CM-1')).toHaveTextContent('Fuera de línea'));

    await act(async () => {
      fireEvent.click(within(screen.getByTestId('punto-CB-2')).getByRole('button', { name: 'Recuperar ahora' }));
    });
    expect(server.payloads('RecoverPointOfSaleCommand')).toEqual([{ pointOfSaleId: 'pv-cb-2' }]);
    expect(await screen.findByText('CB · punto 2: Punto recuperado: 3 documentos enviados en 1 paquete.')).toBeInTheDocument();
  });

  it('terminar la contingencia manual avisa de la transcripción; el error del servidor queda en la confirmación', async () => {
    const server = new SiatServer();
    server.failures.set('EndContingencyCommand', new WebApiError({ kind: 'domain', status: 422, code: 'siat.no_cufd', message: 'No hay comunicación con el SIN para pedir el CUFD.' }));
    await openSiat('ADMIN', '', server);
    fireEvent.click(within(await card('punto-SC-1')).getByRole('button', { name: 'Terminar contingencia' }));
    const dialog = await screen.findByRole('alertdialog', { name: '¿Terminar la contingencia del punto 1 · caja santa cruz?' });
    expect(dialog).toHaveTextContent('transcriba las facturas manuales del talonario CAFC (72 h desde el fin)');
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Terminar contingencia' }));
    });
    expect(await within(dialog).findByText('No hay comunicación con el SIN para pedir el CUFD.')).toBeInTheDocument();
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Terminar contingencia' }));
    });
    expect(server.payloads('EndContingencyCommand')).toEqual([
      { pointOfSaleId: 'pv-sc-1', endedAt: null },
      { pointOfSaleId: 'pv-sc-1', endedAt: null },
    ]);
    expect(await screen.findByText('Contingencia terminada')).toBeInTheDocument();
  });

  it('contingencia manual: solo eventos manuales del catálogo, el CAFC de la sucursal y la forma exacta del contrato', async () => {
    const { server } = await openSiat();
    fireEvent.click(within(await card('punto-CM-1')).getByRole('button', { name: 'Contingencia manual' }));
    const dialog = await screen.findByRole('dialog', { name: 'Contingencia manual · CM · punto 1' });
    const eventSelect = within(dialog).getByRole('combobox', { name: /^Evento/ });
    await waitFor(() => expect(eventSelect).toHaveValue('5'));
    expect(within(eventSelect).queryByRole('option', { name: /internet/i })).not.toBeInTheDocument();
    await waitFor(() => expect(within(dialog).getByRole('combobox', { name: /^Talonario CAFC/ })).toHaveValue('CAFC-CM-1'));
    expect(within(dialog).getByLabelText(/^Inicio/)).toHaveValue('2026-09-29T10:00');
    fireEvent.change(eventSelect, { target: { value: '6' } });
    fireEvent.change(within(dialog).getByLabelText(/^Descripción/), { target: { value: 'Se quemó la fuente del servidor' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Declarar contingencia' }));
    });
    expect(server.payloads('StartManualContingencyCommand')).toEqual([
      { pointOfSaleId: 'pv-cm-1', eventCode: 6, description: 'Se quemó la fuente del servidor', startedAt: '2026-09-29T10:00:00', cafcCode: 'CAFC-CM-1' },
    ]);
    expect(await screen.findByText('Contingencia manual declarada')).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------------------------------- pestañas

describe('Estado del SIAT · eventos, paquetes y talonarios', () => {
  it('eventos de los últimos 45 días; filtra por tipo y «Ver sus paquetes» lleva a los paquetes de ese evento', async () => {
    const { server, location } = await openSiat('ADMIN', '?pestana=eventos');
    const grid = await table();
    expect(server.payloads('GetSignificantEventsQuery')).toEqual([{ from: '2026-08-16', to: '2026-09-29' }]);
    expect(dataRows(grid).map((row) => row.getAttribute('data-row-key'))).toEqual(['ev-cb', 'ev-sc', 'ev-cm']);
    fireEvent.change(screen.getByRole('combobox', { name: 'Tipo de evento' }), { target: { value: 'ManualCafc' } });
    await waitFor(() => expect(dataRows(grid).map((row) => row.getAttribute('data-row-key'))).toEqual(['ev-sc']));
    expect(location()).toBe('/panel/siat?pestana=eventos&e_tipo=ManualCafc');
    fireEvent.change(screen.getByRole('combobox', { name: 'Tipo de evento' }), { target: { value: '' } });
    await waitFor(() => expect(dataRows(grid)).toHaveLength(3));
    fireEvent.click(within(dataRows(grid).find((row) => row.getAttribute('data-row-key') === 'ev-cm')!).getByRole('button', { name: /^Acciones de / }));
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Ver sus paquetes' }));
    await waitFor(() => expect(location()).toBe('/panel/siat?pestana=paquetes&p_evento=ev-cm'));
    const packages = await table();
    expect(server.payloads('GetFiscalPackagesQuery')).toEqual([{ eventId: 'ev-cm' }]);
    expect(dataRows(packages).map((row) => row.getAttribute('data-row-key'))).toEqual(['pk-1']);
    fireEvent.click(screen.getByRole('button', { name: 'Ver todos los paquetes' }));
    await waitFor(() => expect(server.payloads('GetFiscalPackagesQuery').at(-1)).toEqual({ eventId: null }));
  });

  it('talonarios CAFC: lista con filtros y «Registrar talonario» valida y envía la forma exacta del contrato', async () => {
    const { server } = await openSiat('ADMIN', '?pestana=talonarios');
    const grid = await table();
    expect(dataRows(grid).map((row) => row.getAttribute('data-row-key'))).toEqual(['c-cb', 'c-cm', 'c-sc']);
    expect(screen.getByTestId('talonarios-resumen')).toHaveTextContent('3 de 3 talonarios');
    fireEvent.click(within(screen.getByRole('group', { name: 'Acciones de los talonarios' })).getByRole('button', { name: 'Registrar talonario' }));
    const dialog = await screen.findByRole('dialog', { name: 'Registrar talonario CAFC' });
    const branch = within(dialog).getByRole('combobox', { name: /^Sucursal/ });
    await waitFor(() => expect(within(branch).getAllByRole('option')).toHaveLength(3));
    fireEvent.change(branch, { target: { value: 'CB' } });
    fireEvent.change(within(dialog).getByLabelText(/^Hasta el número/), { target: { value: '0' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Registrar talonario' }));
    expect(await within(dialog).findByText('Escriba el código CAFC que entregó el SIN.')).toBeInTheDocument();
    expect(within(dialog).getByText('El último número no puede ser menor que el primero.')).toBeInTheDocument();
    fireEvent.change(within(dialog).getByLabelText(/^Código CAFC/), { target: { value: ' CAFC-CB-2 ' } });
    fireEvent.change(within(dialog).getByLabelText(/^Hasta el número/), { target: { value: '250' } });
    fireEvent.change(within(dialog).getByRole('combobox', { name: /^Documento/ }), { target: { value: '24' } });
    fireEvent.change(within(dialog).getByLabelText(/^Vence/), { target: { value: '2027-03-31' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Registrar talonario' }));
    });
    expect(server.payloads('RegisterContingencyCodeCommand')).toEqual([
      { branchCode: 'CB', documentSector: 24, code: 'CAFC-CB-2', numberFrom: 1, numberTo: 250, validUntil: '2027-03-31' },
    ]);
    expect(await screen.findByText('Talonario CAFC registrado')).toBeInTheDocument();
  });

  it('transcribir una factura manual desde el tablero (?transcribir=1): guía y forma exacta del contrato', async () => {
    const { server, location } = await openSiat('ADMIN', '?pestana=eventos&transcribir=1');
    const dialog = await screen.findByRole('dialog', { name: 'Transcribir factura manual (CAFC)' }, { timeout: 5000 });
    await waitFor(() => expect(location()).toBe('/panel/siat?pestana=eventos'));
    await waitFor(() => expect(server.payloads('GetSignificantEventsQuery')).toContainEqual({ from: '2026-09-19', to: '2026-09-29' }));
    const eventSelect = within(dialog).getByRole('combobox', { name: /^Contingencia/ });
    await waitFor(() => expect(eventSelect).toHaveValue('ev-sc'));
    expect(within(dialog).getByLabelText(/^Fecha y hora de la factura/)).toHaveValue('2026-09-29T07:35');
    await waitFor(() => expect(within(dialog).getByRole('combobox', { name: /^Medio de pago/ })).toHaveValue('EFECTIVO'));

    fireEvent.click(within(dialog).getByRole('button', { name: 'Transcribir' }));
    expect(await within(dialog).findByText('Escriba el número de la factura manual (el del talonario).')).toBeInTheDocument();
    expect(within(dialog).getByText('Escriba el documento del comprador tal como figura en la factura manual.')).toBeInTheDocument();
    expect(within(dialog).getByText('Agregue los productos de la factura.')).toBeInTheDocument();

    fireEvent.change(within(dialog).getByLabelText(/^Número de la factura manual/), { target: { value: '15' } });
    fireEvent.change(within(dialog).getByLabelText(/^Número de documento/), { target: { value: '4455667' } });
    fireEvent.change(within(dialog).getByLabelText(/^Nombre o razón social/), { target: { value: 'Juan Pérez' } });
    const picker = within(dialog).getByRole('combobox', { name: /^Agregar un producto/ });
    await waitFor(() => expect(server.payloads('GetSellableProductsQuery')).toHaveLength(1));
    fireEvent.change(picker, { target: { value: 'tarjeta' } });
    fireEvent.keyDown(picker, { key: 'Enter' });
    const line = await within(dialog).findByTestId('linea-GPU-01');
    fireEvent.change(within(line).getByLabelText(/^Cantidad/), { target: { value: '2' } });
    fireEvent.change(within(line).getByLabelText(/^Descuento/), { target: { value: '5' } });
    fireEvent.change(within(line).getByLabelText(/^Series o IMEI/), { target: { value: 'GPU-S1' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Transcribir' }));
    expect(await within(dialog).findByText('Escriba una serie por unidad de Tarjeta de video (1 de 2).')).toBeInTheDocument();
    fireEvent.change(within(line).getByLabelText(/^Series o IMEI/), { target: { value: 'GPU-S1, GPU-S2' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Transcribir' }));
    });
    expect(server.payloads('TranscribeManualInvoiceCommand')).toEqual([
      {
        significantEventId: 'ev-sc',
        number: 15,
        issuedAt: '2026-09-29T07:35:00',
        buyer: { documentType: 1, documentNumber: '4455667', complement: null, name: 'Juan Pérez', email: null, exceptionRequested: false },
        paymentMethodCode: 'EFECTIVO',
        lines: [{ sku: 'GPU-01', quantity: 2, discountPercent: 5, serials: ['GPU-S1', 'GPU-S2'] }],
      },
    ]);
    expect(await screen.findByText('Factura manual N° 15 transcrita')).toBeInTheDocument();
  });

  it('la bitácora técnica del SIN está plegada y se consulta al abrirla (solo quien configura la facturación)', async () => {
    const { server } = await openSiat();
    await card('punto-CM-1');
    expect(server.payloads('GetSiatServiceCallsQuery')).toEqual([]);
    fireEvent.click(screen.getByRole('button', { name: /Ver la bitácora técnica del SIN/ }));
    const calls = await screen.findByTestId('bitacora-tecnica');
    await waitFor(() => expect(server.payloads('GetSiatServiceCallsQuery')).toEqual([{ from: '2026-09-27', to: '2026-09-29', operation: null, max: 300 }]));
    const grid = within(calls).getByTestId('tabla');
    await waitFor(() => expect(dataRows(grid)).toHaveLength(2));
    fireEvent.click(within(calls).getByRole('checkbox', { name: 'Solo las llamadas con error' }));
    await waitFor(() => expect(dataRows(grid)).toHaveLength(1));
    expect(grid).toHaveTextContent('Tiempo de espera agotado');
  });
});

// ---------------------------------------------------------------------------------------------------- permisos, errores y tablero

describe('Estado del SIAT · permisos, errores, estadística y tablero', () => {
  it('consulta (solo ver): verifica la comunicación, pero no procesa, no declara contingencias ni ve la bitácora', async () => {
    await openSiat('CONSULTA');
    const online = await card('punto-CM-1');
    expect(within(online).getByRole('button', { name: 'Verificar comunicación' })).toBeInTheDocument();
    expect(within(online).queryByRole('button', { name: 'Pasar a fuera de línea' })).not.toBeInTheDocument();
    expect(within(online).queryByRole('button', { name: 'Pedir CUFD nuevo' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Procesar ahora' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Transcribir factura manual' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Ver la bitácora técnica/ })).not.toBeInTheDocument();
  });

  it('gerencia (contingencias): declara y termina contingencias; no pide CUFD ni configura', async () => {
    await openSiat('GERENCIA');
    const online = await card('punto-CM-1');
    expect(within(online).getByRole('button', { name: 'Contingencia manual' })).toBeInTheDocument();
    expect(within(online).queryByRole('button', { name: 'Pedir CUFD nuevo' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Transcribir factura manual' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Configurar' })).not.toBeInTheDocument();
  });

  it('la empresa sin configurar ve el aviso; si la consulta falla, el error con «Reintentar»', async () => {
    const server = new SiatServer();
    server.status = status({ configured: false, points: [], alerts: [] });
    await openSiat('ADMIN', '', server);
    expect(await screen.findByText('La facturación SIAT todavía no está configurada', undefined, { timeout: 5000 })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Configurar la facturación' })).toHaveAttribute('href', '/panel/configuracion?pestana=facturacion');
  });

  it('si el estado no se puede leer muestra el error y se recupera', async () => {
    const server = new SiatServer();
    server.failures.set('GetSiatStatusQuery', new WebApiError({ kind: 'network', message: 'Sin conexión' }));
    await openSiat('ADMIN', '', server);
    const error = await screen.findByTestId('estado-error', undefined, { timeout: 5000 });
    fireEvent.click(within(error).getByRole('button', { name: 'Reintentar' }));
    expect(await card('punto-CM-1')).toBeInTheDocument();
  });

  it('un rol sin `billing.view` (bodega) ve «No tiene acceso a esta pantalla»', async () => {
    await openSiat('BODEGA');
    expect(await screen.findByTestId('sin-acceso', undefined, { timeout: 5000 })).toHaveTextContent('Falta el permiso');
  });

  it('la estadística «Conexión con el SIN» resume el estado y enlaza a la pantalla', async () => {
    const web = await signedInAs('GERENCIA');
    serve(web, new SiatServer());
    await renderPanel(<SiatStatusStat />, { web: web.services });
    const stat = await screen.findByTestId('conexion-sin');
    await waitFor(() => expect(within(stat).getByRole('link', { name: 'Ver el estado del SIAT' })).toHaveAttribute('href', '/panel/siat'));
    expect(stat).toHaveTextContent('2 sin conexión');
    expect(stat).toHaveTextContent('Por enviar4');
    expect(stat).toHaveTextContent('1 alertas urgentes');
  });

  it('el módulo es válido y ofrece al tablero «Estado del SIAT» y, a quien gestiona contingencias, «Transcribir factura manual»', () => {
    expect(REGISTRY.problems).toEqual([]);
    const labels = (granted: string[]) => dashboardActions(REGISTRY, granted).flatMap((group) => group.actions.map((item) => [item.action.label, item.to]));
    expect(labels(['billing.view'])).toEqual([['Estado del SIAT', '/panel/siat']]);
    expect(labels(['billing.view', 'billing.contingency'])).toEqual([
      ['Estado del SIAT', '/panel/siat'],
      ['Transcribir factura manual', '/panel/siat?pestana=eventos&transcribir=1'],
    ]);
    expect(dashboardStats(REGISTRY, ['billing.view']).map((item) => item.stat.title)).toEqual(['Conexión con el SIN']);
  });
});
