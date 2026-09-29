// Módulo «Caja» · facturación del SIN en la caja (funciones puras): la banda de estado fiscal, los datos del comprador
// (nominatividad: tipo de documento, NIT especiales, verificación y autocompletado), los estados del documento fiscal y lo
// que pasó al enviarlo al SIN después de cobrar (`DispatchFiscalDocumentsCommand`, como el escritorio). Los textos son
// los del escritorio (`FiscalText`, `BuyerForm`, `PosFiscalResult`). La validación que manda es la del servidor
// (`FiscalRules.EnsureBuyerDocument`): aquí solo se guía antes de enviar.

import { defineStatuses, statusOf } from '@/4-presentation/panel/kit';
import { formatDateTime } from '@/4-presentation/panel/lib';
import type { BuyerLookupData, BuyerLookupPayload, BuyerPayload, DispatchData, FiscalRowData, FiscalStateData, NitCheckData, SaleResultData, SiatItemData } from './types';

// ---------------------------------------------------------------------------------------------------- comprador

/** Tipos de documento de identidad del SIN (catálogo TIPO_DOCUMENTO_IDENTIDAD). */
export const DOC_CI = 1;
export const DOC_NIT = 5;

/** Datos del comprador que escribe el cajero (en la página, mientras se cobra). */
export interface BuyerDraft {
  documentType: number;
  documentNumber: string;
  complement: string;
  name: string;
  email: string;
  /** «Facturar aunque el NIT no sea válido» (código de excepción 1, solo con NIT). */
  exceptionRequested: boolean;
}

export const EMPTY_BUYER: BuyerDraft = { documentType: DOC_CI, documentNumber: '', complement: '', name: '', email: '', exceptionRequested: false };

const KNOWN_TYPES: Readonly<Record<number, { short: string; label: string }>> = {
  1: { short: 'CI', label: 'CI · Cédula de identidad' },
  2: { short: 'CEX', label: 'CEX · Cédula de extranjero' },
  3: { short: 'PAS', label: 'PAS · Pasaporte' },
  4: { short: 'OD', label: 'OD · Otro documento' },
  5: { short: 'NIT', label: 'NIT · Número de identificación tributaria' },
};

export interface DocumentTypeOption {
  value: string;
  label: string;
  short: string;
}

/** Tipos del catálogo sincronizado con el SIN (los vigentes) o, si todavía no se sincronizó, los 5 del SIN. */
export function documentTypeOptions(catalog: readonly SiatItemData[] | undefined): DocumentTypeOption[] {
  const current = (catalog ?? []).filter((item) => item.isCurrent);
  const codes = current.length > 0 ? [...new Set(current.map((item) => item.code))].sort((a, b) => a - b) : [1, 2, 3, 4, 5];
  return codes.map((code) => {
    const known = KNOWN_TYPES[code];
    if (known) return { value: String(code), ...known };
    const description = current.find((item) => item.code === code)?.description ?? String(code);
    return { value: String(code), short: String(code), label: description };
  });
}

/** «CI», «NIT»… */
export function documentShort(type: number): string {
  return KNOWN_TYPES[type]?.short ?? String(type);
}

/** NIT especial del SIN (va con tipo NIT; el código de excepción lo pone la emisión). */
export interface SpecialNit {
  code: string;
  label: string;
  explanation: string;
  defaultName: string | null;
}

export const SPECIAL_NITS: readonly SpecialNit[] = [
  {
    code: '99003',
    label: '99003 · Ventas menores',
    explanation: 'Ventas menores del día: resumen de ventas pequeñas a compradores que no dieron documento.',
    defaultName: 'VENTAS MENORES DEL DIA',
  },
  { code: '99002', label: '99002 · Control tributario', explanation: 'Control tributario: cuando un funcionario del SIN hace una compra de control.', defaultName: 'CONTROL TRIBUTARIO' },
  { code: '99001', label: '99001 · Consulados', explanation: 'Consulados, embajadas y organismos internacionales: escriba su nombre.', defaultName: null },
];

export function isSpecialNit(number: string): boolean {
  return SPECIAL_NITS.some((special) => special.code === number.trim());
}

/** Aplica un NIT especial: tipo NIT, su número y su nombre (si lo tiene). */
export function applySpecialNit(buyer: BuyerDraft, special: SpecialNit): BuyerDraft {
  return { ...buyer, documentType: DOC_NIT, documentNumber: special.code, complement: '', exceptionRequested: false, name: special.defaultName ?? buyer.name };
}

