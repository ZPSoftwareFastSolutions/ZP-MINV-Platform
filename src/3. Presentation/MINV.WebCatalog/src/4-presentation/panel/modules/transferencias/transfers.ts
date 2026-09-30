// Módulo «Transferencias» · funciones puras (sin React): estados, filtros (estado, origen, destino, lo que espera a esta
// sucursal, fechas y búsqueda), resumen plegado, columnas del CSV, los almacenes de origen (los de la sucursal activa) y de
// destino (los de las otras sucursales activas), y los formularios de solicitar (con series), recibir (con faltantes,
// motivo y las series que no llegaron) y anular (con motivo), con el pedido EXACTO de cada comando.
//
// Los lados los decide el servidor (regla B-03): cada fila trae `canDispatch`, `canReceive` y `canCancel` para la sesión.
// Los tipos del servidor SALEN DEL CONTRATO (regla P-07). Las validaciones de aquí son COMODIDAD: el servidor decide.

import type { RpcRequestOf, RpcResponseOf } from '@/4-presentation/app/contract';
import { defineStatuses, statusOf, type ComboOption, type SelectOption } from '@/4-presentation/panel/kit';
import { inRange, matchesSearch, monthStart, roundTo, toIsoDate, type CsvColumn } from '@/4-presentation/panel/lib';

// ---------------------------------------------------------------------------------------------------- tipos del contrato

/** Una transferencia de `GetTransfersQuery`. */
export type TransferRecord = RpcResponseOf<'GetTransfersQuery'>[number];
/** El detalle (`GetTransferQuery`): cabecera, líneas (lotes, series, faltantes) y bitácora. */
export type TransferDetailData = RpcResponseOf<'GetTransferQuery'>;
export type TransferLineRecord = TransferDetailData['lines'][number];
/** Una sucursal del directorio (`GetBranchesQuery`) con sus almacenes. */
export type BranchRecord = RpcResponseOf<'GetBranchesQuery'>[number];
/** Ficha breve de un producto (`SearchTechProductsQuery`): stock de la sucursal activa y si lleva serie o IMEI. */
export type TechRecord = RpcResponseOf<'SearchTechProductsQuery'>[number];
/** Una serie disponible (`GetAvailableSerialsQuery`). */
export type SerialRecord = RpcResponseOf<'GetAvailableSerialsQuery'>[number];
/** Lo que responde un comando de transferencias (número y mensaje). */
export type TransferOutcome = RpcResponseOf<'CreateTransferCommand'>;
export type CreateTransferPayload = RpcRequestOf<'CreateTransferCommand'>;
export type ReceiveTransferPayload = RpcRequestOf<'ReceiveTransferCommand'>;
export type CancelTransferPayload = RpcRequestOf<'CancelTransferCommand'>;

// ---------------------------------------------------------------------------------------------------- constantes

export const LIMITS = { notes: 250, reason: 200 } as const;

/** Estados (enumeración `TransferStatus` del servidor). «Despachada» = la mercadería está en tránsito. */
export const TRANSFER_STATES = defineStatuses({
  Pending: { label: 'Pendiente', tone: 'warning' },
  Dispatched: { label: 'Despachada', tone: 'info' },
  Received: { label: 'Recibida', tone: 'success' },
  Cancelled: { label: 'Anulada', tone: 'neutral' },
});

export function transferStateLabel(status: string): string {
  return statusOf(TRANSFER_STATES, status).label;
}

/** «Qué espera de esta sucursal»: despachar (el origen) o recibir (el destino). */
export const TO_DISPATCH = 'despachar';
export const TO_RECEIVE = 'recibir';
export const WAITING_OPTIONS: readonly SelectOption[] = [
  { value: TO_DISPATCH, label: 'Por despachar desde mis sucursales' },
  { value: TO_RECEIVE, label: 'Por recibir en mis sucursales' },
];

/** Cuántas transferencias se piden al servidor (acepta de 1 a 2000; la lista filtra en la página). */
export const TAKE_OPTIONS: readonly SelectOption[] = [
  { value: '300', label: 'Últimas 300' },
  { value: '1000', label: 'Últimas 1.000' },
  { value: '2000', label: 'Últimas 2.000' },
];
export const DEFAULT_TAKE = '300';

