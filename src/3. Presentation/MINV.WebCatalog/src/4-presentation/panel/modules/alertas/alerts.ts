// Módulo «Alertas» · funciones puras (sin React): el tipo de cada alerta (semáforo), la acción sugerida (la de la V2.1,
// como el escritorio: AlertItem.Action), los filtros, las opciones de las listas desplegables, los textos, los enlaces a
// los otros módulos y las columnas del CSV. Las alertas, su prioridad y la cantidad sugerida las calcula el servidor
// (`GetStockProjectionQuery`): aquí solo se muestran, se filtran y se formatean (regla P-01).
//
// Los estados del semáforo y las opciones de sucursal se repiten en «Stock» y «Pedido sugerido»: un módulo no importa de
// otro (regla P-09). Ver «Pendientes» del informe M5 (convendría tenerlos en `panel/lib`).

import type { RpcResponseOf } from '@/4-presentation/app/contract';
import { ROUTES } from '@/4-presentation/app/routes';
import { defineStatuses, statusOf } from '@/4-presentation/panel/kit';
import { formatDate, formatNumber, formatQuantity, matchesSearch, type CsvColumn } from '@/4-presentation/panel/lib';

/** Respuesta de `GetStockProjectionQuery`. */
export type ProjectionData = RpcResponseOf<'GetStockProjectionQuery'>;
/** Una alerta priorizada tal como la manda el servidor. */
export type AlertRecord = ProjectionData['result']['alerts'][number];
/** Una sucursal del directorio (`GetBranchesQuery`). */
export type BranchRecord = RpcResponseOf<'GetBranchesQuery'>[number];

/** Tipos de alerta (estados del semáforo que la generan), en el orden de prioridad de la V2.1. */
export const ALERT_TYPES = defineStatuses({
  Inconsistent: { label: 'Inconsistente', tone: 'accent' },
  OutOfStock: { label: 'Sin stock', tone: 'danger' },
  Critical: { label: 'Crítico', tone: 'danger' },
  Low: { label: 'Bajo', tone: 'warning' },
  Overstock: { label: 'Exceso', tone: 'info' },
});

/** Acción sugerida de cada tipo (la de la V2.1, `StockRules.Defaults`). */
const ACTIONS: Readonly<Record<string, string>> = {
  Inconsistent: 'Revisar los movimientos y registrar un ajuste',
  OutOfStock: 'Reabastecer de inmediato',
  Critical: 'Emitir una orden de compra',
  Low: 'Programar la reposición',
  Overstock: 'Frenar compras y rotar el inventario',
};

const TYPE_ORDER = ['Inconsistent', 'OutOfStock', 'Critical', 'Low', 'Overstock'];

export function alertTypeLabel(status: string): string {
  return statusOf(ALERT_TYPES, status).label;
}

/** Posición del tipo para ordenar la columna «Tipo» (lo más urgente primero). */
export function alertTypeOrder(status: string): number {
  const index = TYPE_ORDER.indexOf(status);
  return index < 0 ? 99 : index;
}

export function suggestedAction(status: string): string {
  return ACTIONS[status] ?? 'Revisar';
}

/** ¿Se repone? (sin stock, crítico o bajo: son los que entran al pedido sugerido). */
export function isReplenishable(status: string): boolean {
  return status === 'OutOfStock' || status === 'Critical' || status === 'Low';
}

// ---------------------------------------------------------------------------------------------------- filtros

/** Filtros de la lista (en la dirección). `sucursal`: '' = almacén de la sucursal activa, o un código de almacén. */
export const ALERT_FILTERS = { q: '', tipo: '', categoria: '', proveedor: '', sucursal: '' };
export type AlertFilters = typeof ALERT_FILTERS;

/** Texto del proveedor de una alerta («Sin proveedor» si el producto no tiene preferido). */
export function supplierOf(alert: Pick<AlertRecord, 'supplier'>): string {
  return alert.supplier.trim() ? alert.supplier.trim() : 'Sin proveedor';
}

export function filterAlerts(alerts: readonly AlertRecord[], filters: AlertFilters): AlertRecord[] {
  return alerts.filter(
    (alert) =>
      (!filters.tipo || alert.status === filters.tipo) &&
      (!filters.categoria || alert.category === filters.categoria) &&
      (!filters.proveedor || supplierOf(alert) === filters.proveedor) &&
      matchesSearch(filters.q, [alert.sku, alert.name, alert.category, alert.supplier, alertTypeLabel(alert.status)]),
  );
}

export interface Option {
  value: string;
  label: string;
}

