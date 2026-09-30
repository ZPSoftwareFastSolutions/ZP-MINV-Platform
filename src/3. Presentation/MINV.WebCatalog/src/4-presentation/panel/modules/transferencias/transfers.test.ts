// Módulo «Transferencias» · pruebas de las funciones puras: filtros (lado incluido), resumen, almacenes de origen y
// destino, validaciones y el pedido EXACTO de solicitar, recibir (faltantes y series) y anular.

import { describe, expect, it } from 'vitest';
import {
  TRANSFER_FILTERS,
  addLine,
  cancelTransferPayload,
  createTransferPayload,
  destinationChoices,
  filterTransfers,
  hasTransferProblems,
  originChoices,
  receiptDraftOf,
  receiptProblems,
  receiveTransferPayload,
  serialsProblem,
  takeOf,
  toTransferItems,
  transferProblems,
  transfersSummary,
  type BranchRecord,
  type TechRecord,
  type TransferDraft,
  type TransferLineRecord,
  type TransferRecord,
} from './transfers';

const TODAY = '2026-09-29';

function transfer(overrides: Partial<TransferRecord>): TransferRecord {
  return {
    id: 't1',
    number: 'TR-CM-000001',
    fromBranch: 'Casa matriz',
    fromBranchCode: 'CM',
    fromWarehouse: 'ALMCM',
    toBranch: 'Sucursal Cochabamba',
    toBranchCode: 'CB',
    toWarehouse: 'ALMCB',
    status: 'Pending',
    statusLabel: 'Pendiente',
    requestedAt: '2026-09-20T14:00:00Z',
    dispatchedAt: null,
    receivedAt: null,
    lines: 1,
    quantity: 2,
    value: 0,
    shortage: 0,
    notes: null,
    canDispatch: false,
    canReceive: false,
    canCancel: false,
    ...overrides,
  };
}

const BRANCHES: BranchRecord[] = [
  { id: 'b1', code: 'CM', name: 'Casa matriz', isActive: true, isVisible: true, stockValue: 1, transfersIn: 0, transfersOut: 0, users: 2, warehouses: ['ALMCM'] },
  { id: 'b2', code: 'CB', name: 'Cochabamba', isActive: true, isVisible: false, stockValue: null, transfersIn: 0, transfersOut: 0, users: 1, warehouses: ['ALMCB'] },
  { id: 'b3', code: 'SC', name: 'Santa Cruz', isActive: false, isVisible: true, stockValue: 1, transfersIn: 0, transfersOut: 0, users: 0, warehouses: ['ALMSC'] },
];

function line(overrides: Partial<TransferLineRecord>): TransferLineRecord {
  return { lineId: 'l1', sku: 'MOU-01', name: 'Mouse', unit: 'UND', quantity: 5, unitCost: 80, received: 0, shortage: 0, shortageReason: null, lots: [], serials: null, ...overrides };
}

describe('Transferencias · lista', () => {
  it('filtra por estado, origen, destino, lo que espera a mis sucursales, fechas y búsqueda', () => {
    const items = toTransferItems([
      transfer({ id: 'a', status: 'Pending', canDispatch: true, notes: 'Urgente' }),
      transfer({ id: 'b', number: 'TR-SC-000002', status: 'Dispatched', fromBranchCode: 'SC', toBranchCode: 'CM', canReceive: true, requestedAt: '2026-09-28T14:00:00Z' }),
    ]);
    expect(items[0].route).toBe('CM → CB');
    const keys = (filters: Partial<typeof TRANSFER_FILTERS>) => filterTransfers(items, { ...TRANSFER_FILTERS, ...filters }).map((item) => item.key);
    expect(keys({ estado: 'Dispatched' })).toEqual(['b']);
    expect(keys({ origen: 'SC' })).toEqual(['b']);
    expect(keys({ destino: 'CB' })).toEqual(['a']);
    expect(keys({ pendiente: 'despachar' })).toEqual(['a']);
    expect(keys({ pendiente: 'recibir' })).toEqual(['b']);
    expect(keys({ desde: '2026-09-25' })).toEqual(['b']);
    expect(keys({ q: 'urgente' })).toEqual(['a']);
    expect(takeOf('1000')).toBe(1000);
    expect(takeOf('7')).toBe(300);
  });

  it('resume pendientes, en tránsito, recibidas y faltantes del mes', () => {
    const summary = transfersSummary(
      [
        transfer({ status: 'Pending', canDispatch: true }),
        transfer({ status: 'Pending' }),
        transfer({ status: 'Dispatched', value: 500, canReceive: true }),
        transfer({ status: 'Received', value: 300, shortage: 1, receivedAt: '2026-09-10T15:00:00Z' }),
        transfer({ status: 'Received', value: 999, shortage: 4, receivedAt: '2026-08-31T15:00:00Z' }),
      ],
      TODAY,
    );
    expect(summary).toEqual({
      pending: { count: 2, mine: 1 },
      transit: { count: 1, value: 500, toReceive: 1 },
      receivedThisMonth: { count: 1, value: 300 },
      shortageThisMonth: { quantity: 1, transfers: 1 },
    });
  });

  it('el origen es la sucursal activa (o las visibles) y el destino, las otras sucursales activas', () => {
    expect(originChoices(BRANCHES, 'b1').map((choice) => choice.code)).toEqual(['ALMCM']);
    expect(originChoices(BRANCHES, null).map((choice) => choice.code)).toEqual(['ALMCM']);
    expect(destinationChoices(BRANCHES, 'CM').map((choice) => choice.code)).toEqual(['ALMCB']);
  });
});

