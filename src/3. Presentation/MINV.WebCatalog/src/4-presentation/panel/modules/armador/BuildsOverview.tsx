// Módulo «Armador de PC» · el RESUMEN de los armados (lo que el escritorio muestra en tarjetas: cotizaciones vigentes,
// reservas web activas, vendidos y borradores). Va DENTRO de «Ver resumen de los armados», cerrado al entrar: se monta y
// consulta recién al abrirlo (regla P-10).

import { ClipboardList, FileText, Globe, ShoppingCart } from 'lucide-react';
import { useState } from 'react';
import { useRpcQuery } from '@/4-presentation/panel/hooks';
import { ErrorState, StatCard } from '@/4-presentation/panel/kit';
import { formatNumber } from '@/4-presentation/panel/lib';
import { amountHint, buildsOverview } from './builder';

export function BuildsOverview() {
  const builds = useRpcQuery('GetPcBuildsQuery', { status: null, channel: null, kind: 'Build' });
  const [now] = useState(() => new Date());

  if (builds.error) return <ErrorState error={builds.error} operation="GetPcBuildsQuery" onRetry={builds.reload} retrying={builds.fetching} />;

  const summary = builds.data ? buildsOverview(builds.data, now) : null;
  const loading = summary === null;
  return (
    <div className="grid grid-cols-[repeat(auto-fit,minmax(11rem,1fr))] gap-3" data-testid="resumen-armados">
      <StatCard
        label="Cotizaciones vigentes"
        value={formatNumber(summary?.open ?? 0)}
        hint={summary ? amountHint(summary.open, summary.openValue, 'por cobrar', 'Ninguna vigente') : undefined}
        icon={<FileText />}
        loading={loading}
      />
      <StatCard
        label="Reservas web activas"
        value={formatNumber(summary?.webActive ?? 0)}
        hint={summary ? amountHint(summary.webActive, summary.webValue, 'reservados', 'Ninguna reserva vigente') : undefined}
        icon={<Globe />}
        loading={loading}
      />
      <StatCard
        label="Armados vendidos"
        value={formatNumber(summary?.sold ?? 0)}
        hint={summary ? amountHint(summary.sold, summary.soldValue, 'vendidos', 'Todavía ninguno') : undefined}
        tone="success"
        icon={<ShoppingCart />}
        loading={loading}
      />
      <StatCard label="Borradores" value={formatNumber(summary?.drafts ?? 0)} hint="Por cotizar" tone="warning" icon={<ClipboardList />} loading={loading} />
    </div>
  );
}
