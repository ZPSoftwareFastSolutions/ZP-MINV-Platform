// Funciones puras del módulo «Movimientos»: tipos permitidos por rol, tipo inicial, poka-yoke (disponible de la posición
// y lo que quedaría), validaciones de comodidad, contenido exacto de `RegisterMovementCommand`, series, lista, filtros,
// CSV y tendencia.

import { describe, expect, it } from 'vitest';
import { buildCsv } from '@/4-presentation/panel/lib';
import {
  MOVEMENT_CSV,
  MOVEMENT_FILTERS,
  allowedTypes,
  binAvailable,
  defaultBin,
  filterMovements,
  hasProblems,
  initialType,
  movementProblems,
  parseSerials,
  productOptions,
  projectedStock,
  registerPayload,
  registeredText,
  serialKindLabel,
  serialTypeNotice,
  takeOf,
  toMovementEntries,
  trendSummary,
  typeOptions,
  typeTitle,
  userOptions,
  wouldBeNegative,
  type MovementDraft,
  type MovementTypeRecord,
  type ProductCardData,
  type RecentMovementRecord,
} from './movements';

function type(code: string, name: string, stockFactor: number, domain: 'Sales' | 'Warehouse', requiresNotes = false): MovementTypeRecord {
  return { code, name, description: null, stockFactor, domain, requiresNotes, isInitialBalance: code === 'SALDO_INICIAL' };
}

const TYPES: MovementTypeRecord[] = [
  type('SALDO_INICIAL', 'SALDO INICIAL', 1, 'Warehouse'),
  type('ENTRADA', 'ENTRADA', 1, 'Warehouse'),
  type('AJUSTE_POS', 'AJUSTE (+)', 1, 'Warehouse', true),
  type('AJUSTE_NEG', 'AJUSTE (-)', -1, 'Warehouse', true),
  type('SALIDA', 'SALIDA', -1, 'Sales'),
  type('VENTA_POS', 'VENTA POS', -1, 'Sales'),
  type('DEVOLUCION_CLIENTE', 'DEVOLUCIÓN DE CLIENTE', 1, 'Sales', true),
  type('RECEPCION_COMPRA', 'RECEPCIÓN DE COMPRA', 1, 'Warehouse'),
  type('TRASLADO_SALIDA', 'TRASLADO (SALIDA)', -1, 'Warehouse'),
];

const byCode = (code: string) => TYPES.find((item) => item.code === code);

describe('Movimientos · tipos', () => {
  it('solo los manuales que el rol puede registrar, en el orden del escritorio', () => {
    expect(allowedTypes(TYPES, { warehouse: true, sales: false }).map((item) => item.code)).toEqual(['ENTRADA', 'AJUSTE_POS', 'AJUSTE_NEG', 'SALDO_INICIAL']);
    expect(allowedTypes(TYPES, { warehouse: false, sales: true }).map((item) => item.code)).toEqual(['SALIDA', 'DEVOLUCION_CLIENTE', 'VENTA_POS']);
    expect(allowedTypes(TYPES, { warehouse: false, sales: false })).toEqual([]);
  });

  it('tipo inicial: el pedido si se permite, «ajuste» = el negativo, y si no el primero', () => {
    const warehouse = allowedTypes(TYPES, { warehouse: true, sales: false });
    expect(initialType('ENTRADA', warehouse)).toBe('ENTRADA');
    expect(initialType('ajuste', warehouse)).toBe('AJUSTE_NEG');
    expect(initialType('SALIDA', warehouse)).toBe('ENTRADA');
    expect(initialType('1', warehouse)).toBe('ENTRADA');
    expect(initialType('entrada', warehouse)).toBe('ENTRADA');
    expect(initialType(null, [])).toBe('');
    expect(initialType('ajuste', allowedTypes(TYPES, { warehouse: false, sales: true }))).toBe('SALIDA');
  });

  it('nombres en tipo oración y opciones con su signo', () => {
    expect(typeTitle('DEVOLUCIÓN DE CLIENTE')).toBe('Devolución de cliente');
    expect(typeTitle('VENTA POS')).toBe('Venta POS');
    expect(typeTitle('AJUSTE (-)')).toBe('Ajuste (-)');
    expect(typeOptions([byCode('ENTRADA')!, byCode('AJUSTE_NEG')!])).toEqual([
      { value: 'ENTRADA', label: 'Entrada (suma)' },
      { value: 'AJUSTE_NEG', label: 'Ajuste (-) (resta)' },
    ]);
  });
});

