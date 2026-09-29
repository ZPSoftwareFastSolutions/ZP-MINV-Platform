// Módulo «Armador de PC» · funciones puras (sin React): las ranuras del armado, las piezas elegidas (agregar, quitar,
// cantidades), el pedido de `SavePcBuildCommand`, los filtros de los candidatos (marca, precio, solo compatibles), el
// resumen de la compatibilidad que manda el servidor, los estados y la vigencia de las cotizaciones, qué acciones ofrece
// cada estado y los filtros, columnas del CSV y resumen de la lista de cotizaciones.
//
// La compatibilidad la decide SOLO el servidor (`CheckPcBuildQuery` y `GetPcBuildCandidatesQuery`, regla T-06): aquí no
// hay reglas de compatibilidad. Las ranuras «obligatorias», «de varias piezas» y «por categoría» son la guía de la
// pantalla (las mismas del dominio: `PcCompatibility.Required` y `PcBuild.MultiSlots`); el servidor vuelve a validar al
// guardar (regla P-01). Los tipos del servidor SALEN DEL CONTRATO (regla P-07).

import type { RpcRequestOf, RpcResponseOf } from '@/4-presentation/app/contract';
import { defineStatuses, statusOf, type SelectOption } from '@/4-presentation/panel/kit';
import { formatDate, formatDateTime, formatMoney, formatNumber, inRange, matchesSearch, toDate, type CsvColumn } from '@/4-presentation/panel/lib';

// ---------------------------------------------------------------------------------------------------- tipos del servidor

/** Una fila de `GetPcBuildsQuery`. */
export type BuildRecord = RpcResponseOf<'GetPcBuildsQuery'>[number];
/** El detalle de `GetPcBuildQuery`. */
export type BuildDetailData = RpcResponseOf<'GetPcBuildQuery'>;
/** El informe de `CheckPcBuildQuery` (piezas con precio y stock, errores y avisos, consumo y total). */
export type CheckData = RpcResponseOf<'CheckPcBuildQuery'>;
/** Una pieza del informe o del detalle. */
export type ItemView = CheckData['items'][number];
/** Un error o aviso de compatibilidad. */
export type IssueRecord = CheckData['issues'][number];
/** Un candidato de una ranura (compatible o no, con el motivo). */
export type CandidateRecord = RpcResponseOf<'GetPcBuildCandidatesQuery'>[number];
/** Un hecho de la bitácora del armado. */
export type BuildEventData = NonNullable<BuildDetailData['history']>[number];
export type CustomerRecord = RpcResponseOf<'GetCustomersQuery'>['customers'][number];
export type CategoryRecord = RpcResponseOf<'GetCatalogOptionsQuery'>['categories'][number];

type PartInput = RpcRequestOf<'CheckPcBuildQuery'>['items'][number];
type SaveRequest = RpcRequestOf<'SavePcBuildCommand'>;
type BuildsRequest = RpcRequestOf<'GetPcBuildsQuery'>;
/** Código de una ranura (enumeración `PcSlot` del servidor). */
export type SlotCode = NonNullable<PartInput['slot']>;

// ---------------------------------------------------------------------------------------------------- ranuras

export interface SlotInfo {
  code: SlotCode;
  label: string;
  /** Hace falta para que la PC funcione (el servidor avisa si falta). */
  required: boolean;
  /** Admite varias piezas (memoria, discos, gráficas y extras). */
  multi: boolean;
  /** Se elige por categoría (no tiene una especificación de compatibilidad que la identifique). */
  byCategory: boolean;
  /** Palabra con que se propone la categoría la primera vez. */
  categoryHint?: string;
  group: 'base' | 'extra';
}

