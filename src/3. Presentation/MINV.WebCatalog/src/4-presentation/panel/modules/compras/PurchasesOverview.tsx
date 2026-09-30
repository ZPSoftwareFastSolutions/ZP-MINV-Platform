// Módulo «Órdenes de compra» · los indicadores del escritorio (borradores, por recibir, recibido este mes y proveedores
// con órdenes). Se ven PLEGADOS (regla P-10): dentro de «Ver resumen de las compras» y en el tablero.

import { CircleCheck, ClipboardList, Clock, Truck } from 'lucide-react';
import { StatCard } from '@/4-presentation/panel/kit';
import { formatMoney, formatNumber } from '@/4-presentation/panel/lib';
import type { PurchaseSummary } from './purchasing';

export function PurchasesOverview({ summary, loading = false }: { summary: PurchaseSummary | null; loading?: boolean }) {
  return (
    <div className="grid grid-cols-[repeat(auto-fit,minmax(10rem,1fr))] gap-3" data-testid="resumen-compras">
      <StatCard
        label="Borradores"
        value={formatNumber(summary?.drafts.count ?? 0)}
        hint={summary ? (summary.drafts.count > 0 ? `${formatMoney(summary.drafts.total)} por aprobar` : 'Nada por aprobar') : undefined}
        icon={<ClipboardList />}
        tone={summary && summary.drafts.count > 0 ? 'warning' : 'default'}
        loading={loading}
      />
      <StatCard
        label="Por recibir"
        value={formatNumber(summary?.pending.count ?? 0)}
        hint={
          summary
            ? summary.pending.count > 0
              ? `${formatMoney(summary.pending.total)} · ${formatNumber(summary.pending.overdue)} vencen hoy o antes`
              : 'Sin pendientes'
            : undefined
        }
        icon={<Clock />}
        loading={loading}
      />
      <StatCard
        label="Recibido este mes"
        value={formatMoney(summary?.receivedThisMonth.total ?? 0)}
        hint={summary ? `${formatNumber(summary.receivedThisMonth.count)} órdenes recibidas` : undefined}
        icon={<CircleCheck />}
        tone="success"
        loading={loading}
      />
      <StatCard
        label="Proveedores con órdenes"
        value={formatNumber(summary?.suppliers ?? 0)}
        hint={summary ? `${formatNumber(summary.orders)} órdenes en total` : undefined}
        icon={<Truck />}
        loading={loading}
      />
    </div>
  );
}
