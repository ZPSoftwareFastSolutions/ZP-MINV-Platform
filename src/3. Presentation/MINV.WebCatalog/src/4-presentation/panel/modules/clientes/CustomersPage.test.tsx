// Módulo «Clientes» dentro del panel (con un registro PROPIO de la prueba: solo este módulo, así no depende de los demás
// módulos en construcción) y con un servidor de clientes SIMULADO en la prueba: lista con datos de factura y la marca
// «Cliente web», filtros en la dirección, detalle con «Ver sus ventas», alta con los dos comandos en orden (y el reintento
// que corrige el MISMO cliente), edición (primero los datos de factura), verificación del NIT, activar y desactivar (con
// confirmación), resumen plegado, exportación, estados de error y lo que ve cada rol. Ninguna prueba toca la red.

import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { WebApiError } from '@/1-domain/auth/errors';
import { permissionName, type RpcOperationName, type RpcResponseOf } from '@/4-presentation/app/contract';
import { ROLE_PERMISSIONS, renderPanel, signedInAs, type MockWeb, type StaffRole } from '@/test-utils';
import { buildRegistry, dashboardActions } from '../../registry';
import { PanelApp } from '../../shell/PanelApp';
import clientes from './module';

const REGISTRY = buildRegistry([{ source: '../modules/clientes/module.tsx', definition: clientes }]);

beforeAll(async () => {
  await import('./CustomersPage');
}, 30_000);

afterEach(() => {
  vi.restoreAllMocks();
});

type Customer = RpcResponseOf<'GetCustomersQuery'>['customers'][number];

function customer(overrides: Partial<Customer>): Customer {
  return {
    code: 'C0001',
    name: 'Cliente',
    taxId: null,
    email: null,
    phone: null,
    categoryCode: 'GENERAL',
    category: 'General',
    isActive: true,
    purchases: 0,
    total: 0,
    lastPurchase: null,
    ...overrides,
  };
}

const VIEW: RpcResponseOf<'GetCustomersQuery'> = {
  categories: [
    { code: 'FRECUENTE', name: 'Frecuente' },
    { code: 'GENERAL', name: 'General' },
    { code: 'MAYORISTA', name: 'Mayorista' },
  ],
  customers: [
    customer({ code: 'CF', name: 'Consumidor final', purchases: 40, total: 12000, lastPurchase: '2026-09-29' }),
    customer({ code: 'C0002', name: 'Mariana Céspedes', taxId: '4455667', email: 'mariana@correo.example', phone: '71234567', categoryCode: 'FRECUENTE', category: 'Frecuente', purchases: 5, total: 9500.5, lastPurchase: '2026-09-20' }),
    customer({ code: 'WEB-000003', name: 'Luis Arce', taxId: '1020304050', email: 'luis@correo.example', purchases: 1, total: 120, lastPurchase: '2026-09-28' }),
    customer({ code: 'C0004', name: 'Importadora Andina S.R.L.', taxId: '3344556', phone: '2-2445566', categoryCode: 'MAYORISTA', category: 'Mayorista', isActive: false }),
  ],
};

const IDENTITIES: RpcResponseOf<'GetCustomerFiscalIdentitiesQuery'> = [
  { code: 'C0002', documentType: 1, documentNumber: '4455667', complement: '1A' },
  { code: 'WEB-000003', documentType: 5, documentNumber: '1020304050', complement: null },
  { code: 'C0004', documentType: null, documentNumber: '3344556', complement: null },
  { code: 'CF', documentType: null, documentNumber: null, complement: null },
];

type Handler = (payload: unknown) => unknown;

interface FakeOptions {
  moduleActive?: boolean;
  handlers?: Partial<Record<RpcOperationName, Handler>>;
}

/** Servidor de clientes en memoria: responde las operaciones del módulo y deja el resto al modo mock. */
function fakeServer(web: MockWeb, options: FakeOptions = {}) {
  const real = web.backend.rpc.call.bind(web.backend.rpc);
  const spy = vi.spyOn(web.backend.rpc, 'call').mockImplementation(async (operation, payload, sendOptions) => {
    const reply = (result: unknown) => ({ result: result as never, replayed: false, requestId: sendOptions?.requestId ?? 'prueba' });
    const custom = options.handlers?.[operation];
    if (custom) return reply(await custom(payload));
    switch (operation) {
      case 'GetBillingAccessQuery':
        return reply({ moduleActive: options.moduleActive ?? true, configured: true, enabled: true, environment: 2 });
      case 'GetCustomersQuery':
        return reply(VIEW);
      case 'GetCustomerFiscalIdentitiesQuery':
        return reply(IDENTITIES);
      default:
        // Lo que no es del módulo lo atiende el modo mock (la sesión, el cambio de sucursal…).
        return real(operation as never, payload as never, sendOptions);
    }
  });
  const payloads = (operation: RpcOperationName) => spy.mock.calls.filter(([name]) => name === operation).map(([, payload]) => payload);
  const order = () => spy.mock.calls.map(([name]) => name).filter((name) => name === 'SaveCustomerCommand' || name === 'SaveCustomerFiscalIdentityCommand');
  return { spy, payloads, order };
}

