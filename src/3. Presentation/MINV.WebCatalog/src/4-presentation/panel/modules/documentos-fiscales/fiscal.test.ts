// Módulo «Documentos fiscales» · funciones puras: hora fiscal sin zona, rango del servidor, pedido de la lista, filtros de
// la página, documento vigente de una venta, resumen del período, motivos de anulación, datos del comprador al re-emitir
// y el XML con sangría.

import { describe, expect, it } from 'vitest';
import {
  ALL_DATES_FROM,
  applySpecialNit,
  buyerErrors,
  buyerFromDetail,
  buyerPayload,
  currentDocumentOfSale,
  deadlineText,
  documentFilters,
  documentTypeFromXml,
  documentTypeOptions,
  filterItems,
  fiscalDay,
  formatFiscalDate,
  formatFiscalTime,
  isDeliverable,
  isReissuable,
  linesTotals,
  listPayload,
  periodSummary,
  pointOptions,
  preferredReason,
  prettyXml,
  rejectionText,
  returnLink,
  saleLink,
  sentenceCase,
  serverRange,
  shortCuf,
  splitDocument,
  statusLabel,
  toItems,
  voidReasonOptions,
  withDocumentType,
  EMPTY_BUYER,
  type CatalogItemData,
  type DocumentDetailData,
  type DocumentRecord,
} from './fiscal';

function doc(overrides: Partial<DocumentRecord>): DocumentRecord {
  return {
    id: 'doc-1',
    kind: 'Invoice',
    number: 101,
    cuf: '4A1F2B3C4D5E6F7A8B9C0D1E2F3A4B5C6D7E8F9A',
    issuedAt: '2026-09-29T09:15:00.000',
    branchId: 'b-cm',
    branchCode: 'CM',
    pointOfSaleCode: 1,
    buyerName: 'Juan Pérez',
    buyerDocument: '4455667',
    total: 1850,
    status: 'Valid',
    isReverted: false,
    emissionType: 1,
    saleNumber: 'F-CM-000101',
    lastSiatCode: 908,
    canVoid: true,
    canRevert: false,
    canCreditNote: true,
    voidDeadline: '2026-10-09T23:59:59',
    ...overrides,
  };
}

const REASONS: CatalogItemData[] = [
  { catalog: 'MOTIVOS_ANULACION', code: 3, description: 'DATOS DE EMISION INCORRECTOS', isCurrent: true },
  { catalog: 'MOTIVOS_ANULACION', code: 1, description: 'FACTURA MAL EMITIDA', isCurrent: true },
  { catalog: 'MOTIVOS_ANULACION', code: 2, description: 'NOTA DE CRÉDITO-DÉBITO MAL EMITIDA', isCurrent: true },
  { catalog: 'MOTIVOS_ANULACION', code: 9, description: 'MOTIVO RETIRADO', isCurrent: false },
];

describe('Documentos fiscales · textos y hora fiscal', () => {
  it('la hora fiscal (sin zona) se muestra tal cual; con zona, en la hora de La Paz', () => {
    expect(formatFiscalTime('2026-09-29T10:32:05.123')).toBe('29/09/2026 10:32:05');
    expect(formatFiscalTime('2026-09-29T10:32:05', false)).toBe('29/09/2026 10:32');
    expect(formatFiscalTime('2026-09-29T14:30:00Z')).toBe('29/09/2026 10:30');
    expect(formatFiscalTime('')).toBe('—');
    expect(formatFiscalDate('2026-10-09T23:59:59')).toBe('09/10/2026');
    expect(fiscalDay('2026-09-29T23:59:59')).toBe('2026-09-29');
  });

  it('estados, CUF abreviado, frases del catálogo y plazo de anulación', () => {
    expect(statusLabel(doc({ status: 'Valid', isReverted: true }))).toBe('Válida (anulación revertida)');
    expect(statusLabel(doc({ status: 'PackageRejected' }))).toBe('Observada');
    expect(shortCuf('4A1F2B3C4D5E6F7A8B9C0D1E2F3A4B5C6D7E8F9A')).toBe('4A1F2B3C…7E8F9A');
    expect(sentenceCase('FACTURA MAL EMITIDA')).toBe('Factura mal emitida');
    expect(deadlineText(doc({}))).toBe('Plazo de anulación y reversión: hasta el 09/10/2026 (día 9 del mes siguiente).');
    expect(deadlineText(doc({ status: 'Rejected' }))).toBeNull();
    expect(isReissuable(doc({ status: 'Rejected' }))).toBe(true);
    expect(isReissuable(doc({ status: 'Valid' }))).toBe(false);
    expect(isDeliverable(doc({ status: 'NoResponse' }))).toBe(false);
    expect(isDeliverable(doc({ status: 'Offline' }))).toBe(true);
  });
});