export function takeOf(value: string): number {
  return Number(TAKE_OPTIONS.some((option) => option.value === value) ? value : DEFAULT_TAKE);
}

export const OTHER_REASON = 'otro';
export const CANCEL_REASONS: readonly string[] = ['Ya no se necesita', 'Se pidió por error', 'Se abastecerá con una compra'];
export const SHORTAGE_REASONS: readonly string[] = ['No llegó (extraviado en el camino)', 'Llegó dañado', 'Error en el despacho'];

export function reasonOptions(reasons: readonly string[]): SelectOption[] {
  return [...reasons.map((reason) => ({ value: reason, label: reason })), { value: OTHER_REASON, label: 'Otro motivo…' }];
}

export function reasonText(choice: string, other: string): string {
  return choice === OTHER_REASON ? other.trim() : choice;
}

export function reasonProblem(choice: string, other: string, missing = 'Elija el motivo o escriba uno.'): string | null {
  if (!choice) return missing;
  const text = reasonText(choice, other);
  if (!text) return 'Escriba el motivo.';
  if (text.length > LIMITS.reason) return `El motivo admite hasta ${LIMITS.reason} caracteres.`;
  return null;
}

/** Texto del servidor sin la marca «✔»/«✖» del principio. */
export function plainMessage(text: string | null | undefined): string {
  return (text ?? '').replace(/^[\s✔✖✓✗]+/u, '').trim();
}

// ---------------------------------------------------------------------------------------------------- lista

export interface TransferItem {
  key: string;
  row: TransferRecord;
  /** «CM → CB». */
  route: string;
}

export function toTransferItems(rows: readonly TransferRecord[]): TransferItem[] {
  return rows.map((row) => ({ key: row.id, row, route: `${row.fromBranchCode} → ${row.toBranchCode}` }));
}

/** Filtros de la lista (en la dirección). `?pendiente=recibir` lo usa el tablero «Recibir transferencia». */
export const TRANSFER_FILTERS = { q: '', estado: '', origen: '', destino: '', pendiente: '', desde: '', hasta: '', registros: DEFAULT_TAKE };
export type TransferFilters = typeof TRANSFER_FILTERS;

export function filterTransfers(items: readonly TransferItem[], filters: TransferFilters): TransferItem[] {
  const range = { from: filters.desde || null, to: filters.hasta || null };
  return items.filter(
    (item) =>
      (!filters.estado || item.row.status === filters.estado) &&
      (!filters.origen || item.row.fromBranchCode === filters.origen) &&
      (!filters.destino || item.row.toBranchCode === filters.destino) &&
      (filters.pendiente !== TO_DISPATCH || item.row.canDispatch) &&
      (filters.pendiente !== TO_RECEIVE || item.row.canReceive) &&
      inRange(item.row.requestedAt, range) &&
      matchesSearch(filters.q, [
        item.row.number,
        item.row.fromBranch,
        item.row.fromBranchCode,
        item.row.fromWarehouse,
        item.row.toBranch,
        item.row.toBranchCode,
        item.row.toWarehouse,
        item.row.notes,
      ]),
  );
}

/** Sucursales de las listas desplegables: las del directorio y las que aparecen en las transferencias. */
export function branchOptions(branches: readonly BranchRecord[], items: readonly TransferItem[]): SelectOption[] {
  const byCode = new Map<string, string>();
  for (const branch of branches) byCode.set(branch.code, branch.name);
  for (const item of items) {
    if (!byCode.has(item.row.fromBranchCode)) byCode.set(item.row.fromBranchCode, item.row.fromBranch);
    if (!byCode.has(item.row.toBranchCode)) byCode.set(item.row.toBranchCode, item.row.toBranch);
  }
  return [...byCode.entries()].map(([value, name]) => ({ value, label: `${value} · ${name}` })).sort((a, b) => a.value.localeCompare(b.value, 'es'));
}

