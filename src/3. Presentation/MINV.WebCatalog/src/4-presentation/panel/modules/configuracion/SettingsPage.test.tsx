// Módulo «Configuración» dentro del panel real con un registro que tiene SOLO este módulo. Servidor simulado en memoria en
// cada prueba (empresa, facturación, estado del SIAT, cajas, actividades y sus comandos): ninguna prueba toca la red.
//
// Cubre: las pestañas según los permisos y en la dirección, los parámetros de la empresa, los datos del Padrón (con la
// confirmación aparte para pasar a producción), la conexión con el SIN (el token solo se escribe), las sucursales del
// Padrón, los puntos de venta (CUFD, vincular caja, cerrar con doble confirmación, registrar), preparar, sincronizar y
// probar la conexión, las actividades, el correo de la empresa (la contraseña nunca se muestra; ayuda de Gmail), la
// empresa sin la licencia de facturación y el error con «Reintentar».

import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { WebApiError } from '@/1-domain/auth/errors';
import type { ISessionGateway } from '@/1-domain/ports/ISessionGateway';
import type { RpcRequestOf, RpcResponseOf } from '@/4-presentation/app/contract';
import { renderPanel, signedInAs, webWith, type MockWeb, type StaffRole } from '@/test-utils';
import { buildRegistry } from '../../registry';
import { PanelApp } from '../../shell/PanelApp';
import configuracion from './module';

const REGISTRY = buildRegistry([{ source: '../modules/configuracion/module.tsx', definition: configuracion }]);

beforeAll(async () => {
  await import('./SettingsPage');
}, 30_000);

afterEach(() => {
  vi.restoreAllMocks();
});

// ---------------------------------------------------------------------------------------------------- servidor simulado

type SiatView = RpcResponseOf<'GetSiatSettingsQuery'>;
type Point = RpcResponseOf<'GetSiatStatusQuery'>['points'][number];

const BASE = 'https://pilotosiatservicios.impuestos.gob.bo';

function siatView(): SiatView {
  return {
    configured: true,
    nit: 1023456029,
    businessName: 'Tech Zone Gaming S.R.L.',
    systemCode: 'ABC123',
    environment: 2,
    isEnabled: false,
    onlineLegend: 'Leyenda en línea',
    offlineLegend: 'Leyenda fuera de línea',
    clockSyncedAt: '2026-09-29T12:00:00Z',
    clockOffsetMs: 350,
    profiles: [
      {
        environment: 2,
        endpoints: {
          codes: `${BASE}/v2/FacturacionCodigos`,
          sync: `${BASE}/v2/FacturacionSincronizacion`,
          operations: `${BASE}/v2/FacturacionOperaciones`,
          purchaseSale: `${BASE}/v2/ServicioFacturacionCompraVenta`,
          computerized: `${BASE}/v2/ServicioFacturacionComputarizada`,
          adjustment: `${BASE}/v2/ServicioFacturacionDocumentoAjuste`,
          namespace: 'https://siat.impuestos.gob.bo/',
        },
        qrBaseUrl: 'https://pilotosiat.impuestos.gob.bo/consulta/QR',
        timeoutSeconds: 15,
        hasToken: true,
        tokenValidUntil: '2026-12-31',
        tokenUpdatedAt: '2026-09-01T12:00:00Z',
      },
    ],
    branches: [
      { branchId: 'b-CM', branchCode: 'CM', branchName: 'Casa matriz La Paz', siatCode: 0, municipality: 'La Paz', phone: '2-2440000' },
      { branchId: 'b-CB', branchCode: 'CB', branchName: 'Sucursal Cochabamba', siatCode: null, municipality: null, phone: null },
      { branchId: 'b-SC', branchCode: 'SC', branchName: 'Sucursal Santa Cruz', siatCode: 2, municipality: 'Santa Cruz', phone: null },
    ],
    mail: { host: 'smtp.empresa.example', port: 587, useSsl: true, userName: 'facturas@techzone.example', hasPassword: true, fromAddress: 'facturas@techzone.example', fromName: 'Tech Zone', isEnabled: true },
    moduleActive: true,
  };
}

