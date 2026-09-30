// Módulo «Proveedores» dentro del panel real, con un registro que tiene SOLO este módulo y un servidor de proveedores
// SIMULADO en la prueba: lista con filtros en la dirección, resumen plegado, detalle con los enlaces a sus órdenes, alta
// y edición con validación y el pedido exacto, el error del servidor dentro del diálogo, desactivar con confirmación (el
// menú «⋯» no abre el detalle), activar, exportar, «Reintentar» y lo que ve cada rol. Ninguna prueba toca la red.

import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { WebApiError } from '@/1-domain/auth/errors';
import { permissionName, type RpcOperationName, type RpcResponseOf } from '@/4-presentation/app/contract';
import { renderPanel, signedInAs, type MockWeb, type StaffRole } from '@/test-utils';
import { buildRegistry } from '../../registry';
import { PanelApp } from '../../shell/PanelApp';
import proveedores from './module';

const REGISTRY = buildRegistry([{ source: '../modules/proveedores/module.tsx', definition: proveedores }]);

beforeAll(async () => {
  await import('../../shell/PanelLayout');
  await import('./SuppliersPage');
}, 30_000);

afterEach(() => {
  vi.restoreAllMocks();
});

type Supplier = RpcResponseOf<'GetSuppliersQuery'>[number];

function supplier(overrides: Partial<Supplier>): Supplier {
  return {
    code: 'P001',
    name: 'Distribuidora Andina',
    taxId: '1020304',
    leadTimeDays: 3,
    contact: 'Rosa Quispe',
    phone: '71234567',
    email: 'ventas@andina.example',
    isActive: true,
    products: 4,
    openOrders: 1,
    purchased: 5000,
    ...overrides,
  };
}

const SUPPLIERS: Supplier[] = [
  supplier({}),
  supplier({ code: 'P002', name: 'Tecno Import', taxId: null, contact: null, phone: null, email: null, leadTimeDays: 10, openOrders: 0, purchased: 12000, products: 2 }),
  supplier({ code: 'P003', name: 'Viejo proveedor', contact: 'Luis', phone: null, email: null, isActive: false, leadTimeDays: 5, openOrders: 0, purchased: 0, products: 0 }),
];

type Handler = (payload: unknown) => unknown;

function fakeServer(web: MockWeb, handlers: Partial<Record<RpcOperationName, Handler>> = {}) {
  const real = web.backend.rpc.call.bind(web.backend.rpc);
  const spy = vi.spyOn(web.backend.rpc, 'call').mockImplementation(async (operation, payload, options) => {
    const reply = (result: unknown) => ({ result: result as never, replayed: false, requestId: options?.requestId ?? 'prueba' });
    const custom = handlers[operation];
    if (custom) return reply(await custom(payload));
    if (operation === 'GetSuppliersQuery') return reply(SUPPLIERS);
    return real(operation as never, payload as never, options);
  });
  const payloads = (operation: RpcOperationName) => spy.mock.calls.filter(([name]) => name === operation).map(([, payload]) => payload);
  return { spy, payloads };
}

async function openSuppliers(role: StaffRole = 'ADMIN', query = '', handlers: Partial<Record<RpcOperationName, Handler>> = {}) {
  const web = await signedInAs(role);
  const server = fakeServer(web, handlers);
  const view = await renderPanel(<PanelApp registry={REGISTRY} />, { web: web.services, route: `/panel/proveedores${query}`, path: '/panel/*' });
  return { server, location: view.location };
}

