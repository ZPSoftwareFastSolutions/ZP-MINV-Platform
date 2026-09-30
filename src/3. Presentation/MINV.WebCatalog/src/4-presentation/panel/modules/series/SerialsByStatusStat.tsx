// Módulo «Series» · la ESTADÍSTICA «Unidades por estado» (todas las series de la empresa, `GetSerialSummaryQuery`). Vive
// plegada: en el tablero detrás de «Ver estadísticas» y en la pantalla dentro de «Ver resumen de las unidades» (regla
// P-10). Se descarga y consulta recién al abrirse, con su propio estado de carga y de error. El título lo pone quien la
// muestra.

import { Package, PackageX, ShieldCheck, Wrench } from 'lucide-react';
import { ROUTES } from '@/4-presentation/app/routes';
import { useRpcQuery } from '@/4-presentation/panel/hooks';
import { BarList, Button, ErrorState, StatCard } from '@/4-presentation/panel/kit';
import { formatNumber } from '@/4-presentation/panel/lib';
import { productsText, summaryBars } from './serials';

export function SerialsByStatusStat() {
  const summary = useRpcQuery('GetSerialSummaryQuery', {});

  if (summary.error) return <ErrorState error={summary.error} operation="GetSerialSummaryQuery" onRetry={summary.reload} retrying={summary.fetching} />;

  const data = summary.data;
  const loading = !data;
  return (
    <div className="space-y-4" data-testid="series-por-estado">
      <div className="grid grid-cols-[repeat(auto-fit,minmax(9rem,1fr))] gap-3">
        <StatCard label="En stock" value={formatNumber(data?.inStock ?? 0)} hint={data ? productsText(data.inStockProducts) : undefined} icon={<Package />} tone="success" loading={loading} />
        <StatCard
          label="Vendidas con garantía vigente"
          value={formatNumber(data?.soldInWarranty ?? 0)}
          hint={data ? `de ${formatNumber(data.sold)} vendidas` : undefined}
          icon={<ShieldCheck />}
          loading={loading}
        />
        <StatCard
          label="En garantía (RMA) o devueltas"
          value={formatNumber(data?.inRmaOrReturned ?? 0)}
          hint="Esperan diagnóstico o destino"
          icon={<Wrench />}
          tone={data && data.inRmaOrReturned > 0 ? 'warning' : 'default'}
          loading={loading}
        />
        <StatCard label="Bajas y devueltas al proveedor" value={formatNumber(data?.out ?? 0)} hint="Fuera del inventario" icon={<PackageX />} loading={loading} />
      </div>
      {data && <BarList label={`Unidades por estado (${formatNumber(data.total)} en total)`} items={summaryBars(data)} format={(value) => formatNumber(value)} />}
      {data && data.inRmaOrReturned > 0 && (
        <Button to={ROUTES.panelModule('series?estado=InRma')} variant="outline" leftIcon={<Wrench />}>
          Ver las unidades en garantía
        </Button>
      )}
    </div>
  );
}
