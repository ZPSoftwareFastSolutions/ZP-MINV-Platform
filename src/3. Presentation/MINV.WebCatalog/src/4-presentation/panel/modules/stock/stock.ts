// Módulo «Stock» · funciones puras (sin React): el semáforo, las filas listas para la tabla (stock + reservado +
// disponible + marca), los filtros de la lista, las opciones de las listas desplegables, los textos (cobertura, último
// movimiento), el kardex, las columnas de los CSV y los resúmenes de las estadísticas. Los textos siguen al escritorio
// (StockViewModel, StockItem, ProductDetailViewModel y Fmt.Coverage).
//
// Los tipos del servidor SALEN DEL CONTRATO (regla P-07): se derivan del nombre de la operación. Nada de lógica de negocio
// (regla P-01): el semáforo, la cobertura, el valor y las alertas los calcula el servidor; aquí solo se muestran, se
// filtran y se formatean.

import type { RpcResponseOf } from '@/4-presentation/app/contract';
import { ROUTES } from '@/4-presentation/app/routes';
import { defineStatuses, statusOf } from '@/4-presentation/panel/kit';
import { formatDate, formatDateTime, formatNumber, formatQuantity, matchesSearch, toDate, type CsvColumn } from '@/4-presentation/panel/lib';

// ---------------------------------------------------------------------------------------------------- tipos del servidor

/** Respuesta de `GetStockProjectionQuery` (stock, alertas y pedido sugerido de un almacén). */
export type StockProjectionData = RpcResponseOf<'GetStockProjectionQuery'>;
/** Una fila de stock tal como la manda el servidor. */
export type StockRecord = StockProjectionData['result']['stock'][number];
/** Una alerta priorizada tal como la manda el servidor. */
export type StockAlertRecord = StockProjectionData['result']['alerts'][number];
/** Reservado por SKU (`GetStockReservationsQuery`). */
export type ReservationRecord = RpcResponseOf<'GetStockReservationsQuery'>[number];
/** Una sucursal del directorio (`GetBranchesQuery`). */
export type BranchRecord = RpcResponseOf<'GetBranchesQuery'>[number];
/** Stock consolidado de todas las sucursales visibles (`ConsolidatedStockQuery`). */
export type ConsolidatedData = RpcResponseOf<'ConsolidatedStockQuery'>;
export type ConsolidatedRecord = ConsolidatedData['rows'][number];
/** Ficha de un producto con su kardex (`GetProductCardQuery`). */
export type ProductCardData = RpcResponseOf<'GetProductCardQuery'>;
export type KardexRecord = ProductCardData['movements'][number];
export type BinStockRecord = ProductCardData['bins'][number];
/** Producto del catálogo técnico (`SearchTechProductsQuery`): de aquí sale la marca. */
export type TechCatalogRecord = RpcResponseOf<'SearchTechProductsQuery'>[number];

// ---------------------------------------------------------------------------------------------------- semáforo

/** Semáforo del stock (enumeración `StockStatusCode` del servidor), en el orden de la lista desplegable. */
export const STOCK_STATUSES = defineStatuses({
  OutOfStock: { label: 'Sin stock', tone: 'danger' },
  Critical: { label: 'Crítico', tone: 'danger' },
  Low: { label: 'Bajo', tone: 'warning' },
  Optimal: { label: 'Normal', tone: 'success' },
  Overstock: { label: 'Exceso', tone: 'info' },
  Inconsistent: { label: 'Inconsistente', tone: 'accent' },
  Inactive: { label: 'Inactivo', tone: 'neutral' },
});

/** Prioridad de cada estado (la de la V2.1: primero lo más urgente). Ordena la columna «Semáforo». */
const STATUS_PRIORITY: Readonly<Record<string, number>> = {
  Inconsistent: 1,
  OutOfStock: 2,
  Critical: 3,
  Low: 4,
  Optimal: 5,
  Overstock: 6,
  Inactive: 7,
};

export function statusLabel(status: string): string {
  return statusOf(STOCK_STATUSES, status).label;
}

/** Posición del estado para ordenar (un estado desconocido va al final). */
export function statusPriority(status: string): number {
  return STATUS_PRIORITY[status] ?? 99;
}

// ---------------------------------------------------------------------------------------------------- filtros

/** Valor del filtro «Sucursal» que muestra el stock consolidado de todas las sucursales visibles. */
export const ALL_BRANCHES = 'todas';
/** Valor del filtro «Marca» para los productos sin marca registrada. */
export const NO_BRAND = '_sin-marca';

