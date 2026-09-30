// Módulo «Garantías» dentro del panel (con un registro PROPIO de la prueba: solo este módulo, así no depende de los demás
// módulos en construcción) y con un servidor de casos SIMULADO en la prueba: la lista (los abiertos al entrar) con el
// pedido exacto, los filtros del servidor y de la página en la dirección, el detalle con SOLO los pasos que manda el
// servidor, cada comando con su validación y la forma exacta del contrato (abrir, avanzar, reemplazar, nota), los errores
// del servidor dentro del diálogo, la orden de servicio imprimible, el resumen plegado y lo que ve cada rol. Ninguna
// prueba toca la red.

import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { WebApiError } from '@/1-domain/auth/errors';
import type { RpcOperationName, RpcResponseOf } from '@/4-presentation/app/contract';
import { ROLE_PERMISSIONS, renderPanel, signedInAs, type MockWeb, type StaffRole } from '@/test-utils';
import { buildRegistry, dashboardActions } from '../../registry';
import { PanelApp } from '../../shell/PanelApp';
import garantias from './module';

const REGISTRY = buildRegistry([{ source: '../modules/garantias/module.tsx', definition: garantias }]);

beforeAll(async () => {
  await import('./ClaimsPage');
}, 30_000);

afterEach(() => {
  vi.restoreAllMocks();
});

type Claim = RpcResponseOf<'GetWarrantyClaimsQuery'>[number];
type Status = Claim['status'];
type Warranty = RpcResponseOf<'GetWarrantyStatusQuery'>;

function claim(overrides: Partial<Claim>): Claim {
  return {
    id: '00000000-0000-0000-0000-000000000001',
    number: 'RMA-CM-000001',
    branchCode: 'CM',
    serial: 'SN-C300',
    sku: 'NB-ASUS-01',
    product: 'Notebook ASUS TUF',
    customer: 'Luis Arce',
    issue: 'No enciende',
    status: 'Received',
    isInWarranty: true,
    warrantyUntil: '2099-01-10',
    receivedAt: '2026-09-20T14:00:00Z',
    closedAt: null,
    supplier: null,
    resolution: null,
    replacementSerial: null,
    daysOpen: 9,
    ...overrides,
  };
}

const CLAIMS: Claim[] = [
  claim({ number: 'RMA-CM-000003', status: 'Diagnosing', receivedAt: '2026-09-24T14:00:00Z', daysOpen: 5 }),
  claim({
    number: 'RMA-CB-000002',
    branchCode: 'CB',
    serial: '490154203237518',
    sku: 'CEL-GALAXY',
    product: 'Celular Galaxy A55',
    customer: 'Mariana Céspedes',
    issue: 'No carga la batería',
    status: 'SentToSupplier',
    isInWarranty: false,
    warrantyUntil: '2000-01-01',
    receivedAt: '2026-09-09T14:00:00Z',
    supplier: 'Distribuidora Andina S.R.L.',
    daysOpen: 20,
  }),
  claim({
    number: 'RMA-CM-000001',
    serial: 'SN-B200',
    status: 'Delivered',
    receivedAt: '2026-08-01T14:00:00Z',
    closedAt: '2026-08-11T14:00:00Z',
    daysOpen: 10,
    resolution: 'Reemplazo por falla de fábrica',
    replacementSerial: 'SN-A999',
  }),
];

/** La tabla de transiciones del servidor (la página no la conoce: solo muestra `nextStatuses`). */
const NEXT: Record<Status, Status[]> = {
  Received: ['Diagnosing', 'Rejected'],
  Diagnosing: ['SentToSupplier', 'Repaired', 'Replaced', 'Rejected'],
  SentToSupplier: ['Repaired', 'Replaced', 'Rejected'],
  Repaired: ['Delivered'],
  Replaced: ['Delivered'],
  Rejected: ['Delivered'],
  Delivered: [],
};

function warranty(overrides: Partial<Warranty>): Warranty {
  return {
    serial: 'SN-C300',
    sku: 'NB-ASUS-01',
    product: 'Notebook ASUS TUF',
    status: 'Sold',
    soldOn: '2026-01-10',
    invoiceNumber: 'F-CM-000010',
    customerCode: 'C0003',
    customer: 'Luis Arce',
    warrantyMonths: 12,
    warrantyUntil: '2099-01-10',
    inWarranty: true,
    openClaim: null,
    ...overrides,
  };
}