function point(overrides: Partial<Point> & Pick<Point, 'id' | 'code' | 'name'>): Point {
  return {
    branchCode: 'CM',
    branchId: 'b-CM',
    branchName: 'Casa matriz La Paz',
    cufdObtainedAt: '2026-09-29T12:00:00Z',
    cufdValidUntil: '2099-09-30T12:00:00Z',
    cuisValidUntil: '2099-09-01T00:00:00Z',
    environment: 2,
    isClosed: false,
    lastContactAt: '2026-09-29T12:00:00Z',
    lastError: null,
    mode: 'Online',
    modeSince: '2026-09-01T12:00:00Z',
    offlineDocuments: 0,
    openEvent: null,
    pendingDocuments: 0,
    registerCode: null,
    retryAt: null,
    siatBranchCode: 0,
    ...overrides,
  };
}

function fakeServer() {
  const company: RpcResponseOf<'GetCompanySettingsQuery'> = {
    alertMargin: 0.2,
    code: 'TECHZONE',
    daysWithoutRotation: 60,
    legalName: 'Tech Zone Gaming S.R.L.',
    minBusinessDate: '2026-01-01',
    taxId: '1023456029',
    timeZoneId: 'America/La_Paz',
  };
  const view = siatView();
  const points: Point[] = [
    point({ id: 'p-0', code: 0, name: 'Sin punto de venta' }),
    point({ id: 'p-1', code: 1, name: 'Caja 1', registerCode: 'CM-CAJA1' }),
    point({ id: 'p-2', code: 1, name: 'Caja SC', branchCode: 'SC', branchId: 'b-SC', branchName: 'Sucursal Santa Cruz', siatBranchCode: 2, mode: 'Offline', lastError: 'Sin conexión', pendingDocuments: 3 }),
    point({ id: 'p-3', code: 2, name: 'Caja vieja', isClosed: true }),
  ];
  let communicationFails = false;
  const sent: { operation: string; payload: unknown }[] = [];
  const handlers: Record<string, (payload: unknown) => unknown> = {
    GetCompanySettingsQuery: () => ({ ...company }),
    UpdateCompanySettingsCommand: (payload) => {
      Object.assign(company, payload as RpcRequestOf<'UpdateCompanySettingsCommand'>);
      return true;
    },
    GetSiatSettingsQuery: () => JSON.parse(JSON.stringify(view)) as SiatView,
    SaveSiatSettingsCommand: (payload) => {
      const command = payload as RpcRequestOf<'SaveSiatSettingsCommand'>;
      Object.assign(view, { nit: command.nit, environment: command.environment, isEnabled: command.enabled });
      return `✔ Facturación SIAT ${command.enabled ? 'ACTIVADA' : 'guardada'} · NIT ${command.nit} · ambiente ${command.environment}.`;
    },
    SaveSiatProfileCommand: (payload) => {
      const command = payload as RpcRequestOf<'SaveSiatProfileCommand'>;
      return `✔ Conexión del ambiente ${command.environment} guardada${command.newToken ? ' con el token delegado nuevo (cifrado)' : ''}.`;
    },
    SaveSiatBranchCommand: (payload) => {
      const command = payload as RpcRequestOf<'SaveSiatBranchCommand'>;
      Object.assign(view.branches.find((branch) => branch.branchCode === command.branchCode)!, { siatCode: command.siatCode, municipality: command.municipality, phone: command.phone });
      return `✔ Sucursal ${command.branchCode} · sucursal ${command.siatCode} del Padrón · ${command.municipality}.`;
    },
    SaveMailSettingsCommand: (payload) => {
      const command = payload as RpcRequestOf<'SaveMailSettingsCommand'>;
      return `✔ Correo de la empresa guardado (${command.host}:${command.port}).`;
    },
    GetSiatStatusQuery: () => ({
      alerts: [],
      billedToday: 0,
      businessName: view.businessName,
      clockSyncedAt: view.clockSyncedAt,
      configured: true,
      documentsToday: 0,
      enabled: view.isEnabled,
      environment: view.environment,
      hasToken: true,
      lastCatalogSync: null,
      nit: view.nit,
      offlineDocuments: 0,
      openEvents: 0,
      pendingDocuments: 3,
      points: points.map((item) => ({ ...item })),
      tokenValidUntil: '2026-12-31',
    }),
    GetPosStateQuery: () => ({
      branchName: 'Casa matriz La Paz',
      companyName: 'Tech Zone Gaming S.R.L.',
      customers: [],
      paymentMethods: [],
      registers: [
        { code: 'CM-CAJA1', name: 'Caja 1', opensCashDrawer: true },
        { code: 'CM-CAJA2', name: 'Caja 2', opensCashDrawer: true },
      ],
      session: null,
      suggestedRegister: null,
      taxId: null,
      taxRate: 0.13,
      vatOnInvoicedAmount: true,
    }),
    GetSiatActivitiesQuery: () => [
      { code: '474100', description: 'Venta al por menor de computadoras', activityType: 'P', isCurrent: true, sectors: [1, 24] },
      { code: '620100', description: 'Programación informática', activityType: 'S', isCurrent: false, sectors: [1] },
    ],
    CheckSiatCommunicationCommand: () => {
      if (communicationFails) throw new WebApiError({ kind: 'domain', status: 422, code: 'siat.unavailable', message: 'No hay comunicación con el SIN: tiempo de espera agotado.' });
      return '✔ Comunicación exitosa con el SIN (908) · ambiente 2 · 3 punto(s) de venta al día.';
    },
    PrepareSiatCommand: () => ({ cuisRequested: 1, cufdRequested: 3, documentsSent: 0, messages: ['CUFD nuevo en CM-1.'], packagesValidated: 0, recovered: 0 }),
    SyncSiatCatalogsCommand: () => ({ catalogs: 18, items: 1234, errors: ['El catálogo de leyendas no respondió.'], clockSyncedAt: '2026-09-29T15:00:00Z' }),
    RequestCufdCommand: (payload) => `✔ CUFD nuevo del punto ${points.find((item) => item.id === (payload as { pointOfSaleId: string }).pointOfSaleId)!.code} vigente hasta 30/09/2026 12:00.`,
    RequestCuisCommand: () => '✔ El SIN mantuvo el CUIS vigente.',
    LinkPointOfSaleRegisterCommand: (payload) => {
      const command = payload as RpcRequestOf<'LinkPointOfSaleRegisterCommand'>;
      points.find((item) => item.id === command.pointOfSaleId)!.registerCode = command.registerCode;
      return `✔ La caja ${command.registerCode} factura con el punto de venta.`;
    },
    CloseSiatPointOfSaleCommand: (payload) => {
      const target = points.find((item) => item.id === (payload as { pointOfSaleId: string }).pointOfSaleId)!;
      target.isClosed = true;
      return `✔ Punto de venta ${target.code} · ${target.name} cerrado DEFINITIVAMENTE en el SIN.`;
    },
    RegisterSiatPointOfSaleCommand: (payload) => {
      const command = payload as RpcRequestOf<'RegisterSiatPointOfSaleCommand'>;
      const created = point({ id: 'p-new', code: 3, name: command.name, branchCode: command.branchCode, registerCode: command.registerCode });
      points.push(created);
      return created;
    },
  };
  return {
    company,
    view,
    points,
    sent,
    handlers,
    failCommunication: () => {
      communicationFails = true;
    },
  };
}

