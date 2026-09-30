// Módulo «Contabilidad» · funciones PURAS (sin React): tipos de cuenta y estados, el origen de cada asiento (venta,
// compra, anulación…), filtros del libro diario y del plan de cuentas, el código sugerido para una cuenta nueva, las
// plantillas y la validación del asiento manual (comodidad: el servidor vuelve a validar la partida doble, regla P-01),
// y las columnas del CSV. Los tipos del servidor SALEN DEL CONTRATO (regla P-07).

import type { RpcRequestOf, RpcResponseOf } from '@/4-presentation/app/contract';
import { defineStatuses, statusOf } from '@/4-presentation/panel/kit';
import { formatMoney, isIsoDate, laPazToday, matchesSearch, type CsvColumn } from '@/4-presentation/panel/lib';
import { PERIOD_FILTERS } from './period';

// ---------------------------------------------------------------------------------------------------- tipos del servidor

export type AccountRecord = RpcResponseOf<'GetChartOfAccountsQuery'>[number];
export type JournalRecord = RpcResponseOf<'GetJournalQuery'>[number];
export type JournalLineRecord = JournalRecord['lines'][number];
export type StatementData = RpcResponseOf<'GetIncomeStatementQuery'>;
export type EntryPayload = RpcRequestOf<'CreateJournalEntryCommand'>;
export type AccountPayload = RpcRequestOf<'CreateAccountCommand'>;

// ---------------------------------------------------------------------------------------------------- vistas

export type AccountingView = 'resultados' | 'diario' | 'cuentas';

export const VIEW_PARAM = 'vista';
export const DEFAULT_VIEW: AccountingView = 'resultados';
/** `?nuevo=asiento` o `?nuevo=cuenta` abre el formulario (también desde el tablero). */
export const NEW_PARAM = 'nuevo';

export const VIEWS: readonly { id: AccountingView; label: string }[] = [
  { id: 'resultados', label: 'Estado de resultados' },
  { id: 'diario', label: 'Libro diario' },
  { id: 'cuentas', label: 'Plan de cuentas' },
];

export function viewOf(value: string | null): AccountingView {
  return VIEWS.find((view) => view.id === value)?.id ?? DEFAULT_VIEW;
}

/** Parámetros que se conservan al cambiar de pestaña (el período y la cuenta elegida). */
export const SHARED_PARAMS: readonly string[] = [VIEW_PARAM, ...Object.keys(PERIOD_FILTERS)];

// ---------------------------------------------------------------------------------------------------- cuentas

/** Tipo de cuenta (enumeración `AccountType` del servidor). */
export const ACCOUNT_TYPES = defineStatuses({
  Asset: { label: 'Activo', tone: 'info' },
  Liability: { label: 'Pasivo', tone: 'warning' },
  Equity: { label: 'Patrimonio', tone: 'accent' },
  Revenue: { label: 'Ingreso', tone: 'success' },
  Expense: { label: 'Costo o gasto', tone: 'danger' },
});

export function accountTypeLabel(type: string): string {
  return statusOf(ACCOUNT_TYPES, type).label;
}

/** «6.1.01 · Sueldos y salarios». */
export function accountLabel(account: Pick<AccountRecord, 'code' | 'name'>): string {
  return `${account.code} · ${account.name}`;
}

/** Cuentas imputables (las únicas que llevan asientos). */
export function postableAccounts(accounts: readonly AccountRecord[]): AccountRecord[] {
  return accounts.filter((account) => account.isPostable);
}

/** Grupos (bajo ellos se crean cuentas nuevas). */
export function groupAccounts(accounts: readonly AccountRecord[]): AccountRecord[] {
  return accounts.filter((account) => !account.isPostable);
}

/** Filtros del plan de cuentas (en la dirección). `saldos=periodo` muestra los saldos del período elegido. */
export const CHART_FILTERS = { ...PERIOD_FILTERS, tipo: '', clase: '', movimiento: '', saldos: '', q: '' };
export type ChartFilters = typeof CHART_FILTERS;

