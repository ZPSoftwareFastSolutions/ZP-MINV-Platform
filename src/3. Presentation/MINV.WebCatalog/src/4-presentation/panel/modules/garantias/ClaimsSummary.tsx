// Módulo «Garantías» · el resumen de los casos (los indicadores del escritorio: abiertos, en el taller o en el proveedor,
// con cargo y entregados, y los casos por estado). Vive PLEGADO en «Ver resumen de los casos» (regla P-10): se monta y
// consulta TODOS los casos (`GetWarrantyClaimsQuery` sin filtro) recién al abrirse, con su propio estado de error.

import { CircleCheck, CircleDollarSign, ShieldCheck, Wrench } from 'lucide-react';
import { useRpcQuery } from '@/4-presentation/panel/hooks';
import { BarList, ErrorState, StatCard } from '@/4-presentation/panel/kit';
import { formatNumber } from '@/4-presentation/panel/lib';
import { claimsSummary, statusBars } from './claims';

export function ClaimsSummary() {
  const claims = useRpcQuery('GetWarrantyClaimsQuery', { status: null, onlyOpen: false });

  if (claims.error) return <ErrorState error={claims.error} operation="GetWarrantyClaimsQuery" onRetry={claims.reload} retrying={claims.fetching} />;

  const rows = claims.data;
  const summary = rows ? claimsSummary(rows) : null;
  const loading = summary === null;
  return (
    <div className="space-y-4" data-testid="resumen-de-casos">
      <div className="grid grid-cols-[repeat(auto-fit,minmax(10rem,1fr))] gap-3">
        <StatCard
          label="Casos abiertos"
          value={formatNumber(summary?.open ?? 0)}
          hint={summary ? (summary.open === 0 ? 'Ningún equipo en garantía' : `El más antiguo lleva ${formatNumber(summary.oldest)} días`) : undefined}
          icon={<ShieldCheck />}
          loading={loading}
        />
        <StatCard
          label="En diagnóstico o en el proveedor"
          value={formatNumber(summary?.workshop ?? 0)}
          hint={summary ? `${formatNumber(summary.atSupplier)} en el proveedor` : undefined}
          icon={<Wrench />}
          tone={summary && summary.workshop > 0 ? 'warning' : 'default'}
          loading={loading}
        />
        <StatCard label="Fuera de garantía (con cargo)" value={formatNumber(summary?.chargeable ?? 0)} hint="Reparaciones con cargo" icon={<CircleDollarSign />} loading={loading} />
        <StatCard
          label="Entregados"
          value={formatNumber(summary?.delivered ?? 0)}
          hint={summary ? `${formatNumber(summary.replaced)} con reemplazo` : undefined}
          icon={<CircleCheck />}
          tone="success"
          loading={loading}
        />
      </div>
      {rows && <BarList label={`Casos por estado (${formatNumber(rows.length)} en total)`} items={statusBars(rows)} format={(value) => formatNumber(value)} />}
    </div>
  );
}
