// Módulo «Reservas» dentro del panel real (con un registro que tiene solo este módulo: las pruebas no dependen de los
// demás módulos en construcción). El servidor de las reservas se simula en la prueba (el modo mock no atiende estas
// operaciones): lista con filtros en la dirección, plazo resaltado, estado del correo, detalle lateral, Liberar (motivo y
// confirmación), Reenviar correo, Copiar teléfono, Abrir WhatsApp, Vender en caja, la reserva en mostrador
// (`ReserveCartCommand` con la forma exacta del contrato), errores del servidor, permisos, la estadística y el tablero.
// Ninguna prueba toca la red. La hora queda fija: 29/09/2026 10:00 en La Paz.

import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { WebApiError } from '@/1-domain/auth/errors';
import { renderPanel, signedInAs, type MockWeb, type StaffRole } from '@/test-utils';
import { buildRegistry, dashboardActions, dashboardStats } from '../../registry';
import { PanelApp } from '../../shell/PanelApp';
import { ActiveReservationsStat } from './ActiveReservationsStat';
import reservas from './module';
import type { BuildDetailData, BuildLineData, BuildRecord, CustomerRecord, MailRecord, ProductRecord } from './reservations';

const REGISTRY = buildRegistry([{ source: '../modules/reservas/module.tsx', definition: reservas }]);

/** 10:00 de La Paz del 29/09/2026. */
const NOW = new Date('2026-09-29T14:00:00Z');
const hours = (value: number) => new Date(NOW.getTime() + value * 3_600_000).toISOString();

