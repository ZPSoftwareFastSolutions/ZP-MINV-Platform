// Módulo «Órdenes de compra» · funciones puras (sin React): cómo se presenta cada orden (estado, sucursal, entrega
// vencida y qué se puede hacer con ella), los filtros de la lista, el resumen plegado, las columnas del CSV, el formulario
// de la orden nueva, la recepción (con series de los productos serializados) y la factura del proveedor.
//
// Los tipos del servidor SALEN DEL CONTRATO (regla P-07). Las validaciones de aquí son COMODIDAD (regla P-01): el
// servidor vuelve a validar todo y su mensaje se muestra tal cual.

import type { RpcRequestOf, RpcResponseOf } from '@/4-presentation/app/contract';
import { defineStatuses, statusOf, statusOptions, type ComboOption, type SelectOption } from '@/4-presentation/panel/kit';
import { addDays, inRange, isIsoDate, matchesSearch, monthStart, roundTo, type CsvColumn } from '@/4-presentation/panel/lib';

// ---------------------------------------------------------------------------------------------------- tipos del contrato

/** Una orden de `GetPurchaseOrdersQuery`. */
export type OrderRecord = RpcResponseOf<'GetPurchaseOrdersQuery'>[number];
/** El detalle de una orden (`GetPurchaseOrderQuery`): la orden, sus líneas y sus recepciones. */
export type OrderDetailData = RpcResponseOf<'GetPurchaseOrderQuery'>;
export type OrderLineRecord = OrderDetailData['lines'][number];
/** Un proveedor de `GetSuppliersQuery`. */
export type SupplierRecord = RpcResponseOf<'GetSuppliersQuery'>[number];
/** Un producto del catálogo (`GetCatalogQuery`). */
export type CatalogRecord = RpcResponseOf<'GetCatalogQuery'>[number];
/** Una unidad de medida (`GetCatalogOptionsQuery`): dice si admite decimales. */
export type UnitRecord = RpcResponseOf<'GetCatalogOptionsQuery'>['units'][number];
/** Ficha técnica breve (`SearchTechProductsQuery`): si el producto lleva serie o IMEI. */
export type TechRecord = RpcResponseOf<'SearchTechProductsQuery'>[number];
export type ReceiptOutcome = RpcResponseOf<'ReceivePurchaseOrderCommand'>;
/** Recepción contabilizada sin factura del proveedor (`GetReceiptsWithoutInvoiceQuery`). */
export type PendingReceiptRecord = RpcResponseOf<'GetReceiptsWithoutInvoiceQuery'>[number];
/** Factura de proveedor registrada (`GetSupplierInvoicesQuery`). */
export type InvoiceRecord = RpcResponseOf<'GetSupplierInvoicesQuery'>[number];
export type CreateOrderPayload = RpcRequestOf<'CreatePurchaseOrderCommand'>;
export type ReceiveOrderPayload = RpcRequestOf<'ReceivePurchaseOrderCommand'>;
export type RegisterInvoicePayload = RpcRequestOf<'RegisterSupplierInvoiceCommand'>;

// ---------------------------------------------------------------------------------------------------- constantes

/** Largos máximos (los de la base de datos y del servidor). */
export const LIMITS = { notes: 250, supplierDocument: 40, invoiceNumber: 40, authorizationCode: 100, controlCode: 17 } as const;

/** Estados de una orden (enumeración `PurchaseOrderStatus` del servidor), con los nombres del escritorio. */
export const ORDER_STATES = defineStatuses({
  Draft: { label: 'Borrador', tone: 'warning' },
  Approved: { label: 'Aprobada', tone: 'info' },
  PartiallyReceived: { label: 'Recibida en parte', tone: 'info' },
  Received: { label: 'Recibida', tone: 'success' },
  Cancelled: { label: 'Anulada', tone: 'danger' },
});

