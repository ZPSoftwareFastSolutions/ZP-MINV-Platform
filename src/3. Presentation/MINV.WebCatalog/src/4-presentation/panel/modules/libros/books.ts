// Módulo «Libros fiscales» · funciones puras (sin React): meses que se ofrecen, filtros, estados del libro de ventas del SIN,
// columnas de los CSV, líneas del resumen IVA / IT con su explicación (las del escritorio: `FiscalBooksViewModel`) y la
// guía de la factura del proveedor (`SupplierInvoiceDialog`). Los importes los calcula el servidor; lo que se calcula aquí
// (importe propuesto y crédito estimado de una factura del proveedor) es solo una guía antes de enviar (regla P-01).

import type { RpcRequestOf, RpcResponseOf } from '@/4-presentation/app/contract';
import { defineStatuses, statusOf } from '@/4-presentation/panel/kit';
import { formatDate, formatMoney, matchesSearch, roundTo, type CsvColumn } from '@/4-presentation/panel/lib';

// ---------------------------------------------------------------------------------------------------- tipos del contrato

export type SalesBookData = RpcResponseOf<'GetSalesBookQuery'>;
export type SalesBookRowData = SalesBookData['rows'][number];
export type PurchasesBookData = RpcResponseOf<'GetPurchasesBookQuery'>;
export type PurchasesBookRowData = PurchasesBookData['rows'][number];
export type TaxSummaryData = RpcResponseOf<'GetTaxSummaryQuery'>;
export type PendingReceiptData = RpcResponseOf<'GetReceiptsWithoutInvoiceQuery'>[number];
export type SupplierInvoicePayload = RpcRequestOf<'RegisterSupplierInvoiceCommand'>;

/** Documento sector de la factura compra venta. */
export const SECTOR_PURCHASE_SALE = 1;

// ---------------------------------------------------------------------------------------------------- meses

const MONTHS = ['enero', 'febrero', 'marzo', 'abril', 'mayo', 'junio', 'julio', 'agosto', 'septiembre', 'octubre', 'noviembre', 'diciembre'];

export interface MonthValue {
  year: number;
  month: number;
}

/** «2026-09» del día de hoy («2026-09-29»). */
export function monthOf(isoDay: string): string {
  return isoDay.slice(0, 7);
}

/** «2026-09» → { year: 2026, month: 9 } (null si no sirve). */
export function parseMonth(value: string): MonthValue | null {
  const match = /^(\d{4})-(\d{2})$/.exec(value);
  if (!match) return null;
  const month = Number(match[2]);
  return month >= 1 && month <= 12 ? { year: Number(match[1]), month } : null;
}

/** «Septiembre 2026». */
export function monthLabel(value: MonthValue): string {
  const name = MONTHS[value.month - 1] ?? String(value.month);
  return `${name.charAt(0).toUpperCase()}${name.slice(1)} ${value.year}`;
}

/** Los últimos `count` meses (el actual primero), como el escritorio (18). */
export function recentMonths(isoToday: string, count = 18): { value: string; label: string }[] {
  const current = parseMonth(monthOf(isoToday)) ?? { year: 2026, month: 1 };
  return Array.from({ length: count }, (_, index) => {
    const total = current.year * 12 + (current.month - 1) - index;
    const value = { year: Math.floor(total / 12), month: (total % 12) + 1 };
    return { value: `${value.year}-${String(value.month).padStart(2, '0')}`, label: monthLabel(value) };
  });
}

// ---------------------------------------------------------------------------------------------------- filtros

/** Filtros de la pantalla (en la dirección): mes, sucursal, búsqueda y, en el libro de ventas, estado y tipo. */
export function bookFilters(isoToday: string) {
  return { mes: monthOf(isoToday), sucursal: '', q: '', estado: '', tipo: '' };
}
export type BookFilters = ReturnType<typeof bookFilters>;

/** Estado de un registro del libro de ventas (códigos del SIN). */
export const SALES_STATUSES = defineStatuses({
  V: { label: 'Válida', tone: 'success' },
  A: { label: 'Anulada', tone: 'neutral' },
  E: { label: 'Extraviada', tone: 'warning' },
  N: { label: 'No utilizada', tone: 'warning' },
  C: { label: 'Contingencia', tone: 'warning' },
  L: { label: 'Libre consignación', tone: 'warning' },
});

export const KIND_OPTIONS: readonly { value: string; label: string }[] = [
  { value: 'factura', label: 'Facturas' },
  { value: 'nota', label: 'Notas crédito-débito' },
];

export function kindText(sector: number): string {
  return sector === SECTOR_PURCHASE_SALE ? 'Factura' : 'Nota';
}

/** «5115889-1A». */
export function buyerDocumentText(row: Pick<SalesBookRowData, 'buyerDocument' | 'complement'>): string {
  return row.complement ? `${row.buyerDocument}-${row.complement}` : row.buyerDocument;
}

/** CUF o código de autorización abreviado (el completo va en la fila desplegable). */
export function shortCode(code: string): string {
  return code.length > 16 ? `${code.slice(0, 8)}…${code.slice(-6)}` : code;
}

