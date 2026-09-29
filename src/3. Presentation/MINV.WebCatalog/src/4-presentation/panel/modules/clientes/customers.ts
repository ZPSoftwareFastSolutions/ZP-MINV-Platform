// Módulo «Clientes» · funciones puras (sin React): cómo se presenta cada cliente (con sus datos de factura), los filtros de
// la lista, las columnas del CSV, el resumen de la cartera y el formulario de alta y edición con las reglas del SIN para
// los datos de factura (las mismas del servidor: CI y NIT solo dígitos; complemento solo con CI; hasta 20 caracteres).
//
// Los tipos del servidor SALEN DEL CONTRATO (regla P-07). Las validaciones de aquí son COMODIDAD: el servidor decide.

import type { RpcRequestOf, RpcResponseOf } from '@/4-presentation/app/contract';
import { defineStatuses, statusOf } from '@/4-presentation/panel/kit';
import { matchesSearch, roundTo, toDate, type CsvColumn } from '@/4-presentation/panel/lib';

// ---------------------------------------------------------------------------------------------------- tipos del contrato

/** Un cliente de `GetCustomersQuery`. */
export type CustomerRecord = RpcResponseOf<'GetCustomersQuery'>['customers'][number];
/** Una categoría de cliente. */
export type CategoryRecord = RpcResponseOf<'GetCustomersQuery'>['categories'][number];
/** Tipo de documento y complemento de un cliente (`GetCustomerFiscalIdentitiesQuery`). */
export type IdentityRecord = RpcResponseOf<'GetCustomerFiscalIdentitiesQuery'>[number];
export type SaveCustomerPayload = RpcRequestOf<'SaveCustomerCommand'>;
export type SaveIdentityPayload = RpcRequestOf<'SaveCustomerFiscalIdentityCommand'>;
export type NitCheck = RpcResponseOf<'VerifyNitCommand'>;

// ---------------------------------------------------------------------------------------------------- constantes

/** Código del consumidor final (no se puede desactivar). */
export const FINAL_CONSUMER = 'CF';

/** Tipos de documento de identidad del SIN (catálogo TIPO_DOCUMENTO_IDENTIDAD; los 5 del XSD, como el escritorio). */
export const DOCUMENT_TYPES: readonly { code: number; short: string; label: string }[] = [
  { code: 1, short: 'CI', label: 'CI · Cédula de identidad' },
  { code: 2, short: 'CEX', label: 'CEX · Cédula de extranjero' },
  { code: 3, short: 'PAS', label: 'PAS · Pasaporte' },
  { code: 4, short: 'OD', label: 'OD · Otro documento' },
  { code: 5, short: 'NIT', label: 'NIT · Número de identificación tributaria' },
];
export const DOCUMENT_CI = 1;
export const DOCUMENT_NIT = 5;

export function documentShort(type: number | null | undefined): string | null {
  return DOCUMENT_TYPES.find((item) => item.code === type)?.short ?? null;
}

/** Largos máximos (los del servidor). */
export const LIMITS = { name: 150, taxId: 30, documentNumber: 20, complement: 5, email: 150, phone: 40 } as const;

export const CUSTOMER_STATES = defineStatuses({
  activo: { label: 'Activo', tone: 'success' },
  inactivo: { label: 'Inactivo', tone: 'neutral' },
});

export const INVOICE_DATA_OPTIONS: readonly { value: string; label: string }[] = [
  { value: 'con', label: 'Con datos de factura' },
  { value: 'sin', label: 'Sin datos de factura' },
];

export const ORIGIN_OPTIONS: readonly { value: string; label: string }[] = [
  { value: 'web', label: 'Clientes web (tienda en línea)' },
  { value: 'tienda', label: 'Clientes de la tienda' },
];

/** ¿Es un cliente que se registró en la tienda web? (código `WEB-…`). */
export function isWebCustomer(code: string | null | undefined): boolean {
  return (code ?? '').trim().toUpperCase().startsWith('WEB-');
}

// ---------------------------------------------------------------------------------------------------- lista

/** Un cliente listo para la tabla. */
export interface CustomerItem {
  key: string;
  row: CustomerRecord;
  documentType: number | null;
  complement: string | null;
  /** Tiene tipo y número de documento: se le puede facturar a su nombre. */
  hasInvoiceData: boolean;
  webCustomer: boolean;
  state: keyof typeof CUSTOMER_STATES;
  /** «correo · teléfono». */
  contact: string;
  /** «CI 1234567-1A» · «NIT 1020304050» · «1234567» (sin tipo) · null. */
  document: string | null;
}