async function suppliersTable(): Promise<HTMLElement> {
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

describe('Proveedores · lista', () => {
  it('muestra los proveedores por nombre con el resumen plegado (pedido exacto)', async () => {
    const { server } = await openSuppliers();
    expect(await screen.findByRole('heading', { level: 1, name: 'Proveedores' }, { timeout: 5000 })).toBeInTheDocument();
    const table = await suppliersTable();
    expect(keys(table)).toEqual(['P001', 'P002', 'P003']);
    expect(server.payloads('GetSuppliersQuery')).toEqual([{}]);
    expect(rowOf(table, 'P002')).toHaveTextContent('10 días');
    expect(rowOf(table, 'P003')).toHaveTextContent('Inactivo');
    expect(screen.getByTestId('proveedores-resumen')).toHaveTextContent('3 de 3 proveedores · 2 activos');
    expect(screen.queryByTestId('resumen-proveedores')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /Ver resumen de los proveedores/ }));
    expect(await screen.findByTestId('resumen-proveedores')).toHaveTextContent('Principal: Tecno Import');
  });

  it('filtra con listas desplegables y búsqueda (`?q=` desde Órdenes de compra)', async () => {
    const { location } = await openSuppliers('ADMIN', '?q=P002');
    const table = await suppliersTable();
    await waitFor(() => expect(keys(table)).toEqual(['P002']));
    fireEvent.click(screen.getByRole('button', { name: 'Limpiar filtros' }));
    fireEvent.change(screen.getByRole('combobox', { name: 'Órdenes abiertas' }), { target: { value: 'con' } });
    await waitFor(() => expect(location()).toBe('/panel/proveedores?ordenes=con'));
    await waitFor(() => expect(keys(table)).toEqual(['P001']));
    fireEvent.click(screen.getByRole('button', { name: 'Limpiar filtros' }));
    fireEvent.change(screen.getByRole('combobox', { name: 'Plazo de entrega' }), { target: { value: 'mas' } });
    await waitFor(() => expect(keys(table)).toEqual(['P002']));
    fireEvent.change(screen.getByRole('combobox', { name: 'Estado' }), { target: { value: 'inactivo' } });
    await waitFor(() => expect(within(table).queryAllByRole('row').filter((row) => row.hasAttribute('data-row-key'))).toHaveLength(0));
  });

  it('el detalle lleva a sus órdenes y a una orden nueva', async () => {
    await openSuppliers();
    const table = await suppliersTable();
    fireEvent.click(within(rowOf(table, 'P001')).getAllByRole('button')[0]);
    const panel = await screen.findByRole('dialog', { name: 'Distribuidora Andina' });
    expect(panel).toHaveTextContent('3 días');
    expect(within(panel).getByRole('link', { name: 'Ver sus órdenes de compra' })).toHaveAttribute('href', '/panel/compras?proveedor=P001');
    expect(within(panel).getByRole('link', { name: 'Nueva orden de compra' })).toHaveAttribute('href', '/panel/compras?nueva=1&proveedor=P001');
  });

  it('exporta los proveedores filtrados a CSV', async () => {
    const created: Blob[] = [];
    vi.spyOn(URL, 'createObjectURL').mockImplementation((blob) => {
      created.push(blob as Blob);
      return 'blob:prueba';
    });
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    await openSuppliers('ADMIN', '?estado=activo');
    await suppliersTable();
    fireEvent.click(screen.getByRole('button', { name: 'Exportar CSV' }));
    expect(await screen.findByText('Exportación lista')).toBeInTheDocument();
    const lines = (await created[0].text()).replace(/^﻿/, '').trim().split('\r\n');
    expect(lines[0]).toBe('"Código";"Razón social";"NIT";"Contacto";"Teléfono";"Correo";"Días de entrega";"Productos";"Órdenes abiertas";"Comprado";"Activo"');
    expect(lines).toHaveLength(3);
  });

  it('si la consulta falla muestra «Reintentar» y se recupera', async () => {
    let fail = true;
    await openSuppliers('ADMIN', '', {
      GetSuppliersQuery: () => {
        if (fail) {
          fail = false;
          throw new WebApiError({ kind: 'network', message: 'Sin conexión' });
        }
        return SUPPLIERS;
      },
    });
    const error = await screen.findByTestId('estado-error', undefined, { timeout: 5000 });
    fireEvent.click(within(error).getByRole('button', { name: 'Reintentar' }));
    await suppliersTable();
  });
});

