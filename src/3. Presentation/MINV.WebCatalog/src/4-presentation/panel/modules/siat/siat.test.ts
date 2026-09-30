// Módulo «Estado del SIAT» · funciones puras: cuentas regresivas, vigencia de CUIS y CUFD, hora fiscal, fechas de los
// campos del navegador, acciones de cada punto según su modo, resumen de modos, filtros, eventos de contingencia manual,
// talonarios y la guía de la transcripción de facturas manuales.

import { describe, expect, it } from 'vitest';
import {
  CUFD_MARGIN_MS,
  EMPTY_BUYER,
  addMinutesLocal,
  buyerErrors,
  buyerPayload,
  cafcOptions,
  cafcUsage,
  countdown,
  eventDeadline,
  eventFilters,
  eventPeriod,
  filterCafcs,
  filterEvents,
  formatFiscalTime,
  isManualEvent,
  isTranscribable,
  laPazLocalNow,
  lineProblem,
  linePayload,
  manualEventOptions,
  modesSummary,
  newLine,
  offlineDocumentsLink,
  parseSerials,
  pointAbilities,
  pointMode,
  toFiscalDateTime,
  validity,
  type CafcData,
  type EventData,
  type PointData,
} from './siat';

const NOW = new Date('2026-09-29T14:00:00Z');
const hours = (value: number) => new Date(NOW.getTime() + value * 3_600_000).toISOString();

function point(overrides: Partial<PointData>): PointData {
  return {
    branchCode: 'CM',
    branchId: 'b-cm',
    branchName: 'Casa matriz',
    code: 1,
    cufdObtainedAt: hours(-4),
    cufdValidUntil: hours(20),
    cuisValidUntil: hours(24 * 200),
    environment: 2,
    id: 'pv-1',
    isClosed: false,
    lastContactAt: hours(-0.1),
    lastError: null,
    mode: 'Online',
    modeSince: hours(-48),
    name: 'Caja principal',
    offlineDocuments: 0,
    openEvent: null,
    pendingDocuments: 0,
    registerCode: 'CAJA01',
    retryAt: null,
    siatBranchCode: 0,
    ...overrides,
  };
}

function event(overrides: Partial<EventData>): EventData {
  return {
    branchCode: 'CM',
    branchId: 'b-cm',
    cafc: null,
    description: 'CORTE DEL SERVICIO DE INTERNET',
    documents: 0,
    endedAt: null,
    eventCode: 1,
    id: 'ev-1',
    kind: 'Offline',
    pointOfSaleCode: 1,
    receptionCode: null,
    registrationDeadline: null,
    startedAt: '2026-09-29T08:00:00',
    status: 'Open',
    transcriptionDeadline: null,
    ...overrides,
  };
}

function cafc(overrides: Partial<CafcData>): CafcData {
  return { branchCode: 'CM', code: 'CAFC-CM-1', documentSector: 1, id: 'c-1', isActive: true, numberFrom: 1, numberTo: 100, used: 3, validUntil: '2026-12-31', ...overrides };
}

describe('Estado del SIAT · tiempos', () => {
  it('cuenta regresiva como el escritorio (días, horas y minutos; vencido)', () => {
    expect(countdown(hours(5.5), NOW)).toBe('vence en 5 h 30 min');
    expect(countdown(hours(72), NOW)).toBe('vence en 3 días');
    expect(countdown(hours(-2), NOW)).toBe('venció hace 2 h 0 min');
    expect(countdown(hours(0.001), NOW)).toBe('vence en 1 min');
  });

  it('vigencia del CUFD: vigente, por vencer (2 h antes) o vencido', () => {
    expect(validity(hours(20), NOW, CUFD_MARGIN_MS)).toBe('ok');
    expect(validity(hours(1), NOW, CUFD_MARGIN_MS)).toBe('soon');
    expect(validity(hours(-1), NOW, CUFD_MARGIN_MS)).toBe('expired');
    expect(validity(null, NOW, CUFD_MARGIN_MS)).toBe('expired');
  });

  it('hora fiscal sin zona tal cual; campos del navegador en la hora de La Paz', () => {
    expect(formatFiscalTime('2026-09-29T08:00:00')).toBe('29/09/2026 08:00');
    expect(formatFiscalTime('2026-12-31')).toBe('31/12/2026');
    expect(laPazLocalNow(NOW)).toBe('2026-09-29T10:00');
    expect(addMinutesLocal('2026-09-29T23:58', 5)).toBe('2026-09-30T00:03');
    expect(toFiscalDateTime('2026-09-29T10:30')).toBe('2026-09-29T10:30:00');
    expect(toFiscalDateTime('29/09/2026')).toBeNull();
  });
});

