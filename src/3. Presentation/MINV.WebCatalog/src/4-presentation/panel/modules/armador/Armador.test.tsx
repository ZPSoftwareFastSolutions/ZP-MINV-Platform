// Módulo «Armador de PC» dentro del panel real (con un registro que tiene solo este módulo). El servidor del armador se
// simula en la prueba con un catálogo técnico chico y una regla de compatibilidad de juguete (el socket del procesador y
// el de la placa): la página no decide nada, muestra lo que responde el servidor. Lista de cotizaciones con filtros en la
// dirección, acciones por estado (reservar, publicar, liberar, anular), detalle lateral, armado paso a paso con
// compatibilidad en vivo, cotizar con errores (confirmación), borrador con cliente y vigencia, extras por categoría,
// armado congelado con «Vender en caja» e «Imprimir cotización», permisos y el botón del tablero. Ninguna prueba toca la red.

import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { WebApiError } from '@/1-domain/auth/errors';
import { renderPanel, signedInAs, type MockWeb, type StaffRole } from '@/test-utils';
import { buildRegistry, dashboardActions } from '../../registry';
import { PanelApp } from '../../shell/PanelApp';
import armador from './module';
import type { BuildRecord, ItemView, SlotCode } from './builder';

const REGISTRY = buildRegistry([{ source: '../modules/armador/module.tsx', definition: armador }]);

/** 10:00 de La Paz del 29/09/2026. */
const NOW = new Date('2026-09-29T14:00:00Z');
const hours = (value: number) => new Date(NOW.getTime() + value * 3_600_000).toISOString();

beforeAll(async () => {
  await import('../../shell/PanelLayout');
  await import('./BuildsPage');
  await import('./BuilderPage');
}, 30_000);

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(NOW);
});

afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
  document.documentElement.removeAttribute('data-print');
});

// ---------------------------------------------------------------------------------------------------- servidor simulado

interface CatalogPart {
  slot: SlotCode;
  sku: string;
  name: string;
  brand: string | null;
  price: number;
  stock: number;
  socket?: string;
  draw?: number;
  watts?: number;
  category?: string;
}

const CATALOG: CatalogPart[] = [
  { slot: 'Cpu', sku: 'CPU-R5-7600', name: 'Ryzen 5 7600 (AM5)', brand: 'AMD', price: 1650, stock: 4, socket: 'AM5', draw: 65 },
  { slot: 'Cpu', sku: 'CPU-I5-14400', name: 'Core i5 14400 (LGA1700)', brand: 'Intel', price: 1800, stock: 0, socket: 'LGA1700', draw: 65 },
  { slot: 'Motherboard', sku: 'MB-B650', name: 'Placa B650 AM5', brand: 'ASUS', price: 1400, stock: 3, socket: 'AM5' },
  { slot: 'Motherboard', sku: 'MB-B760', name: 'Placa B760 LGA1700', brand: 'MSI', price: 1200, stock: 3, socket: 'LGA1700' },
  { slot: 'Ram', sku: 'RAM-DDR5-16', name: 'Kit 16 GB DDR5', brand: 'Kingston', price: 600, stock: 10 },
  { slot: 'Gpu', sku: 'GPU-4060', name: 'GeForce RTX 4060 8 GB', brand: 'NVIDIA', price: 2600, stock: 0, draw: 115 },
  { slot: 'Storage', sku: 'SSD-1TB', name: 'SSD NVMe 1 TB', brand: null, price: 500, stock: 8 },
  { slot: 'Psu', sku: 'PSU-650', name: 'Fuente 650 W', brand: 'Corsair', price: 450, stock: 5, watts: 650 },
  { slot: 'Case', sku: 'CASE-ATX', name: 'Gabinete ATX', brand: 'NZXT', price: 400, stock: 6 },
  { slot: 'Cooler', sku: 'FAN-120', name: 'Disipador 120 mm', brand: 'Cooler Master', price: 150, stock: 9 },
  { slot: 'Monitor', sku: 'MON-27', name: 'Monitor 27" QHD', brand: 'LG', price: 1500, stock: 2, category: 'MON' },
  { slot: 'Peripheral', sku: 'MOU-G502', name: 'Mouse Logitech G502', brand: 'Logitech', price: 350, stock: 7, category: 'PER' },
];

const CUSTOMERS = [
  { code: 'CF', name: 'Consumidor final', category: 'General', categoryCode: 'GEN', email: null, phone: null, taxId: null, isActive: true, lastPurchase: null, purchases: 0, total: 0 },
  { code: 'CLI-0002', name: 'Ana Rojas', category: 'General', categoryCode: 'GEN', email: null, phone: null, taxId: '4455667', isActive: true, lastPurchase: null, purchases: 1, total: 900 },
];

type Payload = Record<string, unknown>;
type Line = { slot: SlotCode | null; sku: string; quantity: number; unitPrice: number };

function part(sku: string): CatalogPart {
  const found = CATALOG.find((item) => item.sku === sku);
  if (!found) throw new WebApiError({ kind: 'not_found', status: 404, message: `El producto ${sku} no existe en el catálogo.` });
  return found;
}

