// Módulo «Caja» · vender una reserva o una cotización (funciones puras). Las reservas de carrito (`RES-…`, de la tienda
// web o del mostrador) y los armados de PC (`ARM-…`, cotizados o reservados) se cobran en la caja a sus precios
// congelados con `SellPcBuildCommand`; la venta de una reserva CONSUME su reserva (el stock reservado sale una sola vez,
// regla S-04). Solo se cobran cotizaciones y reservas VIGENTES (como el escritorio).

import { defineStatuses, statusOf } from '@/4-presentation/panel/kit';
import { formatDate, formatDateTime, matchesSearch, type CsvColumn } from '@/4-presentation/panel/lib';
import { DOC_CI, type BuyerDraft } from './fiscal';
import type { BuildListData, BuildRowData } from './types';

/** Estado del armado o de la reserva (enumeración `PcBuildStatus` del servidor). */
export const BUILD_STATUSES = defineStatuses({
  Draft: { label: 'Borrador', tone: 'neutral' },
  Quoted: { label: 'Cotizada', tone: 'info' },
  Reserved: { label: 'Reservada', tone: 'accent' },
  Sold: { label: 'Vendida', tone: 'success' },
  Cancelled: { label: 'Anulada', tone: 'danger' },
});

/** Tipo (enumeración `PcBuildKind`): una compra (carrito) o un armado de PC. */
export const BUILD_KINDS = defineStatuses({
  Cart: { label: 'Compra', tone: 'info' },
  Build: { label: 'Armado de PC', tone: 'accent' },
});

/** Canal (enumeración `PcBuildChannel`). */
export const BUILD_CHANNELS = defineStatuses({
  Web: { label: 'Tienda web', tone: 'info' },
  Desktop: { label: 'Mostrador', tone: 'neutral' },
});

/** El número tal como lo guarda el servidor («res-cm-000012» → «RES-CM-000012»). */
export function normalizeBuildNumber(text: string): string {
  return text.trim().toUpperCase();
}

/** «La reserva», «La cotización»… (para las frases). */
function nounOf(row: Pick<BuildRowData, 'kind' | 'status'>): string {
  if (row.kind === 'Cart' || row.status === 'Reserved') return 'La reserva';
  return row.status === 'Quoted' ? 'La cotización' : 'El armado';
}

/** ¿Se puede cobrar? Solo las cotizaciones y las reservas vigentes. */
export function isSellableBuild(row: Pick<BuildRowData, 'status' | 'isExpired'>): boolean {
  return (row.status === 'Quoted' || row.status === 'Reserved') && !row.isExpired;
}

/** Por qué no se puede cobrar (null si se puede). */
export function buildProblem(row: BuildRowData): string | null {
  const noun = `${nounOf(row)} ${row.number}`;
  switch (row.status) {
    case 'Sold':
      return `${noun} ya se vendió${row.invoiceNumber ? ` (venta ${row.invoiceNumber})` : ''}.`;
    case 'Cancelled':
      return `${noun} está anulada o liberada${row.cancelReason ? `: ${row.cancelReason}` : ''}.`;
    case 'Draft':
      return `${noun} es un borrador: cotícela en el Armador de PC antes de cobrarla.`;
    default:
      return row.isExpired ? `${noun} venció. Solo se cobran cotizaciones y reservas vigentes: vuelva a reservar o cotizar.` : null;
  }
}

/** Estado en palabras, con «Vencida» si ya pasó su vigencia. */
export function buildStatusLabel(row: Pick<BuildRowData, 'status' | 'isExpired'>): string {
  if (row.isExpired && (row.status === 'Quoted' || row.status === 'Reserved')) return 'Vencida';
  return statusOf(BUILD_STATUSES, row.status).label;
}

/** Hasta cuándo vale: la reserva (fecha y hora) o la cotización (fecha). */
export function buildValidity(row: BuildRowData): string {
  if (row.status === 'Reserved' && row.reservedUntil) return `Reservada hasta el ${formatDateTime(row.reservedUntil)}`;
  return `Vigente hasta el ${formatDate(row.validUntil)}`;
}

