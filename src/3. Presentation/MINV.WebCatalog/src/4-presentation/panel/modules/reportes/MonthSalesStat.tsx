// Módulo «Reportes» · ESTADÍSTICA del tablero «Ventas del mes vs mes anterior». Vive plegada detrás de «Ver
// estadísticas» (regla P-10): se descarga y consulta recién al abrirse (`GetSalesReportQuery` del mes en curso y del mes
// anterior completo). Compara el mes en curso con el MISMO tramo del mes anterior (del 1 al mismo día), que es lo justo
// a mitad de mes; el mes anterior completo queda como referencia. El título lo pone el tablero.

import { CalendarDays, Receipt, TrendingUp } from 'lucide-react';
import { useState } from 'react';
import { ROUTES } from '@/4-presentation/app/routes';
import { useRpcQuery } from '@/4-presentation/panel/hooks';
import { Button, ErrorState, StatCard } from '@/4-presentation/panel/kit';
import { formatMoney, formatNumber, formatPercent, laPazToday } from '@/4-presentation/panel/lib';
import { periodRange } from './period';
import { monthComparison } from './reports';

export function MonthSalesStat() {
  // El mes se fija al abrir (una estadística abierta a medianoche no cambia de mes sola).
  const [ranges] = useState(() => {
    const now = new Date();
    return { current: periodRange('esteMes', now), previous: periodRange('mesAnterior', now), today: laPazToday(now) };
  });
  const current = useRpcQuery('GetSalesReportQuery', ranges.current);
  const previous = useRpcQuery('GetSalesReportQuery', ranges.previous);

  if (current.error) return <ErrorState error={current.error} operation="GetSalesReportQuery" onRetry={current.reload} retrying={current.fetching} />;
  if (previous.error) return <ErrorState error={previous.error} operation="GetSalesReportQuery" onRetry={previous.reload} retrying={previous.fetching} />;

  const comparison = current.data && previous.data ? monthComparison(current.data, previous.data, ranges.today) : null;
  const loading = comparison === null;
  const day = Number(ranges.today.slice(8, 10));
  const trend =
    comparison && comparison.change !== null
      ? {
          direction: comparison.change > 0 ? ('up' as const) : comparison.change < 0 ? ('down' as const) : ('flat' as const),
          text: comparison.change === 0 ? 'Igual que el mes anterior' : `${formatPercent(Math.abs(comparison.change), 1)} ${comparison.change > 0 ? 'más' : 'menos'} que del 1 al ${day} del mes anterior`,
        }
      : undefined;

  return (
    <div className="space-y-4" data-testid="ventas-del-mes">
      <div className="grid grid-cols-[repeat(auto-fit,minmax(10rem,1fr))] gap-3">
        <StatCard
          label="Este mes"
          value={comparison ? formatMoney(comparison.current) : ''}
          hint={current.data ? `${formatNumber(current.data.tickets)} ventas · utilidad ${formatMoney(current.data.grossProfit)}` : undefined}
          icon={<Receipt />}
          trend={trend}
          tone="success"
          loading={loading}
        />
        <StatCard
          label={`Mes anterior, del 1 al ${day}`}
          value={comparison ? formatMoney(comparison.previousSamePeriod) : ''}
          hint={comparison ? `Mes completo: ${formatMoney(comparison.previousMonth)}` : undefined}
          icon={<TrendingUp />}
          loading={loading}
        />
      </div>
      {comparison && comparison.change === null && <p className="text-sm text-text-muted">El mes anterior no tuvo ventas en esos días: no hay con qué comparar.</p>}
      {comparison && (
        <Button to={ROUTES.panelModule('reportes?periodo=esteMes')} variant="outline" leftIcon={<CalendarDays />}>
          Ver el reporte de ventas del mes
        </Button>
      )}
    </div>
  );
}