/** Las ranuras, en el orden del armado paso a paso. */
export const SLOTS: readonly SlotInfo[] = [
  { code: 'Cpu', label: 'Procesador', required: true, multi: false, byCategory: false, group: 'base' },
  { code: 'Motherboard', label: 'Placa madre', required: true, multi: false, byCategory: false, group: 'base' },
  { code: 'Ram', label: 'Memoria RAM', required: true, multi: true, byCategory: false, group: 'base' },
  { code: 'Gpu', label: 'Tarjeta de video', required: false, multi: true, byCategory: false, group: 'base' },
  { code: 'Storage', label: 'Almacenamiento', required: true, multi: true, byCategory: false, group: 'base' },
  { code: 'Psu', label: 'Fuente de poder', required: true, multi: false, byCategory: false, group: 'base' },
  { code: 'Case', label: 'Gabinete', required: true, multi: false, byCategory: false, group: 'base' },
  { code: 'Cooler', label: 'Refrigeración', required: false, multi: false, byCategory: false, group: 'base' },
  { code: 'Monitor', label: 'Monitor', required: false, multi: true, byCategory: true, categoryHint: 'monitor', group: 'extra' },
  { code: 'Peripheral', label: 'Periféricos', required: false, multi: true, byCategory: true, categoryHint: 'perif', group: 'extra' },
  { code: 'Software', label: 'Software', required: false, multi: true, byCategory: true, categoryHint: 'software', group: 'extra' },
  { code: 'Service', label: 'Servicios', required: false, multi: true, byCategory: true, categoryHint: 'servic', group: 'extra' },
];

export function slotInfo(code: string | null | undefined): SlotInfo {
  return SLOTS.find((slot) => slot.code === code) ?? SLOTS.find((slot) => slot.code === 'Peripheral')!;
}

export function slotLabel(code: string | null | undefined): string {
  return code ? (SLOTS.find((slot) => slot.code === code)?.label ?? code) : 'Producto';
}

/** Límites del servidor (`PcBuild.MaxQuantity` y `PcBuild.MaxLines`). */
export const MAX_QUANTITY = 16;
export const MAX_LINES = 20;
export const NAME_MAX_LENGTH = 150;

// ---------------------------------------------------------------------------------------------------- piezas elegidas

/** Una pieza elegida en una ranura (con el precio y el stock que dio el servidor). */
export interface PartLine {
  slot: SlotCode;
  sku: string;
  name: string;
  unitPrice: number;
  stock: number;
  keySpecs: readonly string[];
  quantity: number;
}

export function slotParts(parts: readonly PartLine[], slot: SlotCode): PartLine[] {
  return parts.filter((part) => part.slot === slot);
}

/** ¿Se puede agregar otra línea? (ranuras de una pieza reemplazan; el resto suma hasta 20 líneas). */
export function canAddLine(parts: readonly PartLine[], slot: SlotCode, sku: string): boolean {
  const info = slotInfo(slot);
  if (!info.multi) return true;
  if (parts.some((part) => part.slot === slot && part.sku === sku)) return true;
  return parts.length < MAX_LINES;
}

/** Agrega un candidato: en una ranura de una pieza la reemplaza; en una de varias suma una unidad (hasta 16) o una línea. */
export function addPart(parts: readonly PartLine[], slot: SlotCode, candidate: Pick<CandidateRecord, 'sku' | 'name' | 'price' | 'stock' | 'keySpecs'>): PartLine[] {
  const info = slotInfo(slot);
  const line: PartLine = { slot, sku: candidate.sku, name: candidate.name, unitPrice: candidate.price, stock: candidate.stock, keySpecs: candidate.keySpecs, quantity: 1 };
  if (!info.multi) return [...parts.filter((part) => part.slot !== slot), line];
  if (parts.some((part) => part.slot === slot && part.sku === candidate.sku)) return changeQuantity(parts, slot, candidate.sku, 1);
  if (parts.length >= MAX_LINES) return [...parts];
  return [...parts, line];
}

export function removePart(parts: readonly PartLine[], slot: SlotCode, sku: string): PartLine[] {
  return parts.filter((part) => !(part.slot === slot && part.sku === sku));
}

/** Suma o resta unidades (de 1 a 16) de una pieza de una ranura de varias piezas. */
export function changeQuantity(parts: readonly PartLine[], slot: SlotCode, sku: string, delta: number): PartLine[] {
  return parts.map((part) => (part.slot === slot && part.sku === sku ? { ...part, quantity: Math.min(MAX_QUANTITY, Math.max(1, part.quantity + delta)) } : part));
}