describe('Estado del SIAT · puntos de venta', () => {
  it('acciones según el modo: en línea (fuera de línea, contingencia), fuera de línea (recuperar, terminar), manual (terminar)', () => {
    expect(pointAbilities(point({ mode: 'Online' }))).toEqual({ online: true, offline: false, manual: false, canEnd: false });
    expect(pointAbilities(point({ mode: 'Offline' }))).toEqual({ online: false, offline: true, manual: false, canEnd: true });
    expect(pointAbilities(point({ mode: 'Recovering' }))).toEqual({ online: false, offline: true, manual: false, canEnd: false });
    expect(pointAbilities(point({ mode: 'ManualContingency' }))).toEqual({ online: false, offline: false, manual: true, canEnd: true });
    expect(pointAbilities(point({ mode: 'Online', isClosed: true }))).toEqual({ online: false, offline: false, manual: false, canEnd: false });
    expect(pointMode(point({ isClosed: true }))).toBe('Closed');
  });

  it('resumen de modos (los cerrados no cuentan) y enlace a sus documentos fuera de línea', () => {
    expect(modesSummary([point({}), point({ id: 'b', mode: 'Offline' }), point({ id: 'c', mode: 'Offline' }), point({ id: 'd', isClosed: true })])).toEqual({
      active: 3,
      notOnline: 2,
      text: '1 en línea · 2 fuera de línea',
    });
    expect(offlineDocumentsLink(point({ branchCode: 'CB', code: 2 }))).toBe('documentos-fiscales?sucursal=CB&punto=CB-2&estado=Offline');
  });
});