/** Cambia el tipo de documento: el complemento es solo de la CI y la excepción solo del NIT. */
export function withDocumentType(buyer: BuyerDraft, type: number): BuyerDraft {
  return { ...buyer, documentType: type, complement: type === DOC_CI ? buyer.complement : '', exceptionRequested: type === DOC_NIT ? buyer.exceptionRequested : false };
}

export interface BuyerErrors {
  documentNumber?: string;
  complement?: string;
  email?: string;
}

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

export function isEmail(text: string): boolean {
  return EMAIL.test(text.trim());
}

/**
 * Guía antes de enviar (la regla que manda es la del servidor): con CI o NIT solo dígitos, hasta 20 caracteres, el
 * complemento solo con CI (hasta 5) y un correo con forma de correo. `requireDocument`: la venta es a «consumidor final»
 * y la factura tiene que llevar el documento de alguien.
 */
export function buyerErrors(buyer: BuyerDraft, requireDocument: boolean): BuyerErrors {
  const errors: BuyerErrors = {};
  const number = buyer.documentNumber.trim();
  if (number.length === 0) {
    if (requireDocument) {
      errors.documentNumber = 'Toda venta facturada lleva el documento del comprador (CI, NIT, pasaporte…). Si no lo dio, use el NIT especial 99003 · Ventas menores.';
    }
  } else if ((buyer.documentType === DOC_CI || buyer.documentType === DOC_NIT) && !/^\d+$/.test(number)) {
    errors.documentNumber = `El ${documentShort(buyer.documentType)} lleva solo números (sin puntos, guiones ni espacios).`;
  } else if (number.length > 20) {
    errors.documentNumber = 'El número de documento tiene como máximo 20 caracteres.';
  }
  const complement = buyer.complement.trim();
  if (complement.length > 0 && buyer.documentType !== DOC_CI) errors.complement = 'El complemento se usa solo con la cédula de identidad.';
  else if (complement.length > 5) errors.complement = 'El complemento tiene como máximo 5 caracteres.';
  if (buyer.email.trim().length > 0 && !isEmail(buyer.email)) errors.email = 'Escriba un correo válido, por ejemplo nombre@correo.com.';
  return errors;
}

/** Datos del comprador para el comando (null si no se escribió el número: se factura con los datos del cliente). */
export function buyerPayload(buyer: BuyerDraft): BuyerPayload | null {
  const number = buyer.documentNumber.trim();
  if (number.length === 0) return null;
  const complement = buyer.complement.trim().toUpperCase();
  const name = buyer.name.trim();
  const email = buyer.email.trim();
  return {
    documentType: buyer.documentType,
    documentNumber: number,
    complement: buyer.documentType === DOC_CI && complement.length > 0 ? complement : null,
    name: name.length > 0 ? name : null,
    email: email.length > 0 ? email : null,
    exceptionRequested: buyer.documentType === DOC_NIT && buyer.exceptionRequested,
  };
}

/** Pedido de «¿ya compró antes?» (null si no conviene buscar: vacío, mal escrito o NIT especial). */
export function lookupPayload(buyer: BuyerDraft): BuyerLookupPayload | null {
  const number = buyer.documentNumber.trim();
  if (number.length === 0 || isSpecialNit(number) || buyerErrors(buyer, false).documentNumber) return null;
  const complement = buyer.complement.trim().toUpperCase();
  return { documentType: buyer.documentType, documentNumber: number, complement: buyer.documentType === DOC_CI && complement.length > 0 ? complement : null };
}

/** Autocompleta con el cliente que ya compró antes: nombre y correo SOLO si están vacíos (lo escrito manda). */
export function applyLookup(buyer: BuyerDraft, found: BuyerLookupData): BuyerDraft {
  if (!found.found) return buyer;
  return {
    ...buyer,
    name: buyer.name.trim().length > 0 ? buyer.name : (found.name ?? ''),
    email: buyer.email.trim().length > 0 ? buyer.email : (found.email ?? ''),
  };
}

export interface FiscalMessage {
  tone: 'success' | 'warning' | 'danger' | 'info';
  text: string;
}