/** Las piezas como las pide el servidor (`PcBuildItemInput`: ranura, SKU y cantidad), en el orden de las ranuras. */
export function toInputs(parts: readonly PartLine[]): PartInput[] {
  return SLOTS.flatMap((slot) => slotParts(parts, slot.code).map((part) => ({ slot: part.slot, sku: part.sku, quantity: part.quantity })));
}

/** Piezas de un armado guardado (una línea sin ranura se muestra entre los periféricos, como el escritorio). */
export function partsFromItems(items: readonly ItemView[]): PartLine[] {
  return items.map((item) => ({
    slot: (item.slot ?? 'Peripheral') as SlotCode,
    sku: item.sku,
    name: item.name,
    unitPrice: item.unitPrice,
    stock: item.stock,
    keySpecs: item.keySpecs,
    quantity: item.quantity,
  }));
}

/** Precio de lista y stock vigentes de cada pieza según el informe del servidor (solo mientras se arma). */
export function withLivePrices(parts: readonly PartLine[], check: CheckData | undefined): PartLine[] {
  if (!check) return [...parts];
  return parts.map((part) => {
    const live = check.items.find((item) => item.sku === part.sku && (item.slot ?? 'Peripheral') === part.slot);
    return live ? { ...part, unitPrice: live.unitPrice, stock: live.stock } : part;
  });
}

export function partsCount(parts: readonly PartLine[]): number {
  return parts.reduce((sum, part) => sum + part.quantity, 0);
}

export function partsTotal(parts: readonly PartLine[]): number {
  return parts.reduce((sum, part) => sum + part.unitPrice * part.quantity, 0);
}

/** «1 pieza» · «3 piezas». */
export function piecesText(count: number): string {
  return count === 1 ? '1 pieza' : `${count} piezas`;
}

/** Lo que dice cada ranura en la lista: obligatoria vacía, opcional o cuántas piezas lleva. */
export function slotSummary(parts: readonly PartLine[], slot: SlotInfo): string {
  const count = partsCount(slotParts(parts, slot.code));
  if (count === 0) return slot.required ? 'Obligatoria · elija una pieza' : 'Opcional';
  return piecesText(count);
}

/** Siguiente ranura obligatoria vacía (ayuda a armar en orden); null si están todas. */
export function nextRequiredSlot(parts: readonly PartLine[]): SlotCode | null {
  return SLOTS.find((slot) => slot.required && slotParts(parts, slot.code).length === 0)?.code ?? null;
}

/** Nombre propuesto: «PC» + las primeras palabras del procesador y de la tarjeta de video (o «PC a medida»). */
export function autoName(parts: readonly PartLine[]): string {
  const words = (name: string | undefined) => (name ? name.split(/\s+/).filter(Boolean).slice(0, 3).join(' ') : null);
  const chosen = [words(slotParts(parts, 'Cpu')[0]?.name), words(slotParts(parts, 'Gpu')[0]?.name)].filter((part): part is string => Boolean(part));
  return chosen.length === 0 ? 'PC a medida' : `PC ${chosen.join(' + ')}`;
}

// ---------------------------------------------------------------------------------------------------- candidatos

export interface CandidateFilters {
  /** '' = todas las marcas. */
  brand: string;
  minPrice: number | null;
  maxPrice: number | null;
  onlyCompatible: boolean;
}

export const EMPTY_CANDIDATE_FILTERS: CandidateFilters = { brand: '', minPrice: null, maxPrice: null, onlyCompatible: false };
/** Candidatos que se muestran de a tandas («Mostrar más»). */
export const CANDIDATE_PAGE = 24;
/** Valor del filtro de marca para los productos sin marca. */
export const NO_BRAND = '_sin-marca';