describe('Documentos fiscales · lista', () => {
  it('rango del servidor: por defecto los últimos 30 días; «Todas» = desde el principio hasta hoy', () => {
    const filters = documentFilters('2026-09-29');
    expect([filters.desde, filters.hasta]).toEqual(['2026-08-31', '2026-09-29']);
    expect(serverRange({ from: null, to: null }, '2026-09-29')).toEqual({ from: ALL_DATES_FROM, to: '2026-09-29' });
    expect(serverRange({ from: '2026-10-05', to: null }, '2026-09-29')).toEqual({ from: '2026-10-05', to: '2026-10-05' });
    expect(serverRange({ from: '2026-09-30', to: '2026-09-01' }, '2026-09-29')).toBeNull();
  });

  it('el pedido lleva tipo, estado y búsqueda (un valor raro de la dirección no se envía)', () => {
    const filters = { ...documentFilters('2026-09-29'), estado: 'Rejected', tipo: 'Invoice', q: ' 4455667 ' };
    expect(listPayload(filters, { from: '2026-09-01', to: '2026-09-29' })).toEqual({ from: '2026-09-01', to: '2026-09-29', status: 'Rejected', kind: 'Invoice', search: '4455667' });
    expect(listPayload({ ...filters, estado: 'Inventado', tipo: 'otro', q: '' }, { from: '2026-09-01', to: '2026-09-29' })).toEqual({
      from: '2026-09-01',
      to: '2026-09-29',
      status: null,
      kind: null,
      search: null,
    });
  });

  it('filtra en la página por sucursal, punto de venta, tipo de emisión y venta', () => {
    const items = toItems([
      doc({ id: 'a', branchCode: 'CM', pointOfSaleCode: 1 }),
      doc({ id: 'b', branchCode: 'CM', pointOfSaleCode: 2, emissionType: 2, saleNumber: 'F-CM-000102' }),
      doc({ id: 'c', branchCode: 'CB', pointOfSaleCode: 1, saleNumber: null }),
    ]);
    const base = documentFilters('2026-09-29');
    expect(filterItems(items, { ...base, sucursal: 'CM' }).map((item) => item.key)).toEqual(['a', 'b']);
    expect(filterItems(items, { ...base, punto: 'CM-2' }).map((item) => item.key)).toEqual(['b']);
    expect(filterItems(items, { ...base, emision: '2' }).map((item) => item.key)).toEqual(['b']);
    expect(filterItems(items, { ...base, venta: ' f-cm-000102 ' }).map((item) => item.key)).toEqual(['b']);
    expect(pointOptions(items, 'CM')).toEqual([
      { value: 'CM-1', label: 'CM · punto de venta 1' },
      { value: 'CM-2', label: 'CM · punto de venta 2' },
    ]);
  });

  it('el documento vigente de una venta es la última factura emitida que no se descartó', () => {
    const rows = [
      doc({ id: 'nota', kind: 'CreditDebitNote', issuedAt: '2026-09-29T11:00:00' }),
      doc({ id: 'nueva', status: 'Discarded', issuedAt: '2026-09-29T10:00:00' }),
      doc({ id: 'vigente', status: 'Valid', issuedAt: '2026-09-29T09:30:00' }),
      doc({ id: 'vieja', status: 'Rejected', issuedAt: '2026-09-29T09:00:00' }),
    ];
    expect(currentDocumentOfSale(rows, 'f-cm-000101')?.id).toBe('vigente');
    expect(currentDocumentOfSale(rows, 'F-CM-999999')).toBeNull();
  });

  it('resumen del período: documentos, facturado válido, pendientes y anulados', () => {
    const summary = periodSummary([
      doc({ status: 'Valid', total: 100 }),
      doc({ status: 'Valid', total: 50, kind: 'CreditDebitNote' }),
      doc({ status: 'Offline', emissionType: 2, total: 30 }),
      doc({ status: 'Pending', total: 20 }),
      doc({ status: 'Voided', total: 10 }),
      doc({ status: 'Rejected', total: 5 }),
    ]);
    expect(summary).toEqual({ documents: 6, invoices: 5, notes: 1, validInvoices: 1, validTotal: 100, waiting: 2, waitingOffline: 1, bad: 2, voided: 1 });
  });

  it('enlaces a «Ventas»: la venta de ese día y su devolución', () => {
    expect(saleLink(doc({}))).toBe('ventas?q=F-CM-000101&desde=2026-09-29&hasta=2026-09-29');
    expect(saleLink(doc({ saleNumber: null }))).toBeNull();
    expect(returnLink('F-CM-000101')).toBe('ventas?devolver=F-CM-000101');
  });
});