/** Filtro «Por recibir» (aprobadas o recibidas en parte): lo usa el botón del tablero «Recibir mercadería». */
export const TO_RECEIVE = 'por-recibir';
/** Filtro «Abiertas» (borradores y por recibir). */
export const OPEN_ORDERS = 'abiertas';

export function orderStateOptions(): SelectOption[] {
  return [
    { value: TO_RECEIVE, label: 'Por recibir (aprobadas o en parte)' },
    { value: OPEN_ORDERS, label: 'Abiertas (borradores y por recibir)' },
    ...statusOptions(ORDER_STATES),
  ];
}

export function orderStateLabel(status: string): string {
  return statusOf(ORDER_STATES, status).label;
}

/** Texto del servidor sin la marca «✔»/«✖» del principio. */
export function plainMessage(text: string | null | undefined): string {
  return (text ?? '').replace(/^[\s✔✖✓✗]+/u, '').trim();
}

/** Código de la sucursal dentro de un número de documento («OC-CM-000012» → «CM»; regla B-02). */
export function branchOfNumber(number: string | null | undefined): string | null {
  const match = /^[A-Za-z]+-([A-Za-z0-9]+)-\d+$/.exec((number ?? '').trim());
  return match ? match[1].toUpperCase() : null;
}

// ---------------------------------------------------------------------------------------------------- lista

/** Qué se puede hacer con una orden según su estado (el servidor vuelve a decidir). */
export interface OrderAbilities {
  approve: boolean;
  receive: boolean;
  cancel: boolean;
}

export function abilitiesOf(status: string): OrderAbilities {
  return {
    approve: status === 'Draft',
    receive: status === 'Approved' || status === 'PartiallyReceived',
    cancel: status === 'Draft' || status === 'Approved',
  };
}

/** Una orden lista para la tabla. */
export interface OrderItem {
  key: string;
  row: OrderRecord;
  branchCode: string | null;
  /** Por recibir y con la entrega esperada antes de hoy. */
  overdue: boolean;
  abilities: OrderAbilities;
}

export function toOrderItem(row: OrderRecord, today: string): OrderItem {
  const abilities = abilitiesOf(row.status);
  return {
    key: row.id,
    row,
    branchCode: branchOfNumber(row.number),
    overdue: abilities.receive && row.expectedDate !== null && row.expectedDate < today,
    abilities,
  };
}

export function toOrderItems(rows: readonly OrderRecord[], today: string): OrderItem[] {
  return rows.map((row) => toOrderItem(row, today));
}

/** Filtros de la lista (en la dirección). `proveedor` es el código (`?proveedor=P002`, desde Proveedores o el pedido). */
export const ORDER_FILTERS = { q: '', estado: '', proveedor: '', sucursal: '', desde: '', hasta: '' };
export type OrderFilters = typeof ORDER_FILTERS;

export function matchesState(status: string, filter: string): boolean {
  if (!filter) return true;
  if (filter === TO_RECEIVE) return status === 'Approved' || status === 'PartiallyReceived';
  if (filter === OPEN_ORDERS) return status === 'Draft' || status === 'Approved' || status === 'PartiallyReceived';
  return status === filter;
}

export function filterOrders(items: readonly OrderItem[], filters: OrderFilters): OrderItem[] {
  const range = { from: filters.desde || null, to: filters.hasta || null };
  return items.filter(
    (item) =>
      matchesState(item.row.status, filters.estado) &&
      (!filters.proveedor || item.row.supplierCode.toUpperCase() === filters.proveedor.toUpperCase()) &&
      (!filters.sucursal || item.branchCode === filters.sucursal.toUpperCase()) &&
      inRange(item.row.orderDate, range) &&
      matchesSearch(filters.q, [item.row.number, item.row.supplier, item.row.supplierCode, item.row.notes]),
  );
}