export function toCustomerItems(customers: readonly CustomerRecord[], identities: readonly IdentityRecord[]): CustomerItem[] {
  const byCode = new Map(identities.map((row) => [row.code.toUpperCase(), row]));
  return customers.map((row) => {
    const identity = byCode.get(row.code.toUpperCase()) ?? null;
    const documentType = identity?.documentType ?? null;
    const complement = identity?.complement ?? null;
    const number = identity?.documentNumber ?? row.taxId;
    const short = documentShort(documentType);
    const document = number ? `${short ? `${short} ` : ''}${number}${complement ? `-${complement}` : ''}` : null;
    return {
      key: row.code,
      row,
      documentType,
      complement,
      hasInvoiceData: documentType !== null && Boolean(number && number.trim()),
      webCustomer: isWebCustomer(row.code),
      state: row.isActive ? 'activo' : 'inactivo',
      contact: [row.email, row.phone].filter((value) => value && value.trim()).join(' · '),
      document,
    };
  });
}

/** Filtros de la lista (en la dirección). */
export const CUSTOMER_FILTERS = { q: '', categoria: '', estado: '', factura: '', origen: '' };
export type CustomerFilters = typeof CUSTOMER_FILTERS;

/** Aplica los filtros (búsqueda por nombre, código, NIT/CI, correo o teléfono, sin acentos). */
export function filterCustomers(items: readonly CustomerItem[], filters: CustomerFilters): CustomerItem[] {
  return items.filter(
    (item) =>
      (!filters.categoria || item.row.categoryCode === filters.categoria) &&
      (!filters.estado || item.state === filters.estado) &&
      (!filters.factura || (filters.factura === 'con') === item.hasInvoiceData) &&
      (!filters.origen || (filters.origen === 'web') === item.webCustomer) &&
      matchesSearch(filters.q, [item.row.name, item.row.code, item.row.taxId, item.document, item.row.email, item.row.phone]),
  );
}

/** Opciones del filtro «Categoría» con cuántos clientes tiene cada una (como el escritorio). */
export function categoryOptions(categories: readonly CategoryRecord[], items: readonly CustomerItem[]): { value: string; label: string }[] {
  return categories.map((category) => ({
    value: category.code,
    label: `${category.name} (${items.filter((item) => item.row.categoryCode === category.code).length})`,
  }));
}

export function stateLabel(state: string): string {
  return statusOf(CUSTOMER_STATES, state).label;
}

export interface CartSummary {
  active: number;
  total: number;
  buyers: number;
  purchases: number;
  /** Vendido a clientes identificados (sin el consumidor final). */
  soldToCustomers: number;
  finalConsumer: number;
  web: number;
  withInvoiceData: number;
  top: { name: string; total: number; purchases: number } | null;
}

/** Resumen de la cartera (lo que el escritorio mostraba en tarjetas). */
export function cartSummary(items: readonly CustomerItem[]): CartSummary {
  const named = items.filter((item) => item.row.code !== FINAL_CONSUMER);
  const top = named.reduce<CustomerItem | null>((best, item) => (item.row.total > (best?.row.total ?? 0) ? item : best), null);
  return {
    active: items.filter((item) => item.row.isActive).length,
    total: items.length,
    buyers: items.filter((item) => item.row.purchases > 0).length,
    purchases: items.reduce((sum, item) => sum + item.row.purchases, 0),
    soldToCustomers: roundTo(
      named.reduce((sum, item) => sum + item.row.total, 0),
      2,
    ),
    finalConsumer: roundTo(
      items.filter((item) => item.row.code === FINAL_CONSUMER).reduce((sum, item) => sum + item.row.total, 0),
      2,
    ),
    web: items.filter((item) => item.webCustomer).length,
    withInvoiceData: items.filter((item) => item.hasInvoiceData).length,
    top: top ? { name: top.row.name, total: top.row.total, purchases: top.row.purchases } : null,
  };
}

export const CUSTOMERS_CSV: readonly CsvColumn<CustomerItem>[] = [
  { header: 'Código', value: (item) => item.row.code },
  { header: 'Nombre', value: (item) => item.row.name },
  { header: 'Tipo de documento', value: (item) => documentShort(item.documentType) },
  { header: 'NIT / CI', value: (item) => item.row.taxId },
  { header: 'Complemento', value: (item) => item.complement },
  { header: 'Correo', value: (item) => item.row.email },
  { header: 'Teléfono', value: (item) => item.row.phone },
  { header: 'Categoría', value: (item) => item.row.category },
  { header: 'Compras', value: (item) => item.row.purchases },
  { header: 'Total', value: (item) => item.row.total },
  { header: 'Última compra', value: (item) => toDate(item.row.lastPurchase) },
  { header: 'Activo', value: (item) => item.row.isActive },
  { header: 'Cliente web', value: (item) => item.webCustomer },
];

// ---------------------------------------------------------------------------------------------------- formulario

/** Lo que se edita en el diálogo (textos tal como se escriben). */
export interface CustomerDraft {
  name: string;
  categoryCode: string;
  /** '' = sin tipo de documento; si no, el código del SIN como texto ('1' … '5'). */
  documentType: string;
  taxId: string;
  complement: string;
  email: string;
  phone: string;
  isActive: boolean;
}

