// Módulo «Usuarios» · funciones puras (sin React): cómo se presenta cada usuario (tipo, estado, rol y sucursales), los
// filtros de la lista, las columnas del CSV, los formularios (validación y pedido EXACTO de `SaveUserCommand`), la
// contraseña temporal, el resumen para la estadística plegada y la pestaña de solo lectura «Roles y permisos».
//
// Los tipos del servidor SALEN DEL CONTRATO (regla P-07): se derivan del nombre de la operación. Los textos de estados y
// roles siguen al escritorio (UsersViewModel: UserItem.StatusText y UserEditor.Describe).

import { ROLE_LIST, permissionName, roleName, type RpcRequestOf, type RpcResponseOf } from '@/4-presentation/app/contract';
import { defineStatuses, statusOf, type SelectOption } from '@/4-presentation/panel/kit';
import { formatDateTime, laPazToday, matchesSearch, toDate, toIsoDate, type CsvColumn } from '@/4-presentation/panel/lib';

/** Una fila de `GetUsersQuery` tal como la manda el servidor. */
export type UserRecord = RpcResponseOf<'GetUsersQuery'>[number];
/** Roles con sus permisos y la lista de permisos (`GetRolesQuery`). */
export type RolesData = RpcResponseOf<'GetRolesQuery'>;
/** Un rol de `GetRolesQuery`. */
export type RoleRecord = RolesData['roles'][number];
/** Una sucursal de `GetBranchesQuery`. */
export type BranchRecord = RpcResponseOf<'GetBranchesQuery'>[number];
/** Pedido de `SaveUserCommand`. */
export type SaveUserPayload = RpcRequestOf<'SaveUserCommand'>;

export const ADMIN_ROLE = 'ADMIN';
/** Cliente con cuenta en la tienda web (se registra solo; regla P-03). */
export const CUSTOMER_ROLE = 'CLIENTE';
/** Usuario técnico de la tienda web (regla S-02). */
export const STOREFRONT_ROLE = 'TIENDA_WEB';
/** Rol que se propone al crear un usuario (como el escritorio). */
export const DEFAULT_ROLE = 'VENTAS';
/** Permiso de la gerencia global: ve y opera todas las sucursales (regla B-01). */
export const ALL_BRANCHES_PERMISSION = 'corporate.branches.all';

// ---------------------------------------------------------------------------------------------------- roles

export interface RoleChoice {
  code: string;
  name: string;
}

/** Roles conocidos: los del servidor (`GetRolesQuery`) o, mientras llegan, los del contrato. */
export function roleChoices(roles: RolesData | undefined): RoleChoice[] {
  const list: readonly RoleChoice[] = roles?.roles ?? ROLE_LIST;
  return list.map((role) => ({ code: role.code, name: role.name || roleName(role.code) }));
}

/** Nombre de un rol con la lista del servidor (o el del contrato si no está). */
export function roleLabel(code: string, choices: readonly RoleChoice[]): string {
  return choices.find((role) => role.code === code)?.name ?? roleName(code);
}

/** Qué hace cada rol en pocas palabras (la lista de funciones exacta sale del servidor). */
const ROLE_TEXTS: Readonly<Record<string, { short: string; long: string }>> = {
  ADMIN: {
    short: 'todo el sistema',
    long: 'Tiene acceso a todo: usuarios, configuración, integraciones, ventas, inventario, compras, facturación, reportes y contabilidad.',
  },
  GERENCIA: {
    short: 'reportes, contabilidad y compras',
    long: 'Ve todas las sucursales: reportes, contabilidad, compras, transferencias, facturas, series y garantías. No cobra en la caja.',
  },
  BODEGA: {
    short: 'stock, conteos y compras',
    long: 'Registra entradas y ajustes, hace tomas físicas, compras y transferencias, y lleva las series y las garantías.',
  },
  VENTAS: {
    short: 'ventas, clientes y salidas',
    long: 'Vende en la caja, atiende clientes, arma y reserva PC, registra salidas de stock y emite facturas.',
  },
  CAJERO: { short: 'punto de venta y caja', long: 'Abre y cierra la caja, cobra ventas, registra clientes y reservas y emite facturas.' },
  CONSULTA: { short: 'solo consulta', long: 'Solo consulta stock, reportes, facturas y series: no cambia nada.' },
  TIENDA_WEB: {
    short: 'usuario técnico de la tienda web',
    long: 'Usuario técnico de la tienda web (catálogo público y reservas). No es una persona: no se usa para ingresar.',
  },
  CLIENTE: {
    short: 'cuenta de la tienda web',
    long: 'Cliente con cuenta en la tienda web: ve y actualiza sus datos y reserva o cancela solo sus propias reservas. No entra al panel.',
  },
};

