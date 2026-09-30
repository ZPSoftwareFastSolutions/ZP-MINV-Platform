// Módulo «Reportes» · funciones PURAS (sin React): qué reportes hay, cómo se agrupan las filas que manda el servidor, los
// filtros de cada reporte (en la dirección), las columnas de la tabla, del CSV y de la hoja impresa, y los enlaces a otros
// módulos. Los tipos del servidor SALEN DEL CONTRATO (regla P-07); los números los calcula el servidor (regla P-01): la
// página solo ordena, filtra, suma lo que muestra (pie de la tabla) y da formato.

import type { RpcResponseOf } from '@/4-presentation/app/contract';
import { ROUTES } from '@/4-presentation/app/routes';
import { formatDate, formatDateTime, formatMoney, formatNumber, formatPercent, formatQuantity, matchesSearch, roundTo } from '@/4-presentation/panel/lib';
import type { ReportColumn } from './columns';
import { PERIOD_FILTERS } from './period';

// ---------------------------------------------------------------------------------------------------- tipos del servidor

export type SalesData = RpcResponseOf<'GetSalesReportQuery'>;
export type PurchasesData = RpcResponseOf<'GetPurchasesReportQuery'>;
export type MovementRecord = RpcResponseOf<'GetMovementsReportQuery'>[number];
export type BranchData = RpcResponseOf<'GetBranchReportQuery'>;
export type BranchRecord = BranchData['branches'][number];
export type TechData = RpcResponseOf<'GetTechDashboardQuery'>;
export type StockRecord = RpcResponseOf<'GetStockProjectionQuery'>['result']['stock'][number];
type GroupRecord = SalesData['byProduct'][number];
type DayRecord = SalesData['byDay'][number];

// ---------------------------------------------------------------------------------------------------- reportes

export type ReportId = 'ventas' | 'compras' | 'movimientos' | 'inventario' | 'sucursales' | 'tecnologia';

export interface ReportTab {
  id: ReportId;
  label: string;
}

/** Parámetro de la dirección con el reporte elegido (`?reporte=compras`). */
export const REPORT_PARAM = 'reporte';
export const DEFAULT_REPORT: ReportId = 'ventas';

/** Reportes que se ofrecen: inventario solo con permiso para ver el stock; sucursales solo si se ven varias. */
export function reportTabs(options: { inventory: boolean; branches: boolean }): ReportTab[] {
  const tabs: (ReportTab | null)[] = [
    { id: 'ventas', label: 'Ventas' },
    { id: 'compras', label: 'Compras' },
    { id: 'movimientos', label: 'Movimientos' },
    options.inventory ? { id: 'inventario', label: 'Inventario' } : null,
    options.branches ? { id: 'sucursales', label: 'Sucursales' } : null,
    { id: 'tecnologia', label: 'Tecnología' },
  ];
  return tabs.filter((tab): tab is ReportTab => tab !== null);
}

/** El reporte de la dirección (uno desconocido o no permitido vuelve a «Ventas»). */
export function reportOf(value: string | null, tabs: readonly ReportTab[]): ReportId {
  return tabs.find((tab) => tab.id === value)?.id ?? DEFAULT_REPORT;
}

/** Parámetros que se conservan al cambiar de reporte (el período); el resto (filtros, orden, página) es de cada uno. */
export const SHARED_PARAMS: readonly string[] = [REPORT_PARAM, ...Object.keys(PERIOD_FILTERS)];

/** Alcance del reporte según la sesión: la sucursal activa o la vista consolidada de la gerencia. */
export function scopeText(access: { allBranches: boolean; activeBranchId: string | null; branches: readonly { id: string; code: string; name: string }[] } | null | undefined): string {
  if (!access) return 'Sin sucursal';
  const active = access.branches.find((branch) => branch.id === access.activeBranchId);
  if (active) return `Sucursal ${active.code} · ${active.name}`;
  return access.allBranches ? 'Todas las sucursales (vista consolidada)' : 'Las sucursales asignadas';
}

// ---------------------------------------------------------------------------------------------------- filas agrupadas

/** Una fila de un reporte agrupado (por producto, categoría, proveedor, día…), lista para mostrar. */
export interface RankedRow {
  key: string;
  rank: number;
  /** Código (SKU, código de categoría o de proveedor, correo del cajero, fecha). */
  code: string;
  name: string;
  quantity: number | null;
  amount: number;
  profit: number | null;
  count: number | null;
  /** Parte del total (0 a 1). */
  share: number;
  /** Fecha ISO de las filas por día. */
  day: string | null;
}

function shareOf(amount: number, total: number): number {
  return total > 0 ? amount / total : 0;
}

/** Filas de grupos ordenadas por monto (de mayor a menor), con su puesto y su parte del total. */
function ranked(groups: readonly GroupRecord[], total: number, options: { quantity: boolean; profit: boolean }): RankedRow[] {
  return [...groups]
    .sort((a, b) => b.amount - a.amount)
    .map((group, index) => ({
      key: group.key || group.name,
      rank: index + 1,
      code: group.key,
      name: group.name,
      quantity: options.quantity ? group.quantity : null,
      amount: group.amount,
      profit: options.profit ? group.profit : null,
      count: group.count,
      share: shareOf(group.amount, total),
      day: null,
    }));
}

