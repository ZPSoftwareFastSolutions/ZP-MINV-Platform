// Módulo «Movimientos» · funciones puras (sin React): los tipos que la sesión puede registrar (los manuales del
// escritorio, según su dominio y permiso), la vista previa del poka-yoke (lo que quedaría en la posición), las
// validaciones de comodidad del formulario (cantidad, observación obligatoria, series), el contenido EXACTO de
// `RegisterMovementCommand`, las filas de la lista de últimos movimientos con sus filtros, las columnas del CSV y la
// tendencia. Los textos siguen al escritorio (MovementViewModel, MovementItem y MovementTypeOption).
//
// La validación que MANDA es la del servidor (regla P-01 y A-09): el poka-yoke de la página solo avisa y bloquea el
// botón; el dominio vuelve a comprobar el stock, la observación, las series, la fecha y los permisos al registrar.

import type { RpcRequestOf, RpcResponseOf } from '@/4-presentation/app/contract';
import { ROUTES } from '@/4-presentation/app/routes';
import { formatDate, formatQuantity, inRange, matchesSearch, toDate, type CsvColumn } from '@/4-presentation/panel/lib';

// ---------------------------------------------------------------------------------------------------- tipos del servidor

export type RecentMovementRecord = RpcResponseOf<'GetRecentMovementsQuery'>[number];
export type MovementTypeRecord = RpcResponseOf<'GetMovementTypesQuery'>[number];
export type ProductLookupRecord = RpcResponseOf<'GetProductLookupQuery'>[number];
export type BinRecord = RpcResponseOf<'GetBinsQuery'>[number];
export type ProductCardData = RpcResponseOf<'GetProductCardQuery'>;
export type ProductTechData = RpcResponseOf<'GetProductTechQuery'>;
export type AvailableSerialRecord = RpcResponseOf<'GetAvailableSerialsQuery'>[number];
export type TrendDayRecord = RpcResponseOf<'GetMovementTrendQuery'>[number];
/** Contenido de `RegisterMovementCommand`. */
export type RegisterPayload = RpcRequestOf<'RegisterMovementCommand'>;

// ---------------------------------------------------------------------------------------------------- tipos de movimiento

/**
 * Tipos que se registran a mano, en el orden del escritorio (los de compras, transferencias y garantías los generan sus
 * propios procesos): entrada, salida, ajustes, devolución de cliente, saldo inicial y venta.
 */
export const MANUAL_TYPES: readonly string[] = ['ENTRADA', 'SALIDA', 'AJUSTE_POS', 'AJUSTE_NEG', 'DEVOLUCION_CLIENTE', 'SALDO_INICIAL', 'VENTA_POS'];

/** Lote por defecto del servidor (`Batch.DefaultLotNumber`): sin lote, el movimiento se registra ahí. */
export const DEFAULT_LOT = 'SIN-LOTE';

export interface RegisterRights {
  /** `inventory.movements.register.warehouse`: tipos de bodega (entradas, ajustes, saldo inicial). */
  warehouse: boolean;
  /** `inventory.movements.register.sales`: tipos de ventas (salidas, ventas, devoluciones de clientes). */
  sales: boolean;
}

/** Los tipos manuales que la sesión puede registrar (el servidor vuelve a decidir con el mismo criterio). */
export function allowedTypes(types: readonly MovementTypeRecord[], rights: RegisterRights): MovementTypeRecord[] {
  return types
    .filter((type) => MANUAL_TYPES.includes(type.code) && (type.domain === 'Sales' ? rights.sales : rights.warehouse))
    .sort((a, b) => MANUAL_TYPES.indexOf(a.code) - MANUAL_TYPES.indexOf(b.code));
}

/**
 * Tipo con el que se abre el registro: un código (`ENTRADA`), `ajuste` (el ajuste negativo o, si no se puede, el
 * positivo) o cualquier otra cosa (`1`): el primero permitido. Un tipo no permitido abre el primero permitido.
 */
export function initialType(requested: string | null, allowed: readonly MovementTypeRecord[]): string {
  const codes = allowed.map((type) => type.code);
  if (requested === 'ajuste') return codes.find((code) => code === 'AJUSTE_NEG') ?? codes.find((code) => code === 'AJUSTE_POS') ?? codes[0] ?? '';
  if (requested && codes.includes(requested.toUpperCase())) return requested.toUpperCase();
  return codes[0] ?? '';
}

/** «AJUSTE (-)» → «Ajuste (-)»; «VENTA POS» → «Venta POS» (como `Fmt.SentenceCase`). */
export function typeTitle(name: string): string {
  const words = name.trim().toLocaleLowerCase('es').split(/\s+/).filter(Boolean);
  if (words.length === 0) return '';
  const joined = words.map((word) => (['pos', 'iva', 'nit', 'rma'].includes(word) ? word.toUpperCase() : word)).join(' ');
  return `${joined[0].toLocaleUpperCase('es')}${joined.slice(1)}`;
}

