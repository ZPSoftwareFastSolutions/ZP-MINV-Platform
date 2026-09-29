// Módulo «Actividad» · funciones puras: texto de cada acción y resumen del detalle (como el escritorio), filas de la tabla,
// filtros (usuario, resultado, días de La Paz y búsqueda sin acentos), CSV, resumen de hoy y contraseña temporal.

import { describe, expect, it } from 'vitest';
import { buildCsv } from '@/4-presentation/panel/lib';
import {
  ACTIVITY_FILTERS,
  CSV_COLUMNS,
  SYSTEM_USER,
  actionText,
  detailFields,
  filterActivity,
  generateTemporaryPassword,
  summarizeDetails,
  takeOf,
  temporaryPasswordProblem,
  toActivityItems,
  todaySummary,
  userOptions,
  type ActivityRecord,
} from './activity';

function row(overrides: Partial<ActivityRecord>): ActivityRecord {
  return {
    occurredAt: '2026-09-29T14:00:00.000Z',
    userEmail: 'cajero@techzone.example',
    userName: 'Diego Flores',
    action: 'Checkout',
    outcome: 'Succeeded',
    details: null,
    ...overrides,
  };
}

const details = (request: Record<string, unknown>, error: string | null = null, result: unknown = null) => JSON.stringify({ request, result, error });

describe('actividad · textos', () => {
  it('cada acción en palabras, como el escritorio; una nueva se separa en palabras', () => {
    expect(actionText('RegisterMovement')).toBe('Registró un movimiento');
    expect(actionText('ResetUserPassword')).toBe('Restableció la contraseña de un usuario');
    expect(actionText('CreateMyReservation')).toBe('Reservó desde su cuenta');
    expect(actionText('SyncSomethingNew')).toBe('Sync something new');
  });

  it('resume el detalle: el error si lo hubo; si no, los datos del pedido en español', () => {
    expect(summarizeDetails(details({ Sku: 'MOU-LOG-G502', Quantity: 5, SessionId: 'x', Lines: [1] }))).toBe('SKU: MOU-LOG-G502 · Cantidad: 5');
    expect(summarizeDetails(details({ Sku: 'X' }, 'La salida dejaría el stock en negativo.'))).toBe('La salida dejaría el stock en negativo.');
    expect(summarizeDetails(details({ TotalAmount: 35212.54, IsActive: true, BusinessDate: '2026-09-28' }))).toBe('Importe total: Bs 35.212,54 · Activo: sí · Fecha: 28/09/2026');
    expect(summarizeDetails(details({ Number: 'F-CM-000001' }, null, { Message: '✔ Venta registrada' }))).toBe('Número: F-CM-000001 · Venta registrada');
    expect(summarizeDetails(JSON.stringify({ detalle: ' Venta migrada de la V2.1 ' }))).toBe('Venta migrada de la V2.1');
    expect(summarizeDetails('Texto de la V2.1')).toBe('Texto de la V2.1');
    expect(summarizeDetails('{roto')).toBe('{roto');
    expect(summarizeDetails(null)).toBe('');
  });

  it('los datos registrados no repiten nombres ni muestran identificadores internos', () => {
    expect(detailFields(details({ Note: 'uno', Notes: 'dos', BranchId: 'b-1', Quantity: 2 }))).toEqual([
      { label: 'Nota', value: 'uno' },
      { label: 'Nota (Notes)', value: 'dos' },
      { label: 'Cantidad', value: '2' },
    ]);
    expect(detailFields(details({ OccurredAt: '2026-09-29T01:30:00Z' }))).toEqual([{ label: 'OccurredAt', value: '28/09/2026 21:30' }]);
    expect(detailFields('texto')).toEqual([]);
  });
});

