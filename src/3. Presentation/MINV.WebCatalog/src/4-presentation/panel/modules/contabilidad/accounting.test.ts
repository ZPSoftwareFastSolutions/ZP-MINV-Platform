// Módulo «Contabilidad» · funciones puras: el origen de los asientos, los filtros del libro y del plan, el mayor de una
// cuenta, el código sugerido y la validación de una cuenta nueva, las plantillas, la validación y el pedido exacto del
// asiento manual, el estado de resultados y el período (con «Este mes» por defecto).

import { describe, expect, it } from 'vitest';
import {
  CHART_FILTERS,
  ENTRY_TEMPLATES,
  JOURNAL_FILTERS,
  accountFormErrors,
  accountMovement,
  balanceText,
  entryErrors,
  entryPayload,
  entryPrefill,
  entryTotals,
  filterAccounts,
  filterJournal,
  hasEntryErrors,
  originOf,
  statementLines,
  suggestCode,
  templatePrefill,
  toJournalItems,
  viewOf,
  type AccountRecord,
  type EntryLineDraft,
  type JournalRecord,
  type StatementData,
} from './accounting';
import { PERIOD_FILTERS, resolvePeriod } from './period';

const NOW = new Date('2026-09-29T15:00:00Z');

function account(code: string, name: string, patch: Partial<AccountRecord> = {}): AccountRecord {
  const level = code.split('.').length - 1;
  return { code, name, type: 'Expense', level, isPostable: level >= 2, parentCode: level === 0 ? null : code.slice(0, code.lastIndexOf('.')), debit: 0, credit: 0, balance: 0, ...patch };
}

const CHART: AccountRecord[] = [
  account('1', 'ACTIVO', { type: 'Asset' }),
  account('1.1', 'Activo corriente', { type: 'Asset' }),
  account('1.1.01', 'Caja', { type: 'Asset', debit: 500, credit: 200, balance: 300 }),
  account('1.1.02', 'Bancos', { type: 'Asset' }),
  account('6', 'GASTOS'),
  account('6.1', 'Gastos de operación'),
  account('6.1.01', 'Sueldos y salarios', { debit: 200, balance: 200 }),
  account('6.1.04', 'Gastos administrativos'),
];

const JOURNAL: JournalRecord[] = [
  { number: 'AS-CM-000010', date: '2026-09-20', description: 'Venta F-CM-000100 · Juan Pérez · Efectivo', status: 'Posted', total: 116, lines: [{ accountCode: '1.1.01', accountName: 'Caja', debit: 116, credit: 0, memo: null }, { accountCode: '4.1.01', accountName: 'Ventas', debit: 0, credit: 116, memo: null }] },
  { number: 'AS-CM-000011', date: '2026-09-21', description: 'Pago de sueldos del mes', status: 'Posted', total: 200, lines: [{ accountCode: '6.1.01', accountName: 'Sueldos y salarios', debit: 200, credit: 0, memo: 'Septiembre' }, { accountCode: '1.1.01', accountName: 'Caja', debit: 0, credit: 200, memo: null }] },
];

describe('Contabilidad · libro diario', () => {
  it('el origen sale de la descripción, como en el escritorio (y además devoluciones, transferencias y garantías)', () => {
    expect(originOf('Venta F-CM-000001 · Cliente')).toBe('venta');
    expect(originOf('Anulación de la venta F-CM-000001: error')).toBe('anulacion');
    expect(originOf('Devolución DEV-CM-000001 de la venta F-CM-000001: falla')).toBe('devolucion');
    expect(originOf('Compra OC-CM-000001 · Proveedor · recepción REC-1')).toBe('compra');
    expect(originOf('Crédito fiscal · factura 12 de Proveedor')).toBe('compra');
    expect(originOf('Transferencia TR-CM-000001 despachada a CB')).toBe('transferencia');
    expect(originOf('Toma física TF-CM-000001: sobrantes y faltantes al costo promedio')).toBe('inventario');
    expect(originOf('AJUSTE (-) 2 u. · MOU-01')).toBe('inventario');
    expect(originOf('Reposición por garantía RMA-CM-000001 de la serie X')).toBe('garantia');
    expect(originOf('Pago del alquiler')).toBe('manual');
  });

  it('filtra por cuenta, origen, estado y búsqueda; el mayor de la cuenta suma sus líneas', () => {
    const items = toJournalItems(JOURNAL);
    expect(items.map((item) => [item.origin, item.debit, item.credit])).toEqual([
      ['venta', 116, 116],
      ['manual', 200, 200],
    ]);
    expect(filterJournal(items, { ...JOURNAL_FILTERS, cuenta: '6.1.01' }).map((item) => item.key)).toEqual(['AS-CM-000011']);
    expect(filterJournal(items, { ...JOURNAL_FILTERS, origen: 'venta' }).map((item) => item.key)).toEqual(['AS-CM-000010']);
    expect(filterJournal(items, { ...JOURNAL_FILTERS, q: 'septiembre' }).map((item) => item.key)).toEqual(['AS-CM-000011']);
    expect(filterJournal(items, { ...JOURNAL_FILTERS, estado: 'Draft' })).toEqual([]);
    expect(accountMovement(items, '1.1.01')).toEqual({ debit: 116, credit: 200, entries: 2 });
  });

  it('«Usar como plantilla» repite cuentas, montos y glosas', () => {
    expect(entryPrefill(JOURNAL[1])).toEqual({
      description: 'Pago de sueldos del mes',
      lines: [
        { account: '6.1.01', debit: 200, credit: null, memo: 'Septiembre' },
        { account: '1.1.01', debit: null, credit: 200, memo: '' },
      ],
    });
  });
});

