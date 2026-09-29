// Sesión web y RPC EN MEMORIA para el modo mock (`VITE_API_URL=mock`) y las pruebas: permite recorrer ingresar,
// registrarse, «Mi cuenta» y el punto de entrada del panel sin servidor. Respeta el contrato del servidor (mismos
// errores, mensaje único al fallar el ingreso, bloqueo a los 5 intentos, idempotencia por `requestId`, una sesión de
// cliente solo ejecuta operaciones de cliente). Sin red ni almacenamiento: al recargar la página se pierde todo.
//
// Trae dos usuarios de MUESTRA (uno del personal y un cliente). No son cuentas reales: existen solo en la memoria de
// la pestaña y su contraseña es pública a propósito, para la demostración.
//
// V7 · reservas con la cuenta: con la opción `stock` (la pasarela de reservas del modo mock) la reserva descuenta el MISMO
// stock que ve el catálogo y, si no alcanza, responde como el servidor (422 `storefront.insufficient_stock`, con el
// detalle en el mensaje). Las notas y el nombre rechazan los caracteres de control en medio (400).
//
// V7 · W3b (panel): además de los dos usuarios de la pantalla de ingreso, `ROLE_SAMPLE_USERS` trae un usuario del
// personal por rol (BODEGA, VENTAS, CAJERO, GERENCIA y CONSULTA) con los permisos de `ROLE_PERMISSIONS` y sus sucursales
// (la gerencia ve todas; el resto, las asignadas), para recorrer el panel con cada rol. El RPC atiende también
// `GetActivityQuery` (una actividad de muestra de los últimos días más lo que se hace en la pestaña: ingresos y comandos)
// y `ResetUserPasswordCommand` (contraseña temporal y desbloqueo). La actividad nunca guarda contraseñas.

import { WebApiError } from '@/1-domain/auth/errors';
import { isStrongPassword } from '@/1-domain/auth/validation';
import type { Credentials, Registration, Session, SessionKind } from '@/1-domain/auth/types';
import type { Product } from '@/1-domain/catalog/types';
import type { IRpcGateway, RpcOutcome, RpcSendOptions } from '@/1-domain/ports/IRpcGateway';
import type { ISessionGateway } from '@/1-domain/ports/ISessionGateway';
import { formatBolivianPhone, isBolivianPhone, isValidEmail } from '@/1-domain/storefront/contact';
import type { StockShortage } from '@/1-domain/storefront/types';
import { newUuid } from '@/shared/ids';
import {
  PERMISSION_LIST,
  rpcOperation,
  type ActivityRow,
  type AuditOutcome,
  type BranchAccess,
  type ChangePasswordCommand,
  type CreateMyReservationCommand,
  type GetActivityQuery,
  type MyAccountView,
  type ResetUserPasswordCommand,
  type RpcOperationName,
  type RpcOperations,
  type RpcRequestOf,
  type RpcResponseOf,
  type SelectBranchCommand,
  type StorefrontReservationView,
  type UpdateMyAccountCommand,
  type WebSession,
} from '../http/contract';
import { toSession } from '../http/webMappers';

export interface DemoUser {
  kind: SessionKind;
  /** Cómo se presenta en la pantalla de ingreso de la demostración. */
  label: string;
  name: string;
  email: string;
  password: string;
  /** Solo personal: código del rol (por defecto ADMIN). Sus permisos salen de `ROLE_PERMISSIONS`. */
  role?: StaffRole;
  /** Solo personal sin `corporate.branches.all`: códigos de las sucursales asignadas (por defecto, todas). */
  branches?: readonly string[];
}

/** Usuarios de muestra del modo mock. */
export const DEMO_USERS: readonly DemoUser[] = [
  { kind: 'staff', label: 'Personal (administrador)', name: 'Andrea Quiroga', email: 'admin@techzone.example', password: 'Demo1234' },
  { kind: 'customer', label: 'Cliente', name: 'Valentina Aguirre', email: 'cliente@techzone.example', password: 'Demo1234' },
];

/** Roles del personal de la empresa de prueba. */
export type StaffRole = 'ADMIN' | 'BODEGA' | 'VENTAS' | 'CAJERO' | 'GERENCIA' | 'CONSULTA';

/** Todos los permisos del personal (el ADMIN los tiene todos; los `account.*` son solo de las cuentas de cliente). */
const STAFF_PERMISSIONS: readonly string[] = PERMISSION_LIST.map((permission) => permission.code).filter((code) => !code.startsWith('account.'));

/** Matriz de permisos por rol de la empresa de prueba (la misma que siembra el servidor). */
export const ROLE_PERMISSIONS: Readonly<Record<StaffRole, readonly string[]>> = {
  ADMIN: STAFF_PERMISSIONS,
  BODEGA: [
    'inventory.movements.register.warehouse',
    'inventory.stock.view',
    'inventory.counts.record',
    'inventory.counts.post',
    'purchasing.manage',
    'reports.view',
    'inventory.transfers.manage',
    'catalog.specs.manage',
    'inventory.serials.view',
    'inventory.serials.manage',
    'service.rma.open',
    'service.rma.manage',
  ],
  VENTAS: [
    'inventory.movements.register.sales',
    'inventory.stock.view',
    'sales.pos.operate',
    'sales.customers.manage',
    'sales.view',
    'reports.view',
    'billing.view',
    'billing.issue',
    'inventory.serials.view',
    'service.rma.open',
    'sales.pcbuild.manage',
  ],
  CAJERO: [
    'sales.pos.operate',
    'inventory.movements.register.sales',
    'inventory.stock.view',
    'sales.customers.manage',
    'sales.view',
    'billing.view',
    'billing.issue',
    'inventory.serials.view',
    'service.rma.open',
    'sales.pcbuild.manage',
  ],
  GERENCIA: [
    'inventory.stock.view',
    'iam.audit.view',
    'accounting.manage',
    'reports.view',
    'sales.view',
    'purchasing.manage',
    'corporate.branches.all',
    'inventory.transfers.manage',
    'billing.view',
    'billing.void',
    'billing.contingency',
    'catalog.specs.manage',
    'inventory.serials.view',
    'inventory.serials.manage',
    'service.rma.open',
    'service.rma.manage',
    'sales.pcbuild.manage',
    'storefront.read',
    'storefront.reserve',
  ],
  CONSULTA: ['inventory.stock.view', 'reports.view', 'billing.view', 'inventory.serials.view'],
};

