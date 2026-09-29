// Módulo «Usuarios» dentro del panel real (esqueleto, rutas y permisos) con un registro que tiene SOLO este módulo (así
// las pruebas no dependen de los módulos que otros construyen a la vez). El servidor se simula en memoria en cada prueba
// (`GetUsersQuery`, `GetRolesQuery`, `GetBranchesQuery` y los comandos): ninguna prueba toca la red.
//
// Cubre: la lista con filtros en la dirección (tipo, rol, sucursal, estado y búsqueda), exportar CSV, el error con
// «Reintentar», «Nuevo usuario» (validación por campo, pedido exacto, contraseña mostrada una vez con «Copiar»), editar,
// restablecer la contraseña (desde la fila y desde el tablero), asignar sucursales, activar y desactivar con
// confirmación, los errores del servidor dentro del diálogo, «Roles y permisos», los botones según los permisos y la
// estadística plegada.

import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { WebApiError } from '@/1-domain/auth/errors';
import type { ISessionGateway } from '@/1-domain/ports/ISessionGateway';
import { PERMISSION_LIST, type RpcRequestOf, type RpcResponseOf } from '@/4-presentation/app/contract';
import { ROLE_PERMISSIONS, renderPanel, signedInAs, webWith, type MockWeb, type StaffRole } from '@/test-utils';
import { buildRegistry } from '../../registry';
import { PanelApp } from '../../shell/PanelApp';
import usuarios from './module';
import { UsersStat } from './UsersStat';

const REGISTRY = buildRegistry([{ source: '../modules/usuarios/module.tsx', definition: usuarios }]);

beforeAll(async () => {
  await import('./UsersPage');
}, 30_000);

afterEach(() => {
  vi.restoreAllMocks();
});

// ---------------------------------------------------------------------------------------------------- servidor simulado

type UserRecord = RpcResponseOf<'GetUsersQuery'>[number];

function user(overrides: Partial<UserRecord> & Pick<UserRecord, 'email' | 'name' | 'roles'>): UserRecord {
  return { isActive: true, hasPassword: true, mustChangePassword: false, isLocked: false, lastAccess: '2026-09-28T14:00:00Z', failedAttempts: 0, branchCodes: [], ...overrides };
}

function initialUsers(): UserRecord[] {
  return [
    user({ email: 'admin@techzone.example', name: 'Andrea Quiroga', roles: ['ADMIN'], branchCodes: ['CM', 'CB', 'SC'] }),
    user({ email: 'bodega@techzone.example', name: 'Bruno Mamani', roles: ['BODEGA'], branchCodes: ['CM'], isLocked: true, failedAttempts: 5 }),
    user({ email: 'ventas@techzone.example', name: 'Carla Rojas', roles: ['VENTAS'], branchCodes: ['CM', 'CB'], mustChangePassword: true }),
    user({ email: 'gerencia@techzone.example', name: 'Elena Vargas', roles: ['GERENCIA'] }),
    user({ email: 'pedro@techzone.example', name: 'Pedro Salas', roles: ['CAJERO'], branchCodes: ['CB'], isActive: false, lastAccess: null }),
    user({ email: 'cliente@techzone.example', name: 'Valentina Aguirre', roles: ['CLIENTE'] }),
  ];
}

const STAFF_ROLES: StaffRole[] = ['ADMIN', 'BODEGA', 'VENTAS', 'CAJERO', 'GERENCIA', 'CONSULTA'];
const ROLE_NAMES: Record<string, string> = { ADMIN: 'Administrador', BODEGA: 'Bodega', VENTAS: 'Ventas', CAJERO: 'Cajero', GERENCIA: 'Gerencia', CONSULTA: 'Consulta' };