/** «ventas, clientes y salidas» (vacío si el rol es nuevo). */
export function roleShort(code: string): string {
  return ROLE_TEXTS[code]?.short ?? '';
}

/** Una o dos líneas: qué puede hacer el rol, en palabras. */
export function roleDescription(code: string): string {
  if (!code) return '';
  return ROLE_TEXTS[code]?.long ?? 'Rol de la empresa: en «Roles y permisos» está la lista de lo que puede hacer.';
}

/** Opciones de la lista «Rol» del formulario: los del personal (y el actual, si es otro). */
export function formRoleOptions(roles: readonly RoleChoice[], current: string | null): SelectOption[] {
  const list: RoleChoice[] = roles.filter((role) => role.code !== CUSTOMER_ROLE && role.code !== STOREFRONT_ROLE);
  if (current && !list.some((role) => role.code === current)) list.push({ code: current, name: roleLabel(current, roles) });
  return list.map((role) => ({ value: role.code, label: roleShort(role.code) ? `${role.name} · ${roleShort(role.code)}` : role.name }));
}

// ---------------------------------------------------------------------------------------------------- estado y tipo

/** Estado de una cuenta, como el escritorio (inactivo › bloqueado › sin contraseña › debe cambiarla › activo). */
export const USER_STATUSES = defineStatuses({
  activo: { label: 'Activo', tone: 'success' },
  cambiar: { label: 'Debe cambiar la contraseña', tone: 'warning' },
  bloqueado: { label: 'Bloqueado', tone: 'danger' },
  'sin-clave': { label: 'Sin contraseña', tone: 'neutral' },
  inactivo: { label: 'Inactivo', tone: 'neutral' },
});
export type UserStatus = keyof typeof USER_STATUSES;

export function statusOfUser(user: UserRecord): UserStatus {
  if (!user.isActive) return 'inactivo';
  if (user.isLocked) return 'bloqueado';
  if (!user.hasPassword) return 'sin-clave';
  if (user.mustChangePassword) return 'cambiar';
  return 'activo';
}

export function statusLabel(status: string): string {
  return statusOf(USER_STATUSES, status).label;
}

/** Personal de la tienda o cliente de la tienda web (su ÚNICO rol es CLIENTE, como `RoleCodes.IsCustomerOnly`). */
export type UserKind = 'personal' | 'clientes';

export const KIND_LABELS: Readonly<Record<UserKind, string>> = { personal: 'Personal', clientes: 'Cliente web' };

export function kindOf(user: Pick<UserRecord, 'roles'>): UserKind {
  return user.roles.length === 1 && user.roles[0] === CUSTOMER_ROLE ? 'clientes' : 'personal';
}

// ---------------------------------------------------------------------------------------------------- filas

/** Una fila lista para la tabla: lo del servidor más cómo se muestra. */
export interface UserItem {
  /** El correo en minúsculas (único). */
  key: string;
  record: UserRecord;
  kind: UserKind;
  /** Rol que se edita (el primero; '' = sin rol). */
  roleCode: string;
  roleText: string;
  status: UserStatus;
  /** Su rol ve todas las sucursales (gerencia global). */
  allBranches: boolean;
  branchesText: string;
  /** Es la persona de la sesión (no puede desactivarse ni cambiarse el rol). */
  isSelf: boolean;
}