describe('Proveedores · alta, edición y estado', () => {
  it('nuevo proveedor: valida, muestra el error del servidor dentro del diálogo y envía el pedido exacto', async () => {
    let attempts = 0;
    const { server } = await openSuppliers('ADMIN', '', {
      SaveSupplierCommand: () => {
        attempts += 1;
        if (attempts === 1) throw new WebApiError({ kind: 'validation', status: 400, message: 'El correo no es válido.' });
        return 'P004';
      },
    });
    await suppliersTable();
    fireEvent.click(screen.getByRole('button', { name: 'Nuevo proveedor' }));
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo proveedor' });
    fireEvent.change(within(dialog).getByLabelText(/^Correo/), { target: { value: 'compras@' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar' }));
    expect(await within(dialog).findByText('Indique la razón social.')).toBeInTheDocument();
    expect(within(dialog).getByText('El correo no es válido (por ejemplo nombre@dominio.com).')).toBeInTheDocument();
    expect(server.payloads('SaveSupplierCommand')).toHaveLength(0);

    fireEvent.change(within(dialog).getByLabelText(/^Razón social/), { target: { value: 'Importadora Sur S.R.L.' } });
    fireEvent.change(within(dialog).getByLabelText(/^Correo/), { target: { value: 'compras@sur.example' } });
    fireEvent.change(within(dialog).getByRole('combobox', { name: 'Días de entrega' }), { target: { value: 'otro' } });
    fireEvent.change(within(dialog).getByLabelText(/^Plazo de entrega en días/), { target: { value: '12' } });
    fireEvent.change(within(dialog).getByLabelText(/^Contacto/), { target: { value: 'Ana Mamani' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar' }));
    });
    expect(await within(dialog).findByText('El correo no es válido.')).toBeInTheDocument();
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar' }));
    });
    expect(await screen.findByText('Proveedor creado')).toBeInTheDocument();
    expect(screen.getByText('P004 · Importadora Sur S.R.L.')).toBeInTheDocument();
    const expected = { code: null, name: 'Importadora Sur S.R.L.', taxId: null, leadTimeDays: 12, contactName: 'Ana Mamani', phone: null, email: 'compras@sur.example', isActive: true };
    expect(server.payloads('SaveSupplierCommand')).toEqual([expected, expected]);
  });

  it('editar envía los datos con su código', async () => {
    const { server } = await openSuppliers('ADMIN', '', { SaveSupplierCommand: () => 'P002' });
    const table = await suppliersTable();
    fireEvent.click(within(await openRowMenu(table, 'P002')).getByRole('menuitem', { name: 'Editar' }));
    const dialog = await screen.findByRole('dialog', { name: 'Editar el proveedor P002' });
    expect(screen.queryByRole('dialog', { name: 'Tecno Import' })).not.toBeInTheDocument();
    expect(within(dialog).getByRole('combobox', { name: 'Días de entrega' })).toHaveValue('10');
    fireEvent.change(within(dialog).getByRole('combobox', { name: 'Días de entrega' }), { target: { value: '7' } });
    fireEvent.change(within(dialog).getByLabelText(/^Teléfono/), { target: { value: '2-2445566' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar' }));
    });
    expect(await screen.findByText('Proveedor actualizado')).toBeInTheDocument();
    expect(server.payloads('SaveSupplierCommand')).toEqual([
      { code: 'P002', name: 'Tecno Import', taxId: null, leadTimeDays: 7, contactName: null, phone: '2-2445566', email: null, isActive: true },
    ]);
  });

  it('desactivar pide confirmación (sin abrir el detalle); activar es directo', async () => {
    const { server } = await openSuppliers('ADMIN', '', { SaveSupplierCommand: (payload) => (payload as { code: string }).code });
    const table = await suppliersTable();
    fireEvent.click(within(await openRowMenu(table, 'P001')).getByRole('menuitem', { name: 'Desactivar' }));
    const confirm = await screen.findByRole('alertdialog', { name: '¿Desactivar a Distribuidora Andina?' });
    expect(screen.queryByRole('dialog', { name: 'Distribuidora Andina' })).not.toBeInTheDocument();
    expect(server.payloads('SaveSupplierCommand')).toHaveLength(0);
    await act(async () => {
      fireEvent.click(within(confirm).getByRole('button', { name: 'Desactivar' }));
    });
    expect(await screen.findByText('Proveedor desactivado')).toBeInTheDocument();
    expect(server.payloads('SaveSupplierCommand')[0]).toMatchObject({ code: 'P001', isActive: false, leadTimeDays: 3, contactName: 'Rosa Quispe' });
    await act(async () => {
      fireEvent.click(within(await openRowMenu(table, 'P003')).getByRole('menuitem', { name: 'Activar' }));
    });
    expect(await screen.findByText('Proveedor activado')).toBeInTheDocument();
    expect(server.payloads('SaveSupplierCommand')[1]).toMatchObject({ code: 'P003', isActive: true });
  });
});

describe('Proveedores · lo que ve cada rol', () => {
  it('bodega (gestiona compras) ve «Nuevo proveedor» y las acciones', async () => {
    await openSuppliers('BODEGA');
    const table = await suppliersTable();
    expect(screen.getByRole('button', { name: 'Nuevo proveedor' })).toBeInTheDocument();
    const menu = await openRowMenu(table, 'P001');
    expect(within(menu).getByRole('menuitem', { name: 'Nueva orden de compra' })).toBeInTheDocument();
  });

  it('el cajero (sin gestionar compras) ve «No tiene acceso a esta pantalla»', async () => {
    await openSuppliers('CAJERO');
    const denied = await screen.findByTestId('sin-acceso', undefined, { timeout: 5000 });
    expect(denied).toHaveTextContent(permissionName('purchasing.manage'));
  });
});
