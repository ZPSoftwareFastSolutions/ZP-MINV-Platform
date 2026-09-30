// Módulo «Proveedores» · funciones puras (sin React): cómo se presenta cada proveedor, los filtros de la lista, las
// columnas del CSV, el resumen plegado (los indicadores del escritorio) y el formulario de alta y edición con las reglas
// del servidor (razón social obligatoria, días de entrega de 0 a 365, correo válido) y los largos de la base de datos.
//
// Los tipos del servidor SALEN DEL CONTRATO (regla P-07). Las validaciones de aquí son COMODIDAD: el servidor decide.

import type { RpcRequestOf, RpcResponseOf } from '@/4-presentation/app/contract';
import { ROUTES } from '@/4-presentation/app/routes';
import { defineStatuses, type SelectOption } from '@/4-presentation/panel/kit';
import { matchesSearch, roundTo, type CsvColumn } from '@/4-presentation/panel/lib';

/** Un proveedor de `GetSuppliersQuery`. */
export type SupplierRecord = RpcResponseOf<'GetSuppliersQuery'>[number];
export type SaveSupplierPayload = RpcRequestOf<'SaveSupplierCommand'>;

/** Largos máximos (los de la base de datos). */
export const LIMITS = { name: 150, taxId: 30, contact: 120, phone: 40, email: 254 } as const;
/** Días de entrega que acepta el servidor. */
export const MAX_LEAD_DAYS = 365;

export const SUPPLIER_STATES = defineStatuses({
  activo: { label: 'Activo', tone: 'success' },
  inactivo: { label: 'Inactivo', tone: 'neutral' },
});

export const OPEN_ORDER_OPTIONS: readonly SelectOption[] = [
  { value: 'con', label: 'Con órdenes abiertas' },
  { value: 'sin', label: 'Sin órdenes abiertas' },
];

export const LEAD_FILTER_OPTIONS: readonly SelectOption[] = [
  { value: 'rapido', label: 'Hasta 3 días' },
  { value: 'semana', label: 'De 4 a 7 días' },
  { value: 'mas', label: 'Más de 7 días' },
];

/** Plazos que se ofrecen en la lista desplegable del formulario (con «Otro plazo» se escribe cualquiera de 0 a 365). */
export const LEAD_TIME_CHOICES: readonly number[] = [0, 1, 2, 3, 5, 7, 10, 15, 21, 30, 45, 60, 90];

export function daysText(days: number): string {
  if (days === 0) return 'El mismo día';
  return days === 1 ? '1 día' : `${days} días`;
}

/** Texto del servidor sin la marca «✔»/«✖» del principio. */
export function plainMessage(text: string | null | undefined): string {
  return (text ?? '').replace(/^[\s✔✖✓✗]+/u, '').trim();
}

// ---------------------------------------------------------------------------------------------------- lista

export interface SupplierItem {
  key: string;
  row: SupplierRecord;
  state: keyof typeof SUPPLIER_STATES;
  /** «contacto · teléfono · correo». */
  contact: string;
}

export function toSupplierItems(rows: readonly SupplierRecord[]): SupplierItem[] {
  return rows.map((row) => ({
    key: row.code,
    row,
    state: row.isActive ? 'activo' : 'inactivo',
    contact: [row.contact, row.phone, row.email].filter((value) => value && value.trim()).join(' · '),
  }));
}

/** Filtros de la lista (en la dirección): `?q=P002` (desde Órdenes de compra) busca también por código. */
export const SUPPLIER_FILTERS = { q: '', estado: '', ordenes: '', entrega: '' };
export type SupplierFilters = typeof SUPPLIER_FILTERS;

function matchesLead(days: number, filter: string): boolean {
  if (filter === 'rapido') return days <= 3;
  if (filter === 'semana') return days >= 4 && days <= 7;
  if (filter === 'mas') return days > 7;
  return true;
}

export function filterSuppliers(items: readonly SupplierItem[], filters: SupplierFilters): SupplierItem[] {
  return items.filter(
    (item) =>
      (!filters.estado || item.state === filters.estado) &&
      (!filters.ordenes || (filters.ordenes === 'con') === item.row.openOrders > 0) &&
      matchesLead(item.row.leadTimeDays, filters.entrega) &&
      matchesSearch(filters.q, [item.row.name, item.row.code, item.row.taxId, item.row.contact, item.row.phone, item.row.email]),
  );
}

export const SUPPLIERS_CSV: readonly CsvColumn<SupplierItem>[] = [
  { header: 'Código', value: (item) => item.row.code },
  { header: 'Razón social', value: (item) => item.row.name },
  { header: 'NIT', value: (item) => item.row.taxId },
  { header: 'Contacto', value: (item) => item.row.contact },
  { header: 'Teléfono', value: (item) => item.row.phone },
  { header: 'Correo', value: (item) => item.row.email },
  { header: 'Días de entrega', value: (item) => item.row.leadTimeDays },
  { header: 'Productos', value: (item) => item.row.products },
  { header: 'Órdenes abiertas', value: (item) => item.row.openOrders },
  { header: 'Comprado', value: (item) => item.row.purchased },
  { header: 'Activo', value: (item) => item.row.isActive },
];