function distinct(values: Iterable<string>): Option[] {
  return [...new Set(values)]
    .filter((value) => value.trim().length > 0)
    .sort((a, b) => a.localeCompare(b, 'es'))
    .map((value) => ({ value, label: value }));
}

export function categoryOptions(alerts: readonly AlertRecord[]): Option[] {
  return distinct(alerts.map((alert) => alert.category));
}

export function supplierOptions(alerts: readonly AlertRecord[]): Option[] {
  return distinct(alerts.map(supplierOf));
}

/** Un almacén por sucursal visible y activa (con el almacén entre paréntesis si la sucursal tiene varios). */
export function warehouseOptions(branches: readonly BranchRecord[]): Option[] {
  return branches
    .filter((branch) => branch.isVisible && branch.isActive)
    .flatMap((branch) =>
      branch.warehouses.map((warehouse) => ({
        value: warehouse,
        label: branch.warehouses.length > 1 ? `${branch.code} · ${branch.name} (${warehouse})` : `${branch.code} · ${branch.name}`,
      })),
    );
}

export function activeWarehouseLabel(activeBranch: { code: string } | null): string {
  return activeBranch ? `Sucursal activa (${activeBranch.code})` : 'Almacén principal de la empresa';
}

// ---------------------------------------------------------------------------------------------------- textos

export function quantityWithUnit(value: number, unit: string): string {
  return formatQuantity(value, { unit });
}

/** «Último movimiento el 12/09/2026» o «Sin movimientos». */
export function lastMovementText(alert: Pick<AlertRecord, 'lastMovement'>): string {
  return alert.lastMovement ? `Último movimiento el ${formatDate(alert.lastMovement)}` : 'Sin movimientos';
}

export interface AlertCounts {
  total: number;
  byType: { status: string; label: string; count: number; action: string }[];
}

/** Cuántas alertas hay de cada tipo (en el orden de prioridad; los tipos sin alertas quedan fuera). */
export function alertCounts(alerts: readonly AlertRecord[]): AlertCounts {
  return {
    total: alerts.length,
    byType: TYPE_ORDER.map((status) => ({ status, label: alertTypeLabel(status), count: alerts.filter((alert) => alert.status === status).length, action: suggestedAction(status) })).filter(
      (item) => item.count > 0,
    ),
  };
}

/** «3 de 12 alertas». */
export function countText(visible: number, total: number): string {
  return `${formatNumber(visible)} de ${formatNumber(total)} ${total === 1 ? 'alerta' : 'alertas'}`;
}

// ---------------------------------------------------------------------------------------------------- enlaces

/** Ficha y kardex del producto (módulo «Stock»). */
export function productCardLink(sku: string): string {
  return ROUTES.panelModule(`stock?ficha=${encodeURIComponent(sku)}`);
}

/** Registrar una entrada del producto (módulo «Movimientos», con el diálogo abierto). */
export function entryLink(sku: string): string {
  return ROUTES.panelModule(`movimientos?registrar=ENTRADA&sku=${encodeURIComponent(sku)}`);
}

/** Registrar un ajuste del producto (para las inconsistentes y el exceso). */
export function adjustmentLink(sku: string): string {
  return ROUTES.panelModule(`movimientos?registrar=ajuste&sku=${encodeURIComponent(sku)}`);
}

/** El producto en el pedido sugerido (módulo «Pedido sugerido», buscado por su SKU). */
export function orderLink(sku?: string): string {
  return ROUTES.panelModule(sku ? `pedido?q=${encodeURIComponent(sku)}` : 'pedido');
}

// ---------------------------------------------------------------------------------------------------- CSV

export const ALERT_CSV: readonly CsvColumn<AlertRecord>[] = [
  { header: 'Prioridad', value: (alert) => alert.position },
  { header: 'Tipo', value: (alert) => alertTypeLabel(alert.status) },
  { header: 'SKU', value: (alert) => alert.sku },
  { header: 'Producto', value: (alert) => alert.name },
  { header: 'Categoría', value: (alert) => alert.category },
  { header: 'Proveedor', value: (alert) => supplierOf(alert) },
  { header: 'Unidad', value: (alert) => alert.unit },
  { header: 'Existencias', value: (alert) => alert.stock },
  { header: 'Mínimo', value: (alert) => alert.minimum },
  { header: 'Máximo', value: (alert) => alert.maximum },
  { header: 'Faltan', value: (alert) => alert.shortfall },
  { header: 'Sugerido', value: (alert) => alert.suggestedQuantity },
  { header: 'Acción sugerida', value: (alert) => suggestedAction(alert.status) },
  { header: 'Último movimiento', value: (alert) => (alert.lastMovement ? formatDate(alert.lastMovement) : null) },
];