function rolesOf(users: readonly UserRecord[]): RpcResponseOf<'GetRolesQuery'> {
  const count = (code: string) => users.filter((item) => item.roles.includes(code)).length;
  return {
    roles: [
      ...STAFF_ROLES.map((code) => ({ code, name: ROLE_NAMES[code], permissions: [...ROLE_PERMISSIONS[code]], users: count(code) })),
      { code: 'CLIENTE', name: 'Cliente web', permissions: ['account.manage', 'account.reserve'], users: count('CLIENTE') },
    ],
    permissions: PERMISSION_LIST.map((permission) => ({ item1: permission.code, item2: permission.name })),
  };
}

function branch(code: string, name: string, isActive = true): RpcResponseOf<'GetBranchesQuery'>[number] {
  return { id: `id-${code}`, code, name, isActive, isVisible: true, stockValue: 0, transfersIn: 0, transfersOut: 0, users: 1, warehouses: [code] };
}

const BRANCHES = [branch('CM', 'Casa matriz La Paz'), branch('CB', 'Sucursal Cochabamba'), branch('SC', 'Sucursal Santa Cruz'), branch('PT', 'Potosí (cerrada)', false)];

type Handler = (payload: unknown) => unknown;

/** Servidor en memoria de la pantalla: responde las operaciones del módulo y anota lo que se pidió. */
function fakeServer() {
  const users = initialUsers();
  const sent: { operation: string; payload: unknown }[] = [];
  const find = (email: string) => users.find((item) => item.email === email.trim().toLowerCase());
  const handlers: Record<string, Handler> = {
    GetUsersQuery: () => users.map((item) => ({ ...item })),
    GetRolesQuery: () => rolesOf(users),
    GetBranchesQuery: () => BRANCHES,
    SaveUserCommand: (payload) => {
      const command = payload as RpcRequestOf<'SaveUserCommand'>;
      const email = command.email.trim().toLowerCase();
      if (command.originalEmail === null) {
        if (find(email)) throw new WebApiError({ kind: 'domain', status: 422, code: 'user.duplicate', message: `Ya existe un usuario con el correo ${email}.` });
        users.push(user({ email, name: command.name, roles: [command.roleCode], branchCodes: command.branchCodes ?? ['CM'], mustChangePassword: true, lastAccess: null, isActive: command.isActive }));
        return email;
      }
      const existing = find(command.originalEmail)!;
      Object.assign(existing, { email, name: command.name, roles: [command.roleCode], isActive: command.isActive });
      return email;
    },
    ResetUserPasswordCommand: (payload) => {
      const command = payload as RpcRequestOf<'ResetUserPasswordCommand'>;
      Object.assign(find(command.email)!, { isLocked: false, failedAttempts: 0, mustChangePassword: command.mustChange ?? true, hasPassword: true });
      return true;
    },
    AssignUserBranchesCommand: (payload) => {
      const command = payload as RpcRequestOf<'AssignUserBranchesCommand'>;
      const target = find(command.email)!;
      target.branchCodes = [...command.branchCodes];
      return `✔ ${target.name} trabaja en: ${command.branchCodes.join(', ')}.`;
    },
  };
  return { users, sent, handlers };
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

/** La administradora con la sesión en memoria y el servidor simulado. */
async function openAs(role: StaffRole = 'ADMIN', query = '', server = fakeServer()) {
  const web = await signedInAs(role);
  serve(web, server);
  const view = await renderPanel(<PanelApp registry={REGISTRY} />, { web: web.services, route: `/panel/usuarios${query}`, path: '/panel/*' });
  return { web, server, location: view.location };
}

/** Una sesión con permisos elegidos (para probar qué se ofrece a quién). */
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
  const view = await renderPanel(<PanelApp registry={REGISTRY} />, { web: webWith({ session, rpc: web.backend.rpc }), route: `/panel/usuarios${query}`, path: '/panel/*' });
  return { server, location: view.location };
}

async function table(): Promise<HTMLElement> {
  const found = await screen.findByTestId('tabla', undefined, { timeout: 5000 });
  await waitFor(() => expect(within(found).queryAllByRole('row').length).toBeGreaterThan(1));
  return found;
}

function names(grid: HTMLElement): string[] {
  return within(grid)
    .getAllByRole('row')
    .filter((row) => row.hasAttribute('data-row-key'))
    .map((row) => row.getAttribute('data-row-key') ?? '');
}

