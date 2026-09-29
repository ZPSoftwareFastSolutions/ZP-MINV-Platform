// Módulo «Ventas» · ESTADÍSTICA del tablero «Ventas por día (7 días)». Vive plegada detrás de «Ver estadísticas» (regla
// P-10): se descarga y consulta (`GetSalesQuery` de los últimos 7 días) recién al abrirse, con su propio estado de carga y
// de error. Lo vendido cuenta sin las ventas anuladas. El título lo pone el tablero.

import { CalendarDays, Receipt, ShoppingCart } from 'lucide-react';
import { useState } from 'react';
import { ROUTES } from '@/4-presentation/app/routes';
import { useRpcQuery } from '@/4-presentation/panel/hooks';
import { Button, ErrorState, MiniBars, StatCard } from '@/4-presentation/panel/kit';
import { formatDate, formatMoney, formatNumber, roundTo } from '@/4-presentation/panel/lib';
import { lastDays, totalsByDay } from './sales';

const DAYS = 7;

export function SalesByDayStat() {
  // Los días se fijan al abrir (una estadística abierta a medianoche no cambia de día sola).
  const [range] = useState(() => lastDays(DAYS));
  const sales = useRpcQuery('GetSalesQuery', range);

  if (sales.error) return <ErrorState error={sales.error} operation="GetSalesQuery" onRetry={sales.reload} retrying={sales.fetching} />;

  const days = sales.data ? totalsByDay(sales.data, range) : null;
  const loading = days === null;
  const total = days ? roundTo(days.reduce((sum, day) => sum + day.total, 0), 2) : 0;
  const count = days ? days.reduce((sum, day) => sum + day.count, 0) : 0;
  const best = days?.reduce((top, day) => (day.total > top.total ? day : top), days[0]);
  return (
    <div className="space-y-4" data-testid="ventas-por-dia">
      <div className="grid grid-cols-[repeat(auto-fit,minmax(9rem,1fr))] gap-3">
        <StatCard label="Vendido en 7 días" value={formatMoney(total)} icon={<Receipt />} loading={loading} tone="success" />
        <StatCard label="Ventas" value={formatNumber(count)} hint={count > 0 ? `Promedio por día ${formatMoney(roundTo(total / DAYS, 2))}` : undefined} icon={<ShoppingCart />} loading={loading} />
      </div>
      {days && (
        <MiniBars
          label="Ventas de los últimos 7 días"
          points={days.map((day) => ({ label: formatDate(day.day).slice(0, 5), value: day.total }))}
          format={(value) => formatMoney(value)}
          showAxis
        />
      )}
      {best && best.total > 0 && <p className="text-sm text-text-muted">El mejor día: {formatDate(best.day)} con {formatMoney(best.total)}.</p>}
      {days && (
        <Button to={ROUTES.panelModule(`ventas?desde=${range.from}&hasta=${range.to}`)} variant="outline" leftIcon={<CalendarDays />}>
          Ver las ventas de estos 7 días
        </Button>
      )}
    </div>
  );
}
