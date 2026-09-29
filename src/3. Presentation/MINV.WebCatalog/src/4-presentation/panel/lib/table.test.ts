// Orden y paginación de las tablas del panel y ayudas del RPC (llave de contenido, intento de un comando y texto del
// permiso que falta). Funciones puras.

import { describe, expect, it } from 'vitest';
import { WebApiError } from '@/1-domain/auth/errors';
import { permissionName } from '@/4-presentation/app/contract';
import { CommandAttempt, describePanelError, missingPermissions, permissionsInMessage, stableKey } from './rpc';
import { clampPage, formatCellValue, matchesSearch, nextSort, pageCountOf, paginate, rangeText, sortRows } from './table';

interface Row {
  id: string;
  total: number | null;
  date: Date | null;
}

const ROWS: Row[] = [
  { id: 'F-10', total: 50, date: new Date('2026-09-02') },
  { id: 'F-2', total: null, date: new Date('2026-09-01') },
  { id: 'F-1', total: 200, date: null },
  { id: 'F-3', total: 50, date: new Date('2026-09-03') },
];

describe('orden', () => {
  it('ordena números, textos con números naturales y fechas; los vacíos siempre al final y el orden es estable', () => {
    expect(sortRows(ROWS, (row) => row.total, 'asc').map((row) => row.id)).toEqual(['F-10', 'F-3', 'F-1', 'F-2']);
    expect(sortRows(ROWS, (row) => row.total, 'desc').map((row) => row.id)).toEqual(['F-1', 'F-10', 'F-3', 'F-2']);
    expect(sortRows(ROWS, (row) => row.id, 'asc').map((row) => row.id)).toEqual(['F-1', 'F-2', 'F-3', 'F-10']);
    expect(sortRows(ROWS, (row) => row.date, 'desc').map((row) => row.id)).toEqual(['F-3', 'F-10', 'F-2', 'F-1']);
    expect(sortRows(['Ñandú', 'árbol', 'Zorro', 'nube'], (value) => value, 'asc')).toEqual(['árbol', 'nube', 'Ñandú', 'Zorro']);
  });

  it('no cambia el arreglo original', () => {
    const copy = [...ROWS];
    sortRows(ROWS, (row) => row.total, 'desc');
    expect(ROWS).toEqual(copy);
  });

  it('al pulsar el encabezado: ascendente → descendente → sin orden', () => {
    expect(nextSort(null, 'total')).toEqual({ column: 'total', direction: 'asc' });
    expect(nextSort({ column: 'total', direction: 'asc' }, 'total')).toEqual({ column: 'total', direction: 'desc' });
    expect(nextSort({ column: 'total', direction: 'desc' }, 'total')).toBeNull();
    expect(nextSort({ column: 'total', direction: 'desc' }, 'fecha')).toEqual({ column: 'fecha', direction: 'asc' });
  });
});

describe('paginación', () => {
  const numbers = Array.from({ length: 53 }, (_, index) => index + 1);

  it('corta la página y dice qué filas muestra', () => {
    const second = paginate(numbers, 2, 25);
    expect(second.rows[0]).toBe(26);
    expect(second.rows).toHaveLength(25);
    expect(second).toMatchObject({ page: 2, pageCount: 3, from: 26, to: 50, total: 53 });
    expect(rangeText(second)).toBe('Mostrando 26–50 de 53');
    expect(paginate(numbers, 3, 25)).toMatchObject({ from: 51, to: 53 });
  });

  it('una página fuera de rango muestra la última; sin filas, «Sin filas»', () => {
    expect(paginate(numbers, 9, 25).page).toBe(3);
    expect(paginate(numbers, 0, 25).page).toBe(1);
    expect(paginate(numbers, Number.NaN, 10).page).toBe(1);
    const empty = paginate([], 1, 25);
    expect(empty).toMatchObject({ rows: [], page: 1, pageCount: 1, from: 0, to: 0 });
    expect(rangeText(empty)).toBe('Sin filas');
    expect(pageCountOf(0, 25)).toBe(1);
    expect(clampPage(2.7, 5)).toBe(2);
  });

  it('miles con separador en el texto', () => {
    expect(rangeText(paginate(Array.from({ length: 2345 }, (_, index) => index), 2, 1000))).toBe('Mostrando 1.001–2.000 de 2.345');
  });
});