/** Proveedores de la lista desplegable: los del directorio y los que aparecen en las órdenes, por nombre. */
export function supplierOptions(suppliers: readonly SupplierRecord[], items: readonly OrderItem[]): SelectOption[] {
  const byCode = new Map<string, string>();
  for (const supplier of suppliers) byCode.set(supplier.code, supplier.name);
  for (const item of items) if (!byCode.has(item.row.supplierCode)) byCode.set(item.row.supplierCode, item.row.supplier);
  return [...byCode.entries()]
    .map(([value, name]) => ({ value, label: `${name} (${value})` }))
    .sort((a, b) => a.label.localeCompare(b.label, 'es'));
}

/** Sucursales de la lista desplegable: las de la sesión y las que aparecen en los números de las órdenes. */
export function branchOptions(branches: readonly { code: string; name: string }[], codes: readonly (string | null)[]): SelectOption[] {
  const byCode = new Map<string, string>();
  for (const branch of branches) byCode.set(branch.code, `${branch.code} · ${branch.name}`);
  for (const code of codes) if (code && !byCode.has(code)) byCode.set(code, code);
  return [...byCode.entries()].map(([value, label]) => ({ value, label })).sort((a, b) => a.value.localeCompare(b.value, 'es'));
}

export const ORDERS_CSV: readonly CsvColumn<OrderItem>[] = [
  { header: 'Número', value: (item) => item.row.number },
  { header: 'Sucursal', value: (item) => item.branchCode },
  { header: 'Proveedor', value: (item) => item.row.supplier },
  { header: 'Código del proveedor', value: (item) => item.row.supplierCode },
  { header: 'Fecha', value: (item) => item.row.orderDate },
  { header: 'Entrega esperada', value: (item) => item.row.expectedDate },
  { header: 'Estado', value: (item) => orderStateLabel(item.row.status) },
  { header: 'Líneas', value: (item) => item.row.lines },
  { header: 'Total', value: (item) => item.row.total },
  { header: 'Recibido (%)', value: (item) => item.row.receivedPercent },
  { header: 'Notas', value: (item) => item.row.notes },
];

// ---------------------------------------------------------------------------------------------------- resumen (plegado)

export interface PurchaseSummary {
  drafts: { count: number; total: number };
  pending: { count: number; total: number; overdue: number };
  receivedThisMonth: { count: number; total: number };
  suppliers: number;
  orders: number;
}

/** Los indicadores del escritorio: borradores, por recibir, recibido este mes y proveedores con órdenes. */
export function purchaseSummary(rows: readonly OrderRecord[], today: string): PurchaseSummary {
  const sum = (list: readonly OrderRecord[]) => roundTo(list.reduce((total, row) => total + row.total, 0), 2);
  const drafts = rows.filter((row) => row.status === 'Draft');
  const pending = rows.filter((row) => row.status === 'Approved' || row.status === 'PartiallyReceived');
  const first = monthStart(today);
  const received = rows.filter((row) => row.status === 'Received' && row.orderDate >= first);
  return {
    drafts: { count: drafts.length, total: sum(drafts) },
    pending: { count: pending.length, total: sum(pending), overdue: pending.filter((row) => row.expectedDate !== null && row.expectedDate <= today).length },
    receivedThisMonth: { count: received.length, total: sum(received) },
    suppliers: new Set(rows.map((row) => row.supplierCode)).size,
    orders: rows.length,
  };
}

// ---------------------------------------------------------------------------------------------------- detalle

/** Lo que falta recibir de una línea (nunca negativo). */
export function pendingOf(line: Pick<OrderLineRecord, 'quantity' | 'received'>): number {
  return Math.max(0, roundTo(line.quantity - line.received, 6));
}

export function pendingLines(detail: OrderDetailData | undefined): OrderLineRecord[] {
  return (detail?.lines ?? []).filter((line) => pendingOf(line) > 0);
}

/** Valor de lo pendiente (cantidad pendiente × costo), solo para mostrar: el servidor calcula el definitivo. */
export function pendingValue(lines: readonly OrderLineRecord[]): number {
  return roundTo(
    lines.reduce((total, line) => total + pendingOf(line) * line.unitCost, 0),
    2,
  );
}