export function toUserItems(users: readonly UserRecord[], roles: RolesData | undefined, sessionEmail: string | null): UserItem[] {
  const choices = roleChoices(roles);
  const global = new Set((roles?.roles ?? []).filter((role) => role.permissions.includes(ALL_BRANCHES_PERMISSION)).map((role) => role.code));
  const self = sessionEmail ? sessionEmail.trim().toLowerCase() : null;
  return users.map((record) => {
    const kind = kindOf(record);
    const allBranches = kind === 'personal' && record.roles.some((code) => global.has(code));
    let branchesText = '';
    if (kind === 'personal') branchesText = allBranches ? 'Todas (gerencia global)' : record.branchCodes.length > 0 ? record.branchCodes.join(' · ') : 'Sin sucursal';
    return {
      key: record.email.toLowerCase(),
      record,
      kind,
      roleCode: record.roles[0] ?? '',
      roleText: record.roles.map((code) => roleLabel(code, choices)).join(' · '),
      status: statusOfUser(record),
      allBranches,
      branchesText,
      isSelf: self !== null && record.email.toLowerCase() === self,
    };
  });
}

/** Filtros de la lista (en la dirección). Por defecto se ve el PERSONAL: los clientes web van aparte (pedido del cliente). */
export const USER_FILTERS = { q: '', tipo: 'personal', rol: '', sucursal: '', estado: '' };
export type UserFilters = typeof USER_FILTERS;

export function filterUsers(items: readonly UserItem[], filters: UserFilters): UserItem[] {
  return items.filter(
    (item) =>
      (!filters.tipo || item.kind === filters.tipo) &&
      (!filters.rol || item.record.roles.includes(filters.rol)) &&
      (!filters.sucursal || item.allBranches || item.record.branchCodes.includes(filters.sucursal)) &&
      (!filters.estado || item.status === filters.estado) &&
      matchesSearch(filters.q, [item.record.name, item.record.email, item.roleText]),
  );
}

/** Opciones de «Tipo» con cuántos hay de cada uno. */
export function kindOptions(items: readonly UserItem[]): SelectOption[] {
  const count = (kind: UserKind) => items.filter((item) => item.kind === kind).length;
  return [
    { value: 'personal', label: `Personal (${count('personal')})` },
    { value: 'clientes', label: `Clientes web (${count('clientes')})` },
  ];
}

export function roleFilterOptions(choices: readonly RoleChoice[]): SelectOption[] {
  return choices.map((role) => ({ value: role.code, label: role.name }));
}

/** Una sucursal para elegir (del directorio o, sin permiso para leerlo, de las asignaciones conocidas). */
export interface BranchChoice {
  code: string;
  name: string;
  isActive: boolean;
}

export function branchChoicesOf(branches: readonly BranchRecord[] | undefined, users: readonly UserRecord[]): BranchChoice[] {
  if (branches) return branches.map((branch) => ({ code: branch.code, name: branch.name, isActive: branch.isActive }));
  const codes = [...new Set(users.flatMap((user) => user.branchCodes))].sort((a, b) => a.localeCompare(b, 'es'));
  return codes.map((code) => ({ code, name: code, isActive: true }));
}

export function branchLabel(branch: BranchChoice): string {
  return branch.name && branch.name !== branch.code ? `${branch.code} · ${branch.name}` : branch.code;
}

export function branchFilterOptions(branches: readonly BranchChoice[]): SelectOption[] {
  return branches.map((branch) => ({ value: branch.code, label: branchLabel(branch) }));
}

export function lastAccessText(user: UserRecord): string {
  return user.lastAccess ? formatDateTime(user.lastAccess) : 'Nunca ingresó';
}

export function passwordText(user: UserRecord): string {
  if (!user.hasPassword) return 'Sin contraseña';
  return user.mustChangePassword ? 'Temporal: debe cambiarla al ingresar' : 'Asignada';
}

/** Columnas del CSV (más completas que la tabla). */
export const USER_CSV_COLUMNS: readonly CsvColumn<UserItem>[] = [
  { header: 'Nombre', value: (item) => item.record.name },
  { header: 'Correo', value: (item) => item.record.email },
  { header: 'Tipo', value: (item) => KIND_LABELS[item.kind] },
  { header: 'Rol', value: (item) => item.roleText },
  { header: 'Sucursales', value: (item) => item.branchesText },
  { header: 'Estado', value: (item) => statusLabel(item.status) },
  { header: 'Último ingreso', value: (item) => toDate(item.record.lastAccess) },
  { header: 'Intentos fallidos', value: (item) => item.record.failedAttempts },
  { header: 'Tiene contraseña', value: (item) => item.record.hasPassword },
  { header: 'Debe cambiarla', value: (item) => item.record.mustChangePassword },
];