describe('celdas y búsqueda', () => {
  it('formatea el valor de una celda sin contenido propio', () => {
    expect(formatCellValue(null)).toBe('—');
    expect(formatCellValue('  ')).toBe('—');
    expect(formatCellValue(true)).toBe('Sí');
    expect(formatCellValue(false)).toBe('No');
    expect(formatCellValue(1234.567)).toBe('1.234,57');
    expect(formatCellValue(new Date('2026-09-29T01:30:00Z'))).toBe('28/09/2026 21:30');
    expect(formatCellValue('F-CM-1')).toBe('F-CM-1');
  });

  it('busca todas las palabras sin acentos ni mayúsculas', () => {
    expect(matchesSearch('lucia mamani', ['Lucía Mamani', 'F-CM-1'])).toBe(true);
    expect(matchesSearch('LUCÍA f-cm', ['Lucía Mamani', 'F-CM-1'])).toBe(true);
    expect(matchesSearch('lucia quispe', ['Lucía Mamani'])).toBe(false);
    expect(matchesSearch('  ', ['cualquiera'])).toBe(true);
    expect(matchesSearch('123', [null, 1234])).toBe(true);
  });
});

describe('ayudas del RPC', () => {
  it('la llave de un contenido no depende del orden de las claves', () => {
    expect(stableKey({ b: 1, a: { d: [1, 2], c: 'x' } })).toBe(stableKey({ a: { c: 'x', d: [1, 2] }, b: 1 }));
    expect(stableKey({ a: 1 })).not.toBe(stableKey({ a: 2 }));
    expect(stableKey(undefined)).toBe('null');
  });

  it('un intento conserva su id mientras el contenido no cambie y estrena otro después de un éxito', () => {
    const attempt = new CommandAttempt();
    const first = attempt.take('A');
    expect(attempt.take('A')).toBe(first);
    const other = attempt.take('B');
    expect(other).not.toBe(first);
    attempt.succeeded();
    expect(attempt.current).toBeNull();
    expect(attempt.take('B')).not.toBe(other);
  });

  it('lee el permiso que nombra el servidor y lo dice en palabras', () => {
    const denied = new WebApiError({ kind: 'access_denied', status: 403, message: 'Su rol no tiene el permiso sales.view.' });
    expect(permissionsInMessage(denied.message)).toEqual(['sales.view']);
    expect(missingPermissions(denied)).toEqual(['sales.view']);
    expect(describePanelError(denied)).toBe(
      'No tiene permiso para esta operación. Falta el permiso «Consultar el historial de ventas y facturas». Pida al administrador que se lo asigne.',
    );
  });

  it('si el servidor no nombra el permiso, usa los que exige la operación y la sesión no tiene', () => {
    const denied = new WebApiError({ kind: 'access_denied', status: 403, message: 'Acceso denegado.' });
    expect(missingPermissions(denied, { operation: 'CreateMyReservationCommand', granted: [] })).toEqual(['account.reserve']);
    expect(missingPermissions(denied, { operation: 'CreateMyReservationCommand', granted: ['account.reserve'] })).toEqual([]);
    expect(describePanelError(denied, { operation: 'CreateMyReservationCommand', granted: [] })).toContain(`«${permissionName('account.reserve')}»`);
    // Otro rechazo (sucursal, módulo): el mensaje del servidor tal cual.
    expect(describePanelError(new WebApiError({ kind: 'access_denied', message: 'La sucursal elegida no está entre las suyas.' }))).toBe('La sucursal elegida no está entre las suyas.');
  });

  it('varios permisos se enumeran; los demás errores usan el texto del panel', () => {
    const denied = new WebApiError({ kind: 'access_denied', message: 'Falta el permiso billing.view y el permiso reports.view.' });
    expect(describePanelError(denied)).toContain('Faltan los permisos «Consultar documentos fiscales, estado del SIAT y libros de ventas y compras» y «Consultar reportes');
    expect(describePanelError(new WebApiError({ kind: 'network', message: '' }))).toBe('No se pudo conectar con el servidor. Revise la conexión e intente de nuevo.');
    expect(describePanelError(new Error('detalle interno'))).toBe('Ocurrió un error inesperado. Intente de nuevo.');
  });
});
