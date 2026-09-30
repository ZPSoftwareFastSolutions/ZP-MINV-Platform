// Módulo «Homologación» · la ESTADÍSTICA que ofrece al tablero: «Homologación pendiente». Vive plegada detrás de «Ver
// estadísticas» (regla P-10): se descarga y consulta (`GetHomologationQuery`) recién cuando se abre. El título lo pone
// el tablero.

import { CreditCard, Ruler, Tags } from 'lucide-react';
import { ROUTES } from '@/4-presentation/app/routes';
import { useRpcQuery } from '@/4-presentation/panel/hooks';
import { Button, ErrorState, StatCard } from '@/4-presentation/panel/kit';
import { formatNumber } from '@/4-presentation/panel/lib';
import { homologationSummary } from './homologation';

export function PendingStat() {
  const homologation = useRpcQuery('GetHomologationQuery', {});
  if (homologation.error) return <ErrorState error={homologation.error} operation="GetHomologationQuery" onRetry={homologation.reload} retrying={homologation.fetching} />;

  const summary = homologation.data ? homologationSummary(homologation.data) : null;
  const loading = summary === null;
  return (
    <div className="space-y-4" data-testid="homologacion-pendiente">
      <div className="grid grid-cols-[repeat(auto-fit,minmax(9rem,1fr))] gap-3">
        <StatCard
          label="Productos sin homologar"
          value={formatNumber(summary?.pending ?? 0)}
          hint={summary ? `de ${formatNumber(summary.activeProducts)} activos` : undefined}
          tone={summary && summary.pending > 0 ? 'warning' : 'success'}
          icon={<Tags />}
          loading={loading}
        />
        <StatCard label="Unidades sin código" value={formatNumber(summary ? summary.units - summary.unitsDone : 0)} icon={<Ruler />} loading={loading} />
        <StatCard label="Medios de pago sin código" value={formatNumber(summary ? summary.methods - summary.methodsDone : 0)} icon={<CreditCard />} loading={loading} />
      </div>
      {summary && summary.pending > 0 && (
        <Button to={ROUTES.panelModule('homologacion?estado=pendiente')} variant="outline" leftIcon={<Tags />}>
          Ver los productos sin homologar
        </Button>
      )}
    </div>
  );
}
