// Módulo «Toma física» dentro del panel (con un registro PROPIO de la prueba: solo este módulo) y con un servidor de
// tomas SIMULADO en la prueba (con estado: abrir, contar, quitar, contabilizar y anular cambian la planilla): iniciar con
// los contenidos EXACTOS, planilla con diferencias resaltadas y progreso, filtros en la dirección, el lector de códigos
// (código + Enter), corregir y quitar un conteo, pendientes, generar ajustes con el resumen y anular (con confirmación),
// errores del servidor, imprimir la planilla, CSV y quién entra. Ninguna prueba toca la red.

import { act, configure, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { WebApiError } from '@/1-domain/auth/errors';
import { permissionName, type RpcOperationName, type RpcResponseOf } from '@/4-presentation/app/contract';
import { laPazToday } from '@/4-presentation/panel/lib';
import { renderPanel, signedInAs, type MockWeb, type StaffRole } from '@/test-utils';
import { buildRegistry, dashboardActions } from '../../registry';
import { PanelApp } from '../../shell/PanelApp';
import tomaFisica from './module';

const REGISTRY = buildRegistry([{ source: '../modules/toma-fisica/module.tsx', definition: tomaFisica }]);
const CM = '5d0c1f6e-0a51-4d0e-9d11-000000000001';

/** Con toda la suite en paralelo la primera pantalla tarda más: pruebas y esperas con más margen. */
const SLOW = { timeout: 30_000 };
configure({ asyncUtilTimeout: 5000 });

beforeAll(async () => {
  await import('./PhysicalCountPage');
}, 30_000);

afterEach(() => {
  vi.restoreAllMocks();
});

type Sheet = NonNullable<RpcResponseOf<'GetOpenPhysicalCountQuery'>>;
type Line = Sheet['lines'][number];
type Lookup = RpcResponseOf<'GetProductLookupQuery'>[number];

function item(sku: string, name: string, overrides: Partial<Lookup> = {}): Lookup {
  return { sku, name, category: 'Monitores', unit: 'UND', allowsDecimals: false, barcodes: [], isActive: true, primaryBin: 'CM-A1', variantId: `v-${sku}`, ...overrides };
}

function line(sku: string, name: string, counted: number, system: number, overrides: Partial<Line> = {}): Line {
  return {
    sku,
    name,
    unit: 'UND',
    binCode: 'CM-A1',
    lotNumber: 'SIN-LOTE',
    systemQuantity: system,
    countedQuantity: counted,
    difference: counted - system,
    countedBy: 'Bruno Mamani',
    countedAt: '2026-09-29T14:00:00Z',
    ...overrides,
  };
}

const PRODUCTS: Lookup[] = [
  item('MON-1', 'Monitor LG 27GP850', { barcodes: ['8806091234567'] }),
  item('MON-2', 'Monitor AOC 24G2', { primaryBin: 'CM-A2' }),
  item('TEC-1', 'Teclado mecánico', { category: 'Periféricos', primaryBin: 'CM-B1' }),
];

function openSheet(lines: Line[] = []): Sheet {
  return { id: 'toma-1', number: 'CF-20260929-1', countDate: '2026-09-29', warehouseCode: 'CM-PRINCIPAL', notes: 'Conteo mensual', lines };
}

const LINES: Line[] = [line('MON-1', 'Monitor LG 27GP850', 7, 5), line('TEC-1', 'Teclado mecánico', 2, 4, { binCode: 'CM-B1', countedBy: 'Andrea Quiroga', countedAt: '2026-09-29T15:00:00Z' })];

type Handler = (payload: unknown) => unknown;
const COMMANDS: readonly string[] = ['OpenPhysicalCountCommand', 'RecordCountCommand', 'RemoveCountCommand', 'PostPhysicalCountCommand', 'CancelPhysicalCountCommand'];

/** Servidor de tomas en memoria con estado (la planilla cambia con cada comando). */
function fakeServer(web: MockWeb, initial: Sheet | null, handlers: Partial<Record<RpcOperationName, Handler>> = {}, warehouses: string[] = ['CM-PRINCIPAL']) {
  let sheet = initial;
  const real = web.backend.rpc.call.bind(web.backend.rpc);
  const spy = vi.spyOn(web.backend.rpc, 'call').mockImplementation(async (operation, payload, options) => {
    const reply = (result: unknown) => ({ result: result as never, replayed: false, requestId: options?.requestId ?? 'prueba' });
    const custom = handlers[operation];
    if (custom) return reply(await custom(payload));
    const body = payload as Record<string, unknown>;
    switch (operation) {
      case 'GetWorkspaceQuery':
        return reply({ warehouseCode: 'CM-PRINCIPAL', warehouseName: 'Almacén principal', today: '2026-09-29' });
      case 'GetBranchesQuery':
        return reply([{ id: CM, code: 'CM', name: 'Casa matriz', isActive: true, isVisible: true, stockValue: null, transfersIn: 0, transfersOut: 0, users: 3, warehouses }]);
      case 'GetOpenPhysicalCountQuery':
        return reply(sheet && (!body.warehouseCode || body.warehouseCode === sheet.warehouseCode) ? sheet : null);
      case 'GetProductLookupQuery':
        return reply(PRODUCTS);
      case 'GetBinsQuery':
        return reply(['CM-A1', 'CM-A2', 'CM-B1'].map((code) => ({ code, zone: `Zona ${code.slice(3, 4)}`, warehouseCode: 'CM-PRINCIPAL' })));
      case 'GetProductCardQuery':
        return reply({
          sku: body.skuOrBarcode,
          primaryBin: 'CM-A1',
          bins: [
            { binCode: 'CM-A1', lotNumber: 'SIN-LOTE', onHand: 5, available: 5, reserved: 0 },
            { binCode: 'CM-A2', lotNumber: 'SIN-LOTE', onHand: 1, available: 1, reserved: 0 },
          ],
        });
      case 'OpenPhysicalCountCommand':
        sheet = { ...openSheet(), notes: (body.notes as string | null) ?? null };
        return reply({ physicalCountId: 'toma-1', number: 'CF-20260929-1' });
      case 'RecordCountCommand': {
        if (!sheet) throw new WebApiError({ kind: 'not_found', message: 'La toma física no existe.' });
        const product = PRODUCTS.find((entry) => entry.sku === body.sku);
        const counted = Number(body.countedQuantity);
        const others = sheet.lines.filter((entry) => !(entry.sku === body.sku && entry.binCode === body.binCode));
        sheet = { ...sheet, lines: [line(String(body.sku), product?.name ?? '', counted, 5, { binCode: String(body.binCode), countedBy: 'Andrea Quiroga' }), ...others] };
        return reply('linea-1');
      }
      case 'RemoveCountCommand':
        if (sheet) sheet = { ...sheet, lines: sheet.lines.filter((entry) => !(entry.sku === body.sku && entry.binCode === body.binCode)) };
        return reply(true);
      case 'PostPhysicalCountCommand':
        sheet = null;
        return reply({ number: 'CF-20260929-1', movements: 2, surpluses: 1, shortages: 1, initialBalances: 0, matching: 0, message: '✔ CF-20260929-1: 2 movimiento(s) (1 sobrante(s), 1 faltante(s), 0 saldo(s) inicial(es)) · 0 cuadran' });
      case 'CancelPhysicalCountCommand':
        sheet = null;
        return reply('✔ Toma física CF-20260929-1 anulada: no se generaron ajustes.');
      default:
        return real(operation as never, payload as never, options);
    }
  });
  const payloads = (operation: RpcOperationName) => spy.mock.calls.filter(([name]) => name === operation).map(([, payload]) => payload);
  const order = () => spy.mock.calls.map(([name]) => name).filter((name) => COMMANDS.includes(name));
  return { spy, payloads, order };
}

interface OpenOptions {
  handlers?: Partial<Record<RpcOperationName, Handler>>;
  warehouses?: string[];
}

async function openCount(role: StaffRole = 'ADMIN', sheet: Sheet | null = openSheet(LINES), query = '', options: OpenOptions = {}) {
  const web = await signedInAs(role);
  const server = fakeServer(web, sheet, options.handlers, options.warehouses);
  const view = await renderPanel(<PanelApp registry={REGISTRY} />, { web: web.services, route: `/panel/toma-fisica${query}`, path: '/panel/*' });
  return { web, server, location: view.location };
}

async function linesTable(): Promise<HTMLElement> {
  await screen.findByTestId('toma-en-curso', undefined, { timeout: 5000 });
  const found = screen.getByRole('table', { name: 'Conteos de la toma' }).closest('[data-testid="tabla"]') as HTMLElement;
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
    .find((entry) => entry.getAttribute('data-row-key') === key);
  if (!row) throw new Error(`No está la fila ${key}`);
  return row;
}

async function menuOf(table: HTMLElement, key: string): Promise<HTMLElement> {
  fireEvent.click(within(rowOf(table, key)).getByRole('button', { name: /^Acciones de / }));
  return screen.findByTestId('menu-acciones');
}

const MON = 'MON-1|CM-A1|SIN-LOTE';
const TEC = 'TEC-1|CM-B1|SIN-LOTE';

describe('Toma física · iniciar', SLOW, () => {
  it('sin toma abierta: valida la fecha e inicia con el almacén de trabajo, la fecha y la observación (contenido EXACTO)', async () => {
    const { server } = await openCount('ADMIN', null);
    expect(await screen.findByRole('heading', { name: 'No hay una toma física en curso' }, { timeout: 5000 })).toBeInTheDocument();
    expect(server.payloads('GetOpenPhysicalCountQuery')).toEqual([{ warehouseCode: null }]);
    const date = screen.getByLabelText(/^Fecha del conteo/);
    expect(date).toHaveValue(laPazToday());
    fireEvent.change(date, { target: { value: '2999-01-01' } });
    fireEvent.click(screen.getByRole('button', { name: 'Iniciar toma física' }));
    expect(await screen.findByText('La fecha no puede ser futura.')).toBeInTheDocument();
    expect(server.payloads('OpenPhysicalCountCommand')).toHaveLength(0);
    fireEvent.change(date, { target: { value: '2026-09-28' } });
    fireEvent.change(screen.getByLabelText(/^Observación/), { target: { value: '  Conteo de monitores ' } });
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Iniciar toma física' }));
    });
    expect(await screen.findByText('Toma física abierta')).toBeInTheDocument();
    expect(server.payloads('OpenPhysicalCountCommand')).toEqual([{ warehouseCode: 'CM-PRINCIPAL', countDate: '2026-09-28', notes: 'Conteo de monitores' }]);
    expect(await screen.findByTestId('toma-en-curso')).toHaveTextContent('CF-20260929-1');
    expect(screen.getByText('Todavía no hay conteos')).toBeInTheDocument();
  });

  it('con varios almacenes en la sucursal activa se elige cuál contar (`?almacen=`)', async () => {
    const { server, location } = await openCount('ADMIN', null, '', { warehouses: ['CM-PRINCIPAL', 'CM-TIENDA'] });
    const select = await screen.findByRole('combobox', { name: 'Almacén' }, { timeout: 5000 });
    fireEvent.change(select, { target: { value: 'CM-TIENDA' } });
    await waitFor(() => expect(location()).toBe('/panel/toma-fisica?almacen=CM-TIENDA'));
    await waitFor(() => expect(server.payloads('GetOpenPhysicalCountQuery')).toContainEqual({ warehouseCode: 'CM-TIENDA' }));
    await act(async () => {
      fireEvent.click(await screen.findByRole('button', { name: 'Iniciar toma física' }));
    });
    expect(server.payloads('OpenPhysicalCountCommand')).toEqual([{ warehouseCode: 'CM-TIENDA', countDate: laPazToday(), notes: null }]);
  });
});

