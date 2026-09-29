// Módulo «Pedido sugerido» · funciones puras (sin React): las líneas del pedido con la cantidad que la persona revisó,
// el agrupamiento por proveedor, los filtros, las opciones, el texto para copiar (correo o WhatsApp, como el escritorio:
// OrderViewModel.Text), el contenido de `CreatePurchaseOrderCommand` y las columnas de los CSV.
//
// Qué y cuánto pedir lo calcula el servidor (`GetStockProjectionQuery`: estados bajo el mínimo, hasta el máximo). La
// página solo permite REVISAR la cantidad y multiplica cantidad × costo para mostrar el subtotal: el importe de la orden
// lo vuelve a calcular el servidor al crearla (regla P-01).

import type { RpcRequestOf, RpcResponseOf } from '@/4-presentation/app/contract';
import { ROUTES } from '@/4-presentation/app/routes';
import { defineStatuses, statusOf } from '@/4-presentation/panel/kit';
import { formatDate, formatMoney, formatNumber, formatQuantity, matchesSearch, roundTo, type CsvColumn } from '@/4-presentation/panel/lib';

/** Respuesta de `GetStockProjectionQuery` (el pedido sugerido de la sucursal activa). */
export type ProjectionData = RpcResponseOf<'GetStockProjectionQuery'>;
/** Una línea del pedido sugerido tal como la manda el servidor. */
export type OrderLineRecord = ProjectionData['result']['order'][number];
/** Un proveedor (`GetSuppliersQuery`): de aquí salen su código y sus órdenes abiertas. */
export type SupplierRecord = RpcResponseOf<'GetSuppliersQuery'>[number];
/** Contenido de `CreatePurchaseOrderCommand`. */
export type PurchaseOrderPayload = RpcRequestOf<'CreatePurchaseOrderCommand'>;

/** Estados que entran al pedido (los que se reponen), como el semáforo del stock. */
export const ORDER_STATUSES = defineStatuses({
  OutOfStock: { label: 'Sin stock', tone: 'danger' },
  Critical: { label: 'Crítico', tone: 'danger' },
  Low: { label: 'Bajo', tone: 'warning' },
});

export function statusLabel(status: string): string {
  return statusOf(ORDER_STATUSES, status).label;
}

/** Nombre del grupo de los productos sin proveedor preferido (el del servidor: `StockProjection.NoSupplier`). */
export const NO_SUPPLIER = '(Sin proveedor)';

/** Observación de las órdenes creadas desde esta pantalla con las cantidades revisadas. */
export const REVIEWED_ORDER_NOTE = 'Generada desde el pedido sugerido (cantidades revisadas)';

/** Filtros de la lista (en la dirección). `proveedor` es el nombre del proveedor tal como lo agrupa el pedido. */
export const ORDER_FILTERS = { q: '', proveedor: '', categoria: '', estado: '' };
export type OrderFilters = typeof ORDER_FILTERS;

// ---------------------------------------------------------------------------------------------------- líneas

/** Una línea lista para la tabla: la sugerida por el servidor y la cantidad que se va a pedir. */
export interface OrderEntry {
  key: string;
  line: OrderLineRecord;
  category: string;
  /** Cantidad a pedir: la revisada por la persona o, si no la cambió, la sugerida. 0 = no se pide. */
  quantity: number;
  edited: boolean;
  subtotal: number;
}

/** Cantidades revisadas por SKU (null = campo vacío o mal escrito: no se pide). */
export type QuantityEdits = ReadonlyMap<string, number | null>;

export function toOrderEntries(lines: readonly OrderLineRecord[], categories: ReadonlyMap<string, string>, edits: QuantityEdits): OrderEntry[] {
  return lines.map((line) => {
    const edited = edits.has(line.sku);
    const quantity = edited ? (edits.get(line.sku) ?? 0) : line.quantityToOrder;
    return {
      key: line.sku,
      line,
      category: categories.get(line.sku) ?? 'Sin categoría',
      quantity,
      edited: edited && quantity !== line.quantityToOrder,
      subtotal: roundTo(quantity * line.unitCost, 2),
    };
  });
}

/** Categoría de cada SKU (sale de las filas de stock de la misma consulta). */
export function categoriesBySku(stock: ProjectionData['result']['stock']): Map<string, string> {
  return new Map(stock.map((row) => [row.sku, row.category]));
}