export function brandOptions(candidates: readonly CandidateRecord[]): SelectOption[] {
  const brands = [...new Set(candidates.map((candidate) => candidate.brand?.trim() || NO_BRAND))];
  return brands
    .map((brand) => ({ value: brand, label: brand === NO_BRAND ? 'Sin marca' : brand }))
    .sort((a, b) => (a.value === NO_BRAND ? 1 : b.value === NO_BRAND ? -1 : a.label.localeCompare(b.label, 'es')));
}

/** Filtra en la página lo que ya mandó el servidor (que ordena primero los compatibles y después por precio). */
export function filterCandidates(candidates: readonly CandidateRecord[], filters: CandidateFilters): CandidateRecord[] {
  return candidates.filter(
    (candidate) =>
      (!filters.brand || (candidate.brand?.trim() || NO_BRAND) === filters.brand) &&
      (filters.minPrice === null || candidate.price >= filters.minPrice) &&
      (filters.maxPrice === null || candidate.price <= filters.maxPrice) &&
      (!filters.onlyCompatible || candidate.isCompatible),
  );
}

/** Categoría que se propone para una ranura por categoría (la que se parece al nombre de la ranura), o ''. */
export function defaultCategory(slot: SlotInfo, categories: readonly CategoryRecord[]): string {
  if (!slot.byCategory || !slot.categoryHint) return '';
  return categories.find((category) => matchesSearch(slot.categoryHint ?? '', [category.name]))?.code ?? '';
}

// ---------------------------------------------------------------------------------------------------- compatibilidad

export interface CheckSummary {
  title: string;
  tone: 'info' | 'danger' | 'warning' | 'success';
  errors: number;
  warnings: number;
}

/** Título del panel de compatibilidad con lo que respondió el servidor. */
export function checkSummary(check: CheckData | undefined, hasParts: boolean): CheckSummary {
  if (!hasParts || !check) return { title: hasParts ? 'Revisando la compatibilidad…' : 'Elija las piezas', tone: 'info', errors: 0, warnings: 0 };
  const errors = check.issues.filter((issue) => issue.isError).length;
  const warnings = check.issues.length - errors;
  if (errors > 0) return { title: errors === 1 ? '1 error de compatibilidad' : `${errors} errores de compatibilidad`, tone: 'danger', errors, warnings };
  if (warnings > 0) return { title: `Compatible · ${warnings === 1 ? '1 aviso' : `${warnings} avisos`}`, tone: 'warning', errors, warnings };
  return { title: 'Compatible', tone: 'success', errors, warnings };
}

/** Errores primero, después los avisos (como los ordena el escritorio). */
export function sortedIssues(check: CheckData | undefined): IssueRecord[] {
  return [...(check?.issues ?? [])].sort((a, b) => Number(b.isError) - Number(a.isError));
}

/** Disponibilidad de una pieza: reservada (lo que hay además de lo reservado) o, si no, si alcanza lo que hay. */
export function availabilityText(item: Pick<ItemView, 'stock' | 'quantity'>, reserved: boolean): { text: string; tone: 'accent' | 'success' | 'danger' } {
  const stock = formatNumber(item.stock, { maxDecimals: 3 });
  const plural = item.stock === 1 ? '' : 's';
  if (reserved) return { text: item.stock > 0 ? `Reservada · ${stock} más disponible${plural}` : 'Reservada · sin más unidades', tone: 'accent' };
  if (item.stock >= item.quantity) return { text: `Disponible ${stock}`, tone: 'success' };
  return { text: item.stock > 0 ? `Solo ${stock} disponible${plural}` : 'Sin stock en la sucursal', tone: 'danger' };
}

/** Carga de la fuente con el consumo estimado (%), o null si no hay fuente. */
export function psuLoad(check: Pick<CheckData, 'estimatedDrawW' | 'psuW'> | undefined): number | null {
  if (!check?.psuW || check.psuW <= 0) return null;
  return Math.min(999, Math.round((check.estimatedDrawW * 100) / check.psuW));
}

// ---------------------------------------------------------------------------------------------------- estados y vigencia