describe('Movimientos · poka-yoke y formulario', () => {
  const card = {
    primaryBin: 'A-02',
    bins: [
      { binCode: 'A-01', lotNumber: 'SIN-LOTE', onHand: 8, reserved: 1, available: 7 },
      { binCode: 'A-01', lotNumber: 'L-2026', onHand: 50, reserved: 0, available: 50 },
      { binCode: 'A-03', lotNumber: 'SIN-LOTE', onHand: 20, reserved: 0, available: 20 },
    ],
  } as Pick<ProductCardData, 'bins' | 'primaryBin'>;
  const bins = [
    { code: 'A-01', zone: 'A', warehouseCode: 'CM-01' },
    { code: 'A-03', zone: 'A', warehouseCode: 'CM-01' },
  ];

  it('disponible de la posición en el lote por defecto y lo que quedaría', () => {
    expect(binAvailable(card as ProductCardData, 'A-01')).toBe(7);
    expect(binAvailable(card as ProductCardData, 'Z-99')).toBe(0);
    expect(binAvailable(undefined, 'A-01')).toBe(0);
    expect(projectedStock(7, byCode('SALIDA'), 9)).toBe(-2);
    expect(projectedStock(7, byCode('ENTRADA'), 9)).toBe(16);
    expect(projectedStock(7, byCode('ENTRADA'), null)).toBeNull();
  });

  it('posición inicial: la principal si es del almacén; si no, la que más tiene; si no, la primera', () => {
    expect(defaultBin(card as ProductCardData, [])).toBe('A-02');
    expect(defaultBin(card as ProductCardData, bins)).toBe('A-01');
    expect(defaultBin({ primaryBin: null, bins: [] }, bins)).toBe('A-01');
    expect(defaultBin(undefined, [])).toBe('');
  });

  const draft = (patch: Partial<MovementDraft> = {}): MovementDraft => ({
    type: byCode('SALIDA'),
    sku: 'MOU-01',
    binCode: 'A-01',
    quantity: 3,
    allowsDecimals: false,
    document: '',
    notes: '',
    trackSerials: false,
    serials: [],
    available: 7,
    ...patch,
  });

  it('valida lo que falta, el poka-yoke, la observación obligatoria, los decimales y las series', () => {
    expect(hasProblems(movementProblems(draft()))).toBe(false);
    expect(movementProblems(draft({ type: undefined, sku: null, binCode: '', quantity: null }))).toEqual({
      type: 'Elija el tipo de movimiento.',
      product: 'Elija el producto.',
      bin: 'Elija la posición.',
      quantity: 'Indique una cantidad mayor que 0.',
    });
    expect(movementProblems(draft({ quantity: 9 })).quantity).toBe('La salida dejaría la posición en negativo: solo hay 7 disponibles. Revise la cantidad o la posición.');
    expect(wouldBeNegative(draft({ quantity: 9 }))).toBe(true);
    expect(wouldBeNegative(draft({ quantity: 7 }))).toBe(false);
    expect(wouldBeNegative(draft({ type: byCode('ENTRADA'), quantity: 90 }))).toBe(false);
    expect(movementProblems(draft({ quantity: 1.5 })).quantity).toBe('La unidad de este producto no admite decimales.');
    expect(movementProblems(draft({ type: byCode('AJUSTE_NEG') })).notes).toBe('Escriba la observación: es obligatoria para este tipo.');
    expect(movementProblems(draft({ type: byCode('AJUSTE_NEG'), notes: 'Merma' })).notes).toBeUndefined();
    expect(movementProblems(draft({ trackSerials: true, serials: ['A'] })).serials).toBe('Indique 3 series (una por unidad): hay 1.');
    expect(movementProblems(draft({ trackSerials: true, serials: ['A', 'B', 'A'] })).serials).toBe('La serie A está repetida.');
    expect(movementProblems(draft({ trackSerials: true, allowsDecimals: true, quantity: 1.5 })).serials).toBe(
      'Este producto lleva serie: la cantidad debe ser entera (una serie por unidad).',
    );
    expect(movementProblems(draft({ trackSerials: true, serials: ['A', 'B', 'C'] })).serials).toBeUndefined();
  });

  it('arma `RegisterMovementCommand` con TODOS los parámetros (lo vacío como null)', () => {
    expect(registerPayload(draft({ document: '  REM-5 ', notes: ' ' }))).toEqual({
      sku: 'MOU-01',
      binCode: 'A-01',
      movementTypeCode: 'SALIDA',
      quantity: 3,
      businessDate: null,
      documentReference: 'REM-5',
      notes: null,
      lotNumber: null,
      adjustmentReasonCode: null,
      serials: null,
    });
    expect(registerPayload(draft({ trackSerials: true, serials: ['X1', 'X2', 'X3'], notes: 'Venta mayorista' }))).toMatchObject({ notes: 'Venta mayorista', serials: ['X1', 'X2', 'X3'] });
    expect(registeredText(byCode('SALIDA')!, 3, 'u.', 'MOU-01', 5, 'A-01')).toBe('−3 u. · MOU-01 · quedan 5 u. en A-01');
    expect(registeredText(byCode('ENTRADA')!, 2.5, 'kg', 'CAF-01', 12.5, 'B-01')).toBe('+2,5 kg · CAF-01 · quedan 12,5 kg en B-01');
  });

  it('series: una por línea (o separadas por coma), nombre del tipo y los tipos que tienen su propio documento', () => {
    expect(parseSerials(' 111 \n222, 333;\n\n444 ')).toEqual(['111', '222', '333', '444']);
    expect(serialKindLabel('Imei')).toBe('IMEI');
    expect(serialKindLabel('Serial')).toBe('número de serie');
    expect(serialKindLabel('Serial', true)).toBe('números de serie');
    expect(serialTypeNotice('VENTA_POS')).toBe('Este producto lleva serie: la venta se registra desde la caja.');
    expect(serialTypeNotice('DEVOLUCION_CLIENTE')).toBe('Este producto lleva serie: la devolución se registra desde la venta.');
    expect(serialTypeNotice('ENTRADA')).toBeNull();
  });
});