/** El formulario: vacío (categoría GENERAL o la primera) o con los datos del cliente. */
export function draftOf(item: CustomerItem | null, categories: readonly CategoryRecord[]): CustomerDraft {
  const fallback = categories.find((category) => category.code.toUpperCase() === 'GENERAL')?.code ?? categories[0]?.code ?? '';
  return {
    name: item?.row.name ?? '',
    categoryCode: item?.row.categoryCode || fallback,
    documentType: item?.documentType ? String(item.documentType) : '',
    taxId: item?.row.taxId ?? '',
    complement: item?.complement ?? '',
    email: item?.row.email ?? '',
    phone: item?.row.phone ?? '',
    isActive: item?.row.isActive ?? true,
  };
}

export type CustomerErrors = Partial<Record<'name' | 'categoryCode' | 'documentType' | 'taxId' | 'complement' | 'email' | 'phone' | 'isActive', string>>;

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

function blank(text: string): string | null {
  return text.trim().length > 0 ? text.trim() : null;
}

/** Revisa el formulario con las reglas del servidor. `fiscal` = se editan los datos de factura. Vacío = está bien. */
export function customerProblems(draft: CustomerDraft, code: string | null, fiscal: boolean): CustomerErrors {
  const errors: CustomerErrors = {};
  const name = draft.name.trim();
  if (name.length === 0) errors.name = 'Indique el nombre del cliente.';
  else if (name.length > LIMITS.name) errors.name = `Use como máximo ${LIMITS.name} caracteres.`;
  if (!draft.categoryCode) errors.categoryCode = 'Elija la categoría del cliente.';
  const email = draft.email.trim();
  if (email && (!EMAIL.test(email) || email.length > LIMITS.email)) errors.email = 'El correo no es válido (por ejemplo nombre@dominio.com).';
  if (draft.phone.trim().length > LIMITS.phone) errors.phone = `Use como máximo ${LIMITS.phone} caracteres.`;
  if (!draft.isActive && code === FINAL_CONSUMER) errors.isActive = 'El consumidor final no se puede desactivar.';

  const number = draft.taxId.trim();
  const type = fiscal && draft.documentType ? Number(draft.documentType) : null;
  if (type === null) {
    if (number.length > LIMITS.taxId) errors.taxId = `Use como máximo ${LIMITS.taxId} caracteres.`;
  } else {
    if (number.length === 0) errors.taxId = 'Con tipo de documento, el número es obligatorio.';
    else if (number.length > LIMITS.documentNumber) errors.taxId = `El número de documento tiene como máximo ${LIMITS.documentNumber} caracteres.`;
    else if ((type === DOCUMENT_CI || type === DOCUMENT_NIT) && !/^\d+$/.test(number)) errors.taxId = `El ${type === DOCUMENT_NIT ? 'NIT' : 'CI'} lleva solo números (sin puntos ni guiones).`;
    const complement = draft.complement.trim();
    if (type === DOCUMENT_CI && complement.length > LIMITS.complement) errors.complement = `El complemento tiene como máximo ${LIMITS.complement} caracteres.`;
  }
  return errors;
}

export function hasProblems(errors: CustomerErrors): boolean {
  return Object.values(errors).some(Boolean);
}

/** El pedido de `SaveCustomerCommand` (todos los parámetros; `code` null = cliente nuevo, el servidor le da el código). */
export function toSaveCustomer(code: string | null, draft: CustomerDraft): SaveCustomerPayload {
  return {
    code,
    name: draft.name.trim(),
    taxId: blank(draft.taxId),
    email: blank(draft.email),
    phone: blank(draft.phone),
    categoryCode: draft.categoryCode,
    isActive: draft.isActive,
  };
}

/** El pedido de `SaveCustomerFiscalIdentityCommand` (complemento solo con CI, en mayúsculas). */
export function toSaveIdentity(code: string, draft: CustomerDraft): SaveIdentityPayload {
  const type = draft.documentType ? Number(draft.documentType) : null;
  return {
    code,
    documentType: type,
    documentNumber: blank(draft.taxId),
    complement: type === DOCUMENT_CI ? (blank(draft.complement)?.toUpperCase() ?? null) : null,
  };
}

/** El pedido de `SaveCustomerCommand` para activar o desactivar un cliente (sus datos, sin cambios). */
export function toggleActivePayload(item: CustomerItem, isActive: boolean): SaveCustomerPayload {
  return {
    code: item.row.code,
    name: item.row.name,
    taxId: item.row.taxId,
    email: item.row.email,
    phone: item.row.phone,
    categoryCode: item.row.categoryCode,
    isActive,
  };
}

/** ¿Se puede verificar el NIT escrito? (tipo NIT y solo dígitos, sin pasarse de lo que cabe en un número). */
export function verifiableNit(draft: CustomerDraft): number | null {
  const number = draft.taxId.trim();
  if (Number(draft.documentType) !== DOCUMENT_NIT || !/^\d{1,15}$/.test(number)) return null;
  return Number(number);
}

/** Quita el ✔ / ⚠ / ✖ del principio de un mensaje del servidor. */
export function plainMessage(message: string): string {
  return message.replace(/^[✔⚠✖\s]+/u, '').trim();
}