/** Filas por día (en orden de fecha). */
function dayRows(days: readonly DayRecord[], total: number, withCount: boolean): RankedRow[] {
  return days.map((day, index) => ({
    key: day.date,
    rank: index + 1,
    code: day.date,
    name: formatDate(day.date),
    quantity: null,
    amount: day.amount,
    profit: null,
    count: withCount ? day.count : null,
    share: shareOf(day.amount, total),
    day: day.date,
  }));
}

// ---------------------------------------------------------------------------------------------------- ventas

export type SalesGrouping = 'producto' | 'categoria' | 'cliente' | 'cajero' | 'pago' | 'dia';

export const SALES_GROUPINGS: readonly { value: SalesGrouping; label: string }[] = [
  { value: 'producto', label: 'Por producto' },
  { value: 'categoria', label: 'Por categoría' },
  { value: 'cliente', label: 'Por cliente (los 15 que más compraron)' },
  { value: 'cajero', label: 'Por cajero' },
  { value: 'pago', label: 'Por medio de pago' },
  { value: 'dia', label: 'Por día' },
];

export const SALES_FILTERS = { ...PERIOD_FILTERS, agrupar: 'producto', q: '' };
export type SalesFilters = typeof SALES_FILTERS;

export function salesGroupingOf(value: string): SalesGrouping {
  return SALES_GROUPINGS.find((option) => option.value === value)?.value ?? 'producto';
}

/** Nombre de la agrupación para títulos y archivos («Ventas por producto»). */
export function salesGroupingName(grouping: SalesGrouping): string {
  return SALES_GROUPINGS.find((option) => option.value === grouping)?.label.replace(/ \(.*\)$/, '').toLowerCase() ?? 'por producto';
}

/** Filas del reporte de ventas según la agrupación. Cajero y medio de pago suman los PAGOS (sin cantidad ni utilidad). */
export function salesRows(report: SalesData, grouping: SalesGrouping): RankedRow[] {
  switch (grouping) {
    case 'producto':
      return ranked(report.byProduct, report.revenue, { quantity: true, profit: true });
    case 'categoria':
      return ranked(report.byCategory, report.revenue, { quantity: true, profit: true });
    case 'cliente':
      return ranked(report.byCustomer, report.revenue, { quantity: true, profit: true });
    case 'cajero':
      return ranked(report.byCashier, report.byCashier.reduce((sum, group) => sum + group.amount, 0), { quantity: false, profit: false });
    case 'pago':
      return ranked(report.byPaymentMethod, report.byPaymentMethod.reduce((sum, group) => sum + group.amount, 0), { quantity: false, profit: false });
    case 'dia':
      return dayRows(report.byDay, report.revenue, true);
  }
}

/** Filtra por la búsqueda (nombre o código). */
export function searchRows(rows: readonly RankedRow[], query: string): RankedRow[] {
  return rows.filter((row) => matchesSearch(query, [row.name, row.code]));
}

/** Totales y rentabilidad del período (lo que manda el servidor, con formato). */
export function salesSummary(report: SalesData): { label: string; value: string }[] {
  return [
    { label: 'Ingresos (con IVA)', value: formatMoney(report.revenue) },
    { label: 'IVA', value: formatMoney(report.tax) },
    { label: 'Ingresos sin IVA', value: formatMoney(report.netRevenue) },
    { label: 'Costo de lo vendido', value: formatMoney(report.cost) },
    { label: 'Utilidad bruta', value: formatMoney(report.grossProfit) },
    { label: 'Margen', value: formatPercent(report.marginPercent, 1) },
    { label: 'Ventas', value: formatNumber(report.tickets) },
    { label: 'Ticket promedio', value: formatMoney(report.averageTicket) },
    { label: 'Unidades vendidas', value: formatQuantity(report.units) },
    { label: 'Ventas anuladas', value: formatNumber(report.voided) },
  ];
}

// ---------------------------------------------------------------------------------------------------- columnas agrupadas

function sum(rows: readonly RankedRow[], pick: (row: RankedRow) => number | null): number | null {
  let total = 0;
  let any = false;
  for (const row of rows) {
    const value = pick(row);
    if (value === null) continue;
    total += value;
    any = true;
  }
  return any ? roundTo(total, 2) : null;
}

const moneyOrDash = (value: number | null) => (value === null ? '—' : formatMoney(value));
const quantityOrDash = (value: number | null) => (value === null ? '—' : formatQuantity(value));
const countOrDash = (value: number | null) => (value === null ? '—' : formatNumber(value));

export interface GroupedColumnsOptions {
  /** Encabezado de la columna del nombre («Producto», «Proveedor», «Día»). */
  nameHeader: string;
  /** Encabezado del monto («Ventas», «Compras»). */
  amountHeader: string;
  /** Encabezado de la cantidad de operaciones («Operaciones», «Recepciones»). */
  countHeader: string;
  showQuantity: boolean;
  showProfit: boolean;
  showCount: boolean;
  /** ¿Se puede sumar la columna de operaciones? (un ticket con varios productos se contaría varias veces). */
  countAdds: boolean;
  /** ¿Se muestra el código debajo del nombre? */
  showCode: boolean;
}