/** Estado del armado en palabras (con la vigencia vencida o la reserva cumplida). */
export const BUILD_STATES = defineStatuses({
  Draft: { label: 'Borrador', tone: 'neutral' },
  Quoted: { label: 'Cotizado', tone: 'info' },
  QuotedExpired: { label: 'Cotización vencida', tone: 'warning' },
  Reserved: { label: 'Reservado', tone: 'accent' },
  ReservedExpired: { label: 'Reserva vencida', tone: 'warning' },
  Sold: { label: 'Vendido', tone: 'success' },
  Cancelled: { label: 'Anulado', tone: 'danger' },
});

/** Canal del armado: la tienda web (reservas de clientes) o el mostrador (el personal). */
export const CHANNELS = defineStatuses({
  Web: { label: 'Web', tone: 'info' },
  Desktop: { label: 'Mostrador', tone: 'neutral' },
});

/** Clave de `BUILD_STATES` de un armado. */
export function buildState(row: Pick<BuildRecord, 'status' | 'isExpired' | 'reservedUntil'>, now: Date): string {
  if (row.status === 'Quoted' && row.isExpired) return 'QuotedExpired';
  if (row.status === 'Reserved') {
    const until = toDate(row.reservedUntil);
    if (row.isExpired || (until !== null && until.getTime() <= now.getTime())) return 'ReservedExpired';
  }
  return row.status;
}

export function stateLabel(state: string): string {
  return statusOf(BUILD_STATES, state).label;
}

/** «Vigente hasta 06/10/2026» · «Venció el 28/09/2026» · «Venta F-CM-000120» · «—». */
export function validityText(row: Pick<BuildRecord, 'status' | 'isExpired' | 'validUntil' | 'invoiceNumber'>): string {
  if (row.status === 'Quoted' || row.status === 'Reserved') return row.isExpired ? `Venció el ${formatDate(row.validUntil)}` : `Vigente hasta ${formatDate(row.validUntil)}`;
  if (row.invoiceNumber) return `Venta ${row.invoiceNumber}`;
  return '—';
}

export interface CompatibilityText {
  text: string;
  tone: 'success' | 'warning' | 'danger';
}

export function compatibilityText(row: Pick<BuildRecord, 'isCompatible' | 'quotedWithErrors'>): CompatibilityText {
  if (row.isCompatible) return { text: 'Compatible', tone: 'success' };
  return row.quotedWithErrors ? { text: 'Con errores aceptados', tone: 'warning' } : { text: 'Con errores', tone: 'danger' };
}

/** Se cambian las piezas: armado nuevo o borrador (una cotización queda congelada). */
export function isEditable(row: Pick<BuildRecord, 'status'> | null): boolean {
  return row === null || row.status === 'Draft';
}

/** Se cobra en la caja una cotización o una reserva con los precios vigentes (la venta consume la reserva). */
export function canSellBuild(row: Pick<BuildRecord, 'status' | 'isExpired'>): boolean {
  return (row.status === 'Quoted' || row.status === 'Reserved') && !row.isExpired;
}

/** Se reserva el stock de una cotización vigente. */
export function canReserveBuild(row: Pick<BuildRecord, 'status' | 'isExpired'>): boolean {
  return row.status === 'Quoted' && !row.isExpired;
}

export function canReleaseBuild(row: Pick<BuildRecord, 'status'>): boolean {
  return row.status === 'Reserved';
}

/** Se publica un armado del mostrador cotizado, reservado o vendido que todavía no está en la web. */
export function canPublishBuild(row: Pick<BuildRecord, 'status' | 'channel' | 'kind' | 'publishedToWeb'>): boolean {
  return !row.publishedToWeb && row.kind === 'Build' && row.channel === 'Desktop' && (row.status === 'Quoted' || row.status === 'Reserved' || row.status === 'Sold');
}

export function canUnpublishBuild(row: Pick<BuildRecord, 'publishedToWeb'>): boolean {
  return row.publishedToWeb;
}

/** Se anula un borrador o una cotización (una reserva se libera). */
export function canCancelBuild(row: Pick<BuildRecord, 'status'>): boolean {
  return row.status === 'Draft' || row.status === 'Quoted';
}

