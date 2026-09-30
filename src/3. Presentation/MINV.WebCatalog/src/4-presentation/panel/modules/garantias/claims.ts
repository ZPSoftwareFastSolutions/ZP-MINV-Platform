// Módulo «Garantías» · funciones puras (sin React): estados de un caso RMA y de su bitácora, el pedido de la lista (lo que
// filtra el servidor) y lo que se filtra en la página, columnas del CSV, el resumen plegado, los pasos siguientes que el
// SERVIDOR permite (`nextStatuses`, regla T-05: la página nunca decide una transición) con sus textos, y los textos de la
// garantía y de la venta. Se prueban sin React en `claims.test.ts`.

import type { RpcRequestOf, RpcResponseOf } from '@/4-presentation/app/contract';
import { defineStatuses, statusOptions, type ComboOption, type SelectOption } from '@/4-presentation/panel/kit';
import { formatDate, formatNumber, inRange, matchesSearch, type CsvColumn } from '@/4-presentation/panel/lib';

// ---------------------------------------------------------------------------------------------------- tipos del contrato

/** Una fila de `GetWarrantyClaimsQuery` tal como la manda el servidor. */
export type ClaimRecord = RpcResponseOf<'GetWarrantyClaimsQuery'>[number];
/** El detalle de un caso (`GetWarrantyClaimQuery`): el caso, la garantía de la unidad, la bitácora y los pasos siguientes. */
export type ClaimDetailData = RpcResponseOf<'GetWarrantyClaimQuery'>;
export type ClaimEventRecord = ClaimDetailData['events'][number];
export type ClaimStatusCode = ClaimRecord['status'];
export type ClaimActionCode = ClaimEventRecord['action'];
/** Garantía y venta vigente de una unidad (`GetWarrantyStatusQuery`). */
export type WarrantyInfo = RpcResponseOf<'GetWarrantyStatusQuery'>;
/** Unidad disponible para el reemplazo (`GetAvailableSerialsQuery`). */
export type AvailableSerialRecord = RpcResponseOf<'GetAvailableSerialsQuery'>[number];
/** Cliente de la cartera (`GetCustomersQuery`). */
export type CustomerRecord = RpcResponseOf<'GetCustomersQuery'>['customers'][number];
type UnitStatusCode = WarrantyInfo['status'];

// ---------------------------------------------------------------------------------------------------- estados

/** Estado de un caso (enumeración `WarrantyClaimStatus`), con los textos del escritorio. */
export const CLAIM_STATES = defineStatuses<ClaimStatusCode>({
  Received: { label: 'Recibido', tone: 'info' },
  Diagnosing: { label: 'En diagnóstico', tone: 'warning' },
  SentToSupplier: { label: 'En el proveedor', tone: 'warning' },
  Repaired: { label: 'Reparado', tone: 'success' },
  Replaced: { label: 'Reemplazado', tone: 'success' },
  Rejected: { label: 'Rechazado', tone: 'danger' },
  Delivered: { label: 'Entregado', tone: 'neutral' },
});

/** Hechos de la bitácora del caso (enumeración `WarrantyClaimAction`). */
export const CLAIM_ACTIONS = defineStatuses<ClaimActionCode>({
  Opened: { label: 'Caso abierto', tone: 'info' },
  StatusChanged: { label: 'Cambio de estado', tone: 'accent' },
  NoteAdded: { label: 'Nota', tone: 'neutral' },
  ReplacementIssued: { label: 'Reposición entregada', tone: 'success' },
  Closed: { label: 'Caso cerrado', tone: 'neutral' },
});

/** Cobertura decidida al abrir el caso: cubierto por la garantía o reparación con cargo (marcada explícitamente). */
export type Coverage = 'garantia' | 'cargo';

export const COVERAGES = defineStatuses<Coverage>({
  garantia: { label: 'En garantía', tone: 'success' },
  cargo: { label: 'Con cargo', tone: 'warning' },
});