export function increases(type: Pick<MovementTypeRecord, 'stockFactor'>): boolean {
  return type.stockFactor > 0;
}

export function typeOptions(types: readonly MovementTypeRecord[]): { value: string; label: string }[] {
  return types.map((type) => ({ value: type.code, label: `${typeTitle(type.name)} (${increases(type) ? 'suma' : 'resta'})` }));
}

// ---------------------------------------------------------------------------------------------------- poka-yoke

/** Disponible en esa posición y en el lote por defecto (donde se registra sin lote): el que protege el poka-yoke. */
export function binAvailable(card: Pick<ProductCardData, 'bins'> | undefined, binCode: string): number {
  if (!card) return 0;
  return card.bins.filter((bin) => bin.binCode === binCode && bin.lotNumber === DEFAULT_LOT).reduce((sum, bin) => sum + bin.available, 0);
}

/** Lo que quedaría en la posición (null si falta el tipo o la cantidad). */
export function projectedStock(available: number, type: Pick<MovementTypeRecord, 'stockFactor'> | undefined, quantity: number | null): number | null {
  if (!type || quantity === null || quantity <= 0) return null;
  return available + (increases(type) ? quantity : -quantity);
}

/** Posición con la que se abre un producto: la principal, la que más tiene o la primera del almacén. */
export function defaultBin(card: Pick<ProductCardData, 'primaryBin' | 'bins'> | undefined, bins: readonly BinRecord[]): string {
  const codes = bins.map((bin) => bin.code);
  if (card?.primaryBin && (codes.length === 0 || codes.includes(card.primaryBin))) return card.primaryBin;
  const fullest = [...(card?.bins ?? [])].sort((a, b) => b.onHand - a.onHand)[0]?.binCode;
  if (fullest && (codes.length === 0 || codes.includes(fullest))) return fullest;
  return codes[0] ?? '';
}

// ---------------------------------------------------------------------------------------------------- series

/** Series escritas por la persona: una por línea (también separadas por coma o punto y coma). */
export function parseSerials(text: string): string[] {
  return text
    .split(/[\n,;]+/)
    .map((serial) => serial.trim())
    .filter((serial) => serial.length > 0);
}

/** «IMEI» o «número de serie» («números de serie» en plural). */
export function serialKindLabel(kind: string, plural = false): string {
  if (kind === 'Imei') return 'IMEI';
  return plural ? 'números de serie' : 'número de serie';
}

/** Tipos que un producto con serie NO registra aquí (tienen su propio documento en el servidor). */
export function serialTypeNotice(typeCode: string): string | null {
  if (typeCode === 'VENTA_POS') return 'Este producto lleva serie: la venta se registra desde la caja.';
  if (typeCode === 'DEVOLUCION_CLIENTE') return 'Este producto lleva serie: la devolución se registra desde la venta.';
  return null;
}

// ---------------------------------------------------------------------------------------------------- formulario

export interface MovementDraft {
  type: MovementTypeRecord | undefined;
  sku: string | null;
  binCode: string;
  quantity: number | null;
  allowsDecimals: boolean;
  document: string;
  notes: string;
  trackSerials: boolean;
  serials: readonly string[];
  /** Disponible en la posición elegida (del lote por defecto). */
  available: number;
}

export interface MovementProblems {
  type?: string;
  product?: string;
  bin?: string;
  quantity?: string;
  notes?: string;
  serials?: string;
}

/**
 * Problemas del formulario (comodidad: el servidor valida igual). El poka-yoke (`quantity` cuando la salida dejaría la
 * posición en negativo) se muestra al instante; el resto, al intentar registrar.
 */
export function movementProblems(draft: MovementDraft): MovementProblems {
  const problems: MovementProblems = {};
  if (!draft.type) problems.type = 'Elija el tipo de movimiento.';
  if (!draft.sku) problems.product = 'Elija el producto.';
  if (!draft.binCode) problems.bin = 'Elija la posición.';
  if (draft.quantity === null || draft.quantity <= 0) problems.quantity = 'Indique una cantidad mayor que 0.';
  else if (!draft.allowsDecimals && !Number.isInteger(draft.quantity)) problems.quantity = 'La unidad de este producto no admite decimales.';
  else if (wouldBeNegative(draft)) problems.quantity = negativeText(draft.available);
  if (draft.type?.requiresNotes && draft.notes.trim().length === 0) problems.notes = 'Escriba la observación: es obligatoria para este tipo.';
  if (draft.trackSerials && draft.quantity !== null && draft.quantity > 0) {
    if (!Number.isInteger(draft.quantity)) problems.serials = 'Este producto lleva serie: la cantidad debe ser entera (una serie por unidad).';
    else {
      const repeated = draft.serials.find((serial, index) => draft.serials.indexOf(serial) !== index);
      if (repeated) problems.serials = `La serie ${repeated} está repetida.`;
      else if (draft.serials.length !== draft.quantity) {
        problems.serials = `Indique ${draft.quantity} ${draft.quantity === 1 ? 'serie' : 'series'} (una por unidad): hay ${draft.serials.length}.`;
      }
    }
  }
  return problems;
}