export const TRANSFERS_CSV: readonly CsvColumn<TransferItem>[] = [
  { header: 'Número', value: (item) => item.row.number },
  { header: 'Origen', value: (item) => `${item.row.fromBranchCode} · ${item.row.fromBranch}` },
  { header: 'Almacén de origen', value: (item) => item.row.fromWarehouse },
  { header: 'Destino', value: (item) => `${item.row.toBranchCode} · ${item.row.toBranch}` },
  { header: 'Almacén de destino', value: (item) => item.row.toWarehouse },
  { header: 'Estado', value: (item) => transferStateLabel(item.row.status) },
  { header: 'Solicitada', value: (item) => new Date(item.row.requestedAt) },
  { header: 'Despachada', value: (item) => (item.row.dispatchedAt ? new Date(item.row.dispatchedAt) : null) },
  { header: 'Recibida', value: (item) => (item.row.receivedAt ? new Date(item.row.receivedAt) : null) },
  { header: 'Productos', value: (item) => item.row.lines },
  { header: 'Cantidad', value: (item) => item.row.quantity },
  { header: 'Valor', value: (item) => (item.row.status === 'Pending' ? null : item.row.value) },
  { header: 'Faltante', value: (item) => item.row.shortage },
  { header: 'Notas', value: (item) => item.row.notes },
];

export interface TransfersSummary {
  pending: { count: number; mine: number };
  transit: { count: number; value: number; toReceive: number };
  receivedThisMonth: { count: number; value: number };
  shortageThisMonth: { quantity: number; transfers: number };
}

/** Los indicadores del escritorio: pendientes de despacho, en tránsito, recibidas y faltantes del mes. */
export function transfersSummary(rows: readonly TransferRecord[], today: string): TransfersSummary {
  const pending = rows.filter((row) => row.status === 'Pending');
  const transit = rows.filter((row) => row.status === 'Dispatched');
  const first = monthStart(today);
  const received = rows.filter((row) => row.status === 'Received' && (toIsoDate(row.receivedAt) ?? '') >= first);
  return {
    pending: { count: pending.length, mine: pending.filter((row) => row.canDispatch).length },
    transit: { count: transit.length, value: roundTo(transit.reduce((sum, row) => sum + row.value, 0), 2), toReceive: transit.filter((row) => row.canReceive).length },
    receivedThisMonth: { count: received.length, value: roundTo(received.reduce((sum, row) => sum + row.value, 0), 2) },
    shortageThisMonth: { quantity: roundTo(received.reduce((sum, row) => sum + row.shortage, 0), 6), transfers: received.filter((row) => row.shortage > 0).length },
  };
}

// ---------------------------------------------------------------------------------------------------- almacenes

export interface WarehouseChoice {
  /** Código del almacén (lo que viaja al servidor). */
  code: string;
  branchCode: string;
  branchName: string;
}

export function warehouseLabel(choice: WarehouseChoice): string {
  return `${choice.code} · ${choice.branchCode} ${choice.branchName}`;
}

/** Almacenes de ORIGEN: los de la sucursal activa (o, en la vista de todas las sucursales, los de las visibles). */
export function originChoices(branches: readonly BranchRecord[], activeBranchId: string | null): WarehouseChoice[] {
  return branches
    .filter((branch) => branch.isActive && branch.isVisible && (activeBranchId === null || branch.id === activeBranchId))
    .flatMap((branch) => branch.warehouses.map((code) => ({ code, branchCode: branch.code, branchName: branch.name })));
}

/** Almacenes de DESTINO: los de las otras sucursales activas. */
export function destinationChoices(branches: readonly BranchRecord[], originBranchCode: string | null): WarehouseChoice[] {
  return branches
    .filter((branch) => branch.isActive && branch.code !== originBranchCode)
    .flatMap((branch) => branch.warehouses.map((code) => ({ code, branchCode: branch.code, branchName: branch.name })));
}

// ---------------------------------------------------------------------------------------------------- series

export function parseSerials(text: string): string[] {
  return text
    .split(/[\r\n,;\t]+/)
    .map((serial) => serial.trim())
    .filter((serial) => serial.length > 0);
}

