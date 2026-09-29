// Módulo «Stock» · resumen del inventario que la pantalla muestra PLEGADO detrás de «Ver resumen del inventario» (regla
// P-10: nada de indicadores a la vista al entrar). Usa los datos que la lista ya cargó: no hace consultas propias.

import { Boxes, Clock, Coins, Truck, TriangleAlert } from 'lucide-react';
import { BarList, StatCard } from '@/4-presentation/panel/kit';
import { formatMoney, formatNumber } from '@/4-presentation/panel/lib';
import { alertCounts, inventorySummary, type ConsolidatedData, type StockAlertRecord, type StockRecord } from './stock';

const GRID = 'grid grid-cols-[repeat(auto-fit,minmax(11rem,1fr))] gap-3';

export interface WarehouseSummaryProps {
  rows: readonly StockRecord[];
  alerts: readonly StockAlertRecord[];
  /** Productos con unidades reservadas. */
  reservedProducts: number;
  /** Movimientos que procesó el cálculo. */
  movements: number;
}

/** Resumen de un almacén: valor, productos activos, alertas, reservas y valor por categoría. */
export function WarehouseSummary({ rows, alerts, reservedProducts, movements }: WarehouseSummaryProps) {
  const summary = inventorySummary(rows);
  const counts = alertCounts(alerts);
  return (
    <div className="space-y-5" data-testid="resumen-inventario">
      <div className={GRID}>
        <StatCard label="Valor del inventario" value={formatMoney(summary.value)} hint={`${formatNumber(summary.withStock)} productos con stock`} icon={<Coins />} />
        <StatCard
          label="Productos activos"
          value={`${formatNumber(summary.active)} de ${formatNumber(summary.products)}`}
          hint={`${formatNumber(movements)} movimientos procesados`}
          icon={<Boxes />}
        />
        <StatCard label="En alerta" value={formatNumber(counts.total)} tone={counts.total > 0 ? 'warning' : 'default'} icon={<TriangleAlert />} />
        <StatCard label="Con reservas" value={formatNumber(reservedProducts)} icon={<Clock />} />
      </div>
      <BarList label="Valor por categoría (las 6 mayores)" items={summary.byCategory.slice(0, 6)} format={(value) => formatMoney(value)} emptyText="Sin existencias valorizadas." />
    </div>
  );
}

/** Resumen del consolidado: valor total, en tránsito y valor por sucursal. */
export function ConsolidatedSummary({ data }: { data: ConsolidatedData }) {
  const byBranch = data.branches.map((branch, index) => ({ label: `${branch.code} · ${branch.name}`, value: data.valueByBranch[index] ?? 0 }));
  return (
    <div className="space-y-5" data-testid="resumen-inventario">
      <div className={GRID}>
        <StatCard label="Valor total" value={formatMoney(data.totalValue)} hint={`${formatNumber(data.rows.length)} productos con existencias`} icon={<Coins />} />
        <StatCard label="En tránsito entre sucursales" value={formatMoney(data.inTransitValue)} icon={<Truck />} />
      </div>
      <BarList label="Valor por sucursal" items={byBranch} format={(value) => formatMoney(value)} tone="accent" />
    </div>
  );
}
