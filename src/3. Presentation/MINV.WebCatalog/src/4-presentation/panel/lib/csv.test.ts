// Exportación a CSV para Excel: BOM, separador «;», CRLF, comillas escapadas, números sin comillas y neutralización de
// fórmulas (=, +, -, @, tabulador, retorno). La descarga se simula: ninguna prueba toca la red ni el disco.

import { afterEach, describe, expect, it, vi } from 'vitest';
import { CSV_BOM, buildCsv, csvCell, csvFileName, exportCsv, neutralizeFormula, type CsvColumn } from './csv';
import { csvColumnsOf } from './table';

interface Row {
  number: string;
  customer: string;
  total: number;
  paid: boolean;
  date: string | Date | null;
  notes: string | null;
}

const COLUMNS: CsvColumn<Row>[] = [
  { header: 'Número', value: (row) => row.number },
  { header: 'Cliente', value: (row) => row.customer },
  { header: 'Total', value: (row) => row.total },
  { header: 'Pagada', value: (row) => row.paid },
  { header: 'Fecha', value: (row) => (typeof row.date === 'string' ? new Date(row.date) : row.date) },
  { header: 'Notas', value: (row) => row.notes },
];

const ROWS: Row[] = [
  { number: 'F-CM-000123', customer: 'Lucía "La Profe" Mamani', total: 1234.5, paid: true, date: '2026-09-29T01:30:00Z', notes: 'Retira; el sábado' },
  { number: 'F-CM-000124', customer: 'Carlos Quispe', total: -120, paid: false, date: null, notes: 'línea 1\nlínea 2' },
];

describe('neutralización de fórmulas', () => {
  it('antepone un apóstrofo a los textos que empiezan con =, +, -, @, tabulador, retorno o salto de línea', () => {
    expect(neutralizeFormula('=HYPERLINK("http://x","clic")')).toBe(`'=HYPERLINK("http://x","clic")`);
    expect(neutralizeFormula('+591 71234567')).toBe("'+591 71234567");
    expect(neutralizeFormula('-2+3+cmd')).toBe("'-2+3+cmd");
    expect(neutralizeFormula('@SUMA(A1)')).toBe("'@SUMA(A1)");
    expect(neutralizeFormula('\t=1+1')).toBe("'\t=1+1");
    expect(neutralizeFormula('\r=1+1')).toBe("'\r=1+1");
    expect(neutralizeFormula('\n=1+1')).toBe("'\n=1+1");
    expect(neutralizeFormula('Monitor = 27"')).toBe('Monitor = 27"');
    expect(neutralizeFormula('')).toBe('');
  });

  it('en la celda: el texto peligroso va neutralizado y entre comillas; un número negativo NO se toca', () => {
    expect(csvCell('=1+1')).toBe(`"'=1+1"`);
    expect(csvCell('-5')).toBe(`"'-5"`);
    expect(csvCell(-5)).toBe('-5');
    expect(csvCell(-1234.5)).toBe('-1234,5');
  });
});

describe('celdas', () => {
  it('escapa las comillas duplicándolas y encierra el texto entre comillas', () => {
    expect(csvCell('Lucía "La Profe" Mamani')).toBe('"Lucía ""La Profe"" Mamani"');
    expect(csvCell('uno;dos')).toBe('"uno;dos"');
    expect(csvCell('línea 1\nlínea 2')).toBe('"línea 1\nlínea 2"');
  });

  it('números con coma decimal y sin miles; sí/no en palabras; fechas con la hora de La Paz; vacíos sin nada', () => {
    expect(csvCell(1234.5)).toBe('1234,5');
    expect(csvCell(1234567)).toBe('1234567');
    expect(csvCell(0.125)).toBe('0,125');
    expect(csvCell(Number.NaN)).toBe('');
    expect(csvCell(true)).toBe('"Sí"');
    expect(csvCell(false)).toBe('"No"');
    expect(csvCell(new Date('2026-09-29T01:30:00Z'))).toBe('"28/09/2026 21:30"');
    expect(csvCell(null)).toBe('');
    expect(csvCell(undefined)).toBe('');
    expect(csvCell('')).toBe('');
  });
});