/** Columnas de un reporte agrupado. */
export function groupedColumns(options: GroupedColumnsOptions): ReportColumn<RankedRow>[] {
  const columns: ReportColumn<RankedRow>[] = [
    { id: 'puesto', header: '#', value: (row) => row.rank, text: (row) => String(row.rank), align: 'end', className: 'w-12', card: 'hidden', csv: false },
    {
      id: 'nombre',
      header: options.nameHeader,
      value: (row) => (row.day ? row.day : row.name),
      text: (row) => row.name,
      secondary: options.showCode ? (row) => (row.code && row.code !== row.name ? row.code : null) : undefined,
      card: 'title',
      className: 'min-w-48',
    },
  ];
  if (options.showCode) columns.push({ id: 'codigo', header: 'Código', value: (row) => row.code, text: (row) => row.code || '—', table: false });
  if (options.showQuantity)
    columns.push({ id: 'cantidad', header: 'Cantidad', value: (row) => row.quantity, text: (row) => quantityOrDash(row.quantity), align: 'end', footer: (rows) => quantityOrDash(sum(rows, (row) => row.quantity)) });
  columns.push({ id: 'monto', header: options.amountHeader, value: (row) => row.amount, text: (row) => formatMoney(row.amount), align: 'end', footer: (rows) => moneyOrDash(sum(rows, (row) => row.amount)) });
  if (options.showProfit)
    columns.push({ id: 'utilidad', header: 'Utilidad (con IVA)', value: (row) => row.profit, text: (row) => moneyOrDash(row.profit), align: 'end', footer: (rows) => moneyOrDash(sum(rows, (row) => row.profit)) });
  if (options.showCount)
    columns.push({
      id: 'operaciones',
      header: options.countHeader,
      value: (row) => row.count,
      text: (row) => countOrDash(row.count),
      align: 'end',
      footer: options.countAdds ? (rows) => countOrDash(sum(rows, (row) => row.count)) : undefined,
    });
  columns.push({
    id: 'participacion',
    header: 'Participación',
    value: (row) => roundTo(row.share * 100, 1),
    text: (row) => formatPercent(row.share * 100, 1),
    align: 'end',
  });
  return columns;
}

/** Columnas del reporte de ventas según la agrupación. */
export function salesColumns(grouping: SalesGrouping): ReportColumn<RankedRow>[] {
  const names: Record<SalesGrouping, string> = { producto: 'Producto', categoria: 'Categoría', cliente: 'Cliente', cajero: 'Cajero', pago: 'Medio de pago', dia: 'Día' };
  const withGoods = grouping === 'producto' || grouping === 'categoria' || grouping === 'cliente';
  return groupedColumns({
    nameHeader: names[grouping],
    amountHeader: grouping === 'cajero' || grouping === 'pago' ? 'Cobrado' : grouping === 'dia' ? 'Vendido' : 'Ventas',
    countHeader: grouping === 'dia' ? 'Ventas' : grouping === 'cajero' || grouping === 'pago' ? 'Pagos' : 'Operaciones',
    showQuantity: withGoods,
    showProfit: withGoods,
    showCount: true,
    countAdds: grouping === 'dia' || grouping === 'cliente' || grouping === 'cajero' || grouping === 'pago',
    showCode: grouping === 'producto' || grouping === 'categoria' || grouping === 'cliente',
  });
}

/** Enlaces a otros módulos desde una fila del reporte de ventas (solo direcciones: un módulo no importa a otro). */
export function salesRowLinks(row: RankedRow, grouping: SalesGrouping, period: { from: string; to: string }): { label: string; to: string }[] {
  const range = `desde=${period.from}&hasta=${period.to}`;
  const code = encodeURIComponent(row.code);
  switch (grouping) {
    case 'producto':
      return [
        { label: 'Ver ficha y kardex del producto', to: ROUTES.panelModule(`stock?ficha=${code}`) },
        { label: 'Ver los movimientos del producto', to: ROUTES.panelModule(`movimientos?producto=${code}&${range}`) },
      ];
    case 'categoria':
      return [{ label: 'Ver el stock de la categoría', to: ROUTES.panelModule(`stock?categoria=${encodeURIComponent(row.name)}`) }];
    case 'cliente':
      return [{ label: 'Ver las ventas del cliente', to: ROUTES.panelModule(`ventas?cliente=${code}&${range}`) }];
    case 'cajero':
      return [{ label: 'Ver las ventas del cajero', to: ROUTES.panelModule(`ventas?cajero=${encodeURIComponent(row.name)}&${range}`) }];
    case 'pago':
      return [{ label: 'Ver las ventas con este medio de pago', to: ROUTES.panelModule(`ventas?pago=${encodeURIComponent(row.name)}&${range}`) }];
    case 'dia':
      return row.day ? [{ label: 'Ver las ventas de ese día', to: ROUTES.panelModule(`ventas?desde=${row.day}&hasta=${row.day}`) }] : [];
  }
}