/** Fecha fiscal del libro de ventas (hora de Bolivia SIN zona) → «29/09/2026», sin correrla. */
export function fiscalDate(value: string): string {
  const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(value);
  return match ? `${match[3]}/${match[2]}/${match[1]}` : formatDate(value);
}

export function filterSales(rows: readonly SalesBookRowData[], filters: BookFilters): SalesBookRowData[] {
  return rows.filter(
    (row) =>
      (!filters.sucursal || row.branchCode === filters.sucursal) &&
      (!filters.estado || row.status === filters.estado) &&
      (!filters.tipo || (filters.tipo === 'factura') === (row.documentSector === SECTOR_PURCHASE_SALE)) &&
      matchesSearch(filters.q, [row.number, row.cuf, row.buyerDocument, row.buyerName, row.controlCode]),
  );
}

export function filterPurchases(rows: readonly PurchasesBookRowData[], filters: BookFilters): PurchasesBookRowData[] {
  return rows.filter(
    (row) =>
      (!filters.sucursal || row.branchCode === filters.sucursal) &&
      matchesSearch(filters.q, [row.invoiceNumber, row.authorizationCode, row.supplierNit, row.supplierName, row.controlCode]),
  );
}

export function branchOptions(rows: readonly { branchCode: string }[]): { value: string; label: string }[] {
  return [...new Set(rows.map((row) => row.branchCode))].sort().map((code) => ({ value: code, label: code }));
}

export function sum<T>(rows: readonly T[], value: (row: T) => number): number {
  return roundTo(
    rows.reduce((total, row) => total + value(row), 0),
    2,
  );
}

// ---------------------------------------------------------------------------------------------------- resumen IVA / IT

export interface TaxLine {
  label: string;
  amount: string;
  explanation: string;
  total?: boolean;
}

/** Las líneas del resumen IVA / IT con su explicación (como el escritorio). */
export function taxLines(summary: TaxSummaryData): TaxLine[] {
  return [
    { label: 'Ventas brutas facturadas', amount: formatMoney(summary.grossSales), explanation: 'Suma de las facturas válidas, pendientes o fuera de línea del mes (las anuladas no cuentan).' },
    { label: 'Devoluciones con nota crédito-débito', amount: `− ${formatMoney(summary.creditNotes)}`, explanation: 'Monto devuelto en las notas crédito-débito emitidas en el mes.' },
    { label: 'Débito fiscal IVA (13 %)', amount: formatMoney(summary.taxDebit), explanation: '13 % de la base para débito fiscal del libro de ventas (el IVA que cobró en sus ventas).' },
    {
      label: 'Crédito fiscal de compras',
      amount: `− ${formatMoney(summary.taxCreditPurchases)}`,
      explanation: '13 % de la base de las facturas de proveedores registradas en el libro de compras (el IVA que pagó al comprar).',
    },
    { label: 'Crédito fiscal de las notas', amount: `− ${formatMoney(summary.taxCreditNotes)}`, explanation: '13 % de lo devuelto con notas crédito-débito: reduce el IVA a pagar.' },
    {
      label: 'IVA a pagar',
      amount: formatMoney(summary.vatPayable),
      explanation: 'Débito fiscal menos los créditos fiscales. Si es negativo, queda como saldo a favor para el mes siguiente.',
      total: true,
    },
    { label: 'Saldo a favor (crédito que pasa al mes siguiente)', amount: formatMoney(summary.vatCarryForward), explanation: 'Crédito fiscal que no se usó este mes.' },
    { label: 'Base del IT (ventas netas de devoluciones)', amount: formatMoney(summary.transactionTaxBase), explanation: 'Ingresos brutos del mes menos las devoluciones.' },
    {
      label: 'Impuesto a las Transacciones (IT, 3 %)',
      amount: formatMoney(summary.transactionTax),
      explanation: '3 % de la base del IT. El traslado a los formularios 200 (IVA) y 400 (IT) lo hace el contador.',
      total: true,
    },
  ];
}

// ---------------------------------------------------------------------------------------------------- CSV

export const SALES_CSV: readonly CsvColumn<SalesBookRowData>[] = [
  { header: 'N°', value: (row) => row.row },
  { header: 'Fecha', value: (row) => fiscalDate(row.date) },
  { header: 'Tipo', value: (row) => kindText(row.documentSector) },
  { header: 'Número', value: (row) => row.number },
  { header: 'CUF', value: (row) => row.cuf },
  { header: 'NIT / CI', value: (row) => buyerDocumentText(row) },
  { header: 'Nombre o razón social', value: (row) => row.buyerName },
  { header: 'Importe total', value: (row) => row.total },
  { header: 'Descuentos', value: (row) => row.discounts },
  { header: 'Base para débito fiscal', value: (row) => row.taxBase },
  { header: 'Débito fiscal', value: (row) => row.taxDebit },
  { header: 'Estado', value: (row) => statusOf(SALES_STATUSES, row.status).label },
  { header: 'Código de control', value: (row) => row.controlCode },
  { header: 'Sucursal', value: (row) => row.branchCode },
];