export const CLASS_OPTIONS: readonly { value: string; label: string }[] = [
  { value: 'grupos', label: 'Solo grupos' },
  { value: 'imputables', label: 'Solo cuentas imputables' },
];
export const ACTIVITY_OPTIONS: readonly { value: string; label: string }[] = [
  { value: 'con', label: 'Con movimiento' },
  { value: 'sin', label: 'Sin movimiento' },
];
export const BALANCE_OPTIONS: readonly { value: string; label: string }[] = [{ value: 'periodo', label: 'Del período elegido' }];

export function hasActivity(account: AccountRecord): boolean {
  return account.debit !== 0 || account.credit !== 0;
}

export function filterAccounts(accounts: readonly AccountRecord[], filters: ChartFilters): AccountRecord[] {
  return accounts.filter(
    (account) =>
      (!filters.tipo || account.type === filters.tipo) &&
      (!filters.clase || (filters.clase === 'grupos' ? !account.isPostable : account.isPostable)) &&
      (!filters.movimiento || (filters.movimiento === 'con' ? hasActivity(account) : !hasActivity(account))) &&
      matchesSearch(filters.q, [account.code, account.name]),
  );
}

/** Subcuentas directas de una cuenta. */
export function childrenOf(accounts: readonly AccountRecord[], code: string): AccountRecord[] {
  return accounts.filter((account) => account.parentCode === code);
}

/**
 * Código sugerido para una cuenta nueva bajo un grupo: el siguiente número después de la última subcuenta, con las
 * mismas cifras que sus hermanas («6.1.04» → «6.1.05»; sin hermanas, dos cifras debajo de un grupo de segundo nivel).
 */
export function suggestCode(accounts: readonly AccountRecord[], parentCode: string): string {
  const suffixes = childrenOf(accounts, parentCode)
    .map((account) => account.code.slice(parentCode.length + 1))
    .filter((suffix) => /^\d+$/.test(suffix));
  const next = suffixes.reduce((max, suffix) => Math.max(max, Number(suffix)), 0) + 1;
  const width = suffixes.length > 0 ? Math.max(...suffixes.map((suffix) => suffix.length)) : parentCode.includes('.') ? 2 : 1;
  return `${parentCode}.${String(next).padStart(width, '0')}`;
}

/** Errores del formulario «Nueva cuenta» (el servidor valida igual: grupo, prefijo y código repetido). */
export interface AccountFormErrors {
  parent?: string;
  code?: string;
  name?: string;
}

export const ACCOUNT_CODE_MAX = 20;
export const ACCOUNT_NAME_MAX = 120;

export function accountFormErrors(form: { parentCode: string; code: string; name: string }, accounts: readonly AccountRecord[]): AccountFormErrors {
  const errors: AccountFormErrors = {};
  const code = form.code.trim();
  const name = form.name.trim();
  const parent = accounts.find((account) => account.code === form.parentCode);
  if (!parent) errors.parent = 'Elija el grupo donde va la cuenta.';
  else if (parent.isPostable) errors.parent = 'Las cuentas nuevas se crean bajo un grupo, no bajo una cuenta imputable.';
  if (!code) errors.code = 'Escriba el código de la cuenta.';
  else if (parent && !code.startsWith(`${parent.code}.`)) errors.code = `El código debe empezar con ${parent.code}.`;
  else if (parent && code.length <= parent.code.length + 1) errors.code = `Complete el código después de ${parent.code}.`;
  else if (code.length > ACCOUNT_CODE_MAX) errors.code = `Use como máximo ${ACCOUNT_CODE_MAX} caracteres.`;
  else if (accounts.some((account) => account.code === code)) errors.code = `Ya existe la cuenta ${code}.`;
  if (name.length < 2) errors.name = 'Escriba el nombre de la cuenta.';
  else if (name.length > ACCOUNT_NAME_MAX) errors.name = `Use como máximo ${ACCOUNT_NAME_MAX} caracteres.`;
  return errors;
}

