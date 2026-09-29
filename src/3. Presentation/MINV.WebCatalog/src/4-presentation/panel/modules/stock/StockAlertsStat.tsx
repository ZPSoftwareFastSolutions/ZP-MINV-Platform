// Módulo «Stock» · ESTADÍSTICA del tablero «Productos en alerta»: plegada detrás de «Ver estadísticas» (regla P-10). Se
// descarga y consulta recién al abrirla (`GetStockProjectionQuery` del almacén de trabajo): cuántos productos están sin
// stock, críticos, bajos o con exceso, y el acceso a «Alertas». Las alertas las decide el servidor.

import { CircleX, PackageOpen, TrendingDown, TriangleAlert } from 'lucide-react';
import { ROUTES } from '@/4-presentation/app/routes';
import { useRpcQuery } from '@/4-presentation/panel/hooks';
import { BarList, Button, ErrorState, StatCard } from '@/4-presentation/panel/kit';
import { formatNumber } from '@/4-presentation/panel/lib';
import { alertCounts } from './stock';

export function StockAlertsStat() {
  const projection = useRpcQuery('GetStockProjectionQuery', { warehouseCode: null });
  if (projection.error) return <ErrorState error={projection.error} operation="GetStockProjectionQuery" onRetry={projection.reload} retrying={projection.fetching} />;

  const counts = projection.data ? alertCounts(projection.data.result.alerts) : null;
  const loading = counts === null;
  return (
    <div className="space-y-4" data-testid="productos-en-alerta">
      <div className="grid grid-cols-[repeat(auto-fit,minmax(8rem,1fr))] gap-3">
        <StatCard label="Sin stock" value={formatNumber(counts?.outOfStock ?? 0)} tone={counts && counts.outOfStock > 0 ? 'danger' : 'default'} icon={<CircleX />} loading={loading} />
        <StatCard label="Críticos" value={formatNumber(counts?.critical ?? 0)} tone={counts && counts.critical > 0 ? 'danger' : 'default'} icon={<TriangleAlert />} loading={loading} />
        <StatCard label="Bajos" value={formatNumber(counts?.low ?? 0)} tone={counts && counts.low > 0 ? 'warning' : 'default'} icon={<TrendingDown />} loading={loading} />
        <StatCard label="Exceso o inconsistentes" value={formatNumber(counts?.other ?? 0)} icon={<PackageOpen />} loading={loading} />
      </div>
      {counts && counts.byStatus.length > 0 && <BarList label="Alertas por tipo" items={counts.byStatus} format={(value) => formatNumber(value)} tone="warning" />}
      {counts && (
        <Button to={ROUTES.panelModule('alertas')} variant="outline" leftIcon={<TriangleAlert />}>
          {counts.total > 0 ? `Ver las ${formatNumber(counts.total)} alertas` : 'Ver alertas'}
        </Button>
      )}
    </div>
  );
}