// ---------------------------------------------------------------------------------------------------- recepción

/** Productos con serie o IMEI (por SKU en mayúsculas). */
export function serialKinds(tech: readonly TechRecord[] | undefined): Map<string, TechRecord['serialKind']> {
  const map = new Map<string, TechRecord['serialKind']>();
  for (const row of tech ?? []) if (row.trackSerials) map.set(row.sku.toUpperCase(), row.serialKind);
  return map;
}

/** Series escritas o escaneadas: una por renglón (también sirven comas, punto y coma o tabuladores). */
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

/** Problema de las series de una línea (cantidad exacta, sin repetidas; un IMEI tiene 15 dígitos), o null. */
export function serialsProblem(serials: readonly string[], expected: number, kind: string | null | undefined): string | null {
  const word = serialWord(kind, expected);
  if (serials.length !== expected) return `Escriba ${expected} ${word} (una por unidad): hay ${serials.length}.`;
  const seen = new Set<string>();
  for (const serial of serials) {
    const upper = serial.toUpperCase();
    if (seen.has(upper)) return `La ${kind === 'Imei' ? 'IMEI' : 'serie'} ${serial} está repetida.`;
    seen.add(upper);
  }
  if (kind === 'Imei') {
    const wrong = serials.find((serial) => !/^\d{15}$/.test(serial));
    if (wrong) return `El IMEI ${wrong} no es válido: son 15 dígitos.`;
  }
  return null;
}

/** El pedido de la recepción: todo lo pendiente, el documento del proveedor y las series (solo de las líneas que las llevan). */
export function receivePayload(orderId: string, supplierDocument: string, serials: ReadonlyMap<string, readonly string[]>): ReceiveOrderPayload {
  const list = [...serials.entries()].filter(([, values]) => values.length > 0).map(([sku, values]) => ({ sku, serials: [...values] }));
  const document = supplierDocument.trim();
  return { id: orderId, supplierDocument: document || null, serials: list.length > 0 ? list : null };
}

// ---------------------------------------------------------------------------------------------------- orden nueva

export interface OrderDraftLine {
  sku: string;
  name: string;
  unit: string;
  allowsDecimals: boolean;
  quantity: number | null;
  unitCost: number | null;
}

export interface OrderDraft {
  supplierCode: string;
  /** «2026-10-05» o '' (sin fecha: el servidor usa el plazo de entrega del proveedor). */
  expectedDate: string;
  notes: string;
  /** Mostrar solo los productos del proveedor elegido. */
  onlySupplier: boolean;
  lines: OrderDraftLine[];
}

export function emptyDraft(supplierCode = ''): OrderDraft {
  return { supplierCode, expectedDate: '', notes: '', onlySupplier: true, lines: [] };
}

/** ¿La unidad admite decimales? (sin dato, sí: el servidor decide). */
export function allowsDecimals(units: readonly UnitRecord[], unit: string): boolean {
  return units.find((item) => item.code === unit)?.allowsDecimals ?? true;
}

/** Productos que se pueden pedir: activos y, si se pide, solo los del proveedor. */
export function productChoices(catalog: readonly CatalogRecord[], draft: Pick<OrderDraft, 'supplierCode' | 'onlySupplier' | 'lines'>): ComboOption<CatalogRecord>[] {
  return catalog
    .filter((item) => item.isActive && (!draft.onlySupplier || !draft.supplierCode || item.supplierCode === draft.supplierCode))
    .filter((item) => !draft.lines.some((line) => line.sku === item.sku))
    .map((item) => ({ value: item.sku, label: item.name, description: `${item.sku} · ${item.unit} · ${item.supplier ?? 'sin proveedor'}`, data: item }))
    .sort((a, b) => a.label.localeCompare(b.label, 'es'));
}