// ---------------------------------------------------------------------------------------------------- compras

export type PurchasesGrouping = 'proveedor' | 'dia';

export const PURCHASES_GROUPINGS: readonly { value: PurchasesGrouping; label: string }[] = [
  { value: 'proveedor', label: 'Por proveedor' },
  { value: 'dia', label: 'Por día' },
];

export const PURCHASES_FILTERS = { ...PERIOD_FILTERS, agrupar: 'proveedor', q: '' };
export type PurchasesFilters = typeof PURCHASES_FILTERS;

export function purchasesGroupingOf(value: string): PurchasesGrouping {
  return value === 'dia' ? 'dia' : 'proveedor';
}

export function purchasesRows(report: PurchasesData, grouping: PurchasesGrouping): RankedRow[] {
  if (grouping === 'dia') return dayRows(report.byDay, report.received, false);
  return ranked(report.bySupplier, report.received, { quantity: false, profit: false });
}

export function purchasesColumns(grouping: PurchasesGrouping): ReportColumn<RankedRow>[] {
  return groupedColumns({
    nameHeader: grouping === 'dia' ? 'Día' : 'Proveedor',
    amountHeader: 'Compras recibidas',
    countHeader: 'Recepciones',
    showQuantity: false,
    showProfit: false,
    showCount: grouping === 'proveedor',
    countAdds: true,
    showCode: grouping === 'proveedor',
  });
}

export function purchasesSummary(report: PurchasesData): { label: string; value: string }[] {
  return [
    { label: 'Compras recibidas', value: formatMoney(report.received) },
    { label: 'Recepciones', value: formatNumber(report.receipts) },
    { label: 'Promedio por recepción', value: report.receipts > 0 ? formatMoney(roundTo(report.received / report.receipts, 2)) : '—' },
    { label: 'Proveedores', value: formatNumber(report.bySupplier.length) },
    { label: 'Órdenes abiertas (hoy)', value: formatNumber(report.openOrders) },
    { label: 'Por recibir (órdenes abiertas)', value: formatMoney(report.openAmount) },
  ];
}

export function purchasesRowLinks(row: RankedRow, grouping: PurchasesGrouping): { label: string; to: string }[] {
  if (grouping === 'dia') return [];
  const code = encodeURIComponent(row.code);
  return [
    { label: 'Ver las órdenes de compra del proveedor', to: ROUTES.panelModule(`ordenes-compra?q=${code}`) },
    { label: 'Ver la ficha del proveedor', to: ROUTES.panelModule(`proveedores?q=${code}`) },
  ];
}

// ---------------------------------------------------------------------------------------------------- movimientos

export const MOVEMENTS_FILTERS = { ...PERIOD_FILTERS, tipo: '', flujo: '', usuario: '', q: '' };
export type MovementsFilters = typeof MOVEMENTS_FILTERS;

export const FLOW_OPTIONS: readonly { value: string; label: string }[] = [
  { value: 'entradas', label: 'Solo entradas' },
  { value: 'salidas', label: 'Solo salidas' },
];

/** El servidor devuelve como máximo esta cantidad de movimientos por pedido. */
export const MOVEMENTS_LIMIT = 20_000;

/** Nombre de quien no tiene usuario (procesos del sistema). */
export const SYSTEM_USER = 'Sistema';

/** Un movimiento listo para mostrar. */
export interface MovementEntry {
  key: string;
  record: MovementRecord;
  isIn: boolean;
  who: string;
}

export function toMovementEntries(records: readonly MovementRecord[]): MovementEntry[] {
  return records.map((record, index) => ({ key: `${record.recordedAt}|${record.sku}|${record.typeCode}|${index}`, record, isIn: record.signed > 0, who: record.user ?? SYSTEM_USER }));
}

export function filterMovements(entries: readonly MovementEntry[], filters: MovementsFilters): MovementEntry[] {
  return entries.filter(
    (entry) =>
      (!filters.flujo || (filters.flujo === 'entradas' ? entry.isIn : !entry.isIn)) &&
      (!filters.usuario || entry.who === filters.usuario) &&
      matchesSearch(filters.q, [entry.record.sku, entry.record.name, entry.record.typeName, entry.record.document, entry.record.binCode, entry.record.notes, entry.who]),
  );
}

/** Usuarios que aparecen en los movimientos (para la lista desplegable). */
export function movementUserOptions(entries: readonly MovementEntry[]): { value: string; label: string }[] {
  return [...new Set(entries.map((entry) => entry.who))].sort((a, b) => a.localeCompare(b, 'es')).map((who) => ({ value: who, label: who }));
}

/** Tipos de movimiento: los del catálogo del servidor o, si no se pudieron leer, los que aparecen en el reporte. */
export function movementTypeOptions(types: readonly { code: string; name: string }[] | undefined, entries: readonly MovementEntry[]): { value: string; label: string }[] {
  const source = types && types.length > 0 ? types : [...new Map(entries.map((entry) => [entry.record.typeCode, { code: entry.record.typeCode, name: entry.record.typeName }])).values()];
  return source.map((type) => ({ value: type.code, label: type.name })).sort((a, b) => a.label.localeCompare(b.label, 'es'));
}