function rowOf(grid: HTMLElement, email: string): HTMLElement {
  const row = grid.querySelector<HTMLElement>(`[data-row-key="${email}"]`);
  if (!row) throw new Error(`No está la fila de ${email}`);
  return row;
}

async function rowAction(grid: HTMLElement, email: string, label: string) {
  fireEvent.click(within(rowOf(grid, email)).getByRole('button', { name: /^Acciones de / }));
  fireEvent.click(await screen.findByRole('menuitem', { name: label }));
}

function stubClipboard() {
  const writeText = vi.fn(async () => undefined);
  Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
  return writeText;
}

// ---------------------------------------------------------------------------------------------------- lista

describe('Usuarios · lista', () => {
  it('muestra el personal por defecto y separa a los clientes web con la lista «Tipo» (en la dirección)', async () => {
    const { location } = await openAs();
    expect(await screen.findByRole('heading', { level: 1, name: 'Usuarios' })).toBeInTheDocument();
    const grid = await table();
    expect(names(grid)).toEqual(['admin@techzone.example', 'bodega@techzone.example', 'ventas@techzone.example', 'gerencia@techzone.example', 'pedro@techzone.example']);
    expect(screen.getByTestId('usuarios-resumen')).toHaveTextContent('5 de 6 usuarios · 1 bloqueados');
    expect(within(rowOf(grid, 'admin@techzone.example')).getByText('(usted)')).toBeInTheDocument();
    expect(rowOf(grid, 'gerencia@techzone.example')).toHaveTextContent('Todas (gerencia global)');
    expect(rowOf(grid, 'pedro@techzone.example')).toHaveTextContent('InactivoNunca ingresó');

    fireEvent.change(screen.getByRole('combobox', { name: 'Tipo' }), { target: { value: 'clientes' } });
    await waitFor(() => expect(location()).toBe('/panel/usuarios?tipo=clientes'));
    await waitFor(() => expect(names(grid)).toEqual(['cliente@techzone.example']));
    expect(rowOf(grid, 'cliente@techzone.example')).toHaveTextContent('Cliente web');

    fireEvent.change(screen.getByRole('combobox', { name: 'Tipo' }), { target: { value: '' } });
    await waitFor(() => expect(names(grid)).toHaveLength(6));
    expect(location()).toBe('/panel/usuarios?tipo=');
  });

  it('filtra por rol, sucursal y estado con listas desplegables, y busca por nombre o correo', async () => {
    const { location } = await openAs();
    const grid = await table();
    fireEvent.change(screen.getByRole('combobox', { name: 'Estado' }), { target: { value: 'bloqueado' } });
    await waitFor(() => expect(names(grid)).toEqual(['bodega@techzone.example']));
    fireEvent.click(screen.getByRole('button', { name: 'Limpiar filtros' }));
    await waitFor(() => expect(location()).toBe('/panel/usuarios'));

    fireEvent.change(screen.getByRole('combobox', { name: 'Sucursal' }), { target: { value: 'CB' } });
    await waitFor(() => expect(names(grid)).toEqual(['admin@techzone.example', 'ventas@techzone.example', 'gerencia@techzone.example', 'pedro@techzone.example']));
    fireEvent.change(screen.getByRole('combobox', { name: 'Rol' }), { target: { value: 'VENTAS' } });
    await waitFor(() => expect(names(grid)).toEqual(['ventas@techzone.example']));
    expect(location()).toBe('/panel/usuarios?sucursal=CB&rol=VENTAS');
    expect(screen.getByTestId('filtros-activos')).toHaveTextContent('2 activos');

    fireEvent.click(screen.getByRole('button', { name: 'Limpiar filtros' }));
    const search = screen.getByRole('searchbox', { name: 'Buscar' });
    fireEvent.change(search, { target: { value: 'mamani' } });
    fireEvent.keyDown(search, { key: 'Enter' });
    await waitFor(() => expect(names(grid)).toEqual(['bodega@techzone.example']));
  });

  it('abre el detalle con lo que puede hacer su rol', async () => {
    await openAs();
    const grid = await table();
    fireEvent.click(within(rowOf(grid, 'ventas@techzone.example')).getAllByRole('button')[0]);
    const panel = await screen.findByRole('dialog', { name: 'Carla Rojas' });
    expect(panel).toHaveTextContent('Temporal: debe cambiarla al ingresar');
    expect(within(panel).getByRole('heading', { name: /^Lo que puede hacer \(11 de \d+ funciones\)$/ })).toBeInTheDocument();
    expect(panel).toHaveTextContent('Abrir y cerrar caja, vender y cobrar');
    expect(within(panel).getByRole('button', { name: 'Asignar sucursales' })).toBeInTheDocument();
  });

  it('exporta las filas filtradas a CSV', async () => {
    const created: Blob[] = [];
    vi.spyOn(URL, 'createObjectURL').mockImplementation((blob) => {
      created.push(blob as Blob);
      return 'blob:prueba';
    });
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    await openAs('ADMIN', '?estado=bloqueado');
    await table();
    fireEvent.click(screen.getByRole('button', { name: 'Exportar CSV' }));
    expect(await screen.findByText('Exportación lista')).toBeInTheDocument();
    const lines = (await created[0].text()).replace(/^﻿/, '').trim().split('\r\n');
    expect(lines[0]).toBe('"Nombre";"Correo";"Tipo";"Rol";"Sucursales";"Estado";"Último ingreso";"Intentos fallidos";"Tiene contraseña";"Debe cambiarla"');
    expect(lines.slice(1)).toEqual(['"Bruno Mamani";"bodega@techzone.example";"Personal";"Bodega";"CM";"Bloqueado";"28/09/2026 10:00";5;"Sí";"No"']);
  });

  it('si la lista no carga muestra el error con «Reintentar» y se recupera', async () => {
    const server = fakeServer();
    const original = server.handlers.GetUsersQuery;
    let failures = 1;
    server.handlers.GetUsersQuery = (payload) => {
      if (failures-- > 0) throw new WebApiError({ kind: 'network', message: 'Sin conexión' });
      return original(payload);
    };
    await openAs('ADMIN', '', server);
    const error = await screen.findByTestId('estado-error', undefined, { timeout: 5000 });
    fireEvent.click(within(error).getByRole('button', { name: 'Reintentar' }));
    expect(names(await table())).toHaveLength(5);
  });
});