/** Estado de la unidad (para la consulta de la serie al abrir un caso). */
export const UNIT_STATES = defineStatuses<UnitStatusCode>({
  InStock: { label: 'En stock', tone: 'success' },
  Sold: { label: 'Vendida', tone: 'info' },
  InTransit: { label: 'En tránsito', tone: 'accent' },
  Returned: { label: 'Devuelta por el cliente', tone: 'warning' },
  InRma: { label: 'En garantía (RMA)', tone: 'warning' },
  ReturnedToSupplier: { label: 'Devuelta al proveedor', tone: 'neutral' },
  Scrapped: { label: 'Dada de baja', tone: 'neutral' },
  Reserved: { label: 'Reservada', tone: 'accent' },
});

function hasKey<K extends string>(map: Readonly<Record<K, unknown>>, value: string): value is K {
  return Object.prototype.hasOwnProperty.call(map, value);
}

export function claimStatusLabel(status: string): string {
  return hasKey(CLAIM_STATES, status) ? CLAIM_STATES[status].label : status;
}

export function coverageOf(row: Pick<ClaimRecord, 'isInWarranty'>): Coverage {
  return row.isInWarranty ? 'garantia' : 'cargo';
}

/** «Cambio de estado · en diagnóstico», «Caso cerrado · entregado», «Nota». */
export function eventTitle(event: Pick<ClaimEventRecord, 'action' | 'status'>): string {
  const action = hasKey(CLAIM_ACTIONS, event.action) ? CLAIM_ACTIONS[event.action].label : event.action;
  if (event.action === 'StatusChanged' || event.action === 'Closed') return `${action} · ${claimStatusLabel(event.status).toLowerCase()}`;
  return action;
}

/** La bitácora del caso, lo más reciente primero. */
export function claimTimeline(events: readonly ClaimEventRecord[]): ClaimEventRecord[] {
  return [...events].sort((a, b) => new Date(b.occurredAt).getTime() - new Date(a.occurredAt).getTime());
}

// ---------------------------------------------------------------------------------------------------- pasos siguientes

/** Cómo se ve el botón de un paso: el camino normal, uno secundario o el peligroso (rechazar). */
export type StepKind = 'primary' | 'outline' | 'danger';

export interface NextStep {
  next: ClaimStatusCode;
  /** Texto del botón («Pasar a diagnóstico»). */
  label: string;
  kind: StepKind;
}

/** Texto del botón que lleva el caso al estado `next` (los del escritorio). */
export const STEP_LABELS: Readonly<Record<ClaimStatusCode, string>> = {
  Received: 'Recibir',
  Diagnosing: 'Pasar a diagnóstico',
  SentToSupplier: 'Enviar al proveedor',
  Repaired: 'Marcar reparado',
  Replaced: 'Reemplazar con otra unidad',
  Rejected: 'Rechazar la garantía',
  Delivered: 'Entregar al cliente',
};

/** Qué significa cada paso (ayuda del diálogo, como el escritorio). */
export const STEP_HELP: Readonly<Record<ClaimStatusCode, string>> = {
  Received: '',
  Diagnosing: 'El técnico revisa el equipo. Después: repararlo, enviarlo al proveedor, reemplazarlo o rechazar la garantía.',
  SentToSupplier: 'El equipo sale al proveedor o al servicio técnico autorizado (sigue en garantía, sin entrar al stock).',
  Repaired: 'El equipo quedó reparado: al entregarlo vuelve a su dueño.',
  Replaced: 'El cliente ya recibió la unidad de reemplazo: el caso queda listo para cerrarse.',
  Rejected: 'La garantía no cubre la falla: el equipo se devuelve al cliente sin reparar (o con reparación con cargo).',
  Delivered: 'Cierra el caso: el cliente retira su equipo (reparado o rechazado) o ya recibió el reemplazo.',
};

