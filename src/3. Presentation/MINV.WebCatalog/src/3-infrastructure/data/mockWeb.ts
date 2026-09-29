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
  type BranchAccess,
  type ChangePasswordCommand,
  type CreateMyReservationCommand,
  type MyAccountView,
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
}

/** Usuarios de muestra del modo mock. */
export const DEMO_USERS: readonly DemoUser[] = [
  { kind: 'staff', label: 'Personal (administrador)', name: 'Andrea Quiroga', email: 'admin@techzone.example', password: 'Demo1234' },
  { kind: 'customer', label: 'Cliente', name: 'Valentina Aguirre', email: 'cliente@techzone.example', password: 'Demo1234' },
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

interface StoredUser {
  kind: SessionKind;
  name: string;
  email: string;
  phone: string;
  password: string;
  roles: string[];
  permissions: string[];
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
  private currentEmail: string | null = null;
  private sequence = 0;

  constructor(options: InMemoryWebOptions = {}) {
    this.products = options.products ?? [];
    this.now = options.now ?? (() => new Date());
    this.stock = options.stock ?? null;
    for (const user of options.users ?? DEMO_USERS) this.addUser(user);

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
    const stored: StoredUser = {
      kind: user.kind,
      name: user.name,
      email: normalizeEmail(user.email),
      phone: staff ? '' : '+591 71234567',
      password: user.password,
      roles: staff ? ['ADMIN'] : ['CLIENTE'],
      permissions: staff ? PERMISSION_LIST.map((permission) => permission.code).filter((code) => !code.startsWith('account.')) : [...CUSTOMER_PERMISSIONS],
      mustChangePassword: false,
      activeBranchId: staff ? BRANCHES[0].id : null,
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
    return { allBranches: true, branches: BRANCHES, activeBranchId: user.activeBranchId };
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
    const user = this.users.get(normalizeEmail(credentials.email));
    const now = this.now().getTime();
    const locked = new WebApiError({
      kind: 'authentication',
      status: 401,
      message: `Cuenta bloqueada por ${MAX_FAILED_ATTEMPTS} intentos fallidos: espere ${LOCKOUT_MINUTES} minutos.`,
    });
    // El mismo mensaje si el correo no existe o si la contraseña no coincide (regla P-03).
    if (!user) throw new WebApiError({ kind: 'authentication', status: 401, message: LOGIN_FAILED });
    if (user.lockedUntil > now) throw locked;
    if (user.password !== credentials.password) {
      user.failedAttempts += 1;
      if (user.failedAttempts >= MAX_FAILED_ATTEMPTS) {
        user.failedAttempts = 0;
        user.lockedUntil = now + LOCKOUT_MINUTES * 60_000;
        throw locked;
      }
      throw new WebApiError({ kind: 'authentication', status: 401, message: LOGIN_FAILED });
    }
    user.failedAttempts = 0;
    user.lockedUntil = 0;
    this.currentEmail = user.email;
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
    const result = this.execute(user, operation, payload);
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
      default:
        throw new WebApiError({ kind: 'unsupported', status: 400, message: 'Operación desconocida o pedido mal formado.' });
    }
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
    if (command.branchId !== null && !BRANCHES.some((branch) => branch.id === command.branchId)) {
      throw new WebApiError({ kind: 'access_denied', status: 403, message: 'Esa sucursal no está dentro de su alcance.' });
    }
    user.activeBranchId = command.branchId;
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