// ---------------------------------------------------------------------------------------------------- comandos

describe('Usuarios · alta y edición', () => {
  it('«Nuevo usuario» valida por campo, envía SaveUserCommand exacto y muestra la contraseña UNA vez con «Copiar»', async () => {
    const writeText = stubClipboard();
    const { server } = await openAs();
    const grid = await table();
    fireEvent.click(screen.getByRole('button', { name: 'Nuevo usuario' }));
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo usuario' });

    // La sucursal activa (CM) viene marcada; la inactiva no se ofrece.
    expect(within(dialog).getByRole('checkbox', { name: 'CM · Casa matriz La Paz' })).toBeChecked();
    expect(within(dialog).queryByRole('checkbox', { name: /Potosí/ })).not.toBeInTheDocument();
    expect(within(dialog).getByRole('combobox', { name: /^Rol/ })).toHaveValue('VENTAS');

    fireEvent.click(within(dialog).getByRole('button', { name: 'Crear usuario' }));
    expect(await within(dialog).findByText('Indique el nombre completo.')).toBeInTheDocument();
    expect(within(dialog).getByText('Indique un correo válido, por ejemplo nombre@empresa.com.')).toBeInTheDocument();
    expect(sentOf(server, 'SaveUserCommand')).toEqual([]);

    fireEvent.change(within(dialog).getByLabelText(/^Nombre completo/), { target: { value: 'Nora Nina' } });
    fireEvent.change(within(dialog).getByLabelText(/^Correo/), { target: { value: 'Nora@TechZone.example' } });
    fireEvent.change(within(dialog).getByRole('combobox', { name: /^Rol/ }), { target: { value: 'CAJERO' } });
    fireEvent.click(within(dialog).getByRole('checkbox', { name: 'SC · Sucursal Santa Cruz' }));
    const password = (within(dialog).getByLabelText(/^Contraseña inicial/) as HTMLInputElement).value;
    expect(password).toMatch(/^[A-Za-z]{5}-[2-9]{4}$/);
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Crear usuario' }));
    });

    expect(sentOf(server, 'SaveUserCommand')).toEqual([
      { originalEmail: null, email: 'Nora@TechZone.example', name: 'Nora Nina', roleCode: 'CAJERO', isActive: true, newPassword: password, branchCodes: ['CM', 'SC'] },
    ]);
    expect(sentOf(server, 'ResetUserPasswordCommand')).toEqual([]);
    const secret = await screen.findByRole('dialog', { name: 'Usuario creado' });
    expect(secret).toHaveTextContent('Nora Nina ya puede ingresar con esta contraseña inicial y deberá cambiarla la primera vez.');
    expect(within(secret).getByTestId('secreto')).toHaveValue(password);
    expect(secret).toHaveTextContent('Se muestra una sola vez');
    await act(async () => {
      fireEvent.click(within(secret).getByRole('button', { name: 'Copiar' }));
    });
    expect(writeText).toHaveBeenCalledWith(password);
    expect(await within(secret).findByRole('button', { name: 'Copiada' })).toBeInTheDocument();
    fireEvent.click(within(secret).getByRole('button', { name: 'Listo' }));
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Usuario creado' })).not.toBeInTheDocument());
    await waitFor(() => expect(names(grid)).toContain('nora@techzone.example'));
  });

  it('sin «Pedir que la cambie» envía además ResetUserPasswordCommand con mustChange false', async () => {
    const { server } = await openAs();
    await table();
    fireEvent.click(screen.getByRole('button', { name: 'Nuevo usuario' }));
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo usuario' });
    fireEvent.change(within(dialog).getByLabelText(/^Nombre completo/), { target: { value: 'Nora Nina' } });
    fireEvent.change(within(dialog).getByLabelText(/^Correo/), { target: { value: 'nora@techzone.example' } });
    fireEvent.change(within(dialog).getByLabelText(/^Contraseña inicial/), { target: { value: 'Bienvenida2026' } });
    fireEvent.click(within(dialog).getByRole('checkbox', { name: 'Pedir que la cambie al ingresar' }));
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Crear usuario' }));
    });
    expect(sentOf(server, 'ResetUserPasswordCommand')).toEqual([{ email: 'nora@techzone.example', newPassword: 'Bienvenida2026', mustChange: false }]);
    const secret = await screen.findByRole('dialog', { name: 'Usuario creado' });
    expect(secret).toHaveTextContent('Nora Nina ya puede ingresar con esta contraseña.');
    expect(server.users.find((item) => item.email === 'nora@techzone.example')?.mustChangePassword).toBe(false);
  });

  it('el error del servidor se muestra dentro del diálogo, que sigue abierto para corregir', async () => {
    await openAs();
    await table();
    fireEvent.click(screen.getByRole('button', { name: 'Nuevo usuario' }));
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo usuario' });
    fireEvent.change(within(dialog).getByLabelText(/^Nombre completo/), { target: { value: 'Otra Carla' } });
    fireEvent.change(within(dialog).getByLabelText(/^Correo/), { target: { value: 'ventas@techzone.example' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Crear usuario' }));
    });
    expect(await within(dialog).findByText('Ya existe un usuario con el correo ventas@techzone.example.')).toBeInTheDocument();
    expect(screen.queryByRole('dialog', { name: 'Usuario creado' })).not.toBeInTheDocument();
  });

  it('«Editar» envía SaveUserCommand con el correo original y sin tocar contraseña ni sucursales', async () => {
    const { server } = await openAs();
    const grid = await table();
    await rowAction(grid, 'ventas@techzone.example', 'Editar');
    const dialog = await screen.findByRole('dialog', { name: 'Editar usuario' });
    expect(within(dialog).queryByLabelText(/^Contraseña inicial/)).not.toBeInTheDocument();
    fireEvent.change(within(dialog).getByRole('combobox', { name: /^Rol/ }), { target: { value: 'CAJERO' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar cambios' }));
    });
    expect(sentOf(server, 'SaveUserCommand')).toEqual([
      { originalEmail: 'ventas@techzone.example', email: 'ventas@techzone.example', name: 'Carla Rojas', roleCode: 'CAJERO', isActive: true, newPassword: null, branchCodes: null },
    ]);
    expect(await screen.findByText('Usuario actualizado')).toBeInTheDocument();
    await waitFor(() => expect(rowOf(grid, 'ventas@techzone.example')).toHaveTextContent('Cajero'));
  });

  it('nadie puede cambiarse el rol ni desactivarse a sí mismo', async () => {
    await openAs();
    const grid = await table();
    fireEvent.click(within(rowOf(grid, 'admin@techzone.example')).getByRole('button', { name: /^Acciones de / }));
    const menu = await screen.findByTestId('menu-acciones');
    expect(within(menu).queryByRole('menuitem', { name: 'Desactivar' })).not.toBeInTheDocument();
    fireEvent.click(within(menu).getByRole('menuitem', { name: 'Editar' }));
    const dialog = await screen.findByRole('dialog', { name: 'Editar usuario' });
    expect(within(dialog).getByRole('combobox', { name: /^Rol/ })).toBeDisabled();
    expect(within(dialog).getByRole('switch', { name: 'Usuario activo (puede ingresar)' })).toBeDisabled();
    expect(dialog).toHaveTextContent('No puede cambiar su propio rol: pídaselo a otro administrador.');
  });

  it('desactivar pide confirmación y envía el mismo usuario con isActive false', async () => {
    const { server } = await openAs();
    const grid = await table();
    await rowAction(grid, 'ventas@techzone.example', 'Desactivar');
    const confirm = await screen.findByRole('alertdialog', { name: '¿Desactivar a Carla Rojas?' });
    await act(async () => {
      fireEvent.click(within(confirm).getByRole('button', { name: 'Desactivar' }));
    });
    expect(sentOf(server, 'SaveUserCommand')).toEqual([
      { originalEmail: 'ventas@techzone.example', email: 'ventas@techzone.example', name: 'Carla Rojas', roleCode: 'VENTAS', isActive: false, newPassword: null, branchCodes: null },
    ]);
    expect(await screen.findByText('Carla Rojas quedó desactivado: ya no puede ingresar')).toBeInTheDocument();
    await waitFor(() => expect(rowOf(grid, 'ventas@techzone.example')).toHaveTextContent('Inactivo'));
  });
});