/** El informe de compatibilidad «de juguete» del servidor simulado. */
function report(lines: readonly { slot: SlotCode | null; sku: string; quantity: number }[], prices?: readonly number[]) {
  const parts = lines.map((line) => ({ line, part: part(line.sku) }));
  const cpu = parts.find((item) => item.line.slot === 'Cpu')?.part;
  const board = parts.find((item) => item.line.slot === 'Motherboard')?.part;
  const psu = parts.find((item) => item.line.slot === 'Psu')?.part;
  const issues: { code: string; isError: boolean; message: string }[] = [];
  if (cpu && board && cpu.socket !== board.socket) issues.push({ code: 'cpu.socket', isError: true, message: `El procesador (${cpu.socket}) no entra en la placa (${board.socket}).` });
  if (parts.length > 0 && !psu) issues.push({ code: 'psu.missing', isError: false, message: 'Falta la fuente de poder.' });
  const draw = 75 + parts.reduce((sum, item) => sum + (item.part.draw ?? 0) * item.line.quantity, 0);
  const items: ItemView[] = parts.map(({ line, part: found }, index) => {
    const unitPrice = prices?.[index] ?? found.price;
    return { slot: line.slot, sku: found.sku, name: found.name, quantity: line.quantity, unitPrice, subtotal: unitPrice * line.quantity, stock: found.stock, imageId: null, keySpecs: found.socket ? [found.socket] : [] };
  });
  return {
    items,
    issues,
    isCompatible: !issues.some((issue) => issue.isError),
    estimatedDrawW: draw,
    recommendedPsuW: Math.round(draw * 1.3),
    psuW: psu?.watts ?? null,
    total: items.reduce((sum, item) => sum + item.subtotal, 0),
  };
}

function build(overrides: Partial<BuildRecord>): BuildRecord {
  return {
    id: `id-${overrides.number ?? 'x'}`,
    number: 'ARM-CM-000010',
    name: 'PC gamer',
    branchCode: 'CM',
    customer: null,
    status: 'Quoted',
    validUntil: '2026-10-06',
    isExpired: false,
    total: 5000,
    items: 3,
    isCompatible: true,
    createdAt: hours(-24),
    invoiceNumber: null,
    quotedWithErrors: false,
    channel: 'Desktop',
    contactName: null,
    contactPhone: null,
    contactEmail: null,
    reservedUntil: null,
    publishedToWeb: false,
    cancelReason: null,
    notes: null,
    reserved: 0,
    kind: 'Build',
    buyerDocumentType: null,
    buyerDocumentNumber: null,
    buyerComplement: null,
    buyerName: null,
    ...overrides,
  };
}

const QUOTED_LINES: Line[] = [
  { slot: 'Cpu', sku: 'CPU-R5-7600', quantity: 1, unitPrice: 1600 },
  { slot: 'Motherboard', sku: 'MB-B650', quantity: 1, unitPrice: 1400 },
  { slot: 'Psu', sku: 'PSU-650', quantity: 1, unitPrice: 450 },
];

class ArmadorServer {
  builds: BuildRecord[] = [
    build({ number: 'ARM-CM-000010', name: 'PC gamer', customer: 'Ana Rojas', total: 3450 }),
    build({ number: 'ARM-CM-000011', status: 'Draft', name: 'Borrador oficina', total: 1650, items: 1 }),
    build({ number: 'ARM-WEB-000012', status: 'Reserved', channel: 'Web', name: 'Armado web de Luis', contactName: 'Luis Mamani', contactPhone: '+59170011223', reservedUntil: hours(20), total: 9800, reserved: 3 }),
    build({ number: 'ARM-CM-000013', status: 'Sold', name: 'PC oficina', invoiceNumber: 'F-CM-000120', publishedToWeb: true, total: 12500 }),
    build({ number: 'ARM-CM-000014', isExpired: true, name: 'Cotización vieja', total: 4000 }),
    build({ number: 'ARM-CM-000015', status: 'Cancelled', name: 'Anulado', cancelReason: 'El cliente desistió', total: 3000 }),
    build({ number: 'RES-CM-000020', kind: 'Cart', status: 'Reserved', name: 'Reserva de Ana', reservedUntil: hours(10) }),
  ];
  lines: Record<string, Line[]> = {
    'ARM-CM-000010': QUOTED_LINES,
    'ARM-CM-000011': [{ slot: 'Cpu', sku: 'CPU-R5-7600', quantity: 1, unitPrice: 1650 }],
  };
  calls: { operation: string; payload: Payload }[] = [];
  failures = new Map<string, WebApiError>();
  private sequence = 29;

  payloads(operation: string): Payload[] {
    return this.calls.filter((call) => call.operation === operation).map((call) => call.payload);
  }