/** La pregunta del diálogo de confirmación. */
export function stepQuestion(next: ClaimStatusCode, number: string): string {
  switch (next) {
    case 'Diagnosing':
      return `¿Pasar el caso ${number} a diagnóstico?`;
    case 'SentToSupplier':
      return `¿Enviar el equipo del caso ${number} al proveedor?`;
    case 'Repaired':
      return `¿Marcar el caso ${number} como reparado?`;
    case 'Replaced':
      return `¿Marcar el caso ${number} como reemplazado?`;
    case 'Rejected':
      return `¿Rechazar la garantía del caso ${number}?`;
    case 'Delivered':
      return `¿Entregar el equipo del caso ${number} al cliente?`;
    default:
      return `¿Cambiar el estado del caso ${number}?`;
  }
}

export function stepKind(next: ClaimStatusCode): StepKind {
  if (next === 'Rejected') return 'danger';
  if (next === 'SentToSupplier') return 'outline';
  return 'primary';
}

/** Los pasos que el SERVIDOR permite desde el estado actual (`nextStatuses`), en su orden. */
export function nextSteps(detail: Pick<ClaimDetailData, 'nextStatuses'>): NextStep[] {
  return detail.nextStatuses.map((next) => ({ next, label: STEP_LABELS[next] ?? claimStatusLabel(next), kind: stepKind(next) }));
}

/** Reparar, reemplazar o rechazar exigen la resolución (el servidor la pide). */
export function needsResolution(next: ClaimStatusCode): boolean {
  return next === 'Repaired' || next === 'Replaced' || next === 'Rejected';
}

/** Enviar al proveedor pide el proveedor (o el preferido del producto). */
export function needsSupplier(next: ClaimStatusCode): boolean {
  return next === 'SentToSupplier';
}

/** «Reemplazar» sin una unidad de reemplazo registrada se hace con la entrega del reemplazo (sale del stock). */
export function usesReplacement(next: ClaimStatusCode, claim: Pick<ClaimRecord, 'replacementSerial'>): boolean {
  return next === 'Replaced' && !claim.replacementSerial;
}

/** Resoluciones sugeridas (las del escritorio) por paso. */
export const RESOLUTIONS: Readonly<Partial<Record<ClaimStatusCode, readonly string[]>>> = {
  Repaired: ['Se reemplazó el componente dañado', 'Actualización de firmware y pruebas OK', 'Limpieza y cambio de pasta térmica'],
  Rejected: ['Daño por mal uso (golpe o líquido)', 'Sello de garantía roto', 'No se reprodujo la falla'],
  Replaced: ['Reemplazo por falla de fábrica', 'Reemplazo con unidad nueva del proveedor'],
};

export const OTHER_OPTION = 'otro';
export const TEXT_MAX = 500;

export function suggestionOptions(values: readonly string[], otherLabel: string): SelectOption[] {
  return [...values.map((value) => ({ value, label: value })), { value: OTHER_OPTION, label: otherLabel }];
}

export function chosenText(choice: string, other: string): string {
  return (choice === OTHER_OPTION ? other : choice).trim();
}

export const DEFAULT_REPLACEMENT_RESOLUTION = 'Reemplazo por falla de fábrica';

// ---------------------------------------------------------------------------------------------------- filas y filtros

export interface ClaimItem {
  key: string;
  row: ClaimRecord;
  coverage: Coverage;
  /** Sin entregar. */
  open: boolean;
}

export function toClaimItems(rows: readonly ClaimRecord[]): ClaimItem[] {
  return rows.map((row) => ({ key: row.number, row, coverage: coverageOf(row), open: row.status !== 'Delivered' }));
}

/** Valor del filtro «Estado» para los casos sin entregar (el que se ve al entrar, como el escritorio). */
export const OPEN_FILTER = 'abiertos';

/**
 * Filtros de la lista (en la dirección). Va al SERVIDOR: `estado` (abiertos, todos o uno). Se filtran en la PÁGINA:
 * `q`, `sucursal`, `cobertura`, `dias` y las fechas de recepción (`desde`, `hasta`).
 */