/** La cotización se imprime cuando tiene precios congelados. */
export function canPrintBuild(row: Pick<BuildRecord, 'status'>): boolean {
  return row.status === 'Quoted' || row.status === 'Reserved' || row.status === 'Sold';
}

/** Qué pasa con el armado abierto (debajo del título, como el escritorio). */
export function buildNote(row: BuildRecord | null, now: Date): string {
  if (!row) return 'Elija las piezas por ranura: la compatibilidad se revisa sola mientras arma.';
  const until = toDate(row.reservedUntil);
  switch (row.status) {
    case 'Draft':
      return 'Borrador: puede cambiar las piezas y cotizarlo cuando el cliente lo apruebe.';
    case 'Quoted':
      if (row.isExpired) return `La cotización venció el ${formatDate(row.validUntil)}: arme una nueva para cotizar otra vez.`;
      return `Precios congelados hasta el ${formatDate(row.validUntil)}${row.quotedWithErrors ? ' · cotizado con errores aceptados' : ''}${row.publishedToWeb ? ' · publicado en la tienda web' : ''}.`;
    case 'Reserved':
      if (until && until.getTime() <= now.getTime())
        return 'La reserva ya venció: el trabajo automático la libera; puede liberarla ahora o cobrarla si el cliente llegó.';
      return `Stock reservado hasta el ${until ? formatDateTime(until) : '—'} · precios congelados hasta el ${formatDate(row.validUntil)}${row.channel === 'Web' ? ' · reserva hecha por el cliente en la tienda web' : ''}. Al cobrarla en la caja se consume la reserva.`;
    case 'Sold':
      return `Vendido en la venta ${row.invoiceNumber ?? '—'}.${row.publishedToWeb ? ' Sigue publicado como armado sugerido en la web.' : ''}`;
    default:
      return row.cancelReason ? `Armado anulado: ${row.cancelReason}.` : 'Armado anulado.';
  }
}

/** Qué pasó en cada hecho de la bitácora (enumeración `PcBuildEventAction`). */
const HISTORY_ACTIONS: Readonly<Record<string, string>> = {
  Created: 'Creado',
  Quoted: 'Cotizado',
  Reserved: 'Stock reservado',
  Released: 'Reserva liberada',
  Expired: 'Reserva vencida',
  Sold: 'Vendido en la caja',
  Cancelled: 'Anulado',
  Published: 'Publicado en la tienda web',
  Unpublished: 'Retirado de la tienda web',
};

export function historyAction(action: string): string {
  return HISTORY_ACTIONS[action] ?? action;
}

/** Dirección de la caja con la cotización o la reserva cargada (módulo «Caja»: `/panel/caja?reserva=<NÚMERO>`). */
export function sellPath(number: string): string {
  return `caja?reserva=${encodeURIComponent(number)}`;
}

/** Dirección de un armado en el armador (`/panel/armador/<NÚMERO>`). */
export function builderPath(number: string | null): string {
  return number ? `armador/${encodeURIComponent(number)}` : 'armador/nuevo';
}

// ---------------------------------------------------------------------------------------------------- cotizar y reservar

/** Vigencia de la cotización (el servidor acepta de 1 a 90 días). */
export const VALIDITY_OPTIONS: readonly SelectOption[] = [
  { value: '3', label: '3 días' },
  { value: '7', label: '7 días' },
  { value: '15', label: '15 días' },
  { value: '30', label: '30 días' },
];
export const DEFAULT_VALIDITY = '7';

/** Horas de la reserva de stock (el servidor acepta de 1 a 720). */
export const HOURS_OPTIONS: readonly SelectOption[] = [
  { value: '24', label: '24 horas (1 día)' },
  { value: '48', label: '48 horas (2 días)' },
  { value: '72', label: '72 horas (3 días)' },
  { value: '168', label: '168 horas (1 semana)' },
  { value: 'otra', label: 'Otra cantidad de horas' },
];
export const DEFAULT_HOURS = '48';
export const MAX_HOURS = 720;

