// Módulo «Sucursales» · funciones puras (sin React): cómo se presenta cada sucursal, filtros, columnas del CSV, el
// comparativo por sucursal (`GetBranchReportQuery`), el stock consolidado (`ConsolidatedStockQuery`), los formularios de
// alta y edición (con las reglas del servidor: códigos de letras y números de hasta 12, nombres de hasta 100, no dejar
// la empresa sin sucursales activas) y la asignación de usuarios por sucursal y por usuario (`AssignUserBranchesCommand`,
// que REEMPLAZA las sucursales de la persona: siempre al menos una).
//
// Los tipos del servidor SALEN DEL CONTRATO (regla P-07). Las validaciones de aquí son COMODIDAD: el servidor decide.

import type { RpcRequestOf, RpcResponseOf } from '@/4-presentation/app/contract';
import { ROUTES } from '@/4-presentation/app/routes';
import { defineStatuses, type SelectOption } from '@/4-presentation/panel/kit';
import { addDays, matchesSearch, type CsvColumn, type DateRange } from '@/4-presentation/panel/lib';

// ---------------------------------------------------------------------------------------------------- tipos del contrato

/** Una sucursal del directorio (`GetBranchesQuery`). */
export type BranchRecord = RpcResponseOf<'GetBranchesQuery'>[number];
/** El comparativo por sucursal (`GetBranchReportQuery`, del modelo de lectura). */
export type BranchReportData = RpcResponseOf<'GetBranchReportQuery'>;
export type BranchReportLine = BranchReportData['branches'][number];
/** El stock consolidado (`ConsolidatedStockQuery`): por sucursal y en tránsito. */
export type ConsolidatedData = RpcResponseOf<'ConsolidatedStockQuery'>;
export type ConsolidatedLine = ConsolidatedData['rows'][number];
/** Una persona (`GetUsersQuery`) con sus sucursales. */
export type UserRecord = RpcResponseOf<'GetUsersQuery'>[number];
export type CreateBranchPayload = RpcRequestOf<'CreateBranchCommand'>;
export type UpdateBranchPayload = RpcRequestOf<'UpdateBranchCommand'>;
export type AssignBranchesPayload = RpcRequestOf<'AssignUserBranchesCommand'>;

// ---------------------------------------------------------------------------------------------------- constantes

export const LIMITS = { code: 12, name: 100 } as const;
const CODE = /^[A-Za-z0-9]+$/;

export const BRANCH_STATES = defineStatuses({
  activa: { label: 'Activa', tone: 'success' },
  inactiva: { label: 'Inactiva', tone: 'neutral' },
});

export const ACCESS_OPTIONS: readonly SelectOption[] = [
  { value: 'mias', label: 'Mis sucursales (con datos)' },
  { value: 'otras', label: 'Otras sucursales' },
];

export const TRANSIT_OPTIONS: readonly SelectOption[] = [
  { value: 'con', label: 'Con mercadería en tránsito' },
  { value: 'sin', label: 'Sin mercadería en tránsito' },
];

/** Texto del servidor sin la marca «✔»/«✖» del principio. */
export function plainMessage(text: string | null | undefined): string {
  return (text ?? '').replace(/^[\s✔✖✓✗]+/u, '').trim();
}

/** Transferencias que salen de (o llegan a) una sucursal (módulo «Transferencias»). */
export function transfersLink(side: 'origen' | 'destino', code: string): string {
  return ROUTES.panelModule(`transferencias?${side}=${encodeURIComponent(code)}`);
}

export const IN_TRANSIT_LINK = ROUTES.panelModule('transferencias?estado=Dispatched');

// ---------------------------------------------------------------------------------------------------- lista

export interface BranchItem {
  key: string;
  row: BranchRecord;
  state: keyof typeof BRANCH_STATES;
}

export function toBranchItems(rows: readonly BranchRecord[]): BranchItem[] {
  return rows.map((row) => ({ key: row.code, row, state: row.isActive ? 'activa' : 'inactiva' }));
}

export const BRANCH_FILTERS = { q: '', estado: '', acceso: '' };
export type BranchFilters = typeof BRANCH_FILTERS;

export function filterBranches(items: readonly BranchItem[], filters: BranchFilters): BranchItem[] {
  return items.filter(
    (item) =>
      (!filters.estado || item.state === filters.estado) &&
      (!filters.acceso || (filters.acceso === 'mias') === item.row.isVisible) &&
      matchesSearch(filters.q, [item.row.code, item.row.name, ...item.row.warehouses]),
  );
}

