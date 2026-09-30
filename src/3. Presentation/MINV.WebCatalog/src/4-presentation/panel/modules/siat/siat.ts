// Módulo «Estado del SIAT» · funciones puras (sin React): estados y textos de los puntos de venta, eventos significativos,
// paquetes y talonarios CAFC (los del escritorio: `FiscalText`, `SiatStatusViewModel`), cuentas regresivas de los plazos,
// vigencia de CUIS y CUFD, hora fiscal, filtros y columnas del CSV, y la guía de la contingencia manual y de la
// transcripción de facturas manuales. La regla que manda es la del servidor: aquí solo se muestra, se filtra y se guía.

import type { RpcRequestOf, RpcResponseOf } from '@/4-presentation/app/contract';
import { defineStatuses, statusOf } from '@/4-presentation/panel/kit';
import { addDays, formatDateTime, formatTime, laPazToday, type CsvColumn } from '@/4-presentation/panel/lib';

// ---------------------------------------------------------------------------------------------------- tipos del contrato

export type SiatStatusData = RpcResponseOf<'GetSiatStatusQuery'>;
export type PointData = SiatStatusData['points'][number];
export type AlertData = SiatStatusData['alerts'][number];
export type EventData = RpcResponseOf<'GetSignificantEventsQuery'>[number];
export type PackageData = RpcResponseOf<'GetFiscalPackagesQuery'>[number];
export type CafcData = RpcResponseOf<'GetContingencyCodesQuery'>[number];
export type ServiceCallData = RpcResponseOf<'GetSiatServiceCallsQuery'>[number];
export type CatalogItemData = RpcResponseOf<'GetSiatCatalogQuery'>[number];
export type ProductData = RpcResponseOf<'GetSellableProductsQuery'>[number];
export type BuyerPayload = RpcRequestOf<'TranscribeManualInvoiceCommand'>['buyer'];
export type TranscribeLinePayload = RpcRequestOf<'TranscribeManualInvoiceCommand'>['lines'][number];

/** Catálogos del SIN que usa la pantalla (nombres de `SiatCatalogNames`). */
export const CATALOG_EVENTS = 'EVENTOS_SIGNIFICATIVOS';
export const CATALOG_DOCUMENT_TYPES = 'TIPO_DOCUMENTO_IDENTIDAD';

/** Documentos sector del SIN (`SiatCodes`). */
export const SECTOR_PURCHASE_SALE = 1;
export const SECTOR_CREDIT_DEBIT_NOTE = 24;
export const ENVIRONMENT_TEST = 2;

// ---------------------------------------------------------------------------------------------------- estados

/** Modo de conexión de un punto de venta (`SiatConnectionMode`); «Closed» = cerrado en el SIN. */
export const MODES = defineStatuses({
  Online: { label: 'En línea', tone: 'success' },
  Offline: { label: 'Fuera de línea', tone: 'warning' },
  ManualContingency: { label: 'Contingencia manual', tone: 'danger' },
  Recovering: { label: 'Recuperando', tone: 'info' },
  Closed: { label: 'Cerrado', tone: 'neutral' },
});

/** Estado para mostrar de un punto (un punto cerrado ya no emite). */
export function pointMode(point: Pick<PointData, 'mode' | 'isClosed'>): string {
  return point.isClosed ? 'Closed' : point.mode;
}

/** Estado de un evento significativo (`SignificantEventStatus`). */
export const EVENT_STATUSES = defineStatuses({
  Open: { label: 'Abierto', tone: 'warning' },
  Closed: { label: 'Cerrado (sin registrar)', tone: 'danger' },
  Registered: { label: 'Registrado en el SIN', tone: 'info' },
  PackagesSent: { label: 'Paquetes enviados', tone: 'info' },
  Reconciled: { label: 'Conciliado', tone: 'success' },
  WithObservations: { label: 'Con observaciones', tone: 'danger' },
});

/** Tipo de evento (`SignificantEventKind`). */
export const EVENT_KINDS = defineStatuses({
  Offline: { label: 'Fuera de línea', tone: 'warning' },
  ManualCafc: { label: 'Contingencia manual (CAFC)', tone: 'danger' },
});

/** Estado de un paquete de contingencia (`FiscalPackageStatus`). */
export const PACKAGE_STATUSES = defineStatuses({
  Sent: { label: 'Enviado, por validar', tone: 'info' },
  Validated: { label: 'Validado', tone: 'success' },
  Observed: { label: 'Observado', tone: 'warning' },
  Rejected: { label: 'Rechazado', tone: 'danger' },
});

