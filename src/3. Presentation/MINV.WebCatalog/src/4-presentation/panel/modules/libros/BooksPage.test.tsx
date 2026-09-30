// Módulo «Libros fiscales» dentro del panel real, con un registro que tiene SOLO este módulo. El servidor se simula en la
// prueba (el modo mock no atiende estas operaciones): libro de ventas y de compras del mes (filtros en la dirección y
// totales al pie), descarga del libro para el SIN, resumen IVA / IT, resumen del mes plegado, recepciones sin factura con
// el registro de la factura del proveedor (forma exacta del contrato), errores del servidor, permisos, la estadística y
// el tablero. Ninguna prueba toca la red. La hora queda fija: 29/09/2026 10:00 en La Paz.

import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { WebApiError } from '@/1-domain/auth/errors';
import { renderPanel, signedInAs, type MockWeb, type StaffRole } from '@/test-utils';
import { buildRegistry, dashboardActions, dashboardStats } from '../../registry';
import { PanelApp } from '../../shell/PanelApp';
import type { PendingReceiptData, PurchasesBookData, SalesBookData, SalesBookRowData, TaxSummaryData } from './books';
import libros from './module';
import { TaxStat } from './TaxStat';

const REGISTRY = buildRegistry([{ source: '../modules/libros/module.tsx', definition: libros }]);

/** 10:00 de La Paz del 29/09/2026. */
const NOW = new Date('2026-09-29T14:00:00Z');

beforeAll(async () => {
  await import('../../shell/PanelLayout');
  await import('./BooksPage');
}, 30_000);

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(NOW);
});

afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
});

// ---------------------------------------------------------------------------------------------------- servidor simulado

function sale(overrides: Partial<SalesBookRowData>): SalesBookRowData {
  return {
    branchCode: 'CM',
    buyerDocument: '4455667',
    buyerName: 'Juan Pérez',
    complement: null,
    controlCode: '0',
    cuf: '4A1F2B3C4D5E6F7A8B9C0D1E2F3A4B5C',
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
    subtotal: 1850,
    taxBase: 1850,
    taxDebit: 240.5,
    total: 1850,
    zeroRate: 0,
    ...overrides,
  };
}

function salesBook(month: number): SalesBookData {
  if (month !== 9) return { month, rows: [], taxBase: 0, taxDebit: 0, total: 0, valid: 0, voided: 0, year: 2026 };
  return {
    month,
    rows: [
      sale({ complement: '1A' }),
      sale({ row: 2, number: 102, buyerName: 'Ana Rojas', buyerDocument: '1020304', status: 'A', total: 700, taxBase: 700, taxDebit: 91, subtotal: 700 }),
      sale({ row: 3, number: 5, documentSector: 24, branchCode: 'CB', total: 350, taxBase: 350, taxDebit: 45.5, subtotal: 350, date: '2026-09-15T16:00:00' }),
    ],
    taxBase: 2200,
    taxDebit: 286,
    total: 2200,
    valid: 2,
    voided: 1,
    year: 2026,
  };
}

const PURCHASES: PurchasesBookData = {
  month: 9,
  rows: [
    {
      authorizationCode: 'AUT-00112233445566778899',
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
    },
  ],
  taxBase: 1000,
  taxCredit: 130,
  total: 1000,
  year: 2026,
};

const SUMMARY: TaxSummaryData = {
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
};

const PENDING: PendingReceiptData[] = [{ receiptNumber: 'REC-CM-000010', receivedOn: '2026-09-20', supplier: 'Distribuidora Andina', supplierCode: 'PRV-01', total: 870 }];

type Payload = Record<string, unknown>;

class BooksServer {
  calls: { operation: string; payload: Payload }[] = [];
  failures = new Map<string, WebApiError>();

  payloads(operation: string): Payload[] {
    return this.calls.filter((call) => call.operation === operation).map((call) => call.payload);
  }

