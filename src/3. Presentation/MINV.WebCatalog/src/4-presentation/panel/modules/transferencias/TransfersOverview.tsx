// Módulo «Transferencias» · los indicadores del escritorio (pendientes de despacho, en tránsito, recibidas y faltantes del
// mes). Se ven PLEGADOS (regla P-10): dentro de «Ver resumen de las transferencias» y en el tablero.

import { CircleCheck, Clock, Truck, TriangleAlert } from 'lucide-react';
import { StatCard } from '@/4-presentation/panel/kit';
import { formatMoney, formatNumber, formatQuantity } from '@/4-presentation/panel/lib';
import type { TransfersSummary } from './transfers';

export function TransfersOverview({ summary, loading = false }: { summary: TransfersSummary | null; loading?: boolean }) {
  const transitHint = !summary
    ? undefined
    : summary.transit.count === 0
      ? 'Nada en camino'
      : `${formatNumber(summary.transit.count)} transferencias · ${formatNumber(summary.transit.toReceive)} por recibir aquí`;
  const shortageHint = !summary
    ? undefined
    : summary.shortageThisMonth.transfers > 0
      ? `En ${formatNumber(summary.shortageThisMonth.transfers)} recepciones (merma en tránsito)`
      : 'Sin faltantes';
  return (
    <div className="grid grid-cols-[repeat(auto-fit,minmax(10rem,1fr))] gap-3" data-testid="resumen-transferencias">
      <StatCard
        label="Pendientes de despacho"
        value={formatNumber(summary?.pending.count ?? 0)}
        hint={summary ? (summary.pending.mine > 0 ? `${formatNumber(summary.pending.mine)} esperan su despacho` : 'Nada por despachar') : undefined}
        icon={<Clock />}
        tone={summary && summary.pending.mine > 0 ? 'warning' : 'default'}
        loading={loading}
      />
      <StatCard label="En tránsito" value={formatMoney(summary?.transit.value ?? 0)} hint={transitHint} icon={<Truck />} loading={loading} />
      <StatCard
        label="Recibidas este mes"
        value={formatNumber(summary?.receivedThisMonth.count ?? 0)}
        hint={summary ? `${formatMoney(summary.receivedThisMonth.value)} movidos entre sucursales` : undefined}
        icon={<CircleCheck />}
        tone="success"
        loading={loading}
      />
      <StatCard
        label="Faltantes este mes"
        value={formatQuantity(summary?.shortageThisMonth.quantity ?? 0)}
        hint={shortageHint}
        icon={<TriangleAlert />}
        tone={summary && summary.shortageThisMonth.quantity > 0 ? 'danger' : 'default'}
        loading={loading}
      />
    </div>
  );
}