export const ACCOUNT_CSV: readonly CsvColumn<AccountRecord>[] = [
  { header: 'Código', value: (account) => account.code },
  { header: 'Cuenta', value: (account) => account.name },
  { header: 'Tipo', value: (account) => accountTypeLabel(account.type) },
  { header: 'Nivel', value: (account) => account.level },
  { header: 'Imputable', value: (account) => account.isPostable },
  { header: 'Cuenta padre', value: (account) => account.parentCode },
  { header: 'Debe', value: (account) => account.debit },
  { header: 'Haber', value: (account) => account.credit },
  { header: 'Saldo', value: (account) => account.balance },
];

// ---------------------------------------------------------------------------------------------------- libro diario

/** Estado de un asiento (enumeración `JournalEntryStatus`). */
export const ENTRY_STATUSES = defineStatuses({
  Posted: { label: 'Contabilizado', tone: 'success' },
  Draft: { label: 'Borrador', tone: 'neutral' },
});

/** Origen de un asiento, según su descripción (el servidor no lo informa aparte: ver «Pendientes» del informe M9). */
export const ORIGINS = defineStatuses({
  venta: { label: 'Venta', tone: 'success' },
  devolucion: { label: 'Devolución', tone: 'warning' },
  anulacion: { label: 'Anulación', tone: 'danger' },
  compra: { label: 'Compra', tone: 'info' },
  transferencia: { label: 'Transferencia', tone: 'accent' },
  inventario: { label: 'Inventario', tone: 'warning' },
  garantia: { label: 'Garantía', tone: 'accent' },
  manual: { label: 'Manual', tone: 'neutral' },
});
export type Origin = keyof typeof ORIGINS;

/** Texto sin acentos ni mayúsculas (para reconocer el comienzo de la descripción). */
function plain(text: string): string {
  return text.normalize('NFD').replace(/\p{Diacritic}/gu, '').toLowerCase().trim();
}

/** El origen de un asiento por su descripción, como el escritorio (y además devoluciones, transferencias y garantías). */
export function originOf(description: string): Origin {
  const text = plain(description);
  if (text.startsWith('venta')) return 'venta';
  if (text.startsWith('devolucion')) return 'devolucion';
  if (text.startsWith('anulacion')) return 'anulacion';
  if (text.startsWith('compra') || text.startsWith('recepcion') || text.startsWith('credito fiscal')) return 'compra';
  if (text.startsWith('transferencia')) return 'transferencia';
  if (text.startsWith('reposicion por garantia')) return 'garantia';
  if (text.includes('toma fisica') || text.includes('conteo') || text.includes('ajuste')) return 'inventario';
  return 'manual';
}

/** Filtros del libro diario (en la dirección). `cuenta` es un código imputable. */
export const JOURNAL_FILTERS = { ...PERIOD_FILTERS, cuenta: '', origen: '', estado: '', q: '' };
export type JournalFilters = typeof JOURNAL_FILTERS;

/** El servidor devuelve como máximo esta cantidad de asientos por pedido. */
export const JOURNAL_LIMIT = 5000;

/** Un asiento listo para mostrar. */
export interface JournalItem {
  key: string;
  record: JournalRecord;
  origin: Origin;
  debit: number;
  credit: number;
}

function cents(value: number): number {
  return Math.round(value * 100);
}

export function toJournalItems(records: readonly JournalRecord[]): JournalItem[] {
  return records.map((record) => ({
    key: record.number,
    record,
    origin: originOf(record.description),
    debit: record.lines.reduce((sum, line) => sum + cents(line.debit), 0) / 100,
    credit: record.lines.reduce((sum, line) => sum + cents(line.credit), 0) / 100,
  }));
}