describe('archivo completo', () => {
  it('BOM, encabezados, separador «;» y CRLF', () => {
    const csv = buildCsv(COLUMNS, ROWS);
    expect(csv.startsWith(CSV_BOM)).toBe(true);
    const lines = csv.slice(1).split('\r\n');
    expect(lines[0]).toBe('"Número";"Cliente";"Total";"Pagada";"Fecha";"Notas"');
    expect(lines[1]).toBe('"F-CM-000123";"Lucía ""La Profe"" Mamani";1234,5;"Sí";"28/09/2026 21:30";"Retira; el sábado"');
    // El salto de línea dentro de las notas queda DENTRO de las comillas (no parte la fila).
    expect(csv).toContain('"F-CM-000124";"Carlos Quispe";-120;"No";;"línea 1\nlínea 2"\r\n');
    expect(csv.endsWith('\r\n')).toBe(true);
  });

  it('se puede pedir sin BOM', () => {
    expect(buildCsv(COLUMNS.slice(0, 1), [], { bom: false })).toBe('"Número"\r\n');
  });

  it('las columnas de una tabla sirven para el CSV (sin las que no tienen valor o se excluyen)', () => {
    const columns = csvColumnsOf<Row>([
      { header: 'Número', value: (row) => row.number },
      { header: 'Acciones' },
      { header: 'Interno', value: (row) => row.notes, csv: false },
    ]);
    expect(columns.map((column) => column.header)).toEqual(['Número']);
  });

  it('el nombre del archivo va sin acentos ni símbolos y con la fecha de La Paz', () => {
    const now = new Date('2026-09-29T01:30:00Z');
    expect(csvFileName('Ventas del día', now)).toBe('ventas-del-dia-2026-09-28.csv');
    expect(csvFileName('  Stock / Sucursal CM  ', now)).toBe('stock-sucursal-cm-2026-09-28.csv');
    expect(csvFileName('reporte.csv', now)).toBe('reporte-2026-09-28.csv');
    expect(csvFileName('¡!', now)).toBe('exportacion-2026-09-28.csv');
  });
});

/** Bytes del archivo (con FileReader, como en el navegador). */
function bytesOf(blob: Blob): Promise<Uint8Array> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(new Uint8Array(reader.result as ArrayBuffer));
    reader.onerror = () => reject(reader.error);
    reader.readAsArrayBuffer(blob);
  });
}

describe('descarga', () => {
  const original = { create: URL.createObjectURL, revoke: URL.revokeObjectURL };

  afterEach(() => {
    URL.createObjectURL = original.create;
    URL.revokeObjectURL = original.revoke;
    vi.restoreAllMocks();
    vi.useRealTimers();
  });

  it('arma el archivo (UTF-8 con BOM), lo descarga con un enlace temporal y libera la dirección', async () => {
    vi.useFakeTimers();
    let blob: Blob | null = null;
    URL.createObjectURL = vi.fn((value: Blob | MediaSource) => {
      blob = value as Blob;
      return 'blob:interno.invalid/archivo';
    });
    URL.revokeObjectURL = vi.fn();
    const clicked: { download: string; href: string | null }[] = [];
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      clicked.push({ download: this.download, href: this.getAttribute('href') });
    });

    const name = exportCsv('ventas', COLUMNS, ROWS, { now: new Date('2026-09-29T01:30:00Z') });

    expect(name).toBe('ventas-2026-09-28.csv');
    expect(click).toHaveBeenCalledTimes(1);
    expect(clicked).toEqual([{ download: 'ventas-2026-09-28.csv', href: 'blob:interno.invalid/archivo' }]);
    expect(document.querySelector('a[download]')).toBeNull();
    expect(URL.revokeObjectURL).not.toHaveBeenCalled();
    vi.runAllTimers();
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:interno.invalid/archivo');
    vi.useRealTimers();

    expect(blob).not.toBeNull();
    expect(blob!.type).toBe('text/csv;charset=utf-8');
    const bytes = await bytesOf(blob!);
    expect([...bytes.slice(0, 3)]).toEqual([0xef, 0xbb, 0xbf]);
    expect(new TextDecoder('utf-8', { ignoreBOM: true }).decode(bytes)).toBe(buildCsv(COLUMNS, ROWS));
  });
});