describe('Contabilidad · plan de cuentas', () => {
  it('filtra por tipo, clase, movimiento y búsqueda', () => {
    expect(filterAccounts(CHART, { ...CHART_FILTERS, tipo: 'Asset', clase: 'imputables' }).map((item) => item.code)).toEqual(['1.1.01', '1.1.02']);
    expect(filterAccounts(CHART, { ...CHART_FILTERS, movimiento: 'con' }).map((item) => item.code)).toEqual(['1.1.01', '6.1.01']);
    expect(filterAccounts(CHART, { ...CHART_FILTERS, clase: 'grupos', q: 'gastos' }).map((item) => item.code)).toEqual(['6', '6.1']);
  });

  it('sugiere la siguiente subcuenta con las cifras de sus hermanas', () => {
    expect(suggestCode(CHART, '6.1')).toBe('6.1.05');
    expect(suggestCode(CHART, '1.1')).toBe('1.1.03');
    expect(suggestCode(CHART, '6')).toBe('6.2');
    expect(suggestCode([...CHART, account('6.2', 'Gastos financieros')], '6.2')).toBe('6.2.01');
  });

  it('valida la cuenta nueva como el servidor: grupo, prefijo, repetida y nombre', () => {
    expect(accountFormErrors({ parentCode: '6.1', code: '6.1.05', name: 'Publicidad' }, CHART)).toEqual({});
    expect(accountFormErrors({ parentCode: '', code: '', name: '' }, CHART)).toEqual({ parent: 'Elija el grupo donde va la cuenta.', code: 'Escriba el código de la cuenta.', name: 'Escriba el nombre de la cuenta.' });
    expect(accountFormErrors({ parentCode: '1.1.01', code: '1.1.01.1', name: 'Caja chica' }, CHART).parent).toBe('Las cuentas nuevas se crean bajo un grupo, no bajo una cuenta imputable.');
    expect(accountFormErrors({ parentCode: '6.1', code: '5.1.05', name: 'Publicidad' }, CHART).code).toBe('El código debe empezar con 6.1.');
    expect(accountFormErrors({ parentCode: '6.1', code: '6.1.', name: 'Publicidad' }, CHART).code).toBe('Complete el código después de 6.1.');
    expect(accountFormErrors({ parentCode: '6.1', code: '6.1.01', name: 'Publicidad' }, CHART).code).toBe('Ya existe la cuenta 6.1.01.');
  });
});