describe('Usuarios · contraseñas y sucursales', () => {
  it('restablecer desde la fila genera la temporal, la envía y la muestra UNA vez', async () => {
    const { server } = await openAs();
    const grid = await table();
    await rowAction(grid, 'bodega@techzone.example', 'Restablecer la contraseña');
    const dialog = await screen.findByRole('dialog', { name: 'Restablecer la contraseña' });
    expect(dialog).toHaveTextContent('Bruno Mamani · bodega@techzone.example');
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Generar contraseña' }));
    });
    const [sent] = sentOf(server, 'ResetUserPasswordCommand') as RpcRequestOf<'ResetUserPasswordCommand'>[];
    expect(sent).toEqual({ email: 'bodega@techzone.example', newPassword: expect.stringMatching(/^[A-Za-z]{5}-[2-9]{4}$/), mustChange: true });
    const secret = await screen.findByRole('dialog', { name: 'Contraseña temporal' });
    expect(within(secret).getByTestId('secreto')).toHaveValue(sent.newPassword);
    expect(secret).toHaveTextContent('La cuenta de Bruno Mamani quedó desbloqueada. Deberá cambiar esta contraseña al ingresar.');
    await waitFor(() => expect(rowOf(grid, 'bodega@techzone.example')).not.toHaveTextContent('Bloqueado'));
  });

  it('el botón del tablero (`?accion=restablecer`) pide elegir a la persona en una lista con búsqueda', async () => {
    const { server, location } = await openAs('ADMIN', '?accion=restablecer');
    const dialog = await screen.findByRole('dialog', { name: 'Restablecer la contraseña' }, { timeout: 5000 });
    await waitFor(() => expect(location()).toBe('/panel/usuarios'));
    fireEvent.click(within(dialog).getByRole('button', { name: 'Generar contraseña' }));
    expect(await within(dialog).findByText('Elija a la persona.')).toBeInTheDocument();
    const person = within(dialog).getByRole('combobox', { name: /^Persona/ });
    fireEvent.change(person, { target: { value: 'carla' } });
    fireEvent.click(await within(dialog).findByRole('option', { name: /Carla Rojas/ }));
    fireEvent.click(within(dialog).getByRole('checkbox', { name: 'Pedir que la cambie al ingresar' }));
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Generar contraseña' }));
    });
    expect(sentOf(server, 'ResetUserPasswordCommand')).toEqual([{ email: 'ventas@techzone.example', newPassword: expect.any(String), mustChange: false }]);
    expect(await screen.findByRole('dialog', { name: 'Contraseña temporal' })).toHaveTextContent('quedó desbloqueada con esta contraseña');
  });

  it('el botón del tablero «Nuevo usuario» (`?accion=nuevo`) abre el formulario', async () => {
    const { location } = await openAs('ADMIN', '?accion=nuevo');
    expect(await screen.findByRole('dialog', { name: 'Nuevo usuario' }, { timeout: 5000 })).toBeInTheDocument();
    await waitFor(() => expect(location()).toBe('/panel/usuarios'));
  });

  it('asignar sucursales usa casillas, exige al menos una y envía AssignUserBranchesCommand', async () => {
    const { server } = await openAs();
    const grid = await table();
    await rowAction(grid, 'ventas@techzone.example', 'Asignar sucursales');
    const dialog = await screen.findByRole('dialog', { name: 'Asignar sucursales' });
    fireEvent.click(within(dialog).getByRole('checkbox', { name: 'CM · Casa matriz La Paz' }));
    fireEvent.click(within(dialog).getByRole('checkbox', { name: 'CB · Sucursal Cochabamba' }));
    expect(within(dialog).getByRole('checkbox', { name: 'PT · Potosí (cerrada)' })).toBeDisabled();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar sucursales' }));
    expect(await within(dialog).findByText('Elija al menos una sucursal.')).toBeInTheDocument();
    expect(sentOf(server, 'AssignUserBranchesCommand')).toEqual([]);
    fireEvent.click(within(dialog).getByRole('checkbox', { name: 'SC · Sucursal Santa Cruz' }));
    fireEvent.click(within(dialog).getByRole('checkbox', { name: 'CM · Casa matriz La Paz' }));
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar sucursales' }));
    });
    expect(sentOf(server, 'AssignUserBranchesCommand')).toEqual([{ email: 'ventas@techzone.example', branchCodes: ['CM', 'SC'] }]);
    expect(await screen.findByText('Carla Rojas trabaja en: CM, SC.')).toBeInTheDocument();
    await waitFor(() => expect(rowOf(grid, 'ventas@techzone.example')).toHaveTextContent('CM · SC'));
  });
});