/**
 * Un usuario del personal por rol (además del administrador de `DEMO_USERS`). NO se ofrecen en la pantalla de ingreso:
 * sirven para recorrer el panel con cada rol en el modo mock (misma contraseña pública de la demostración).
 */
export const ROLE_SAMPLE_USERS: readonly DemoUser[] = [
  { kind: 'staff', role: 'BODEGA', label: 'Bodega', name: 'Bruno Mamani', email: 'bodega@techzone.example', password: 'Demo1234', branches: ['CM'] },
  { kind: 'staff', role: 'VENTAS', label: 'Ventas', name: 'Carla Rojas', email: 'ventas@techzone.example', password: 'Demo1234', branches: ['CM', 'CB'] },
  { kind: 'staff', role: 'CAJERO', label: 'Cajero', name: 'Diego Flores', email: 'cajero@techzone.example', password: 'Demo1234', branches: ['CB'] },
  { kind: 'staff', role: 'GERENCIA', label: 'Gerencia', name: 'Elena Vargas', email: 'gerencia@techzone.example', password: 'Demo1234' },
  { kind: 'staff', role: 'CONSULTA', label: 'Consulta', name: 'Fernando Choque', email: 'consulta@techzone.example', password: 'Demo1234', branches: ['SC'] },
];

const COMPANY = 'Tech Zone Gaming S.R.L.';
const SERVER_VERSION = '7.0.0-mock';
const SESSION_HOURS = 12;
const MAX_FAILED_ATTEMPTS = 5;
const LOCKOUT_MINUTES = 15;
const DEFAULT_HOLD_HOURS = 48;

const LOGIN_FAILED = 'Correo o contraseña incorrectos.';
const SESSION_EXPIRED = 'La sesión venció o se cerró: vuelva a iniciar sesión.';

const BRANCHES: BranchAccess['branches'] = [
  { id: '5d0c1f6e-0a51-4d0e-9d11-000000000001', code: 'CM', name: 'Casa matriz La Paz · Av. 16 de Julio (El Prado)' },
  { id: '5d0c1f6e-0a51-4d0e-9d11-000000000002', code: 'CB', name: 'Sucursal Cochabamba' },
  { id: '5d0c1f6e-0a51-4d0e-9d11-000000000003', code: 'SC', name: 'Sucursal Santa Cruz' },
];

const CUSTOMER_PERMISSIONS = ['account.manage', 'account.reserve'];
const ALL_BRANCHES_PERMISSION = 'corporate.branches.all';
/** Como el servidor: `GetActivityQuery` devuelve entre 1 y 5000 filas (200 si no se indica). */
const ACTIVITY_DEFAULT_TAKE = 200;
const ACTIVITY_MAX_TAKE = 5000;

interface StoredUser {
  kind: SessionKind;
  name: string;
  email: string;
  phone: string;
  password: string;
  roles: string[];
  permissions: string[];
  /** Sucursales asignadas (códigos); null = todas las de la empresa. */
  branchCodes: readonly string[] | null;
  mustChangePassword: boolean;
  activeBranchId: string | null;
  documentType: number | null;
  documentNumber: string | null;
  complement: string | null;
  failedAttempts: number;
  lockedUntil: number;
  reservations: StorefrontReservationView[];
}

interface ProcessedRequest {
  fingerprint: string;
  result: unknown;
}

/** Stock compartido con la tienda del modo mock (lo implementa InMemoryReservationGateway). */
export interface InMemoryStock {
  /** Reserva todo o nada; devuelve los faltantes (vacío = reservó). */
  hold(lines: readonly { sku: string; quantity: number }[]): StockShortage[];
  release(lines: readonly { sku: string; quantity: number }[]): void;
}

export interface InMemoryWebOptions {
  /** Productos para armar las reservas de muestra del cliente (por defecto, ninguna reserva). */
  products?: readonly Product[];
  now?: () => Date;
  /** Usuarios iniciales (por defecto los de muestra). */
  users?: readonly DemoUser[];
  /** V7: stock compartido con la tienda; sin él, las reservas de la cuenta no descuentan stock. */
  stock?: InMemoryStock;
}

function normalizeEmail(email: string): string {
  return email.trim().toLowerCase();
}

function hasControlChars(text: string): boolean {
  return /\p{Cc}/u.test(text);
}

function roundMoney(value: number): number {
  return Math.round(value * 100) / 100;
}

/** Números pseudoaleatorios con semilla (mulberry32): la actividad de muestra es la misma en cada carga. */
function seededRandom(seed: number): () => number {
  let state = seed >>> 0;
  return () => {
    state = (state + 0x6d2b79f5) >>> 0;
    let value = state;
    value = Math.imul(value ^ (value >>> 15), value | 1);
    value ^= value + Math.imul(value ^ (value >>> 7), value | 61);
    return ((value ^ (value >>> 14)) >>> 0) / 4_294_967_296;
  };
}

