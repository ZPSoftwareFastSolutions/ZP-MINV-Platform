// Módulo «Stock» · ESTADÍSTICA del tablero «Valor del inventario»: vive plegada detrás de «Ver estadísticas» (regla
// P-10). Se descarga y consulta recién al abrirla (`GetStockProjectionQuery` del almacén de trabajo), con su propio estado
// de carga y de error. El título lo pone el tablero.

import { Boxes, Coins, PackageSearch } from 'lucide-react';
import { ROUTES } from '@/4-presentation/app/routes';
import { useRpcQuery } from '@/4-presentation/panel/hooks';
import { BarList, Button, ErrorState, StatCard } from '@/4-presentation/panel/kit';
import { formatDate, formatMoney, formatNumber } from '@/4-presentation/panel/lib';
import { inventorySummary } from './stock';

export function StockValueStat() {
  const projection = useRpcQuery('GetStockProjectionQuery', { warehouseCode: null });
  if (projection.error) return <ErrorState error={projection.error} operation="GetStockProjectionQuery" onRetry={projection.reload} retrying={projection.fetching} />;

  const data = projection.data;
  const summary = data ? inventorySummary(data.result.stock) : null;
  const loading = summary === null;
  return (
    <div className="space-y-4" data-testid="valor-del-inventario">
      <div className="grid grid-cols-[repeat(auto-fit,minmax(9rem,1fr))] gap-3">
        <StatCard
          label="Valor"
          value={summary ? formatMoney(summary.value) : ''}
          hint={data ? `Almacén ${data.warehouseCode} · al ${formatDate(data.today)}` : undefined}
          icon={<Coins />}
          loading={loading}
        />
        <StatCard label="Productos con stock" value={summary ? `${formatNumber(summary.withStock)} de ${formatNumber(summary.active)}` : ''} icon={<Boxes />} loading={loading} />
      </div>
      {summary && summary.byCategory.length > 0 && (
        <BarList label="Valor por categoría" items={summary.byCategory.slice(0, 5)} format={(value) => formatMoney(value)} />
      )}
      {summary && (
        <Button to={ROUTES.panelModule('stock')} variant="outline" leftIcon={<PackageSearch />}>
          Consultar el stock
        </Button>
      )}
    </div>
  );
}
