// Módulo «Libros fiscales» · funciones puras: meses, filtros de los libros, totales, líneas del resumen IVA / IT y la guía
// de la factura del proveedor.

import { describe, expect, it } from 'vitest';
import {
  bookFilters,
  buyerDocumentText,
  estimatedCredit,
  filterPurchases,
  filterSales,
  fiscalDate,
  monthLabel,
  parseMonth,
  proposedInvoiceTotal,
  recentMonths,
  sum,
  supplierInvoiceErrors,
  supplierInvoicePayload,
  taxLines,
  type PurchasesBookRowData,
  type SalesBookRowData,
  type SupplierInvoiceDraft,
} from './books';

function sale(overrides: Partial<SalesBookRowData>): SalesBookRowData {
  return {
    branchCode: 'CM',
    buyerDocument: '4455667',
    buyerName: 'Juan Pérez',
    complement: null,
    controlCode: '0',
    cuf: 'CUF-1',
    date: '2026-09-01T10:00:00',
    discounts: 0,
    documentSector: 1,
    exports: 0,
    fees: 0,
    giftCard: 0,
    ice: 0,
    iehd: 0,
    ipj: 0,
    number: 101,
    otherNotSubject: 0,
    row: 1,
    status: 'V',
    subtotal: 100,
    taxBase: 100,
    taxDebit: 13,
    total: 100,
    zeroRate: 0,
    ...overrides,
  };
}

function purchase(overrides: Partial<PurchasesBookRowData>): PurchasesBookRowData {
  return {
    authorizationCode: 'AUT-1',
    branchCode: 'CM',
    controlCode: '0',
    date: '2026-09-05',
    discounts: 0,
    exempt: 0,
    fees: 0,
    giftCard: 0,
    ice: 0,
    iehd: 0,
    invoiceNumber: '555',
    ipj: 0,
    otherNotSubject: 0,
    purchaseType: '1',
    row: 1,
    subtotal: 1000,
    supplierName: 'Distribuidora Andina',
    supplierNit: '1023456',
    taxBase: 1000,
    taxCredit: 130,
    total: 1000,
    zeroRate: 0,
    ...overrides,
  };
}

describe('Libros fiscales · meses', () => {
  it('los últimos 18 meses (el actual primero) y la lectura del mes de la dirección', () => {
    const months = recentMonths('2026-09-29');
    expect(months).toHaveLength(18);
    expect(months[0]).toEqual({ value: '2026-09', label: 'Septiembre 2026' });
    expect(months[9]).toEqual({ value: '2025-12', label: 'Diciembre 2025' });
    expect(months[17]).toEqual({ value: '2025-04', label: 'Abril 2025' });
    expect(parseMonth('2026-13')).toBeNull();
    expect(parseMonth('2026-01')).toEqual({ year: 2026, month: 1 });
    expect(monthLabel({ year: 2026, month: 2 })).toBe('Febrero 2026');
    expect(bookFilters('2026-09-29').mes).toBe('2026-09');
  });
});

describe('Libros fiscales · filtros y totales', () => {
  it('libro de ventas: sucursal, estado, tipo (factura o nota) y búsqueda', () => {
    const rows = [sale({ row: 1 }), sale({ row: 2, status: 'A', buyerName: 'Ana Rojas', number: 102 }), sale({ row: 3, documentSector: 24, branchCode: 'CB', number: 5 })];
    const base = bookFilters('2026-09-29');
    expect(filterSales(rows, { ...base, estado: 'A' }).map((row) => row.row)).toEqual([2]);
    expect(filterSales(rows, { ...base, tipo: 'nota' }).map((row) => row.row)).toEqual([3]);
    expect(filterSales(rows, { ...base, tipo: 'factura' }).map((row) => row.row)).toEqual([1, 2]);
    expect(filterSales(rows, { ...base, sucursal: 'CB' }).map((row) => row.row)).toEqual([3]);
    expect(filterSales(rows, { ...base, q: 'ana' }).map((row) => row.row)).toEqual([2]);
    expect(buyerDocumentText(sale({ complement: '1A' }))).toBe('4455667-1A');
    expect(fiscalDate('2026-09-01T23:59:59')).toBe('01/09/2026');
  });

  it('libro de compras: sucursal y búsqueda; totales redondeados a centavos', () => {
    const rows = [purchase({}), purchase({ row: 2, supplierName: 'Importadora Sur', branchCode: 'SC', total: 0.1 }), purchase({ row: 3, total: 0.2 })];
    const base = bookFilters('2026-09-29');
    expect(filterPurchases(rows, { ...base, q: 'sur' }).map((row) => row.row)).toEqual([2]);
    expect(filterPurchases(rows, { ...base, sucursal: 'CM' }).map((row) => row.row)).toEqual([1, 3]);
    expect(sum(rows.slice(1), (row) => row.total)).toBe(0.3);
  });

  it('resumen IVA / IT: las líneas del escritorio con su explicación', () => {
    const lines = taxLines({
      creditNotes: 350,
      grossSales: 2200,
      invoices: 2,
      month: 9,
      notes: 1,
      taxCreditNotes: 45.5,
      taxCreditPurchases: 130,
      taxDebit: 286,
      transactionTax: 55.5,
      transactionTaxBase: 1850,
      vatCarryForward: 0,
      vatPayable: 110.5,
      voidedInvoices: 1,
      year: 2026,
    });
    expect(lines).toHaveLength(9);
    expect(lines[1]).toMatchObject({ label: 'Devoluciones con nota crédito-débito', amount: '− Bs 350,00' });
    expect(lines[5]).toMatchObject({ label: 'IVA a pagar', amount: 'Bs 110,50', total: true });
  });
});

describe('Libros fiscales · factura del proveedor', () => {
  const draft: SupplierInvoiceDraft = {
    invoiceNumber: ' 555-B ',
    authorizationCode: ' ABC123 ',
    invoiceDate: '2026-09-20',
    total: 1000,
    discounts: 50,
    notSubject: null,
    purchaseType: '2',
    controlCode: ' ',
  };

  it('propone recepción ÷ 0,87 y estima la base y el crédito (13 %)', () => {
    expect(proposedInvoiceTotal(870)).toBe(1000);
    expect(proposedInvoiceTotal(100)).toBe(114.94);
    expect(estimatedCredit(1000, 50, 10)).toEqual({ base: 940, credit: 122.2 });
    expect(estimatedCredit(null, null, null)).toEqual({ base: 0, credit: 0 });
  });

  it('valida lo obligatorio y arma el pedido con la forma exacta del contrato', () => {
    expect(supplierInvoiceErrors({ ...draft, invoiceNumber: '', authorizationCode: '', invoiceDate: '', total: null })).toEqual({
      invoiceNumber: 'Escriba el número de la factura del proveedor.',
      authorizationCode: 'Escriba el código de autorización o el CUF de la factura.',
      invoiceDate: 'Elija la fecha de la factura.',
      total: 'Escriba el importe total de la factura.',
    });
    expect(supplierInvoiceErrors({ ...draft, discounts: 900, notSubject: 200 }).discounts).toBe('Los descuentos y lo no sujeto a IVA no pueden pasar del importe total.');
    expect(supplierInvoiceErrors(draft)).toEqual({});
    expect(supplierInvoicePayload('REC-CM-000010', draft)).toEqual({
      receiptNumber: 'REC-CM-000010',
      invoiceNumber: '555-B',
      authorizationCode: 'ABC123',
      invoiceDate: '2026-09-20',
      totalAmount: 1000,
      discounts: 50,
      notSubjectToVat: 0,
      purchaseType: 2,
      controlCode: null,
    });
  });
});
