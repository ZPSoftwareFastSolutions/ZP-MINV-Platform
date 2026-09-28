// Dominio de la tienda conectada (V6): lo que la web sabe de la empresa, la instantánea del catálogo y las reservas de
// armados. Son tipos del DOMINIO (ya traducidos): el JSON exacto de la API vive en 2-application/storefront/dto.ts.

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
  generatedAt: Date;
}

/** Horas que la tienda guarda un armado reservado (`Minv:Storefront:ReservationHours`). */
export const RESERVATION_HOURS = 48;

/** Límites del contrato para una reserva. */
export const RESERVATION_LIMITS = {
  maxLines: 20,
  maxQuantityPerLine: 16,
  nameMaxLength: 120,
  phoneMaxLength: 30,
  notesMaxLength: 500,
} as const;

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
  slot: SlotKey;
}

export interface ReservationRequest {
  lines: ReservationRequestLine[];
  contact: ReservationContact;
  notes?: string;
  /** Nombre del armado (por defecto la tienda usa «Armado web de <nombre>»). */
  name?: string;
  /** UUID único por intento: la API responde la misma reserva si la petición se repite. */
  idempotencyKey: string;
}

export interface ReservationLine {
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
  /** `ARM-WEB-000001`. */
  number: string;
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
