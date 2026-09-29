// Módulo «Movimientos» · tendencia de entradas y salidas por día (`GetMovementTrendQuery`). Vive dentro del plegable
// «Ver tendencia» de la pantalla (regla P-10): se monta, y consulta, recién cuando la persona lo abre.

import { ArrowDownToLine, ArrowUpFromLine, Flag, ListChecks } from 'lucide-react';
import { useState } from 'react';
import { useRpcQuery } from '@/4-presentation/panel/hooks';
import { ErrorState, MiniBars, SelectField, StatCard } from '@/4-presentation/panel/kit';
import { formatNumber, formatQuantity } from '@/4-presentation/panel/lib';
import { TREND_DAYS, trendSummary } from './movements';

export function MovementTrend() {
  const [days, setDays] = useState('14');
  const trend = useRpcQuery('GetMovementTrendQuery', { days: Number(days) });
  const summary = trend.data ? trendSummary(trend.data) : null;
  const loading = summary === null;
  return (
    <div className="space-y-4" data-testid="tendencia-movimientos">
      <SelectField label="Período" allLabel={false} value={days} onChange={(value) => setDays(value || '14')} options={TREND_DAYS} className="max-w-xs" />
      {trend.error ? (
        <ErrorState error={trend.error} operation="GetMovementTrendQuery" onRetry={trend.reload} retrying={trend.fetching} />
      ) : (
        <>
          <div className="grid grid-cols-[repeat(auto-fit,minmax(10rem,1fr))] gap-3">
            <StatCard label="Entradas" value={formatQuantity(summary?.entries ?? 0)} tone="success" icon={<ArrowDownToLine />} loading={loading} />
            <StatCard label="Salidas" value={formatQuantity(summary?.issues ?? 0)} tone="warning" icon={<ArrowUpFromLine />} loading={loading} />
            <StatCard label="Movimientos" value={formatNumber(summary?.movements ?? 0)} icon={<ListChecks />} loading={loading} />
            {summary && summary.opening > 0 && <StatCard label="Saldo inicial (apertura)" value={formatQuantity(summary.opening)} icon={<Flag />} />}
          </div>
          {summary && (
            <div className="grid grid-cols-1 gap-6 md:grid-cols-2">
              <div>
                <p className="mb-2 text-sm font-semibold text-text">Entradas por día</p>
                <MiniBars label="Unidades que entraron por día" points={summary.entryPoints} tone="success" showAxis format={(value) => formatQuantity(value)} />
              </div>
              <div>
                <p className="mb-2 text-sm font-semibold text-text">Salidas por día</p>
                <MiniBars label="Unidades que salieron por día" points={summary.issuePoints} tone="warning" showAxis format={(value) => formatQuantity(value)} />
              </div>
            </div>
          )}
        </>
      )}
    </div>
  );
}