/** El poka-yoke: la salida dejaría la posición en negativo. */
export function negativeText(available: number): string {
  return `La salida dejaría la posición en negativo: solo hay ${formatQuantity(available)} disponibles. Revise la cantidad o la posición.`;
}

export function wouldBeNegative(draft: Pick<MovementDraft, 'type' | 'quantity' | 'available'>): boolean {
  const projected = projectedStock(draft.available, draft.type, draft.quantity);
  return projected !== null && projected < 0;
}

export function hasProblems(problems: MovementProblems): boolean {
  return Object.values(problems).some((problem) => problem !== undefined);
}

/** Contenido EXACTO de `RegisterMovementCommand` (todos los parámetros; lo opcional vacío va como null). */
export function registerPayload(draft: MovementDraft): RegisterPayload {
  return {
    sku: draft.sku ?? '',
    binCode: draft.binCode,
    movementTypeCode: draft.type?.code ?? '',
    quantity: draft.quantity ?? 0,
    businessDate: null,
    documentReference: draft.document.trim() ? draft.document.trim() : null,
    notes: draft.notes.trim() ? draft.notes.trim() : null,
    lotNumber: null,
    adjustmentReasonCode: null,
    serials: draft.trackSerials ? [...draft.serials] : null,
  };
}

/** Texto del aviso de éxito: «+5 u. · MOU-01 · quedan 12 u. en CM-A-01». */
export function registeredText(type: MovementTypeRecord, quantity: number, unit: string, sku: string, onHand: number, binCode: string): string {
  return `${increases(type) ? '+' : '−'}${formatQuantity(quantity, { unit })} · ${sku} · quedan ${formatQuantity(onHand, { unit })} en ${binCode}`;
}

// ---------------------------------------------------------------------------------------------------- lista

/** Cuántos movimientos se piden al servidor (acepta de 1 a 500; la lista filtra en la página). */
export const TAKE_OPTIONS: readonly { value: string; label: string }[] = [
  { value: '50', label: 'Últimos 50' },
  { value: '100', label: 'Últimos 100' },
  { value: '200', label: 'Últimos 200' },
  { value: '500', label: 'Últimos 500' },
];
export const DEFAULT_TAKE = '200';

export function takeOf(value: string): number {
  return Number(TAKE_OPTIONS.some((option) => option.value === value) ? value : DEFAULT_TAKE);
}

/**
 * Filtros de la lista (en la dirección). `flujo`: 'entradas' o 'salidas' (el nombre `sentido` es de la tabla).
 * `producto`: SKU. Fechas: la fecha del movimiento (días de La Paz).
 */
export const MOVEMENT_FILTERS = { q: '', tipo: '', flujo: '', usuario: '', producto: '', desde: '', hasta: '', registros: DEFAULT_TAKE };
export type MovementFilters = typeof MOVEMENT_FILTERS;

/** Parámetros de la dirección que abren el registro (`?registrar=ENTRADA&sku=MOU-01`). No son filtros. */
export const REGISTER_PARAM = 'registrar';
export const SKU_PARAM = 'sku';

export const FLOW_OPTIONS: readonly { value: string; label: string }[] = [
  { value: 'entradas', label: 'Solo entradas (suman)' },
  { value: 'salidas', label: 'Solo salidas (restan)' },
];

/** Valor del filtro «Usuario» para lo que registró el sistema (sin usuario). */
export const SYSTEM_USER = '_sistema';

export interface MovementEntry {
  key: string;
  record: RecentMovementRecord;
  isIn: boolean;
  typeTitle: string;
  who: string;
  userValue: string;
}

export function toMovementEntries(records: readonly RecentMovementRecord[]): MovementEntry[] {
  const repeated = new Map<string, number>();
  return records.map((record) => {
    const base = `${record.recordedAt}|${record.sku}|${record.typeCode}|${record.binCode}`;
    const count = repeated.get(base) ?? 0;
    repeated.set(base, count + 1);
    return {
      key: count === 0 ? base : `${base}|${count}`,
      record,
      isIn: record.stockFactor > 0,
      typeTitle: typeTitle(record.typeName),
      who: record.userName ?? 'Sistema',
      userValue: record.userName ?? SYSTEM_USER,
    };
  });
}