  private find(number: unknown): BuildRecord {
    const found = this.builds.find((item) => item.number === number);
    if (!found) throw new WebApiError({ kind: 'not_found', status: 404, message: `El armado ${String(number)} no existe o es de otra sucursal.` });
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
      'GetPcBuildCandidatesQuery',
      'CheckPcBuildQuery',
      'SavePcBuildCommand',
      'ReservePcBuildCommand',
      'ReleasePcBuildReservationCommand',
      'PublishPcBuildCommand',
      'CancelPcBuildCommand',
      'GetCustomersQuery',
      'GetCatalogOptionsQuery',
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
        const lines = this.lines[row.number] ?? [{ slot: 'Cpu' as const, sku: 'CPU-R5-7600', quantity: 1, unitPrice: row.total }];
        return {
          build: row,
          check: report(lines),
          quotedItems: report(
            lines,
            lines.map((line) => line.unitPrice),
          ).items,
          history: [{ occurredAt: row.createdAt, action: 'Created', status: 'Draft', detail: 'Armado creado', user: 'Andrea Quiroga' }],
        };
      }
      case 'CheckPcBuildQuery':
        return report(payload.items as Line[]);
      case 'GetPcBuildCandidatesQuery': {
        const slot = payload.slot as SlotCode;
        const current = (payload.current as Line[]).filter((line) => line.slot !== slot || ['Ram', 'Gpu', 'Storage', 'Monitor', 'Peripheral', 'Software', 'Service'].includes(slot));
        const byCategory = ['Monitor', 'Peripheral', 'Software', 'Service'].includes(slot);
        if (byCategory && !payload.categoryCode) throw new WebApiError({ kind: 'domain', status: 422, message: 'Indique la categoría de los productos.' });
        const baseline = new Set(report(current).issues.filter((issue) => issue.isError).map((issue) => issue.message));
        const text = String(payload.text ?? '').toUpperCase();
        return CATALOG.filter(
          (item) =>
            (byCategory ? item.category === payload.categoryCode : item.slot === slot) &&
            (!text || item.sku.includes(text) || item.name.toUpperCase().includes(text)) &&
            (!payload.onlyInStock || item.stock > 0),
        )
          .map((item) => {
            const added = report([...current, { slot, sku: item.sku, quantity: 1 }]).issues.filter((issue) => issue.isError && !baseline.has(issue.message));
            return { sku: item.sku, name: item.name, brand: item.brand, price: item.price, stock: item.stock, isCompatible: added.length === 0, reason: added[0]?.message ?? null, imageId: null, keySpecs: item.socket ? [item.socket] : [] };
          })
          .sort((a, b) => Number(b.isCompatible) - Number(a.isCompatible) || a.price - b.price);
      }
      case 'SavePcBuildCommand': {
        const items = payload.items as Line[];
        const checked = report(items);
        if (payload.quote && !checked.isCompatible && !payload.acceptIncompatible) {
          throw new WebApiError({ kind: 'domain', status: 422, code: 'pcbuild.incompatible', message: 'El armado tiene 1 error(es) de compatibilidad: confírmelo explícitamente para cotizarlo igual.' });
        }
        const existing = payload.id ? this.builds.find((item) => item.id === payload.id) : undefined;
        const number = existing?.number ?? `ARM-CM-0000${++this.sequence}`;
        const customer = CUSTOMERS.find((item) => item.code === payload.customerCode)?.name ?? null;
        const validUntil = new Date(NOW.getTime() + Number(payload.validDays) * 86_400_000).toISOString().slice(0, 10);
        const row = build({
          ...(existing ?? {}),
          id: existing?.id ?? `id-${number}`,
          number,
          name: String(payload.name),
          customer,
          status: payload.quote ? 'Quoted' : 'Draft',
          validUntil,
          total: checked.total,
          items: items.length,
          isCompatible: checked.isCompatible,
          quotedWithErrors: Boolean(payload.quote) && !checked.isCompatible,
          createdAt: existing?.createdAt ?? new Date().toISOString(),
        });
        this.lines[number] = items.map((line) => ({ ...line, unitPrice: part(line.sku).price }));
        if (existing) this.replace(row);
        else this.builds = [row, ...this.builds];
        return row;
      }
      case 'ReservePcBuildCommand': {
        const row = this.find(payload.number);
        return this.replace({ ...row, status: 'Reserved', reservedUntil: hours(Number(payload.hours)), reserved: row.items });
      }
      case 'ReleasePcBuildReservationCommand': {
        const row = this.find(payload.number);
        return this.replace({ ...row, status: 'Cancelled', cancelReason: String(payload.reason), reserved: 0 });
      }
      case 'PublishPcBuildCommand': {
        const row = this.find(payload.number);
        return this.replace({ ...row, publishedToWeb: Boolean(payload.published) });
      }
      case 'CancelPcBuildCommand': {
        const row = this.find(payload.number);
        this.replace({ ...row, status: 'Cancelled', cancelReason: (payload.reason as string | null) ?? null });
        return `✔ Armado ${row.number} anulado.`;
      }
      case 'GetCustomersQuery':
        return { categories: [], customers: CUSTOMERS };
      case 'GetCatalogOptionsQuery':
        return {
          categories: [
            { code: 'CPU', name: 'Procesadores' },
            { code: 'MON', name: 'Monitores' },
            { code: 'PER', name: 'Periféricos' },
          ],
          priceListName: 'Lista general',
          suppliers: [],
          taxRate: 13,
          units: [],
          vatOnInvoicedAmount: true,
        };
      default:
        throw new Error(`Operación no simulada: ${operation}`);
    }
  }
}

function serve(web: MockWeb, server: ArmadorServer) {
  const real = web.backend.rpc.call.bind(web.backend.rpc);
  vi.spyOn(web.backend.rpc, 'call').mockImplementation(async (operation, payload, options) => {
    const answer = server.handle(operation, payload as Payload);
    if (answer.handled) return { result: answer.result as never, replayed: false, requestId: options?.requestId ?? 'prueba' };
    return real(operation, payload, options);
  });
}

/** Sesión con SOLO estos permisos (para probar lo que ve quien no gestiona armados). */
function onlyPermissions(web: MockWeb, permissions: string[]) {
  const original = web.backend.session.current.bind(web.backend.session);
  vi.spyOn(web.backend.session, 'current').mockImplementation(async () => {
    const session = await original();
    return session && { ...session, permissions };
  });
}