async function openCustomers(role: StaffRole = 'ADMIN', query = '', options: FakeOptions = {}) {
  const web = await signedInAs(role);
  const server = fakeServer(web, options);
  const view = await renderPanel(<PanelApp registry={REGISTRY} />, { web: web.services, route: `/panel/clientes${query}`, path: '/panel/*' });
  return { web, server, location: view.location };
}

async function customersTable(): Promise<HTMLElement> {
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

describe('Clientes · lista', () => {
  it('muestra la cartera por nombre, con los datos de factura y la marca «Cliente web» (pedidos exactos)', async () => {
    const { server } = await openCustomers();
    expect(await screen.findByRole('heading', { level: 1, name: 'Clientes' }, { timeout: 5000 })).toBeInTheDocument();
    const table = await customersTable();
    expect(keys(table)).toEqual(['CF', 'C0004', 'WEB-000003', 'C0002']);
    expect(server.payloads('GetCustomersQuery')).toEqual([{}]);
    expect(server.payloads('GetCustomerFiscalIdentitiesQuery')).toEqual([{}]);
    expect(rowOf(table, 'C0002')).toHaveTextContent('CI 4455667-1A');
    expect(rowOf(table, 'WEB-000003')).toHaveTextContent('Cliente web');
    expect(rowOf(table, 'C0004')).toHaveTextContent('Sin tipo de documento');
    expect(rowOf(table, 'C0004')).toHaveTextContent('Inactivo');
    expect(screen.getByTestId('clientes-resumen')).toHaveTextContent('4 de 4 clientes · 3 activos');
    // El resumen de la cartera está plegado al entrar.
    expect(screen.queryByTestId('resumen-cartera')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /Ver resumen de la cartera/ }));
    expect(await screen.findByTestId('resumen-cartera')).toHaveTextContent('Mejor clienteMariana Céspedes');
  });

  it('filtra con listas desplegables y búsqueda; los filtros quedan en la dirección', async () => {
    const { location } = await openCustomers();
    const table = await customersTable();
    fireEvent.change(screen.getByRole('combobox', { name: 'Categoría' }), { target: { value: 'FRECUENTE' } });
    await waitFor(() => expect(location()).toBe('/panel/clientes?categoria=FRECUENTE'));
    await waitFor(() => expect(keys(table)).toEqual(['C0002']));
    fireEvent.click(screen.getByRole('button', { name: 'Limpiar filtros' }));
    fireEvent.change(screen.getByRole('combobox', { name: 'Datos de factura' }), { target: { value: 'sin' } });
    await waitFor(() => expect(keys(table)).toEqual(['CF', 'C0004']));
    fireEvent.change(screen.getByRole('combobox', { name: 'Estado' }), { target: { value: 'inactivo' } });
    await waitFor(() => expect(keys(table)).toEqual(['C0004']));
    fireEvent.click(screen.getByRole('button', { name: 'Limpiar filtros' }));
    fireEvent.change(screen.getByRole('combobox', { name: 'Origen' }), { target: { value: 'web' } });
    await waitFor(() => expect(keys(table)).toEqual(['WEB-000003']));
    fireEvent.click(screen.getByRole('button', { name: 'Limpiar filtros' }));
    const search = screen.getByRole('searchbox', { name: 'Buscar' });
    fireEvent.change(search, { target: { value: '4455667' } });
    fireEvent.keyDown(search, { key: 'Enter' });
    await waitFor(() => expect(location()).toBe('/panel/clientes?q=4455667'));
    await waitFor(() => expect(keys(table)).toEqual(['C0002']));
  });

  it('el detalle lateral muestra los datos de factura y lleva a «Ver sus ventas»', async () => {
    await openCustomers();
    const table = await customersTable();
    fireEvent.click(within(rowOf(table, 'C0002')).getAllByRole('button')[0]);
    const panel = await screen.findByRole('dialog', { name: 'Mariana Céspedes' });
    expect(panel).toHaveTextContent('CI · Cédula de identidad');
    expect(panel).toHaveTextContent('Completos: se le factura a su nombre');
    expect(within(panel).getByRole('link', { name: 'Ver sus ventas' })).toHaveAttribute('href', '/panel/ventas?cliente=C0002');
  });

  it('exporta los clientes filtrados a CSV', async () => {
    const created: Blob[] = [];
    vi.spyOn(URL, 'createObjectURL').mockImplementation((blob) => {
      created.push(blob as Blob);
      return 'blob:prueba';
    });
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    await openCustomers('ADMIN', '?origen=web');
    await customersTable();
    fireEvent.click(screen.getByRole('button', { name: 'Exportar CSV' }));
    expect(await screen.findByText('Exportación lista')).toBeInTheDocument();
    const lines = (await created[0].text()).replace(/^﻿/, '').trim().split('\r\n');
    expect(lines[0]).toBe('"Código";"Nombre";"Tipo de documento";"NIT / CI";"Complemento";"Correo";"Teléfono";"Categoría";"Compras";"Total";"Última compra";"Activo";"Cliente web"');
    expect(lines.slice(1)).toEqual([expect.stringContaining('"WEB-000003";"Luis Arce";"NIT";"1020304050"')]);
  });

  it('si la consulta falla muestra «Reintentar» y se recupera', async () => {
    let fail = true;
    await openCustomers('ADMIN', '', {
      handlers: {
        GetCustomersQuery: () => {
          if (fail) {
            fail = false;
            throw new WebApiError({ kind: 'network', message: 'Sin conexión' });
          }
          return VIEW;
        },
      },
    });
    const error = await screen.findByTestId('estado-error', undefined, { timeout: 5000 });
    fireEvent.click(within(error).getByRole('button', { name: 'Reintentar' }));
    await customersTable();
  });

  it('sin la facturación del SIN no pide ni muestra los datos de factura', async () => {
    const { server } = await openCustomers('ADMIN', '', { moduleActive: false });
    await customersTable();
    await waitFor(() => expect(screen.queryByRole('combobox', { name: 'Datos de factura' })).not.toBeInTheDocument());
    expect(server.payloads('GetCustomersQuery')).toEqual([{}]);
    fireEvent.click(screen.getByRole('button', { name: 'Nuevo cliente' }));
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo cliente' });
    expect(within(dialog).queryByRole('combobox', { name: 'Tipo de documento' })).not.toBeInTheDocument();
    expect(within(dialog).getByLabelText(/^NIT \/ CI/)).toBeInTheDocument();
  });
});