  handle(operation: string, payload: Payload): { handled: true; result: unknown } | { handled: false } {
    const known = ['GetSalesBookQuery', 'GetPurchasesBookQuery', 'GetTaxSummaryQuery', 'ExportFiscalBookQuery', 'GetReceiptsWithoutInvoiceQuery', 'RegisterSupplierInvoiceCommand'];
    if (!known.includes(operation)) return { handled: false };
    this.calls.push({ operation, payload });
    const failure = this.failures.get(operation);
    if (failure) {
      this.failures.delete(operation);
      throw failure;
    }
    switch (operation) {
      case 'GetSalesBookQuery':
        return { handled: true, result: salesBook(Number(payload.month)) };
      case 'GetPurchasesBookQuery':
        return { handled: true, result: PURCHASES };
      case 'GetTaxSummaryQuery':
        return { handled: true, result: SUMMARY };
      case 'ExportFiscalBookQuery':
        return { handled: true, result: { fileName: `Libro-${payload.purchases ? 'Compras' : 'Ventas'}-202609.${String(payload.format)}`, contentType: 'text/csv', content: btoa('a;b') } };
      case 'GetReceiptsWithoutInvoiceQuery':
        return { handled: true, result: PENDING };
      default:
        return { handled: true, result: '✔ Factura 555-B de Distribuidora Andina registrada: crédito fiscal Bs 123,50.' };
    }
  }
}

function serve(web: MockWeb, server: BooksServer) {
  const real = web.backend.rpc.call.bind(web.backend.rpc);
  vi.spyOn(web.backend.rpc, 'call').mockImplementation(async (operation, payload, options) => {
    const answer = server.handle(operation, payload as Payload);
    if (answer.handled) return { result: answer.result as never, replayed: false, requestId: options?.requestId ?? 'prueba' };
    return real(operation, payload, options);
  });
}

async function openBooks(role: StaffRole = 'ADMIN', query = '', server = new BooksServer()) {
  const web = await signedInAs(role);
  serve(web, server);
  const view = await renderPanel(<PanelApp registry={REGISTRY} />, { web: web.services, route: `/panel/libros${query}`, path: '/panel/*' });
  return { web, server, location: view.location };
}

async function table(): Promise<HTMLElement> {
  const found = await screen.findByTestId('tabla', undefined, { timeout: 5000 });
  await waitFor(() => expect(within(found).queryAllByRole('row').length).toBeGreaterThan(1));
  return found;
}

function dataRows(tableElement: HTMLElement): HTMLElement[] {
  return within(tableElement)
    .getAllByRole('row')
    .filter((row) => row.hasAttribute('data-row-key'));
}

function mockDownloads(): Blob[] {
  const created: Blob[] = [];
  vi.spyOn(URL, 'createObjectURL').mockImplementation((blob) => {
    created.push(blob as Blob);
    return 'blob:prueba';
  });
  vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
  vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
  return created;
}

// ---------------------------------------------------------------------------------------------------- libros

