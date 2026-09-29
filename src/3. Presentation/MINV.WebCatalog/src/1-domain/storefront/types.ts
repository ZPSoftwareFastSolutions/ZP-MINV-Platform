// Dominio de la tienda conectada (V6): lo que la web sabe de la empresa, la instantánea del catálogo y las reservas de
// armados. Son tipos del DOMINIO (ya traducidos): el JSON exacto de la API vive en 2-application/storefront/dto.ts.
// V7: las reservas pueden ser de un ARMADO (`build`, número ARM-WEB-…) o de un CARRITO (`cart`, número RES-WEB-…, líneas
// sin ranura), con los días para recogerla (1 a 3) y los datos opcionales para la factura; las horas que se guarda una
// reserva salen del catálogo (`reservationHours`, `maxHoldDays`), no de una constante de la web.

import type { DocumentTypeCode } from '@/1-domain/account/documents';
import type { BuildPreset, SlotKey } from '@/1-domain/builder/types';
import type { Brand, Category, Product } from '@/1-domain/catalog/types';

export interface StoreBranch {
  code: string;
  name: string;
}

export interface StoreCompany {
  code: string;
  name: string;
  /** Sucursales activas de la empresa. */
  branches: StoreBranch[];
}

/** Empresa y sucursal cuya disponibilidad muestra la tienda (donde se retira lo reservado). */
export interface StoreInfo {
  company: StoreCompany;
  branch: StoreBranch;
}

/** Todo lo que la web necesita para pintar el sitio, en una sola carga (`GET /storefront/v1/catalog`). */
export interface CatalogSnapshot {
  store: StoreInfo;
  categories: Category[];
  brands: Brand[];
  products: Product[];
  /** Armados sugeridos publicados desde el escritorio (puede estar vacío). */
  presets: BuildPreset[];
  /** V7: cuánto guarda la tienda una reserva y cuántos días puede pedir quien reserva (del servidor). */
  reservationPolicy: ReservationPolicy;
  generatedAt: Date;
}

/**
 * Plazos de una reserva según la configuración del servidor (`Minv:Storefront:ReservationHours` y
 * `MaxReservationHours`): las horas de una reserva que no indica los días y los días que se pueden pedir (1 a 3).
 */
export interface ReservationPolicy {
  /** Horas que se guarda una reserva sin días indicados (48 por defecto). */
  reservationHours: number;
  /** Días que puede pedir quien reserva para pasar a recogerla (1 a 3; 3 por defecto). */
  maxHoldDays: number;
}

/** Tope de días para recoger una reserva que acepta el contrato. */
export const HOLD_DAYS_LIMIT = 3;

/** Lo que vale cuando el catálogo no trae los plazos (servidor de la V6): 48 h y hasta 3 días. */
export const DEFAULT_RESERVATION_POLICY: ReservationPolicy = { reservationHours: 48, maxHoldDays: HOLD_DAYS_LIMIT };

/** Límites del contrato para una reserva. */
export const RESERVATION_LIMITS = {
  maxLines: 20,
  maxQuantityPerLine: 16,
  nameMaxLength: 120,
  phoneMaxLength: 30,
  emailMaxLength: 254,
  notesMaxLength: 500,
  /** Nombre o razón social para la factura. */
  buyerNameMaxLength: 150,
} as const;

/** `build`: armado de PC (ARM-WEB-…) · `cart`: carrito o artículo suelto (RES-WEB-…). */
export type ReservationKind = 'build' | 'cart';

/** Datos para la factura que deja quien reserva (opcionales; instantánea del visitante, regla P-05). */
export interface ReservationBuyer {
  documentType: DocumentTypeCode;
  documentNumber: string;
  /** Solo con cédula de identidad (hasta 5 caracteres). */
  complement?: string;
  /** Nombre o razón social para la factura (hasta 150 caracteres, en una línea). */
  name?: string;
}

export type ReservationStatus = 'Reserved' | 'Sold' | 'Cancelled' | 'Expired';

export interface ReservationContact {
  name: string;
  /** Teléfono o WhatsApp de Bolivia (7 u 8 dígitos, con o sin +591): es la clave para consultar o liberar la reserva. */
  phone: string;
  email?: string;
}

export interface ReservationRequestLine {
  sku: string;
  quantity: number;
  /** Ranura del armado; nula o ausente en las líneas de un carrito (V7). */
  slot?: SlotKey | null;
}

export interface ReservationRequest {
  lines: ReservationRequestLine[];
  contact: ReservationContact;
  notes?: string;
  /** Nombre del armado (por defecto la tienda usa «Armado web de <nombre>»). */
  name?: string;
  /** UUID único por intento: la API responde la misma reserva si la petición se repite. */
  idempotencyKey: string;
  /** V7: tipo de reserva (sin valor, la tienda la toma como armado). */
  kind?: ReservationKind;
  /** V7: días para pasar a recogerla (1 a `maxHoldDays`); sin valor, las horas configuradas. */
  holdDays?: number;
  /** V7: datos opcionales para la factura. */
  buyer?: ReservationBuyer;
}

export interface ReservationLine {
  /** Ranura del armado; texto vacío en las líneas de un carrito (el servidor la manda nula). */
  slot: string;
  sku: string;
  name: string;
  quantity: number;
  /** Precio congelado al reservar (Bs, IVA incluido). */
  unitPrice: number;
  subtotal: number;
}

/** Una reserva tal como la devuelve la tienda: nunca trae el teléfono ni el correo (regla S-06). */
export interface Reservation {
  /** `ARM-WEB-000001` (armado) o `RES-WEB-000001` (carrito). */
  number: string;
  /** V7: armado o carrito. */
  kind: ReservationKind;
  status: ReservationStatus;
  /** «Reservada», «Vendida», «Cancelada», «Vencida». */
  statusText: string;
  createdAt: Date;
  reservedUntil: Date;
  total: number;
  contactName: string;
  /** Código de la sucursal donde se retira. */
  branch: string;
  notes: string | null;
  hasCompatibilityWarnings: boolean;
  lines: ReservationLine[];
  cancelReason: string | null;
  /** La API devolvió una reserva ya creada con la misma llave (`Idempotent-Replayed`). */
  replayed: boolean;
  /** V7: el servidor dejó en cola el correo con el código y el detalle de la reserva. */
  mailQueued: boolean;
}

/** Pieza a la que le falta stock cuando la tienda rechaza una reserva (409). */
export interface StockShortage {
  sku: string;
  name: string;
  requested: number;
  available: number;
}

export function isReservationActive(reservation: Pick<Reservation, 'status'>): boolean {
  return reservation.status === 'Reserved';
}