describe('Movimientos · lista', () => {
  const record = (patch: Partial<RecentMovementRecord>): RecentMovementRecord => ({
    recordedAt: '2026-09-29T14:00:00Z',
    businessDate: '2026-09-29',
    sku: 'MOU-01',
    name: 'Mouse gamer',
    typeCode: 'ENTRADA',
    typeName: 'ENTRADA',
    stockFactor: 1,
    quantity: 5,
    unit: 'u.',
    binCode: 'A-01',
    userName: 'Bruno Mamani',
    document: 'REM-5',
    ...patch,
  });
  const records = [
    record({}),
    record({}),
    record({ recordedAt: '2026-09-28T14:00:00Z', businessDate: '2026-09-28', sku: 'TEC-01', name: 'Teclado', typeCode: 'SALIDA', typeName: 'SALIDA', stockFactor: -1, userName: 'Carla Rojas', document: null }),
    record({ recordedAt: '2026-09-20T14:00:00Z', businessDate: '2026-09-20', typeCode: 'TRASLADO_ENTRADA', typeName: 'TRASLADO (ENTRADA)', userName: null }),
  ];
  const entries = toMovementEntries(records);

  it('claves únicas aunque se repita todo, entrada o salida y quién', () => {
    expect(new Set(entries.map((entry) => entry.key)).size).toBe(4);
    expect(entries.map((entry) => [entry.isIn, entry.typeTitle, entry.who])).toEqual([
      [true, 'Entrada', 'Bruno Mamani'],
      [true, 'Entrada', 'Bruno Mamani'],
      [false, 'Salida', 'Carla Rojas'],
      [true, 'Traslado (entrada)', 'Sistema'],
    ]);
  });

  it('filtra por tipo, entradas o salidas, usuario, producto, fechas y búsqueda', () => {
    const count = (filters: Partial<typeof MOVEMENT_FILTERS>) => filterMovements(entries, { ...MOVEMENT_FILTERS, ...filters }).length;
    expect(count({})).toBe(4);
    expect(count({ tipo: 'SALIDA' })).toBe(1);
    expect(count({ flujo: 'entradas' })).toBe(3);
    expect(count({ flujo: 'salidas' })).toBe(1);
    expect(count({ usuario: '_sistema' })).toBe(1);
    expect(count({ producto: 'TEC-01' })).toBe(1);
    expect(count({ desde: '2026-09-28', hasta: '2026-09-29' })).toBe(3);
    expect(count({ q: 'rem-5' })).toBe(3);
    expect(count({ q: 'traslado' })).toBe(1);
  });

  it('opciones de usuario y producto, cuántos traer y CSV', () => {
    expect(userOptions(entries).map((option) => option.label)).toEqual(['Bruno Mamani', 'Carla Rojas', 'Sistema (procesos automáticos)']);
    expect(productOptions(entries)).toEqual([
      { value: 'MOU-01', label: 'Mouse gamer', description: 'MOU-01' },
      { value: 'TEC-01', label: 'Teclado', description: 'TEC-01' },
    ]);
    expect([takeOf('50'), takeOf('500'), takeOf('9999'), takeOf('')]).toEqual([50, 500, 200, 200]);
    const lines = buildCsv(MOVEMENT_CSV, [entries[2]], { bom: false }).trim().split('\r\n');
    expect(lines[0]).toBe('"Registrado";"Fecha del movimiento";"Tipo";"Entrada o salida";"SKU";"Producto";"Cantidad";"Unidad";"Posición";"Documento";"Usuario"');
    expect(lines[1]).toBe('"28/09/2026 10:00";"28/09/2026";"Salida";"Salida";"TEC-01";"Teclado";-5;"u.";"A-01";;"Carla Rojas"');
  });

  it('tendencia: totales y series por día (el saldo inicial aparte)', () => {
    const summary = trendSummary([
      { date: '2026-09-28', entries: 10, issues: 4, movements: 6, opening: 0 },
      { date: '2026-09-29', entries: 2, issues: 7, movements: 5, opening: 100 },
    ]);
    expect(summary).toMatchObject({ entries: 12, issues: 11, movements: 11, opening: 100 });
    expect(summary.entryPoints).toEqual([
      { label: '28/09', value: 10 },
      { label: '29/09', value: 2 },
    ]);
  });
});
