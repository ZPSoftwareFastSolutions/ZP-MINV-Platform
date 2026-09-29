// Mapeo del contrato del servidor (JSON) al dominio para la sesión y la cuenta del cliente. Lo que llega de la red es
// un dato EXTERNO: se lee campo por campo y con tolerancia (listas ausentes, nulos, campos nuevos), y se rechaza lo que
// no se reconoce en vez de propagarlo. Funciones puras.

import { isDocumentTypeCode } from '@/1-domain/account/documents';
import type { AccountUpdate, CustomerAccount } from '@/1-domain/account/types';
import { WebApiError } from '@/1-domain/auth/errors';
import { CUSTOMER_ROLE } from '@/1-domain/auth/permissions';
import type { BranchAccess, Session, SessionBranch, SessionKind } from '@/1-domain/auth/types';
import type { Reservation } from '@/1-domain/storefront/types';
import { toReservation, type StorefrontReservationLineDto, type StorefrontReservationViewDto } from '@/2-application/storefront';
import type { UpdateMyAccountCommand } from './contract';

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function str(value: unknown, fallback = ''): string {
  return typeof value === 'string' ? value : fallback;
}

function optionalStr(value: unknown): string | null {
  return typeof value === 'string' && value.trim().length > 0 ? value : null;
}

function num(value: unknown, fallback = 0): number {
  return typeof value === 'number' && Number.isFinite(value) ? value : fallback;
}

function strings(value: unknown): string[] {
  return Array.isArray(value) ? value.filter((item): item is string => typeof item === 'string') : [];
}

function unexpected(what: string): WebApiError {
  return new WebApiError({ kind: 'server', status: 0, message: `El servidor devolvió ${what} que la página no reconoce. Actualice la página e intente de nuevo.` });
}

// ---------------------------------------------------------------------------------------------------- sesión
function toBranch(value: unknown): SessionBranch | null {
  if (!isRecord(value) || typeof value.id !== 'string') return null;
  return { id: value.id, code: str(value.code), name: str(value.name) };
}

function toAccess(value: unknown): BranchAccess {
  if (!isRecord(value)) return { allBranches: false, branches: [], activeBranchId: null };
  const branches = (Array.isArray(value.branches) ? value.branches : []).map(toBranch).filter((branch): branch is SessionBranch => branch !== null);
  return { allBranches: value.allBranches === true, branches, activeBranchId: optionalStr(value.activeBranchId) };
}

/** `WebSession` del servidor → sesión del dominio. Sin correo no hay sesión reconocible. */
export function toSession(dto: unknown): Session {
  if (!isRecord(dto) || !optionalStr(dto.email)) throw unexpected('una sesión');
  const roles = strings(dto.roles);
  const declared = dto.kind === 'staff' || dto.kind === 'customer' ? (dto.kind as SessionKind) : null;
  // Si el servidor no indica el tipo, es cliente SOLO cuando su único rol es CLIENTE (misma regla del servidor).
  const kind: SessionKind = declared ?? (roles.length === 1 && roles[0] === CUSTOMER_ROLE ? 'customer' : 'staff');
  const email = str(dto.email).trim();
  return {
    displayName: optionalStr(dto.displayName)?.trim() ?? email,
    email,
    roles,
    permissions: strings(dto.permissions),
    mustChangePassword: dto.mustChangePassword === true,
    access: toAccess(dto.access),
    kind,
    expiresAt: new Date(str(dto.expiresAt)),
    serverVersion: str(dto.serverVersion),
    company: str(dto.company),
  };
}

// ---------------------------------------------------------------------------------------------------- cuenta
export function looksLikeAccount(value: unknown): boolean {
  return isRecord(value) && typeof value.name === 'string' && typeof value.email === 'string';
}

export function toCustomerAccount(dto: unknown): CustomerAccount {
  if (!isRecord(dto) || !looksLikeAccount(dto)) throw unexpected('unos datos de la cuenta');
  const documentType = isDocumentTypeCode(dto.documentType) ? dto.documentType : null;
  const documentNumber = optionalStr(dto.documentNumber);
  return {
    name: str(dto.name),
    email: str(dto.email),
    phone: str(dto.phone),
    documentType: documentNumber ? documentType : null,
    documentNumber: documentType ? documentNumber : null,
    complement: documentType === 1 ? optionalStr(dto.complement) : null,
  };
}

export function toUpdateAccountPayload(update: AccountUpdate): UpdateMyAccountCommand {
  return {
    name: update.name,
    phone: update.phone,
    documentType: update.documentType,
    documentNumber: update.documentNumber,
    complement: update.complement,
  };
}

// ---------------------------------------------------------------------------------------------------- reservas
export function looksLikeReservation(value: unknown): boolean {
  return isRecord(value) && typeof value.number === 'string' && typeof value.status === 'string' && Array.isArray(value.lines);
}

function toLineDto(value: unknown): StorefrontReservationLineDto | null {
  if (!isRecord(value) || typeof value.sku !== 'string') return null;
  const quantity = num(value.quantity, 1);
  const unitPrice = num(value.unitPrice);
  return {
    // En un carrito las líneas no tienen ranura.
    slot: optionalStr(value.slot),
    sku: value.sku,
    name: str(value.name, value.sku),
    quantity,
    unitPrice,
    subtotal: num(value.subtotal, unitPrice * quantity),
  };
}

/** `StorefrontReservationView` tal como llegó → reserva del dominio (mismo mapeo que la tienda pública). */
export function toAccountReservation(dto: unknown): Reservation {
  if (!isRecord(dto) || !looksLikeReservation(dto)) throw unexpected('una reserva');
  const view: StorefrontReservationViewDto = {
    number: str(dto.number),
    status: str(dto.status),
    statusText: str(dto.statusText),
    createdAt: str(dto.createdAt),
    reservedUntil: str(dto.reservedUntil),
    total: num(dto.total),
    contactName: str(dto.contactName),
    branch: str(dto.branch),
    notes: optionalStr(dto.notes),
    hasCompatibilityWarnings: dto.hasCompatibilityWarnings === true,
    lines: (dto.lines as unknown[]).map(toLineDto).filter((line): line is StorefrontReservationLineDto => line !== null),
    cancelReason: optionalStr(dto.cancelReason),
  };
  return toReservation(view);
}

export function toAccountReservations(dto: unknown): Reservation[] {
  if (!Array.isArray(dto)) throw unexpected('una lista de reservas');
  return dto.map(toAccountReservation);
}