/** Estado de un talonario CAFC. */
export const CAFC_STATUSES = defineStatuses({
  active: { label: 'Activo', tone: 'success' },
  inactive: { label: 'Inactivo', tone: 'neutral' },
});

/** Tono del aviso de una alerta del servidor (`SiatAlertSeverity`: danger, warning, info). */
export function alertTone(severity: string): 'danger' | 'warning' | 'info' {
  return severity === 'danger' || severity === 'warning' ? severity : 'info';
}

// ---------------------------------------------------------------------------------------------------- tiempos

/** Cuenta regresiva hasta un plazo («vence en 5 h 20 min», «venció hace 2 h»). */
export function countdown(deadline: string, now: Date): string {
  const span = new Date(deadline).getTime() - now.getTime();
  if (!Number.isFinite(span)) return '';
  const past = span < 0;
  const minutesTotal = Math.floor(Math.abs(span) / 60_000);
  const days = Math.floor(minutesTotal / 1440);
  const hours = Math.floor(minutesTotal / 60);
  const minutes = minutesTotal % 60;
  const text = days >= 2 ? `${days} días` : hours >= 1 ? `${hours} h ${minutes} min` : `${Math.max(1, minutes)} min`;
  return past ? `venció hace ${text}` : `vence en ${text}`;
}

export type Validity = 'ok' | 'soon' | 'expired';

/** Vigencia de un código: vencido (o sin código), por vencer (dentro de `margin` ms) o vigente. */
export function validity(until: string | null, now: Date, marginMs: number): Validity {
  if (!until) return 'expired';
  const time = new Date(until).getTime();
  if (!Number.isFinite(time) || time <= now.getTime()) return 'expired';
  return time - marginMs <= now.getTime() ? 'soon' : 'ok';
}

/** CUIS: por vencer 5 días antes; CUFD: por vencer 2 horas antes (como el escritorio). */
export const CUIS_MARGIN_MS = 5 * 24 * 3_600_000;
export const CUFD_MARGIN_MS = 2 * 3_600_000;

export function validityTone(value: Validity): 'success' | 'warning' | 'danger' {
  return value === 'ok' ? 'success' : value === 'soon' ? 'warning' : 'danger';
}

const HAS_ZONE = /(?:Z|[+-]\d{2}:?\d{2})$/i;
const FISCAL = /^(\d{4})-(\d{2})-(\d{2})(?:T(\d{2}):(\d{2}))?/;

/** Fecha y hora fiscal (hora de Bolivia SIN zona, `DateTime` del servidor) sin correrla: «29/09/2026 10:30». */
export function formatFiscalTime(value: string | null | undefined): string {
  const text = (value ?? '').trim();
  if (text.length === 0) return '—';
  if (HAS_ZONE.test(text)) return formatDateTime(text);
  const match = FISCAL.exec(text);
  if (!match) return text;
  return match[4] ? `${match[3]}/${match[2]}/${match[1]} ${match[4]}:${match[5]}` : `${match[3]}/${match[2]}/${match[1]}`;
}

/** Día fiscal ISO de una fecha fiscal («2026-09-29»). */
export function fiscalDay(value: string | null | undefined): string | null {
  const match = FISCAL.exec((value ?? '').trim());
  return match ? `${match[1]}-${match[2]}-${match[3]}` : null;
}

/** Ahora en La Paz para un campo «fecha y hora» del navegador («2026-09-29T10:30»). */
export function laPazLocalNow(now: Date = new Date()): string {
  return `${laPazToday(now)}T${formatTime(now)}`;
}

/** Suma minutos a una fecha y hora local sin zona («2026-09-29T10:30» + 5 → «2026-09-29T10:35»). */
export function addMinutesLocal(local: string, minutes: number): string {
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})/.exec(local);
  if (!match) return local;
  const date = new Date(Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3]), Number(match[4]), Number(match[5]) + minutes));
  return date.toISOString().slice(0, 16);
}

/** Lo que escribe el campo del navegador («2026-09-29T10:30») → hora fiscal para el servidor («2026-09-29T10:30:00»). */
export function toFiscalDateTime(local: string): string | null {
  const match = /^(\d{4}-\d{2}-\d{2})T(\d{2}:\d{2})(?::(\d{2}))?$/.exec(local.trim());
  return match ? `${match[1]}T${match[2]}:${match[3] ?? '00'}` : null;
}