export function filterJournal(items: readonly JournalItem[], filters: JournalFilters): JournalItem[] {
  return items.filter(
    (item) =>
      (!filters.cuenta || item.record.lines.some((line) => line.accountCode === filters.cuenta)) &&
      (!filters.origen || item.origin === filters.origen) &&
      (!filters.estado || item.record.status === filters.estado) &&
      matchesSearch(filters.q, [item.record.number, item.record.description, ...item.record.lines.flatMap((line) => [line.accountCode, line.accountName, line.memo])]),
  );
}

/** Debe y Haber de UNA cuenta en los asientos que se ven (el «mayor» de la cuenta en el período). */
export function accountMovement(items: readonly JournalItem[], code: string): { debit: number; credit: number; entries: number } {
  let debit = 0;
  let credit = 0;
  let entries = 0;
  for (const item of items) {
    const lines = item.record.lines.filter((line) => line.accountCode === code);
    if (lines.length === 0) continue;
    entries += 1;
    for (const line of lines) {
      debit += cents(line.debit);
      credit += cents(line.credit);
    }
  }
  return { debit: debit / 100, credit: credit / 100, entries };
}

/** Una fila por LÍNEA de asiento (como el escritorio). */
export const JOURNAL_CSV: readonly CsvColumn<{ item: JournalItem; line: JournalLineRecord }>[] = [
  { header: 'Asiento', value: ({ item }) => item.record.number },
  { header: 'Fecha', value: ({ item }) => item.record.date },
  { header: 'Origen', value: ({ item }) => ORIGINS[item.origin].label },
  { header: 'Estado', value: ({ item }) => statusOf(ENTRY_STATUSES, item.record.status).label },
  { header: 'Descripción', value: ({ item }) => item.record.description },
  { header: 'Cuenta', value: ({ line }) => line.accountCode },
  { header: 'Nombre de la cuenta', value: ({ line }) => line.accountName },
  { header: 'Debe', value: ({ line }) => line.debit },
  { header: 'Haber', value: ({ line }) => line.credit },
  { header: 'Glosa', value: ({ line }) => line.memo },
];

export function journalCsvRows(items: readonly JournalItem[]): { item: JournalItem; line: JournalLineRecord }[] {
  return items.flatMap((item) => item.record.lines.map((line) => ({ item, line })));
}

// ---------------------------------------------------------------------------------------------------- estado de resultados

export type StatementSection = 'ingresos' | 'costos' | 'gastos';

/** Una fila del estado de resultados (cuenta, subtotal o resultado). */
export interface StatementLine {
  key: string;
  kind: 'cuenta' | 'total' | 'resultado';
  section: StatementSection | null;
  code: string | null;
  name: string;
  amount: number;
  /** Parte de los ingresos (0 a 1); null sin ingresos. */
  ofRevenue: number | null;
}

export function statementLines(statement: StatementData): StatementLine[] {
  const share = (amount: number) => (statement.totalRevenue !== 0 ? amount / statement.totalRevenue : null);
  const accounts = (section: StatementSection, rows: readonly AccountRecord[]): StatementLine[] =>
    rows.map((row) => ({ key: `${section}-${row.code}`, kind: 'cuenta', section, code: row.code, name: row.name, amount: row.balance, ofRevenue: share(row.balance) }));
  const total = (key: string, name: string, amount: number, kind: 'total' | 'resultado' = 'total'): StatementLine => ({ key, kind, section: null, code: null, name, amount, ofRevenue: share(amount) });
  return [
    ...accounts('ingresos', statement.revenue),
    total('total-ingresos', 'Total ingresos', statement.totalRevenue),
    ...accounts('costos', statement.costs),
    total('total-costos', 'Total costo de ventas', statement.totalCosts),
    total('utilidad-bruta', 'Utilidad bruta', statement.grossProfit, 'resultado'),
    ...accounts('gastos', statement.expenses),
    total('total-gastos', 'Total gastos de operación', statement.totalExpenses),
    total('utilidad-neta', statement.netIncome >= 0 ? 'Utilidad neta del período' : 'Pérdida neta del período', statement.netIncome, 'resultado'),
  ];
}