export const PURCHASES_CSV: readonly CsvColumn<PurchasesBookRowData>[] = [
  { header: 'N°', value: (row) => row.row },
  { header: 'Fecha', value: (row) => formatDate(row.date) },
  { header: 'NIT del proveedor', value: (row) => row.supplierNit },
  { header: 'Proveedor', value: (row) => row.supplierName },
  { header: 'Código de autorización', value: (row) => row.authorizationCode },
  { header: 'N° de factura', value: (row) => row.invoiceNumber },
  { header: 'Importe total', value: (row) => row.total },
  { header: 'Descuentos', value: (row) => row.discounts },
  { header: 'Base para crédito fiscal', value: (row) => row.taxBase },
  { header: 'Crédito fiscal', value: (row) => row.taxCredit },
  { header: 'Tipo de compra', value: (row) => row.purchaseType },
  { header: 'Código de control', value: (row) => row.controlCode },
  { header: 'Sucursal', value: (row) => row.branchCode },
];

export const TAX_CSV: readonly CsvColumn<TaxLine>[] = [
  { header: 'Concepto', value: (line) => line.label },
  { header: 'Importe', value: (line) => line.amount },
  { header: 'Explicación', value: (line) => line.explanation },
];

export const PENDING_CSV: readonly CsvColumn<PendingReceiptData>[] = [
  { header: 'Recepción', value: (row) => row.receiptNumber },
  { header: 'Recibida', value: (row) => formatDate(row.receivedOn) },
  { header: 'Proveedor', value: (row) => row.supplier },
  { header: 'Código del proveedor', value: (row) => row.supplierCode },
  { header: 'Total al costo neto', value: (row) => row.total },
];

// ---------------------------------------------------------------------------------------------------- factura del proveedor

/** Tipos de compra del libro de compras del SIN. */
export const PURCHASE_TYPES: readonly { value: string; label: string }[] = [
  { value: '1', label: '1 · Compras para el mercado interno (actividades gravadas)' },
  { value: '2', label: '2 · Compras para el mercado interno (actividades no gravadas)' },
  { value: '3', label: '3 · Compras sujetas a proporcionalidad' },
  { value: '4', label: '4 · Compras para exportaciones' },
  { value: '5', label: '5 · Compras para mercado interno y exportaciones' },
];

/** Importe con IVA que se propone para una recepción al costo neto: recepción ÷ 0,87 (guía; lo que manda es la factura). */
export function proposedInvoiceTotal(receiptTotal: number): number {
  return roundTo(receiptTotal / 0.87, 2);
}

/** Base y crédito fiscal estimados (13 %) de lo escrito, para verlos antes de registrar. */
export function estimatedCredit(total: number | null, discounts: number | null, notSubject: number | null): { base: number; credit: number } {
  const base = Math.max(0, (total ?? 0) - (discounts ?? 0) - (notSubject ?? 0));
  return { base: roundTo(base, 2), credit: roundTo(base * 0.13, 2) };
}

export interface SupplierInvoiceDraft {
  invoiceNumber: string;
  authorizationCode: string;
  invoiceDate: string;
  total: number | null;
  discounts: number | null;
  notSubject: number | null;
  purchaseType: string;
  controlCode: string;
}

export interface SupplierInvoiceErrors {
  invoiceNumber?: string;
  authorizationCode?: string;
  invoiceDate?: string;
  total?: string;
  discounts?: string;
}

export function supplierInvoiceErrors(draft: SupplierInvoiceDraft): SupplierInvoiceErrors {
  const errors: SupplierInvoiceErrors = {};
  if (draft.invoiceNumber.trim().length === 0) errors.invoiceNumber = 'Escriba el número de la factura del proveedor.';
  if (draft.authorizationCode.trim().length === 0) errors.authorizationCode = 'Escriba el código de autorización o el CUF de la factura.';
  if (!/^\d{4}-\d{2}-\d{2}$/.test(draft.invoiceDate)) errors.invoiceDate = 'Elija la fecha de la factura.';
  if (draft.total === null || draft.total <= 0) errors.total = 'Escriba el importe total de la factura.';
  else if ((draft.discounts ?? 0) + (draft.notSubject ?? 0) > draft.total) errors.discounts = 'Los descuentos y lo no sujeto a IVA no pueden pasar del importe total.';
  return errors;
}

export function supplierInvoicePayload(receiptNumber: string, draft: SupplierInvoiceDraft): SupplierInvoicePayload {
  const control = draft.controlCode.trim();
  return {
    receiptNumber,
    invoiceNumber: draft.invoiceNumber.trim(),
    authorizationCode: draft.authorizationCode.trim(),
    invoiceDate: draft.invoiceDate,
    totalAmount: draft.total ?? 0,
    discounts: draft.discounts ?? 0,
    notSubjectToVat: draft.notSubject ?? 0,
    purchaseType: Number(draft.purchaseType),
    controlCode: control.length > 0 ? control : null,
  };
}

/** Quita el ✔ / ⚠ / ✖ del principio de un mensaje del servidor (la pantalla pone su propio ícono). */
export function plainMessage(text: string | null | undefined): string {
  return (text ?? '').replace(/^[\s✔⚠✖]+/u, '').trim();
}