async function openArmador(path = '', role: StaffRole = 'ADMIN', server = new ArmadorServer(), prepare?: (web: MockWeb) => void) {
  const web = await signedInAs(role);
  prepare?.(web);
  serve(web, server);
  const view = await renderPanel(<PanelApp registry={REGISTRY} />, { web: web.services, route: `/panel/armador${path}`, path: '/panel/*' });
  return { web, server, location: view.location };
}

async function table(): Promise<HTMLElement> {
  const found = await screen.findByTestId('tabla', undefined, { timeout: 5000 });
  await waitFor(() => expect(within(found).queryAllByRole('row').length).toBeGreaterThan(1));
  return found;
}

function keys(tableElement: HTMLElement): (string | null)[] {
  return within(tableElement)
    .getAllByRole('row')
    .filter((row) => row.hasAttribute('data-row-key'))
    .map((row) => row.getAttribute('data-row-key'));
}

function rowOf(tableElement: HTMLElement, number: string): HTMLElement {
  const found = within(tableElement)
    .getAllByRole('row')
    .find((row) => row.getAttribute('data-row-key') === number);
  if (!found) throw new Error(`No está la fila ${number}`);
  return found;
}

async function menuOf(tableElement: HTMLElement, number: string): Promise<string[]> {
  fireEvent.click(within(rowOf(tableElement, number)).getByRole('button', { name: `Acciones de la cotización ${number}` }));
  const menu = await screen.findByTestId('menu-acciones');
  return within(menu)
    .getAllByRole('menuitem')
    .map((item) => item.textContent ?? '');
}

async function candidates(): Promise<HTMLElement> {
  const panel = await screen.findByTestId('candidatos', undefined, { timeout: 5000 });
  await waitFor(() => expect(panel.querySelectorAll('li[data-sku]').length).toBeGreaterThan(0));
  return panel;
}

// ---------------------------------------------------------------------------------------------------- lista