export function filterOrder(entries: readonly OrderEntry[], filters: OrderFilters): OrderEntry[] {
  return entries.filter(
    (entry) =>
      (!filters.proveedor || entry.line.supplier === filters.proveedor) &&
      (!filters.categoria || entry.category === filters.categoria) &&
      (!filters.estado || entry.line.status === filters.estado) &&
      matchesSearch(filters.q, [entry.line.sku, entry.line.name, entry.line.supplier, entry.category]),
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
    .map((value) => ({ value, label: value === NO_SUPPLIER ? 'Sin proveedor' : value }));
}

export function supplierOptions(entries: readonly OrderEntry[]): Option[] {
  return distinct(entries.map((entry) => entry.line.supplier));
}

export function categoryOptions(entries: readonly OrderEntry[]): Option[] {
  return distinct(entries.map((entry) => entry.category));
}

export function supplierName(name: string): string {
  return name === NO_SUPPLIER ? 'Sin proveedor' : name;
}

// ---------------------------------------------------------------------------------------------------- proveedores

/** El pedido de un proveedor: sus líneas, su total y lo que se sabe de él. */
export interface SupplierGroup {
  key: string;
  name: string;
  /** Código del proveedor (null si no está en la lista de proveedores o si son los productos sin proveedor). */
  code: string | null;
  active: boolean;
  /** Órdenes abiertas (borrador, aprobada o recibida en parte) del proveedor. */
  openOrders: number;
  entries: OrderEntry[];
  total: number;
  /** Líneas con cantidad mayor que 0 (las que irían en la orden). */
  toOrder: OrderEntry[];
  contact: string;
  phone: string;
  email: string;
  leadTimeDays: number | null;
  estimatedDelivery: string | null;
}

export function supplierGroups(entries: readonly OrderEntry[], suppliers: readonly SupplierRecord[]): SupplierGroup[] {
  const byName = new Map(suppliers.map((supplier) => [supplier.name.trim(), supplier]));
  const groups = new Map<string, OrderEntry[]>();
  for (const entry of entries) groups.set(entry.line.supplier, [...(groups.get(entry.line.supplier) ?? []), entry]);
  return [...groups.entries()].map(([name, list]) => {
    const first = list[0].line;
    const supplier = name === NO_SUPPLIER ? undefined : byName.get(name.trim());
    return {
      key: name,
      name,
      code: supplier?.code ?? null,
      active: supplier?.isActive ?? false,
      openOrders: supplier?.openOrders ?? 0,
      entries: list,
      total: roundTo(
        list.reduce((sum, entry) => sum + entry.subtotal, 0),
        2,
      ),
      toOrder: list.filter((entry) => entry.quantity > 0),
      contact: first.contact.trim() || supplier?.contact || '',
      phone: first.phone.trim() || supplier?.phone || '',
      email: first.email.trim() || supplier?.email || '',
      leadTimeDays: first.leadTimeDays ?? supplier?.leadTimeDays ?? null,
      estimatedDelivery: first.estimatedDelivery,
    };
  });
}

/** Por qué no se puede crear la orden de un proveedor desde aquí (null si se puede). */
export function cannotOrderReason(group: SupplierGroup): string | null {
  if (group.name === NO_SUPPLIER) return 'Estos productos no tienen proveedor preferido: asígnelo en el catálogo.';
  if (group.code === null) return 'El proveedor no está en la lista de proveedores.';
  if (!group.active) return 'El proveedor está inactivo.';
  if (group.toOrder.length === 0) return 'Todas las cantidades están en 0.';
  return null;
}

/** «Entrega en 5 días · llegaría el 04/10/2026» o «Plazo de entrega sin definir». */
export function deliveryText(group: Pick<SupplierGroup, 'leadTimeDays' | 'estimatedDelivery'>): string {
  if (group.leadTimeDays === null) return 'Plazo de entrega sin definir';
  const lead = `Entrega en ${group.leadTimeDays} ${group.leadTimeDays === 1 ? 'día' : 'días'}`;
  return group.estimatedDelivery ? `${lead} · llegaría el ${formatDate(group.estimatedDelivery)}` : lead;
}

/** Contenido de `CreatePurchaseOrderCommand` con las cantidades revisadas (solo las mayores que 0, todos los campos). */
export function purchaseOrderPayload(group: SupplierGroup, expectedDate: string | null, notes: string): PurchaseOrderPayload {
  return {
    supplierCode: group.code ?? '',
    expectedDate: expectedDate && expectedDate.trim() ? expectedDate : null,
    notes: notes.trim() ? notes.trim() : null,
    lines: group.toOrder.map((entry) => ({ sku: entry.line.sku, quantity: entry.quantity, unitCost: entry.line.unitCost })),
  };
}

/** Proveedores del pedido que ya tienen una orden abierta (la generación automática los omite). */
export function busySuppliers(groups: readonly SupplierGroup[]): SupplierGroup[] {
  return groups.filter((group) => group.openOrders > 0);
}

/** Texto del pedido de un proveedor para pegar en un correo o en WhatsApp (como «Copiar» del escritorio). */
export function orderText(group: SupplierGroup, today: string | null): string {
  const lines = [
    `Pedido sugerido${today ? ` · ${formatDate(today)}` : ''}`,
    `Proveedor: ${supplierName(group.name)}${group.phone ? ` · ${group.phone}` : ''}${group.email ? ` · ${group.email}` : ''}`,
    ...group.toOrder.map((entry) => `  ${entry.line.sku}  ${entry.line.name}  →  ${formatQuantity(entry.quantity, { unit: entry.line.unit })}  (${formatMoney(entry.subtotal)})`),
    `Total: ${formatMoney(group.total)}`,
  ];
  return lines.join('\n');
}

/** «12 productos · 3 proveedores». */
export function countText(products: number, suppliers: number): string {
  return `${formatNumber(products)} ${products === 1 ? 'producto' : 'productos'} · ${formatNumber(suppliers)} ${suppliers === 1 ? 'proveedor' : 'proveedores'}`;
}

// ---------------------------------------------------------------------------------------------------- enlaces

export function productCardLink(sku: string): string {
  return ROUTES.panelModule(`stock?ficha=${encodeURIComponent(sku)}`);
}

/** Órdenes de compra (módulo «Órdenes de compra»), opcionalmente las de un proveedor. */
export function purchaseOrdersLink(supplierCode?: string | null): string {
  return ROUTES.panelModule(supplierCode ? `compras?proveedor=${encodeURIComponent(supplierCode)}` : 'compras');
}

// ---------------------------------------------------------------------------------------------------- CSV

export const ORDER_CSV: readonly CsvColumn<OrderEntry>[] = [
  { header: 'Proveedor', value: (entry) => supplierName(entry.line.supplier) },
  { header: 'SKU', value: (entry) => entry.line.sku },
  { header: 'Producto', value: (entry) => entry.line.name },
  { header: 'Categoría', value: (entry) => entry.category },
  { header: 'Unidad', value: (entry) => entry.line.unit },
  { header: 'Semáforo', value: (entry) => statusLabel(entry.line.status) },
  { header: 'Existencias', value: (entry) => entry.line.stock },
  { header: 'Mínimo', value: (entry) => entry.line.minimum },
  { header: 'Máximo', value: (entry) => entry.line.maximum },
  { header: 'Sugerido', value: (entry) => entry.line.quantityToOrder },
  { header: 'A pedir', value: (entry) => entry.quantity },
  { header: 'Costo unitario', value: (entry) => entry.line.unitCost },
  { header: 'Subtotal', value: (entry) => entry.subtotal },
  { header: 'Entrega estimada', value: (entry) => (entry.line.estimatedDelivery ? formatDate(entry.line.estimatedDelivery) : null) },
  { header: 'Contacto', value: (entry) => entry.line.contact },
  { header: 'Teléfono', value: (entry) => entry.line.phone },
  { header: 'Correo', value: (entry) => entry.line.email },
];

export const SUPPLIER_CSV: readonly CsvColumn<SupplierGroup>[] = [
  { header: 'Proveedor', value: (group) => supplierName(group.name) },
  { header: 'Código', value: (group) => group.code },
  { header: 'Productos a pedir', value: (group) => group.toOrder.length },
  { header: 'Total', value: (group) => group.total },
  { header: 'Plazo de entrega (días)', value: (group) => group.leadTimeDays },
  { header: 'Entrega estimada', value: (group) => (group.estimatedDelivery ? formatDate(group.estimatedDelivery) : null) },
  { header: 'Órdenes abiertas', value: (group) => group.openOrders },
  { header: 'Contacto', value: (group) => group.contact },
  { header: 'Teléfono', value: (group) => group.phone },
  { header: 'Correo', value: (group) => group.email },
];