/** Qué se sabe del comprador encontrado: «Cliente CLI-0042: ya compró antes» y su última verificación del NIT. */
export function lookupMessages(found: BuyerLookupData): FiscalMessage[] {
  const messages: FiscalMessage[] = [];
  if (found.found) messages.push({ tone: 'info', text: found.customerCode ? `Cliente ${found.customerCode}: ya compró antes.` : 'Este comprador ya compró antes.' });
  if (found.nitValid === true) messages.push({ tone: 'success', text: 'NIT verificado en el Padrón (activo).' });
  if (found.nitValid === false) messages.push({ tone: 'danger', text: 'La última verificación dio NIT no válido.' });
  return messages;
}

/** El NIT escrito como número (null si no es un NIT que se pueda verificar). */
export function nitToVerify(buyer: BuyerDraft): number | null {
  const number = buyer.documentNumber.trim();
  if (buyer.documentType !== DOC_NIT || !/^\d{1,15}$/.test(number) || isSpecialNit(number)) return null;
  return Number(number);
}

/** Resultado de la verificación del NIT en el Padrón del SIN. */
export function nitCheckMessage(result: NitCheckData): FiscalMessage {
  const description = plainMessage(result.description);
  return result.isValid
    ? { tone: 'success', text: `NIT activo en el Padrón · ${description}` }
    : { tone: 'danger', text: `${description} · Puede facturar igual marcando «Facturar aunque el NIT no sea válido».` };
}

/** Quita el ✔ / ⚠ / ✖ del principio de un mensaje del servidor (la pantalla pone su propio ícono). */
export function plainMessage(text: string | null | undefined): string {
  return (text ?? '').replace(/^[\s✔⚠✖]+/u, '').trim();
}

// ---------------------------------------------------------------------------------------------------- banda fiscal

export interface FiscalBand {
  tone: 'success' | 'warning' | 'danger' | 'info';
  title: string;
  detail: string;
}

/** Banda de estado fiscal de la caja (solo si la empresa factura), como el escritorio. */
export function fiscalBand(state: FiscalStateData | undefined): FiscalBand | null {
  if (!state || !state.billingEnabled) return null;
  const detail = plainMessage(state.message);
  if (!state.ready) {
    return state.mode === 'ManualContingency'
      ? { tone: 'danger', title: 'Contingencia manual: use el talonario CAFC', detail }
      : { tone: 'danger', title: 'La caja todavía no puede facturar', detail };
  }
  if (state.mode === 'Offline') return { tone: 'warning', title: 'Fuera de línea: las facturas se envían solas al volver la conexión', detail };
  if (state.mode === 'Recovering') return { tone: 'warning', title: 'Recuperando la conexión con el SIN', detail };
  const title = state.pointOfSaleCode != null ? `Facturación en línea · punto de venta ${state.pointOfSaleCode}` : 'Facturación en línea';
  const warned = state.message.trimStart().startsWith('⚠') || state.pendingHomologation > 0;
  return { tone: state.mode === 'Online' && !warned ? 'success' : 'warning', title, detail };
}

// ---------------------------------------------------------------------------------------------------- documento fiscal

/** Estado del documento fiscal (enumeración `FiscalDocumentStatus` del servidor). */
export const FISCAL_STATUSES = defineStatuses({
  Pending: { label: 'Pendiente de envío', tone: 'warning' },
  Valid: { label: 'Válida', tone: 'success' },
  Rejected: { label: 'Rechazada', tone: 'danger' },
  NoResponse: { label: 'Sin respuesta', tone: 'warning' },
  Offline: { label: 'Fuera de línea', tone: 'info' },
  InPackage: { label: 'En paquete', tone: 'info' },
  PackageRejected: { label: 'Observada', tone: 'danger' },
  DuplicateToVoid: { label: 'Duplicada: anular', tone: 'danger' },
  Voided: { label: 'Anulada', tone: 'neutral' },
  Discarded: { label: 'Descartada', tone: 'neutral' },
});

export function fiscalStatusLabel(status: string | null | undefined): string {
  return statusOf(FISCAL_STATUSES, status).label;
}

/** Qué significa el estado del documento para el cajero (como el escritorio). */
export function fiscalExplanation(status: string | null | undefined): string {
  switch (status) {
    case 'Valid':
      return 'El SIN recibió y validó la factura.';
    case 'Offline':
      return 'No hubo comunicación con el SIN: la factura es válida para el cliente y se enviará sola en un paquete al volver la conexión.';
    case 'Rejected':
    case 'PackageRejected':
      return 'El SIN no aceptó la factura. Revise los mensajes y re-emítala con los datos corregidos desde Facturación › Documentos (la venta ya está cobrada).';
    case 'Pending':
      return 'Se envía al SIN automáticamente en los próximos segundos.';
    case 'NoResponse':
      return 'Se perdió la respuesta del SIN: la venta se re-emite fuera de línea y esta factura se verifica al volver la conexión.';
    case 'InPackage':
      return 'Enviada en un paquete de contingencia; falta la validación del SIN.';
    default:
      return 'Consulte el detalle en Facturación › Documentos.';
  }
}