/** «+12 u.» · «-3 u.». */
export function signedText(record: MovementRecord): string {
  const text = formatQuantity(Math.abs(record.signed), { unit: record.unit });
  return `${record.signed > 0 ? '+' : '-'}${text}`;
}

export function movementsSummary(entries: readonly MovementEntry[]): { label: string; value: string }[] {
  const ins = entries.filter((entry) => entry.isIn);
  const outs = entries.filter((entry) => !entry.isIn);
  const units = (list: readonly MovementEntry[]) => formatQuantity(roundTo(list.reduce((total, entry) => total + Math.abs(entry.record.signed), 0), 6));
  return [
    { label: 'Movimientos', value: formatNumber(entries.length) },
    { label: 'Entradas', value: `${formatNumber(ins.length)} (${units(ins)} unidades)` },
    { label: 'Salidas', value: `${formatNumber(outs.length)} (${units(outs)} unidades)` },
    { label: 'Productos', value: formatNumber(new Set(entries.map((entry) => entry.record.sku)).size) },
    { label: 'Usuarios', value: formatNumber(new Set(entries.map((entry) => entry.who)).size) },
  ];
}

export const MOVEMENT_COLUMNS: readonly ReportColumn<MovementEntry>[] = [
  { id: 'fecha', header: 'Registrado', value: (entry) => new Date(entry.record.recordedAt), text: (entry) => formatDateTime(entry.record.recordedAt), className: 'whitespace-nowrap' },
  { id: 'producto', header: 'Producto', value: (entry) => entry.record.name, text: (entry) => entry.record.name, secondary: (entry) => entry.record.sku, card: 'title', className: 'min-w-48' },
  { id: 'sku', header: 'SKU', value: (entry) => entry.record.sku, text: (entry) => entry.record.sku, table: false },
  { id: 'tipo', header: 'Tipo', value: (entry) => entry.record.typeName, text: (entry) => entry.record.typeName },
  {
    id: 'cantidad',
    header: 'Cantidad',
    value: (entry) => entry.record.signed,
    text: (entry) => signedText(entry.record),
    tone: (entry) => (entry.isIn ? 'success' : 'warning'),
    align: 'end',
  },
  { id: 'unidad', header: 'Unidad', value: (entry) => entry.record.unit, text: (entry) => entry.record.unit, table: false },
  { id: 'posicion', header: 'Posición', value: (entry) => entry.record.binCode, text: (entry) => entry.record.binCode },
  { id: 'usuario', header: 'Usuario', value: (entry) => entry.who, text: (entry) => entry.who },
  { id: 'documento', header: 'Documento', value: (entry) => entry.record.document, text: (entry) => entry.record.document ?? '—' },
  { id: 'notas', header: 'Observaciones', value: (entry) => entry.record.notes, text: (entry) => entry.record.notes ?? '—', table: false },
];

// ---------------------------------------------------------------------------------------------------- inventario

export const INVENTORY_FILTERS = { sucursal: '', q: '' };
export type InventoryFilters = typeof INVENTORY_FILTERS;

/** Estados de stock que cuentan como alerta de reposición. */
const ALERT_STATUSES: readonly string[] = ['OutOfStock', 'Critical', 'Low'];

/** Una categoría del reporte de inventario. */
export interface CategoryRow {
  key: string;
  rank: number;
  name: string;
  products: number;
  units: number;
  value: number;
  share: number;
  alerts: number;
  idle: number;
  /** Los productos de la categoría (para el detalle). */
  items: readonly StockRecord[];
}

/** ¿Sin movimiento hace más de 30 días (o nunca)? */
export function isIdle(row: StockRecord): boolean {
  return row.daysWithoutMovement === null || row.daysWithoutMovement > 30;
}

/** Valor del inventario por categoría (solo productos activos, como el escritorio), de mayor a menor. */
export function categoryRows(stock: readonly StockRecord[]): CategoryRow[] {
  const active = stock.filter((row) => row.isActive);
  const total = active.reduce((sum, row) => sum + row.inventoryValue, 0);
  const groups = new Map<string, StockRecord[]>();
  for (const row of active) groups.set(row.category, [...(groups.get(row.category) ?? []), row]);
  return [...groups.entries()]
    .map(([name, items]) => {
      const value = roundTo(items.reduce((sum, row) => sum + row.inventoryValue, 0), 2);
      return {
        key: name,
        rank: 0,
        name,
        products: items.length,
        units: roundTo(items.reduce((sum, row) => sum + row.stock, 0), 6),
        value,
        share: total > 0 ? value / total : 0,
        alerts: items.filter((row) => ALERT_STATUSES.includes(row.status)).length,
        idle: items.filter(isIdle).length,
        items: [...items].sort((a, b) => b.inventoryValue - a.inventoryValue),
      };
    })
    .sort((a, b) => b.value - a.value)
    .map((row, index) => ({ ...row, rank: index + 1 }));
}