describe('Contabilidad · asiento manual', () => {
  const line = (id: number, account: string, debit: number | null, credit: number | null, memo = ''): EntryLineDraft => ({ id, account, debit, credit, memo });

  it('totales en vivo sin errores del binario (0,1 + 0,2 = 0,3)', () => {
    const lines = [line(1, '6.1.04', 0.1, null), line(2, '6.1.04', 0.2, null), line(3, '1.1.01', null, 0.3)];
    expect(entryTotals(lines)).toEqual({ debit: 0.3, credit: 0.3, difference: 0, balanced: true });
    expect(balanceText(lines)).toBe('Cuadrado: el debe es igual al haber.');
    expect(balanceText([line(1, '6.1.04', 100, null), line(2, '1.1.01', null, 60)])).toBe('No cuadra: la diferencia es Bs 40,00.');
    expect(balanceText([line(1, '', null, null), line(2, '', null, null)])).toBe('Escriba los montos del debe y del haber.');
  });

  it('valida fecha, descripción, cuentas, lados y partida doble', () => {
    const empty = entryErrors({ date: '', description: '', lines: [line(1, '', null, null), line(2, '', null, null)] }, NOW);
    expect(empty).toMatchObject({ date: 'Elija la fecha del asiento.', description: 'Escriba la descripción del asiento (qué registra).', general: 'Un asiento necesita al menos dos líneas con monto.' });
    expect(entryErrors({ date: '2026-09-30', description: 'Futuro', lines: [] }, NOW).date).toBe('La fecha del asiento no puede ser futura.');
    const wrong = entryErrors({ date: '2026-09-29', description: 'Pago', lines: [line(1, '', 100, null), line(2, '1.1.01', 50, 50), line(3, '1.1.02', null, null)] }, NOW);
    expect(wrong.lines).toEqual({
      1: { account: 'Elija la cuenta de esta línea.' },
      2: { amount: 'Cada línea va al debe o al haber, no a los dos.' },
      3: { amount: 'Escriba el monto en el debe o en el haber (o quite la línea).' },
    });
    expect(entryErrors({ date: '2026-09-29', description: 'Pago', lines: [line(1, '6.1.04', 100, null), line(2, '1.1.01', null, 90)] }, NOW).general).toBe(
      'El asiento no cuadra: el total del debe debe ser igual al del haber (diferencia Bs 10,00).',
    );
    const ok = entryErrors({ date: '2026-09-29', description: 'Pago de luz', lines: [line(1, '6.1.03', 150, null), line(2, '1.1.01', null, 150), line(3, '', null, null)] }, NOW);
    expect(hasEntryErrors(ok)).toBe(false);
  });

  it('el pedido lleva solo las líneas con monto, glosa vacía como null y la sucursal', () => {
    expect(
      entryPayload({ date: '2026-09-29', description: '  Pago de luz ', branchId: null, lines: [line(1, '6.1.03', 150, null, ' Factura 123 '), line(2, '1.1.01', null, 150), line(3, '', null, null)] }),
    ).toEqual({
      date: '2026-09-29',
      description: 'Pago de luz',
      branchId: null,
      lines: [
        { accountCode: '6.1.03', debit: 150, credit: 0, memo: 'Factura 123' },
        { accountCode: '1.1.01', debit: 0, credit: 150, memo: null },
      ],
    });
  });

  it('una plantilla llena las cuentas que existen en el plan', () => {
    const sueldos = ENTRY_TEMPLATES.find((template) => template.id === 'sueldos')!;
    expect(templatePrefill(sueldos, CHART)).toEqual({
      description: 'Pago de sueldos del mes',
      lines: [
        { account: '6.1.01', debit: null, credit: null, memo: '' },
        { account: '1.1.02', debit: null, credit: null, memo: '' },
      ],
    });
    // «Aporte de capital» usa 3.1.01, que este plan no tiene: esa línea queda sin cuenta.
    expect(templatePrefill(ENTRY_TEMPLATES.find((template) => template.id === 'capital')!, CHART).lines.map((item) => item.account)).toEqual(['1.1.02', '']);
  });
});

describe('Contabilidad · estado de resultados y período', () => {
  it('arma las secciones con sus totales y la parte de los ingresos', () => {
    const statement: StatementData = {
      from: '2026-09-01',
      to: '2026-09-29',
      revenue: [account('4.1.01', 'Ventas', { type: 'Revenue', balance: 1000 })],
      costs: [account('5.1.01', 'Costo de ventas', { balance: 600 })],
      expenses: [account('6.1.01', 'Sueldos y salarios', { balance: 500 })],
      totalRevenue: 1000,
      totalCosts: 600,
      grossProfit: 400,
      totalExpenses: 500,
      netIncome: -100,
    };
    expect(statementLines(statement).map((item) => [item.kind, item.name, item.amount, item.ofRevenue])).toEqual([
      ['cuenta', 'Ventas', 1000, 1],
      ['total', 'Total ingresos', 1000, 1],
      ['cuenta', 'Costo de ventas', 600, 0.6],
      ['total', 'Total costo de ventas', 600, 0.6],
      ['resultado', 'Utilidad bruta', 400, 0.4],
      ['cuenta', 'Sueldos y salarios', 500, 0.5],
      ['total', 'Total gastos de operación', 500, 0.5],
      ['resultado', 'Pérdida neta del período', -100, -0.1],
    ]);
  });

  it('la contabilidad abre en «Este mes» y en «Estado de resultados»', () => {
    expect(resolvePeriod(PERIOD_FILTERS, NOW)).toEqual({ id: 'esteMes', from: '2026-09-01', to: '2026-09-29', problem: null });
    expect(viewOf(null)).toBe('resultados');
    expect(viewOf('diario')).toBe('diario');
    expect(viewOf('otra')).toBe('resultados');
  });
});