describe('Libros fiscales · libro de ventas', () => {
  it('pide el mes en curso, muestra los registros con los totales del libro y los de lo filtrado al pie', async () => {
    const { server } = await openBooks();
    expect(await screen.findByRole('heading', { level: 1, name: 'Libros fiscales' }, { timeout: 5000 })).toBeInTheDocument();
    const grid = await table();
    expect(server.payloads('GetSalesBookQuery')).toEqual([{ year: 2026, month: 9 }]);
    expect(dataRows(grid)).toHaveLength(3);
    expect(dataRows(grid)[0]).toHaveTextContent('4455667-1A');
    expect(screen.getByTestId('libro-ventas-totales')).toHaveTextContent('Septiembre 2026: 3 registros · 2 válidas · 1 anuladas · total Bs 2.200,00');
    expect(within(grid).getByTestId('tabla-totales')).toHaveTextContent('Bs 2.900,00Bs 2.900,00Bs 377,00');
    // El resumen del mes está plegado: no se consulta hasta abrirlo.
    expect(server.payloads('GetTaxSummaryQuery')).toEqual([]);
    fireEvent.click(screen.getByRole('button', { name: /Ver resumen del mes/ }));
    expect(await screen.findByTestId('resumen-mes')).toHaveTextContent('IVA a pagarBs 110,50');
  });

  it('filtros en la dirección: mes (vuelve a pedir), estado, tipo y sucursal', async () => {
    const { server, location } = await openBooks();
    const grid = await table();
    fireEvent.change(screen.getByRole('combobox', { name: 'Estado' }), { target: { value: 'A' } });
    await waitFor(() => expect(dataRows(grid)).toHaveLength(1));
    expect(within(grid).getByTestId('tabla-totales')).toHaveTextContent('Bs 700,00');
    fireEvent.change(screen.getByRole('combobox', { name: 'Estado' }), { target: { value: '' } });
    fireEvent.change(screen.getByRole('combobox', { name: 'Tipo' }), { target: { value: 'nota' } });
    await waitFor(() => expect(dataRows(grid)).toHaveLength(1));
    expect(dataRows(grid)[0]).toHaveTextContent('Nota N° 5');
    fireEvent.change(screen.getByRole('combobox', { name: 'Tipo' }), { target: { value: '' } });
    fireEvent.change(screen.getByRole('combobox', { name: 'Sucursal' }), { target: { value: 'CB' } });
    await waitFor(() => expect(dataRows(grid)).toHaveLength(1));
    expect(location()).toBe('/panel/libros?sucursal=CB');
    fireEvent.change(screen.getByRole('combobox', { name: 'Mes' }), { target: { value: '2026-08' } });
    await waitFor(() => expect(server.payloads('GetSalesBookQuery').at(-1)).toEqual({ year: 2026, month: 8 }));
    expect(location()).toBe('/panel/libros?sucursal=CB&mes=2026-08');
    expect(await screen.findByText('No hay facturas ni notas en Agosto 2026')).toBeInTheDocument();
  });

  it('descarga el libro del mes para el SIN (CSV) y en Excel; «Exportar lo filtrado» arma el CSV de la página', async () => {
    const created = mockDownloads();
    const { server } = await openBooks();
    await table();
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'CSV para el SIN' }));
    });
    expect(server.payloads('ExportFiscalBookQuery')).toEqual([{ year: 2026, month: 9, purchases: false, format: 'csv' }]);
    expect(await screen.findByText('Se descargó Libro-Ventas-202609.csv.')).toBeInTheDocument();
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Excel' }));
    });
    expect(server.payloads('ExportFiscalBookQuery')[1]).toEqual({ year: 2026, month: 9, purchases: false, format: 'xlsx' });
    fireEvent.click(screen.getByRole('button', { name: 'Exportar lo filtrado' }));
    const text = await created[created.length - 1].text();
    const lines = text.replace(/^﻿/, '').trim().split('\r\n');
    expect(lines[0]).toBe(
      '"N°";"Fecha";"Tipo";"Número";"CUF";"NIT / CI";"Nombre o razón social";"Importe total";"Descuentos";"Base para débito fiscal";"Débito fiscal";"Estado";"Código de control";"Sucursal"',
    );
    expect(lines).toHaveLength(4);
  });

  it('si el libro no se puede leer, el error con «Reintentar»', async () => {
    const server = new BooksServer();
    server.failures.set('GetSalesBookQuery', new WebApiError({ kind: 'network', message: 'Sin conexión' }));
    await openBooks('ADMIN', '', server);
    const error = await screen.findByTestId('estado-error', undefined, { timeout: 5000 });
    fireEvent.click(within(error).getByRole('button', { name: 'Reintentar' }));
    await table();
  });
});