/** Horas elegidas (o escritas) para reservar, o null si no sirven. */
export function reserveHours(choice: string, other: number | null): number | null {
  const hours = choice === 'otra' ? other : Number(choice);
  return hours !== null && Number.isInteger(hours) && hours >= 1 && hours <= MAX_HOURS ? hours : null;
}

/** Motivos frecuentes para liberar una reserva (los mismos que sugiere el escritorio). */
export const RELEASE_REASONS: readonly string[] = ['El cliente desistió', 'El cliente no pasó a recoger', 'Sin respuesta del cliente', 'Reservado por error'];
export const OTHER_REASON = '_otro';
export const REASON_MAX_LENGTH = 250;

export function releaseReason(choice: string, other: string): string {
  return choice === OTHER_REASON ? other.trim() : choice.trim();
}

export interface SaveInput {
  current: Pick<BuildRecord, 'id'> | null;
  name: string;
  customerCode: string;
  parts: readonly PartLine[];
  quote: boolean;
  validDays: string;
  acceptIncompatible: boolean;
}

/** El pedido de `SavePcBuildCommand` con TODOS sus parámetros (sin nombre, se propone uno como el escritorio). */
export function toSavePayload(input: SaveInput): SaveRequest {
  return {
    id: input.current?.id ?? null,
    name: input.name.trim() || autoName(input.parts),
    customerCode: input.customerCode || null,
    items: toInputs(input.parts),
    quote: input.quote,
    validDays: Number(input.validDays) || Number(DEFAULT_VALIDITY),
    acceptIncompatible: input.acceptIncompatible,
    kind: 'Build',
  };
}

/** Opciones de clientes registrados (activos, sin el consumidor final «CF»), por nombre. */
export function customerOptions(customers: readonly CustomerRecord[]): { value: string; label: string; description: string; data: CustomerRecord }[] {
  return customers
    .filter((customer) => customer.isActive && customer.code !== 'CF')
    .map((customer) => ({
      value: customer.code,
      label: customer.name,
      description: [customer.code, customer.taxId ? `NIT/CI ${customer.taxId}` : null].filter(Boolean).join(' · '),
      data: customer,
    }))
    .sort((a, b) => a.label.localeCompare(b.label, 'es'));
}

/**
 * El cliente de un armado guardado: la fila trae su NOMBRE (no el código), así que se busca por nombre (el primero que
 * coincide, como el escritorio).
 */
export function customerCodeByName(customers: readonly CustomerRecord[], name: string | null | undefined): string {
  if (!name) return '';
  return customers.find((customer) => customer.name === name && customer.code !== 'CF')?.code ?? '';
}

// ---------------------------------------------------------------------------------------------------- lista de cotizaciones

export const BUILD_FILTERS = { q: '', estado: '', vigencia: '', publicada: '', canal: '', desde: '', hasta: '' };
export type BuildFilters = typeof BUILD_FILTERS;

export const STATUS_OPTIONS: readonly SelectOption[] = [
  { value: 'Draft', label: 'Borradores' },
  { value: 'Quoted', label: 'Cotizados' },
  { value: 'Reserved', label: 'Reservados' },
  { value: 'Sold', label: 'Vendidos' },
  { value: 'Cancelled', label: 'Anulados' },
];
export const VALIDITY_FILTER: readonly SelectOption[] = [
  { value: 'vigente', label: 'Vigentes' },
  { value: 'vencida', label: 'Vencidas' },
];
export const PUBLISHED_FILTER: readonly SelectOption[] = [
  { value: 'si', label: 'Publicados en la web' },
  { value: 'no', label: 'Sin publicar' },
];

/** Lo que filtra el SERVIDOR: solo armados (no carritos), el estado y el canal. */
export function buildsRequest(filters: Pick<BuildFilters, 'estado' | 'canal'>): BuildsRequest {
  const status = STATUS_OPTIONS.some((option) => option.value === filters.estado) ? (filters.estado as BuildRecord['status']) : null;
  return { status, channel: filters.canal === 'Web' || filters.canal === 'Desktop' ? filters.canal : null, kind: 'Build' };
}