type FakeServer = ReturnType<typeof fakeServer>;

function serve(web: MockWeb, server: FakeServer) {
  const real = web.backend.rpc.call.bind(web.backend.rpc);
  return vi.spyOn(web.backend.rpc, 'call').mockImplementation(async (operation, payload, options) => {
    const handler = server.handlers[operation];
    if (!handler) return real(operation, payload, options);
    server.sent.push({ operation, payload });
    return { result: (await handler(payload)) as never, replayed: false, requestId: options?.requestId ?? 'prueba' };
  });
}

function sentOf(server: FakeServer, operation: string): unknown[] {
  return server.sent.filter((item) => item.operation === operation).map((item) => item.payload);
}

async function openAs(role: StaffRole = 'ADMIN', query = '', server = fakeServer()) {
  const web = await signedInAs(role);
  serve(web, server);
  const view = await renderPanel(<PanelApp registry={REGISTRY} />, { web: web.services, route: `/panel/configuracion${query}`, path: '/panel/*' });
  return { server, location: view.location };
}

async function openWithPermissions(permissions: readonly string[], query = '') {
  const web = await signedInAs('ADMIN');
  const server = fakeServer();
  serve(web, server);
  const base = web.backend.session;
  const session: ISessionGateway = {
    login: (credentials) => base.login(credentials),
    register: (registration) => base.register(registration),
    logout: () => base.logout(),
    current: async () => {
      const current = await base.current();
      return current && { ...current, permissions: [...permissions] };
    },
  };
  const view = await renderPanel(<PanelApp registry={REGISTRY} />, { web: webWith({ session, rpc: web.backend.rpc }), route: `/panel/configuracion${query}`, path: '/panel/*' });
  return { server, location: view.location };
}