export function inventorySummary(stock: readonly StockRecord[], alerts: number): { label: string; value: string }[] {
  const active = stock.filter((row) => row.isActive);
  return [
    { label: 'Valor del inventario', value: formatMoney(roundTo(active.reduce((sum, row) => sum + row.inventoryValue, 0), 2)) },
    { label: 'Productos activos', value: formatNumber(active.length) },
    { label: 'Productos en alerta', value: formatNumber(alerts) },
    { label: 'Agotados', value: formatNumber(active.filter((row) => row.status === 'OutOfStock').length) },
    { label: 'Críticos', value: formatNumber(active.filter((row) => row.status === 'Critical').length) },
    { label: 'Sin movimiento (más de 30 días)', value: formatNumber(active.filter(isIdle).length) },
  ];
}

export const CATEGORY_COLUMNS: readonly ReportColumn<CategoryRow>[] = [
  { id: 'puesto', header: '#', value: (row) => row.rank, text: (row) => String(row.rank), align: 'end', className: 'w-12', card: 'hidden', csv: false },
  { id: 'categoria', header: 'Categoría', value: (row) => row.name, text: (row) => row.name, card: 'title', className: 'min-w-44' },
  { id: 'productos', header: 'Productos', value: (row) => row.products, text: (row) => formatNumber(row.products), align: 'end', footer: (rows) => formatNumber(rows.reduce((sum, row) => sum + row.products, 0)) },
  { id: 'unidades', header: 'Unidades', value: (row) => row.units, text: (row) => formatQuantity(row.units), align: 'end', footer: (rows) => formatQuantity(roundTo(rows.reduce((sum, row) => sum + row.units, 0), 6)) },
  { id: 'valor', header: 'Valor', value: (row) => row.value, text: (row) => formatMoney(row.value), align: 'end', footer: (rows) => formatMoney(roundTo(rows.reduce((sum, row) => sum + row.value, 0), 2)) },
  { id: 'participacion', header: 'Participación', value: (row) => roundTo(row.share * 100, 1), text: (row) => formatPercent(row.share * 100, 1), align: 'end' },
  { id: 'alertas', header: 'En alerta', value: (row) => row.alerts, text: (row) => formatNumber(row.alerts), align: 'end', footer: (rows) => formatNumber(rows.reduce((sum, row) => sum + row.alerts, 0)) },
  { id: 'quietos', header: 'Sin movimiento', value: (row) => row.idle, text: (row) => formatNumber(row.idle), align: 'end', footer: (rows) => formatNumber(rows.reduce((sum, row) => sum + row.idle, 0)) },
];

/** Almacenes de las sucursales visibles (para el filtro «Sucursal» del inventario). */
export function warehouseOptions(branches: readonly { code: string; name: string; isActive: boolean; isVisible: boolean; warehouses: readonly string[] }[]): { value: string; label: string }[] {
  return branches
    .filter((branch) => branch.isVisible && branch.isActive)
    .flatMap((branch) => branch.warehouses.map((warehouse) => ({ value: warehouse, label: branch.warehouses.length > 1 ? `${branch.code} · ${branch.name} (${warehouse})` : `${branch.code} · ${branch.name}` })));
}

// ---------------------------------------------------------------------------------------------------- sucursales

export type BranchGrouping = 'sucursal' | 'dia';

export const BRANCH_FILTERS = { ...PERIOD_FILTERS, agrupar: 'sucursal', sucursal: '', q: '' };
export type BranchFilters = typeof BRANCH_FILTERS;

export const BRANCH_GROUPINGS: readonly { value: BranchGrouping; label: string }[] = [
  { value: 'sucursal', label: 'Por sucursal' },
  { value: 'dia', label: 'Por día (ingresos de cada sucursal)' },
];

export function branchGroupingOf(value: string): BranchGrouping {
  return value === 'dia' ? 'dia' : 'sucursal';
}

export function filterBranches(rows: readonly BranchRecord[], filters: Pick<BranchFilters, 'sucursal' | 'q'>): BranchRecord[] {
  return rows.filter((row) => (!filters.sucursal || row.code === filters.sucursal) && matchesSearch(filters.q, [row.code, row.name]));
}

export const BRANCH_COLUMNS: readonly ReportColumn<BranchRecord>[] = [
  { id: 'sucursal', header: 'Sucursal', value: (row) => row.code, text: (row) => `${row.code} · ${row.name}`, card: 'title', className: 'min-w-44' },
  { id: 'ventas', header: 'Ventas', value: (row) => row.tickets, text: (row) => formatNumber(row.tickets), align: 'end', footer: (rows) => formatNumber(rows.reduce((total, row) => total + row.tickets, 0)) },
  { id: 'ingresos', header: 'Ingresos', value: (row) => row.revenue, text: (row) => formatMoney(row.revenue), align: 'end', footer: (rows) => formatMoney(roundTo(rows.reduce((total, row) => total + row.revenue, 0), 2)) },
  { id: 'iva', header: 'IVA', value: (row) => row.tax, text: (row) => formatMoney(row.tax), align: 'end', footer: (rows) => formatMoney(roundTo(rows.reduce((total, row) => total + row.tax, 0), 2)) },
  { id: 'ticket', header: 'Ticket promedio', value: (row) => row.averageTicket, text: (row) => formatMoney(row.averageTicket), align: 'end' },
  { id: 'stock', header: 'Valor del stock', value: (row) => row.stockValue, text: (row) => formatMoney(row.stockValue), align: 'end', footer: (rows) => formatMoney(roundTo(rows.reduce((total, row) => total + row.stockValue, 0), 2)) },
  { id: 'participacion', header: 'Participación', value: (row) => row.sharePercent, text: (row) => formatPercent(row.sharePercent, 1), align: 'end' },
];