// ---------------------------------------------------------------------------------------------------- textos

/** Quita el ✔ / ⚠ / ✖ del principio de un mensaje del servidor (la pantalla pone su propio ícono). */
export function plainMessage(text: string | null | undefined): string {
  return (text ?? '').replace(/^[\s✔⚠✖]+/u, '').trim();
}

/** «CORTE DEL SERVICIO DE INTERNET» → «Corte del servicio de internet». */
export function sentenceCase(text: string): string {
  const lower = text.trim().toLocaleLowerCase('es');
  return lower.charAt(0).toLocaleUpperCase('es') + lower.slice(1);
}

/** «CM · Casa matriz». */
export function branchText(point: Pick<PointData, 'branchCode' | 'branchName'>): string {
  return `${point.branchCode} · ${point.branchName}`;
}

/** «Punto 1 · Caja principal». */
export function pointTitle(point: Pick<PointData, 'code' | 'name'>): string {
  return `Punto ${point.code} · ${point.name}`;
}

/** «CM · punto 1». */
export function placeText(row: { branchCode: string; pointOfSaleCode: number }): string {
  return `${row.branchCode} · punto ${row.pointOfSaleCode}`;
}

export function sectorText(sector: number): string {
  if (sector === SECTOR_PURCHASE_SALE) return 'Facturas';
  if (sector === SECTOR_CREDIT_DEBIT_NOTE) return 'Notas crédito-débito';
  return `Sector ${sector}`;
}

/** Plazo de un evento: transcribir (contingencia manual) o registrar en el SIN. */
export function eventDeadline(row: Pick<EventData, 'transcriptionDeadline' | 'registrationDeadline'>): string | null {
  if (row.transcriptionDeadline) return `Transcribir hasta ${formatFiscalTime(row.transcriptionDeadline)}`;
  if (row.registrationDeadline) return `Registrar hasta ${formatFiscalTime(row.registrationDeadline)}`;
  return null;
}

/** «25/09/2026 10:00 → 25/09/2026 12:30» · «… → abierto». */
export function eventPeriod(row: Pick<EventData, 'startedAt' | 'endedAt'>): string {
  return `${formatFiscalTime(row.startedAt)} → ${row.endedAt ? formatFiscalTime(row.endedAt) : 'abierto'}`;
}

/** ¿Se pueden transcribir facturas manuales en este evento? (contingencia manual todavía no conciliada). */
export function isTranscribable(row: Pick<EventData, 'kind' | 'status'>): boolean {
  return row.kind === 'ManualCafc' && row.status !== 'Reconciled' && row.status !== 'WithObservations';
}

// ---------------------------------------------------------------------------------------------------- puntos de venta

/** Qué acciones de contingencia admite el punto según su modo (como el escritorio). */
export function pointAbilities(point: Pick<PointData, 'mode' | 'isClosed'>): { online: boolean; offline: boolean; manual: boolean; canEnd: boolean } {
  const online = !point.isClosed && point.mode === 'Online';
  const offline = !point.isClosed && (point.mode === 'Offline' || point.mode === 'Recovering');
  const manual = !point.isClosed && point.mode === 'ManualContingency';
  return { online, offline, manual, canEnd: manual || (!point.isClosed && point.mode === 'Offline') };
}

/** Enlace a los documentos fuera de línea de un punto (en «Documentos fiscales»). */
export function offlineDocumentsLink(point: Pick<PointData, 'branchCode' | 'code'>): string {
  const params = new URLSearchParams({ sucursal: point.branchCode, punto: `${point.branchCode}-${point.code}`, estado: 'Offline' });
  return `documentos-fiscales?${params.toString()}`;
}

/** Resumen de los puntos activos: «2 en línea · 1 fuera de línea». */
export function modesSummary(points: readonly PointData[]): { active: number; notOnline: number; text: string } {
  const active = points.filter((point) => !point.isClosed);
  const counts = new Map<string, number>();
  for (const point of active) counts.set(point.mode, (counts.get(point.mode) ?? 0) + 1);
  const text = [...counts.entries()].map(([mode, count]) => `${count} ${statusOf(MODES, mode).label.toLocaleLowerCase('es')}`).join(' · ');
  return { active: active.length, notOnline: active.filter((point) => point.mode !== 'Online').length, text };
}