/** Cantidad propuesta: lo que falta para llegar del mínimo al máximo (como el escritorio), al menos 1. */
export function suggestedQuantity(item: Pick<CatalogRecord, 'minimum' | 'maximum'>): number {
  return Math.max(1, item.maximum > 0 ? roundTo(item.maximum - item.minimum, 3) : 1);
}

/** Agrega un producto (si ya está, suma 1 a su cantidad). */
export function addProduct(draft: OrderDraft, item: CatalogRecord, units: readonly UnitRecord[]): OrderDraft {
  const existing = draft.lines.find((line) => line.sku === item.sku);
  if (existing) {
    return { ...draft, lines: draft.lines.map((line) => (line.sku === item.sku ? { ...line, quantity: (line.quantity ?? 0) + 1 } : line)) };
  }
  const decimals = allowsDecimals(units, item.unit);
  const quantity = suggestedQuantity(item);
  const line: OrderDraftLine = {
    sku: item.sku,
    name: item.name,
    unit: item.unit,
    allowsDecimals: decimals,
    quantity: decimals ? quantity : Math.max(1, Math.round(quantity)),
    unitCost: item.unitCost,
  };
  return { ...draft, lines: [...draft.lines, line] };
}

export function lineSubtotal(line: Pick<OrderDraftLine, 'quantity' | 'unitCost'>): number {
  return roundTo((line.quantity ?? 0) * (line.unitCost ?? 0), 2);
}

export function draftTotal(draft: Pick<OrderDraft, 'lines'>): number {
  return roundTo(
    draft.lines.reduce((total, line) => total + lineSubtotal(line), 0),
    2,
  );
}

export interface LineProblems {
  quantity?: string;
  unitCost?: string;
}

export interface OrderProblems {
  supplierCode?: string;
  expectedDate?: string;
  notes?: string;
  lines?: string;
  byLine: Record<string, LineProblems>;
}

export function orderProblems(draft: OrderDraft, today: string): OrderProblems {
  const problems: OrderProblems = { byLine: {} };
  if (!draft.supplierCode) problems.supplierCode = 'Elija el proveedor.';
  const expected = draft.expectedDate.trim();
  if (expected && !isIsoDate(expected)) problems.expectedDate = 'Escriba una fecha válida.';
  else if (expected && expected < today) problems.expectedDate = 'La entrega esperada no puede ser anterior a hoy.';
  if (draft.notes.trim().length > LIMITS.notes) problems.notes = `Las notas admiten hasta ${LIMITS.notes} caracteres.`;
  if (draft.lines.length === 0) problems.lines = 'Agregue al menos un producto a la orden.';
  for (const line of draft.lines) {
    const found: LineProblems = {};
    if (line.quantity === null) found.quantity = 'Indique la cantidad.';
    else if (line.quantity <= 0) found.quantity = 'La cantidad debe ser mayor que 0.';
    else if (!line.allowsDecimals && !Number.isInteger(line.quantity)) found.quantity = `En ${line.unit} la cantidad es entera.`;
    if (line.unitCost === null) found.unitCost = 'Indique el costo unitario.';
    else if (line.unitCost < 0) found.unitCost = 'El costo no puede ser negativo.';
    if (found.quantity || found.unitCost) problems.byLine[line.sku] = found;
  }
  return problems;
}

export function hasOrderProblems(problems: OrderProblems): boolean {
  return Boolean(problems.supplierCode || problems.expectedDate || problems.notes || problems.lines || Object.keys(problems.byLine).length > 0);
}

/** El pedido de `CreatePurchaseOrderCommand` (todos los parámetros, los vacíos como null). */
export function createOrderPayload(draft: OrderDraft): CreateOrderPayload {
  const notes = draft.notes.trim();
  return {
    supplierCode: draft.supplierCode,
    expectedDate: draft.expectedDate.trim() || null,
    notes: notes || null,
    lines: draft.lines.map((line) => ({ sku: line.sku, quantity: line.quantity ?? 0, unitCost: line.unitCost ?? 0 })),
  };
}