describe('Libros fiscales · compras, resumen y recepciones', () => {
  it('libro de compras con sus totales y descarga (compras)', async () => {
    mockDownloads();
    const { server } = await openBooks('ADMIN', '?pestana=compras');
    const grid = await table();
    expect(server.payloads('GetPurchasesBookQuery')).toEqual([{ year: 2026, month: 9 }]);
    expect(dataRows(grid)[0]).toHaveTextContent('Distribuidora Andina');
    expect(screen.getByTestId('libro-compras-totales')).toHaveTextContent('1 facturas · total Bs 1.000,00 · base Bs 1.000,00 · crédito fiscal Bs 130,00');
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'CSV para el SIN' }));
    });
    expect(server.payloads('ExportFiscalBookQuery')).toEqual([{ year: 2026, month: 9, purchases: true, format: 'csv' }]);
  });

  it('resumen IVA / IT con la explicación de cada línea', async () => {
    const { server } = await openBooks('ADMIN', '?pestana=resumen');
    const summary = await screen.findByTestId('resumen-iva', undefined, { timeout: 5000 });
    expect(server.payloads('GetTaxSummaryQuery')).toEqual([{ year: 2026, month: 9 }]);
    expect(summary).toHaveTextContent('IVA a pagar');
    expect(summary).toHaveTextContent('Bs 110,50');
    expect(summary).toHaveTextContent('3 % de la base del IT.');
  });

  it('recepciones sin factura: registrar la factura del proveedor con el importe propuesto y la forma exacta del contrato', async () => {
    const { server } = await openBooks('ADMIN', '?pestana=recepciones');
    const grid = await table();
    expect(dataRows(grid)[0]).toHaveTextContent('REC-CM-000010');
    fireEvent.click(within(dataRows(grid)[0]).getByRole('button', { name: /^Acciones de / }));
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Registrar factura' }));
    const dialog = await screen.findByRole('dialog', { name: 'Factura del proveedor · recepción REC-CM-000010' });
    expect(within(dialog).getByLabelText(/^Importe total/)).toHaveValue('1.000,00');
    expect(within(dialog).getByLabelText(/^Fecha de la factura/)).toHaveValue('2026-09-20');
    fireEvent.click(within(dialog).getByRole('button', { name: 'Registrar factura' }));
    expect(await within(dialog).findByText('Escriba el número de la factura del proveedor.')).toBeInTheDocument();
    fireEvent.change(within(dialog).getByLabelText(/^N° de factura/), { target: { value: '555-B' } });
    fireEvent.change(within(dialog).getByLabelText(/^Código de autorización o CUF/), { target: { value: 'ABC123' } });
    fireEvent.change(within(dialog).getByLabelText(/^Descuentos/), { target: { value: '50' } });
    expect(within(dialog).getByTestId('credito-estimado')).toHaveTextContent('Base para crédito fiscal Bs 950,00 · crédito fiscal IVA 13 % estimado Bs 123,50');
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Registrar factura' }));
    });
    expect(server.payloads('RegisterSupplierInvoiceCommand')).toEqual([
      {
        receiptNumber: 'REC-CM-000010',
        invoiceNumber: '555-B',
        authorizationCode: 'ABC123',
        invoiceDate: '2026-09-20',
        totalAmount: 1000,
        discounts: 50,
        notSubjectToVat: 0,
        purchaseType: 1,
        controlCode: null,
      },
    ]);
    expect(await screen.findByText('Factura del proveedor registrada')).toBeInTheDocument();
  });

  it('el error del servidor al registrar la factura se ve en el diálogo', async () => {
    const server = new BooksServer();
    server.failures.set('RegisterSupplierInvoiceCommand', new WebApiError({ kind: 'domain', status: 422, code: 'purchasing.invoice_duplicate', message: 'La factura 555-B de este proveedor ya está registrada.' }));
    await openBooks('ADMIN', '?pestana=recepciones', server);
    const grid = await table();
    fireEvent.click(within(dataRows(grid)[0]).getByRole('button', { name: /^Acciones de / }));
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Registrar factura' }));
    const dialog = await screen.findByRole('dialog', { name: 'Factura del proveedor · recepción REC-CM-000010' });
    fireEvent.change(within(dialog).getByLabelText(/^N° de factura/), { target: { value: '555-B' } });
    fireEvent.change(within(dialog).getByLabelText(/^Código de autorización o CUF/), { target: { value: 'ABC123' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Registrar factura' }));
    });
    expect(await within(dialog).findByText('La factura 555-B de este proveedor ya está registrada.')).toBeInTheDocument();
  });
});

describe('Libros fiscales · permisos, estadística y tablero', () => {
  it('quien no gestiona compras (consulta) no ve «Recepciones sin factura»', async () => {
    await openBooks('CONSULTA', '?pestana=recepciones');
    await table();
    expect(screen.queryByRole('tab', { name: 'Recepciones sin factura' })).not.toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Libro de ventas' })).toHaveAttribute('aria-selected', 'true');
  });

  it('un rol sin `billing.view` (bodega) ve «No tiene acceso a esta pantalla»', async () => {
    await openBooks('BODEGA');
    expect(await screen.findByTestId('sin-acceso', undefined, { timeout: 5000 })).toHaveTextContent('Falta el permiso');
  });

  it('la estadística «IVA del mes» y los botones del tablero', async () => {
    const web = await signedInAs('GERENCIA');
    const server = new BooksServer();
    serve(web, server);
    await renderPanel(<TaxStat />, { web: web.services });
    const stat = await screen.findByTestId('iva-del-mes');
    await waitFor(() => expect(within(stat).getByRole('link', { name: 'Ver el resumen IVA / IT' })).toHaveAttribute('href', '/panel/libros?pestana=resumen'));
    expect(server.payloads('GetTaxSummaryQuery')).toEqual([{ year: 2026, month: 9 }]);
    expect(stat).toHaveTextContent('Septiembre 2026');
    expect(REGISTRY.problems).toEqual([]);
    expect(dashboardActions(REGISTRY, ['billing.view']).flatMap((group) => group.actions.map((item) => [item.action.label, item.to]))).toEqual([
      ['Libro de ventas del mes', '/panel/libros'],
      ['Resumen IVA e IT del mes', '/panel/libros?pestana=resumen'],
    ]);
    expect(dashboardStats(REGISTRY, ['billing.view']).map((item) => item.stat.title)).toEqual(['IVA del mes']);
  });
});