/** Sucursales de los puntos (para la lista desplegable). */
export function branchOptions(rows: readonly { branchCode: string }[]): { value: string; label: string }[] {
  return [...new Set(rows.map((row) => row.branchCode))].sort().map((code) => ({ value: code, label: code }));
}

// ---------------------------------------------------------------------------------------------------- eventos, paquetes, CAFC

/** Filtros de la pestaña «Eventos» (prefijo `e_` en la dirección): los últimos 45 días, como el escritorio. */
export function eventFilters(today: string) {
  return { desde: addDays(today, -44), hasta: today, estado: '', tipo: '', sucursal: '' };
}
export type EventFilters = ReturnType<typeof eventFilters>;

export function filterEvents(rows: readonly EventData[], filters: EventFilters): EventData[] {
  return rows.filter(
    (row) => (!filters.estado || row.status === filters.estado) && (!filters.tipo || row.kind === filters.tipo) && (!filters.sucursal || row.branchCode === filters.sucursal),
  );
}

export const PACKAGE_FILTERS = { evento: '', estado: '', sucursal: '' };
export type PackageFilters = typeof PACKAGE_FILTERS;

export function filterPackages(rows: readonly PackageData[], filters: PackageFilters): PackageData[] {
  return rows.filter((row) => (!filters.estado || row.status === filters.estado) && (!filters.sucursal || row.branchCode === filters.sucursal));
}

export const CAFC_FILTERS = { estado: '', sucursal: '', sector: '' };
export type CafcFilters = typeof CAFC_FILTERS;

export function filterCafcs(rows: readonly CafcData[], filters: CafcFilters): CafcData[] {
  return rows.filter(
    (row) =>
      (!filters.estado || (row.isActive ? 'active' : 'inactive') === filters.estado) &&
      (!filters.sucursal || row.branchCode === filters.sucursal) &&
      (!filters.sector || String(row.documentSector) === filters.sector),
  );
}

export const SECTOR_OPTIONS: readonly { value: string; label: string }[] = [
  { value: String(SECTOR_PURCHASE_SALE), label: 'Factura compra venta (sector 1)' },
  { value: String(SECTOR_CREDIT_DEBIT_NOTE), label: 'Nota crédito-débito (sector 24)' },
];

/** «12 de 100 usados». */
export function cafcUsage(row: Pick<CafcData, 'used' | 'numberFrom' | 'numberTo'>): string {
  return `${row.used} de ${row.numberTo - row.numberFrom + 1} usados`;
}

export const EVENT_CSV: readonly CsvColumn<EventData>[] = [
  { header: 'Tipo', value: (row) => statusOf(EVENT_KINDS, row.kind).label },
  { header: 'Sucursal', value: (row) => row.branchCode },
  { header: 'Punto de venta', value: (row) => row.pointOfSaleCode },
  { header: 'Código del evento', value: (row) => row.eventCode },
  { header: 'Descripción', value: (row) => sentenceCase(row.description) },
  { header: 'Inicio', value: (row) => formatFiscalTime(row.startedAt) },
  { header: 'Fin', value: (row) => (row.endedAt ? formatFiscalTime(row.endedAt) : 'Abierto') },
  { header: 'Estado', value: (row) => statusOf(EVENT_STATUSES, row.status).label },
  { header: 'Documentos', value: (row) => row.documents },
  { header: 'CAFC', value: (row) => row.cafc },
  { header: 'Código de recepción', value: (row) => row.receptionCode },
];

export const PACKAGE_CSV: readonly CsvColumn<PackageData>[] = [
  { header: 'Sucursal', value: (row) => row.branchCode },
  { header: 'Punto de venta', value: (row) => row.pointOfSaleCode },
  { header: 'Documento', value: (row) => sectorText(row.documentSector) },
  { header: 'Documentos', value: (row) => row.documents },
  { header: 'CAFC', value: (row) => row.cafc },
  { header: 'Enviado', value: (row) => formatDateTime(row.sentAt) },
  { header: 'Validado', value: (row) => (row.validatedAt ? formatDateTime(row.validatedAt) : null) },
  { header: 'Estado', value: (row) => statusOf(PACKAGE_STATUSES, row.status).label },
  { header: 'Código del SIN', value: (row) => row.lastSiatCode },
  { header: 'Código de recepción', value: (row) => row.receptionCode },
  { header: 'Mensajes', value: (row) => row.messages },
];