/** Una fila por día con los ingresos de cada sucursal (en el orden de `branches` del reporte). */
export interface BranchDayRow {
  key: string;
  day: string;
  values: readonly number[];
  total: number;
}

export function branchDayRows(report: BranchData): BranchDayRow[] {
  return report.days.map((day) => ({ key: day.day, day: day.day, values: day.revenueByBranch, total: roundTo(day.revenueByBranch.reduce((sum, value) => sum + value, 0), 2) }));
}

/** Columnas por día: una por sucursal (las del reporte) y el total. */
export function branchDayColumns(branches: readonly BranchRecord[]): ReportColumn<BranchDayRow>[] {
  return [
    { id: 'dia', header: 'Día', value: (row) => row.day, text: (row) => formatDate(row.day), card: 'title' },
    ...branches.map(
      (branch, index): ReportColumn<BranchDayRow> => ({
        id: `s-${branch.code}`,
        header: branch.code,
        value: (row) => row.values[index] ?? 0,
        text: (row) => formatMoney(row.values[index] ?? 0),
        align: 'end',
        footer: (rows) => formatMoney(roundTo(rows.reduce((sum, row) => sum + (row.values[index] ?? 0), 0), 2)),
      }),
    ),
    { id: 'total', header: 'Total', value: (row) => row.total, text: (row) => formatMoney(row.total), align: 'end', footer: (rows) => formatMoney(roundTo(rows.reduce((sum, row) => sum + row.total, 0), 2)) },
  ];
}

export function branchSummary(report: BranchData): { label: string; value: string }[] {
  return [
    { label: 'Ingresos de las sucursales', value: formatMoney(report.totalRevenue) },
    { label: 'Valor del stock', value: formatMoney(report.totalStockValue) },
    { label: 'Mercadería en tránsito', value: formatMoney(report.inTransitValue) },
    { label: 'Datos al', value: report.refreshedAt ? formatDateTime(report.refreshedAt) : 'Sin actualizar todavía' },
  ];
}

// ---------------------------------------------------------------------------------------------------- tecnología

export type TechList = 'categorias' | 'plataformas' | 'gpu' | 'consolas' | 'garantias' | 'series';

export const TECH_LISTS: readonly { value: TechList; label: string }[] = [
  { value: 'categorias', label: 'Ventas por categoría' },
  { value: 'plataformas', label: 'Ventas por plataforma' },
  { value: 'gpu', label: 'Tarjetas de video más vendidas' },
  { value: 'consolas', label: 'Consolas más vendidas' },
  { value: 'garantias', label: 'Garantías abiertas por estado' },
  { value: 'series', label: 'Unidades con serie en stock por categoría' },
];

export const TECH_DAYS: readonly { value: string; label: string }[] = [
  { value: '7', label: 'Últimos 7 días' },
  { value: '30', label: 'Últimos 30 días' },
  { value: '90', label: 'Últimos 90 días' },
  { value: '180', label: 'Últimos 180 días' },
  { value: '365', label: 'Últimos 365 días' },
];

export const TECH_FILTERS = { dias: '30', lista: 'categorias', q: '' };
export type TechFilters = typeof TECH_FILTERS;

/** Categorías de la edición Tecnología para «más vendidas» (códigos del catálogo de prueba). */
export const TECH_CATEGORY_CODES = { gpu: 'GPU', consoles: 'CON' } as const;

export function techDaysOf(value: string): number {
  return Number(TECH_DAYS.some((option) => option.value === value) ? value : TECH_FILTERS.dias);
}

export function techListOf(value: string): TechList {
  return TECH_LISTS.find((option) => option.value === value)?.value ?? 'categorias';
}

/** ¿La lista cuenta unidades (garantías, series) en vez de sumar ventas? */
export function isCountList(list: TechList): boolean {
  return list === 'garantias' || list === 'series';
}

export interface TechRow {
  key: string;
  rank: number;
  name: string;
  quantity: number | null;
  amount: number | null;
  count: number | null;
  share: number;
}

export function techRows(view: TechData, list: TechList): TechRow[] {
  if (isCountList(list)) {
    const counts = list === 'garantias' ? view.openClaimsByStatus : view.serialsInStockByCategory;
    const total = counts.reduce((sum, item) => sum + item.count, 0);
    return counts.map((item, index) => ({ key: item.name, rank: index + 1, name: item.name, quantity: null, amount: null, count: item.count, share: total > 0 ? item.count / total : 0 }));
  }
  const amounts = list === 'plataformas' ? view.salesByPlatform : list === 'gpu' ? view.topGpus : list === 'consolas' ? view.topConsoles : view.salesByCategory;
  const total = amounts.reduce((sum, item) => sum + item.amount, 0);
  return amounts.map((item, index) => ({ key: item.name, rank: index + 1, name: item.name, quantity: item.quantity, amount: item.amount, count: null, share: total > 0 ? item.amount / total : 0 }));
}