describe('Clientes · alta, edición y estado', () => {
  it('nuevo cliente: valida, crea el cliente y DESPUÉS guarda sus datos de factura (pedidos exactos)', async () => {
    const { server } = await openCustomers('ADMIN', '', {
      handlers: {
        SaveCustomerCommand: () => 'C0005',
        SaveCustomerFiscalIdentityCommand: () => '✔ Datos de facturación de Rosa Quispe: documento 7788990-2B.',
      },
    });
    await customersTable();
    fireEvent.click(screen.getByRole('button', { name: 'Nuevo cliente' }));
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo cliente' });
    fireEvent.change(within(dialog).getByRole('combobox', { name: 'Tipo de documento' }), { target: { value: '1' } });
    fireEvent.change(within(dialog).getByLabelText(/^Número de documento/), { target: { value: '77.88' } });
    fireEvent.change(within(dialog).getByLabelText(/^Correo/), { target: { value: 'rosa@' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar' }));
    expect(await within(dialog).findByText('Indique el nombre del cliente.')).toBeInTheDocument();
    expect(within(dialog).getByText('El CI lleva solo números (sin puntos ni guiones).')).toBeInTheDocument();
    expect(within(dialog).getByText('El correo no es válido (por ejemplo nombre@dominio.com).')).toBeInTheDocument();
    expect(server.payloads('SaveCustomerCommand')).toHaveLength(0);

    fireEvent.change(within(dialog).getByLabelText(/^Nombre o razón social/), { target: { value: 'Rosa Quispe' } });
    fireEvent.change(within(dialog).getByRole('combobox', { name: 'Categoría' }), { target: { value: 'FRECUENTE' } });
    fireEvent.change(within(dialog).getByLabelText(/^Número de documento/), { target: { value: '7788990' } });
    fireEvent.change(within(dialog).getByLabelText(/^Complemento/), { target: { value: '2b' } });
    fireEvent.change(within(dialog).getByLabelText(/^Correo/), { target: { value: 'rosa@correo.example' } });
    fireEvent.change(within(dialog).getByLabelText(/^Teléfono/), { target: { value: '76543210' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar' }));
    });
    expect(await screen.findByText('Cliente creado')).toBeInTheDocument();
    expect(screen.getByText('C0005 · Rosa Quispe')).toBeInTheDocument();
    expect(server.order()).toEqual(['SaveCustomerCommand', 'SaveCustomerFiscalIdentityCommand']);
    expect(server.payloads('SaveCustomerCommand')).toEqual([
      { code: null, name: 'Rosa Quispe', taxId: '7788990', email: 'rosa@correo.example', phone: '76543210', categoryCode: 'FRECUENTE', isActive: true },
    ]);
    expect(server.payloads('SaveCustomerFiscalIdentityCommand')).toEqual([{ code: 'C0005', documentType: 1, documentNumber: '7788990', complement: '2B' }]);
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Nuevo cliente' })).not.toBeInTheDocument());
  });

  it('si los datos de factura fallan después de crear, el reintento corrige el MISMO cliente (no crea otro)', async () => {
    let identityAttempts = 0;
    const { server } = await openCustomers('ADMIN', '', {
      handlers: {
        SaveCustomerCommand: (payload) => (payload as { code: string | null }).code ?? 'C0005',
        SaveCustomerFiscalIdentityCommand: () => {
          identityAttempts += 1;
          if (identityAttempts === 1) throw new WebApiError({ kind: 'domain', status: 422, message: 'Con CI o NIT el número de documento solo admite dígitos.' });
          return '✔ Datos de facturación guardados.';
        },
      },
    });
    await customersTable();
    fireEvent.click(screen.getByRole('button', { name: 'Nuevo cliente' }));
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo cliente' });
    fireEvent.change(within(dialog).getByLabelText(/^Nombre o razón social/), { target: { value: 'Rosa Quispe' } });
    fireEvent.change(within(dialog).getByRole('combobox', { name: 'Tipo de documento' }), { target: { value: '5' } });
    fireEvent.change(within(dialog).getByLabelText(/^Número de documento/), { target: { value: '7788990' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar' }));
    });
    expect(await within(dialog).findByText(/El cliente se creó con el código C0005, pero sus datos de factura no se guardaron/)).toBeInTheDocument();
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar' }));
    });
    expect(await screen.findByText('Cliente creado')).toBeInTheDocument();
    expect(server.payloads('SaveCustomerCommand').map((payload) => (payload as { code: string | null }).code)).toEqual([null, 'C0005']);
    expect(server.order()).toEqual(['SaveCustomerCommand', 'SaveCustomerFiscalIdentityCommand', 'SaveCustomerFiscalIdentityCommand', 'SaveCustomerCommand']);
  });

  it('editar: primero los datos de factura y después el cliente; verifica el NIT en el Padrón', async () => {
    const { server } = await openCustomers('ADMIN', '', {
      handlers: {
        SaveCustomerCommand: () => 'WEB-000003',
        SaveCustomerFiscalIdentityCommand: () => '✔ Datos guardados.',
        VerifyNitCommand: () => ({ nit: 1020304050, isValid: true, siatCode: 986, description: '✔ NIT ACTIVO', checked: true }),
      },
    });
    const table = await customersTable();
    fireEvent.click(within(await openRowMenu(table, 'WEB-000003')).getByRole('menuitem', { name: 'Editar' }));
    const dialog = await screen.findByRole('dialog', { name: 'Editar el cliente WEB-000003' });
    expect(within(dialog).getByRole('combobox', { name: 'Tipo de documento' })).toHaveValue('5');
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Verificar el NIT en el Padrón' }));
    });
    expect(await within(dialog).findByText('NIT activo en el Padrón · NIT ACTIVO')).toBeInTheDocument();
    expect(server.payloads('VerifyNitCommand')).toEqual([{ nit: 1020304050, customerCode: 'WEB-000003' }]);

    fireEvent.change(within(dialog).getByLabelText(/^Teléfono/), { target: { value: '70011223' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar' }));
    });
    expect(await screen.findByText('Cliente actualizado')).toBeInTheDocument();
    expect(server.order()).toEqual(['SaveCustomerFiscalIdentityCommand', 'SaveCustomerCommand']);
    expect(server.payloads('SaveCustomerCommand')).toEqual([
      { code: 'WEB-000003', name: 'Luis Arce', taxId: '1020304050', email: 'luis@correo.example', phone: '70011223', categoryCode: 'GENERAL', isActive: true },
    ]);
    expect(server.payloads('SaveCustomerFiscalIdentityCommand')).toEqual([{ code: 'WEB-000003', documentType: 5, documentNumber: '1020304050', complement: null }]);
  });

  it('desactivar pide confirmación y envía el cliente con isActive=false; activar es directo; el consumidor final no se desactiva', async () => {
    const { server } = await openCustomers('ADMIN', '', { handlers: { SaveCustomerCommand: (payload) => (payload as { code: string }).code } });
    const table = await customersTable();
    fireEvent.click(within(await openRowMenu(table, 'C0002')).getByRole('menuitem', { name: 'Desactivar' }));
    const confirm = await screen.findByRole('alertdialog', { name: '¿Desactivar a Mariana Céspedes?' });
    expect(server.payloads('SaveCustomerCommand')).toHaveLength(0);
    await act(async () => {
      fireEvent.click(within(confirm).getByRole('button', { name: 'Desactivar' }));
    });
    expect(await screen.findByText('Cliente desactivado')).toBeInTheDocument();
    expect(server.payloads('SaveCustomerCommand')).toEqual([
      { code: 'C0002', name: 'Mariana Céspedes', taxId: '4455667', email: 'mariana@correo.example', phone: '71234567', categoryCode: 'FRECUENTE', isActive: false },
    ]);

    await act(async () => {
      fireEvent.click(within(await openRowMenu(table, 'C0004')).getByRole('menuitem', { name: 'Activar' }));
    });
    expect(await screen.findByText('Cliente activado')).toBeInTheDocument();
    expect(server.payloads('SaveCustomerCommand')[1]).toMatchObject({ code: 'C0004', isActive: true });

    const menu = await openRowMenu(table, 'CF');
    expect(within(menu).getByRole('menuitem', { name: /Desactivar/ })).toHaveAttribute('aria-disabled', 'true');
    expect(menu).toHaveTextContent('El consumidor final no se puede desactivar.');
  });

  it('desde el tablero (?nuevo=1) abre «Nuevo cliente» y el parámetro se quita de la dirección', async () => {
    const { location } = await openCustomers('ADMIN', '?nuevo=1');
    expect(await screen.findByRole('dialog', { name: 'Nuevo cliente' }, { timeout: 5000 })).toBeInTheDocument();
    await waitFor(() => expect(location()).toBe('/panel/clientes'));
  });
});

describe('Clientes · lo que ve cada rol', () => {
  it('la gerencia (ve ventas, no gestiona clientes) ve la cartera sin «Nuevo cliente» ni «Editar»', async () => {
    await openCustomers('GERENCIA');
    const table = await customersTable();
    expect(screen.queryByRole('button', { name: 'Nuevo cliente' })).not.toBeInTheDocument();
    const menu = await openRowMenu(table, 'C0002');
    expect(within(menu).getByRole('menuitem', { name: 'Ver sus ventas' })).toBeInTheDocument();
    expect(within(menu).queryByRole('menuitem', { name: 'Editar' })).not.toBeInTheDocument();
    expect(within(menu).queryByRole('menuitem', { name: 'Desactivar' })).not.toBeInTheDocument();
  });

  it('bodega (sin gestionar clientes ni ver ventas) ve «No tiene acceso a esta pantalla»', async () => {
    await openCustomers('BODEGA');
    const denied = await screen.findByTestId('sin-acceso', undefined, { timeout: 5000 });
    expect(denied).toHaveTextContent(`«${permissionName('sales.customers.manage')}» o «${permissionName('sales.view')}»`);
  });

  it('ofrece «Nuevo cliente» al tablero solo a quien gestiona clientes', () => {
    expect(REGISTRY.problems).toEqual([]);
    const offered = (role: StaffRole) => dashboardActions(REGISTRY, ROLE_PERMISSIONS[role]).flatMap((group) => group.actions.map((item) => item.to));
    expect(offered('CAJERO')).toEqual(['/panel/clientes?nuevo=1']);
    expect(offered('GERENCIA')).toEqual([]);
    expect(offered('CONSULTA')).toEqual([]);
  });
});