export interface SuppliersSummary {
  active: number;
  total: number;
  products: number;
  openOrders: number;
  withOpenOrders: number;
  purchased: number;
  top: SupplierRecord | null;
  /** Promedio de días de entrega de los activos (null si no hay activos). */
  averageLead: number | null;
}

/** Los indicadores del escritorio: activos, órdenes abiertas, comprado (recibido) y entrega promedio. */
export function suppliersSummary(rows: readonly SupplierRecord[]): SuppliersSummary {
  const active = rows.filter((row) => row.isActive);
  const top = rows.reduce<SupplierRecord | null>((best, row) => (row.purchased > 0 && (!best || row.purchased > best.purchased) ? row : best), null);
  return {
    active: active.length,
    total: rows.length,
    products: rows.reduce((sum, row) => sum + row.products, 0),
    openOrders: rows.reduce((sum, row) => sum + row.openOrders, 0),
    withOpenOrders: rows.filter((row) => row.openOrders > 0).length,
    purchased: roundTo(
      rows.reduce((sum, row) => sum + row.purchased, 0),
      2,
    ),
    top,
    averageLead: active.length > 0 ? roundTo(active.reduce((sum, row) => sum + row.leadTimeDays, 0) / active.length, 1) : null,
  };
}

/** Sus órdenes de compra (módulo «Órdenes de compra»). */
export function ordersLink(code: string): string {
  return ROUTES.panelModule(`compras?proveedor=${encodeURIComponent(code)}`);
}

/** Orden nueva para este proveedor (módulo «Órdenes de compra»). */
export function newOrderLink(code: string): string {
  return ROUTES.panelModule(`compras?nueva=1&proveedor=${encodeURIComponent(code)}`);
}

// ---------------------------------------------------------------------------------------------------- formulario

export interface SupplierDraft {
  name: string;
  taxId: string;
  leadTimeDays: number | null;
  contactName: string;
  phone: string;
  email: string;
  isActive: boolean;
}

export function draftOf(row: SupplierRecord | null): SupplierDraft {
  return {
    name: row?.name ?? '',
    taxId: row?.taxId ?? '',
    leadTimeDays: row?.leadTimeDays ?? 3,
    contactName: row?.contact ?? '',
    phone: row?.phone ?? '',
    email: row?.email ?? '',
    isActive: row?.isActive ?? true,
  };
}

export function leadChoiceOptions(): SelectOption[] {
  return [...LEAD_TIME_CHOICES.map((days) => ({ value: String(days), label: daysText(days) })), { value: 'otro', label: 'Otro plazo…' }];
}

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

export type SupplierProblems = Partial<Record<keyof SupplierDraft, string>>;

export function supplierProblems(draft: SupplierDraft): SupplierProblems {
  const problems: SupplierProblems = {};
  const name = draft.name.trim();
  if (!name) problems.name = 'Indique la razón social.';
  else if (name.length > LIMITS.name) problems.name = `Hasta ${LIMITS.name} caracteres.`;
  if (draft.taxId.trim().length > LIMITS.taxId) problems.taxId = `Hasta ${LIMITS.taxId} caracteres.`;
  if (draft.leadTimeDays === null) problems.leadTimeDays = 'Indique los días de entrega.';
  else if (!Number.isInteger(draft.leadTimeDays) || draft.leadTimeDays < 0 || draft.leadTimeDays > MAX_LEAD_DAYS)
    problems.leadTimeDays = `Los días de entrega van de 0 a ${MAX_LEAD_DAYS}.`;
  if (draft.contactName.trim().length > LIMITS.contact) problems.contactName = `Hasta ${LIMITS.contact} caracteres.`;
  if (draft.phone.trim().length > LIMITS.phone) problems.phone = `Hasta ${LIMITS.phone} caracteres.`;
  const email = draft.email.trim();
  if (email && (!EMAIL.test(email) || email.length > LIMITS.email)) problems.email = 'El correo no es válido (por ejemplo nombre@dominio.com).';
  return problems;
}

function blank(value: string | null | undefined): string | null {
  const text = (value ?? '').trim();
  return text ? text : null;
}

/** El pedido de `SaveSupplierCommand` (código null = proveedor nuevo: el servidor le da el código P001…). */
export function toSavePayload(code: string | null, draft: SupplierDraft): SaveSupplierPayload {
  return {
    code,
    name: draft.name.trim(),
    taxId: blank(draft.taxId),
    leadTimeDays: draft.leadTimeDays ?? 0,
    contactName: blank(draft.contactName),
    phone: blank(draft.phone),
    email: blank(draft.email),
    isActive: draft.isActive,
  };
}

/** Activar o desactivar: los mismos datos con otro estado. */
export function toggleActivePayload(row: SupplierRecord, isActive: boolean): SaveSupplierPayload {
  return toSavePayload(row.code, { ...draftOf(row), isActive });
}