export const BRANCHES_CSV: readonly CsvColumn<BranchItem>[] = [
  { header: 'Código', value: (item) => item.row.code },
  { header: 'Sucursal', value: (item) => item.row.name },
  { header: 'Almacenes', value: (item) => item.row.warehouses.join(', ') },
  { header: 'Usuarios', value: (item) => item.row.users },
  { header: 'Stock valorizado', value: (item) => item.row.stockValue },
  { header: 'Transferencias que salen', value: (item) => item.row.transfersOut },
  { header: 'Transferencias que llegan', value: (item) => item.row.transfersIn },
  { header: 'Activa', value: (item) => item.row.isActive },
  { header: 'Es de mis sucursales', value: (item) => item.row.isVisible },
];

// ---------------------------------------------------------------------------------------------------- comparativo

/** Rango por defecto del comparativo: los últimos 30 días (como el escritorio). */
export function defaultReportRange(today: string): DateRange {
  return { from: addDays(today, -29), to: today };
}

/** El pedido de `GetBranchReportQuery` (el servidor exige las dos fechas); null si el rango no sirve. */
export function reportRequest(range: DateRange, today: string): { from: string; to: string } | null {
  const from = range.from ?? range.to ?? today;
  const to = range.to ?? today;
  if (from > to) return null;
  return { from, to };
}

export const REPORT_CSV: readonly CsvColumn<BranchReportLine>[] = [
  { header: 'Código', value: (row) => row.code },
  { header: 'Sucursal', value: (row) => row.name },
  { header: 'Tickets', value: (row) => row.tickets },
  { header: 'Ventas', value: (row) => row.revenue },
  { header: 'IVA', value: (row) => row.tax },
  { header: 'Ticket promedio', value: (row) => row.averageTicket },
  { header: 'Participación (%)', value: (row) => row.sharePercent },
  { header: 'Stock valorizado', value: (row) => row.stockValue },
];

// ---------------------------------------------------------------------------------------------------- stock consolidado

export const STOCK_FILTERS = { q: '', categoria: '', transito: '' };
export type StockFilters = typeof STOCK_FILTERS;

export function filterConsolidated(rows: readonly ConsolidatedLine[], filters: StockFilters): ConsolidatedLine[] {
  return rows.filter(
    (row) =>
      (!filters.categoria || row.category === filters.categoria) &&
      (!filters.transito || (filters.transito === 'con') === row.inTransit > 0),
  );
}

export function categoryOptions(rows: readonly ConsolidatedLine[]): SelectOption[] {
  return [...new Set(rows.map((row) => row.category))].sort((a, b) => a.localeCompare(b, 'es')).map((value) => ({ value, label: value }));
}

/** Columnas del CSV del consolidado: una por sucursal (en el orden de la respuesta). */
export function consolidatedCsv(branches: ConsolidatedData['branches']): CsvColumn<ConsolidatedLine>[] {
  return [
    { header: 'SKU', value: (row) => row.sku },
    { header: 'Producto', value: (row) => row.name },
    { header: 'Categoría', value: (row) => row.category },
    { header: 'Unidad', value: (row) => row.unit },
    ...branches.map((branch, index) => ({ header: branch.code, value: (row: ConsolidatedLine) => row.byBranch[index] ?? 0 })),
    { header: 'En tránsito', value: (row) => row.inTransit },
    { header: 'Total', value: (row) => row.total },
    { header: 'Valor', value: (row) => row.value },
  ];
}

// ---------------------------------------------------------------------------------------------------- alta y edición

export interface BranchDraft {
  code: string;
  name: string;
  warehouseCode: string;
  warehouseName: string;
  createPosRegister: boolean;
}

export function emptyBranchDraft(): BranchDraft {
  return { code: '', name: '', warehouseCode: '', warehouseName: '', createPosRegister: true };
}

/** Propuestas del escritorio: el almacén se llama «ALM» + código y «Almacén » + el nombre (sin «Sucursal »). */
export function proposedWarehouseCode(code: string): string {
  return `ALM${code.trim().toUpperCase()}`;
}

export function proposedWarehouseName(name: string): string {
  const base = name.trim().replace(/^sucursal\s+/i, '');
  return base ? `Almacén ${base}` : '';
}

export type BranchProblems = Partial<Record<keyof BranchDraft, string>>;

export function codeProblem(value: string, what: string): string | undefined {
  const code = value.trim();
  if (!code) return `Indique el código ${what}.`;
  if (!CODE.test(code) || code.length > LIMITS.code) return `El código ${what}: letras y números, sin espacios (hasta ${LIMITS.code}).`;
  return undefined;
}