/**
 * Filtros de la lista (en la dirección de la página). `sucursal`: '' = el almacén de la sucursal activa, un código de
 * almacén o `todas` (consolidado). `reservas`: 'con' = solo con unidades reservadas. `rotacion`: 'sin' = sin rotación.
 */
export const STOCK_FILTERS = { q: '', sucursal: '', categoria: '', marca: '', estado: '', reservas: '', rotacion: '' };
export type StockFilters = typeof STOCK_FILTERS;

/** Parámetro de la dirección que abre la ficha de un producto (`/panel/stock?ficha=SKU`). No es un filtro. */
export const DETAIL_PARAM = 'ficha';

export const RESERVATION_OPTIONS: readonly { value: string; label: string }[] = [{ value: 'con', label: 'Solo con unidades reservadas' }];
export const ROTATION_OPTIONS: readonly { value: string; label: string }[] = [{ value: 'sin', label: 'Solo sin rotación' }];

/** Pedido de `SearchTechProductsQuery` para conocer la marca de cada producto activo (todos los parámetros, regla §5.2). */
export const BRAND_LOOKUP = { text: null, categoryCode: null, platform: null, filters: null, onlyInStock: false, max: 2000 } as const;

/** Filas del kardex que se piden al abrir la ficha. */
export const KARDEX_TAKE = 300;

// ---------------------------------------------------------------------------------------------------- filas

/** Una fila lista para la tabla: lo del servidor más el reservado, el disponible y la marca. */
export interface StockEntry {
  /** SKU (identificador estable de la fila). */
  key: string;
  row: StockRecord;
  /** Unidades reservadas (armados, carritos y caja) en este almacén. */
  reserved: number;
  /** Existencias − reservado, nunca negativo (lo mismo que muestra el escritorio). */
  available: number;
  brand: string | null;
  /** Sin movimientos hace más días que los de la empresa (regla de la V1: activo, con stock y sin rotación). */
  withoutRotation: boolean;
}

/** Reservado por SKU (sin distinguir mayúsculas). */
export function reservedBySku(records: readonly ReservationRecord[]): Map<string, number> {
  const map = new Map<string, number>();
  for (const record of records) map.set(record.sku.toUpperCase(), (map.get(record.sku.toUpperCase()) ?? 0) + record.reserved);
  return map;
}

/** Marca por SKU (de los productos activos del catálogo técnico). */
export function brandBySku(records: readonly TechCatalogRecord[]): Map<string, string | null> {
  return new Map(records.map((record) => [record.sku.toUpperCase(), record.brand && record.brand.trim() ? record.brand.trim() : null]));
}

/**
 * ¿Sin rotación? Como el indicador «Sin rotación» de la V1 y la V2.1: producto activo, con stock y con su último
 * movimiento hace MÁS días que los configurados en la empresa. Sin la configuración, ninguno.
 */
export function isWithoutRotation(row: StockRecord, daysWithoutRotation: number | null): boolean {
  if (daysWithoutRotation === null || !row.isActive || row.stock <= 0 || row.daysWithoutMovement === null) return false;
  return row.daysWithoutMovement > daysWithoutRotation;
}

export function toStockEntries(
  rows: readonly StockRecord[],
  reserved: ReadonlyMap<string, number>,
  brands: ReadonlyMap<string, string | null>,
  daysWithoutRotation: number | null,
): StockEntry[] {
  return rows.map((row) => {
    const sku = row.sku.toUpperCase();
    const onReserve = reserved.get(sku) ?? 0;
    return {
      key: row.sku,
      row,
      reserved: onReserve,
      available: Math.max(0, row.stock - onReserve),
      brand: brands.get(sku) ?? null,
      withoutRotation: isWithoutRotation(row, daysWithoutRotation),
    };
  });
}

/** Aplica los filtros de la página (la sucursal ya se aplicó en el pedido al servidor). */
export function filterStock(entries: readonly StockEntry[], filters: StockFilters): StockEntry[] {
  return entries.filter(
    (entry) =>
      (!filters.estado || entry.row.status === filters.estado) &&
      (!filters.categoria || entry.row.category === filters.categoria) &&
      matchesBrand(entry.brand, filters.marca) &&
      (filters.reservas !== 'con' || entry.reserved > 0) &&
      (filters.rotacion !== 'sin' || entry.withoutRotation) &&
      matchesSearch(filters.q, [entry.row.sku, entry.row.name, entry.row.category, entry.row.supplier, entry.brand]),
  );
}