/** Abre una sección plegable por el comienzo de su título. */
function expand(label: string) {
  fireEvent.click(screen.getByRole('button', { name: new RegExp(`^${label}`) }));
}

async function billing() {
  return screen.findByTestId('lista-para-facturar', undefined, { timeout: 5000 });
}

function keysOf(grid: HTMLElement): string[] {
  return within(grid)
    .getAllByRole('row')
    .filter((row) => row.hasAttribute('data-row-key'))
    .map((row) => row.getAttribute('data-row-key') ?? '');
}

async function rowAction(grid: HTMLElement, key: string, label: string) {
  const row = grid.querySelector<HTMLElement>(`[data-row-key="${key}"]`);
  if (!row) throw new Error(`No está la fila ${key}`);
  fireEvent.click(within(row).getByRole('button', { name: /^Acciones de / }));
  fireEvent.click(await screen.findByRole('menuitem', { name: label }));
}

// ---------------------------------------------------------------------------------------------------- empresa

describe('Configuración · empresa', () => {
  it('muestra los datos de la empresa y cambia los parámetros del semáforo con listas desplegables', async () => {
    const { server } = await openAs();
    expect(await screen.findByRole('heading', { level: 1, name: 'Configuración' })).toBeInTheDocument();
    expect(await screen.findByText('TECHZONE', undefined, { timeout: 5000 })).toBeInTheDocument();
    expect(screen.getByText(/^20 % sobre el mínimo/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Cambiar parámetros' }));
    const dialog = await screen.findByRole('dialog', { name: 'Parámetros del semáforo' });
    expect(within(dialog).getByRole('combobox', { name: /^Margen de alerta/ })).toHaveValue('0.2');
    fireEvent.change(within(dialog).getByRole('combobox', { name: /^Margen de alerta/ }), { target: { value: '0.3' } });
    fireEvent.change(within(dialog).getByRole('combobox', { name: /^Días sin rotación/ }), { target: { value: '90' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar parámetros' }));
    });
    expect(sentOf(server, 'UpdateCompanySettingsCommand')).toEqual([{ alertMargin: 0.3, daysWithoutRotation: 90 }]);
    expect(await screen.findByText('Parámetros guardados: alerta 30 % · rotación 90 días')).toBeInTheDocument();
    expect(await screen.findByText(/^30 % sobre el mínimo/)).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------------------------------- facturación

describe('Configuración · facturación SIAT', () => {
  it('el botón del tablero abre el estado con lo que falta; pasar a producción pide una confirmación aparte', async () => {
    const { server, location } = await openAs('ADMIN', '?pestana=facturacion');
    const checks = await billing();
    expect(location()).toBe('/panel/configuracion?pestana=facturacion');
    expect(checks).toHaveTextContent('Listo: Datos del Padrón');
    expect(screen.getByText('Guardada, desactivada')).toBeInTheDocument();
    // «Datos del Padrón» empieza abierto.
    expect(screen.getByText('1023456029')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Cambiar los datos' }));
    const dialog = await screen.findByRole('dialog', { name: 'Datos del Padrón y del sistema' });
    fireEvent.change(within(dialog).getByLabelText(/^NIT de la empresa/), { target: { value: '10234A' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar los datos' }));
    expect(await within(dialog).findByText('El NIT lleva solo números (de 1 a 13 cifras), tal como figura en el Padrón.')).toBeInTheDocument();
    fireEvent.change(within(dialog).getByLabelText(/^NIT de la empresa/), { target: { value: '1023456029' } });
    fireEvent.click(within(dialog).getByRole('radio', { name: 'Producción (1)' }));
    fireEvent.click(within(dialog).getByRole('switch', { name: 'Facturación activa: las ventas emiten factura del SIN' }));
    expect(dialog).toHaveTextContent('Para activarla todavía falta');
    fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar los datos' }));
    const confirm = await screen.findByRole('alertdialog', { name: '¿Pasar a PRODUCCIÓN?' });
    expect(sentOf(server, 'SaveSiatSettingsCommand')).toEqual([]);
    await act(async () => {
      fireEvent.click(within(confirm).getByRole('button', { name: 'Usar producción' }));
    });
    expect(sentOf(server, 'SaveSiatSettingsCommand')).toEqual([
      { nit: 1023456029, businessName: 'Tech Zone Gaming S.R.L.', systemCode: 'ABC123', environment: 1, onlineLegend: 'Leyenda en línea', offlineLegend: 'Leyenda fuera de línea', enabled: true },
    ]);
    expect(await screen.findByText('Facturación SIAT ACTIVADA · NIT 1023456029 · ambiente 1.')).toBeInTheDocument();
  });

  it('la conexión muestra si hay token (nunca el token) y se guarda completando las direcciones desde la base', async () => {
    const { server } = await openAs('ADMIN', '?pestana=facturacion');
    await billing();
    expand('Conexión con el SIN');
    const token = await screen.findByTestId('estado-del-token');
    expect(token).toHaveTextContent('Token guardado');
    expect(token).toHaveTextContent('vigente hasta el 31/12/2026');
    fireEvent.click(screen.getByRole('button', { name: 'Cambiar la conexión' }));
    const dialog = await screen.findByRole('dialog', { name: 'Conexión con el SIN' });
    const tokenField = within(dialog).getByLabelText(/^Token delegado nuevo/);
    expect(tokenField).toHaveValue('');
    expect(tokenField).toHaveAttribute('type', 'password');

    fireEvent.change(within(dialog).getByLabelText(/^Dirección base de los servicios/), { target: { value: 'http://siat.example' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Completar las direcciones' }));
    expect(await within(dialog).findByText('Escriba la dirección base completa, con https (http solo en este mismo equipo).')).toBeInTheDocument();
    fireEvent.change(within(dialog).getByLabelText(/^Dirección base de los servicios/), { target: { value: 'http://localhost:5095/' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Completar las direcciones' }));
    expect(within(dialog).getByLabelText(/^Servicio de Códigos/)).toHaveValue('http://localhost:5095/v2/FacturacionCodigos');
    fireEvent.change(tokenField, { target: { value: 'TOKEN-NUEVO-DE-PRUEBA-123' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar la conexión' }));
    });
    expect(sentOf(server, 'SaveSiatProfileCommand')).toEqual([
      {
        environment: 2,
        endpoints: {
          codes: 'http://localhost:5095/v2/FacturacionCodigos',
          sync: 'http://localhost:5095/v2/FacturacionSincronizacion',
          operations: 'http://localhost:5095/v2/FacturacionOperaciones',
          purchaseSale: 'http://localhost:5095/v2/ServicioFacturacionCompraVenta',
          computerized: 'http://localhost:5095/v2/ServicioFacturacionComputarizada',
          adjustment: 'http://localhost:5095/v2/ServicioFacturacionDocumentoAjuste',
          namespace: 'https://siat.impuestos.gob.bo/',
        },
        qrBaseUrl: 'https://pilotosiat.impuestos.gob.bo/consulta/QR',
        timeoutSeconds: 15,
        newToken: 'TOKEN-NUEVO-DE-PRUEBA-123',
        tokenValidUntil: '2026-12-31',
      },
    ]);
    expect(await screen.findByText('Conexión del ambiente 2 guardada con el token delegado nuevo (cifrado).')).toBeInTheDocument();
  });

  it('asigna el código del Padrón a una sucursal con validación por campo', async () => {
    const { server } = await openAs('ADMIN', '?pestana=facturacion');
    await billing();
    expand('Sucursales del Padrón');
    const grid = await screen.findByRole('table', { name: 'Sucursales del Padrón' });
    await rowAction(grid.closest('[data-testid="tabla"]') as HTMLElement, 'CB', 'Asignar el código del Padrón');
    const dialog = await screen.findByRole('dialog', { name: 'Sucursal del Padrón' });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar la sucursal' }));
    expect(await within(dialog).findByText('Escriba el código de la sucursal en el Padrón (0 = casa matriz).')).toBeInTheDocument();
    expect(within(dialog).getByText('Indique el municipio que va en la factura.')).toBeInTheDocument();
    fireEvent.change(within(dialog).getByLabelText(/^Código de sucursal en el Padrón/), { target: { value: '1' } });
    fireEvent.change(within(dialog).getByLabelText(/^Municipio/), { target: { value: 'Cochabamba' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar la sucursal' }));
    });
    expect(sentOf(server, 'SaveSiatBranchCommand')).toEqual([{ branchCode: 'CB', siatCode: 1, municipality: 'Cochabamba', phone: null }]);
    expect(await screen.findByText('Sucursal CB · sucursal 1 del Padrón · Cochabamba.')).toBeInTheDocument();
  });

  it('los puntos de venta: pedir CUFD, vincular una caja y cerrar con doble confirmación', async () => {
    const { server } = await openAs('ADMIN', '?pestana=facturacion');
    await billing();
    expand('Puntos de venta, CUIS y CUFD');
    const summary = await screen.findByTestId('puntos-resumen', undefined, { timeout: 5000 });
    expect(summary).toHaveTextContent('3 de 4 puntos · 2 de 3 abiertos en línea');
    const grid = screen.getByRole('table', { name: 'Puntos de venta del SIN' }).closest('[data-testid="tabla"]') as HTMLElement;
    expect(keysOf(grid)).toEqual(['p-0', 'p-1', 'p-2']);

    await rowAction(grid, 'p-1', 'Solicitar CUFD');
    await waitFor(() => expect(sentOf(server, 'RequestCufdCommand')).toEqual([{ pointOfSaleId: 'p-1' }]));
    expect(await screen.findByText('CUFD nuevo del punto 1 vigente hasta 30/09/2026 12:00.')).toBeInTheDocument();

    await rowAction(grid, 'p-1', 'Vincular una caja');
    const link = await screen.findByRole('dialog', { name: 'Vincular una caja' });
    await waitFor(() => expect(within(link).getByRole('combobox', { name: 'Caja de M-INV' })).toHaveValue('CM-CAJA1'));
    fireEvent.change(within(link).getByRole('combobox', { name: 'Caja de M-INV' }), { target: { value: 'CM-CAJA2' } });
    await act(async () => {
      fireEvent.click(within(link).getByRole('button', { name: 'Guardar' }));
    });
    expect(sentOf(server, 'LinkPointOfSaleRegisterCommand')).toEqual([{ pointOfSaleId: 'p-1', registerCode: 'CM-CAJA2' }]);

    await rowAction(grid, 'p-1', 'Cerrar en el SIN');
    const confirm = await screen.findByRole('alertdialog', { name: '¿Cerrar el punto 1 en el SIN?' });
    const button = within(confirm).getByRole('button', { name: 'Cerrar definitivamente' });
    expect(button).toBeDisabled();
    fireEvent.click(within(confirm).getByRole('checkbox', { name: 'Entiendo que el cierre es definitivo' }));
    await act(async () => {
      fireEvent.click(button);
    });
    expect(sentOf(server, 'CloseSiatPointOfSaleCommand')).toEqual([{ pointOfSaleId: 'p-1' }]);
    await waitFor(() => expect(keysOf(grid)).toEqual(['p-0', 'p-2']));
  });

  it('registrar un punto de venta ofrece solo las sucursales con código del Padrón', async () => {
    const { server } = await openAs('ADMIN', '?pestana=facturacion');
    await billing();
    expand('Puntos de venta, CUIS y CUFD');
    await screen.findByTestId('puntos-resumen', undefined, { timeout: 5000 });
    fireEvent.click(screen.getByRole('button', { name: 'Registrar punto de venta' }));
    const dialog = await screen.findByRole('dialog', { name: 'Registrar un punto de venta' });
    const branch = within(dialog).getByRole('combobox', { name: /^Sucursal/ });
    expect(within(branch).getAllByRole('option').map((option) => option.getAttribute('value'))).toEqual(['', 'CM', 'SC']);
    expect(branch).toHaveValue('CM');
    fireEvent.change(branch, { target: { value: 'SC' } });
    fireEvent.change(within(dialog).getByLabelText(/^Nombre del punto/), { target: { value: 'Caja 2 SC' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Registrar en el SIN' }));
    });
    expect(sentOf(server, 'RegisterSiatPointOfSaleCommand')).toEqual([{ branchCode: 'SC', name: 'Caja 2 SC', description: null, registerCode: null, typeCode: 5 }]);
    expect(await screen.findByText('Punto de venta 3 registrado en SC · Caja 2 SC')).toBeInTheDocument();
  });

  it('preparar, sincronizar y probar la conexión muestran su resultado (también si no hay comunicación)', async () => {
    const { server } = await openAs('ADMIN', '?pestana=facturacion');
    await billing();
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Preparar SIAT' }));
    });
    expect(sentOf(server, 'PrepareSiatCommand')).toEqual([{}]);
    expect(await screen.findByText('CUIS pedidos: 1 · CUFD pedidos: 3 · documentos enviados: 0 · paquetes validados: 0 · puntos recuperados: 0')).toBeInTheDocument();
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Sincronizar catálogos' }));
    });
    expect(sentOf(server, 'SyncSiatCatalogsCommand')).toEqual([{ catalog: null }]);
    expect(await screen.findByText('18 catálogos · 1.234 filas')).toBeInTheDocument();
    expect(screen.getByText('El catálogo de leyendas no respondió.')).toBeInTheDocument();
    server.failCommunication();
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Probar conexión' }));
    });
    expect(sentOf(server, 'CheckSiatCommunicationCommand')).toEqual([{ pointOfSaleId: null }]);
    expect(await screen.findByText('Sin comunicación con el SIN')).toBeInTheDocument();
    expect(screen.getByText('No hay comunicación con el SIN: tiempo de espera agotado.')).toBeInTheDocument();
  });

  it('las actividades económicas se cargan al abrir su sección y se filtran por vigencia', async () => {
    const { server } = await openAs('ADMIN', '?pestana=facturacion');
    await billing();
    expect(sentOf(server, 'GetSiatActivitiesQuery')).toEqual([]);
    expand('Actividades económicas del SIN');
    const summary = await screen.findByTestId('actividades-resumen', undefined, { timeout: 5000 });
    expect(summary).toHaveTextContent('2 de 2 actividades');
    fireEvent.change(screen.getByRole('combobox', { name: 'Vigencia' }), { target: { value: 'no' } });
    await waitFor(() => expect(summary).toHaveTextContent('1 de 2 actividades'));
  });

  it('sin la licencia de facturación se ve todo, con el aviso, pero no se puede cambiar', async () => {
    const server = fakeServer();
    server.view.moduleActive = false;
    await openAs('ADMIN', '?pestana=facturacion', server);
    await billing();
    expect(screen.getByText('Módulo sin licencia')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Preparar SIAT' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Cambiar los datos' })).toBeDisabled();
  });

  it('si la configuración no carga, ofrece «Reintentar»', async () => {
    const server = fakeServer();
    const original = server.handlers.GetSiatSettingsQuery;
    let failures = 1;
    server.handlers.GetSiatSettingsQuery = (payload) => {
      if (failures-- > 0) throw new WebApiError({ kind: 'network', message: 'Sin conexión' });
      return original(payload);
    };
    await openAs('ADMIN', '?pestana=facturacion', server);
    const error = await screen.findByTestId('estado-error', undefined, { timeout: 5000 });
    fireEvent.click(within(error).getByRole('button', { name: 'Reintentar' }));
    await billing();
  });
});

// ---------------------------------------------------------------------------------------------------- correo

describe('Configuración · correo de la empresa', () => {
  it('la contraseña nunca se muestra: se conserva (null) salvo que se marque «Cambiar la contraseña»', async () => {
    const { server } = await openAs('ADMIN', '?pestana=correo');
    expect(await screen.findByText('Guardada (cifrada; nunca se muestra)', undefined, { timeout: 5000 })).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Cambiar el correo' }));
    const dialog = await screen.findByRole('dialog', { name: 'Correo de la empresa' });
    expect(within(dialog).queryByLabelText(/^Contraseña nueva/)).not.toBeInTheDocument();
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar el correo' }));
    });
    expect(sentOf(server, 'SaveMailSettingsCommand')).toEqual([
      {
        host: 'smtp.empresa.example',
        port: 587,
        useSsl: true,
        userName: 'facturas@techzone.example',
        newPassword: null,
        fromAddress: 'facturas@techzone.example',
        fromName: 'Tech Zone',
        enabled: true,
      },
    ]);
    expect(await screen.findByText('Correo de la empresa guardado (smtp.empresa.example:587).')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Cambiar el correo' }));
    const again = await screen.findByRole('dialog', { name: 'Correo de la empresa' });
    fireEvent.click(within(again).getByRole('checkbox', { name: 'Cambiar la contraseña' }));
    const password = within(again).getByLabelText(/^Contraseña nueva/);
    expect(password).toHaveAttribute('type', 'password');
    fireEvent.click(within(again).getByRole('button', { name: 'Guardar el correo' }));
    expect(await within(again).findByText('Escriba la contraseña nueva o desmarque «Cambiar la contraseña».')).toBeInTheDocument();
    fireEvent.change(password, { target: { value: 'abcdefghijklmnop' } });
    await act(async () => {
      fireEvent.click(within(again).getByRole('button', { name: 'Guardar el correo' }));
    });
    expect(sentOf(server, 'SaveMailSettingsCommand')[1]).toMatchObject({ newPassword: 'abcdefghijklmnop' });
  });

  it('«Configurar con Gmail» abre el formulario con smtp.gmail.com, 587 y STARTTLS, y la ayuda', async () => {
    await openAs('ADMIN', '?pestana=correo');
    await screen.findByText('Guardada (cifrada; nunca se muestra)', undefined, { timeout: 5000 });
    fireEvent.click(screen.getByRole('button', { name: 'Configurar con Gmail' }));
    const dialog = await screen.findByRole('dialog', { name: 'Correo de la empresa' });
    expect(within(dialog).getByLabelText(/^Servidor SMTP/)).toHaveValue('smtp.gmail.com');
    expect(within(dialog).getByLabelText(/^Puerto/)).toHaveValue('587');
    expect(within(dialog).getByRole('checkbox', { name: 'Conexión segura (STARTTLS o SSL)' })).toBeChecked();
    expect(dialog).toHaveTextContent('contraseña de aplicación');
  });
});

// ---------------------------------------------------------------------------------------------------- permisos

describe('Configuración · quién ve qué', () => {
  it('quien administra usuarios pero no la facturación ve solo «Empresa» (la dirección de otra pestaña vuelve a ella)', async () => {
    await openWithPermissions(['iam.users.manage', 'inventory.stock.view'], '?pestana=facturacion');
    expect(await screen.findByText('TECHZONE', undefined, { timeout: 5000 })).toBeInTheDocument();
    expect(screen.queryByRole('tab', { name: 'Facturación SIAT' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Cambiar parámetros' })).toBeInTheDocument();
  });

  it('quien configura la facturación sin administrar usuarios no ve «Empresa» o no puede cambiarla', async () => {
    await openWithPermissions(['billing.configure', 'billing.view']);
    await billing();
    expect(screen.queryByRole('tab', { name: 'Empresa' })).not.toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Facturación SIAT' })).toHaveAttribute('aria-selected', 'true');
  });

  it('con el stock a la vista ve la empresa, pero sin `iam.users.manage` no se ofrece cambiarla', async () => {
    await openWithPermissions(['billing.configure', 'billing.view', 'inventory.stock.view']);
    expect(await screen.findByText('TECHZONE', undefined, { timeout: 5000 })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Cambiar parámetros' })).not.toBeInTheDocument();
  });

  it('un rol sin estos permisos (gerencia) ve «No tiene acceso a esta pantalla»', async () => {
    await openAs('GERENCIA');
    expect(await screen.findByTestId('sin-acceso')).toHaveTextContent('Necesita al menos uno de estos permisos');
  });
});