describe('Toma física · contar', SLOW, () => {
  it('muestra la planilla con las diferencias resaltadas, el progreso y los filtros en la dirección', async () => {
    const { location } = await openCount();
    const table = await linesTable();
    expect(keys(table)).toEqual([TEC, MON]);
    expect(rowOf(table, MON)).toHaveTextContent('+2 UND');
    expect(rowOf(table, TEC)).toHaveTextContent('−2 UND');
    expect(screen.getByTestId('toma-en-curso')).toHaveTextContent('CF-20260929-1 · abierta el 29/09/2026 · almacén CM-PRINCIPAL · Conteo mensual');
    await waitFor(() => expect(screen.getByTestId('progreso-toma')).toHaveTextContent('Contados 2 de 3 productos · 67 %'));
    fireEvent.change(screen.getByRole('combobox', { name: 'Diferencia' }), { target: { value: 'faltante' } });
    await waitFor(() => expect(keys(table)).toEqual([TEC]));
    expect(location()).toBe('/panel/toma-fisica?diferencia=faltante');
    fireEvent.click(screen.getByRole('button', { name: 'Limpiar filtros' }));
    fireEvent.change(screen.getByRole('combobox', { name: 'Categoría' }), { target: { value: 'Monitores' } });
    await waitFor(() => expect(keys(table)).toEqual([MON]));
    await waitFor(() => expect(screen.getByTestId('progreso-toma')).toHaveTextContent('Contados 1 de 2 productos (Categoría: Monitores) · 50 %'));
    // El resumen de diferencias está plegado al entrar.
    expect(screen.queryByTestId('resumen-diferencias')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /Ver resumen de diferencias/ }));
    expect(await screen.findByTestId('resumen-diferencias')).toHaveTextContent(/Sobrantes\s*1/);
  });

  it('el lector de códigos (código + Enter) elige el producto, muestra el sistema y la diferencia, y registra el conteo EXACTO', async () => {
    const { server } = await openCount();
    await linesTable();
    const scanner = screen.getByLabelText('Código de barras o SKU');
    fireEvent.change(scanner, { target: { value: '9999' } });
    fireEvent.keyDown(scanner, { key: 'Enter' });
    expect(await screen.findByText('Código no encontrado')).toBeInTheDocument();
    fireEvent.change(scanner, { target: { value: '8806091234567' } });
    fireEvent.keyDown(scanner, { key: 'Enter' });
    await waitFor(() => expect(screen.getByRole('combobox', { name: /^Producto/ })).toHaveValue('Monitor LG 27GP850'));
    expect(screen.getByRole('combobox', { name: /^Posición/ })).toHaveValue('CM-A1');
    const quantity = screen.getByRole('textbox', { name: /^Cantidad contada/ });
    await waitFor(() => expect(quantity).toHaveFocus());
    const guide = screen.getByTestId('guia-conteo');
    await waitFor(() => expect(guide).toHaveTextContent('5 UND'));
    expect(guide).toHaveTextContent('Ya contado: 7 UND por Bruno Mamani');
    fireEvent.change(quantity, { target: { value: '4' } });
    expect(guide).toHaveTextContent('Faltante: −1 UND');
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Registrar conteo' }));
    });
    expect(await screen.findByText('Conteo registrado')).toBeInTheDocument();
    expect(server.payloads('RecordCountCommand')).toEqual([{ physicalCountId: 'toma-1', sku: 'MON-1', binCode: 'CM-A1', countedQuantity: 4, lotNumber: null }]);
    expect(server.payloads('GetProductCardQuery')).toEqual([{ skuOrBarcode: 'MON-1', take: 1 }]);
    await waitFor(() => expect(screen.getByLabelText('Código de barras o SKU')).toHaveFocus());
    const table = await linesTable();
    await waitFor(() => expect(rowOf(table, MON)).toHaveTextContent('−1 UND'));
  });

  it('valida el conteo en la página y muestra el rechazo del servidor dentro del formulario', async () => {
    const { server } = await openCount('ADMIN', openSheet(LINES), '', {
      handlers: {
        RecordCountCommand: () => {
          throw new WebApiError({ kind: 'domain', status: 422, message: 'La posición CM-A2 no pertenece al almacén de la toma CF-20260929-1.' });
        },
      },
    });
    await linesTable();
    fireEvent.click(screen.getByRole('button', { name: 'Registrar conteo' }));
    expect(await screen.findByText('Escanee o elija el producto.')).toBeInTheDocument();
    expect(server.payloads('RecordCountCommand')).toHaveLength(0);
    const scanner = screen.getByLabelText('Código de barras o SKU');
    fireEvent.change(scanner, { target: { value: 'mon-2' } });
    fireEvent.keyDown(scanner, { key: 'Enter' });
    await waitFor(() => expect(screen.getByRole('combobox', { name: /^Posición/ })).toHaveValue('CM-A2'));
    fireEvent.click(screen.getByRole('button', { name: 'Registrar conteo' }));
    expect(await screen.findByText('Indique la cantidad contada (0 si no hay ninguno).')).toBeInTheDocument();
    fireEvent.change(screen.getByRole('textbox', { name: /^Cantidad contada/ }), { target: { value: '0' } });
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Registrar conteo' }));
    });
    expect(await screen.findByText('La posición CM-A2 no pertenece al almacén de la toma CF-20260929-1.')).toBeInTheDocument();
    expect(server.payloads('RecordCountCommand')).toEqual([{ physicalCountId: 'toma-1', sku: 'MON-2', binCode: 'CM-A2', countedQuantity: 0, lotNumber: null }]);
  });

  it('«Corregir conteo» carga el producto y la posición; «Quitar conteo» pide confirmación (contenido EXACTO)', async () => {
    const { server } = await openCount();
    const table = await linesTable();
    fireEvent.click(within(await menuOf(table, TEC)).getByRole('menuitem', { name: 'Corregir conteo' }));
    await waitFor(() => expect(screen.getByRole('combobox', { name: /^Producto/ })).toHaveValue('Teclado mecánico'));
    expect(screen.getByRole('combobox', { name: /^Posición/ })).toHaveValue('CM-B1');
    fireEvent.click(within(await menuOf(table, MON)).getByRole('menuitem', { name: 'Quitar conteo' }));
    const confirm = await screen.findByRole('alertdialog', { name: '¿Quitar el conteo de MON-1 en CM-A1?' });
    expect(server.payloads('RemoveCountCommand')).toHaveLength(0);
    await act(async () => {
      fireEvent.click(within(confirm).getByRole('button', { name: 'Quitar conteo' }));
    });
    expect(await screen.findByText('Conteo quitado')).toBeInTheDocument();
    expect(server.payloads('RemoveCountCommand')).toEqual([{ physicalCountId: 'toma-1', sku: 'MON-1', binCode: 'CM-A1', lotNumber: null }]);
    await waitFor(() => expect(keys(table)).toEqual([TEC]));
  });

  it('«Pendientes de contar» lista lo que falta y «Contar» lo carga en el formulario', async () => {
    const { location } = await openCount();
    await linesTable();
    fireEvent.click(screen.getByRole('tab', { name: /Pendientes de contar/ }));
    await waitFor(() => expect(location()).toBe('/panel/toma-fisica?vista=pendientes'));
    const pending = (await screen.findByRole('table', { name: 'Productos pendientes de contar' })).closest('[data-testid="tabla"]') as HTMLElement;
    expect(keys(pending)).toEqual(['MON-2']);
    fireEvent.click(within(await menuOf(pending, 'MON-2')).getByRole('menuitem', { name: 'Contar' }));
    await waitFor(() => expect(screen.getByRole('combobox', { name: /^Producto/ })).toHaveValue('Monitor AOC 24G2'));
    expect(screen.getByRole('combobox', { name: /^Posición/ })).toHaveValue('CM-A2');
  });
});