// ---------------------------------------------------------------------------------------------------- formularios

export interface UserFormValues {
  name: string;
  email: string;
  roleCode: string;
  isActive: boolean;
  branchCodes: string[];
  password: string;
  mustChange: boolean;
}

export type UserFormField = 'name' | 'email' | 'roleCode' | 'branchCodes' | 'password';

/** Valores iniciales: los del usuario que se edita o, para uno nuevo, rol Ventas, la sucursal activa y una contraseña. */
export function initialUserForm(editing: UserItem | null, roles: readonly RoleChoice[], activeBranchCode: string | null): UserFormValues {
  if (editing) {
    return {
      name: editing.record.name,
      email: editing.record.email,
      roleCode: editing.roleCode,
      isActive: editing.record.isActive,
      branchCodes: [...editing.record.branchCodes],
      password: '',
      mustChange: true,
    };
  }
  const staff = formRoleOptions(roles, null);
  const roleCode = staff.some((option) => option.value === DEFAULT_ROLE) ? DEFAULT_ROLE : (staff[0]?.value ?? '');
  return { name: '', email: '', roleCode, isActive: true, branchCodes: activeBranchCode ? [activeBranchCode] : [], password: generateTemporaryPassword(), mustChange: true };
}

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

export function isEmail(text: string): boolean {
  const value = text.trim();
  return value.length <= 150 && EMAIL.test(value);
}

/** Problemas del formulario por campo (las mismas reglas del servidor; el servidor vuelve a validar). */
export function userFormProblems(values: UserFormValues, options: { isNew: boolean; branchesRequired: boolean }): Partial<Record<UserFormField, string>> {
  const problems: Partial<Record<UserFormField, string>> = {};
  const name = values.name.trim();
  if (name.length < 2) problems.name = 'Indique el nombre completo.';
  else if (name.length > 120) problems.name = 'El nombre tiene como máximo 120 caracteres.';
  if (!isEmail(values.email)) problems.email = 'Indique un correo válido, por ejemplo nombre@empresa.com.';
  if (!values.roleCode) problems.roleCode = 'Elija el rol.';
  if (options.isNew && options.branchesRequired && values.branchCodes.length === 0) problems.branchCodes = 'Elija al menos una sucursal.';
  if (options.isNew) {
    const password = temporaryPasswordProblem(values.password);
    if (password) problems.password = password;
  }
  return problems;
}

/** Pedido de un usuario NUEVO. Sin permiso para listar sucursales, `branchCodes: null` (el servidor usa la activa). */
export function newUserPayload(values: UserFormValues, sendBranches: boolean): SaveUserPayload {
  return {
    originalEmail: null,
    email: values.email.trim(),
    name: values.name.trim(),
    roleCode: values.roleCode,
    isActive: values.isActive,
    newPassword: values.password,
    branchCodes: sendBranches ? [...values.branchCodes] : null,
  };
}

/** Pedido al EDITAR: sin contraseña (se restablece aparte) y sin tocar las sucursales (se asignan aparte). */
export function editUserPayload(values: UserFormValues, original: UserRecord): SaveUserPayload {
  return {
    originalEmail: original.email,
    email: values.email.trim(),
    name: values.name.trim(),
    roleCode: values.roleCode,
    isActive: values.isActive,
    newPassword: null,
    branchCodes: null,
  };
}

/** Activar o desactivar: el mismo usuario con otro `isActive` (null si no tiene rol: hay que editarlo). */
export function activationPayload(user: UserRecord, isActive: boolean): SaveUserPayload | null {
  const roleCode = user.roles[0];
  if (!roleCode) return null;
  return { originalEmail: user.email, email: user.email, name: user.name, roleCode, isActive, newPassword: null, branchCodes: null };
}