/** Una fila lista para la tabla. */
export interface BuildItem {
  row: BuildRecord;
  state: string;
  /** El cliente registrado, quien reservó en la web o «Sin cliente». */
  client: string;
  validity: string;
  compatibility: CompatibilityText;
}

export function toBuildItems(rows: readonly BuildRecord[], now: Date): BuildItem[] {
  return rows
    .filter((row) => row.kind === 'Build')
    .map((row) => ({
      row,
      state: buildState(row, now),
      client: row.customer ?? row.contactName ?? 'Sin cliente',
      validity: validityText(row),
      compatibility: compatibilityText(row),
    }));
}

/** Filtros de la página (los del servidor se repiten por si la respuesta es de antes). */
export function filterBuilds(items: readonly BuildItem[], filters: BuildFilters): BuildItem[] {
  const range = { from: filters.desde || null, to: filters.hasta || null };
  const open = (item: BuildItem) => item.row.status === 'Quoted' || item.row.status === 'Reserved';
  return items.filter(
    (item) =>
      (!filters.estado || item.row.status === filters.estado) &&
      (!filters.canal || item.row.channel === filters.canal) &&
      (filters.vigencia !== 'vigente' || (open(item) && !item.row.isExpired)) &&
      (filters.vigencia !== 'vencida' || (open(item) && item.row.isExpired)) &&
      (filters.publicada !== 'si' || item.row.publishedToWeb) &&
      (filters.publicada !== 'no' || !item.row.publishedToWeb) &&
      inRange(item.row.createdAt, range) &&
      matchesSearch(filters.q, [item.row.number, item.row.name, item.client, item.row.contactName, item.row.customer, item.row.invoiceNumber]),
  );
}

export const CSV_COLUMNS: readonly CsvColumn<BuildItem>[] = [
  { header: 'Número', value: (item) => item.row.number },
  { header: 'Armado', value: (item) => item.row.name },
  { header: 'Cliente', value: (item) => item.client },
  { header: 'Canal', value: (item) => statusOf(CHANNELS, item.row.channel).label },
  { header: 'Sucursal', value: (item) => item.row.branchCode },
  { header: 'Piezas', value: (item) => item.row.items },
  { header: 'Compatibilidad', value: (item) => item.compatibility.text },
  { header: 'Estado', value: (item) => stateLabel(item.state) },
  { header: 'Vigencia o venta', value: (item) => item.validity },
  { header: 'Publicado en la web', value: (item) => item.row.publishedToWeb },
  { header: 'Creado', value: (item) => toDate(item.row.createdAt) },
  { header: 'Total', value: (item) => item.row.total },
];

export interface BuildsOverview {
  /** Cotizaciones y reservas con precios vigentes. */
  open: number;
  openValue: number;
  /** Reservas de la tienda web con el plazo vigente. */
  webActive: number;
  webValue: number;
  sold: number;
  soldValue: number;
  drafts: number;
}

/** Resumen de los armados (lo que el escritorio muestra en sus tarjetas; aquí va plegado en «Ver resumen»). */
export function buildsOverview(rows: readonly BuildRecord[], now: Date): BuildsOverview {
  const builds = rows.filter((row) => row.kind === 'Build');
  const open = builds.filter((row) => canSellBuild(row));
  const web = builds.filter((row) => {
    const until = toDate(row.reservedUntil);
    return row.channel === 'Web' && row.status === 'Reserved' && until !== null && until.getTime() > now.getTime();
  });
  const sold = builds.filter((row) => row.status === 'Sold');
  const sum = (list: readonly BuildRecord[]) => list.reduce((total, row) => total + row.total, 0);
  return {
    open: open.length,
    openValue: sum(open),
    webActive: web.length,
    webValue: sum(web),
    sold: sold.length,
    soldValue: sum(sold),
    drafts: builds.filter((row) => row.status === 'Draft').length,
  };
}

/** «Bs 1.234,50 por cobrar» (o el texto si no hay). */
export function amountHint(count: number, value: number, suffix: string, none: string): string {
  return count === 0 ? none : `${formatMoney(value)} ${suffix}`;
}