export const STATEMENT_CSV: readonly CsvColumn<StatementLine>[] = [
  { header: 'Sección', value: (line) => (line.section === 'ingresos' ? 'Ingresos' : line.section === 'costos' ? 'Costo de ventas' : line.section === 'gastos' ? 'Gastos de operación' : '') },
  { header: 'Cuenta', value: (line) => line.code },
  { header: 'Concepto', value: (line) => line.name },
  { header: 'Monto', value: (line) => line.amount },
  { header: '% de los ingresos', value: (line) => (line.ofRevenue === null ? null : Math.round(line.ofRevenue * 1000) / 10) },
];

// ---------------------------------------------------------------------------------------------------- asiento manual

/** Una línea del formulario del asiento. */
export interface EntryLineDraft {
  id: number;
  account: string;
  debit: number | null;
  credit: number | null;
  memo: string;
}

export interface EntryTemplate {
  id: string;
  label: string;
  description: string;
  /** Cuentas y lado de cada línea (debe = true). */
  lines: readonly { account: string; debit: boolean }[];
}

/** Plantillas del escritorio (las cuentas se buscan en el plan: si una no existe, la línea queda sin cuenta). */
export const ENTRY_TEMPLATES: readonly EntryTemplate[] = [
  { id: 'deposito', label: 'Depósito de caja en el banco', description: 'Depósito del efectivo en el banco', lines: [{ account: '1.1.02', debit: true }, { account: '1.1.01', debit: false }] },
  { id: 'sueldos', label: 'Pago de sueldos', description: 'Pago de sueldos del mes', lines: [{ account: '6.1.01', debit: true }, { account: '1.1.02', debit: false }] },
  { id: 'alquiler', label: 'Pago de alquiler', description: 'Pago del alquiler del local', lines: [{ account: '6.1.02', debit: true }, { account: '1.1.02', debit: false }] },
  { id: 'servicios', label: 'Pago de servicios básicos', description: 'Pago de luz, agua e internet', lines: [{ account: '6.1.03', debit: true }, { account: '1.1.01', debit: false }] },
  { id: 'proveedores', label: 'Pago a proveedores', description: 'Pago a proveedores por transferencia', lines: [{ account: '2.1.01', debit: true }, { account: '1.1.02', debit: false }] },
  { id: 'capital', label: 'Aporte de capital', description: 'Aporte de capital de los socios', lines: [{ account: '1.1.02', debit: true }, { account: '3.1.01', debit: false }] },
  { id: 'administrativos', label: 'Gastos administrativos', description: 'Útiles de oficina y gastos varios', lines: [{ account: '6.1.04', debit: true }, { account: '1.1.01', debit: false }] },
];

/** Lo que llena el formulario al abrirlo (una plantilla o un asiento existente que se repite). */
export interface EntryPrefill {
  description: string;
  lines: readonly { account: string; debit: number | null; credit: number | null; memo: string }[];
}

export function templatePrefill(template: EntryTemplate, accounts: readonly AccountRecord[]): EntryPrefill {
  const known = new Set(postableAccounts(accounts).map((account) => account.code));
  return {
    description: template.description,
    lines: template.lines.map((line) => ({ account: known.has(line.account) ? line.account : '', debit: null, credit: null, memo: '' })),
  };
}

/** «Usar como plantilla»: las mismas cuentas, montos y glosas de un asiento del libro. */
export function entryPrefill(record: JournalRecord): EntryPrefill {
  return {
    description: record.description,
    lines: record.lines.map((line) => ({ account: line.accountCode, debit: line.debit > 0 ? line.debit : null, credit: line.credit > 0 ? line.credit : null, memo: line.memo ?? '' })),
  };
}

export const ENTRY_DESCRIPTION_MAX = 250;
export const ENTRY_MEMO_MAX = 200;

