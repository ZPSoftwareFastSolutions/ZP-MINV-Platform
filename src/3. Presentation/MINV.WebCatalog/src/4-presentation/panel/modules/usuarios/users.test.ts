// Módulo «Usuarios» · funciones puras: tipo y estado de cada cuenta, filas de la tabla, filtros (tipo, rol, sucursal,
// estado y búsqueda sin acentos), opciones de las listas, formularios (validación y pedido EXACTO de SaveUserCommand),
// contraseña temporal, resumen de la estadística y la pestaña «Roles y permisos» (áreas, lo que puede y lo que no, CSV).

import { describe, expect, it } from 'vitest';
import { PERMISSION_LIST } from '@/4-presentation/app/contract';
import { buildCsv } from '@/4-presentation/panel/lib';
import {
  USER_CSV_COLUMNS,
  USER_FILTERS,
  activationPayload,
  areaOf,
  branchChoicesOf,
  editUserPayload,
  filterUsers,
  formRoleOptions,
  generateTemporaryPassword,
  initialUserForm,
  kindOf,
  kindOptions,
  matrixCsvColumns,
  newUserPayload,
  orderedCodes,
  plainMessage,
  roleChoices,
  roleViewOf,
  roleViews,
  statusOfUser,
  temporaryPasswordProblem,
  toUserItems,
  userFormProblems,
  usersSummary,
  type RolesData,
  type UserFormValues,
  type UserRecord,
} from './users';

function user(overrides: Partial<UserRecord> & Pick<UserRecord, 'email' | 'name' | 'roles'>): UserRecord {
  return { isActive: true, hasPassword: true, mustChangePassword: false, isLocked: false, lastAccess: null, failedAttempts: 0, branchCodes: [], ...overrides };
}

const USERS: UserRecord[] = [
  user({ email: 'admin@techzone.example', name: 'Andrea Quiroga', roles: ['ADMIN'], branchCodes: ['CM', 'CB', 'SC'], lastAccess: '2026-09-29T13:00:00Z' }),
  user({ email: 'bodega@techzone.example', name: 'Bruno Mamani', roles: ['BODEGA'], branchCodes: ['CM'], isLocked: true, failedAttempts: 5 }),
  user({ email: 'ventas@techzone.example', name: 'Carla Rojas', roles: ['VENTAS'], branchCodes: ['CM', 'CB'], mustChangePassword: true, lastAccess: '2026-09-29T02:00:00Z' }),
  user({ email: 'gerencia@techzone.example', name: 'Elena Vargas', roles: ['GERENCIA'] }),
  user({ email: 'pedro@techzone.example', name: 'Pedro Salas', roles: ['CAJERO'], branchCodes: ['CB'], isActive: false }),
  user({ email: 'cliente@techzone.example', name: 'Valentina Aguirre', roles: ['CLIENTE'] }),
  user({ email: 'tienda-web@techzone.example', name: 'Tienda web', roles: ['TIENDA_WEB'], branchCodes: ['CM'], hasPassword: false }),
];

const ROLES: RolesData = {
  roles: [
    { code: 'ADMIN', name: 'Administrador', permissions: PERMISSION_LIST.map((permission) => permission.code), users: 1 },
    { code: 'GERENCIA', name: 'Gerencia', permissions: ['corporate.branches.all', 'billing.void', 'reports.view'], users: 1 },
    { code: 'VENTAS', name: 'Ventas', permissions: ['sales.view', 'sales.pos.operate', 'billing.issue'], users: 1 },
    { code: 'CLIENTE', name: 'Cliente web', permissions: ['account.manage'], users: 1 },
  ],
  permissions: PERMISSION_LIST.map((permission) => ({ item1: permission.code, item2: permission.name })),
};