/** Lo que responde «consultar la serie» al abrir un caso. */
const LOOKUPS: Record<string, Warranty> = {
  'SN-NEW1': warranty({ serial: 'SN-NEW1' }),
  'SN-OLD': warranty({ serial: 'SN-OLD', warrantyUntil: '2020-01-10', inWarranty: false }),
  'SN-OTRA': warranty({ serial: 'SN-OTRA', customerCode: null, customer: null, invoiceNumber: 'F-SC-000099' }),
};

function detailOf(row: Claim): RpcResponseOf<'GetWarrantyClaimQuery'> {
  return {
    claim: row,
    warranty: warranty({ serial: row.serial, sku: row.sku, product: row.product, status: 'InRma', customer: row.customer, inWarranty: row.isInWarranty, warrantyUntil: row.warrantyUntil }),
    events: [
      { occurredAt: row.receivedAt, action: 'Opened', status: 'Received', note: row.isInWarranty ? 'Recibido en garantía' : 'Recibido FUERA de garantía (servicio con cargo)', user: 'Carla Rojas' },
      ...(row.status !== 'Received' ? [{ occurredAt: '2026-09-25T14:00:00Z', action: 'StatusChanged' as const, status: row.status, note: null, user: 'Bruno Mamani' }] : []),
    ],
    invoiceNumber: 'F-CM-000010',
    nextStatuses: NEXT[row.status],
  };
}

const CUSTOMERS: RpcResponseOf<'GetCustomersQuery'> = {
  categories: [],
  customers: [
    { code: 'C0005', name: 'Ana Rojas', taxId: '7654321', email: null, phone: null, categoryCode: 'GENERAL', category: 'General', isActive: true, purchases: 1, total: 100, lastPurchase: null },
    { code: 'C0009', name: 'Inactivo', taxId: null, email: null, phone: null, categoryCode: 'GENERAL', category: 'General', isActive: false, purchases: 0, total: 0, lastPurchase: null },
  ],
};

type Handler = (payload: never) => unknown;

/** Servidor de garantías en memoria: responde las operaciones del módulo y deja el resto al modo mock. */
function fakeServer(web: MockWeb, handlers: Partial<Record<RpcOperationName, Handler>> = {}) {
  const real = web.backend.rpc.call.bind(web.backend.rpc);
  const spy = vi.spyOn(web.backend.rpc, 'call').mockImplementation(async (operation, payload, sendOptions) => {
    const reply = (result: unknown) => ({ result: result as never, replayed: false, requestId: sendOptions?.requestId ?? 'prueba' });
    const custom = handlers[operation];
    if (custom) return reply(await custom(payload as never));
    const byNumber = (number: string) => CLAIMS.find((row) => row.number === number);
    switch (operation) {
      case 'GetWarrantyClaimsQuery': {
        const request = payload as { status: Status | null; onlyOpen: boolean };
        return reply(CLAIMS.filter((row) => (!request.status || row.status === request.status) && (!request.onlyOpen || row.status !== 'Delivered')));
      }
      case 'GetWarrantyClaimQuery': {
        const row = byNumber((payload as { number: string }).number);
        if (!row) throw new WebApiError({ kind: 'not_found', status: 404, message: 'El caso no existe o es de otra sucursal.' });
        return reply(detailOf(row));
      }
      case 'GetWarrantyStatusQuery': {
        const found = LOOKUPS[(payload as { serial: string }).serial];
        if (!found) throw new WebApiError({ kind: 'not_found', status: 404, message: 'La serie no está registrada.' });
        return reply(found);
      }
      case 'GetCustomersQuery':
        return reply(CUSTOMERS);
      case 'GetCatalogOptionsQuery':
        return reply({ categories: [], units: [], suppliers: [{ code: 'PROV-01', name: 'Distribuidora Andina S.R.L.' }], taxRate: 13, priceListName: 'General', vatOnInvoicedAmount: true });
      case 'GetAvailableSerialsQuery':
        return reply(
          ['SN-C300', 'SN-R1', 'SN-R2'].map((serial) => ({
            serial,
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
          })),
        );
      case 'OpenWarrantyClaimCommand': {
        const request = payload as { serial: string; chargeableRepair: boolean };
        return reply(claim({ number: 'RMA-CM-000004', serial: request.serial, isInWarranty: !request.chargeableRepair, daysOpen: 0 }));
      }
      case 'MoveWarrantyClaimCommand': {
        const request = payload as { number: string; next: Status };
        return reply({ ...byNumber(request.number)!, status: request.next });
      }
      case 'IssueWarrantyReplacementCommand': {
        const request = payload as { number: string; replacementSerial: string };
        return reply({ ...byNumber(request.number)!, status: 'Replaced', replacementSerial: request.replacementSerial });
      }
      case 'AddWarrantyClaimNoteCommand':
        return reply(byNumber((payload as { number: string }).number));
      default:
        return real(operation as never, payload as never, sendOptions);
    }
  });
  const payloads = (operation: RpcOperationName) => spy.mock.calls.filter(([name]) => name === operation).map(([, payload]) => payload);
  return { spy, payloads };
}

