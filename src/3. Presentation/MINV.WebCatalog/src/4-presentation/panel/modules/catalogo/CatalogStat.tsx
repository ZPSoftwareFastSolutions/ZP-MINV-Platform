// Módulo «Catálogo» · la ESTADÍSTICA que ofrece al tablero: «Estado del catálogo». Vive plegada detrás de «Ver
// estadísticas» (regla P-10): se descarga y consulta recién al abrirla. Productos activos, sin imagen, sin precio y con
// margen bajo, con accesos al catálogo ya filtrado. El título lo pone el tablero.

import { ImageOff, Percent, Tags, TicketX } from 'lucide-react';
import { ROUTES } from '@/4-presentation/app/routes';
import { useRpcQuery } from '@/4-presentation/panel/hooks';
import { Button, ErrorState, StatCard } from '@/4-presentation/panel/kit';
import { formatNumber } from '@/4-presentation/panel/lib';
import { ALL_CATALOG, catalogSummary, marginText, taxRuleOf, toProductEntries } from './catalog';

export function CatalogStat() {
  const catalog = useRpcQuery('GetCatalogQuery', ALL_CATALOG);
  const options = useRpcQuery('GetCatalogOptionsQuery', {});
  const error = catalog.error ?? options.error;
  if (error) {
    return (
      <ErrorState
        error={error}
        operation={catalog.error ? 'GetCatalogQuery' : 'GetCatalogOptionsQuery'}
        onRetry={() => {
          catalog.reload();
          options.reload();
        }}
        retrying={catalog.fetching || options.fetching}
      />
    );
  }
  const summary = catalog.data && options.data ? catalogSummary(toProductEntries(catalog.data, [], taxRuleOf(options.data))) : null;
  const loading = summary === null;
  return (
    <div className="space-y-4" data-testid="estado-del-catalogo">
      <div className="grid grid-cols-[repeat(auto-fit,minmax(9rem,1fr))] gap-3">
        <StatCard label="Productos activos" value={formatNumber(summary?.active ?? 0)} hint={summary ? `${formatNumber(summary.total)} en total` : undefined} icon={<Tags />} loading={loading} />
        <StatCard label="Sin imagen" value={formatNumber(summary?.withoutImage ?? 0)} tone={summary && summary.withoutImage > 0 ? 'warning' : 'default'} icon={<ImageOff />} loading={loading} />
        <StatCard label="Activos sin precio" value={formatNumber(summary?.withoutPrice ?? 0)} tone={summary && summary.withoutPrice > 0 ? 'danger' : 'default'} icon={<TicketX />} loading={loading} />
        <StatCard
          label="Margen bajo"
          value={formatNumber(summary?.lowMargin ?? 0)}
          hint={summary ? `Promedio ${marginText(summary.averageMargin)}` : undefined}
          tone={summary && summary.lowMargin > 0 ? 'warning' : 'default'}
          icon={<Percent />}
          loading={loading}
        />
      </div>
      {summary && (
        <div className="flex flex-wrap gap-2">
          <Button variant="outline" leftIcon={<ImageOff />} to={ROUTES.panelModule('catalogo?imagen=sin')}>
            Ver los productos sin imagen
          </Button>
          <Button variant="outline" leftIcon={<Percent />} to={ROUTES.panelModule('catalogo?precio=margen-bajo')}>
            Ver los de margen bajo
          </Button>
        </div>
      )}
    </div>
  );
}