export function branchProblems(draft: BranchDraft, existing: readonly BranchRecord[]): BranchProblems {
  const problems: BranchProblems = {};
  const code = draft.code.trim().toUpperCase();
  problems.code = codeProblem(draft.code, 'de la sucursal');
  if (!problems.code && existing.some((branch) => branch.code.toUpperCase() === code)) problems.code = `Ya existe la sucursal ${code}.`;
  if (!draft.name.trim()) problems.name = 'Indique el nombre de la sucursal.';
  else if (draft.name.trim().length > LIMITS.name) problems.name = `Hasta ${LIMITS.name} caracteres.`;
  const warehouse = draft.warehouseCode.trim().toUpperCase();
  problems.warehouseCode = codeProblem(draft.warehouseCode, 'del almacén');
  if (!problems.warehouseCode && existing.some((branch) => branch.warehouses.some((item) => item.toUpperCase() === warehouse)))
    problems.warehouseCode = `Ya existe el almacén ${warehouse}.`;
  if (!draft.warehouseName.trim()) problems.warehouseName = 'Indique el nombre del almacén.';
  else if (draft.warehouseName.trim().length > LIMITS.name) problems.warehouseName = `Hasta ${LIMITS.name} caracteres.`;
  for (const key of Object.keys(problems) as (keyof BranchDraft)[]) if (!problems[key]) delete problems[key];
  return problems;
}

export function createBranchPayload(draft: BranchDraft): CreateBranchPayload {
  return {
    code: draft.code.trim().toUpperCase(),
    name: draft.name.trim(),
    warehouseCode: draft.warehouseCode.trim().toUpperCase(),
    warehouseName: draft.warehouseName.trim(),
    createPosRegister: draft.createPosRegister,
  };
}

/** Problema de la edición (nombre y no dejar la empresa sin sucursales activas), o null. */
export function editProblem(name: string, isActive: boolean, branch: BranchRecord, all: readonly BranchRecord[]): { name?: string; isActive?: string } {
  const problems: { name?: string; isActive?: string } = {};
  if (!name.trim()) problems.name = 'Indique el nombre de la sucursal.';
  else if (name.trim().length > LIMITS.name) problems.name = `Hasta ${LIMITS.name} caracteres.`;
  if (!isActive && branch.isActive && all.filter((item) => item.isActive).length <= 1) problems.isActive = 'No puede desactivar la única sucursal activa.';
  return problems;
}

export function updateBranchPayload(branch: BranchRecord, name: string, isActive: boolean): UpdateBranchPayload {
  return { code: branch.code, name: name.trim(), isActive };
}

// ---------------------------------------------------------------------------------------------------- usuarios por sucursal

/** El personal activo (las cuentas de cliente no trabajan en sucursales). */
export function staffUsers(users: readonly UserRecord[]): UserRecord[] {
  return users.filter((user) => user.isActive && !user.roles.includes('CLIENTE')).sort((a, b) => a.name.localeCompare(b.name, 'es'));
}

/** Códigos en el orden del directorio (los desconocidos al final). */
export function orderedCodes(codes: readonly string[], branches: readonly BranchRecord[]): string[] {
  const order = branches.map((branch) => branch.code);
  const unique = [...new Set(codes)];
  return unique.sort((a, b) => {
    const ia = order.indexOf(a);
    const ib = order.indexOf(b);
    return (ia < 0 ? Number.MAX_SAFE_INTEGER : ia) - (ib < 0 ? Number.MAX_SAFE_INTEGER : ib) || a.localeCompare(b);
  });
}

export interface MembershipChange {
  user: UserRecord;
  payload: AssignBranchesPayload;
  /** true = entra a la sucursal; false = sale. */
  joins: boolean;
}

/** Quiénes cambian al marcar o desmarcar personas en UNA sucursal (a cada uno se le envían todas sus sucursales). */
export function membershipChanges(users: readonly UserRecord[], branchCode: string, checked: ReadonlySet<string>, branches: readonly BranchRecord[]): MembershipChange[] {
  return users.flatMap((user) => {
    const has = user.branchCodes.includes(branchCode);
    const wants = checked.has(user.email);
    if (has === wants) return [];
    const codes = wants ? [...user.branchCodes, branchCode] : user.branchCodes.filter((code) => code !== branchCode);
    return [{ user, joins: wants, payload: { email: user.email, branchCodes: orderedCodes(codes, branches) } }];
  });
}

/** Personas que quedarían sin ninguna sucursal (el servidor exige al menos una). */
export function orphaned(changes: readonly MembershipChange[]): UserRecord[] {
  return changes.filter((change) => change.payload.branchCodes.length === 0).map((change) => change.user);
}