export const CLAIM_FILTERS = { q: '', estado: OPEN_FILTER, sucursal: '', cobertura: '', dias: '', desde: '', hasta: '' };
export type ClaimFilters = typeof CLAIM_FILTERS;

export const STATE_FILTER_OPTIONS: readonly SelectOption[] = [{ value: OPEN_FILTER, label: 'Abiertos (sin entregar)' }, ...statusOptions(CLAIM_STATES)];
export const COVERAGE_OPTIONS: readonly SelectOption[] = statusOptions(COVERAGES);
export const DAYS_OPTIONS: readonly SelectOption[] = [
  { value: '7', label: 'Más de 7 días' },
  { value: '15', label: 'Más de 15 días' },
  { value: '30', label: 'Más de 30 días' },
];

/** El pedido de la lista: SIEMPRE con todos los parámetros. */
export function claimsRequest(estado: string): RpcRequestOf<'GetWarrantyClaimsQuery'> {
  if (estado === OPEN_FILTER) return { status: null, onlyOpen: true };
  return { status: estado && hasKey(CLAIM_STATES, estado) ? estado : null, onlyOpen: false };
}

export function filterClaims(items: readonly ClaimItem[], filters: ClaimFilters): ClaimItem[] {
  const range = { from: filters.desde || null, to: filters.hasta || null };
  const days = Number(filters.dias);
  return items.filter(
    (item) =>
      (!filters.sucursal || item.row.branchCode === filters.sucursal) &&
      (!filters.cobertura || item.coverage === filters.cobertura) &&
      (!filters.dias || !Number.isFinite(days) || item.row.daysOpen > days) &&
      inRange(item.row.receivedAt, range) &&
      matchesSearch(filters.q, [
        item.row.number,
        item.row.serial,
        item.row.customer,
        item.row.product,
        item.row.sku,
        item.row.issue,
        item.row.supplier,
        item.row.replacementSerial,
      ]),
  );
}

/** Sucursales de la lista, con su nombre si la sesión la conoce. */
export function branchOptions(items: readonly ClaimItem[], branches: readonly { code: string; name: string }[], selected = ''): SelectOption[] {
  const codes = new Set(items.map((item) => item.row.branchCode).filter(Boolean));
  if (selected) codes.add(selected);
  return [...codes]
    .sort((a, b) => a.localeCompare(b, 'es'))
    .map((code) => {
      const branch = branches.find((item) => item.code === code);
      return { value: code, label: branch ? `${code} · ${branch.name}` : code };
    });
}

export function branchLabel(code: string, branches: readonly { code: string; name: string }[]): string {
  const branch = branches.find((item) => item.code === code);
  return branch ? `${code} · ${branch.name}` : code;
}

/** «Cerrado el 29/09/2026», «1 día abierto», «12 días abierto». */
export function daysText(row: Pick<ClaimRecord, 'closedAt' | 'daysOpen'>): string {
  if (row.closedAt) return `Cerrado el ${formatDate(row.closedAt)}`;
  return row.daysOpen === 1 ? '1 día abierto' : `${formatNumber(row.daysOpen)} días abierto`;
}