describe('usuarios · tipo, estado y filas', () => {
  it('un cliente web es quien tiene SOLO el rol CLIENTE; el resto es personal', () => {
    expect(kindOf({ roles: ['CLIENTE'] })).toBe('clientes');
    expect(kindOf({ roles: ['CLIENTE', 'VENTAS'] })).toBe('personal');
    expect(kindOf({ roles: [] })).toBe('personal');
  });

  it('el estado sigue al escritorio: inactivo › bloqueado › sin contraseña › debe cambiarla › activo', () => {
    expect(USERS.map(statusOfUser)).toEqual(['activo', 'bloqueado', 'cambiar', 'activo', 'inactivo', 'activo', 'sin-clave']);
    expect(statusOfUser(user({ email: 'x@y.z', name: 'X', roles: ['VENTAS'], isActive: false, isLocked: true }))).toBe('inactivo');
  });

  it('cada fila dice su rol, sus sucursales (la gerencia global ve todas) y si es la persona de la sesión', () => {
    const items = toUserItems(USERS, ROLES, 'ADMIN@techzone.example');
    expect(items.map((item) => [item.key, item.roleText, item.branchesText, item.isSelf])).toEqual([
      ['admin@techzone.example', 'Administrador', 'Todas (gerencia global)', true],
      ['bodega@techzone.example', 'Bodega', 'CM', false],
      ['ventas@techzone.example', 'Ventas', 'CM · CB', false],
      ['gerencia@techzone.example', 'Gerencia', 'Todas (gerencia global)', false],
      ['pedro@techzone.example', 'Cajero', 'CB', false],
      ['cliente@techzone.example', 'Cliente web', '', false],
      ['tienda-web@techzone.example', 'Tienda web', 'CM', false],
    ]);
    // Sin los roles del servidor todavía: nombres del contrato y solo las sucursales asignadas.
    expect(toUserItems(USERS.slice(3, 4), undefined, null)[0]).toMatchObject({ roleText: 'Gerencia', allBranches: false, branchesText: 'Sin sucursal' });
  });

  it('filtra por tipo (por defecto el personal), rol, sucursal, estado y búsqueda sin acentos', () => {
    const items = toUserItems(USERS, ROLES, null);
    const names = (filters: Partial<typeof USER_FILTERS>) => filterUsers(items, { ...USER_FILTERS, ...filters }).map((item) => item.record.name);
    expect(names({})).not.toContain('Valentina Aguirre');
    expect(names({})).toHaveLength(6);
    expect(names({ tipo: 'clientes' })).toEqual(['Valentina Aguirre']);
    expect(names({ tipo: '' })).toHaveLength(7);
    expect(names({ rol: 'VENTAS' })).toEqual(['Carla Rojas']);
    // En Cochabamba: las asignadas y quienes ven todas las sucursales.
    expect(names({ sucursal: 'CB' })).toEqual(['Andrea Quiroga', 'Carla Rojas', 'Elena Vargas', 'Pedro Salas']);
    expect(names({ estado: 'bloqueado' })).toEqual(['Bruno Mamani']);
    expect(names({ q: 'MAMANÍ' })).toEqual(['Bruno Mamani']);
    expect(names({ q: 'gerencia' })).toEqual(['Elena Vargas']);
  });

  it('las listas: tipo con cuántos hay, sucursales del directorio (o de las asignaciones) y roles del formulario', () => {
    const items = toUserItems(USERS, ROLES, null);
    expect(kindOptions(items)).toEqual([
      { value: 'personal', label: 'Personal (6)' },
      { value: 'clientes', label: 'Clientes web (1)' },
    ]);
    expect(branchChoicesOf(undefined, USERS)).toEqual([
      { code: 'CB', name: 'CB', isActive: true },
      { code: 'CM', name: 'CM', isActive: true },
      { code: 'SC', name: 'SC', isActive: true },
    ]);
    const choices = roleChoices(ROLES);
    // El formulario no ofrece los roles de la tienda web (salvo el que ya tiene la persona).
    expect(formRoleOptions(choices, null).map((option) => option.value)).toEqual(['ADMIN', 'GERENCIA', 'VENTAS']);
    expect(formRoleOptions(choices, 'TIENDA_WEB').map((option) => option.value)).toEqual(['ADMIN', 'GERENCIA', 'VENTAS', 'TIENDA_WEB']);
    expect(formRoleOptions(choices, null)[2].label).toBe('Ventas · ventas, clientes y salidas');
    // Sin la respuesta del servidor, los roles del contrato.
    expect(roleChoices(undefined).map((role) => role.code)).toContain('CONSULTA');
  });

  it('el CSV lleva nombre, correo, tipo, rol, sucursales, estado, último ingreso y la contraseña', () => {
    const items = toUserItems(USERS, ROLES, null);
    const [header, first] = buildCsv(USER_CSV_COLUMNS, items.slice(2, 3), { bom: false }).split('\r\n');
    expect(header).toBe('"Nombre";"Correo";"Tipo";"Rol";"Sucursales";"Estado";"Último ingreso";"Intentos fallidos";"Tiene contraseña";"Debe cambiarla"');
    expect(first).toBe('"Carla Rojas";"ventas@techzone.example";"Personal";"Ventas";"CM · CB";"Debe cambiar la contraseña";"28/09/2026 22:00";0;"Sí";"Sí"');
  });
});