// ---------------------------------------------------------------------------------------------------- roles y permisos

describe('Usuarios · roles y permisos', () => {
  it('explica cada rol, busca una función y «Ver sus usuarios» abre la lista filtrada', async () => {
    const { location } = await openAs();
    await table();
    fireEvent.click(screen.getByRole('tab', { name: 'Roles y permisos' }));
    await waitFor(() => expect(location()).toBe('/panel/usuarios?pestana=roles'));
    const roles = await screen.findByTestId('roles');
    expect(within(roles).getByRole('heading', { level: 2, name: 'Ventas' })).toBeInTheDocument();
    expect(roles).toHaveTextContent('Vende en la caja, atiende clientes, arma y reserva PC');
    expect(screen.getByText('Solo lectura')).toBeInTheDocument();

    const search = screen.getByRole('searchbox', { name: 'Buscar una función' });
    fireEvent.change(search, { target: { value: 'anular revertir' } });
    fireEvent.keyDown(search, { key: 'Enter' });
    await waitFor(() => expect(location()).toBe('/panel/usuarios?pestana=roles&q=anular+revertir'));
    const gerencia = within(roles).getByRole('heading', { level: 2, name: 'Gerencia' }).closest('section')!;
    await waitFor(() => expect(gerencia).toHaveTextContent('Puede: Anular y revertir documentos fiscales'));
    const ventas = within(roles).getByRole('heading', { level: 2, name: 'Ventas' }).closest('section')!;
    expect(ventas).toHaveTextContent('Ninguna de sus funciones coincide con los filtros.');
    expect(within(ventas).getByRole('button', { name: /Ver lo que no puede \(1 de las buscadas\)/ })).toBeInTheDocument();

    fireEvent.click(within(ventas).getByRole('button', { name: 'Ver sus usuarios (1)' }));
    await waitFor(() => expect(location()).toBe('/panel/usuarios?rol=VENTAS'));
    expect(names(await table())).toEqual(['ventas@techzone.example']);
  });
});