// No se usa `preloadPanel()`: descargaría el descubrimiento de TODOS los módulos (algunos en construcción). Se descargan
// antes el esqueleto y la pantalla de este módulo.
beforeAll(async () => {
  await import('../../shell/PanelLayout');
  await import('./ReservationsPage');
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

function build(overrides: Partial<BuildRecord>): BuildRecord {
  return {
    id: `id-${overrides.number ?? 'x'}`,
    number: 'RES-WEB-000001',
    name: 'Reserva',
    branchCode: 'CM',
    customer: null,
    status: 'Reserved',
    validUntil: '2026-10-06',
    isExpired: false,
    total: 1000,
    items: 1,
    isCompatible: true,
    createdAt: hours(-20),
    invoiceNumber: null,
    quotedWithErrors: false,
    channel: 'Web',
    contactName: null,
    contactPhone: null,
    contactEmail: null,
    reservedUntil: hours(24),
    publishedToWeb: false,
    cancelReason: null,
    notes: null,
    reserved: 1,
    kind: 'Cart',
    buyerDocumentType: null,
    buyerDocumentNumber: null,
    buyerComplement: null,
    buyerName: null,
    ...overrides,
  };
}

function line(overrides: Partial<BuildLineData>): BuildLineData {
  return { slot: null, sku: 'SKU-1', name: 'Producto', quantity: 1, unitPrice: 100, subtotal: 100, stock: 5, imageId: null, keySpecs: [], ...overrides };
}

function mail(overrides: Partial<MailRecord>): MailRecord {
  return {
    id: `m-${Math.random()}`,
    reservation: 'RES-WEB-000001',
    reservationKind: 'Cart',
    branchCode: 'CM',
    kind: 'ReservationConfirmed',
    kindText: 'Confirmación de reserva',
    recipient: 'juan@correo.example',
    status: 'Sent',
    statusText: 'Enviado',
    attempts: 1,
    maxAttempts: 5,
    lastError: null,
    requestedAt: hours(-19),
    nextAttemptAt: null,
    lastAttemptAt: hours(-19),
    completedAt: hours(-19),
    ...overrides,
  };
}

const PRODUCTS: ProductRecord[] = [
  { variantId: 'v-1', sku: 'MOU-LOG-G502', name: 'Mouse Logitech G502', categoryCode: 'PER', category: 'Periféricos', unit: 'UND', allowsDecimals: false, price: 350, available: 4, barcodes: ['7790001112223'] },
  { variantId: 'v-2', sku: 'TEC-RZ-HUNT', name: 'Teclado Razer Huntsman', categoryCode: 'PER', category: 'Periféricos', unit: 'UND', allowsDecimals: false, price: 900, available: 0, barcodes: [] },
];

const CUSTOMERS: CustomerRecord[] = [
  { code: 'CF', name: 'Consumidor final', category: 'General', categoryCode: 'GEN', email: null, phone: null, taxId: null, isActive: true, lastPurchase: null, purchases: 0, total: 0 },
  { code: 'CLI-0002', name: 'Ana Rojas', category: 'General', categoryCode: 'GEN', email: 'ana@correo.example', phone: '76543210', taxId: '4455667', isActive: true, lastPurchase: null, purchases: 3, total: 2500 },
];

type Payload = Record<string, unknown>;

/** Servidor de las reservas en memoria: responde las operaciones del módulo y anota lo que se pidió. */
class ReservationsServer {
  builds: BuildRecord[] = [
    build({
      number: 'RES-WEB-000001',
      name: 'Reserva de Juan Pérez',
      contactName: 'Juan Pérez',
      contactPhone: '+59171234567',
      contactEmail: 'juan@correo.example',
      reservedUntil: hours(2),
      total: 1850,
      reserved: 2,
      notes: 'Pasa después del trabajo',
      buyerDocumentType: 1,
      buyerDocumentNumber: '4455667',
      buyerComplement: '1A',
      buyerName: 'Juan Pérez',
    }),
    build({ number: 'RES-CM-000002', name: 'Reserva de Ana Rojas', channel: 'Desktop', branchCode: 'CB', contactName: 'Ana Rojas', contactPhone: '76543210', reservedUntil: hours(30), total: 700 }),
    build({
      number: 'ARM-WEB-000003',
      name: 'Armado web de Luis Mamani',
      kind: 'Build',
      contactName: 'Luis Mamani',
      contactPhone: '+59170011223',
      contactEmail: 'luis@correo.example',
      reservedUntil: hours(-3),
      total: 9800,
    }),
    build({ number: 'ARM-CM-000004', name: 'PC oficina', kind: 'Build', channel: 'Desktop', status: 'Sold', customer: 'Tech SRL', invoiceNumber: 'F-CM-000120', reservedUntil: hours(-40), total: 12500 }),
    build({ number: 'RES-WEB-000005', name: 'Reserva de Eva Luna', contactName: 'Eva Luna', status: 'Cancelled', cancelReason: 'El cliente desistió', reservedUntil: hours(-10), total: 300 }),
    build({ number: 'ARM-CM-000006', name: 'Cotización sin reservar', kind: 'Build', channel: 'Desktop', status: 'Quoted', reservedUntil: null }),
  ];
  mails: MailRecord[] = [
    mail({ id: 'm-1', reservation: 'RES-WEB-000001' }),
    mail({ id: 'm-2', reservation: 'ARM-WEB-000003', reservationKind: 'Build', recipient: 'luis@correo.example', status: 'Exhausted', statusText: 'Agotado', attempts: 5, lastError: 'El servidor de correo no respondió.' }),
    mail({ id: 'm-3', reservation: 'RES-WEB-000005', status: 'Cancelled', statusText: 'Cancelado' }),
  ];
  lines: Record<string, BuildLineData[]> = {
    'RES-WEB-000001': [
      line({ sku: 'MOU-LOG-G502', name: 'Mouse Logitech G502', unitPrice: 350, subtotal: 350, stock: 3 }),
      line({ sku: 'MON-27-QHD', name: 'Monitor 27" QHD', unitPrice: 1500, subtotal: 1500, stock: 0 }),
    ],
  };
  calls: { operation: string; payload: Payload }[] = [];
  /** Errores de una sola vez por operación. */
  failures = new Map<string, WebApiError>();
  private sequence = 10;

  payloads(operation: string): Payload[] {
    return this.calls.filter((call) => call.operation === operation).map((call) => call.payload);
  }

  private find(number: unknown): BuildRecord {
    const found = this.builds.find((item) => item.number === number);
    if (!found) throw new WebApiError({ kind: 'not_found', status: 404, message: `La reserva ${String(number)} no existe o es de otra sucursal.` });
    return found;
  }

  private replace(next: BuildRecord): BuildRecord {
    this.builds = this.builds.map((item) => (item.number === next.number ? next : item));
    return next;
  }

  handle(operation: string, payload: Payload): { handled: true; result: unknown } | { handled: false } {
    const known = [
      'GetPcBuildsQuery',
      'GetPcBuildQuery',
      'GetOutgoingMailsQuery',
      'ReleasePcBuildReservationCommand',
      'ResendReservationMailCommand',
      'ReserveCartCommand',
      'GetSellableProductsQuery',
      'GetCustomersQuery',
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

  private execute(operation: string, payload: Payload): unknown {
    switch (operation) {
      case 'GetPcBuildsQuery':
        return this.builds.filter(
          (item) =>
            (payload.status == null || item.status === payload.status) &&
            (payload.channel == null || item.channel === payload.channel) &&
            (payload.kind == null || item.kind === payload.kind),
        );
      case 'GetPcBuildQuery': {
        const row = this.find(payload.number);
        const items = this.lines[row.number] ?? [line({ sku: 'SKU-1', name: 'Producto de la reserva', unitPrice: row.total, subtotal: row.total })];
        const detail: BuildDetailData = {
          build: row,
          check: { items, issues: [], isCompatible: true, estimatedDrawW: 0, recommendedPsuW: 0, psuW: null, total: row.total },
          quotedItems: items,
          history: [
            { occurredAt: row.createdAt, action: 'Created', status: 'Draft', detail: 'Carrito creado desde la tienda web', user: 'tienda-web' },
            { occurredAt: row.createdAt, action: 'Reserved', status: 'Reserved', detail: 'Stock reservado hasta el 29/09/2026 12:00 (tienda web)', user: 'tienda-web' },
          ],
        };
        return detail;
      }
      case 'GetOutgoingMailsQuery':
        return this.mails
          .filter((item) => (payload.number == null || item.reservation === payload.number) && (payload.status == null || item.status === payload.status))
          .sort((a, b) => b.requestedAt.localeCompare(a.requestedAt))
          .slice(0, Number(payload.take ?? 200));
      case 'ReleasePcBuildReservationCommand': {
        const row = this.find(payload.number);
        if (row.status !== 'Reserved') throw new WebApiError({ kind: 'domain', status: 422, code: 'pcbuild.state', message: `El armado ${row.number} está anulado: no tiene una reserva que liberar.` });
        return this.replace({ ...row, status: 'Cancelled', cancelReason: String(payload.reason), reserved: 0 });
      }
      case 'ResendReservationMailCommand': {
        const row = this.find(payload.number);
        const recipient = (payload.email as string | null) ?? row.contactEmail;
        if (!recipient)
          throw new WebApiError({ kind: 'domain', status: 422, code: 'mail.no_recipient', message: `La reserva ${row.number} no tiene correo de contacto: indique a qué correo enviar la confirmación.` });
        const queued = mail({ id: `m-${++this.sequence}`, reservation: row.number, recipient, status: 'Pending', statusText: 'Pendiente', attempts: 0, requestedAt: new Date().toISOString(), lastAttemptAt: null, completedAt: null });
        this.mails = [...this.mails.map((item) => (item.reservation === row.number && item.status === 'Pending' ? { ...item, status: 'Cancelled' as const } : item)), queued];
        return queued;
      }
      case 'ReserveCartCommand': {
        const items = payload.items as { sku: string; quantity: number }[];
        const short = items.filter((item) => (PRODUCTS.find((product) => product.sku === item.sku)?.available ?? 0) < item.quantity);
        if (short.length > 0) {
          throw new WebApiError({
            kind: 'domain',
            status: 422,
            code: 'storefront.insufficient_stock',
            message: 'No hay stock suficiente para reservar.',
            errors: short.map((item) => `${item.sku}: pidió ${item.quantity}, hay ${PRODUCTS.find((product) => product.sku === item.sku)?.available ?? 0}.`),
          });
        }
        const number = `RES-CM-0000${++this.sequence}`;
        const total = items.reduce((sum, item) => sum + (PRODUCTS.find((product) => product.sku === item.sku)?.price ?? 0) * item.quantity, 0);
        const row = build({
          number,
          name: (payload.name as string | null) ?? `Reserva de ${String(payload.contactName)}`,
          channel: 'Desktop',
          contactName: String(payload.contactName),
          contactPhone: String(payload.contactPhone),
          contactEmail: (payload.contactEmail as string | null) ?? null,
          createdAt: new Date().toISOString(),
          reservedUntil: hours(24 * Number(payload.holdDays ?? 2)),
          total,
        });
        this.builds = [row, ...this.builds];
        return row;
      }
      case 'GetSellableProductsQuery':
        return PRODUCTS;
      case 'GetCustomersQuery':
        return { categories: [], customers: CUSTOMERS };
      default:
        throw new Error(`Operación no simulada: ${operation}`);
    }
  }
}

/** El RPC de la sesión en memoria, con las operaciones de las reservas atendidas por el servidor simulado. */
function serve(web: MockWeb, server: ReservationsServer) {
  const real = web.backend.rpc.call.bind(web.backend.rpc);
  vi.spyOn(web.backend.rpc, 'call').mockImplementation(async (operation, payload, options) => {
    const answer = server.handle(operation, payload as Payload);
    if (answer.handled) return { result: answer.result as never, replayed: false, requestId: options?.requestId ?? 'prueba' };
    return real(operation, payload, options);
  });
}

async function openReservations(role: StaffRole = 'ADMIN', query = '', server = new ReservationsServer()) {
  const web = await signedInAs(role);
  serve(web, server);
  const view = await renderPanel(<PanelApp registry={REGISTRY} />, { web: web.services, route: `/panel/reservas${query}`, path: '/panel/*' });
  return { web, server, location: view.location };
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

function rowOf(tableElement: HTMLElement, number: string): HTMLElement {
  const found = dataRows(tableElement).find((row) => row.getAttribute('data-row-key') === number);
  if (!found) throw new Error(`No está la fila ${number}`);
  return found;
}

async function rowAction(tableElement: HTMLElement, number: string, action: string) {
  fireEvent.click(within(rowOf(tableElement, number)).getByRole('button', { name: `Acciones de la reserva ${number}` }));
  fireEvent.click(await screen.findByRole('menuitem', { name: action }));
}

function menuLabels(): string[] {
  return within(screen.getByTestId('menu-acciones'))
    .getAllByRole('menuitem')
    .map((item) => item.textContent ?? '');
}

// ---------------------------------------------------------------------------------------------------- lista

describe('Reservas · lista', () => {
  it('muestra solo las reservas (no las cotizaciones), con plazo resaltado, estado y correo; pide todo al servidor', async () => {
    const { server } = await openReservations();
    expect(await screen.findByRole('heading', { level: 1, name: 'Reservas' }, { timeout: 5000 })).toBeInTheDocument();
    const grid = await table();
    // Orden por «Reservado hasta», de la más lejana a la más antigua.
    expect(dataRows(grid).map((row) => row.getAttribute('data-row-key'))).toEqual(['RES-CM-000002', 'RES-WEB-000001', 'ARM-WEB-000003', 'RES-WEB-000005', 'ARM-CM-000004']);
    const soon = rowOf(grid, 'RES-WEB-000001');
    expect(soon).toHaveTextContent('Reserva de Juan Pérez');
    expect(soon).toHaveTextContent('CompraWebJuan Pérez+591 71234567');
    expect(soon).toHaveTextContent('29/09/2026 12:00 · vence en 2 h');
    expect(soon.querySelector('[data-plazo]')).toHaveAttribute('data-plazo', 'soon');
    expect(soon).toHaveTextContent('ReservadaEnviado');
    expect(rowOf(grid, 'RES-CM-000002')).toHaveTextContent('MostradorAna Rojas+591 76543210');
    expect(rowOf(grid, 'RES-CM-000002')).toHaveTextContent('Sin correo');
    expect(rowOf(grid, 'ARM-WEB-000003')).toHaveTextContent('Venció el 29/09/2026 07:00VencidaNo se pudo enviar');
    expect(rowOf(grid, 'ARM-CM-000004')).toHaveTextContent('Tech SRL');
    expect(rowOf(grid, 'ARM-CM-000004')).toHaveTextContent('Vendida');
    expect(rowOf(grid, 'RES-WEB-000005')).toHaveTextContent('LiberadaCancelado');
    expect(screen.getByTestId('reservas-resumen')).toHaveTextContent('5 reservas · Bs 25.150,00 · 1 vence en menos de 6 h');
    expect(server.payloads('GetPcBuildsQuery')).toEqual([{ status: null, channel: null, kind: null }]);
    expect(server.payloads('GetOutgoingMailsQuery')).toEqual([{ status: null, number: null, take: 500 }]);
    expect(document.title).toBe('Reservas · Panel · Tech Zone Gaming');
  });

  it('filtra con listas desplegables: tipo, canal y estado van al servidor y quedan en la dirección', async () => {
    const view = await openReservations();
    const grid = await table();
    fireEvent.change(screen.getByRole('combobox', { name: 'Tipo' }), { target: { value: 'Cart' } });
    await waitFor(() => expect(view.location()).toBe('/panel/reservas?tipo=Cart'));
    await waitFor(() => expect(dataRows(grid).map((row) => row.getAttribute('data-row-key'))).toEqual(['RES-CM-000002', 'RES-WEB-000001', 'RES-WEB-000005']));
    fireEvent.change(screen.getByRole('combobox', { name: 'Canal' }), { target: { value: 'Web' } });
    fireEvent.change(screen.getByRole('combobox', { name: 'Estado' }), { target: { value: 'reservada' } });
    await waitFor(() => expect(view.location()).toBe('/panel/reservas?tipo=Cart&canal=Web&estado=reservada'));
    await waitFor(() => expect(dataRows(grid).map((row) => row.getAttribute('data-row-key'))).toEqual(['RES-WEB-000001']));
    expect(view.server.payloads('GetPcBuildsQuery').at(-1)).toEqual({ status: 'Reserved', channel: 'Web', kind: 'Cart' });
    expect(screen.getByTestId('filtros-activos')).toHaveTextContent('3 activos');
    fireEvent.click(screen.getByRole('button', { name: 'Limpiar filtros' }));
    await waitFor(() => expect(view.location()).toBe('/panel/reservas'));
    await waitFor(() => expect(dataRows(grid)).toHaveLength(5));
  });

  it('«Reservas que vencen hoy» (botón del tablero) llega por la dirección: vigentes que vencen hoy, la más urgente primero', async () => {
    const { server } = await openReservations('VENTAS', '?vence=hoy&orden=plazo&sentido=asc');
    const grid = await table();
    await waitFor(() => expect(dataRows(grid).map((row) => row.getAttribute('data-row-key'))).toEqual(['RES-WEB-000001']));
    expect(server.payloads('GetPcBuildsQuery')).toEqual([{ status: 'Reserved', channel: null, kind: null }]);
    expect(within(grid).getByRole('columnheader', { name: /Reservado hasta/ })).toHaveAttribute('aria-sort', 'ascending');
    fireEvent.change(screen.getByRole('combobox', { name: 'Vence' }), { target: { value: 'vencidas' } });
    await waitFor(() => expect(dataRows(grid).map((row) => row.getAttribute('data-row-key'))).toEqual(['ARM-WEB-000003']));
  });

  it('busca por teléfono (con espacios) y por nombre, sin acentos', async () => {
    const view = await openReservations();
    const grid = await table();
    const search = screen.getByRole('searchbox', { name: 'Buscar' });
    fireEvent.change(search, { target: { value: '7654 3210' } });
    fireEvent.keyDown(search, { key: 'Enter' });
    await waitFor(() => expect(view.location()).toBe('/panel/reservas?q=7654+3210'));
    await waitFor(() => expect(dataRows(grid).map((row) => row.getAttribute('data-row-key'))).toEqual(['RES-CM-000002']));
    fireEvent.change(search, { target: { value: 'juan perez' } });
    fireEvent.keyDown(search, { key: 'Enter' });
    await waitFor(() => expect(dataRows(grid).map((row) => row.getAttribute('data-row-key'))).toEqual(['RES-WEB-000001']));
  });

  it('si la consulta falla muestra el error con «Reintentar» y se recupera', async () => {
    const server = new ReservationsServer();
    server.failures.set('GetPcBuildsQuery', new WebApiError({ kind: 'network', message: 'Sin conexión' }));
    await openReservations('ADMIN', '', server);
    const error = await screen.findByTestId('estado-error', undefined, { timeout: 5000 });
    expect(error).toHaveTextContent('No se pudo cargar la información');
    fireEvent.click(within(error).getByRole('button', { name: 'Reintentar' }));
    expect(dataRows(await table())).toHaveLength(5);
  });

  it('si la cola de correos no se puede leer, la lista sigue y lo avisa', async () => {
    const server = new ReservationsServer();
    server.failures.set('GetOutgoingMailsQuery', new WebApiError({ kind: 'server', status: 500, message: 'Falla' }));
    await openReservations('ADMIN', '', server);
    const grid = await table();
    expect(await screen.findByText('No se pudo leer el estado de los correos')).toBeInTheDocument();
    expect(dataRows(grid)).toHaveLength(5);
    fireEvent.click(screen.getByRole('button', { name: 'Reintentar' }));
    await waitFor(() => expect(screen.queryByText('No se pudo leer el estado de los correos')).not.toBeInTheDocument());
    await waitFor(() => expect(rowOf(grid, 'RES-WEB-000001')).toHaveTextContent('Enviado'));
  });

  it('exporta a CSV las filas filtradas', async () => {
    const created: Blob[] = [];
    vi.spyOn(URL, 'createObjectURL').mockImplementation((blob) => {
      created.push(blob as Blob);
      return 'blob:prueba';
    });
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    await openReservations('ADMIN', '?estado=vendida');
    await table();
    fireEvent.click(screen.getByRole('button', { name: 'Exportar CSV' }));
    expect(await screen.findByText('Exportación lista')).toBeInTheDocument();
    expect(screen.getByText('Se descargó reservas-2026-09-29.csv (1 filas).')).toBeInTheDocument();
    const lines = (await created[0].text()).replace(/^﻿/, '').trim().split('\r\n');
    expect(lines).toHaveLength(2);
    expect(lines[1]).toContain('"ARM-CM-000004";"PC oficina";"Armado";"Mostrador"');
    expect(lines[1]).toContain('"Vendida"');
  });
});

// ---------------------------------------------------------------------------------------------------- detalle y acciones

describe('Reservas · detalle y acciones', () => {
  it('el detalle lateral muestra datos de factura, notas, productos con su disponibilidad, bitácora y correos', async () => {
    const { server } = await openReservations();
    const grid = await table();
    fireEvent.click(within(rowOf(grid, 'RES-WEB-000001')).getAllByRole('button')[0]);
    const panel = await screen.findByRole('dialog', { name: 'Reserva RES-WEB-000001' });
    expect(await within(panel).findByText('Cédula de identidad (CI)')).toBeInTheDocument();
    expect(panel).toHaveTextContent('4455667');
    expect(panel).toHaveTextContent('Pasa después del trabajo');
    expect(panel).toHaveTextContent('29/09/2026 12:00 · vence en 2 h');
    expect(server.payloads('GetPcBuildQuery')).toEqual([{ number: 'RES-WEB-000001' }]);

    fireEvent.click(within(panel).getByRole('tab', { name: /Productos/ }));
    expect(within(panel).getByText('Mouse Logitech G502')).toBeInTheDocument();
    expect(panel).toHaveTextContent('Reservada · 3 más disponibles');
    expect(panel).toHaveTextContent('Reservada · sin más unidades');

    fireEvent.click(within(panel).getByRole('tab', { name: /Bitácora/ }));
    expect(within(panel).getByText('Stock reservado')).toBeInTheDocument();

    fireEvent.click(within(panel).getByRole('tab', { name: /Correo/ }));
    expect(await within(panel).findByText('juan@correo.example')).toBeInTheDocument();
    expect(panel).toHaveTextContent('Confirmación de reserva · 1 de 5 intentos');
    expect(server.payloads('GetOutgoingMailsQuery')).toContainEqual({ status: null, number: 'RES-WEB-000001', take: 50 });
    // Al pie: las acciones de una reserva vigente.
    expect(within(panel).getByRole('link', { name: 'Vender en caja' })).toHaveAttribute('href', '/panel/caja?reserva=RES-WEB-000001');
    expect(within(panel).getAllByRole('button', { name: 'Reenviar correo' }).length).toBeGreaterThan(0);
    expect(within(panel).getByRole('button', { name: 'Liberar reserva' })).toBeInTheDocument();
  });

  it('Liberar pide el motivo y confirma; envía el comando exacto y la reserva queda liberada', async () => {
    const { server } = await openReservations();
    const grid = await table();
    await rowAction(grid, 'RES-CM-000002', 'Liberar reserva');
    const dialog = await screen.findByRole('alertdialog', { name: '¿Liberar la reserva RES-CM-000002?' });
    expect(dialog).toHaveTextContent('Reserva de Ana Rojas');
    expect(dialog).toHaveTextContent('No se puede deshacer.');
    fireEvent.click(within(dialog).getByRole('button', { name: 'Liberar reserva' }));
    expect(await within(dialog).findByText('Elija el motivo.')).toBeInTheDocument();
    expect(server.payloads('ReleasePcBuildReservationCommand')).toEqual([]);

    fireEvent.change(within(dialog).getByRole('combobox', { name: /^Motivo/ }), { target: { value: '_otro' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Liberar reserva' }));
    expect(await within(dialog).findByText('Escriba el motivo.')).toBeInTheDocument();
    fireEvent.change(within(dialog).getByRole('textbox', { name: /Escriba el motivo/ }), { target: { value: '  Pidió otro modelo ' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Liberar reserva' }));
    });
    expect(await screen.findByText('Reserva RES-CM-000002 liberada: el stock volvió a estar disponible')).toBeInTheDocument();
    expect(server.payloads('ReleasePcBuildReservationCommand')).toEqual([{ number: 'RES-CM-000002', reason: 'Pidió otro modelo' }]);
    await waitFor(() => expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument());
    await waitFor(() => expect(rowOf(grid, 'RES-CM-000002')).toHaveTextContent('Liberada'));
  });

  it('si el servidor rechaza la liberación, el mensaje queda dentro del diálogo para reintentar', async () => {
    const server = new ReservationsServer();
    server.failures.set(
      'ReleasePcBuildReservationCommand',
      new WebApiError({ kind: 'concurrency', status: 409, message: 'Otra persona cambió la reserva: vuelva a intentarlo.' }),
    );
    await openReservations('ADMIN', '', server);
    const grid = await table();
    await rowAction(grid, 'RES-WEB-000001', 'Liberar reserva');
    const dialog = await screen.findByRole('alertdialog', { name: '¿Liberar la reserva RES-WEB-000001?' });
    fireEvent.change(within(dialog).getByRole('combobox', { name: /^Motivo/ }), { target: { value: 'El cliente desistió' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Liberar reserva' }));
    });
    expect(await within(dialog).findByText('Otra persona cambió la reserva: vuelva a intentarlo.')).toBeInTheDocument();
    expect(screen.getByRole('alertdialog')).toBeInTheDocument();
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Liberar reserva' }));
    });
    await waitFor(() => expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument());
    expect(server.payloads('ReleasePcBuildReservationCommand')).toEqual([
      { number: 'RES-WEB-000001', reason: 'El cliente desistió' },
      { number: 'RES-WEB-000001', reason: 'El cliente desistió' },
    ]);
  });

  it('Reenviar correo: al de la reserva (vacío) o a otro válido; sin correo de contacto lo pide', async () => {
    const { server } = await openReservations();
    const grid = await table();
    await rowAction(grid, 'RES-WEB-000001', 'Reenviar correo');
    let dialog = await screen.findByRole('dialog', { name: 'Reenviar el correo de la reserva' });
    expect(dialog).toHaveTextContent('Déjelo vacío para usar el correo de la reserva (juan@correo.example).');
    const field = within(dialog).getByRole('textbox', { name: /Enviar a/ });
    fireEvent.change(field, { target: { value: 'no-es-correo' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Reenviar correo' }));
    expect(await within(dialog).findByText('Escriba un correo válido, por ejemplo nombre@correo.com.')).toBeInTheDocument();
    fireEvent.change(field, { target: { value: '' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Reenviar correo' }));
    });
    expect(await screen.findByText('Correo de la reserva RES-WEB-000001 en la cola de envío')).toBeInTheDocument();
    expect(server.payloads('ResendReservationMailCommand')).toEqual([{ number: 'RES-WEB-000001', email: null }]);
    await waitFor(() => expect(rowOf(grid, 'RES-WEB-000001')).toHaveTextContent('Pendiente'));

    // Sin correo de contacto hay que indicarlo.
    await rowAction(grid, 'RES-CM-000002', 'Reenviar correo');
    dialog = await screen.findByRole('dialog', { name: 'Reenviar el correo de la reserva' });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Reenviar correo' }));
    expect(await within(dialog).findByText('La reserva no tiene correo de contacto: indique a qué correo enviar la confirmación.')).toBeInTheDocument();
    fireEvent.change(within(dialog).getByRole('textbox', { name: /Enviar a/ }), { target: { value: ' ana@correo.example ' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Reenviar correo' }));
    });
    await waitFor(() => expect(server.payloads('ResendReservationMailCommand')).toEqual([
      { number: 'RES-WEB-000001', email: null },
      { number: 'RES-CM-000002', email: 'ana@correo.example' },
    ]));
  });

  it('Copiar teléfono, Abrir WhatsApp (solo con los dígitos) y Vender en caja', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText } });
    const open = vi.spyOn(window, 'open').mockImplementation(() => null);
    const view = await openReservations();
    const grid = await table();
    await rowAction(grid, 'RES-WEB-000001', 'Copiar teléfono');
    expect(await screen.findByText('Teléfono copiado')).toBeInTheDocument();
    expect(writeText).toHaveBeenCalledWith('+591 71234567');
    await rowAction(grid, 'RES-WEB-000001', 'Abrir WhatsApp');
    expect(open).toHaveBeenCalledWith('https://wa.me/59171234567', '_blank', 'noopener,noreferrer');
    // Una vendida o liberada no ofrece los comandos de una reserva vigente.
    fireEvent.click(within(rowOf(grid, 'ARM-CM-000004')).getByRole('button', { name: 'Acciones de la reserva ARM-CM-000004' }));
    expect(menuLabels()).toEqual(['Ver detalle']);
    fireEvent.keyDown(screen.getByTestId('menu-acciones'), { key: 'Escape' });
    await rowAction(grid, 'RES-WEB-000001', 'Vender en caja');
    await waitFor(() => expect(view.location()).toBe('/panel/caja?reserva=RES-WEB-000001'));
    Reflect.deleteProperty(navigator, 'clipboard');
  });

  it('Nueva reserva en mostrador: valida, busca productos y clientes, envía ReserveCartCommand exacto y abre la reserva creada', async () => {
    const { server } = await openReservations();
    await table();
    fireEvent.click(screen.getByRole('button', { name: 'Nueva reserva en mostrador' }));
    const dialog = await screen.findByRole('dialog', { name: 'Nueva reserva en mostrador' });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Reservar' }));
    expect(await within(dialog).findByText('Agregue al menos un producto a la reserva.')).toBeInTheDocument();
    expect(within(dialog).getByText('Indique el nombre de quien recoge la reserva.')).toBeInTheDocument();
    expect(within(dialog).getByText('Indique un teléfono o WhatsApp para avisar al cliente.')).toBeInTheDocument();
    expect(server.payloads('ReserveCartCommand')).toEqual([]);

    // Producto: se busca por nombre, SKU o código de barras y se elige de la lista.
    const product = within(dialog).getByRole('combobox', { name: 'Agregar un producto' });
    await waitFor(() => expect(product).not.toBeDisabled());
    fireEvent.change(product, { target: { value: '7790001112223' } });
    fireEvent.click(await within(dialog).findByRole('option', { name: /Mouse Logitech G502/ }));
    const quantity = within(dialog).getByRole('textbox', { name: 'Cantidad de Mouse Logitech G502' });
    fireEvent.change(quantity, { target: { value: '2' } });
    expect(within(dialog).getByTestId('total-de-la-reserva')).toHaveTextContent('Bs 700,00');

    // Cliente registrado: completa nombre, teléfono y correo.
    fireEvent.change(within(dialog).getByRole('combobox', { name: /Cliente registrado/ }), { target: { value: 'ana' } });
    fireEvent.click(await within(dialog).findByRole('option', { name: /Ana Rojas/ }));
    expect(within(dialog).getByRole('textbox', { name: 'Nombre' })).toHaveValue('Ana Rojas');
    expect(within(dialog).getByRole('textbox', { name: 'Teléfono o WhatsApp' })).toHaveValue('76543210');
    fireEvent.change(within(dialog).getByRole('combobox', { name: 'Días para recoger' }), { target: { value: '3' } });
    fireEvent.change(within(dialog).getByRole('textbox', { name: /^Notas/ }), { target: { value: 'Pasa el sábado' } });

    // Datos para la factura: con NIT el número solo admite dígitos; el complemento solo aparece con CI.
    fireEvent.change(within(dialog).getByRole('combobox', { name: 'Tipo de documento' }), { target: { value: '5' } });
    expect(within(dialog).queryByRole('textbox', { name: /Complemento/ })).not.toBeInTheDocument();
    fireEvent.change(within(dialog).getByRole('textbox', { name: 'Número de documento' }), { target: { value: '10203A' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Reservar' }));
    expect(await within(dialog).findByText('Con NIT el número solo admite dígitos.')).toBeInTheDocument();
    fireEvent.change(within(dialog).getByRole('textbox', { name: 'Número de documento' }), { target: { value: '1020304050' } });
    fireEvent.change(within(dialog).getByRole('textbox', { name: /Nombre o razón social/ }), { target: { value: 'Rojas SRL' } });

    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Reservar' }));
    });
    expect(await screen.findByText('Reserva RES-CM-000011 creada')).toBeInTheDocument();
    expect(server.payloads('ReserveCartCommand')).toEqual([
      {
        items: [{ sku: 'MOU-LOG-G502', quantity: 2 }],
        contactName: 'Ana Rojas',
        contactPhone: '76543210',
        contactEmail: 'ana@correo.example',
        notes: 'Pasa el sábado',
        holdDays: 3,
        buyer: { documentType: 5, documentNumber: '1020304050', complement: null, name: 'Rojas SRL' },
        customerCode: 'CLI-0002',
        name: null,
      },
    ]);
    // Se abre el detalle de la reserva nueva.
    expect(await screen.findByRole('dialog', { name: 'Reserva RES-CM-000011' })).toBeInTheDocument();
  });

  it('si falta stock, el servidor dice qué falta y el formulario sigue abierto con lo cargado', async () => {
    const { server } = await openReservations();
    await table();
    fireEvent.click(screen.getByRole('button', { name: 'Nueva reserva en mostrador' }));
    const dialog = await screen.findByRole('dialog', { name: 'Nueva reserva en mostrador' });
    const product = within(dialog).getByRole('combobox', { name: 'Agregar un producto' });
    await waitFor(() => expect(product).not.toBeDisabled());
    fireEvent.change(product, { target: { value: 'razer' } });
    fireEvent.click(await within(dialog).findByRole('option', { name: /Teclado Razer Huntsman/ }));
    expect(dialog).toHaveTextContent('sin stock disponible');
    fireEvent.change(within(dialog).getByRole('textbox', { name: 'Nombre' }), { target: { value: 'Pedro Paz' } });
    fireEvent.change(within(dialog).getByRole('textbox', { name: 'Teléfono o WhatsApp' }), { target: { value: '+591 7000-1122' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Reservar' }));
    });
    const error = await within(dialog).findByTestId('error-del-servidor');
    expect(error).toHaveTextContent('No hay stock suficiente para reservar.');
    expect(error).toHaveTextContent('TEC-RZ-HUNT: pidió 1, hay 0.');
    expect(screen.getByRole('dialog', { name: 'Nueva reserva en mostrador' })).toBeInTheDocument();
    expect(within(dialog).getByRole('textbox', { name: 'Nombre' })).toHaveValue('Pedro Paz');
    expect(server.payloads('ReserveCartCommand')).toHaveLength(1);
  });

  it('el botón del tablero (?nueva=1) abre el formulario y se quita de la dirección', async () => {
    const view = await openReservations('CAJERO', '?nueva=1');
    expect(await screen.findByRole('dialog', { name: 'Nueva reserva en mostrador' }, { timeout: 5000 })).toBeInTheDocument();
    await waitFor(() => expect(view.location()).toBe('/panel/reservas'));
  });
});

// ---------------------------------------------------------------------------------------------------- permisos

describe('Reservas · permisos', () => {
  it('la gerencia (sin caja) no ve «Vender en caja», pero sí liberar y reenviar', async () => {
    await openReservations('GERENCIA');
    const grid = await table();
    fireEvent.click(within(rowOf(grid, 'RES-WEB-000001')).getByRole('button', { name: 'Acciones de la reserva RES-WEB-000001' }));
    expect(menuLabels()).toEqual(['Ver detalle', 'Reenviar correo', 'Copiar teléfono', 'Abrir WhatsApp', 'Liberar reserva']);
  });

  it('bodega (sin gestión de reservas ni ventas) ve «No tiene acceso» con los permisos que faltan', async () => {
    await openReservations('BODEGA');
    const denied = await screen.findByTestId('sin-acceso', undefined, { timeout: 5000 });
    expect(denied).toHaveTextContent('Faltan los permisos');
    expect(screen.queryByTestId('tabla')).not.toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------------------------------- tablero

describe('Reservas · tablero', () => {
  it('ofrece «Nueva reserva en mostrador» y «Reservas que vencen hoy» y la estadística, solo a quien tiene los permisos', () => {
    const ventas = ['sales.pcbuild.manage', 'sales.view', 'inventory.stock.view'];
    expect(dashboardActions(REGISTRY, ventas).flatMap((group) => group.actions.map((item) => [item.action.label, item.to]))).toEqual([
      ['Nueva reserva en mostrador', '/panel/reservas?nueva=1'],
      ['Reservas que vencen hoy', '/panel/reservas?vence=hoy&orden=plazo&sentido=asc'],
    ]);
    expect(dashboardStats(REGISTRY, ventas).map((item) => item.stat.title)).toEqual(['Reservas activas y su valor']);
    expect(dashboardActions(REGISTRY, ['inventory.stock.view', 'reports.view'])).toEqual([]);
  });

  it('la estadística resume las reservas vigentes con su propio estado y enlaza a la lista', async () => {
    const web = await signedInAs('VENTAS');
    const server = new ReservationsServer();
    serve(web, server);
    await renderPanel(<ActiveReservationsStat />, { web: web.services });
    const stat = await screen.findByTestId('reservas-activas');
    await waitFor(() => expect(stat).toHaveTextContent('Reservas activas2'));
    expect(stat).toHaveTextContent('Valor reservadoBs 2.550,00');
    expect(stat).toHaveTextContent('Vencen en menos de 6 h1');
    expect(stat).toHaveTextContent('Vencidas sin liberar1');
    expect(within(stat).getByRole('link', { name: 'Ver las reservas activas' })).toHaveAttribute('href', '/panel/reservas?estado=reservada');
    expect(server.payloads('GetPcBuildsQuery')).toEqual([{ status: 'Reserved', channel: null, kind: null }]);
  });

  it('si la estadística falla, muestra su error con «Reintentar»', async () => {
    const web = await signedInAs('VENTAS');
    vi.spyOn(web.backend.rpc, 'call').mockRejectedValue(new WebApiError({ kind: 'server', status: 500, message: 'Falla' }));
    await renderPanel(<ActiveReservationsStat />, { web: web.services });
    expect(await screen.findByTestId('estado-error')).toHaveTextContent('No se pudo cargar la información');
    expect(screen.getByRole('button', { name: 'Reintentar' })).toBeInTheDocument();
  });
});