/** Entrega que usará el servidor si no se indica fecha: hoy + los días de entrega del proveedor (al menos 1). */
export function defaultDelivery(today: string, leadTimeDays: number | null | undefined): string {
  return addDays(today, Math.max(1, leadTimeDays ?? 1));
}

// ---------------------------------------------------------------------------------------------------- facturas de proveedores

/** Tipos de compra del libro de compras del SIN (los del escritorio). */
export const PURCHASE_TYPES: readonly SelectOption[] = [
  { value: '1', label: '1 · Mercado interno (actividades gravadas)' },
  { value: '2', label: '2 · Mercado interno (actividades no gravadas)' },
  { value: '3', label: '3 · Sujetas a proporcionalidad' },
  { value: '4', label: '4 · Para exportaciones' },
  { value: '5', label: '5 · Mercado interno y exportaciones' },
];

/**
 * Importe con IVA que se PROPONE para una recepción al costo neto (el 87 % de lo facturado): recepción ÷ 0,87, como el
 * escritorio. Es solo una propuesta: se escribe el importe que dice la factura del proveedor.
 */
export function proposedInvoiceTotal(receiptTotal: number): number {
  return roundTo(receiptTotal / 0.87, 2);
}

export interface InvoiceDraft {
  invoiceNumber: string;
  authorizationCode: string;
  invoiceDate: string;
  totalAmount: number | null;
  discounts: number | null;
  notSubjectToVat: number | null;
  purchaseType: string;
  controlCode: string;
}

export function invoiceDraftFor(receipt: PendingReceiptRecord): InvoiceDraft {
  return {
    invoiceNumber: '',
    authorizationCode: '',
    invoiceDate: receipt.receivedOn,
    totalAmount: proposedInvoiceTotal(receipt.total),
    discounts: 0,
    notSubjectToVat: 0,
    purchaseType: '1',
    controlCode: '',
  };
}

export type InvoiceProblems = Partial<Record<keyof InvoiceDraft, string>>;

export function invoiceProblems(draft: InvoiceDraft, today: string): InvoiceProblems {
  const problems: InvoiceProblems = {};
  const number = draft.invoiceNumber.trim();
  if (!number) problems.invoiceNumber = 'Indique el número de la factura del proveedor.';
  else if (number.length > LIMITS.invoiceNumber) problems.invoiceNumber = `Hasta ${LIMITS.invoiceNumber} caracteres.`;
  const authorization = draft.authorizationCode.trim();
  if (!authorization) problems.authorizationCode = 'Indique el CUF (o el código de autorización) de la factura.';
  else if (authorization.length > LIMITS.authorizationCode) problems.authorizationCode = `Hasta ${LIMITS.authorizationCode} caracteres.`;
  if (!isIsoDate(draft.invoiceDate)) problems.invoiceDate = 'Escriba la fecha de la factura.';
  else if (draft.invoiceDate > today) problems.invoiceDate = 'La fecha de la factura no puede ser posterior a hoy.';
  if (draft.totalAmount === null) problems.totalAmount = 'Indique el importe total de la factura.';
  else if (draft.totalAmount <= 0) problems.totalAmount = 'El importe total debe ser mayor que 0.';
  if (draft.discounts !== null && draft.discounts < 0) problems.discounts = 'Los descuentos no pueden ser negativos.';
  if (draft.notSubjectToVat !== null && draft.notSubjectToVat < 0) problems.notSubjectToVat = 'No puede ser negativo.';
  if (!PURCHASE_TYPES.some((type) => type.value === draft.purchaseType)) problems.purchaseType = 'Elija el tipo de compra.';
  if (draft.controlCode.trim().length > LIMITS.controlCode) problems.controlCode = `Hasta ${LIMITS.controlCode} caracteres.`;
  return problems;
}