describe('Documentos fiscales · anulación', () => {
  it('motivos vigentes ordenados y el propuesto según el tipo de documento', () => {
    expect(voidReasonOptions(REASONS)).toEqual([
      { value: '1', label: '1 · Factura mal emitida' },
      { value: '2', label: '2 · Nota de crédito-débito mal emitida' },
      { value: '3', label: '3 · Datos de emision incorrectos' },
    ]);
    expect(preferredReason(REASONS, 'Invoice')).toBe('1');
    expect(preferredReason(REASONS, 'CreditDebitNote')).toBe('2');
    expect(preferredReason([REASONS[0]], 'Invoice')).toBe('3');
    expect(preferredReason([], 'Invoice')).toBe('');
  });
});

describe('Documentos fiscales · detalle y re-emisión', () => {
  const detail = {
    row: doc({ status: 'Rejected', buyerDocument: '5115889-1A', kind: 'Invoice', total: 90 }),
    xml: '<factura><cabecera><codigoTipoDocumentoIdentidad>1</codigoTipoDocumentoIdentidad><nitEmisor>123</nitEmisor></cabecera></factura>',
    buyerEmail: 'juan@correo.example',
    events: [
      { occurredAt: '2026-09-29T13:00:00Z', action: 'Sent', siatCode: null, description: null, receptionCode: null, messages: null, user: null },
      { occurredAt: '2026-09-29T13:01:00Z', action: 'Rejected', siatCode: 902, description: null, receptionCode: null, messages: 'NIT inexistente', user: null },
    ],
    lines: [
      { lineNumber: 1, productCode: 'A', description: 'A', quantity: 1, sinUnitCode: 58, unit: 'UNIDAD', unitPrice: 60, discount: 0, subtotal: 60, activityCode: '461000', sinProductCode: 1, transactionCode: null, serialsText: null },
      { lineNumber: 2, productCode: 'B', description: 'B', quantity: 1, sinUnitCode: 58, unit: 'UNIDAD', unitPrice: 40, discount: 0, subtotal: 40, activityCode: '461000', sinProductCode: 1, transactionCode: null, serialsText: null },
    ],
  } satisfies Pick<DocumentDetailData, 'row' | 'xml' | 'buyerEmail' | 'events' | 'lines'>;

  it('comprador del documento: tipo leído del XML, número y complemento separados', () => {
    expect(documentTypeFromXml(detail.xml)).toBe(1);
    expect(documentTypeFromXml('')).toBeNull();
    expect(splitDocument('5115889-1A')).toEqual({ number: '5115889', complement: '1A' });
    expect(buyerFromDetail(detail)).toEqual({ documentType: 1, documentNumber: '5115889', complement: '1A', name: 'Juan Pérez', email: 'juan@correo.example', exceptionRequested: false });
  });

  it('mensajes del último rechazo y totales de las líneas', () => {
    expect(rejectionText(detail)).toBe('NIT inexistente');
    expect(rejectionText({ ...detail, row: doc({ status: 'Valid' }) })).toBeNull();
    expect(linesTotals(detail)).toEqual({ subtotal: 100, discount: 10 });
  });

  it('guía del comprador como el servidor: solo dígitos con CI o NIT, complemento solo con CI, correo válido', () => {
    expect(buyerErrors({ ...EMPTY_BUYER, documentNumber: '' }).documentNumber).toMatch(/^Escriba el documento/);
    expect(buyerErrors({ ...EMPTY_BUYER, documentNumber: '44.55' }).documentNumber).toBe('El CI lleva solo números (sin puntos, guiones ni espacios).');
    expect(buyerErrors({ ...EMPTY_BUYER, documentType: 3, documentNumber: 'AB123', complement: '1A' }).complement).toBe('El complemento se usa solo con la cédula de identidad.');
    expect(buyerErrors({ ...EMPTY_BUYER, documentNumber: '123', email: 'no-es-correo' }).email).toMatch(/^Escriba un correo válido/);
    expect(buyerErrors({ ...EMPTY_BUYER, documentNumber: '123' })).toEqual({});
  });

  it('el pedido del comprador: complemento solo con CI, excepción solo con NIT, vacíos como null', () => {
    expect(buyerPayload({ documentType: 1, documentNumber: ' 5115889 ', complement: '1a', name: ' ', email: '', exceptionRequested: true })).toEqual({
      documentType: 1,
      documentNumber: '5115889',
      complement: '1A',
      name: null,
      email: null,
      exceptionRequested: false,
    });
    const nit = applySpecialNit(EMPTY_BUYER, '99003');
    expect(nit).toMatchObject({ documentType: 5, documentNumber: '99003', name: 'VENTAS MENORES DEL DIA' });
    expect(buyerPayload({ ...withDocumentType(nit, 5), exceptionRequested: true })).toMatchObject({ documentType: 5, complement: null, exceptionRequested: true });
    expect(withDocumentType({ ...EMPTY_BUYER, complement: '1A', exceptionRequested: true }, 3)).toMatchObject({ complement: '', exceptionRequested: false });
  });

  it('tipos de documento: los del catálogo vigente o, sin catálogo, los 5 del SIN', () => {
    expect(documentTypeOptions(undefined).map((option) => option.value)).toEqual(['1', '2', '3', '4', '5']);
    expect(documentTypeOptions([{ catalog: 'TIPO_DOCUMENTO_IDENTIDAD', code: 5, description: 'NIT', isCurrent: true }])).toEqual([
      { value: '5', label: 'NIT · Número de identificación tributaria' },
    ]);
  });

  it('el XML se muestra con sangría, sin cambiar su contenido', () => {
    expect(prettyXml('<?xml version="1.0"?><a><b>1</b><c xsi:nil="true"/><d><e>x</e></d></a>')).toBe(
      ['<?xml version="1.0"?>', '<a>', '  <b>1</b>', '  <c xsi:nil="true"/>', '  <d>', '    <e>x</e>', '  </d>', '</a>'].join('\n'),
    );
    expect(prettyXml('  ')).toBe('(Este documento no tiene XML guardado.)');
  });
});