function matchesBrand(brand: string | null, filter: string): boolean {
  if (!filter) return true;
  if (filter === NO_BRAND) return brand === null;
  return brand === filter;
}

// ---------------------------------------------------------------------------------------------------- consolidado

/** Una fila del stock consolidado lista para la tabla. */
export interface ConsolidatedEntry {
  key: string;
  row: ConsolidatedRecord;
  brand: string | null;
}

export function toConsolidatedEntries(rows: readonly ConsolidatedRecord[], brands: ReadonlyMap<string, string | null>): ConsolidatedEntry[] {
  return rows.map((row) => ({ key: row.sku, row, brand: brands.get(row.sku.toUpperCase()) ?? null }));
}

/** En el consolidado solo filtran la búsqueda, la categoría y la marca (el semáforo es de cada almacén). */
export function filterConsolidated(entries: readonly ConsolidatedEntry[], filters: StockFilters): ConsolidatedEntry[] {
  return entries.filter(
    (entry) =>
      (!filters.categoria || entry.row.category === filters.categoria) &&
      matchesBrand(entry.brand, filters.marca) &&
      matchesSearch(filters.q, [entry.row.sku, entry.row.name, entry.row.category, entry.brand]),
  );
}

/** La fila del consolidado de un SKU (la búsqueda del servidor es «contiene»: aquí se elige la exacta). */
export function consolidatedRowOf(data: ConsolidatedData | undefined, sku: string): ConsolidatedRecord | null {
  return data?.rows.find((row) => row.sku.toUpperCase() === sku.toUpperCase()) ?? null;
}

// ---------------------------------------------------------------------------------------------------- opciones

export interface Option {
  value: string;
  label: string;
}

function sortedLabels(values: Iterable<string>): string[] {
  return [...new Set(values)].filter((value) => value.trim().length > 0).sort((a, b) => a.localeCompare(b, 'es'));
}

/** Categorías presentes en las filas cargadas. */
export function categoryOptions(categories: Iterable<string>): Option[] {
  return sortedLabels(categories).map((category) => ({ value: category, label: category }));
}

/** Marcas presentes en las filas cargadas (y «Sin marca» si hay productos sin ella). */
export function brandOptions(brands: Iterable<string | null>): Option[] {
  const list = [...brands];
  const options = sortedLabels(list.filter((brand): brand is string => brand !== null)).map((brand) => ({ value: brand, label: brand }));
  return list.some((brand) => brand === null) && options.length > 0 ? [...options, { value: NO_BRAND, label: 'Sin marca' }] : options;
}

/**
 * Opciones del filtro «Sucursal»: un almacén por sucursal visible y activa (con el almacén entre paréntesis si la sucursal
 * tiene varios) y, si la sesión ve más de una sucursal, «Todas las sucursales (consolidado)».
 */
export function warehouseOptions(branches: readonly BranchRecord[]): Option[] {
  const visible = branches.filter((branch) => branch.isVisible && branch.isActive && branch.warehouses.length > 0);
  const options = visible.flatMap((branch) =>
    branch.warehouses.map((warehouse) => ({
      value: warehouse,
      label: branch.warehouses.length > 1 ? `${branch.code} · ${branch.name} (${warehouse})` : `${branch.code} · ${branch.name}`,
    })),
  );
  return visible.length > 1 ? [...options, { value: ALL_BRANCHES, label: 'Todas las sucursales (consolidado)' }] : options;
}

/** Texto de la opción por defecto del filtro «Sucursal» (el almacén de trabajo de la sesión). */
export function activeWarehouseLabel(activeBranch: { code: string; name: string } | null): string {
  return activeBranch ? `Sucursal activa (${activeBranch.code})` : 'Almacén principal de la empresa';
}

// ---------------------------------------------------------------------------------------------------- textos

/** Cobertura en días al ritmo de salidas de 30 días (como el escritorio): «—», «1 día», «12 días», «+999 días». */
export function coverageText(days: number | null): string {
  if (days === null) return '—';
  if (days > 999) return '+999 días';
  return days === 1 ? '1 día' : `${days} días`;
}

/** «Hoy», «Ayer», «Hace 12 días» o «Sin movimientos». */
export function lastMovementText(row: Pick<StockRecord, 'lastMovement' | 'daysWithoutMovement'>): string {
  if (row.lastMovement === null) return 'Sin movimientos';
  const days = row.daysWithoutMovement;
  if (days === null) return formatDate(row.lastMovement);
  if (days <= 0) return 'Hoy';
  return days === 1 ? 'Ayer' : `Hace ${days} días`;
}