/** Detalle de auditoría con la forma del servidor (`{ request, result, error }`, datos del pedido en PascalCase). */
function auditDetails(request: Record<string, unknown>, error: string | null = null, result: unknown = null): string {
  return JSON.stringify({ request, result, error });
}

interface SampleActivity {
  /** Correo del usuario de muestra (null = el sistema: trabajos automáticos). */
  email: string | null;
  action: string;
  outcome: AuditOutcome;
  request: Record<string, unknown>;
  error?: string;
}

const SAMPLE_SKUS = ['PROC-AMD-R7-7800X3D', 'GPU-NV-RTX4070S-12G', 'RAM-KNG-FURY-32G-6000', 'SSD-SAM-990PRO-2T', 'MON-LG-27GR75Q', 'CON-NIN-SWOLED'];
const SERVER_FAILURE = 'El servidor no pudo completar la operación. Intente de nuevo; si persiste, avise a soporte.';

/** Qué hace cada usuario de muestra en un día normal (la actividad de muestra elige de aquí). */
function sampleActivities(random: () => number): SampleActivity[] {
  const pick = <T>(items: readonly T[]): T => items[Math.floor(random() * items.length)];
  const sku = () => pick(SAMPLE_SKUS);
  const number = (prefix: string) => `${prefix}-${String(100 + Math.floor(random() * 800)).padStart(6, '0')}`;
  const amount = () => roundMoney(150 + random() * 9_000);
  return [
    { email: 'cajero@techzone.example', action: 'OpenPosSession', outcome: 'Succeeded', request: { RegisterCode: 'CAJA-CB-01', OpeningCash: 500 } },
    { email: 'cajero@techzone.example', action: 'Checkout', outcome: 'Succeeded', request: { RegisterCode: 'CAJA-CB-01', CustomerCode: 'CLI-0042', TotalAmount: amount() } },
    { email: 'cajero@techzone.example', action: 'Checkout', outcome: 'Rejected', request: { RegisterCode: 'CAJA-CB-01', TotalAmount: amount() }, error: `No hay stock suficiente de ${sku()}: disponible 0.` },
    { email: 'cajero@techzone.example', action: 'ClosePosSession', outcome: 'Succeeded', request: { RegisterCode: 'CAJA-CB-01', CountedCash: amount() } },
    { email: 'ventas@techzone.example', action: 'Checkout', outcome: 'Succeeded', request: { RegisterCode: 'CAJA-CM-01', CustomerCode: 'CLI-0107', TotalAmount: amount() } },
    { email: 'ventas@techzone.example', action: 'SavePcBuild', outcome: 'Succeeded', request: { Number: number('ARM-CM'), Quote: true, ValidDays: 7 } },
    { email: 'ventas@techzone.example', action: 'ReservePcBuild', outcome: 'Succeeded', request: { Number: number('ARM-CM') } },
    { email: 'ventas@techzone.example', action: 'VoidSale', outcome: 'Rejected', request: { Number: number('F-CM'), Reason: 'Cliente pidió otro modelo' }, error: 'La venta ya fue anulada.' },
    { email: 'ventas@techzone.example', action: 'SaveCustomer', outcome: 'Succeeded', request: { CustomerCode: 'CLI-0188', Name: 'Mariana Céspedes' } },
    { email: 'bodega@techzone.example', action: 'RegisterMovement', outcome: 'Succeeded', request: { Sku: sku(), MovementTypeCode: 'ENTRADA', Quantity: 1 + Math.floor(random() * 12), DocumentReference: number('OC-CM') } },
    { email: 'bodega@techzone.example', action: 'RegisterMovement', outcome: 'Rejected', request: { Sku: sku(), MovementTypeCode: 'AJUSTE (-)', Quantity: 3 }, error: 'La salida dejaría el stock en negativo.' },
    { email: 'bodega@techzone.example', action: 'ReceivePurchaseOrder', outcome: 'Succeeded', request: { Number: number('OC-CM') } },
    { email: 'bodega@techzone.example', action: 'DispatchTransfer', outcome: 'Succeeded', request: { Number: number('TR-CM') } },
    { email: 'bodega@techzone.example', action: 'RecordCount', outcome: 'Succeeded', request: { Sku: sku(), Quantity: Math.floor(random() * 20) } },
    { email: 'gerencia@techzone.example', action: 'ApprovePurchaseOrder', outcome: 'Succeeded', request: { Number: number('OC-CB') } },
    { email: 'gerencia@techzone.example', action: 'CreateJournalEntry', outcome: 'Succeeded', request: { Description: 'Ajuste de fin de mes' } },
    { email: 'gerencia@techzone.example', action: 'DispatchFiscalDocuments', outcome: 'Failed', request: {}, error: SERVER_FAILURE },
    { email: 'admin@techzone.example', action: 'SaveUser', outcome: 'Succeeded', request: { Email: 'cajero@techzone.example', Name: 'Diego Flores', RoleCode: 'CAJERO', IsActive: true } },
    { email: 'admin@techzone.example', action: 'UpdateCompanySettings', outcome: 'Succeeded', request: {} },
    { email: 'cliente@techzone.example', action: 'CreateMyReservation', outcome: 'Succeeded', request: { Kind: 'cart', HoldDays: 2 } },
    { email: 'cliente@techzone.example', action: 'UpdateMyAccount', outcome: 'Succeeded', request: { Name: 'Valentina Aguirre', Phone: '+591 71234567' } },
    { email: 'consulta@techzone.example', action: 'Login', outcome: 'Rejected', request: { Email: 'consulta@techzone.example' }, error: 'Correo o contraseña incorrectos.' },
    { email: null, action: 'ExpirePcBuildReservations', outcome: 'Succeeded', request: { Count: 1 + Math.floor(random() * 3) } },
  ];
}