export function serialWord(kind: string | null | undefined, count = 2): string {
  if (kind === 'Imei') return 'IMEI';
  return count === 1 ? 'serie' : 'series';
}

/** Problema de las series de una línea (una por unidad, sin repetidas), o null. */
export function serialsProblem(serials: readonly string[], expected: number, kind: string | null | undefined, verb = 'Elija'): string | null {
  if (!Number.isInteger(expected)) return 'Con serie, la cantidad es entera.';
  if (serials.length !== expected) return `${verb} ${expected} ${serialWord(kind, expected)} (una por unidad): hay ${serials.length}.`;
  const seen = new Set<string>();
  for (const serial of serials) {
    const upper = serial.toUpperCase();
    if (seen.has(upper)) return `La ${kind === 'Imei' ? 'IMEI' : 'serie'} ${serial} está repetida.`;
    seen.add(upper);
  }
  return null;
}

// ---------------------------------------------------------------------------------------------------- solicitar

export interface TransferDraftLine {
  sku: string;
  name: string;
  /** Existencias en la sucursal activa (referencia; el servidor valida al despachar). */
  stock: number;
  trackSerials: boolean;
  serialKind: TechRecord['serialKind'];
  quantity: number | null;
  /** Series elegidas (productos serializados). */
  serials: string[];
}

export interface TransferDraft {
  fromWarehouseCode: string;
  toWarehouseCode: string;
  notes: string;
  lines: TransferDraftLine[];
}

export function productChoices(products: readonly TechRecord[], lines: readonly TransferDraftLine[]): ComboOption<TechRecord>[] {
  return products
    .filter((product) => !lines.some((line) => line.sku === product.sku))
    .map((product) => ({
      value: product.sku,
      label: product.name,
      description: `${product.sku} · stock ${product.stock}${product.trackSerials ? ` · con ${product.serialKind === 'Imei' ? 'IMEI' : 'serie'}` : ''}`,
      data: product,
    }));
}

export function addLine(draft: TransferDraft, product: TechRecord): TransferDraft {
  if (draft.lines.some((line) => line.sku === product.sku)) return draft;
  const line: TransferDraftLine = {
    sku: product.sku,
    name: product.name,
    stock: product.stock,
    trackSerials: product.trackSerials,
    serialKind: product.serialKind,
    quantity: 1,
    serials: [],
  };
  return { ...draft, lines: [...draft.lines, line] };
}

export interface TransferLineProblems {
  quantity?: string;
  serials?: string;
}

export interface TransferProblems {
  fromWarehouseCode?: string;
  toWarehouseCode?: string;
  notes?: string;
  lines?: string;
  byLine: Record<string, TransferLineProblems>;
}

export function transferProblems(draft: TransferDraft): TransferProblems {
  const problems: TransferProblems = { byLine: {} };
  if (!draft.fromWarehouseCode) problems.fromWarehouseCode = 'Elija el almacén de origen.';
  if (!draft.toWarehouseCode) problems.toWarehouseCode = 'Elija el almacén de destino.';
  else if (draft.toWarehouseCode === draft.fromWarehouseCode) problems.toWarehouseCode = 'El destino no puede ser el mismo almacén.';
  if (draft.notes.trim().length > LIMITS.notes) problems.notes = `Las notas admiten hasta ${LIMITS.notes} caracteres.`;
  if (draft.lines.length === 0) problems.lines = 'Agregue al menos un producto.';
  for (const line of draft.lines) {
    const found: TransferLineProblems = {};
    if (line.quantity === null) found.quantity = 'Indique la cantidad.';
    else if (line.quantity <= 0) found.quantity = 'La cantidad debe ser mayor que 0.';
    else if (line.trackSerials && !Number.isInteger(line.quantity)) found.quantity = 'Con serie, la cantidad es entera (una serie por unidad).';
    else if (line.trackSerials) {
      const problem = serialsProblem(line.serials, line.quantity, line.serialKind);
      if (problem) found.serials = problem;
    }
    if (found.quantity || found.serials) problems.byLine[line.sku] = found;
  }
  return problems;
}