describe('usuarios · formularios y pedidos', () => {
  const values: UserFormValues = {
    name: '  Nora Nina ',
    email: ' nora@techzone.example ',
    roleCode: 'CAJERO',
    isActive: true,
    branchCodes: ['CM', 'SC'],
    password: 'kPxRt-4827',
    mustChange: true,
  };

  it('valida por campo con las reglas del servidor', () => {
    expect(userFormProblems({ ...values, name: 'N', email: 'nora@', roleCode: '', branchCodes: [], password: 'corta' }, { isNew: true, branchesRequired: true })).toEqual({
      name: 'Indique el nombre completo.',
      email: 'Indique un correo válido, por ejemplo nombre@empresa.com.',
      roleCode: 'Elija el rol.',
      branchCodes: 'Elija al menos una sucursal.',
      password: 'Use entre 8 y 128 caracteres.',
    });
    expect(userFormProblems(values, { isNew: true, branchesRequired: true })).toEqual({});
    // Al editar no se pide contraseña ni sucursales.
    expect(userFormProblems({ ...values, password: '', branchCodes: [] }, { isNew: false, branchesRequired: false })).toEqual({});
  });

  it('el pedido de un usuario nuevo lleva TODOS los datos (sucursales en null si no se pueden elegir)', () => {
    expect(newUserPayload(values, true)).toEqual({
      originalEmail: null,
      email: 'nora@techzone.example',
      name: 'Nora Nina',
      roleCode: 'CAJERO',
      isActive: true,
      newPassword: 'kPxRt-4827',
      branchCodes: ['CM', 'SC'],
    });
    expect(newUserPayload(values, false).branchCodes).toBeNull();
  });

  it('editar no cambia la contraseña ni las sucursales; activar y desactivar repiten el usuario', () => {
    expect(editUserPayload({ ...values, roleCode: 'VENTAS' }, USERS[2])).toEqual({
      originalEmail: 'ventas@techzone.example',
      email: 'nora@techzone.example',
      name: 'Nora Nina',
      roleCode: 'VENTAS',
      isActive: true,
      newPassword: null,
      branchCodes: null,
    });
    expect(activationPayload(USERS[4], true)).toEqual({
      originalEmail: 'pedro@techzone.example',
      email: 'pedro@techzone.example',
      name: 'Pedro Salas',
      roleCode: 'CAJERO',
      isActive: true,
      newPassword: null,
      branchCodes: null,
    });
    expect(activationPayload(user({ email: 'sin@rol.example', name: 'Sin rol', roles: [] }), false)).toBeNull();
  });

  it('el formulario nuevo propone Ventas, la sucursal activa y una contraseña que cumple las reglas', () => {
    const initial = initialUserForm(null, roleChoices(ROLES), 'CB');
    expect(initial).toMatchObject({ name: '', email: '', roleCode: 'VENTAS', isActive: true, branchCodes: ['CB'], mustChange: true });
    expect(temporaryPasswordProblem(initial.password)).toBeNull();
    const item = toUserItems(USERS, ROLES, null)[2];
    expect(initialUserForm(item, roleChoices(ROLES), 'CB')).toMatchObject({ name: 'Carla Rojas', roleCode: 'VENTAS', branchCodes: ['CM', 'CB'], password: '' });
  });

  it('ordena las sucursales como la lista, quita la marca del servidor y cuida la contraseña temporal', () => {
    expect(orderedCodes(['SC', 'XX', 'CM'], branchChoicesOf(undefined, USERS))).toEqual(['CM', 'SC', 'XX']);
    expect(plainMessage('✔ Carla Rojas trabaja en: CM.')).toBe('Carla Rojas trabaja en: CM.');
    expect(temporaryPasswordProblem('soloLetras')).toBe('Combine letras y números.');
    expect(temporaryPasswordProblem('Temporal2026')).toBeNull();
    let seed = 3;
    expect(generateTemporaryPassword((max) => (seed = (seed * 17 + 5) % 211) % max)).toMatch(/^[A-Za-z]{5}-[2-9]{4}$/);
  });
});