/**
 * Servidor de la sesión web en memoria. `session` y `rpc` comparten el estado: quién ingresó, sus datos y sus reservas.
 */
export class InMemoryWebBackend {
  readonly session: ISessionGateway;
  readonly rpc: IRpcGateway<RpcOperations>;

  private readonly users = new Map<string, StoredUser>();
  private readonly processed = new Map<string, ProcessedRequest>();
  private readonly products: readonly Product[];
  private readonly now: () => Date;
  private readonly stock: InMemoryStock | null;
  /** Reservas de la cuenta que tienen stock tomado (las de muestra no): al liberarlas se devuelve. */
  private readonly holding = new Set<string>();
  /** Auditoría en memoria (append-only): la de muestra más lo que pasa en esta pestaña. */
  private readonly activity: ActivityRow[] = [];
  private currentEmail: string | null = null;
  private sequence = 0;

  constructor(options: InMemoryWebOptions = {}) {
    this.products = options.products ?? [];
    this.now = options.now ?? (() => new Date());
    this.stock = options.stock ?? null;
    for (const user of options.users ?? DEMO_USERS) this.addUser(user);
    this.activity.push(...this.sampleActivity());

    this.session = {
      login: (credentials) => this.login(credentials),
      register: (registration) => this.register(registration),
      current: async () => (this.currentUser() ? toSession(this.view(this.currentUser()!)) : null),
      logout: async () => {
        this.currentEmail = null;
      },
    };
    this.rpc = {
      call: (operation, payload, sendOptions) => this.call(operation, payload, sendOptions),
      send: async (operation, payload, sendOptions) => (await this.call(operation, payload, sendOptions)).result,
    };
  }

  /** Simula que la sesión venció en el servidor: el próximo pedido responde 401. */
  expire(): void {
    this.currentEmail = null;
  }

  /** Marca que el usuario debe cambiar la contraseña (como cuando la asigna el administrador). */
  requirePasswordChange(email: string): void {
    const user = this.users.get(normalizeEmail(email));
    if (user) user.mustChangePassword = true;
  }

  // -------------------------------------------------------------------------------------------------- usuarios
  private addUser(user: DemoUser): StoredUser {
    const staff = user.kind === 'staff';
    const role: StaffRole = user.role ?? 'ADMIN';
    const permissions = staff ? [...ROLE_PERMISSIONS[role]] : [...CUSTOMER_PERMISSIONS];
    const all = permissions.includes(ALL_BRANCHES_PERMISSION);
    const branchCodes = staff && !all && user.branches ? [...user.branches] : null;
    // Como el servidor (UserAccess.AccessAsync): la primera sucursal asignada; la gerencia global sin asignaciones, todas (null).
    const firstBranch = branchCodes ? BRANCHES.find((branch) => branchCodes.includes(branch.code)) : role === 'ADMIN' ? BRANCHES[0] : undefined;
    const stored: StoredUser = {
      kind: user.kind,
      name: user.name,
      email: normalizeEmail(user.email),
      phone: staff ? '' : '+591 71234567',
      password: user.password,
      roles: staff ? [role] : ['CLIENTE'],
      permissions,
      branchCodes,
      mustChangePassword: false,
      activeBranchId: staff ? (firstBranch?.id ?? null) : null,
      documentType: null,
      documentNumber: null,
      complement: null,
      failedAttempts: 0,
      lockedUntil: 0,
      reservations: [],
    };
    if (!staff) stored.reservations = this.sampleReservations(stored.name);
    this.users.set(stored.email, stored);
    return stored;
  }

  private currentUser(): StoredUser | null {
    return this.currentEmail ? (this.users.get(this.currentEmail) ?? null) : null;
  }

  private access(user: StoredUser): BranchAccess {
    if (user.kind === 'customer') return { allBranches: false, branches: [BRANCHES[0]], activeBranchId: BRANCHES[0].id };
    const all = user.permissions.includes(ALL_BRANCHES_PERMISSION);
    const branches = all || !user.branchCodes ? BRANCHES : BRANCHES.filter((branch) => user.branchCodes?.includes(branch.code));
    return { allBranches: all, branches, activeBranchId: user.activeBranchId };
  }

  private view(user: StoredUser): WebSession {
    return {
      displayName: user.name,
      email: user.email,
      roles: [...user.roles],
      permissions: [...user.permissions],
      mustChangePassword: user.mustChangePassword,
      access: this.access(user),
      kind: user.kind,
      expiresAt: new Date(this.now().getTime() + SESSION_HOURS * 3_600_000).toISOString(),
      serverVersion: SERVER_VERSION,
      company: COMPANY,
    };
  }

  private async login(credentials: Credentials): Promise<Session> {
    const email = normalizeEmail(credentials.email);
    const user = this.users.get(email);
    const now = this.now().getTime();
    const locked = new WebApiError({
      kind: 'authentication',
      status: 401,
      message: `Cuenta bloqueada por ${MAX_FAILED_ATTEMPTS} intentos fallidos: espere ${LOCKOUT_MINUTES} minutos.`,
    });
    const rejected = (error: WebApiError): WebApiError => {
      // Queda en la actividad (sin la contraseña), como en el servidor.
      this.record(user ?? null, 'Login', 'Rejected', { Email: email }, error.message, user ? undefined : email);
      return error;
    };
    // El mismo mensaje si el correo no existe o si la contraseña no coincide (regla P-03).
    if (!user) throw rejected(new WebApiError({ kind: 'authentication', status: 401, message: LOGIN_FAILED }));
    if (user.lockedUntil > now) throw rejected(locked);
    if (user.password !== credentials.password) {
      user.failedAttempts += 1;
      if (user.failedAttempts >= MAX_FAILED_ATTEMPTS) {
        user.failedAttempts = 0;
        user.lockedUntil = now + LOCKOUT_MINUTES * 60_000;
        throw rejected(locked);
      }
      throw rejected(new WebApiError({ kind: 'authentication', status: 401, message: LOGIN_FAILED }));
    }
    user.failedAttempts = 0;
    user.lockedUntil = 0;
    this.currentEmail = user.email;
    this.record(user, 'Login', 'Succeeded', { Email: user.email });
    return toSession(this.view(user));
  }

