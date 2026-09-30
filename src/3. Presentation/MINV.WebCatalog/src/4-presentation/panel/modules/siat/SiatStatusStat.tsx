// Módulo «Estado del SIAT» · la ESTADÍSTICA que ofrece al tablero: «Conexión con el SIN». Vive plegada detrás de «Ver
// estadísticas» (regla P-10): se descarga y consulta (`GetSiatStatusQuery`) recién cuando se abre. El título lo pone el
// tablero.

import { Cloud, FileText, Flag, WifiOff } from 'lucide-react';
import { ROUTES } from '@/4-presentation/app/routes';
import { useRpcQuery } from '@/4-presentation/panel/hooks';
import { Button, ErrorState, StatCard } from '@/4-presentation/panel/kit';
import { formatMoney, formatNumber } from '@/4-presentation/panel/lib';
import { modesSummary } from './siat';

export function SiatStatusStat() {
  const status = useRpcQuery('GetSiatStatusQuery', {});
  if (status.error) return <ErrorState error={status.error} operation="GetSiatStatusQuery" onRetry={status.reload} retrying={status.fetching} />;

  const data = status.data;
  const summary = data ? modesSummary(data.points) : null;
  const loading = !data;
  const alerts = data?.alerts.filter((alert) => alert.severity === 'danger').length ?? 0;
  return (
    <div className="space-y-4" data-testid="conexion-sin">
      <div className="grid grid-cols-[repeat(auto-fit,minmax(9rem,1fr))] gap-3">
        <StatCard
          label="Puntos de venta"
          value={summary ? (summary.active === 0 ? 'Sin puntos' : summary.notOnline === 0 ? 'En línea' : `${summary.notOnline} sin conexión`) : ''}
          hint={summary?.text || undefined}
          tone={summary && summary.notOnline > 0 ? 'warning' : 'success'}
          icon={<Cloud />}
          loading={loading}
        />
        <StatCard label="Emitidos hoy" value={formatNumber(data?.documentsToday ?? 0)} hint={data ? formatMoney(data.billedToday) : undefined} icon={<FileText />} loading={loading} />
        <StatCard
          label="Por enviar"
          value={formatNumber((data?.pendingDocuments ?? 0) + (data?.offlineDocuments ?? 0))}
          hint={data && data.offlineDocuments > 0 ? `${formatNumber(data.offlineDocuments)} fuera de línea` : undefined}
          tone={data && data.pendingDocuments + data.offlineDocuments > 0 ? 'warning' : 'default'}
          icon={<WifiOff />}
          loading={loading}
        />
        <StatCard
          label="Eventos abiertos"
          value={formatNumber(data?.openEvents ?? 0)}
          hint={alerts > 0 ? `${formatNumber(alerts)} alertas urgentes` : undefined}
          tone={data && (data.openEvents > 0 || alerts > 0) ? 'danger' : 'default'}
          icon={<Flag />}
          loading={loading}
        />
      </div>
      {data && (
        <Button to={ROUTES.panelModule('siat')} variant="outline" leftIcon={<Cloud />}>
          Ver el estado del SIAT
        </Button>
      )}
    </div>
  );
}