describe('Estado del SIAT · eventos y talonarios', () => {
  it('filtros de los eventos (últimos 45 días) y textos del período y del plazo', () => {
    expect(eventFilters('2026-09-29')).toMatchObject({ desde: '2026-08-16', hasta: '2026-09-29' });
    const rows = [event({ id: 'a' }), event({ id: 'b', kind: 'ManualCafc', status: 'Reconciled', branchCode: 'SC' })];
    expect(filterEvents(rows, { ...eventFilters('2026-09-29'), tipo: 'ManualCafc' }).map((row) => row.id)).toEqual(['b']);
    expect(filterEvents(rows, { ...eventFilters('2026-09-29'), sucursal: 'CM', estado: 'Open' }).map((row) => row.id)).toEqual(['a']);
    expect(eventPeriod(event({}))).toBe('29/09/2026 08:00 → abierto');
    expect(eventDeadline(event({ transcriptionDeadline: '2026-10-02T12:00:00' }))).toBe('Transcribir hasta 02/10/2026 12:00');
    expect(eventDeadline(event({ registrationDeadline: '2026-10-01T08:00:00' }))).toBe('Registrar hasta 01/10/2026 08:00');
    expect(isTranscribable(event({ kind: 'ManualCafc', status: 'Closed' }))).toBe(true);
    expect(isTranscribable(event({ kind: 'ManualCafc', status: 'Reconciled' }))).toBe(false);
    expect(isTranscribable(event({ kind: 'Offline' }))).toBe(false);
  });

  it('solo los eventos de contingencia manual (energía, virus o software, hardware) y los talonarios activos de la sucursal', () => {
    expect(isManualEvent('CORTE DE SUMINISTRO DE ENERGÍA ELÉCTRICA')).toBe(true);
    expect(isManualEvent('CORTE DEL SERVICIO DE INTERNET')).toBe(false);
    expect(
      manualEventOptions([
        { catalog: 'EVENTOS_SIGNIFICATIVOS', code: 1, description: 'CORTE DEL SERVICIO DE INTERNET', isCurrent: true },
        { catalog: 'EVENTOS_SIGNIFICATIVOS', code: 7, description: 'FALLA DE HARDWARE', isCurrent: true },
        { catalog: 'EVENTOS_SIGNIFICATIVOS', code: 5, description: 'CORTE DE SUMINISTRO DE ENERGIA ELECTRICA', isCurrent: true },
        { catalog: 'EVENTOS_SIGNIFICATIVOS', code: 6, description: 'VIRUS INFORMATICO', isCurrent: false },
      ]),
    ).toEqual([
      { value: '5', label: '5 · Corte de suministro de energia electrica' },
      { value: '7', label: '7 · Falla de hardware' },
    ]);
    const cafcs = [cafc({}), cafc({ id: 'c-2', code: 'NOTAS', documentSector: 24 }), cafc({ id: 'c-3', code: 'VIEJO', isActive: false }), cafc({ id: 'c-4', code: 'SC', branchCode: 'SC' })];
    expect(cafcOptions(cafcs, 'CM')).toEqual([{ value: 'CAFC-CM-1', label: 'CAFC-CM-1 · N° 1 a 100 · vence 31/12/2026' }]);
    expect(filterCafcs(cafcs, { estado: 'inactive', sucursal: '', sector: '' }).map((row) => row.code)).toEqual(['VIEJO']);
    expect(filterCafcs(cafcs, { estado: '', sucursal: '', sector: '24' }).map((row) => row.code)).toEqual(['NOTAS']);
    expect(cafcUsage(cafc({}))).toBe('3 de 100 usados');
  });
});

describe('Estado del SIAT · transcripción de facturas manuales', () => {
  it('líneas: cantidad, descuento de 0 a 100 % y una serie por unidad', () => {
    const line = newLine({ sku: 'GPU-1', name: 'Tarjeta de video', unit: 'UND' });
    expect(lineProblem(line)).toBeNull();
    expect(lineProblem({ ...line, quantity: 0 })).toBe('Escriba la cantidad de Tarjeta de video.');
    expect(lineProblem({ ...line, discount: 120 })).toBe('El descuento de Tarjeta de video va de 0 a 100 %.');
    expect(lineProblem({ ...line, quantity: 2, serials: 'S1' })).toBe('Escriba una serie por unidad de Tarjeta de video (1 de 2).');
    expect(parseSerials(' S1, S2 ;S3 ')).toEqual(['S1', 'S2', 'S3']);
    expect(parseSerials('  ')).toBeNull();
    expect(linePayload({ ...line, quantity: 2, discount: 5, serials: 'S1, S2' })).toEqual({ sku: 'GPU-1', quantity: 2, discountPercent: 5, serials: ['S1', 'S2'] });
    expect(linePayload(line)).toEqual({ sku: 'GPU-1', quantity: 1, discountPercent: 0, serials: null });
  });

  it('el comprador es obligatorio en la factura manual (nominatividad) y viaja con la forma del contrato', () => {
    expect(buyerErrors(EMPTY_BUYER).documentNumber).toBe('Escriba el documento del comprador tal como figura en la factura manual.');
    expect(buyerPayload({ ...EMPTY_BUYER, documentNumber: '4455667', complement: '1a', name: 'Juan' })).toEqual({
      documentType: 1,
      documentNumber: '4455667',
      complement: '1A',
      name: 'Juan',
      email: null,
      exceptionRequested: false,
    });
  });
});