export function techColumns(list: TechList): ReportColumn<TechRow>[] {
  const nameHeaders: Record<TechList, string> = { categorias: 'Categoría', plataformas: 'Plataforma', gpu: 'Tarjeta de video', consolas: 'Consola', garantias: 'Estado', series: 'Categoría' };
  const columns: ReportColumn<TechRow>[] = [
    { id: 'puesto', header: '#', value: (row) => row.rank, text: (row) => String(row.rank), align: 'end', className: 'w-12', card: 'hidden', csv: false },
    { id: 'nombre', header: nameHeaders[list], value: (row) => row.name, text: (row) => row.name, card: 'title', className: 'min-w-44' },
  ];
  if (isCountList(list)) {
    columns.push({ id: 'cantidad', header: list === 'garantias' ? 'Casos' : 'Unidades', value: (row) => row.count, text: (row) => countOrDash(row.count), align: 'end', footer: (rows) => countOrDash(rows.reduce((sum, row) => sum + (row.count ?? 0), 0)) });
  } else {
    columns.push(
      { id: 'unidades', header: 'Unidades', value: (row) => row.quantity, text: (row) => quantityOrDash(row.quantity), align: 'end', footer: (rows) => quantityOrDash(roundTo(rows.reduce((sum, row) => sum + (row.quantity ?? 0), 0), 6)) },
      { id: 'ventas', header: 'Ventas', value: (row) => row.amount, text: (row) => moneyOrDash(row.amount), align: 'end', footer: (rows) => moneyOrDash(roundTo(rows.reduce((sum, row) => sum + (row.amount ?? 0), 0), 2)) },
    );
  }
  columns.push({ id: 'participacion', header: 'Participación', value: (row) => roundTo(row.share * 100, 1), text: (row) => formatPercent(row.share * 100, 1), align: 'end' });
  return columns;
}

export function techSummary(view: TechData): { label: string; value: string }[] {
  return [
    { label: 'Unidades con serie en stock', value: formatNumber(view.serialsInStock) },
    { label: 'Garantías abiertas', value: `${formatNumber(view.openClaims)} (${formatNumber(view.claimsOutOfWarranty)} fuera de garantía)` },
    { label: 'Cotizaciones vigentes', value: `${formatNumber(view.quotesOpen)} · ${formatMoney(view.quotesValue)}` },
    { label: 'Armados vendidos', value: `${formatNumber(view.buildsSold)} · ${formatMoney(view.buildsSoldValue)}` },
    { label: 'Reservas web activas', value: `${formatNumber(view.webReservationsActive)} · ${formatMoney(view.webReservationsValue)}` },
    { label: 'Productos con series que no cuadran', value: formatNumber(view.serializedWithoutSerials) },
  ];
}

/** A dónde ir desde cada lista del reporte de tecnología. */
export function techListLink(list: TechList): { label: string; to: string } | null {
  if (list === 'garantias') return { label: 'Ver las garantías', to: ROUTES.panelModule('garantias') };
  if (list === 'series') return { label: 'Ver las series', to: ROUTES.panelModule('series') };
  return null;
}

// ---------------------------------------------------------------------------------------------------- gráficos

/** Puntos de una serie por día; más de 62 días se agrupan por semana (las columnas se leen mejor). */
export function seriesPoints(days: readonly { date: string; amount: number }[]): { label: string; value: number }[] {
  if (days.length <= 62) return days.map((day) => ({ label: formatDate(day.date).slice(0, 5), value: day.amount }));
  const weeks: { label: string; value: number }[] = [];
  for (let index = 0; index < days.length; index += 7) {
    const chunk = days.slice(index, index + 7);
    weeks.push({ label: `Semana del ${formatDate(chunk[0].date).slice(0, 5)}`, value: roundTo(chunk.reduce((total, day) => total + day.amount, 0), 2) });
  }
  return weeks;
}

// ---------------------------------------------------------------------------------------------------- estadística del tablero

/** Ventas del mes en curso contra el MISMO tramo del mes anterior (del 1 al mismo día), con la serie diaria del anterior. */
export function monthComparison(current: SalesData, previous: SalesData, today: string): { current: number; previousSamePeriod: number; previousMonth: number; change: number | null } {
  const dayOfMonth = Number(today.slice(8, 10));
  const previousSamePeriod = roundTo(
    previous.byDay.filter((day) => Number(day.date.slice(8, 10)) <= dayOfMonth).reduce((sum, day) => sum + day.amount, 0),
    2,
  );
  const change = previousSamePeriod > 0 ? roundTo(((current.revenue - previousSamePeriod) / previousSamePeriod) * 100, 1) : null;
  return { current: current.revenue, previousSamePeriod, previousMonth: previous.revenue, change };
}