  private async register(registration: Registration): Promise<Session> {
    const errors: string[] = [];
    const name = registration.name.trim();
    if (name.length === 0 || name.length > 120 || hasControlChars(registration.name)) errors.push('Indique su nombre (hasta 120 caracteres, sin caracteres de control).');
    if (!isValidEmail(registration.email)) errors.push('Indique un correo válido.');
    if (!isBolivianPhone(registration.phone)) errors.push('El teléfono debe tener 7 u 8 dígitos (Bolivia), con o sin +591.');
    if (!isStrongPassword(registration.password)) errors.push('La contraseña debe tener entre 8 y 128 caracteres y combinar letras y números.');
    if (errors.length > 0) throw new WebApiError({ kind: 'validation', status: 400, message: `Datos no válidos: ${errors.join(' · ')}`, errors });
    const email = normalizeEmail(registration.email);
    if (this.users.has(email)) {
      throw new WebApiError({ kind: 'domain', status: 422, code: 'account.email_taken', message: 'Ese correo ya tiene una cuenta. Inicie sesión con él.' });
    }
    // El registro crea SIEMPRE una cuenta de cliente: no acepta rol, sucursal ni permisos (regla P-03).
    const user = this.addUser({ kind: 'customer', label: 'Cliente', name, email, password: registration.password });
    user.phone = formatBolivianPhone(registration.phone);
    user.reservations = [];
    this.currentEmail = user.email;
    return toSession(this.view(user));
  }

  // -------------------------------------------------------------------------------------------------- RPC
  private async call<K extends RpcOperationName>(operation: K, payload: RpcRequestOf<K>, options: RpcSendOptions = {}): Promise<RpcOutcome<RpcResponseOf<K>>> {
    const requestId = options.requestId ?? newUuid();
    if (options.signal?.aborted) throw new DOMException('La operación se canceló.', 'AbortError');
    try {
      const { result, replayed } = this.dispatch(operation, payload, requestId);
      return { result: result as RpcResponseOf<K>, replayed, requestId };
    } catch (error) {
      // Como el adaptador HTTP: el error lleva el id del pedido para poder reintentar con el mismo.
      if (!(error instanceof WebApiError)) throw error;
      throw new WebApiError({ kind: error.kind, status: error.status, message: error.message, errors: error.errors, code: error.code, requestId });
    }
  }

  private dispatch(operation: RpcOperationName, payload: unknown, requestId: string): { result: unknown; replayed: boolean } {
    const info = rpcOperation(operation);
    if (!info) throw new WebApiError({ kind: 'unsupported', status: 400, message: 'Operación desconocida o pedido mal formado.' });
    const user = this.currentUser();
    if (!user) throw new WebApiError({ kind: 'authentication', status: 401, message: SESSION_EXPIRED });
    if (user.kind === 'customer' && !info.customer) {
      throw new WebApiError({ kind: 'access_denied', status: 403, message: 'Una cuenta de cliente no puede ejecutar esta operación.' });
    }
    const missing = info.permissions.find((permission) => !user.permissions.includes(permission));
    if (missing) throw new WebApiError({ kind: 'access_denied', status: 403, message: `Su cuenta no tiene el permiso ${missing}.` });
    if (!info.command) return { result: this.execute(user, operation, payload), replayed: false };

    // Comando idempotente: el mismo id con el mismo contenido devuelve la respuesta guardada; con otro, se rechaza.
    const fingerprint = `${user.email}\n${info.type}\n${JSON.stringify(payload ?? {})}`;
    const previous = this.processed.get(requestId);
    if (previous) {
      if (previous.fingerprint !== fingerprint) {
        throw new WebApiError({ kind: 'idempotency', status: 422, message: 'Ese identificador de pedido ya se usó con otro contenido.' });
      }
      return { result: previous.result, replayed: true };
    }
    // Como la tubería del servidor: todo comando que se ejecuta deja su fila en la auditoría, también si se rechaza.
    const action = operation.replace(/Command$/, '');
    let result: unknown;
    try {
      result = this.execute(user, operation, payload);
    } catch (error) {
      const failure = error instanceof WebApiError ? error : null;
      const outcome: AuditOutcome = failure && failure.kind !== 'server' && failure.kind !== 'unknown' ? 'Rejected' : 'Failed';
      this.record(user, action, outcome, this.auditRequestOf(operation, payload), failure?.message ?? SERVER_FAILURE);
      throw error;
    }
    this.record(user, action, 'Succeeded', this.auditRequestOf(operation, payload));
    this.processed.set(requestId, { fingerprint, result });
    return { result, replayed: false };
  }