describe('Toma física · generar ajustes, anular, imprimir y exportar', SLOW, () => {
  it('«Generar ajustes» confirma con el resumen de diferencias y envía la confirmación EXPLÍCITA; el resultado queda a la vista', async () => {
    const { server } = await openCount();
    await linesTable();
    fireEvent.click(screen.getByRole('button', { name: 'Generar ajustes' }));
    const dialog = await screen.findByRole('alertdialog', { name: '¿Generar los ajustes de la toma CF-20260929-1?' });
    const summary = within(dialog).getByTestId('resumen-ajustes');
    expect(summary).toHaveTextContent(/Conteos registrados\s*2 \(2 productos\)/);
    expect(summary).toHaveTextContent(/Sobrantes → AJUSTE \(\+\)\s*1/);
    expect(summary).toHaveTextContent(/Faltantes → AJUSTE \(−\)\s*1/);
    expect(summary).toHaveTextContent('Teclado mecánico');
    expect(server.payloads('PostPhysicalCountCommand')).toHaveLength(0);
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Generar ajustes' }));
    });
    expect(await screen.findByText('Toma física contabilizada')).toBeInTheDocument();
    expect(server.payloads('PostPhysicalCountCommand')).toEqual([{ physicalCountId: 'toma-1', confirmed: true }]);
    expect(await screen.findByTestId('resultado-toma')).toHaveTextContent('CF-20260929-1: 2 movimiento(s)');
    expect(await screen.findByRole('heading', { name: 'No hay una toma física en curso' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Ver los movimientos' })).toHaveAttribute('href', '/panel/movimientos');
  });

  it('si el servidor rechaza los ajustes (un producto con serie con diferencia), el error queda en la confirmación', async () => {
    await openCount('ADMIN', openSheet(LINES), '', {
      handlers: {
        PostPhysicalCountCommand: () => {
          throw new WebApiError({ kind: 'domain', status: 422, message: 'MON-1 lleva(n) serie: registre la diferencia con un ajuste que indique las series (Registrar movimiento).' });
        },
      },
    });
    await linesTable();
    fireEvent.click(screen.getByRole('button', { name: 'Generar ajustes' }));
    const dialog = await screen.findByRole('alertdialog', { name: /Generar los ajustes/ });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Generar ajustes' }));
    });
    expect(await within(dialog).findByText(/MON-1 lleva\(n\) serie/)).toBeInTheDocument();
    expect(screen.getByTestId('toma-en-curso')).toBeInTheDocument();
  });

  it('«Anular toma» pide confirmación y envía `CancelPhysicalCountCommand`', async () => {
    const { server } = await openCount();
    await linesTable();
    fireEvent.click(screen.getByRole('button', { name: 'Anular toma' }));
    const confirm = await screen.findByRole('alertdialog', { name: '¿Anular la toma CF-20260929-1?' });
    expect(confirm).toHaveTextContent('Se descartan los 2 conteos registrados');
    await act(async () => {
      fireEvent.click(within(confirm).getByRole('button', { name: 'Anular toma' }));
    });
    expect(await screen.findByText('Toma física anulada')).toBeInTheDocument();
    expect(server.payloads('CancelPhysicalCountCommand')).toEqual([{ physicalCountId: 'toma-1' }]);
    expect(await screen.findByRole('heading', { name: 'No hay una toma física en curso' })).toBeInTheDocument();
  });

  it('imprime la planilla de conteo del alcance elegido (vista imprimible del navegador)', async () => {
    const print = vi.spyOn(window, 'print').mockImplementation(() => undefined);
    await openCount('ADMIN', openSheet(LINES), '?categoria=Monitores');
    await linesTable();
    fireEvent.click(screen.getByRole('button', { name: 'Imprimir planilla' }));
    await waitFor(() => expect(print).toHaveBeenCalledTimes(1));
    expect(document.documentElement).toHaveAttribute('data-print', 'planilla-conteo');
    const sheet = screen.getByTestId('planilla-imprimible');
    expect(sheet).toHaveTextContent('Planilla de conteo · CF-20260929-1');
    expect(sheet).toHaveTextContent('Categoría: Monitores');
    expect(within(sheet).getAllByRole('row', { hidden: true })).toHaveLength(3);
    window.dispatchEvent(new Event('afterprint'));
    expect(document.documentElement).not.toHaveAttribute('data-print');
  });

  it('exporta los conteos filtrados a CSV', async () => {
    const created: Blob[] = [];
    vi.spyOn(URL, 'createObjectURL').mockImplementation((blob) => {
      created.push(blob as Blob);
      return 'blob:prueba';
    });
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    await openCount('ADMIN', openSheet(LINES), '?diferencia=faltante');
    const table = await linesTable();
    await waitFor(() => expect(keys(table)).toEqual([TEC]));
    fireEvent.click(screen.getByRole('button', { name: 'Exportar CSV' }));
    expect(await screen.findByText('Exportación lista')).toBeInTheDocument();
    const lines = (await created[0].text()).replace(/^﻿/, '').trim().split('\r\n');
    expect(lines[0]).toBe('"SKU";"Producto";"Categoría";"Posición";"Lote";"Unidad";"Sistema";"Contado";"Diferencia";"Resultado";"Contó";"Hora del conteo"');
    expect(lines.slice(1)).toEqual(['"TEC-1";"Teclado mecánico";"Periféricos";"CM-B1";"SIN-LOTE";"UND";4;2;-2;"Faltante";"Andrea Quiroga";"29/09/2026 11:00"']);
  });

  it('si la consulta de la toma falla muestra «Reintentar» y se recupera', async () => {
    let fail = true;
    await openCount('ADMIN', openSheet(LINES), '', {
      handlers: {
        GetOpenPhysicalCountQuery: () => {
          if (fail) {
            fail = false;
            throw new WebApiError({ kind: 'network', message: 'Sin conexión' });
          }
          return openSheet(LINES);
        },
      },
    });
    const error = await screen.findByTestId('estado-error', undefined, { timeout: 5000 });
    fireEvent.click(within(error).getByRole('button', { name: 'Reintentar' }));
    await linesTable();
  });
});

describe('Toma física · permisos y definición', SLOW, () => {
  it('ventas y consulta no entran (no registran conteos)', async () => {
    await openCount('VENTAS');
    expect(await screen.findByTestId('sin-acceso', undefined, { timeout: 5000 })).toHaveTextContent(permissionName('inventory.counts.record'));
  });

  it('bodega cuenta, genera ajustes y anula', async () => {
    await openCount('BODEGA');
    await linesTable();
    expect(screen.getByRole('button', { name: 'Generar ajustes' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Anular toma' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Registrar conteo' })).toBeInTheDocument();
  });

  it('la definición del módulo: sección, permisos y la acción del tablero', () => {
    expect(tomaFisica).toMatchObject({ key: 'toma-fisica', section: 'inventario', order: 40, permissions: { all: ['inventory.counts.record', 'inventory.stock.view'] } });
    const offered = (permissions: string[]) => dashboardActions(REGISTRY, permissions).flatMap((group) => group.actions.map((entry) => `${entry.action.label} → ${entry.to}`));
    expect(offered(['inventory.counts.record', 'inventory.stock.view'])).toEqual(['Iniciar toma física → /panel/toma-fisica']);
    expect(offered(['inventory.stock.view'])).toEqual([]);
  });
});