/** «5 u.» (cantidad con la unidad). */
export function quantityWithUnit(value: number, unit: string): string {
  return formatQuantity(value, { unit });
}

/** «12 / 40» (mínimo y máximo) o «—» si el producto no tiene política. */
export function minMaxText(minimum: number, maximum: number): string {
  return minimum > 0 || maximum > 0 ? `${formatQuantity(minimum)} / ${formatQuantity(maximum)}` : '—';
}

/** «+5» o «−3» (entradas y salidas del kardex). */
export function signedText(value: number): string {
  if (value > 0) return `+${formatQuantity(value)}`;
  if (value < 0) return `−${formatQuantity(Math.abs(value))}`;
  return formatQuantity(0);
}

/** «Tipo de movimiento» en tipo oración («AJUSTE (-)» → «Ajuste (-)»; conserva siglas como POS). */
export function sentenceCase(text: string): string {
  const words = text.trim().toLocaleLowerCase('es').split(/\s+/).filter(Boolean);
  if (words.length === 0) return '';
  const fixed = words.map((word) => (['pos', 'iva', 'nit', 'rma'].includes(word) ? word.toUpperCase() : word));
  const joined = fixed.join(' ');
  return `${joined[0].toLocaleUpperCase('es')}${joined.slice(1)}`;
}

/** «CM-01 · FAC-123 · Bruno Mamani» (posición, documento y usuario de una línea del kardex). */
export function kardexDetail(line: KardexRecord): string {
  return [line.binCode, line.document, line.userName].filter((part): part is string => typeof part === 'string' && part.trim().length > 0).join(' · ');
}

/** «Últimos 300 de 1.204 movimientos» o «12 movimientos». */
export function kardexCountText(card: Pick<ProductCardData, 'movements' | 'totalMovements'>): string {
  if (card.totalMovements === 0) return 'Sin movimientos';
  if (card.totalMovements === card.movements.length) return card.totalMovements === 1 ? '1 movimiento' : `${formatNumber(card.totalMovements)} movimientos`;
  return `Últimos ${formatNumber(card.movements.length)} de ${formatNumber(card.totalMovements)} movimientos`;
}

/** Posiciones y lotes de la ficha con unidades reservadas. */
export function reservedBins(card: Pick<ProductCardData, 'bins'>): BinStockRecord[] {
  return card.bins.filter((bin) => bin.reserved > 0);
}

/** Una línea del kardex para la tabla (con una clave estable: su posición en la respuesta del servidor). */
export interface KardexEntry {
  key: string;
  line: KardexRecord;
}

export function toKardexEntries(lines: readonly KardexRecord[]): KardexEntry[] {
  return lines.map((line, index) => ({ key: String(index), line }));
}

// ---------------------------------------------------------------------------------------------------- enlaces

/**
 * Enlace a «Movimientos» con el diálogo de registro abierto: `tipo` es un código de tipo de movimiento (`ENTRADA`,
 * `SALIDA`…), `ajuste` o `1` (el primero permitido); `sku`, el producto ya elegido.
 */
export function movementLink(type: string, sku?: string): string {
  const product = sku ? `&sku=${encodeURIComponent(sku)}` : '';
  return ROUTES.panelModule(`movimientos?registrar=${encodeURIComponent(type)}${product}`);
}

// ---------------------------------------------------------------------------------------------------- CSV

/** Columnas del CSV del stock (como el escritorio, más la marca). */
export const STOCK_CSV: readonly CsvColumn<StockEntry>[] = [
  { header: 'SKU', value: (entry) => entry.row.sku },
  { header: 'Producto', value: (entry) => entry.row.name },
  { header: 'Categoría', value: (entry) => entry.row.category },
  { header: 'Marca', value: (entry) => entry.brand },
  { header: 'Proveedor', value: (entry) => entry.row.supplier },
  { header: 'Unidad', value: (entry) => entry.row.unit },
  { header: 'Existencias', value: (entry) => entry.row.stock },
  { header: 'Reservado', value: (entry) => entry.reserved },
  { header: 'Disponible', value: (entry) => entry.available },
  { header: 'Mínimo', value: (entry) => entry.row.minimum },
  { header: 'Máximo', value: (entry) => entry.row.maximum },
  { header: 'Semáforo', value: (entry) => statusLabel(entry.row.status) },
  { header: 'Salidas 30 días', value: (entry) => entry.row.sales30Days },
  { header: 'Cobertura (días)', value: (entry) => entry.row.coverageDays },
  { header: 'Costo unitario', value: (entry) => entry.row.unitCost },
  { header: 'Valor', value: (entry) => entry.row.inventoryValue },
  { header: 'Último movimiento', value: (entry) => (entry.row.lastMovement ? formatDate(entry.row.lastMovement) : null) },
  { header: 'Sin rotación', value: (entry) => entry.withoutRotation },
];