export const CAFC_CSV: readonly CsvColumn<CafcData>[] = [
  { header: 'Sucursal', value: (row) => row.branchCode },
  { header: 'Documento', value: (row) => (row.documentSector === SECTOR_PURCHASE_SALE ? 'Factura compra venta' : 'Nota crédito-débito') },
  { header: 'Código CAFC', value: (row) => row.code },
  { header: 'Desde el número', value: (row) => row.numberFrom },
  { header: 'Hasta el número', value: (row) => row.numberTo },
  { header: 'Usados', value: (row) => row.used },
  { header: 'Vence', value: (row) => (row.validUntil ? formatFiscalTime(row.validUntil) : 'Sin vencimiento') },
  { header: 'Estado', value: (row) => (row.isActive ? 'Activo' : 'Inactivo') },
];

// ---------------------------------------------------------------------------------------------------- contingencia manual

function plain(text: string): string {
  return text
    .normalize('NFD')
    .replace(/\p{Diacritic}/gu, '')
    .toUpperCase();
}

/** ¿El evento del catálogo es de contingencia MANUAL (energía, virus o software, hardware)? (`FiscalIssuer.IsManualEvent`). */
export function isManualEvent(description: string): boolean {
  const text = plain(description);
  return ['ENERGIA', 'VIRUS', 'SOFTWARE', 'HARDWARE'].some((word) => text.includes(word));
}

/** Eventos de contingencia manual vigentes del catálogo (se eligen por descripción: su numeración cambió entre versiones). */
export function manualEventOptions(catalog: readonly CatalogItemData[] | undefined): { value: string; label: string }[] {
  return (catalog ?? [])
    .filter((item) => item.isCurrent && isManualEvent(item.description))
    .sort((a, b) => a.code - b.code)
    .map((item) => ({ value: String(item.code), label: `${item.code} · ${sentenceCase(item.description)}` }));
}

/** Talonarios activos de facturas de la sucursal del punto. */
export function cafcOptions(cafcs: readonly CafcData[] | undefined, branchCode: string): { value: string; label: string }[] {
  return (cafcs ?? [])
    .filter((row) => row.isActive && row.branchCode === branchCode && row.documentSector === SECTOR_PURCHASE_SALE)
    .map((row) => ({ value: row.code, label: `${row.code} · N° ${row.numberFrom} a ${row.numberTo}${row.validUntil ? ` · vence ${formatFiscalTime(row.validUntil)}` : ''}` }));
}

// ---------------------------------------------------------------------------------------------------- transcripción CAFC

/** Contingencias manuales abiertas o por transcribir (las conciliadas ya no admiten facturas). */
export function transcribableEvents(events: readonly EventData[] | undefined): EventData[] {
  return (events ?? []).filter(isTranscribable);
}

export function eventOptionLabel(row: EventData): string {
  const end = row.endedAt ? ` hasta ${formatFiscalTime(row.endedAt)}` : ' (abierta)';
  return `${placeText(row)} · CAFC ${row.cafc ?? '—'} · desde ${formatFiscalTime(row.startedAt)}${end}`;
}

/** Una línea de la factura manual que se transcribe (en la página). */
export interface TranscribeLineDraft {
  sku: string;
  name: string;
  unit: string;
  quantity: number | null;
  discount: number | null;
  /** Series o IMEI separados por coma (solo productos serializados). */
  serials: string;
}

export function newLine(product: Pick<ProductData, 'sku' | 'name' | 'unit'>): TranscribeLineDraft {
  return { sku: product.sku, name: product.name, unit: product.unit, quantity: 1, discount: null, serials: '' };
}

/** «ABC1, ABC2 ;ABC3» → ['ABC1', 'ABC2', 'ABC3'] (null si no hay ninguna). */
export function parseSerials(text: string): string[] | null {
  const serials = text
    .split(/[,;\n]+/)
    .map((serial) => serial.trim())
    .filter((serial) => serial.length > 0);
  return serials.length > 0 ? serials : null;
}

/** Problema de una línea (null si está bien). */
export function lineProblem(line: TranscribeLineDraft): string | null {
  if (line.quantity === null || line.quantity <= 0) return `Escriba la cantidad de ${line.name}.`;
  if (line.discount !== null && (line.discount < 0 || line.discount > 100)) return `El descuento de ${line.name} va de 0 a 100 %.`;
  const serials = parseSerials(line.serials);
  if (serials && serials.length !== line.quantity) return `Escriba una serie por unidad de ${line.name} (${serials.length} de ${line.quantity}).`;
  return null;
}