/** Los códigos elegidos en el orden de la lista (y los desconocidos al final). */
export function orderedCodes(selected: readonly string[], branches: readonly BranchChoice[]): string[] {
  const known = branches.map((branch) => branch.code).filter((code) => selected.includes(code));
  return [...known, ...selected.filter((code) => !known.includes(code))];
}

/** El mensaje del servidor sin la marca «✔ » o «✖ » del escritorio. */
export function plainMessage(text: string): string {
  return text.replace(/^[✔✖\s]+/u, '').trim();
}

// ---------------------------------------------------------------------------------------------------- contraseña temporal

/** Por qué no sirve una contraseña (las mismas reglas del servidor), o null si sirve. */
export function temporaryPasswordProblem(password: string): string | null {
  if (password.length < 8 || password.length > 128) return 'Use entre 8 y 128 caracteres.';
  if (!/\p{L}/u.test(password) || !/\p{N}/u.test(password)) return 'Combine letras y números.';
  return null;
}

const LETTERS = 'abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ';
const DIGITS = '23456789';

/** Una contraseña temporal fácil de dictar («kPxRt-4827»): sin letras ni números que se confunden (l, 1, O, 0). */
export function generateTemporaryPassword(random: (max: number) => number = secureRandom): string {
  const pick = (alphabet: string, length: number) => Array.from({ length }, () => alphabet[random(alphabet.length)]).join('');
  return `${pick(LETTERS, 5)}-${pick(DIGITS, 4)}`;
}

function secureRandom(max: number): number {
  const values = new Uint32Array(1);
  crypto.getRandomValues(values);
  return values[0] % max;
}

// ---------------------------------------------------------------------------------------------------- resumen (estadística)

export interface UsersSummaryData {
  staff: number;
  staffActive: number;
  customers: number;
  locked: number;
  mustChange: number;
  /** Personas que ingresaron hoy (día de La Paz). */
  today: number;
  /** Personal activo por rol (de más a menos). */
  byRole: { label: string; value: number }[];
}

export function usersSummary(users: readonly UserRecord[], now: Date): UsersSummaryData {
  const day = laPazToday(now);
  const staff = users.filter((user) => kindOf(user) === 'personal');
  const active = staff.filter((user) => user.isActive);
  const byRole = new Map<string, number>();
  for (const user of active) {
    const label = user.roles.length > 0 ? user.roles.map(roleName).join(' · ') : 'Sin rol';
    byRole.set(label, (byRole.get(label) ?? 0) + 1);
  }
  return {
    staff: staff.length,
    staffActive: active.length,
    customers: users.length - staff.length,
    locked: users.filter((user) => user.isActive && user.isLocked).length,
    mustChange: users.filter((user) => user.isActive && user.hasPassword && user.mustChangePassword).length,
    today: users.filter((user) => toIsoDate(user.lastAccess) === day).length,
    byRole: [...byRole.entries()].map(([label, value]) => ({ label, value })).sort((a, b) => b.value - a.value || a.label.localeCompare(b.label, 'es')),
  };
}

// ---------------------------------------------------------------------------------------------------- roles y permisos

/** Áreas para agrupar los permisos (en el orden de las secciones del panel). */
export const AREAS = [
  { key: 'ventas', label: 'Ventas y caja' },
  { key: 'tecnologia', label: 'Tecnología: armados, series y garantías' },
  { key: 'inventario', label: 'Inventario y catálogo' },
  { key: 'compras', label: 'Compras' },
  { key: 'sucursales', label: 'Sucursales y transferencias' },
  { key: 'facturacion', label: 'Facturación' },
  { key: 'analisis', label: 'Reportes y contabilidad' },
  { key: 'administracion', label: 'Administración' },
  { key: 'tienda', label: 'Tienda web y cuentas de cliente' },
  { key: 'otros', label: 'Otras funciones' },
] as const;
export type AreaKey = (typeof AREAS)[number]['key'];