export function hasTransferProblems(problems: TransferProblems): boolean {
  return Boolean(problems.fromWarehouseCode || problems.toWarehouseCode || problems.notes || problems.lines || Object.keys(problems.byLine).length > 0);
}

/** El pedido de `CreateTransferCommand` (todos los parámetros; las series solo en los productos que las llevan). */
export function createTransferPayload(draft: TransferDraft): CreateTransferPayload {
  const notes = draft.notes.trim();
  return {
    toWarehouseCode: draft.toWarehouseCode,
    lines: draft.lines.map((line) => ({ sku: line.sku, quantity: line.quantity ?? 0, serials: line.trackSerials ? [...line.serials] : null })),
    notes: notes || null,
    fromWarehouseCode: draft.fromWarehouseCode || null,
  };
}

// ---------------------------------------------------------------------------------------------------- recibir

export interface ReceiptDraftLine {
  received: number | null;
  reasonChoice: string;
  reasonOther: string;
  /** Series que NO llegaron (productos serializados con faltante). */
  missing: string[];
}

export function receiptDraftOf(lines: readonly TransferLineRecord[]): Record<string, ReceiptDraftLine> {
  return Object.fromEntries(lines.map((line) => [line.sku, { received: line.quantity, reasonChoice: '', reasonOther: '', missing: [] }]));
}

export function shortageOf(line: TransferLineRecord, draft: ReceiptDraftLine | undefined): number {
  const received = draft?.received ?? 0;
  return Math.max(0, roundTo(line.quantity - received, 6));
}

export interface ReceiptLineProblems {
  received?: string;
  reason?: string;
  missing?: string;
}

export function receiptProblems(lines: readonly TransferLineRecord[], draft: Readonly<Record<string, ReceiptDraftLine>>): Record<string, ReceiptLineProblems> {
  const problems: Record<string, ReceiptLineProblems> = {};
  for (const line of lines) {
    const entry = draft[line.sku];
    const found: ReceiptLineProblems = {};
    const serialized = (line.serials?.length ?? 0) > 0;
    if (!entry || entry.received === null) found.received = 'Indique lo que llegó (0 si no llegó nada).';
    else if (entry.received < 0) found.received = 'Lo recibido no puede ser negativo.';
    else if (entry.received > line.quantity) found.received = `No se puede recibir más de lo despachado (${line.quantity}).`;
    else if (serialized && !Number.isInteger(entry.received)) found.received = 'Lleva serie: lo recibido es un número entero.';
    else if (entry.received < line.quantity) {
      const reason = reasonProblem(entry.reasonChoice, entry.reasonOther, 'Indique el motivo del faltante.');
      if (reason) found.reason = reason;
      if (serialized) {
        const shortage = shortageOf(line, entry);
        if (entry.missing.length !== shortage)
          found.missing = `${shortage === 1 ? 'Marque la unidad que no llegó' : `Marque las ${shortage} unidades que no llegaron`} (marcadas: ${entry.missing.length}).`;
      }
    }
    if (found.received || found.reason || found.missing) problems[line.sku] = found;
  }
  return problems;
}

/** El pedido de `ReceiveTransferCommand`: cada línea con lo recibido y, si falta algo, el motivo y las series faltantes. */
export function receiveTransferPayload(id: string, lines: readonly TransferLineRecord[], draft: Readonly<Record<string, ReceiptDraftLine>>): ReceiveTransferPayload {
  return {
    id,
    lines: lines.map((line) => {
      const entry = draft[line.sku];
      const received = entry?.received ?? line.quantity;
      const short = received < line.quantity;
      const serialized = (line.serials?.length ?? 0) > 0;
      return {
        sku: line.sku,
        receivedQuantity: received,
        shortageReason: short && entry ? reasonText(entry.reasonChoice, entry.reasonOther) : null,
        missingSerials: short && serialized && entry ? [...entry.missing] : null,
      };
    }),
  };
}

// ---------------------------------------------------------------------------------------------------- anular

export function cancelTransferPayload(id: string, choice: string, other: string): CancelTransferPayload {
  return { id, reason: reasonText(choice, other) };
}