export function invoicePayload(receiptNumber: string, draft: InvoiceDraft): RegisterInvoicePayload {
  const control = draft.controlCode.trim();
  return {
    receiptNumber,
    invoiceNumber: draft.invoiceNumber.trim(),
    authorizationCode: draft.authorizationCode.trim(),
    invoiceDate: draft.invoiceDate,
    totalAmount: draft.totalAmount ?? 0,
    discounts: draft.discounts ?? 0,
    notSubjectToVat: draft.notSubjectToVat ?? 0,
    purchaseType: Number(draft.purchaseType),
    controlCode: control || null,
  };
}

/** Filtros de la pestaña de facturas (prefijo `f_`). Las fechas son las de las facturas; por defecto, el mes en curso. */
export function invoiceFilters(today: string) {
  return { q: '', proveedor: '', desde: monthStart(today), hasta: today };
}
export type InvoiceFilters = ReturnType<typeof invoiceFilters>;

/** Primer día que se consulta cuando se eligen «Todas» las fechas. */
export const FIRST_DAY = '2000-01-01';

/** El rango que se pide al servidor (`GetSupplierInvoicesQuery` exige las dos fechas). */
export function invoiceRange(filters: Pick<InvoiceFilters, 'desde' | 'hasta'>, today: string): { from: string; to: string } {
  return { from: filters.desde || FIRST_DAY, to: filters.hasta || today };
}

export function filterInvoices(rows: readonly InvoiceRecord[], filters: InvoiceFilters): InvoiceRecord[] {
  return rows.filter(
    (row) =>
      (!filters.proveedor || row.supplierCode === filters.proveedor) &&
      matchesSearch(filters.q, [row.number, row.supplier, row.supplierCode, row.supplierNit, row.authorizationCode, row.receiptNumber]),
  );
}

export function filterPendingReceipts(rows: readonly PendingReceiptRecord[], filters: InvoiceFilters): PendingReceiptRecord[] {
  return rows.filter((row) => (!filters.proveedor || row.supplierCode === filters.proveedor) && matchesSearch(filters.q, [row.receiptNumber, row.supplier, row.supplierCode]));
}

export function invoiceSupplierOptions(pending: readonly PendingReceiptRecord[], invoices: readonly InvoiceRecord[]): SelectOption[] {
  const byCode = new Map<string, string>();
  for (const row of pending) byCode.set(row.supplierCode, row.supplier);
  for (const row of invoices) byCode.set(row.supplierCode, row.supplier);
  return [...byCode.entries()].map(([value, name]) => ({ value, label: `${name} (${value})` })).sort((a, b) => a.label.localeCompare(b.label, 'es'));
}

export const INVOICES_CSV: readonly CsvColumn<InvoiceRecord>[] = [
  { header: 'Fecha', value: (row) => row.invoiceDate },
  { header: 'Número', value: (row) => row.number },
  { header: 'Proveedor', value: (row) => row.supplier },
  { header: 'NIT del proveedor', value: (row) => row.supplierNit },
  { header: 'CUF o autorización', value: (row) => row.authorizationCode },
  { header: 'Recepción', value: (row) => row.receiptNumber },
  { header: 'Sucursal', value: (row) => row.branchCode },
  { header: 'Importe', value: (row) => row.totalAmount },
  { header: 'Base crédito fiscal', value: (row) => row.taxBase },
  { header: 'Crédito fiscal', value: (row) => row.taxCredit },
  { header: 'Estado', value: (row) => row.status },
];

export const PENDING_RECEIPTS_CSV: readonly CsvColumn<PendingReceiptRecord>[] = [
  { header: 'Recepción', value: (row) => row.receiptNumber },
  { header: 'Recibida el', value: (row) => row.receivedOn },
  { header: 'Proveedor', value: (row) => row.supplier },
  { header: 'Código del proveedor', value: (row) => row.supplierCode },
  { header: 'Total recibido', value: (row) => row.total },
];
