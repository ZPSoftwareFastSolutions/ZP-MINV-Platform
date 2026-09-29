// Módulo «Actividad» · la ESTADÍSTICA que ofrece al tablero: «Actividad de hoy». Vive plegada detrás de «Ver
// estadísticas» (regla P-10): este componente se descarga y hace su consulta recién cuando se abre, con su propio estado
// de carga y de error. El título lo pone el tablero.

import { Activity, CircleX, History, ShieldAlert } from 'lucide-react';
import { useState } from 'react';
import { ROUTES } from '@/4-presentation/app/routes';
import { useRpcQuery } from '@/4-presentation/panel/hooks';
import { BarList, Button, ErrorState, StatCard } from '@/4-presentation/panel/kit';
import { formatNumber, formatTime } from '@/4-presentation/panel/lib';
import { todaySummary } from './activity';

/** Registros que se revisan para el resumen de hoy. */
const STAT_TAKE = 1000;

export function ActivityTodayStat() {
  const activity = useRpcQuery('GetActivityQuery', { take: STAT_TAKE });
  // «Hoy» se fija al abrir (una estadística abierta a medianoche no cambia de día sola).
  const [now] = useState(() => new Date());

  if (activity.error) return <ErrorState error={activity.error} operation="GetActivityQuery" onRetry={activity.reload} retrying={activity.fetching} />;

  const summary = activity.data ? todaySummary(activity.data, now, STAT_TAKE) : null;
  const loading = summary === null;
  const total = summary ? `${summary.capped ? 'Más de ' : ''}${formatNumber(summary.total)}` : '';
  return (
    <div className="space-y-4" data-testid="actividad-de-hoy">
      <div className="grid grid-cols-[repeat(auto-fit,minmax(9rem,1fr))] gap-3">
        <StatCard
          label="Operaciones"
          value={total}
          hint={summary?.lastAt ? `La última a las ${formatTime(summary.lastAt)}` : summary ? 'Ninguna todavía' : undefined}
          icon={<Activity />}
          loading={loading}
        />
        <StatCard
          label="Rechazadas"
          value={formatNumber(summary?.rejected ?? 0)}
          tone={summary && summary.rejected > 0 ? 'warning' : 'default'}
          icon={<ShieldAlert />}
          loading={loading}
        />
        <StatCard label="Con error" value={formatNumber(summary?.failed ?? 0)} tone={summary && summary.failed > 0 ? 'danger' : 'default'} icon={<CircleX />} loading={loading} />
      </div>
      {summary && summary.byUser.length > 0 && <BarList label="Quién operó hoy" items={summary.byUser.slice(0, 5)} format={(value) => formatNumber(value)} />}
      {summary && (
        <Button to={ROUTES.panelModule(`actividad?desde=${summary.day}&hasta=${summary.day}`)} variant="outline" leftIcon={<History />}>
          Ver la actividad de hoy
        </Button>
      )}
    </div>
  );
}