// ---------------------------------------------------------------------------------------------------- permisos

describe('Usuarios · permisos', () => {
  it('un rol sin `iam.users.manage` (gerencia) ve «No tiene acceso a esta pantalla»', async () => {
    await openAs('GERENCIA');
    expect(await screen.findByTestId('sin-acceso')).toHaveTextContent('Falta el permiso «Administrar usuarios, roles y permisos»');
  });

  it('solo se ofrece lo que la sesión puede hacer: sin sucursales ni auditoría no hay «Asignar sucursales» ni «Ver su actividad»', async () => {
    await openWithPermissions(['iam.users.manage', 'inventory.stock.view']);
    const grid = await table();
    fireEvent.click(within(rowOf(grid, 'ventas@techzone.example')).getByRole('button', { name: /^Acciones de / }));
    const menu = await screen.findByTestId('menu-acciones');
    expect(within(menu).getByRole('menuitem', { name: 'Editar' })).toBeInTheDocument();
    expect(within(menu).queryByRole('menuitem', { name: 'Asignar sucursales' })).not.toBeInTheDocument();
    expect(within(menu).queryByRole('menuitem', { name: 'Ver su actividad' })).not.toBeInTheDocument();
  });

  it('con la auditoría, «Ver su actividad» lleva a Actividad filtrada por esa persona', async () => {
    const { location } = await openWithPermissions(['iam.users.manage', 'inventory.stock.view', 'iam.audit.view']);
    const grid = await table();
    await rowAction(grid, 'ventas@techzone.example', 'Ver su actividad');
    await waitFor(() => expect(location()).toBe('/panel/actividad?usuario=ventas%40techzone.example'));
  });
});

describe('Usuarios · estadística plegada', () => {
  it('resume el personal activo, los bloqueados y enlaza a la lista de bloqueados', async () => {
    const web = await signedInAs('ADMIN');
    serve(web, fakeServer());
    await renderPanel(<UsersStat />, { web: web.services });
    const stat = await screen.findByTestId('resumen-usuarios');
    await waitFor(() => expect(stat).toHaveTextContent('Personal activo4'));
    expect(stat).toHaveTextContent('Bloqueados1');
    expect(stat).toHaveTextContent('Deben cambiar la contraseña1');
    expect(within(stat).getByRole('link', { name: 'Ver los bloqueados' })).toHaveAttribute('href', '/panel/usuarios?tipo=&estado=bloqueado');
  });
});