describe('usuarios · resumen y roles', () => {
  it('resume el personal activo, bloqueados, contraseñas por cambiar, ingresos de hoy (La Paz) y personal por rol', () => {
    const summary = usersSummary(USERS, new Date('2026-09-29T20:00:00Z'));
    expect(summary).toMatchObject({ staff: 6, staffActive: 5, customers: 1, locked: 1, mustChange: 1, today: 1 });
    expect(summary.byRole[0]).toEqual({ label: 'Administrador', value: 1 });
    expect(summary.byRole.map((row) => row.label)).not.toContain('Cajero');
  });

  it('agrupa cada permiso en su área', () => {
    expect(areaOf('sales.pos.operate')).toBe('ventas');
    expect(areaOf('sales.pcbuild.manage')).toBe('tecnologia');
    expect(areaOf('inventory.transfers.manage')).toBe('sucursales');
    expect(areaOf('inventory.stock.view')).toBe('inventario');
    expect(areaOf('billing.void')).toBe('facturacion');
    expect(areaOf('account.manage')).toBe('tienda');
    expect(areaOf('iam.users.manage')).toBe('administracion');
  });

  it('explica cada rol: lo que puede (por área) y, con una búsqueda, quién puede y quién no', () => {
    const all = roleViews(ROLES, { rol: '', area: '', q: '' });
    expect(all.map((view) => [view.code, view.granted, view.total])).toEqual([
      ['ADMIN', PERMISSION_LIST.length, PERMISSION_LIST.length],
      ['GERENCIA', 3, PERMISSION_LIST.length],
      ['VENTAS', 3, PERMISSION_LIST.length],
      ['CLIENTE', 1, PERMISSION_LIST.length],
    ]);
    const ventas = roleViewOf(ROLES, 'VENTAS')!;
    expect(ventas.groups.map((group) => group.label)).toEqual(['Ventas y caja', 'Facturación']);
    expect(ventas.description).toBe('Vende en la caja, atiende clientes, arma y reserva PC, registra salidas de stock y emite facturas.');
    const anular = roleViews(ROLES, { rol: '', area: '', q: 'anular documentos' });
    expect(anular.find((view) => view.code === 'GERENCIA')!.groups.flatMap((group) => group.items.map((item) => item.code))).toEqual(['billing.void']);
    expect(anular.find((view) => view.code === 'VENTAS')!.missing.map((item) => item.code)).toEqual(['billing.void']);
    expect(roleViews(ROLES, { rol: 'VENTAS', area: 'facturacion', q: '' })[0].groups[0].items.map((item) => item.code)).toEqual(['billing.issue']);
  });

  it('la matriz CSV tiene una columna Sí/No por rol', () => {
    const csv = buildCsv(matrixCsvColumns(ROLES.roles.slice(1, 3)), [{ code: 'billing.void', name: 'Anular', area: 'facturacion' }], { bom: false });
    expect(csv.split('\r\n').slice(0, 2)).toEqual(['"Área";"Función";"Código";"Gerencia";"Ventas"', '"Facturación";"Anular";"billing.void";"Sí";"No"']);
  });
});