  private execute(user: StoredUser, operation: RpcOperationName, payload: unknown): unknown {
    switch (operation) {
      case 'GetMyAccountQuery':
        return this.accountView(user);
      case 'UpdateMyAccountCommand':
        return this.updateAccount(user, payload as UpdateMyAccountCommand);
      case 'GetMyReservationsQuery':
        return user.reservations.map((reservation) => this.refreshed(reservation));
      case 'CancelMyReservationCommand':
        return this.cancelReservation(user, (payload as { number: string }).number);
      case 'CreateMyReservationCommand':
        return this.createReservation(user, payload as CreateMyReservationCommand);
      case 'ChangePasswordCommand':
        return this.changePassword(user, payload as ChangePasswordCommand);
      case 'SelectBranchCommand':
        return this.selectBranch(user, payload as SelectBranchCommand);
      case 'GetActivityQuery':
        return this.activityOf(payload as GetActivityQuery);
      case 'ResetUserPasswordCommand':
        return this.resetPassword(payload as ResetUserPasswordCommand);
      default:
        throw new WebApiError({ kind: 'unsupported', status: 400, message: 'Operación desconocida o pedido mal formado.' });
    }
  }

  // -------------------------------------------------------------------------------------------------- actividad
  /**
   * Lo que queda en la auditoría de cada comando: solo datos que el servidor también guarda (`AuditDetails`). NUNCA una
   * contraseña: los comandos que las llevan dejan únicamente el correo o nada.
   */
  private auditRequestOf(operation: RpcOperationName, payload: unknown): Record<string, unknown> {
    switch (operation) {
      case 'SelectBranchCommand': {
        const command = payload as SelectBranchCommand;
        return { SessionId: command.sessionId, BranchId: command.branchId };
      }
      case 'ResetUserPasswordCommand': {
        const command = payload as ResetUserPasswordCommand;
        return { Email: normalizeEmail(command.email ?? ''), MustChange: command.mustChange ?? true };
      }
      case 'UpdateMyAccountCommand': {
        const command = payload as UpdateMyAccountCommand;
        return { Name: command.name, Phone: command.phone };
      }
      case 'CancelMyReservationCommand':
        return { Number: (payload as { number?: string }).number ?? null };
      case 'CreateMyReservationCommand': {
        const command = payload as CreateMyReservationCommand;
        return { Kind: command.kind, HoldDays: command.holdDays ?? null };
      }
      default:
        return {};
    }
  }

  /** Agrega una fila a la auditoría en memoria. `email` sirve para un intento de ingreso con un correo que no existe. */
  private record(user: StoredUser | null, action: string, outcome: AuditOutcome, request: Record<string, unknown>, error: string | null = null, email?: string): void {
    this.activity.push({
      occurredAt: this.now().toISOString(),
      userEmail: user?.email ?? email ?? null,
      userName: user?.name ?? null,
      action,
      outcome,
      details: auditDetails(request, error),
    });
  }

  /** La actividad más reciente primero, como `GetActivityQuery` (entre 1 y 5000 filas). */
  private activityOf(query: GetActivityQuery): ActivityRow[] {
    const requested = Number.isFinite(query?.take) ? Math.trunc(query.take as number) : ACTIVITY_DEFAULT_TAKE;
    const take = Math.min(Math.max(requested, 1), ACTIVITY_MAX_TAKE);
    // A la misma hora, lo registrado último va primero (el servidor guarda la hora con más precisión).
    return this.activity
      .map((row, index) => ({ row, index }))
      .sort((a, b) => b.row.occurredAt.localeCompare(a.row.occurredAt) || b.index - a.index)
      .slice(0, take)
      .map(({ row }) => ({ ...row }));
  }

  /** Actividad de muestra: los últimos 12 días (hoy, lo de las últimas horas), siempre la misma para la misma fecha. */
  private sampleActivity(): ActivityRow[] {
    const random = seededRandom(2026);
    const now = this.now().getTime();
    const minute = 60_000;
    const rows: ActivityRow[] = [];
    const add = (at: number) => {
      const options = sampleActivities(random);
      const sample = options[Math.floor(random() * options.length)];
      const user = sample.email ? this.users.get(sample.email) : undefined;
      // Solo usuarios que existen en esta instancia (o el sistema).
      if (sample.email && !user) return;
      rows.push({
        occurredAt: new Date(at).toISOString(),
        userEmail: user?.email ?? null,
        userName: user?.name ?? null,
        action: sample.action,
        outcome: sample.outcome,
        details: auditDetails(sample.request, sample.error ?? null),
      });
    };
    // Hoy: una operación cada 17 minutos hacia atrás desde hace 5 minutos.
    for (let index = 0; index < 10; index += 1) add(now - (5 + index * 17) * minute);
    // Días anteriores: entre 8 y 15 operaciones por día, en horario de la tienda.
    for (let day = 1; day <= 12; day += 1) {
      const count = 8 + Math.floor(random() * 8);
      for (let index = 0; index < count; index += 1) add(now - day * 24 * 60 * minute - Math.floor(random() * 10 * 60) * minute);
    }
    return rows;
  }

  /** El administrador asigna una contraseña temporal: la cuenta se desbloquea y, si `mustChange`, pide cambiarla al ingresar. */
  private resetPassword(command: ResetUserPasswordCommand): boolean {
    const email = normalizeEmail(command.email ?? '');
    if (!isStrongPassword(command.newPassword ?? '')) {
      const message = 'La contraseña debe tener entre 8 y 128 caracteres y combinar letras y números.';
      throw new WebApiError({ kind: 'validation', status: 400, message: `Datos no válidos: ${message}`, errors: [message] });
    }
    const user = this.users.get(email);
    if (!user) throw new WebApiError({ kind: 'not_found', status: 404, message: `El usuario ${email} no existe.` });
    user.password = command.newPassword;
    user.mustChangePassword = command.mustChange ?? true;
    user.failedAttempts = 0;
    user.lockedUntil = 0;
    return true;
  }