/** Columnas del CSV del consolidado: una por sucursal, en tránsito, total y valor. */
export function consolidatedCsv(branches: ConsolidatedData['branches']): CsvColumn<ConsolidatedEntry>[] {
  return [
    { header: 'SKU', value: (entry) => entry.row.sku },
    { header: 'Producto', value: (entry) => entry.row.name },
    { header: 'Categoría', value: (entry) => entry.row.category },
    { header: 'Marca', value: (entry) => entry.brand },
    { header: 'Unidad', value: (entry) => entry.row.unit },
    ...branches.map((branch, index) => ({ header: branch.code, value: (entry: ConsolidatedEntry) => entry.row.byBranch[index] ?? 0 })),
    { header: 'En tránsito', value: (entry) => entry.row.inTransit },
    { header: 'Total', value: (entry) => entry.row.total },
    { header: 'Valor', value: (entry) => entry.row.value },
  ];
}

/** Columnas del CSV del kardex de un producto. */
export const KARDEX_CSV: readonly CsvColumn<KardexRecord>[] = [
  { header: 'Fecha', value: (line) => formatDate(line.businessDate) },
  { header: 'Registrado', value: (line) => toDate(line.recordedAt) },
  { header: 'Movimiento', value: (line) => sentenceCase(line.typeName) },
  { header: 'Cantidad', value: (line) => line.signed },
  { header: 'Saldo', value: (line) => line.balance },
  { header: 'Posición', value: (line) => line.binCode },
  { header: 'Documento', value: (line) => line.document },
  { header: 'Usuario', value: (line) => line.userName },
  { header: 'Observaciones', value: (line) => line.notes },
];

/** «28/09/2026 21:30» del momento en que se registró una línea del kardex. */
export function recordedText(line: KardexRecord): string {
  return formatDateTime(line.recordedAt);
}

// ---------------------------------------------------------------------------------------------------- resúmenes

export interface CountByLabel {
  label: string;
  value: number;
}

export interface InventorySummary {
  /** Valor del inventario (Σ valor de cada fila, calculado por el servidor). */
  value: number;
  products: number;
  active: number;
  /** Productos activos con existencias. */
  withStock: number;
  /** Valor por categoría, de mayor a menor. */
  byCategory: CountByLabel[];
}

export function inventorySummary(rows: readonly StockRecord[]): InventorySummary {
  const categories = new Map<string, number>();
  for (const row of rows) categories.set(row.category, (categories.get(row.category) ?? 0) + row.inventoryValue);
  return {
    value: rows.reduce((sum, row) => sum + row.inventoryValue, 0),
    products: rows.length,
    active: rows.filter((row) => row.isActive).length,
    withStock: rows.filter((row) => row.isActive && row.stock > 0).length,
    byCategory: [...categories.entries()]
      .map(([label, value]) => ({ label, value }))
      .filter((item) => item.value > 0)
      .sort((a, b) => b.value - a.value || a.label.localeCompare(b.label, 'es')),
  };
}

export interface AlertCounts {
  total: number;
  outOfStock: number;
  critical: number;
  low: number;
  /** Exceso e inconsistentes (revisar y ajustar). */
  other: number;
  byStatus: CountByLabel[];
}

/** Cuántas alertas hay de cada tipo (las alertas y su orden los decide el servidor). */
export function alertCounts(alerts: readonly StockAlertRecord[]): AlertCounts {
  const count = (status: string) => alerts.filter((alert) => alert.status === status).length;
  const outOfStock = count('OutOfStock');
  const critical = count('Critical');
  const low = count('Low');
  const byStatus = (['Inconsistent', 'OutOfStock', 'Critical', 'Low', 'Overstock'] as const)
    .map((status) => ({ label: statusLabel(status), value: count(status) }))
    .filter((item) => item.value > 0);
  return { total: alerts.length, outOfStock, critical, low, other: alerts.length - outOfStock - critical - low, byStatus };
}