describe('Transferencias · solicitar', () => {
  const phone = { sku: 'CEL-01', name: 'Celular', stock: 3, trackSerials: true, serialKind: 'Imei' } as TechRecord;
  const mouse = { sku: 'MOU-01', name: 'Mouse', stock: 10, trackSerials: false, serialKind: 'Serial' } as TechRecord;

  it('valida origen, destino, productos, cantidades y las series (una por unidad)', () => {
    const empty: TransferDraft = { fromWarehouseCode: '', toWarehouseCode: '', notes: '', lines: [] };
    expect(transferProblems(empty)).toMatchObject({
      fromWarehouseCode: 'Elija el almacén de origen.',
      toWarehouseCode: 'Elija el almacén de destino.',
      lines: 'Agregue al menos un producto.',
    });
    let draft: TransferDraft = { fromWarehouseCode: 'ALMCM', toWarehouseCode: 'ALMCB', notes: '', lines: [] };
    draft = addLine(addLine(draft, phone), mouse);
    draft = { ...draft, lines: draft.lines.map((item) => (item.sku === 'CEL-01' ? { ...item, quantity: 2, serials: ['356938035643809'] } : { ...item, quantity: 0 })) };
    const problems = transferProblems(draft);
    expect(problems.byLine).toEqual({
      'CEL-01': { serials: 'Elija 2 IMEI (una por unidad): hay 1.' },
      'MOU-01': { quantity: 'La cantidad debe ser mayor que 0.' },
    });
    expect(hasTransferProblems(problems)).toBe(true);
    expect(serialsProblem(['A', 'A'], 2, 'Serial')).toBe('La serie A está repetida.');
  });

  it('arma el pedido exacto de `CreateTransferCommand` (series solo en los serializados)', () => {
    const draft: TransferDraft = {
      fromWarehouseCode: 'ALMCM',
      toWarehouseCode: 'ALMCB',
      notes: '  Para la feria ',
      lines: [
        { ...addLine({ fromWarehouseCode: '', toWarehouseCode: '', notes: '', lines: [] }, phone).lines[0], quantity: 1, serials: ['356938035643809'] },
        { ...addLine({ fromWarehouseCode: '', toWarehouseCode: '', notes: '', lines: [] }, mouse).lines[0], quantity: 4 },
      ],
    };
    expect(createTransferPayload(draft)).toEqual({
      toWarehouseCode: 'ALMCB',
      lines: [
        { sku: 'CEL-01', quantity: 1, serials: ['356938035643809'] },
        { sku: 'MOU-01', quantity: 4, serials: null },
      ],
      notes: 'Para la feria',
      fromWarehouseCode: 'ALMCM',
    });
  });
});

describe('Transferencias · recibir y anular', () => {
  const lines = [line({}), line({ lineId: 'l2', sku: 'CEL-01', name: 'Celular', quantity: 2, serials: ['S1', 'S2'] })];

  it('por defecto llegó todo; un faltante exige motivo y, con serie, marcar las que no llegaron', () => {
    const draft = receiptDraftOf(lines);
    expect(receiptProblems(lines, draft)).toEqual({});
    expect(receiveTransferPayload('t1', lines, draft)).toEqual({
      id: 't1',
      lines: [
        { sku: 'MOU-01', receivedQuantity: 5, shortageReason: null, missingSerials: null },
        { sku: 'CEL-01', receivedQuantity: 2, shortageReason: null, missingSerials: null },
      ],
    });
    const short = { ...draft, 'MOU-01': { ...draft['MOU-01'], received: 4 }, 'CEL-01': { ...draft['CEL-01'], received: 1 } };
    expect(receiptProblems(lines, short)).toEqual({
      'MOU-01': { reason: 'Indique el motivo del faltante.' },
      'CEL-01': { reason: 'Indique el motivo del faltante.', missing: 'Marque la unidad que no llegó (marcadas: 0).' },
    });
    expect(receiptProblems(lines, { ...draft, 'MOU-01': { ...draft['MOU-01'], received: 6 } })).toEqual({ 'MOU-01': { received: 'No se puede recibir más de lo despachado (5).' } });
    const ready = {
      'MOU-01': { received: 4, reasonChoice: 'Llegó dañado', reasonOther: '', missing: [] },
      'CEL-01': { received: 1, reasonChoice: 'otro', reasonOther: ' Caja abierta ', missing: ['S2'] },
    };
    expect(receiptProblems(lines, ready)).toEqual({});
    expect(receiveTransferPayload('t1', lines, ready)).toEqual({
      id: 't1',
      lines: [
        { sku: 'MOU-01', receivedQuantity: 4, shortageReason: 'Llegó dañado', missingSerials: null },
        { sku: 'CEL-01', receivedQuantity: 1, shortageReason: 'Caja abierta', missingSerials: ['S2'] },
      ],
    });
  });

  it('anular lleva el motivo elegido o el escrito', () => {
    expect(cancelTransferPayload('t1', 'Se pidió por error', '')).toEqual({ id: 't1', reason: 'Se pidió por error' });
    expect(cancelTransferPayload('t1', 'otro', ' Cambio de plan ')).toEqual({ id: 't1', reason: 'Cambio de plan' });
  });
});
