// Exportación a CSV para Excel (regla P-10: toda lista exporta). Sin red: el archivo se arma en la página y se descarga
// con un enlace temporal (`blob:`).
//
//   exportCsv('ventas', [{ header: 'Número', value: (v) => v.number }, { header: 'Total', value: (v) => v.total }], filas)
//     → descarga «ventas-2026-09-28.csv»
//
// Formato pensado para Excel en español:
// - BOM UTF-8 al inicio (Excel respeta los acentos), separador «;» (el de listas en es-BO) y fin de línea CRLF.
// - Números sin separador de miles y con coma decimal («1234,5») y SIN comillas: Excel los toma como números.
// - Todo lo demás va entre comillas y las comillas internas se duplican (`"` → `""`): un «;» o un salto de línea dentro
//   del texto no rompe las columnas.
// - Fechas con hora de La Paz («28/09/2026 21:30») y sí/no en palabras.
// - Contra la inyección de fórmulas: un TEXTO que empieza con =, +, -, @, tabulador, retorno o salto de línea se
//   neutraliza con un apóstrofo delante («'=HYPERLINK(…)»), así Excel lo muestra como texto y no lo ejecuta. Los números
//   de tipo `number` no se tocan (un -120 sigue siendo un número).

import { formatDateTime, laPazToday } from './format';

export type CsvValue = string | number | boolean | Date | null | undefined;

export interface CsvColumn<T> {
  /** Encabezado de la columna en el archivo. */
  header: string;
  /** Valor de la celda para una fila. */
  value: (row: T) => CsvValue;
}

export const CSV_SEPARATOR = ';';
export const CSV_BOM = '﻿';
export const CSV_NEWLINE = '\r\n';

/** Primer carácter que Excel (o LibreOffice) puede tomar como el inicio de una fórmula. */
const FORMULA_START = /^[=+\-@\t\r\n]/;

const NUMBER = new Intl.NumberFormat('es-BO', { useGrouping: false, maximumFractionDigits: 10 });

/** Antepone un apóstrofo si el texto podría ejecutarse como fórmula. */
export function neutralizeFormula(text: string): string {
  return FORMULA_START.test(text) ? `'${text}` : text;
}

/** Una celda ya escrita para el archivo (con comillas si es texto). */
export function csvCell(value: CsvValue): string {
  if (value == null) return '';
  if (typeof value === 'number') return Number.isFinite(value) ? NUMBER.format(value) : '';
  let text: string;
  if (typeof value === 'boolean') text = value ? 'Sí' : 'No';
  else if (value instanceof Date) text = Number.isNaN(value.getTime()) ? '' : formatDateTime(value);
  else text = neutralizeFormula(value);
  if (text.length === 0) return '';
  return `"${text.replace(/"/g, '""')}"`;
}

export interface BuildCsvOptions {
  /** Agrega el BOM UTF-8 al inicio (por defecto sí: sin él Excel rompe los acentos). */
  bom?: boolean;
}

/** Contenido completo del archivo: encabezados y una línea por fila. */
export function buildCsv<T>(columns: readonly CsvColumn<T>[], rows: readonly T[], options: BuildCsvOptions = {}): string {
  const lines = [columns.map((column) => csvCell(column.header)).join(CSV_SEPARATOR)];
  for (const row of rows) lines.push(columns.map((column) => csvCell(column.value(row))).join(CSV_SEPARATOR));
  return `${options.bom === false ? '' : CSV_BOM}${lines.join(CSV_NEWLINE)}${CSV_NEWLINE}`;
}

/** Nombre del archivo: «Ventas del día» → «ventas-del-dia-2026-09-28.csv» (fecha de La Paz; sin acentos ni símbolos). */
export function csvFileName(name: string, now: Date = new Date()): string {
  const base =
    name
      .normalize('NFD')
      .replace(/\p{Diacritic}/gu, '')
      .toLowerCase()
      .replace(/\.csv$/, '')
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/^-+|-+$/g, '')
      .slice(0, 60) || 'exportacion';
  return `${base}-${laPazToday(now)}.csv`;
}

export interface ExportCsvOptions extends BuildCsvOptions {
  /** Fecha para el nombre del archivo (pruebas). */
  now?: Date;
}

/**
 * Arma el CSV y lo descarga en el navegador. Devuelve el nombre del archivo (para el aviso «Se descargó …»).
 * El enlace temporal se quita enseguida y la URL del archivo se libera después de la descarga.
 */
export function exportCsv<T>(name: string, columns: readonly CsvColumn<T>[], rows: readonly T[], options: ExportCsvOptions = {}): string {
  const fileName = csvFileName(name, options.now);
  const blob = new Blob([buildCsv(columns, rows, options)], { type: 'text/csv;charset=utf-8' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  link.rel = 'noopener';
  link.hidden = true;
  document.body.appendChild(link);
  try {
    link.click();
  } finally {
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }
  return fileName;
}