describe('actividad · lista y filtros', () => {
  const records: ActivityRecord[] = [
    row({ occurredAt: '2026-09-29T14:00:00.000Z', action: 'Checkout', details: details({ RegisterCode: 'CAJA-CB-01' }) }),
    row({ occurredAt: '2026-09-29T14:00:00.000Z', action: 'Checkout', outcome: 'Rejected', details: details({}, 'No hay stock suficiente.') }),
    row({ occurredAt: '2026-09-28T13:00:00.000Z', userEmail: 'bodega@techzone.example', userName: 'Bruno Mamani', action: 'RegisterMovement', details: details({ Sku: 'SSD-1' }) }),
    row({ occurredAt: '2026-09-28T02:00:00.000Z', userEmail: null, userName: null, action: 'ExpirePcBuildReservations', outcome: 'Failed' }),
    row({ occurredAt: '2026-09-27T12:00:00.000Z', userEmail: 'nuevo@techzone.example', userName: null, action: 'Login', outcome: 'Rejected' }),
  ];
  const items = toActivityItems(records);

  it('cada fila tiene una clave única (aunque se repitan hora, usuario y acción) y dice quién fue', () => {
    expect(new Set(items.map((item) => item.key)).size).toBe(items.length);
    expect(items.map((item) => item.who)).toEqual(['Diego Flores', 'Diego Flores', 'Bruno Mamani', 'Sistema', 'nuevo@techzone.example']);
    expect(items[3].userValue).toBe(SYSTEM_USER);
    expect(items[0].actionText).toBe('Cobró una venta');
    expect(items[1].summary).toBe('No hay stock suficiente.');
  });

  it('las opciones de «Usuario» salen de la actividad cargada, por nombre', () => {
    expect(userOptions(items)).toEqual([
      { value: 'bodega@techzone.example', label: 'Bruno Mamani (bodega@techzone.example)' },
      { value: 'cajero@techzone.example', label: 'Diego Flores (cajero@techzone.example)' },
      { value: 'nuevo@techzone.example', label: 'nuevo@techzone.example' },
      { value: SYSTEM_USER, label: 'Sistema (trabajos automáticos)' },
    ]);
  });

  it('filtra por usuario, resultado, días de La Paz y búsqueda sin acentos', () => {
    const keys = (filters: Partial<typeof ACTIVITY_FILTERS>) => filterActivity(items, { ...ACTIVITY_FILTERS, ...filters }).map((item) => item.actionText);
    expect(keys({})).toHaveLength(5);
    expect(keys({ usuario: 'bodega@techzone.example' })).toEqual(['Registró un movimiento']);
    expect(keys({ usuario: SYSTEM_USER })).toEqual(['Venció reservas automáticamente']);
    expect(keys({ resultado: 'Rejected' })).toEqual(['Cobró una venta', 'Inició sesión']);
    // 2026-09-28T02:00Z es todavía el 27 en La Paz.
    expect(keys({ desde: '2026-09-28', hasta: '2026-09-28' })).toEqual(['Registró un movimiento']);
    expect(keys({ desde: '2026-09-27', hasta: '2026-09-27' })).toEqual(['Venció reservas automáticamente', 'Inició sesión']);
    expect(keys({ q: 'vencio reservas' })).toEqual(['Venció reservas automáticamente']);
    expect(keys({ q: 'caja-cb' })).toEqual(['Cobró una venta']);
  });

  it('el CSV lleva la fecha, quién, la acción en palabras y en código, el resultado y el detalle', () => {
    const csv = buildCsv(CSV_COLUMNS, items.slice(0, 1), { bom: false });
    const [header, first] = csv.split('\r\n');
    expect(header).toBe('"Fecha y hora";"Usuario";"Correo";"Acción";"Código de la acción";"Resultado";"Detalle"');
    expect(first).toBe('"29/09/2026 10:00";"Diego Flores";"cajero@techzone.example";"Cobró una venta";"Checkout";"Correcto";"Caja: CAJA-CB-01"');
  });

  it('`?registros=` vuelve a 500 si el valor no es uno de los ofrecidos', () => {
    expect(takeOf('1000')).toBe(1000);
    expect(takeOf('5000')).toBe(5000);
    expect(takeOf('999999')).toBe(500);
    expect(takeOf('')).toBe(500);
  });
});

describe('actividad · hoy y contraseña temporal', () => {
  it('resume la actividad de hoy (día de La Paz) y avisa si pudo haber más que las recibidas', () => {
    const now = new Date('2026-09-29T20:00:00Z');
    const today = [
      row({ occurredAt: '2026-09-29T19:00:00Z' }),
      row({ occurredAt: '2026-09-29T18:00:00Z', outcome: 'Rejected' }),
      row({ occurredAt: '2026-09-29T17:00:00Z', outcome: 'Failed', userEmail: 'bodega@techzone.example', userName: 'Bruno Mamani' }),
    ];
    const summary = todaySummary([...today, row({ occurredAt: '2026-09-29T03:00:00Z' })], now, 1000);
    expect(summary).toMatchObject({ day: '2026-09-29', total: 3, rejected: 1, failed: 1, lastAt: '2026-09-29T19:00:00Z', capped: false });
    expect(summary.byUser).toEqual([
      { label: 'Diego Flores', value: 2 },
      { label: 'Bruno Mamani', value: 1 },
    ]);
    expect(todaySummary(today, now, 3).capped).toBe(true);
  });

  it('la contraseña temporal sigue las reglas del servidor y la generada las cumple', () => {
    expect(temporaryPasswordProblem('corta1')).toBe('Use entre 8 y 128 caracteres.');
    expect(temporaryPasswordProblem('soloLetras')).toBe('Combine letras y números.');
    expect(temporaryPasswordProblem('12345678')).toBe('Combine letras y números.');
    expect(temporaryPasswordProblem('Temporal2026')).toBeNull();
    let seed = 7;
    const password = generateTemporaryPassword((max) => (seed = (seed * 31 + 11) % 997) % max);
    expect(password).toMatch(/^[A-Za-z]{5}-[2-9]{4}$/);
    expect(temporaryPasswordProblem(password)).toBeNull();
    expect(generateTemporaryPassword()).toMatch(/^[A-Za-z]{5}-[2-9]{4}$/);
  });
});