async function openClaims(role: StaffRole = 'ADMIN', query = '', handlers: Partial<Record<RpcOperationName, Handler>> = {}) {
  const web = await signedInAs(role);
  const server = fakeServer(web, handlers);
  const view = await renderPanel(<PanelApp registry={REGISTRY} />, { web: web.services, route: `/panel/garantias${query}`, path: '/panel/*' });
  return { web, server, location: view.location };
}

async function claimsTable(): Promise<HTMLElement> {
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

async function openDetail(table: HTMLElement, key: string): Promise<HTMLElement> {
  fireEvent.click(within(rowOf(table, key)).getAllByRole('button')[0]);
  const panel = await screen.findByRole('dialog', { name: `Caso ${key}` });
  await within(panel).findByRole('tab', { name: /^Resumen/ });
  return panel;
}

describe('Garantías · lista', () => {
  it('al entrar muestra los casos ABIERTOS (pedido exacto) y el resumen plegado no consulta hasta abrirse', async () => {
    const { server } = await openClaims();
    expect(await screen.findByRole('heading', { level: 1, name: 'Garantías' }, { timeout: 5000 })).toBeInTheDocument();
    const table = await claimsTable();
    expect(keys(table)).toEqual(['RMA-CM-000003', 'RMA-CB-000002']);
    expect(server.payloads('GetWarrantyClaimsQuery')).toEqual([{ status: null, onlyOpen: true }]);
    expect(screen.getByTestId('casos-resumen')).toHaveTextContent('2 de 2 casos abiertos');
    expect(rowOf(table, 'RMA-CB-000002')).toHaveTextContent('Con cargo');
    expect(rowOf(table, 'RMA-CB-000002')).toHaveTextContent('En el proveedor');
    expect(rowOf(table, 'RMA-CM-000003')).toHaveTextContent('5 días abierto');

    const toggle = screen.getByRole('button', { name: /Ver resumen de los casos/ });
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    fireEvent.click(toggle);
    const summary = await screen.findByTestId('resumen-de-casos');
    await waitFor(() => expect(summary).toHaveTextContent('Casos abiertos2El más antiguo lleva 20 días'));
    expect(summary).toHaveTextContent('Entregados11 con reemplazo');
    expect(server.payloads('GetWarrantyClaimsQuery')).toEqual([
      { status: null, onlyOpen: true },
      { status: null, onlyOpen: false },
    ]);
  });

  it('el estado se pide al servidor (todos o uno) y queda en la dirección', async () => {
    const { server, location } = await openClaims();
    const table = await claimsTable();
    const state = screen.getByRole('combobox', { name: 'Estado' });
    fireEvent.change(state, { target: { value: '' } });
    await waitFor(() => expect(keys(table)).toEqual(['RMA-CM-000003', 'RMA-CB-000002', 'RMA-CM-000001']));
    expect(location()).toBe('/panel/garantias?estado=');
    expect(server.payloads('GetWarrantyClaimsQuery').at(-1)).toEqual({ status: null, onlyOpen: false });
    fireEvent.change(state, { target: { value: 'Delivered' } });
    await waitFor(() => expect(keys(table)).toEqual(['RMA-CM-000001']));
    expect(server.payloads('GetWarrantyClaimsQuery').at(-1)).toEqual({ status: 'Delivered', onlyOpen: false });
    fireEvent.click(screen.getByRole('button', { name: 'Limpiar filtros' }));
    await waitFor(() => expect(location()).toBe('/panel/garantias'));
    await waitFor(() => expect(keys(table)).toEqual(['RMA-CM-000003', 'RMA-CB-000002']));
  });

  it('búsqueda, sucursal, cobertura y tiempo en el taller se filtran en la página', async () => {
    const { server, location } = await openClaims();
    const table = await claimsTable();
    fireEvent.change(screen.getByRole('combobox', { name: 'Cobertura' }), { target: { value: 'cargo' } });
    await waitFor(() => expect(keys(table)).toEqual(['RMA-CB-000002']));
    fireEvent.change(screen.getByRole('combobox', { name: 'Cobertura' }), { target: { value: '' } });
    fireEvent.change(screen.getByRole('combobox', { name: 'Sucursal' }), { target: { value: 'CM' } });
    await waitFor(() => expect(keys(table)).toEqual(['RMA-CM-000003']));
    fireEvent.change(screen.getByRole('combobox', { name: 'Sucursal' }), { target: { value: '' } });
    fireEvent.change(screen.getByRole('combobox', { name: 'Tiempo en el taller' }), { target: { value: '15' } });
    await waitFor(() => expect(keys(table)).toEqual(['RMA-CB-000002']));
    const search = screen.getByRole('searchbox', { name: 'Buscar' });
    fireEvent.change(search, { target: { value: 'cespedes' } });
    fireEvent.keyDown(search, { key: 'Enter' });
    await waitFor(() => expect(location()).toBe('/panel/garantias?dias=15&q=cespedes'));
    expect(keys(table)).toEqual(['RMA-CB-000002']);
    expect(server.payloads('GetWarrantyClaimsQuery')).toHaveLength(1);
  });

  it('si la consulta falla muestra el error con «Reintentar» y se recupera', async () => {
    let fail = true;
    await openClaims('ADMIN', '', {
      GetWarrantyClaimsQuery: () => {
        if (fail) {
          fail = false;
          throw new WebApiError({ kind: 'network', message: 'Sin conexión' });
        }
        return CLAIMS;
      },
    });
    const error = await screen.findByTestId('estado-error', undefined, { timeout: 5000 });
    fireEvent.click(within(error).getByRole('button', { name: 'Reintentar' }));
    await claimsTable();
  });
});

describe('Garantías · detalle y pasos', () => {
  it('el detalle ofrece SOLO los pasos que manda el servidor, con la bitácora y el enlace a la serie', async () => {
    const { server } = await openClaims();
    const table = await claimsTable();
    const panel = await openDetail(table, 'RMA-CM-000003');
    expect(server.payloads('GetWarrantyClaimQuery')).toEqual([{ number: 'RMA-CM-000003' }]);
    const buttons = within(panel)
      .getAllByRole('button')
      .map((button) => button.textContent);
    expect(buttons).toEqual(expect.arrayContaining(['Enviar al proveedor', 'Marcar reparado', 'Reemplazar con otra unidad', 'Rechazar la garantía', 'Orden de servicio', 'Agregar nota']));
    expect(within(panel).queryByRole('button', { name: 'Entregar al cliente' })).not.toBeInTheDocument();
    expect(panel).toHaveTextContent('En garantía hasta el 10/01/2099');
    expect(within(panel).getByRole('link', { name: 'Ver la serie' })).toHaveAttribute('href', '/panel/series?q=SN-C300&producto=NB-ASUS-01&ver=SN-C300');
    expect(within(panel).getByRole('link', { name: 'Ver las ventas del cliente' })).toHaveAttribute('href', '/panel/ventas?cliente=C0003');
    fireEvent.click(within(panel).getByRole('tab', { name: /^Bitácora/ }));
    const log = within(panel).getByRole('list', { name: 'Bitácora del caso' });
    expect(within(log).getAllByRole('listitem').map((item) => item.querySelector('span')?.textContent)).toEqual(['Cambio de estado · en diagnóstico', 'Caso abierto']);
  });

  it('enviar al proveedor: se confirma, se elige el proveedor y se envía el comando exacto; recarga lista y detalle', async () => {
    const { server } = await openClaims('BODEGA');
    const table = await claimsTable();
    const panel = await openDetail(table, 'RMA-CM-000003');
    fireEvent.click(within(panel).getByRole('button', { name: 'Enviar al proveedor' }));
    const dialog = await screen.findByRole('alertdialog', { name: '¿Enviar el equipo del caso RMA-CM-000003 al proveedor?' });
    expect(dialog).toHaveTextContent('no se puede deshacer');
    const supplier = within(dialog).getByRole('combobox', { name: 'Proveedor' });
    await waitFor(() => expect(within(supplier).getByRole('option', { name: 'Distribuidora Andina S.R.L.' })).toBeInTheDocument());
    expect(server.payloads('GetCatalogOptionsQuery')).toEqual([{}]);
    fireEvent.change(supplier, { target: { value: 'PROV-01' } });
    fireEvent.change(within(dialog).getByRole('textbox', { name: /^Nota/ }), { target: { value: 'Guía 123' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Enviar al proveedor' }));
    });
    await waitFor(() =>
      expect(server.payloads('MoveWarrantyClaimCommand')).toEqual([{ number: 'RMA-CM-000003', next: 'SentToSupplier', resolution: null, supplierCode: 'PROV-01', note: 'Guía 123' }]),
    );
    expect(await screen.findByText('Caso RMA-CM-000003: en el proveedor')).toBeInTheDocument();
    await waitFor(() => expect(server.payloads('GetWarrantyClaimsQuery')).toHaveLength(2));
    await waitFor(() => expect(server.payloads('GetWarrantyClaimQuery')).toHaveLength(2));
  });

  it('marcar reparado exige la resolución (sugerida u otra) y la envía', async () => {
    const { server } = await openClaims();
    const table = await claimsTable();
    const panel = await openDetail(table, 'RMA-CM-000003');
    fireEvent.click(within(panel).getByRole('button', { name: 'Marcar reparado' }));
    const dialog = await screen.findByRole('alertdialog', { name: '¿Marcar el caso RMA-CM-000003 como reparado?' });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Marcar reparado' }));
    expect(await within(dialog).findByText('Elija la resolución o escriba una.')).toBeInTheDocument();
    expect(server.payloads('MoveWarrantyClaimCommand')).toEqual([]);
    fireEvent.change(within(dialog).getByRole('combobox', { name: 'Resolución' }), { target: { value: 'otro' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Marcar reparado' }));
    expect(await within(dialog).findByText('Escriba la resolución.')).toBeInTheDocument();
    fireEvent.change(within(dialog).getByRole('textbox', { name: 'Escriba la resolución' }), { target: { value: 'Cambio del puerto de carga' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Marcar reparado' }));
    });
    await waitFor(() =>
      expect(server.payloads('MoveWarrantyClaimCommand')).toEqual([{ number: 'RMA-CM-000003', next: 'Repaired', resolution: 'Cambio del puerto de carga', supplierCode: null, note: null }]),
    );
  });

  it('el error del servidor al avanzar se muestra dentro de la confirmación, sin cerrarla', async () => {
    await openClaims('ADMIN', '', {
      MoveWarrantyClaimCommand: () => {
        throw new WebApiError({ kind: 'domain', status: 422, message: 'Un caso en diagnóstico no puede pasar a entregado.' });
      },
    });
    const table = await claimsTable();
    const panel = await openDetail(table, 'RMA-CM-000003');
    fireEvent.click(within(panel).getByRole('button', { name: 'Rechazar la garantía' }));
    const dialog = await screen.findByRole('alertdialog', { name: '¿Rechazar la garantía del caso RMA-CM-000003?' });
    fireEvent.change(within(dialog).getByRole('combobox', { name: 'Resolución' }), { target: { value: 'Sello de garantía roto' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Rechazar la garantía' }));
    });
    expect(await within(dialog).findByTestId('error-del-servidor')).toHaveTextContent('no puede pasar a entregado');
    expect(screen.getByRole('alertdialog')).toBeInTheDocument();
  });

  it('reemplazar: se elige una unidad disponible (nunca la del caso) y se entrega con el comando exacto', async () => {
    const { server } = await openClaims('GERENCIA');
    const table = await claimsTable();
    const panel = await openDetail(table, 'RMA-CM-000003');
    fireEvent.click(within(panel).getByRole('button', { name: 'Reemplazar con otra unidad' }));
    const dialog = await screen.findByRole('alertdialog', { name: '¿Entregar una unidad de reemplazo para el caso RMA-CM-000003?' });
    expect(dialog).toHaveTextContent('No se puede deshacer.');
    const unit = await within(dialog).findByRole('combobox', { name: 'Unidad de reemplazo' });
    expect(server.payloads('GetAvailableSerialsQuery')).toEqual([{ sku: 'NB-ASUS-01', warehouseCode: null }]);
    fireEvent.click(within(dialog).getByRole('button', { name: 'Entregar reemplazo' }));
    expect(await within(dialog).findByText('Elija la unidad de reemplazo.')).toBeInTheDocument();
    fireEvent.change(unit, { target: { value: 'SN-' } });
    const options = await within(dialog).findAllByRole('option');
    expect(options.map((option) => option.textContent?.split('CM')[0])).toEqual(['SN-R1', 'SN-R2']);
    fireEvent.click(within(dialog).getByRole('option', { name: /SN-R2/ }));
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Entregar reemplazo' }));
    });
    await waitFor(() =>
      expect(server.payloads('IssueWarrantyReplacementCommand')).toEqual([{ number: 'RMA-CM-000003', replacementSerial: 'SN-R2', resolution: 'Reemplazo por falla de fábrica' }]),
    );
    expect(await screen.findByText('Caso RMA-CM-000003: reemplazado')).toBeInTheDocument();
    expect(server.payloads('MoveWarrantyClaimCommand')).toEqual([]);
  });

  it('ventas agrega notas pero no ve los pasos del caso; la nota se envía exacta', async () => {
    const { server } = await openClaims('VENTAS');
    const table = await claimsTable();
    const panel = await openDetail(table, 'RMA-CB-000002');
    expect(within(panel).queryByRole('button', { name: 'Marcar reparado' })).not.toBeInTheDocument();
    expect(within(panel).queryByRole('button', { name: 'Rechazar la garantía' })).not.toBeInTheDocument();
    fireEvent.click(within(panel).getByRole('button', { name: 'Agregar nota' }));
    const dialog = await screen.findByRole('dialog', { name: 'Nota en el caso RMA-CB-000002' });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Agregar nota' }));
    expect(await within(dialog).findByText('Escriba la nota.')).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Se contactó al cliente' }));
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Agregar nota' }));
    });
    await waitFor(() => expect(server.payloads('AddWarrantyClaimNoteCommand')).toEqual([{ number: 'RMA-CB-000002', note: 'Se contactó al cliente' }]));
    expect(await screen.findByText('Nota agregada al caso RMA-CB-000002')).toBeInTheDocument();
  });

  it('`?ver=<número>` (desde Series) abre el detalle del caso; al cerrarlo se quita de la dirección', async () => {
    const { server, location } = await openClaims('ADMIN', '?ver=RMA-CB-000002');
    const panel = await screen.findByRole('dialog', { name: 'Caso RMA-CB-000002' }, { timeout: 5000 });
    await within(panel).findByRole('tab', { name: /^Resumen/ });
    expect(server.payloads('GetWarrantyClaimQuery')).toEqual([{ number: 'RMA-CB-000002' }]);
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(screen.getByRole('dialog', { name: 'Caso RMA-CB-000002' })).toBeInTheDocument();
    expect(location()).toBe('/panel/garantias?ver=RMA-CB-000002');
    fireEvent.click(within(panel).getByRole('button', { name: 'Cerrar' }));
    await waitFor(() => expect(location()).toBe('/panel/garantias'));
  });

  it('un caso entregado no ofrece pasos ni notas', async () => {
    await openClaims('ADMIN', '?estado=Delivered');
    const table = await claimsTable();
    const panel = await openDetail(table, 'RMA-CM-000001');
    expect(within(panel).queryByRole('button', { name: 'Entregar al cliente' })).not.toBeInTheDocument();
    expect(within(panel).queryByRole('button', { name: 'Agregar nota' })).not.toBeInTheDocument();
    expect(within(panel).getByRole('button', { name: 'Orden de servicio' })).toBeInTheDocument();
  });
});

describe('Garantías · abrir un caso', () => {
  it('desde Series (`?abrir=1&serie=&sku=`): consulta la serie; fuera de garantía exige «Reparación con cargo»', async () => {
    const { server, location } = await openClaims('VENTAS', '?abrir=1&serie=SN-OLD&sku=NB-ASUS-01');
    const dialog = await screen.findByRole('dialog', { name: 'Abrir un caso de garantía' }, { timeout: 5000 });
    await waitFor(() => expect(location()).toBe('/panel/garantias'));
    const unit = await within(dialog).findByTestId('unidad-del-caso');
    expect(unit).toHaveTextContent('Garantía vencida el 10/01/2020');
    expect(server.payloads('GetWarrantyStatusQuery')).toEqual([{ serial: 'SN-OLD', sku: 'NB-ASUS-01' }]);

    fireEvent.click(within(dialog).getByRole('button', { name: 'Abrir caso' }));
    expect(await within(dialog).findByText('Describa la falla reportada (al menos 5 caracteres).')).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole('button', { name: 'No enciende' }));
    fireEvent.click(within(dialog).getByRole('button', { name: 'Abrir caso' }));
    expect(await within(dialog).findByText('La unidad está fuera de garantía: marque «Reparación con cargo» para abrir el caso.')).toBeInTheDocument();
    expect(server.payloads('OpenWarrantyClaimCommand')).toEqual([]);

    fireEvent.click(within(dialog).getByRole('checkbox', { name: /Reparación con cargo/ }));
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Abrir caso' }));
    });
    await waitFor(() =>
      expect(server.payloads('OpenWarrantyClaimCommand')).toEqual([{ serial: 'SN-OLD', issue: 'No enciende', chargeableRepair: true, customerCode: null, sku: 'NB-ASUS-01' }]),
    );
    expect(await screen.findByText('Caso RMA-CM-000004 abierto')).toBeInTheDocument();
    // El caso nuevo se abre en el detalle.
    await waitFor(() => expect(server.payloads('GetWarrantyClaimQuery')).toEqual([{ number: 'RMA-CM-000004' }]));
  });

  it('con la venta en otra sucursal pide el cliente (de la cartera) antes de abrir', async () => {
    const { server } = await openClaims();
    await claimsTable();
    fireEvent.click(screen.getByRole('button', { name: 'Abrir un caso' }));
    const dialog = await screen.findByRole('dialog', { name: 'Abrir un caso de garantía' });
    const serial = within(dialog).getByRole('textbox', { name: /^Serie o IMEI del equipo/ });
    fireEvent.change(serial, { target: { value: 'SN-OTRA' } });
    fireEvent.keyDown(serial, { key: 'Enter' });
    expect(await within(dialog).findByTestId('unidad-del-caso')).toHaveTextContent('En garantía hasta el 10/01/2099');
    expect(server.payloads('GetWarrantyStatusQuery')).toEqual([{ serial: 'SN-OTRA', sku: null }]);
    const customer = await within(dialog).findByRole('combobox', { name: 'Cliente del caso' });
    await waitFor(() => expect(customer).not.toBeDisabled());
    expect(server.payloads('GetCustomersQuery')).toEqual([{}]);
    fireEvent.change(within(dialog).getByRole('textbox', { name: /^Falla reportada/ }), { target: { value: 'Pantalla con rayas' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Abrir caso' }));
    expect(await within(dialog).findByText('Indique el cliente del caso (la venta es de otra sucursal o no está a la vista).')).toBeInTheDocument();
    fireEvent.change(customer, { target: { value: 'ana' } });
    fireEvent.click(await within(dialog).findByRole('option', { name: /Ana Rojas/ }));
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Abrir caso' }));
    });
    await waitFor(() =>
      expect(server.payloads('OpenWarrantyClaimCommand')).toEqual([{ serial: 'SN-OTRA', issue: 'Pantalla con rayas', chargeableRepair: false, customerCode: 'C0005', sku: 'NB-ASUS-01' }]),
    );
  });

  it('el rechazo del servidor (caso ya abierto) se ve dentro del diálogo', async () => {
    await openClaims('ADMIN', '?abrir=1&serie=SN-NEW1', {
      OpenWarrantyClaimCommand: () => {
        throw new WebApiError({ kind: 'domain', status: 422, message: 'La serie SN-NEW1 ya tiene el caso RMA-CM-000003 abierto.' });
      },
    });
    const dialog = await screen.findByRole('dialog', { name: 'Abrir un caso de garantía' }, { timeout: 5000 });
    await within(dialog).findByTestId('unidad-del-caso');
    fireEvent.change(within(dialog).getByRole('textbox', { name: /^Falla reportada/ }), { target: { value: 'No enciende' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Abrir caso' }));
    });
    expect(await within(dialog).findByTestId('error-del-servidor')).toHaveTextContent('ya tiene el caso RMA-CM-000003 abierto');
  });
});

describe('Garantías · orden de servicio, permisos y tablero', () => {
  it('imprime la orden de servicio con la impresora del navegador', async () => {
    const print = vi.spyOn(window, 'print').mockImplementation(() => undefined);
    const { server } = await openClaims();
    const table = await claimsTable();
    fireEvent.click(within(rowOf(table, 'RMA-CB-000002')).getByRole('button', { name: /^Acciones de / }));
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Imprimir la orden de servicio' }));
    const dialog = await screen.findByRole('dialog', { name: 'Orden de servicio RMA-CB-000002' });
    const sheet = await within(dialog).findByTestId('orden-de-servicio');
    expect(sheet).toHaveTextContent('Orden de servicio técnico');
    expect(sheet).toHaveTextContent('Mariana Céspedes');
    expect(sheet).toHaveTextContent('Con cargo (reparación con cargo)');
    expect(sheet).toHaveTextContent('Proveedor: Distribuidora Andina S.R.L.');
    expect(sheet).toHaveTextContent('Firma del cliente');
    expect(screen.getByTestId('area-imprimible')).toHaveTextContent('RMA-CB-000002');
    expect(server.payloads('GetWarrantyClaimQuery')).toEqual([{ number: 'RMA-CB-000002' }]);
    fireEvent.click(within(dialog).getByRole('button', { name: 'Imprimir' }));
    expect(print).toHaveBeenCalledTimes(1);
  });

  it('consulta (solo ver) no abre casos: sin el botón y `?abrir=1` no abre nada', async () => {
    const { location } = await openClaims('CONSULTA', '?abrir=1');
    await claimsTable();
    await waitFor(() => expect(location()).toBe('/panel/garantias'));
    expect(screen.queryByRole('button', { name: 'Abrir un caso' })).not.toBeInTheDocument();
    expect(screen.queryByRole('dialog', { name: 'Abrir un caso de garantía' })).not.toBeInTheDocument();
  });

  it('consulta (solo ver) no ve los pasos del caso, ni notas, ni las ventas del cliente', async () => {
    await openClaims('CONSULTA');
    const table = await claimsTable();
    const panel = await openDetail(table, 'RMA-CM-000003');
    expect(within(panel).getByRole('button', { name: 'Orden de servicio' })).toBeInTheDocument();
    expect(within(panel).queryByRole('button', { name: 'Marcar reparado' })).not.toBeInTheDocument();
    expect(within(panel).queryByRole('button', { name: 'Agregar nota' })).not.toBeInTheDocument();
    expect(within(panel).queryByRole('link', { name: 'Ver las ventas del cliente' })).not.toBeInTheDocument();
  });

  it('el tablero ofrece «Abrir un caso de garantía» solo a quien puede abrir casos', () => {
    const offered = (role: StaffRole) => dashboardActions(REGISTRY, ROLE_PERMISSIONS[role]).flatMap((group) => group.actions.map((item) => item.to));
    expect(offered('VENTAS')).toEqual(['/panel/garantias?abrir=1']);
    expect(offered('BODEGA')).toEqual(['/panel/garantias?abrir=1']);
    expect(offered('CONSULTA')).toEqual([]);
  });
});
