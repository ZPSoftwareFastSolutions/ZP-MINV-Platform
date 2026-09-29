// Módulo «Caja» · reglas puras de la factura, las reservas, el turno y el comprobante: banda fiscal, tipos de
// documento del catálogo del SIN, NIT especiales, datos del comprador, lo que dejó el envío al SIN, hora fiscal sin
// correrla, qué reservas se pueden cobrar, arqueo y comprobante (con y sin factura).

import { describe, expect, it } from 'vitest';
import { buildProblem, buildStatusLabel, buildSummary, buyerFromBuild, filterBuilds, normalizeBuildNumber, sellableBuilds, BUILD_PICKER_FILTERS } from './builds';
import {
  EMPTY_BUYER,
  SPECIAL_NITS,
  applyLookup,
  applySpecialNit,
  buyerErrors,
  buyerPayload,
  documentTypeOptions,
  fiscalAfterDispatch,
  fiscalBand,
  fiscalHeadline,
  fiscalSummary,
  formatFiscalTime,
  initialFiscal,
  lookupMessages,
  lookupPayload,
  nitCheckMessage,
  nitToVerify,
  withDocumentType,
} from './fiscal';
import { receiptFromModel, receiptFromSale } from './receipt';
import { cashDifference, closedNotice, sessionSummary, suggestedRegister } from './session';
import type { BuildRowData, FiscalRowData, FiscalStateData, PosStateData, PrintModelData, SaleResultData } from './types';

const fiscalState = (overrides: Partial<FiscalStateData>): FiscalStateData => ({
  billingEnabled: true,
  ready: true,
  message: '✔ Punto de venta 1 listo para facturar',
  mode: 'Online',
  pointOfSaleCode: 1,
  documentTypes: [],
  pendingHomologation: 0,
  ...overrides,
});

const sale: SaleResultData = {
  invoiceNumber: 'F-CB-000124',
  orderNumber: 'PV-CB-000124',
  issuedAt: '2026-09-29T14:30:00Z',
  total: 4010,
  tax: 521.3,
  change: 90,
  customer: 'Consumidor final',
  paymentMethod: 'Efectivo',
  lines: [{ description: 'Celular Samsung A55', quantity: 1, unitPrice: 3200, amount: 3200, serialsText: 'IMEI: 356938035643809', warrantyUntil: '2027-09-29' }],
  fiscalDocumentId: 'doc-1',
  fiscalNumber: 45,
  cuf: 'CUF-ORIGINAL',
  fiscalStatus: 'Pending',
};

const row = (overrides: Partial<FiscalRowData>): FiscalRowData => ({
  branchCode: 'CB',
  branchId: 'b',
  buyerDocument: '1020304050',
  buyerName: 'ACME SRL',
  canCreditNote: true,
  canRevert: false,
  canVoid: true,
  cuf: 'CUF-FINAL',
  emissionType: 1,
  id: 'doc-1',
  isReverted: false,
  issuedAt: '2026-09-29T10:30:00.000',
  kind: 'Invoice',
  lastSiatCode: 908,
  number: 45,
  pointOfSaleCode: 1,
  saleNumber: 'F-CB-000124',
  status: 'Valid',
  total: 4010,
  voidDeadline: '2026-10-09T23:59:59',
  ...overrides,
});

function build(overrides: Partial<BuildRowData>): BuildRowData {
  return {
    id: 'b1',
    number: 'RES-CB-000007',
    name: 'Compra de Ana',
    branchCode: 'CB',
    customer: null,
    status: 'Reserved',
    validUntil: '2026-10-05',
    isExpired: false,
    total: 3650,
    items: 2,
    isCompatible: true,
    createdAt: '2026-09-28T14:00:00Z',
    invoiceNumber: null,
    quotedWithErrors: false,
    channel: 'Web',
    contactName: 'Ana Pérez',
    contactPhone: '71234567',
    contactEmail: 'ana@correo.example',
    reservedUntil: '2026-09-30T14:00:00Z',
    publishedToWeb: false,
    cancelReason: null,
    notes: null,
    reserved: 2,
    kind: 'Cart',
    buyerDocumentType: null,
    buyerDocumentNumber: null,
    buyerComplement: null,
    buyerName: null,
    ...overrides,
  };
}