/** Totales en vivo del asiento (en centavos, sin errores del binario). */
export function entryTotals(lines: readonly EntryLineDraft[]): { debit: number; credit: number; difference: number; balanced: boolean } {
  const debit = lines.reduce((sum, line) => sum + cents(line.debit ?? 0), 0);
  const credit = lines.reduce((sum, line) => sum + cents(line.credit ?? 0), 0);
  return { debit: debit / 100, credit: credit / 100, difference: Math.abs(debit - credit) / 100, balanced: debit > 0 && debit === credit };
}

/** Texto del estado del asiento mientras se escribe. */
export function balanceText(lines: readonly EntryLineDraft[]): string {
  const totals = entryTotals(lines);
  if (totals.balanced) return 'Cuadrado: el debe es igual al haber.';
  if (totals.debit === 0 && totals.credit === 0) return 'Escriba los montos del debe y del haber.';
  return `No cuadra: la diferencia es ${formatMoney(totals.difference)}.`;
}

export interface EntryErrors {
  date?: string;
  description?: string;
  /** Por id de línea. */
  lines: Record<number, { account?: string; amount?: string; memo?: string }>;
  /** Error de todo el asiento (menos de dos líneas, no cuadra). */
  general?: string;
}

export function hasEntryErrors(errors: EntryErrors): boolean {
  return Boolean(errors.date || errors.description || errors.general || Object.keys(errors.lines).length > 0);
}

/** Validación del asiento en la página (la misma del servidor, para avisar antes de enviar). */
export function entryErrors(form: { date: string; description: string; lines: readonly EntryLineDraft[] }, now: Date = new Date()): EntryErrors {
  const errors: EntryErrors = { lines: {} };
  if (!isIsoDate(form.date)) errors.date = 'Elija la fecha del asiento.';
  else if (form.date > laPazToday(now)) errors.date = 'La fecha del asiento no puede ser futura.';
  const description = form.description.trim();
  if (description.length < 3) errors.description = 'Escriba la descripción del asiento (qué registra).';
  else if (description.length > ENTRY_DESCRIPTION_MAX) errors.description = `Use como máximo ${ENTRY_DESCRIPTION_MAX} caracteres.`;
  let withAmount = 0;
  for (const line of form.lines) {
    const debit = line.debit ?? 0;
    const credit = line.credit ?? 0;
    const problem: { account?: string; amount?: string; memo?: string } = {};
    if (debit > 0 || credit > 0) {
      withAmount += 1;
      if (!line.account) problem.account = 'Elija la cuenta de esta línea.';
      if (debit > 0 && credit > 0) problem.amount = 'Cada línea va al debe o al haber, no a los dos.';
    } else if (line.account) {
      problem.amount = 'Escriba el monto en el debe o en el haber (o quite la línea).';
    }
    if (line.memo.trim().length > ENTRY_MEMO_MAX) problem.memo = `Use como máximo ${ENTRY_MEMO_MAX} caracteres.`;
    if (Object.keys(problem).length > 0) errors.lines[line.id] = problem;
  }
  const totals = entryTotals(form.lines);
  if (withAmount < 2) errors.general = 'Un asiento necesita al menos dos líneas con monto.';
  else if (!totals.balanced) errors.general = `El asiento no cuadra: el total del debe debe ser igual al del haber (diferencia ${formatMoney(totals.difference)}).`;
  return errors;
}

/** El pedido exacto del comando (solo las líneas con monto; «sin glosa» es null). */
export function entryPayload(form: { date: string; description: string; lines: readonly EntryLineDraft[]; branchId: string | null }): EntryPayload {
  return {
    date: form.date,
    description: form.description.trim(),
    lines: form.lines
      .filter((line) => (line.debit ?? 0) > 0 || (line.credit ?? 0) > 0)
      .map((line) => ({ accountCode: line.account, debit: line.debit ?? 0, credit: line.credit ?? 0, memo: line.memo.trim() || null })),
    branchId: form.branchId,
  };
}