export function linePayload(line: TranscribeLineDraft): TranscribeLinePayload {
  return { sku: line.sku, quantity: line.quantity ?? 0, discountPercent: line.discount ?? 0, serials: parseSerials(line.serials) };
}

// ---------------------------------------------------------------------------------------------------- comprador

export const DOC_CI = 1;
export const DOC_NIT = 5;

export interface BuyerDraft {
  documentType: number;
  documentNumber: string;
  complement: string;
  name: string;
  email: string;
  exceptionRequested: boolean;
}

export const EMPTY_BUYER: BuyerDraft = { documentType: DOC_CI, documentNumber: '', complement: '', name: '', email: '', exceptionRequested: false };

const KNOWN_TYPES: Readonly<Record<number, string>> = {
  1: 'CI · Cédula de identidad',
  2: 'CEX · Cédula de extranjero',
  3: 'PAS · Pasaporte',
  4: 'OD · Otro documento',
  5: 'NIT · Número de identificación tributaria',
};

export function documentTypeOptions(catalog: readonly CatalogItemData[] | undefined): { value: string; label: string }[] {
  const current = (catalog ?? []).filter((item) => item.isCurrent);
  const codes = current.length > 0 ? [...new Set(current.map((item) => item.code))].sort((a, b) => a - b) : [1, 2, 3, 4, 5];
  return codes.map((code) => ({ value: String(code), label: KNOWN_TYPES[code] ?? current.find((item) => item.code === code)?.description ?? String(code) }));
}

export const SPECIAL_NITS: readonly { code: string; label: string; defaultName: string | null }[] = [
  { code: '99003', label: '99003 · Ventas menores', defaultName: 'VENTAS MENORES DEL DIA' },
  { code: '99002', label: '99002 · Control tributario', defaultName: 'CONTROL TRIBUTARIO' },
  { code: '99001', label: '99001 · Consulados', defaultName: null },
];

export function applySpecialNit(buyer: BuyerDraft, code: string): BuyerDraft {
  const special = SPECIAL_NITS.find((item) => item.code === code);
  if (!special) return buyer;
  return { ...buyer, documentType: DOC_NIT, documentNumber: special.code, complement: '', exceptionRequested: false, name: special.defaultName ?? buyer.name };
}

export function withDocumentType(buyer: BuyerDraft, type: number): BuyerDraft {
  return { ...buyer, documentType: type, complement: type === DOC_CI ? buyer.complement : '', exceptionRequested: type === DOC_NIT ? buyer.exceptionRequested : false };
}

export interface BuyerErrors {
  documentNumber?: string;
  complement?: string;
  email?: string;
}

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

export function buyerErrors(buyer: BuyerDraft): BuyerErrors {
  const errors: BuyerErrors = {};
  const number = buyer.documentNumber.trim();
  if (number.length === 0) errors.documentNumber = 'Escriba el documento del comprador tal como figura en la factura manual.';
  else if ((buyer.documentType === DOC_CI || buyer.documentType === DOC_NIT) && !/^\d+$/.test(number))
    errors.documentNumber = `El ${buyer.documentType === DOC_NIT ? 'NIT' : 'CI'} lleva solo números (sin puntos, guiones ni espacios).`;
  else if (number.length > 20) errors.documentNumber = 'El número de documento tiene como máximo 20 caracteres.';
  const complement = buyer.complement.trim();
  if (complement.length > 0 && buyer.documentType !== DOC_CI) errors.complement = 'El complemento se usa solo con la cédula de identidad.';
  else if (complement.length > 5) errors.complement = 'El complemento tiene como máximo 5 caracteres.';
  if (buyer.email.trim().length > 0 && !EMAIL.test(buyer.email.trim())) errors.email = 'Escriba un correo válido, por ejemplo nombre@correo.com.';
  return errors;
}

export function buyerPayload(buyer: BuyerDraft): BuyerPayload {
  const complement = buyer.complement.trim().toUpperCase();
  const name = buyer.name.trim();
  const email = buyer.email.trim();
  return {
    documentType: buyer.documentType,
    documentNumber: buyer.documentNumber.trim(),
    complement: buyer.documentType === DOC_CI && complement.length > 0 ? complement : null,
    name: name.length > 0 ? name : null,
    email: email.length > 0 ? email : null,
    exceptionRequested: buyer.documentType === DOC_NIT && buyer.exceptionRequested,
  };
}