/** Qué pasó con la factura después de cobrar. */
export interface FiscalOutcome {
  /** 'sending': se está enviando al SIN · 'queued': se envía sola (la sesión no puede enviarla) · 'done': terminó. */
  phase: 'sending' | 'queued' | 'done';
  /** El documento DEFINITIVO (si el SIN no respondió y se re-emitió fuera de línea, ese). */
  row: FiscalRowData | null;
  notice: string | null;
  messages: readonly string[];
}

/** Estado fiscal al terminar de cobrar (antes de enviar al SIN). null si la venta no tiene documento fiscal. */
export function initialFiscal(sale: SaleResultData, canDispatch: boolean): FiscalOutcome | null {
  if (!sale.fiscalDocumentId) return null;
  return canDispatch
    ? { phase: 'sending', row: null, notice: null, messages: [] }
    : { phase: 'queued', row: null, notice: 'La factura se envía al SIN automáticamente en los próximos segundos.', messages: [] };
}

/** Lo que dejó el envío al SIN (o por qué no se pudo enviar ahora: se envía sola). */
export function fiscalAfterDispatch(sale: SaleResultData, outcome: { ok: true; result: DispatchData } | { ok: false; message: string }): FiscalOutcome {
  if (!outcome.ok) {
    return { phase: 'done', row: null, notice: `La factura quedó pendiente de envío y se envía sola. ${outcome.message}`.trim(), messages: [] };
  }
  const { result } = outcome;
  const row = result.documents.find((document) => document.saleNumber === sale.invoiceNumber) ?? result.documents[0] ?? null;
  const notice = result.wentOffline > 0 ? 'Sin comunicación con el SIN: la caja pasó a fuera de línea y la factura se envía sola al volver la conexión.' : null;
  return { phase: 'done', row, notice, messages: result.messages.map(plainMessage).filter((message) => message.length > 0) };
}

/** Número, CUF, estado e id del documento que ve el cajero: el definitivo si ya se envió; si no, el de la venta. */
export interface FiscalSummary {
  documentId: string | null;
  number: number | null;
  cuf: string | null;
  status: string | null;
  buyer: string | null;
}

export function fiscalSummary(sale: SaleResultData, outcome: FiscalOutcome | null): FiscalSummary {
  const row = outcome?.row;
  if (row) return { documentId: row.id, number: row.number, cuf: row.cuf, status: row.status, buyer: `${row.buyerName} · ${row.buyerDocument}` };
  return { documentId: sale.fiscalDocumentId, number: sale.fiscalNumber, cuf: sale.cuf, status: sale.fiscalStatus, buyer: null };
}

/** «Factura N° 45 válida», «… rechazada por el SIN»… */
export function fiscalHeadline(summary: FiscalSummary): string {
  const number = summary.number != null ? `Factura N° ${summary.number}` : 'Factura';
  switch (summary.status) {
    case 'Valid':
      return `${number} válida`;
    case 'Offline':
      return `${number} emitida fuera de línea`;
    case 'Rejected':
    case 'PackageRejected':
      return `${number} rechazada por el SIN`;
    case 'Pending':
      return `${number} pendiente de envío`;
    default:
      return `${number} · ${fiscalStatusLabel(summary.status).toLocaleLowerCase('es')}`;
  }
}

/**
 * Fecha y hora fiscal: el SIN usa la hora de Bolivia y el servidor la manda SIN zona («2026-09-29T10:32:05.123»); se
 * muestra tal cual, sin correrla. Con zona, se muestra con la hora de La Paz.
 */
export function formatFiscalTime(value: string | null | undefined): string {
  const text = (value ?? '').trim();
  if (text.length === 0) return '—';
  if (/(?:Z|[+-]\d{2}:?\d{2})$/i.test(text)) return formatDateTime(text);
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})(?::(\d{2}))?/.exec(text);
  if (!match) return formatDateTime(text);
  return `${match[3]}/${match[2]}/${match[1]} ${match[4]}:${match[5]}${match[6] ? `:${match[6]}` : ''}`;
}