describe('Armador · lista de cotizaciones', () => {
  it('muestra solo armados de PC con canal, compatibilidad, vigencia, estado y publicación; pide al servidor solo armados', async () => {
    const { server } = await openArmador();
    expect(await screen.findByRole('heading', { level: 1, name: 'Armador de PC' }, { timeout: 5000 })).toBeInTheDocument();
    const grid = await table();
    expect(keys(grid)).toEqual(['ARM-CM-000010', 'ARM-CM-000011', 'ARM-WEB-000012', 'ARM-CM-000013', 'ARM-CM-000014', 'ARM-CM-000015']);
    expect(server.payloads('GetPcBuildsQuery')).toEqual([{ status: null, channel: null, kind: 'Build' }]);
    expect(rowOf(grid, 'ARM-CM-000010')).toHaveTextContent('PC gamerAna Rojas · 3 piezasMostradorCompatibleVigente hasta 06/10/2026Cotizado—Bs 3.450,00');
    expect(rowOf(grid, 'ARM-WEB-000012')).toHaveTextContent('Luis Mamani');
    expect(rowOf(grid, 'ARM-WEB-000012')).toHaveTextContent('WebCompatible');
    expect(rowOf(grid, 'ARM-CM-000013')).toHaveTextContent('Venta F-CM-000120VendidoPublicado');
    expect(rowOf(grid, 'ARM-CM-000014')).toHaveTextContent('Venció el 06/10/2026Cotización vencida');
    expect(screen.getByTestId('armados-resumen')).toHaveTextContent('6 armados · Bs 34.400,00');
  });

  it('filtra con listas desplegables: estado al servidor; vigencia y publicado en la página; todo en la dirección', async () => {
    const view = await openArmador();
    const grid = await table();
    fireEvent.change(screen.getByRole('combobox', { name: 'Estado' }), { target: { value: 'Quoted' } });
    await waitFor(() => expect(keys(grid)).toEqual(['ARM-CM-000010', 'ARM-CM-000014']));
    expect(view.server.payloads('GetPcBuildsQuery').at(-1)).toEqual({ status: 'Quoted', channel: null, kind: 'Build' });
    fireEvent.change(screen.getByRole('combobox', { name: 'Vigencia' }), { target: { value: 'vencida' } });
    await waitFor(() => expect(view.location()).toBe('/panel/armador?estado=Quoted&vigencia=vencida'));
    await waitFor(() => expect(keys(grid)).toEqual(['ARM-CM-000014']));
    fireEvent.click(screen.getByRole('button', { name: 'Limpiar filtros' }));
    fireEvent.change(screen.getByRole('combobox', { name: 'Publicado en la web' }), { target: { value: 'si' } });
    await waitFor(() => expect(keys(grid)).toEqual(['ARM-CM-000013']));
    const search = screen.getByRole('searchbox', { name: 'Buscar' });
    fireEvent.click(screen.getByRole('button', { name: 'Limpiar filtros' }));
    fireEvent.change(search, { target: { value: 'luis' } });
    fireEvent.keyDown(search, { key: 'Enter' });
    await waitFor(() => expect(keys(grid)).toEqual(['ARM-WEB-000012']));
  });

  it('cada estado ofrece sus acciones', async () => {
    await openArmador();
    const grid = await table();
    expect(await menuOf(grid, 'ARM-CM-000010')).toEqual(['Abrir en el armador', 'Ver detalle', 'Vender en caja', 'Reservar stock', 'Publicar en la web', 'Anular armado']);
    fireEvent.keyDown(screen.getByTestId('menu-acciones'), { key: 'Escape' });
    expect(await menuOf(grid, 'ARM-WEB-000012')).toEqual(['Abrir en el armador', 'Ver detalle', 'Vender en caja', 'Liberar reserva']);
    fireEvent.keyDown(screen.getByTestId('menu-acciones'), { key: 'Escape' });
    expect(await menuOf(grid, 'ARM-CM-000013')).toEqual(['Abrir en el armador', 'Ver detalle', 'Quitar de la web']);
    fireEvent.keyDown(screen.getByTestId('menu-acciones'), { key: 'Escape' });
    expect(await menuOf(grid, 'ARM-CM-000014')).toEqual(['Abrir en el armador', 'Ver detalle', 'Publicar en la web', 'Anular armado']);
  });

  it('Reservar stock: horas de la lista u otra cantidad (1 a 720); envía el comando exacto', async () => {
    const { server } = await openArmador();
    const grid = await table();
    await menuOf(grid, 'ARM-CM-000010');
    fireEvent.click(screen.getByRole('menuitem', { name: 'Reservar stock' }));
    const dialog = await screen.findByRole('dialog', { name: 'Reservar el stock de ARM-CM-000010' });
    fireEvent.change(within(dialog).getByRole('combobox', { name: 'Horas de reserva' }), { target: { value: 'otra' } });
    fireEvent.change(within(dialog).getByRole('textbox', { name: /Cantidad de horas/ }), { target: { value: '900' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Reservar stock' }));
    expect(await within(dialog).findByText('Escriba un número entero de horas entre 1 y 720.')).toBeInTheDocument();
    fireEvent.change(within(dialog).getByRole('textbox', { name: /Cantidad de horas/ }), { target: { value: '36' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Reservar stock' }));
    });
    expect(await screen.findByText('Stock reservado para ARM-CM-000010 hasta el 30/09/2026 22:00')).toBeInTheDocument();
    expect(server.payloads('ReservePcBuildCommand')).toEqual([{ number: 'ARM-CM-000010', hours: 36 }]);
    await waitFor(() => expect(rowOf(grid, 'ARM-CM-000010')).toHaveTextContent('Reservado'));
  });

  it('Publicar en la web y Anular (con confirmación y motivo opcional)', async () => {
    const { server } = await openArmador();
    const grid = await table();
    await menuOf(grid, 'ARM-CM-000010');
    await act(async () => {
      fireEvent.click(screen.getByRole('menuitem', { name: 'Publicar en la web' }));
    });
    expect(await screen.findByText('ARM-CM-000010 publicado en la tienda web')).toBeInTheDocument();
    expect(server.payloads('PublishPcBuildCommand')).toEqual([{ number: 'ARM-CM-000010', published: true }]);
    await waitFor(() => expect(rowOf(grid, 'ARM-CM-000010')).toHaveTextContent('Publicado'));

    await menuOf(grid, 'ARM-CM-000014');
    fireEvent.click(screen.getByRole('menuitem', { name: 'Anular armado' }));
    const dialog = await screen.findByRole('alertdialog', { name: '¿Anular el armado ARM-CM-000014?' });
    expect(dialog).toHaveTextContent('ya no se puede cobrar en la caja');
    fireEvent.change(within(dialog).getByRole('textbox', { name: /Motivo/ }), { target: { value: ' Compró en otra tienda ' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Anular armado' }));
    });
    expect(await screen.findByText('Armado ARM-CM-000014 anulado')).toBeInTheDocument();
    expect(server.payloads('CancelPcBuildCommand')).toEqual([{ number: 'ARM-CM-000014', reason: 'Compró en otra tienda' }]);
    await waitFor(() => expect(rowOf(grid, 'ARM-CM-000014')).toHaveTextContent('Anulado'));
  });

  it('Liberar la reserva web pide el motivo; el error del servidor queda en el diálogo', async () => {
    const server = new ArmadorServer();
    server.failures.set('ReleasePcBuildReservationCommand', new WebApiError({ kind: 'domain', status: 422, code: 'pcbuild.state', message: 'La reserva ya se vendió en la caja.' }));
    await openArmador('', 'ADMIN', server);
    const grid = await table();
    await menuOf(grid, 'ARM-WEB-000012');
    fireEvent.click(screen.getByRole('menuitem', { name: 'Liberar reserva' }));
    const dialog = await screen.findByRole('alertdialog', { name: '¿Liberar la reserva de ARM-WEB-000012?' });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Liberar reserva' }));
    expect(await within(dialog).findByText('Elija el motivo.')).toBeInTheDocument();
    fireEvent.change(within(dialog).getByRole('combobox', { name: /^Motivo/ }), { target: { value: 'El cliente no pasó a recoger' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Liberar reserva' }));
    });
    expect(await within(dialog).findByText('La reserva ya se vendió en la caja.')).toBeInTheDocument();
    expect(server.payloads('ReleasePcBuildReservationCommand')).toEqual([{ number: 'ARM-WEB-000012', reason: 'El cliente no pasó a recoger' }]);
  });

  it('el detalle lateral muestra piezas, compatibilidad y bitácora, y enlaza al armador y a la caja', async () => {
    const { server } = await openArmador();
    const grid = await table();
    fireEvent.click(within(rowOf(grid, 'ARM-CM-000010')).getAllByRole('button')[0]);
    const panel = await screen.findByRole('dialog', { name: 'Armado ARM-CM-000010' });
    expect(await within(panel).findByText(/Precios congelados hasta el 06\/10\/2026/)).toBeInTheDocument();
    expect(server.payloads('GetPcBuildQuery')).toEqual([{ number: 'ARM-CM-000010' }]);
    fireEvent.click(within(panel).getByRole('tab', { name: /Piezas/ }));
    expect(panel).toHaveTextContent('Ryzen 5 7600 (AM5)');
    expect(panel).toHaveTextContent('Bs 1.600,00');
    fireEvent.click(within(panel).getByRole('tab', { name: /Compatibilidad/ }));
    expect(within(panel).getByTestId('compatibilidad-titulo')).toHaveTextContent('Compatible');
    fireEvent.click(within(panel).getByRole('tab', { name: /Bitácora/ }));
    expect(within(panel).getByText('Creado')).toBeInTheDocument();
    expect(within(panel).getByRole('link', { name: 'Abrir en el armador' })).toHaveAttribute('href', '/panel/armador/ARM-CM-000010');
    expect(within(panel).getByRole('link', { name: 'Vender en caja' })).toHaveAttribute('href', '/panel/caja?reserva=ARM-CM-000010');
  });

  it('si la consulta falla muestra el error con «Reintentar» y se recupera; exporta a CSV', async () => {
    const server = new ArmadorServer();
    server.failures.set('GetPcBuildsQuery', new WebApiError({ kind: 'network', message: 'Sin conexión' }));
    vi.spyOn(URL, 'createObjectURL').mockImplementation(() => 'blob:prueba');
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    await openArmador('', 'ADMIN', server);
    const error = await screen.findByTestId('estado-error', undefined, { timeout: 5000 });
    fireEvent.click(within(error).getByRole('button', { name: 'Reintentar' }));
    expect(keys(await table())).toHaveLength(6);
    fireEvent.click(screen.getByRole('button', { name: 'Exportar CSV' }));
    expect(await screen.findByText('Se descargó armados-2026-09-29.csv (6 filas).')).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------------------------------- armar

describe('Armador · armar paso a paso', () => {
  it('arma con la compatibilidad en vivo del servidor y cotiza con errores solo si se confirma', async () => {
    const view = await openArmador('/nuevo');
    expect(await screen.findByRole('heading', { level: 1, name: 'Armar una PC' }, { timeout: 5000 })).toBeInTheDocument();
    let panel = await candidates();
    expect(panel).toHaveTextContent('Paso 1 de 12');
    expect(view.server.payloads('GetPcBuildCandidatesQuery')[0]).toEqual({ slot: 'Cpu', current: [], text: null, onlyInStock: false, categoryCode: null });
    expect(screen.getByRole('button', { name: 'Guardar cotización' })).toBeDisabled();

    fireEvent.click(within(panel).getByRole('button', { name: 'Agregar Ryzen 5 7600 (AM5)' }));
    // Ranura de una sola pieza: pasa sola a la siguiente obligatoria.
    await waitFor(() => expect(screen.getByTestId('candidatos')).toHaveTextContent('Paso 2 de 12'));
    await waitFor(() => expect(screen.getByTestId('compatibilidad-titulo')).toHaveTextContent('Compatible · 1 aviso'));
    expect(screen.getByTestId('compatibilidad')).toHaveTextContent('Falta la fuente de poder.');
    expect(view.server.payloads('CheckPcBuildQuery').at(-1)).toEqual({ items: [{ slot: 'Cpu', sku: 'CPU-R5-7600', quantity: 1 }] });

    // La placa de otro socket aparece atenuada con el motivo que da el servidor.
    panel = await candidates();
    await waitFor(() => expect(panel.querySelector('li[data-sku="MB-B760"]')).toHaveTextContent('El procesador (AM5) no entra en la placa (LGA1700).'));
    fireEvent.click(within(panel).getByRole('button', { name: 'Agregar Placa B760 LGA1700' }));
    expect(await screen.findByText('Pieza incompatible')).toBeInTheDocument();
    await waitFor(() => expect(screen.getByTestId('compatibilidad-titulo')).toHaveTextContent('1 error de compatibilidad'));
    expect(screen.getByTestId('total-del-armado')).toHaveTextContent('Bs 2.850,00');

    await waitFor(() => expect(screen.getByRole('button', { name: 'Guardar cotización' })).not.toBeDisabled());
    fireEvent.click(screen.getByRole('button', { name: 'Guardar cotización' }));
    const confirm = await screen.findByRole('alertdialog', { name: '¿Cotizar con errores de compatibilidad?' });
    expect(confirm).toHaveTextContent('El procesador (AM5) no entra en la placa (LGA1700).');
    expect(view.server.payloads('SavePcBuildCommand')).toEqual([]);
    await act(async () => {
      fireEvent.click(within(confirm).getByRole('button', { name: 'Cotizar igual' }));
    });
    expect(await screen.findByText('Cotización ARM-CM-000030 emitida · Bs 2.850,00')).toBeInTheDocument();
    expect(view.server.payloads('SavePcBuildCommand')).toEqual([
      {
        id: null,
        name: 'PC Ryzen 5 7600',
        customerCode: null,
        items: [
          { slot: 'Cpu', sku: 'CPU-R5-7600', quantity: 1 },
          { slot: 'Motherboard', sku: 'MB-B760', quantity: 1 },
        ],
        quote: true,
        validDays: 7,
        acceptIncompatible: true,
        kind: 'Build',
      },
    ]);
    // Queda en la dirección del armado guardado, ya congelado.
    await waitFor(() => expect(view.location()).toBe('/panel/armador/ARM-CM-000030'));
    expect(await screen.findByRole('heading', { name: 'Piezas de la cotización' }, { timeout: 5000 })).toBeInTheDocument();
    expect(screen.queryByTestId('candidatos')).not.toBeInTheDocument();
  });

  it('guarda un borrador con nombre, cliente, vigencia y cantidades; al volver a guardar actualiza el mismo', async () => {
    const view = await openArmador('/nuevo');
    let panel = await candidates();
    fireEvent.click(within(panel).getByRole('button', { name: 'Agregar Ryzen 5 7600 (AM5)' }));
    fireEvent.click(within(screen.getByRole('navigation', { name: 'Ranuras' })).getByRole('button', { name: /Memoria RAM/ }));
    panel = await candidates();
    expect(panel).toHaveTextContent('Paso 3 de 12');
    fireEvent.click(within(panel).getByRole('button', { name: 'Agregar Kit 16 GB DDR5' }));
    fireEvent.click(within(panel).getByRole('button', { name: 'Agregar Kit 16 GB DDR5' }));
    const chosen = screen.getByRole('list', { name: 'Piezas elegidas' });
    expect(within(chosen).getByLabelText('Cantidad: 2')).toBeInTheDocument();
    fireEvent.click(within(chosen).getByRole('button', { name: 'Una más de Kit 16 GB DDR5' }));
    fireEvent.click(within(chosen).getByRole('button', { name: 'Una más de Kit 16 GB DDR5' }));
    fireEvent.click(within(chosen).getByRole('button', { name: 'Una menos de Kit 16 GB DDR5' }));
    expect(within(chosen).getByLabelText('Cantidad: 3')).toBeInTheDocument();

    fireEvent.change(screen.getByRole('textbox', { name: /Nombre del armado/ }), { target: { value: 'PC de Ana' } });
    fireEvent.change(screen.getByRole('combobox', { name: /^Cliente/ }), { target: { value: 'ana' } });
    fireEvent.click(await screen.findByRole('option', { name: /Ana Rojas/ }));
    fireEvent.change(screen.getByRole('combobox', { name: 'Vigencia de la cotización' }), { target: { value: '15' } });
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Guardar borrador' }));
    });
    expect(await screen.findByText('Borrador ARM-CM-000030 guardado')).toBeInTheDocument();
    expect(view.server.payloads('SavePcBuildCommand')[0]).toEqual({
      id: null,
      name: 'PC de Ana',
      customerCode: 'CLI-0002',
      items: [
        { slot: 'Cpu', sku: 'CPU-R5-7600', quantity: 1 },
        { slot: 'Ram', sku: 'RAM-DDR5-16', quantity: 3 },
      ],
      quote: false,
      validDays: 15,
      acceptIncompatible: false,
      kind: 'Build',
    });
    await waitFor(() => expect(view.location()).toBe('/panel/armador/ARM-CM-000030'));
    // El borrador se vuelve a abrir editable con lo guardado (el cliente se reconoce por su nombre).
    await waitFor(() => expect(screen.getByRole('textbox', { name: /Nombre del armado/ })).toHaveValue('PC de Ana'));
    await waitFor(() => expect(screen.getByRole('combobox', { name: /^Cliente/ })).toHaveValue('Ana Rojas'));
    expect(within(screen.getByRole('list', { name: 'Piezas elegidas' })).getByLabelText('Cantidad: 3')).toBeInTheDocument();
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Guardar borrador' }));
    });
    await waitFor(() => expect(view.server.payloads('SavePcBuildCommand')).toHaveLength(2));
    expect(view.server.payloads('SavePcBuildCommand')[1]).toMatchObject({ id: 'id-ARM-CM-000030', name: 'PC de Ana', customerCode: 'CLI-0002' });
    expect(view.location()).toBe('/panel/armador/ARM-CM-000030');
  });

  it('los extras se eligen por categoría (propuesta por el nombre) y los candidatos se filtran por marca, precio y stock', async () => {
    const view = await openArmador('/nuevo');
    let panel = await candidates();
    fireEvent.change(within(panel).getByRole('combobox', { name: 'Marca' }), { target: { value: 'Intel' } });
    expect(panel.querySelectorAll('li[data-sku]')).toHaveLength(1);
    fireEvent.change(within(panel).getByRole('combobox', { name: 'Marca' }), { target: { value: '' } });
    fireEvent.change(within(panel).getByRole('textbox', { name: 'Precio hasta' }), { target: { value: '1700' } });
    await waitFor(() => expect([...panel.querySelectorAll('li[data-sku]')].map((item) => item.getAttribute('data-sku'))).toEqual(['CPU-R5-7600']));
    fireEvent.click(within(panel).getByRole('checkbox', { name: 'Solo con stock en la sucursal' }));
    await waitFor(() => expect(view.server.payloads('GetPcBuildCandidatesQuery').at(-1)).toMatchObject({ slot: 'Cpu', onlyInStock: true }));

    fireEvent.click(within(screen.getByRole('navigation', { name: 'Ranuras' })).getByRole('button', { name: /^Monitor/ }));
    panel = await candidates();
    expect(within(panel).getByRole('combobox', { name: 'Categoría' })).toHaveValue('MON');
    expect(panel.querySelector('li[data-sku="MON-27"]')).not.toBeNull();
    expect(view.server.payloads('GetPcBuildCandidatesQuery').at(-1)).toMatchObject({ slot: 'Monitor', categoryCode: 'MON' });
    fireEvent.change(within(panel).getByRole('combobox', { name: 'Categoría' }), { target: { value: 'PER' } });
    await waitFor(() => expect(view.server.payloads('GetPcBuildCandidatesQuery').at(-1)).toMatchObject({ slot: 'Monitor', categoryCode: 'PER' }));
  });

  it('un armado cotizado queda congelado con sus acciones: vender en caja, publicar e imprimir la cotización', async () => {
    const print = vi.spyOn(window, 'print').mockImplementation(() => {
      expect(document.documentElement.getAttribute('data-print')).toBe('cotizacion');
    });
    const view = await openArmador('/ARM-CM-000010');
    expect(await screen.findByRole('heading', { level: 1, name: 'ARM-CM-000010 · PC gamer' }, { timeout: 5000 })).toBeInTheDocument();
    expect(await screen.findByRole('heading', { name: 'Piezas de la cotización' })).toBeInTheDocument();
    expect(screen.getByRole('list', { name: 'Piezas de la cotización' })).toHaveTextContent('Ryzen 5 7600 (AM5)');
    expect(screen.getByTestId('total-del-armado')).toHaveTextContent('Bs 3.450,00');
    expect(screen.queryByTestId('candidatos')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Guardar cotización' })).not.toBeInTheDocument();
    const actions = screen.getByRole('region', { name: 'Acciones del armado' });
    expect(within(actions).getByRole('link', { name: 'Vender en caja' })).toHaveAttribute('href', '/panel/caja?reserva=ARM-CM-000010');
    expect(within(actions).getAllByRole('button').map((button) => button.textContent)).toEqual(['Reservar stock', 'Publicar en la web', 'Imprimir cotización', 'Anular armado']);

    fireEvent.click(within(actions).getByRole('button', { name: 'Imprimir cotización' }));
    expect(print).toHaveBeenCalledTimes(1);
    const printable = screen.getByTestId('cotizacion-imprimible');
    expect(printable).toHaveTextContent('Cotización ARM-CM-000010');
    expect(printable).toHaveTextContent('Tech Zone Gaming S.R.L.');
    expect(printable).toHaveTextContent('Cliente: Ana Rojas');
    window.dispatchEvent(new Event('afterprint'));
    expect(document.documentElement.hasAttribute('data-print')).toBe(false);

    await act(async () => {
      fireEvent.click(within(actions).getByRole('button', { name: 'Publicar en la web' }));
    });
    expect(await screen.findByText('ARM-CM-000010 publicado en la tienda web')).toBeInTheDocument();
    await waitFor(() => expect(within(screen.getByRole('region', { name: 'Acciones del armado' })).getByRole('button', { name: 'Quitar de la web' })).toBeInTheDocument());
    expect(view.server.payloads('PublishPcBuildCommand')).toEqual([{ number: 'ARM-CM-000010', published: true }]);
  });

  it('un número que es una reserva de compra avisa y lleva a Reservas; uno que no existe muestra el error', async () => {
    await openArmador('/RES-CM-000020');
    expect(await screen.findByText('RES-CM-000020 es una reserva de compra, no un armado de PC', undefined, { timeout: 5000 })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Verla en Reservas' })).toHaveAttribute('href', '/panel/reservas?q=RES-CM-000020');
  });

  it('un armado que no existe muestra el error del servidor con «Reintentar»', async () => {
    await openArmador('/ARM-CM-999999');
    const error = await screen.findByTestId('estado-error', undefined, { timeout: 5000 });
    expect(error).toHaveTextContent('El armado ARM-CM-999999 no existe o es de otra sucursal.');
  });
});

// ---------------------------------------------------------------------------------------------------- permisos y tablero

describe('Armador · permisos y tablero', () => {
  it('sin «gestionar armados» se arma y se revisa la compatibilidad, pero no se ofrecen los comandos', async () => {
    const prepare = (web: MockWeb) => onlyPermissions(web, ['sales.view', 'inventory.stock.view']);
    await openArmador('', 'VENTAS', new ArmadorServer(), prepare);
    const grid = await table();
    expect(screen.getByRole('link', { name: 'Armar una PC' })).toBeInTheDocument();
    expect(await menuOf(grid, 'ARM-CM-000010')).toEqual(['Abrir en el armador', 'Ver detalle']);
  });

  it('en el armador, sin «gestionar armados» avisa qué permiso falta para guardar', async () => {
    const prepare = (web: MockWeb) => onlyPermissions(web, ['sales.view', 'inventory.stock.view']);
    await openArmador('/nuevo', 'VENTAS', new ArmadorServer(), prepare);
    const panel = await candidates();
    fireEvent.click(within(panel).getByRole('button', { name: 'Agregar Ryzen 5 7600 (AM5)' }));
    await waitFor(() => expect(screen.getByTestId('compatibilidad-titulo')).toHaveTextContent('Compatible · 1 aviso'));
    expect(screen.getByText('Puede armar y revisar la compatibilidad')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Guardar borrador' })).not.toBeInTheDocument();
  });

  it('bodega (sin ventas) ve «No tiene acceso»', async () => {
    await openArmador('', 'BODEGA');
    expect(await screen.findByTestId('sin-acceso', undefined, { timeout: 5000 })).toBeInTheDocument();
    expect(screen.queryByTestId('tabla')).not.toBeInTheDocument();
  });

  it('ofrece «Armar una PC» en el tablero a quien ve el módulo', () => {
    expect(dashboardActions(REGISTRY, ['sales.view', 'inventory.stock.view']).flatMap((group) => group.actions.map((item) => [item.action.label, item.to]))).toEqual([
      ['Armar una PC', '/panel/armador/nuevo'],
    ]);
    expect(dashboardActions(REGISTRY, ['inventory.stock.view'])).toEqual([]);
  });
});