describe('caja · factura del SIN', () => {
  it('banda fiscal como el escritorio: en línea, fuera de línea, contingencia, no lista y sin facturación', () => {
    expect(fiscalBand(fiscalState({}))).toEqual({ tone: 'success', title: 'Facturación en línea · punto de venta 1', detail: 'Punto de venta 1 listo para facturar' });
    expect(fiscalBand(fiscalState({ mode: 'Offline' }))?.title).toBe('Fuera de línea: las facturas se envían solas al volver la conexión');
    expect(fiscalBand(fiscalState({ ready: false, mode: 'ManualContingency' }))).toMatchObject({ tone: 'danger', title: 'Contingencia manual: use el talonario CAFC' });
    expect(fiscalBand(fiscalState({ ready: false, mode: null, message: 'Abra un turno de caja para facturar.' }))).toMatchObject({ tone: 'danger', detail: 'Abra un turno de caja para facturar.' });
    expect(fiscalBand(fiscalState({ pendingHomologation: 3 }))?.tone).toBe('warning');
    expect(fiscalBand(fiscalState({ billingEnabled: false }))).toBeNull();
    expect(fiscalBand(undefined)).toBeNull();
  });

  it('tipos de documento del catálogo del SIN (los vigentes) o, sin sincronizar, los 5 del SIN', () => {
    expect(documentTypeOptions(undefined).map((option) => option.short)).toEqual(['CI', 'CEX', 'PAS', 'OD', 'NIT']);
    expect(
      documentTypeOptions([
        { catalog: 'TIPO_DOCUMENTO_IDENTIDAD', code: 5, description: 'NIT', isCurrent: true },
        { catalog: 'TIPO_DOCUMENTO_IDENTIDAD', code: 1, description: 'CI', isCurrent: true },
        { catalog: 'TIPO_DOCUMENTO_IDENTIDAD', code: 2, description: 'CEX', isCurrent: false },
        { catalog: 'TIPO_DOCUMENTO_IDENTIDAD', code: 9, description: 'Documento nuevo', isCurrent: true },
      ]),
    ).toEqual([
      { value: '1', short: 'CI', label: 'CI · Cédula de identidad' },
      { value: '5', short: 'NIT', label: 'NIT · Número de identificación tributaria' },
      { value: '9', short: '9', label: 'Documento nuevo' },
    ]);
  });

  it('datos del comprador: complemento solo con CI, NIT especiales y el pedido para el comando', () => {
    const ci = { ...EMPTY_BUYER, documentNumber: '4567890', complement: '1a', name: ' Juan ' };
    expect(buyerErrors(ci, true)).toEqual({});
    expect(buyerPayload(ci)).toEqual({ documentType: 1, documentNumber: '4567890', complement: '1A', name: 'Juan', email: null, exceptionRequested: false });
    expect(withDocumentType(ci, 5)).toMatchObject({ documentType: 5, complement: '' });
    expect(buyerErrors({ ...ci, documentType: 3, documentNumber: 'AB-123' }, true)).toEqual({ complement: 'El complemento se usa solo con la cédula de identidad.' });
    expect(buyerErrors({ ...ci, complement: '123456' }, true).complement).toBe('El complemento tiene como máximo 5 caracteres.');
    expect(buyerErrors({ ...ci, email: 'no-es-correo' }, false).email).toMatch(/^Escriba un correo válido/);
    expect(buyerPayload(EMPTY_BUYER)).toBeNull();
    const minor = applySpecialNit(ci, SPECIAL_NITS[0]);
    expect(minor).toMatchObject({ documentType: 5, documentNumber: '99003', complement: '', name: 'VENTAS MENORES DEL DIA' });
    expect(lookupPayload(minor)).toBeNull();
    expect(nitToVerify(minor)).toBeNull();
    expect(nitToVerify({ ...EMPTY_BUYER, documentType: 5, documentNumber: '1020304050' })).toBe(1020304050);
    expect(nitToVerify(ci)).toBeNull();
    expect(lookupPayload(ci)).toEqual({ documentType: 1, documentNumber: '4567890', complement: '1A' });
  });

  it('«ya compró antes»: autocompleta sin pisar lo escrito y avisa del NIT; resultado de verificar el NIT', () => {
    const found = { found: true, customerCode: 'CLI-0042', name: 'ANA PEREZ', email: 'ana@correo.example', documentType: 5, documentNumber: '1020304050', complement: null, nitValid: false };
    expect(applyLookup({ ...EMPTY_BUYER, name: 'Ya escrito' }, found)).toMatchObject({ name: 'Ya escrito', email: 'ana@correo.example' });
    expect(lookupMessages(found)).toEqual([
      { tone: 'info', text: 'Cliente CLI-0042: ya compró antes.' },
      { tone: 'danger', text: 'La última verificación dio NIT no válido.' },
    ]);
    expect(nitCheckMessage({ nit: 1020304050, isValid: true, siatCode: 986, description: '✔ NIT ACTIVO', checked: true })).toEqual({ tone: 'success', text: 'NIT activo en el Padrón · NIT ACTIVO' });
    expect(nitCheckMessage({ nit: 1, isValid: false, siatCode: 994, description: 'NIT INEXISTENTE', checked: true }).tone).toBe('danger');
  });

  it('después de cobrar: se envía al SIN y se muestra el documento DEFINITIVO (o se envía solo si no se pudo)', () => {
    expect(initialFiscal(sale, true)).toMatchObject({ phase: 'sending' });
    expect(initialFiscal(sale, false)).toMatchObject({ phase: 'queued' });
    expect(initialFiscal({ ...sale, fiscalDocumentId: null }, true)).toBeNull();
    const offline = fiscalAfterDispatch(sale, {
      ok: true,
      result: { sent: 1, valid: 0, rejected: 0, wentOffline: 1, documents: [row({ id: 'doc-2', status: 'Offline', number: 46, cuf: 'CUF-OFF' })], messages: ['⚠ Sin conexión'] },
    });
    expect(offline).toMatchObject({ phase: 'done', notice: 'Sin comunicación con el SIN: la caja pasó a fuera de línea y la factura se envía sola al volver la conexión.', messages: ['Sin conexión'] });
    expect(fiscalSummary(sale, offline)).toEqual({ documentId: 'doc-2', number: 46, cuf: 'CUF-OFF', status: 'Offline', buyer: 'ACME SRL · 1020304050' });
    expect(fiscalHeadline(fiscalSummary(sale, offline))).toBe('Factura N° 46 emitida fuera de línea');
    const failed = fiscalAfterDispatch(sale, { ok: false, message: 'Sin conexión con el servidor.' });
    expect(failed.notice).toBe('La factura quedó pendiente de envío y se envía sola. Sin conexión con el servidor.');
    expect(fiscalSummary(sale, failed)).toMatchObject({ documentId: 'doc-1', number: 45, cuf: 'CUF-ORIGINAL', status: 'Pending' });
    expect(fiscalHeadline({ documentId: 'x', number: 45, cuf: null, status: 'Rejected', buyer: null })).toBe('Factura N° 45 rechazada por el SIN');
  });

  it('la hora fiscal (sin zona, hora de Bolivia) se muestra tal cual; con zona, en la hora de La Paz', () => {
    expect(formatFiscalTime('2026-09-29T10:32:05.123')).toBe('29/09/2026 10:32:05');
    expect(formatFiscalTime('2026-09-29T14:32:05Z')).toBe('29/09/2026 10:32');
    expect(formatFiscalTime(null)).toBe('—');
  });
});