export const CLAIMS_CSV: readonly CsvColumn<ClaimItem>[] = [
  { header: 'Caso', value: (item) => item.row.number },
  { header: 'Sucursal', value: (item) => item.row.branchCode },
  { header: 'Recibido', value: (item) => new Date(item.row.receivedAt) },
  { header: 'Estado', value: (item) => claimStatusLabel(item.row.status) },
  { header: 'Cobertura', value: (item) => COVERAGES[item.coverage].label },
  { header: 'Serie o IMEI', value: (item) => item.row.serial },
  { header: 'SKU', value: (item) => item.row.sku },
  { header: 'Producto', value: (item) => item.row.product },
  { header: 'Cliente', value: (item) => item.row.customer },
  { header: 'Falla reportada', value: (item) => item.row.issue },
  { header: 'Garantía hasta', value: (item) => (item.row.warrantyUntil ? formatDate(item.row.warrantyUntil) : null) },
  { header: 'Proveedor', value: (item) => item.row.supplier },
  { header: 'Resolución', value: (item) => item.row.resolution },
  { header: 'Reemplazo', value: (item) => item.row.replacementSerial },
  { header: 'Días', value: (item) => item.row.daysOpen },
  { header: 'Cerrado', value: (item) => (item.row.closedAt ? new Date(item.row.closedAt) : null) },
];

// ---------------------------------------------------------------------------------------------------- resumen plegado

export interface ClaimsSummary {
  total: number;
  open: number;
  /** Días del caso abierto más antiguo. */
  oldest: number;
  /** En diagnóstico o en el proveedor. */
  workshop: number;
  atSupplier: number;
  chargeable: number;
  delivered: number;
  replaced: number;
}

/** Los indicadores del escritorio, sobre todos los casos. */
export function claimsSummary(rows: readonly ClaimRecord[]): ClaimsSummary {
  const open = rows.filter((row) => row.status !== 'Delivered');
  return {
    total: rows.length,
    open: open.length,
    oldest: open.reduce((max, row) => Math.max(max, row.daysOpen), 0),
    workshop: open.filter((row) => row.status === 'Diagnosing' || row.status === 'SentToSupplier').length,
    atSupplier: open.filter((row) => row.status === 'SentToSupplier').length,
    chargeable: rows.filter((row) => !row.isInWarranty).length,
    delivered: rows.filter((row) => row.status === 'Delivered').length,
    replaced: rows.filter((row) => Boolean(row.replacementSerial)).length,
  };
}

/** Casos por estado (barras del resumen), en el orden de los estados. */
export function statusBars(rows: readonly ClaimRecord[]): { label: string; value: number }[] {
  return (Object.keys(CLAIM_STATES) as ClaimStatusCode[])
    .map((status) => ({ label: CLAIM_STATES[status].label, value: rows.filter((row) => row.status === status).length }))
    .filter((bar) => bar.value > 0);
}

// ---------------------------------------------------------------------------------------------------- garantía y venta

/** «1 mes», «6 meses», «1 año», «2 años». */
export function monthsText(months: number): string {
  if (months >= 12 && months % 12 === 0) return months === 12 ? '1 año' : `${months / 12} años`;
  return months === 1 ? '1 mes' : `${formatNumber(months)} meses`;
}

/** La garantía de la unidad en una línea (la calcula el servidor: fecha de la venta + meses del producto, regla T-04). */
export function warrantyLine(info: Pick<WarrantyInfo, 'warrantyMonths' | 'warrantyUntil' | 'inWarranty'>): { text: string; tone: 'success' | 'danger' | 'neutral' | 'info' } {
  if (info.warrantyMonths <= 0) return { text: 'El producto no tiene garantía', tone: 'neutral' };
  if (!info.warrantyUntil) return { text: `Garantía de ${monthsText(info.warrantyMonths)} (corre desde la venta)`, tone: 'info' };
  return info.inWarranty
    ? { text: `En garantía hasta el ${formatDate(info.warrantyUntil)}`, tone: 'success' }
    : { text: `Garantía vencida el ${formatDate(info.warrantyUntil)}`, tone: 'danger' };
}

/** «vendido el 12/03/2026 · F-CM-000123 · Mariana Céspedes». */
export function saleLine(detail: Pick<ClaimDetailData, 'invoiceNumber' | 'warranty'>): string {
  const warranty = detail.warranty;
  return [warranty.soldOn ? `vendido el ${formatDate(warranty.soldOn)}` : null, detail.invoiceNumber ?? warranty.invoiceNumber, warranty.customer]
    .filter((part): part is string => Boolean(part && part.trim()))
    .join(' · ');
}