export function filterMovements(entries: readonly MovementEntry[], filters: MovementFilters): MovementEntry[] {
  const range = { from: filters.desde || null, to: filters.hasta || null };
  return entries.filter(
    (entry) =>
      (!filters.tipo || entry.record.typeCode === filters.tipo) &&
      (filters.flujo !== 'entradas' || entry.isIn) &&
      (filters.flujo !== 'salidas' || !entry.isIn) &&
      (!filters.usuario || entry.userValue === filters.usuario) &&
      (!filters.producto || entry.record.sku === filters.producto) &&
      inRange(entry.record.businessDate, range) &&
      matchesSearch(filters.q, [entry.record.sku, entry.record.name, entry.typeTitle, entry.record.document, entry.record.binCode, entry.who]),
  );
}

export function userOptions(entries: readonly MovementEntry[]): { value: string; label: string }[] {
  const users = new Map<string, string>();
  for (const entry of entries) if (!users.has(entry.userValue)) users.set(entry.userValue, entry.userValue === SYSTEM_USER ? 'Sistema (procesos automáticos)' : entry.who);
  return [...users.entries()].map(([value, label]) => ({ value, label })).sort((a, b) => a.label.localeCompare(b.label, 'es'));
}

/** Productos presentes en los movimientos cargados (para el filtro «Producto»). */
export function productOptions(entries: readonly MovementEntry[]): { value: string; label: string; description: string }[] {
  const products = new Map<string, string>();
  for (const entry of entries) if (!products.has(entry.record.sku)) products.set(entry.record.sku, entry.record.name);
  return [...products.entries()].map(([value, label]) => ({ value, label, description: value })).sort((a, b) => a.label.localeCompare(b.label, 'es'));
}

/** «+5 u.» o «−3 u.» */
export function signedQuantity(entry: Pick<MovementEntry, 'isIn' | 'record'>): string {
  return `${entry.isIn ? '+' : '−'}${formatQuantity(entry.record.quantity, { unit: entry.record.unit })}`;
}

export const MOVEMENT_CSV: readonly CsvColumn<MovementEntry>[] = [
  { header: 'Registrado', value: (entry) => toDate(entry.record.recordedAt) },
  { header: 'Fecha del movimiento', value: (entry) => formatDate(entry.record.businessDate) },
  { header: 'Tipo', value: (entry) => entry.typeTitle },
  { header: 'Entrada o salida', value: (entry) => (entry.isIn ? 'Entrada' : 'Salida') },
  { header: 'SKU', value: (entry) => entry.record.sku },
  { header: 'Producto', value: (entry) => entry.record.name },
  { header: 'Cantidad', value: (entry) => (entry.isIn ? entry.record.quantity : -entry.record.quantity) },
  { header: 'Unidad', value: (entry) => entry.record.unit },
  { header: 'Posición', value: (entry) => entry.record.binCode },
  { header: 'Documento', value: (entry) => entry.record.document },
  { header: 'Usuario', value: (entry) => entry.who },
];

// ---------------------------------------------------------------------------------------------------- enlaces

export function productCardLink(sku: string): string {
  return ROUTES.panelModule(`stock?ficha=${encodeURIComponent(sku)}`);
}

// ---------------------------------------------------------------------------------------------------- tendencia

export const TREND_DAYS: readonly { value: string; label: string }[] = [
  { value: '7', label: 'Últimos 7 días' },
  { value: '14', label: 'Últimos 14 días' },
  { value: '30', label: 'Últimos 30 días' },
  { value: '90', label: 'Últimos 90 días' },
];

export interface TrendSummary {
  entries: number;
  issues: number;
  movements: number;
  opening: number;
  entryPoints: { label: string; value: number }[];
  issuePoints: { label: string; value: number }[];
}

/** Totales y series de la tendencia (el saldo inicial va aparte: es la apertura del inventario, no operación). */
export function trendSummary(days: readonly TrendDayRecord[]): TrendSummary {
  const label = (day: TrendDayRecord) => formatDate(day.date).slice(0, 5);
  return {
    entries: days.reduce((sum, day) => sum + day.entries, 0),
    issues: days.reduce((sum, day) => sum + day.issues, 0),
    movements: days.reduce((sum, day) => sum + day.movements, 0),
    opening: days.reduce((sum, day) => sum + day.opening, 0),
    entryPoints: days.map((day) => ({ label: label(day), value: day.entries })),
    issuePoints: days.map((day) => ({ label: label(day), value: day.issues })),
  };
}