describe('caja · reservas, turno y comprobante', () => {
  it('solo se cobran cotizaciones y reservas vigentes, con el motivo en palabras', () => {
    expect(buildProblem(build({}))).toBeNull();
    expect(buildProblem(build({ status: 'Quoted', kind: 'Build', number: 'ARM-CB-000003' }))).toBeNull();
    expect(buildProblem(build({ status: 'Sold', invoiceNumber: 'F-CB-000100' }))).toBe('La reserva RES-CB-000007 ya se vendió (venta F-CB-000100).');
    expect(buildProblem(build({ status: 'Cancelled', cancelReason: 'Venció' }))).toBe('La reserva RES-CB-000007 está anulada o liberada: Venció.');
    expect(buildProblem(build({ isExpired: true }))).toMatch(/^La reserva RES-CB-000007 venció/);
    expect(buildProblem(build({ status: 'Draft', kind: 'Build', number: 'ARM-CB-000004' }))).toBe('El armado ARM-CB-000004 es un borrador: cotícela en el Armador de PC antes de cobrarla.');
    expect(buildStatusLabel(build({ isExpired: true }))).toBe('Vencida');
    expect(normalizeBuildNumber(' res-cb-000007 ')).toBe('RES-CB-000007');
    expect(buildSummary(build({}))).toBe(
      'Reserva de la tienda web vigente hasta el 30/09/2026 10:00: al cobrar se consume la reserva (el stock reservado sale una sola vez) · precios congelados · Ana Pérez',
    );
  });

  it('precarga los datos para la factura que dejó quien reservó', () => {
    expect(buyerFromBuild(build({}))).toBeNull();
    expect(buyerFromBuild(build({ buyerDocumentType: 5, buyerDocumentNumber: '1020304050', buyerName: 'ANA PEREZ SRL' }))).toEqual({
      documentType: 5,
      documentNumber: '1020304050',
      complement: '',
      name: 'ANA PEREZ SRL',
      email: 'ana@correo.example',
      exceptionRequested: false,
    });
  });

  it('la lista para elegir: vigentes (las que vencen antes primero) y filtros por tipo, estado, canal y texto', () => {
    const rows = [
      build({ number: 'RES-CB-000001', reservedUntil: '2026-09-30T20:00:00Z' }),
      build({ number: 'ARM-CB-000002', kind: 'Build', status: 'Quoted', channel: 'Desktop', reservedUntil: null, validUntil: '2026-09-29', customer: 'Carlos Rojas' }),
      build({ number: 'RES-CB-000003', status: 'Sold' }),
      build({ number: 'RES-CB-000004', isExpired: true }),
    ];
    const sellable = sellableBuilds(rows);
    expect(sellable.map((item) => item.number)).toEqual(['ARM-CB-000002', 'RES-CB-000001']);
    expect(filterBuilds(sellable, { ...BUILD_PICKER_FILTERS, tipo: 'Cart' }).map((item) => item.number)).toEqual(['RES-CB-000001']);
    expect(filterBuilds(sellable, { ...BUILD_PICKER_FILTERS, canal: 'Desktop' }).map((item) => item.number)).toEqual(['ARM-CB-000002']);
    expect(filterBuilds(sellable, { ...BUILD_PICKER_FILTERS, q: 'carlos' }).map((item) => item.number)).toEqual(['ARM-CB-000002']);
    expect(filterBuilds(sellable, { ...BUILD_PICKER_FILTERS, q: '71234567' })).toHaveLength(2);
  });

  it('turno: resumen, caja sugerida y arqueo (sobrante, faltante, exacto)', () => {
    const session = { id: 's1', registerCode: 'CAJA-CB-01', registerName: 'Caja 1', openedAt: '2026-09-29T12:05:00Z', openingCash: 500, tickets: 12, sales: 3450, cashSales: 750, expectedCash: 1250 };
    expect(sessionSummary(session)).toBe('Abierta a las 08:05 · 12 ventas por Bs 3.450,00 · efectivo esperado Bs 1.250,00');
    const state = { registers: [{ code: 'CAJA-CB-01', name: 'Caja 1', opensCashDrawer: false }, { code: 'CAJA-CB-02', name: 'Caja 2', opensCashDrawer: false }], suggestedRegister: 'CAJA-CB-02', session: null } as unknown as PosStateData;
    expect(suggestedRegister(state)).toBe('CAJA-CB-02');
    expect(suggestedRegister({ ...state, suggestedRegister: 'NO-EXISTE' })).toBe('CAJA-CB-01');
    expect(suggestedRegister(undefined)).toBe('');
    expect(cashDifference(1200, 1250)).toMatchObject({ kind: 'faltante', amount: 50, text: 'Faltante de Bs 50,00.', tone: 'danger' });
    expect(cashDifference(1300.5, 1250)).toMatchObject({ kind: 'sobrante', amount: 50.5, tone: 'warning' });
    expect(cashDifference(1250, 1250)).toMatchObject({ kind: 'exacto', text: 'Arqueo exacto: el efectivo cuadra.' });
    expect(closedNotice(-50)).toEqual({ tone: 'warning', description: 'Faltante de Bs 50,00.' });
    expect(closedNotice(0).tone).toBe('success');
  });

  it('comprobante sin factura (ticket de la venta) y con factura (lo que arma el servidor, con «SIN VALOR LEGAL» en pruebas)', () => {
    const ticket = receiptFromSale(sale, { companyName: 'Tech Zone Gaming S.R.L.', taxId: '1020304050', branchName: 'Sucursal Cochabamba' } as PosStateData, 'Diego Flores');
    expect(ticket.header).toEqual(['Tech Zone Gaming S.R.L.', 'NIT 1020304050', 'Sucursal Cochabamba']);
    expect(ticket.meta.map((pair) => pair.label)).toEqual(['Venta', 'Pedido', 'Fecha', 'Cliente', 'Cajero', 'Factura del SIN N°']);
    expect(ticket.lines[0]).toMatchObject({ serials: 'IMEI: 356938035643809', warranty: 'Garantía hasta el 29/09/2027' });
    expect(ticket.totals.map((pair) => `${pair.label}: ${pair.value}`)).toEqual(['Total: Bs 4.010,00', 'IVA incluido: Bs 521,30', 'Pago: Efectivo', 'Vuelto: Bs 90,00']);

    const model: PrintModelData = {
      title: 'FACTURA',
      subtitle: '(Con Derecho A Crédito Fiscal)',
      issuerName: 'TECH ZONE GAMING S.R.L.',
      issuerNit: 1020304050,
      branchLabel: 'Sucursal 1',
      pointOfSaleCode: 1,
      address: 'Av. Heroínas 123',
      phone: null,
      municipality: 'Cochabamba',
      number: 46,
      cuf: 'CUF-FINAL',
      issuedAt: '2026-09-29T10:30:00.000',
      buyerName: 'ACME SRL',
      buyerDocument: '1020304050',
      customerCode: 'CLI-0042',
      lines: [{ productCode: 'CEL-SAM-A55', description: 'Celular', unit: 'UND', quantity: 1, unitPrice: 3200, discount: 0, subtotal: 3200, transactionCode: null, serialsText: 'IMEI: 356938035643809', warrantyUntil: null }],
      subtotal: 3200,
      discount: 0,
      total: 3200,
      giftCard: 0,
      amountToPay: 3200,
      taxBase: 3200,
      amountInWords: 'TRES MIL DOSCIENTOS 00/100 BOLIVIANOS',
      paymentMethod: 'Efectivo',
      cashier: 'Diego Flores',
      legends: ['Ley N° 453: el proveedor debe informar las condiciones de la garantía.'],
      qrUrl: 'enlace-de-verificacion',
      isTest: true,
      isVoided: false,
      isOffline: false,
      original: null,
      returnedTotal: null,
      creditDebitAmount: null,
      saleNumber: 'F-CB-000124',
    };
    const fiscalReceipt = receiptFromModel(model);
    expect(fiscalReceipt.warning).toBe('SIN VALOR LEGAL · ambiente de pruebas del SIN');
    expect(fiscalReceipt.header).toEqual(['TECH ZONE GAMING S.R.L.', 'NIT 1020304050', 'Sucursal 1', 'Av. Heroínas 123', 'Cochabamba']);
    expect(fiscalReceipt.meta.find((pair) => pair.label === 'Fecha de emisión')?.value).toBe('29/09/2026 10:30:00');
    expect(fiscalReceipt.lines[0].description).toBe('CEL-SAM-A55 · Celular');
    expect(fiscalReceipt.totals.find((pair) => pair.strong)).toEqual({ label: 'Monto a pagar', value: 'Bs 3.200,00', strong: true });
    expect(fiscalReceipt.notes).toEqual([
      'Son: TRES MIL DOSCIENTOS 00/100 BOLIVIANOS',
      'Ley N° 453: el proveedor debe informar las condiciones de la garantía.',
      'Verifique esta factura en el sitio del SIN: enlace-de-verificacion',
    ]);
  });
});