// ---------------------------------------------------------------------------------------------------- abrir un caso

/** Fallas frecuentes (sugerencias del escritorio): un clic las escribe en el campo. */
export const ISSUE_SUGGESTIONS: readonly string[] = [
  'No enciende',
  'Se reinicia solo / pantallazos azules',
  'Artefactos en pantalla',
  'No carga la batería',
  'Ruido o temperatura excesiva',
  'Joystick con drift',
];

/** Notas frecuentes de la bitácora. */
export const NOTE_SUGGESTIONS: readonly string[] = ['Se contactó al cliente', 'Se pidió el repuesto al proveedor', 'Diagnóstico: falla de fábrica', 'Equipo listo para retirar'];

export function issueProblem(issue: string): string | null {
  const text = issue.trim();
  if (text.length < 5) return 'Describa la falla reportada (al menos 5 caracteres).';
  if (text.length > TEXT_MAX) return `La falla admite hasta ${TEXT_MAX} caracteres.`;
  return null;
}

export function noteProblem(note: string): string | null {
  const text = note.trim();
  if (!text) return 'Escriba la nota.';
  if (text.length > TEXT_MAX) return `La nota admite hasta ${TEXT_MAX} caracteres.`;
  return null;
}

/** Aviso de la consulta de la serie antes de abrir el caso (el servidor vuelve a decidir). */
export function lookupWarning(info: Pick<WarrantyInfo, 'status' | 'openClaim'>): string | null {
  if (info.openClaim) return `La serie ya tiene el caso ${info.openClaim} abierto.`;
  if (info.status !== 'Sold' && info.status !== 'Returned') {
    const label = hasKey(UNIT_STATES, info.status) ? UNIT_STATES[info.status].label.toLowerCase() : info.status;
    return `Solo se abre un caso de garantía de una unidad vendida: la serie está ${label}.`;
  }
  return null;
}

/** La venta es de otra sucursal (o no hay venta visible): hay que indicar el cliente del caso. */
export function needsCustomer(info: Pick<WarrantyInfo, 'customerCode'>): boolean {
  return info.customerCode === null;
}

/** Clientes activos para elegir el del caso. */
export function customerOptions(customers: readonly CustomerRecord[]): ComboOption<CustomerRecord>[] {
  return customers
    .filter((customer) => customer.isActive)
    .map((customer) => ({ value: customer.code, label: customer.name, description: customer.taxId ? `${customer.code} · NIT/CI ${customer.taxId}` : customer.code, data: customer }))
    .sort((a, b) => a.label.localeCompare(b.label, 'es'));
}

/** Unidades del mismo producto para el reemplazo (nunca la misma del caso). */
export function replacementOptions(available: readonly AvailableSerialRecord[], claimSerial: string): ComboOption<AvailableSerialRecord>[] {
  return available
    .filter((unit) => unit.serial !== claimSerial)
    .map((unit) => ({ value: unit.serial, label: unit.serial, description: [unit.branch, unit.warehouse].filter(Boolean).join(' · ') || unit.sku, data: unit }));
}

// ---------------------------------------------------------------------------------------------------- enlaces

/** Series con el detalle de esa unidad abierto (y su producto, por si la serie existe en dos). */
export function seriesPath(serial: string, sku: string): string {
  return `series?${new URLSearchParams({ q: serial, producto: sku, ver: serial }).toString()}`;
}

/** Ventas › las ventas de un cliente (parámetro del módulo «Ventas»). */
export function customerSalesPath(code: string): string {
  return `ventas?${new URLSearchParams({ cliente: code }).toString()}`;
}

/** El mensaje de éxito de un cambio de estado. */
export function movedText(row: Pick<ClaimRecord, 'number' | 'status'>): string {
  return `Caso ${row.number}: ${claimStatusLabel(row.status).toLowerCase()}`;
}