  private accountView(user: StoredUser): MyAccountView {
    return {
      name: user.name,
      email: user.email,
      phone: user.phone,
      documentType: user.documentType,
      documentNumber: user.documentNumber,
      complement: user.complement,
    };
  }

  private updateAccount(user: StoredUser, command: UpdateMyAccountCommand): MyAccountView {
    const errors: string[] = [];
    const name = (command.name ?? '').trim();
    if (name.length === 0 || name.length > 120 || hasControlChars(command.name ?? '')) errors.push('Indique su nombre (hasta 120 caracteres, sin caracteres de control).');
    if (!isBolivianPhone(command.phone ?? '')) errors.push('El teléfono debe tener 7 u 8 dígitos (Bolivia), con o sin +591.');
    if (errors.length > 0) throw new WebApiError({ kind: 'validation', status: 400, message: `Datos no válidos: ${errors.join(' · ')}`, errors });

    const type = command.documentType ?? null;
    const number = (command.documentNumber ?? '').trim();
    const complement = (command.complement ?? '').trim();
    if (type === null) {
      if (complement) throw new WebApiError({ kind: 'domain', status: 422, code: 'customer.complement', message: 'El complemento requiere el tipo de documento CI.' });
    } else {
      if (!Number.isInteger(type) || type < 1 || type > 5) {
        throw new WebApiError({ kind: 'domain', status: 422, code: 'buyer.doc_type', message: 'El tipo de documento del comprador va de 1 (CI) a 5 (NIT).' });
      }
      if (number.length < 1 || number.length > 20) {
        throw new WebApiError({ kind: 'domain', status: 422, code: 'buyer.doc_number', message: 'El número de documento del comprador es obligatorio (hasta 20 caracteres).' });
      }
      if ((type === 1 || type === 5) && !/^[0-9]+$/.test(number)) {
        throw new WebApiError({ kind: 'domain', status: 422, code: 'buyer.doc_numeric', message: 'Con CI o NIT el número de documento solo admite dígitos.' });
      }
      if (complement && type !== 1) {
        throw new WebApiError({ kind: 'domain', status: 422, code: 'buyer.complement', message: 'El complemento solo se usa con cédula de identidad.' });
      }
      if (complement.length > 5) {
        throw new WebApiError({ kind: 'domain', status: 422, code: 'buyer.complement', message: 'El complemento tiene como máximo 5 caracteres.' });
      }
    }
    user.name = name;
    user.phone = formatBolivianPhone(command.phone);
    user.documentType = type;
    user.documentNumber = type === null ? null : number;
    user.complement = type === 1 && complement ? complement.toUpperCase() : null;
    return this.accountView(user);
  }

  private changePassword(user: StoredUser, command: ChangePasswordCommand): boolean {
    const errors: string[] = [];
    if (!command.currentPassword) errors.push('Indique su contraseña actual.');
    if (!isStrongPassword(command.newPassword ?? '')) errors.push('La nueva contraseña debe tener entre 8 y 128 caracteres y combinar letras y números.');
    else if (command.newPassword === command.currentPassword) errors.push('La nueva contraseña debe ser distinta de la actual.');
    if (errors.length > 0) throw new WebApiError({ kind: 'validation', status: 400, message: `Datos no válidos: ${errors.join(' · ')}`, errors });
    // Igual que el servidor: contraseña actual incorrecta responde 401 SIN cerrar la sesión.
    if (command.currentPassword !== user.password) throw new WebApiError({ kind: 'authentication', status: 401, message: 'La contraseña actual no es correcta.' });
    user.password = command.newPassword;
    user.mustChangePassword = false;
    return true;
  }

  private selectBranch(user: StoredUser, command: SelectBranchCommand): BranchAccess {
    // Mismas reglas que SelectBranchHandler: una de sus sucursales, o todas (null) solo la gerencia global.
    const access = this.access(user);
    const branchId = command.branchId ?? null;
    if (branchId === null && !access.allBranches) {
      throw new WebApiError({ kind: 'access_denied', status: 403, message: 'Elija una de sus sucursales (la vista de todas es solo para la gerencia).' });
    }
    if (branchId !== null && !access.branches.some((branch) => branch.id === branchId)) {
      throw new WebApiError({ kind: 'access_denied', status: 403, message: 'La sucursal elegida no está entre las suyas.' });
    }
    user.activeBranchId = branchId;
    return this.access(user);
  }

  // -------------------------------------------------------------------------------------------------- reservas
  /** Una reserva activa cuyo plazo pasó se muestra vencida (el servidor la cierra con un trabajo en segundo plano). */
  private refreshed(reservation: StorefrontReservationView): StorefrontReservationView {
    if (reservation.status === 'Reserved' && new Date(reservation.reservedUntil).getTime() <= this.now().getTime()) {
      return { ...reservation, status: 'Expired', statusText: 'Vencida' };
    }
    return reservation;
  }

  private cancelReservation(user: StoredUser, number: string): StorefrontReservationView {
    const code = (number ?? '').trim().toUpperCase();
    const index = user.reservations.findIndex((reservation) => reservation.number === code);
    // Una reserva de otra cuenta «no existe»: no se revela nada más (regla P-04).
    if (index < 0) throw new WebApiError({ kind: 'not_found', status: 404, message: `La reserva ${code} no existe.` });
    const current = this.refreshed(user.reservations[index]);
    if (current.status !== 'Reserved') {
      throw new WebApiError({ kind: 'domain', status: 422, code: 'pcbuild.state', message: `La reserva ${code} ya no está activa: no se puede liberar.` });
    }
    const cancelled: StorefrontReservationView = { ...current, status: 'Cancelled', statusText: 'Cancelada', cancelReason: 'Cancelada por el cliente desde su cuenta' };
    user.reservations[index] = cancelled;
    if (this.holding.delete(code)) this.stock?.release(current.lines.map((line) => ({ sku: line.sku, quantity: line.quantity })));
    return cancelled;
  }