/** Quién la hizo: el cliente o el contacto de la reserva. */
export function buildCustomer(row: BuildRowData): string | null {
  return row.customer ?? (row.contactName && row.contactName.trim().length > 0 ? row.contactName : null);
}

/** Explicación de lo que se cobra (como el escritorio). */
export function buildSummary(row: BuildRowData): string {
  const parts: string[] = [];
  if (row.status === 'Reserved') {
    const channel = row.channel === 'Web' ? ' de la tienda web' : '';
    parts.push(
      row.reservedUntil
        ? `Reserva${channel} vigente hasta el ${formatDateTime(row.reservedUntil)}: al cobrar se consume la reserva (el stock reservado sale una sola vez)`
        : `Reserva${channel}: al cobrar se consume la reserva`,
    );
  } else {
    parts.push(`Cotización vigente hasta el ${formatDate(row.validUntil)}`);
  }
  parts.push('precios congelados');
  const customer = buildCustomer(row);
  if (customer) parts.push(customer);
  if (row.quotedWithErrors) parts.push('cotizada con errores de compatibilidad aceptados');
  return parts.join(' · ');
}

/** Datos para la factura que dejó quien reservó (null si no los dejó): la caja los precarga. */
export function buyerFromBuild(row: BuildRowData): BuyerDraft | null {
  const number = row.buyerDocumentNumber?.trim() ?? '';
  if (number.length === 0) return null;
  return {
    documentType: row.buyerDocumentType ?? DOC_CI,
    documentNumber: number,
    complement: row.buyerComplement ?? '',
    name: row.buyerName ?? row.contactName ?? '',
    email: row.contactEmail ?? '',
    exceptionRequested: false,
  };
}

// ---------------------------------------------------------------------------------------------------- lista para elegir

/** Filtros de la lista «Vender una reserva» (dentro del diálogo). */
export const BUILD_PICKER_FILTERS = { q: '', tipo: '', estado: '', canal: '' };
export type BuildPickerFilters = typeof BUILD_PICKER_FILTERS;

/** Solo las que se pueden cobrar ahora, las que vencen antes primero. */
export function sellableBuilds(rows: readonly BuildListData[]): BuildListData[] {
  const deadline = (row: BuildListData) => (row.status === 'Reserved' && row.reservedUntil ? row.reservedUntil : `${row.validUntil}T23:59:59`);
  return rows.filter(isSellableBuild).sort((a, b) => deadline(a).localeCompare(deadline(b)));
}

export function filterBuilds(rows: readonly BuildListData[], filters: BuildPickerFilters): BuildListData[] {
  return rows.filter(
    (row) =>
      (!filters.tipo || row.kind === filters.tipo) &&
      (!filters.estado || row.status === filters.estado) &&
      (!filters.canal || row.channel === filters.canal) &&
      matchesSearch(filters.q, [row.number, row.name, row.customer, row.contactName, row.contactPhone, row.branchCode]),
  );
}

export const BUILD_CSV: readonly CsvColumn<BuildListData>[] = [
  { header: 'Número', value: (row) => row.number },
  { header: 'Tipo', value: (row) => statusOf(BUILD_KINDS, row.kind).label },
  { header: 'Canal', value: (row) => statusOf(BUILD_CHANNELS, row.channel).label },
  { header: 'Estado', value: (row) => buildStatusLabel(row) },
  { header: 'Nombre', value: (row) => row.name },
  { header: 'Cliente', value: (row) => buildCustomer(row) },
  { header: 'Total (Bs)', value: (row) => row.total },
  { header: 'Reservada hasta', value: (row) => (row.reservedUntil ? new Date(row.reservedUntil) : null) },
  { header: 'Vigente hasta', value: (row) => formatDate(row.validUntil) },
];