/** Área de un permiso por su código (solo para presentarlo agrupado). */
export function areaOf(code: string): AreaKey {
  if (code.startsWith('account.') || code.startsWith('storefront.')) return 'tienda';
  if (code === 'sales.pcbuild.manage' || code.startsWith('inventory.serials.') || code.startsWith('service.') || code === 'catalog.specs.manage') return 'tecnologia';
  if (code.startsWith('sales.')) return 'ventas';
  if (code.startsWith('inventory.transfers.') || code.startsWith('corporate.')) return 'sucursales';
  if (code.startsWith('inventory.') || code.startsWith('catalog.')) return 'inventario';
  if (code.startsWith('purchasing.')) return 'compras';
  if (code.startsWith('billing.')) return 'facturacion';
  if (code.startsWith('accounting.') || code.startsWith('reports.')) return 'analisis';
  if (code.startsWith('iam.') || code.startsWith('integration.')) return 'administracion';
  return 'otros';
}

export function areaLabel(area: string): string {
  return AREAS.find((item) => item.key === area)?.label ?? area;
}

/** Una función (permiso) con su nombre en español y su área. */
export interface PermissionEntry {
  code: string;
  name: string;
  area: AreaKey;
}

export function permissionEntries(roles: RolesData): PermissionEntry[] {
  return roles.permissions.map((permission) => ({ code: permission.item1, name: permission.item2 || permissionName(permission.item1), area: areaOf(permission.item1) }));
}

export interface PermissionGroup {
  area: AreaKey;
  label: string;
  items: PermissionEntry[];
}

function groupByArea(entries: readonly PermissionEntry[]): PermissionGroup[] {
  return AREAS.map((area) => ({ area: area.key, label: area.label, items: entries.filter((entry) => entry.area === area.key) })).filter((group) => group.items.length > 0);
}

/** Un rol explicado: lo que puede hacer (agrupado) y lo que no. */
export interface RoleView {
  code: string;
  name: string;
  users: number;
  description: string;
  /** Funciones que tiene (con los filtros aplicados), por área. */
  groups: PermissionGroup[];
  /** Funciones que NO tiene (con los filtros aplicados). */
  missing: PermissionEntry[];
  /** Cuántas funciones tiene y cuántas hay (sin filtros). */
  granted: number;
  total: number;
}

/** Filtros de la pestaña «Roles y permisos» (en la dirección). */
export const ROLE_FILTERS = { rol: '', area: '', q: '' };
export type RoleFilters = typeof ROLE_FILTERS;

export function roleViews(roles: RolesData, filters: RoleFilters): RoleView[] {
  const all = permissionEntries(roles);
  const matching = all.filter((entry) => (!filters.area || entry.area === filters.area) && matchesSearch(filters.q, [entry.name, entry.code, areaLabel(entry.area)]));
  return roles.roles
    .filter((role) => !filters.rol || role.code === filters.rol)
    .map((role) => {
      const has = new Set(role.permissions);
      return {
        code: role.code,
        name: role.name || roleName(role.code),
        users: role.users,
        description: roleDescription(role.code),
        groups: groupByArea(matching.filter((entry) => has.has(entry.code))),
        missing: matching.filter((entry) => !has.has(entry.code)),
        granted: all.filter((entry) => has.has(entry.code)).length,
        total: all.length,
      };
    });
}

/** El rol de una persona, explicado (para el detalle). */
export function roleViewOf(roles: RolesData | undefined, code: string): RoleView | null {
  if (!roles || !code) return null;
  return roleViews(roles, { ...ROLE_FILTERS, rol: code })[0] ?? null;
}

/** Funciones que coinciden con los filtros (filas del CSV de la matriz). */
export function matchingPermissions(roles: RolesData, filters: RoleFilters): PermissionEntry[] {
  return permissionEntries(roles).filter((entry) => (!filters.area || entry.area === filters.area) && matchesSearch(filters.q, [entry.name, entry.code, areaLabel(entry.area)]));
}

/** Columnas del CSV de la matriz: la función y un «Sí/No» por rol. */
export function matrixCsvColumns(roles: readonly RoleRecord[]): CsvColumn<PermissionEntry>[] {
  return [
    { header: 'Área', value: (entry) => areaLabel(entry.area) },
    { header: 'Función', value: (entry) => entry.name },
    { header: 'Código', value: (entry) => entry.code },
    ...roles.map((role) => ({ header: role.name || roleName(role.code), value: (entry: PermissionEntry) => role.permissions.includes(entry.code) })),
  ];
}