  private createReservation(user: StoredUser, command: CreateMyReservationCommand): StorefrontReservationView {
    const lines = command.lines ?? [];
    if (lines.length === 0) {
      throw new WebApiError({ kind: 'validation', status: 400, message: 'Datos no válidos: Agregue al menos un producto.', errors: ['Agregue al menos un producto.'] });
    }
    const kind = String(command.kind).toLowerCase() === 'cart' ? 'cart' : 'build';
    const days = command.holdDays ?? null;
    const invalid: string[] = [];
    if (days !== null && (!Number.isInteger(days) || days < 1 || days > 3)) invalid.push('Los días para recoger la reserva van de 1 a 3.');
    // Como el servidor: los extremos se recortan; en medio no se admiten saltos de línea ni otros caracteres de control.
    if (hasControlChars((command.notes ?? '').trim())) invalid.push('Las notas van en una sola línea: no admite saltos de línea, tabuladores ni otros caracteres de control.');
    if (hasControlChars((command.name ?? '').trim())) invalid.push('El nombre de la reserva no admite saltos de línea, tabuladores ni otros caracteres de control.');
    if (invalid.length > 0) throw new WebApiError({ kind: 'validation', status: 400, message: `Datos no válidos: ${invalid.join(' · ')}`, errors: invalid });
    const bySku = new Map(this.products.map((product) => [product.sku, product]));
    const viewLines = lines.map((line) => {
      const product = bySku.get(line.sku);
      if (!product) throw new WebApiError({ kind: 'domain', status: 422, code: 'product.inactive', message: `El producto ${line.sku} no está en el catálogo.` });
      return { slot: kind === 'cart' ? null : (line.slot ?? null), sku: product.sku, name: product.name, quantity: line.quantity, unitPrice: product.price, subtotal: roundMoney(product.price * line.quantity) };
    });
    if (this.stock) {
      const shortages = this.stock.hold(lines.map((line) => ({ sku: line.sku, quantity: line.quantity })));
      if (shortages.length > 0) {
        const detail = shortages.map((item) => `${item.sku} (pedido ${item.requested}, disponible ${item.available})`).join('; ');
        throw new WebApiError({ kind: 'domain', status: 422, code: 'storefront.insufficient_stock', message: `No hay stock suficiente para ${shortages.length} pieza(s): ${detail}` });
      }
    }
    const createdAt = this.now();
    this.sequence += 1;
    const number = `${kind === 'cart' ? 'RES' : 'ARM'}-WEB-${String(900000 + this.sequence)}`;
    if (this.stock) this.holding.add(number);
    const reservation: StorefrontReservationView = {
      number,
      status: 'Reserved',
      statusText: 'Reservada',
      createdAt: createdAt.toISOString(),
      reservedUntil: new Date(createdAt.getTime() + (days === null ? DEFAULT_HOLD_HOURS : days * 24) * 3_600_000).toISOString(),
      total: roundMoney(viewLines.reduce((total, line) => total + line.subtotal, 0)),
      contactName: user.name,
      branch: BRANCHES[0].code,
      notes: command.notes?.trim() || null,
      hasCompatibilityWarnings: false,
      lines: viewLines,
      cancelReason: null,
      kind,
      mailQueued: true,
    };
    user.reservations.unshift(reservation);
    return reservation;
  }

  /** Reservas de muestra del cliente: una activa (carrito), una vendida (armado) y una vencida. */
  private sampleReservations(contactName: string): StorefrontReservationView[] {
    const available = this.products.filter((product) => product.stock > 0);
    if (available.length < 4) return [];
    const now = this.now().getTime();
    const hours = (count: number) => count * 3_600_000;
    const build = (number: string, kind: 'cart' | 'build', picks: readonly { product: Product; quantity: number; slot: string | null }[], status: 'Reserved' | 'Sold' | 'Expired', createdAt: number): StorefrontReservationView => {
      const lines = picks.map(({ product, quantity, slot }) => ({ slot, sku: product.sku, name: product.name, quantity, unitPrice: product.price, subtotal: roundMoney(product.price * quantity) }));
      return {
        number,
        status,
        statusText: status === 'Reserved' ? 'Reservada' : status === 'Sold' ? 'Vendida' : 'Vencida',
        createdAt: new Date(createdAt).toISOString(),
        reservedUntil: new Date(createdAt + hours(DEFAULT_HOLD_HOURS)).toISOString(),
        total: roundMoney(lines.reduce((total, line) => total + line.subtotal, 0)),
        contactName,
        branch: BRANCHES[0].code,
        notes: status === 'Reserved' ? 'Paso a retirar el sábado por la mañana' : null,
        hasCompatibilityWarnings: false,
        lines,
        cancelReason: status === 'Expired' ? 'Venció el plazo de la reserva' : null,
        kind,
        mailQueued: true,
      };
    };
    return [
      build('RES-WEB-000012', 'cart', [{ product: available[0], quantity: 1, slot: null }, { product: available[1], quantity: 2, slot: null }], 'Reserved', now - hours(5)),
      build('ARM-WEB-000007', 'build', [{ product: available[2], quantity: 1, slot: 'cpu' }], 'Sold', now - hours(24 * 12)),
      build('RES-WEB-000003', 'cart', [{ product: available[3], quantity: 1, slot: null }], 'Expired', now - hours(24 * 30)),
    ];
  }
}
