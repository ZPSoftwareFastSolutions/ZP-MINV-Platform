// Módulo «Integraciones» · la ESTADÍSTICA «Correos de reservas»: pendientes, agotados (no se pudieron enviar), enviados
// hoy y cancelados. Plegada detrás de «Ver estadísticas» (regla P-10): consulta recién al abrirse.

import { Ban, Clock, MailCheck, MailWarning } from 'lucide-react';
import { useState } from 'react';
import { ROUTES } from '@/4-presentation/app/routes';
import { useRpcQuery } from '@/4-presentation/panel/hooks';
import { Button, ErrorState, StatCard } from '@/4-presentation/panel/kit';
import { formatNumber } from '@/4-presentation/panel/lib';
import { mailsSummary } from './integrations';

/** Correos que se revisan para el resumen (el máximo que acepta el servidor). */
const STAT_TAKE = 500;

export function MailsStat() {
  const mails = useRpcQuery('GetOutgoingMailsQuery', { status: null, number: null, take: STAT_TAKE });
  // «Hoy» se fija al abrir.
  const [now] = useState(() => new Date());
  if (mails.error) return <ErrorState error={mails.error} operation="GetOutgoingMailsQuery" onRetry={mails.reload} retrying={mails.fetching} />;
  const summary = mails.data ? mailsSummary(mails.data, now) : null;
  const loading = summary === null;
  return (
    <div className="space-y-4" data-testid="resumen-correos">
      <div className="grid grid-cols-[repeat(auto-fit,minmax(9rem,1fr))] gap-3">
        <StatCard label="Pendientes" value={formatNumber(summary?.pending ?? 0)} icon={<Clock />} loading={loading} />
        <StatCard
          label="Agotados (sin enviar)"
          value={formatNumber(summary?.exhausted ?? 0)}
          tone={summary && summary.exhausted > 0 ? 'danger' : 'default'}
          icon={<MailWarning />}
          loading={loading}
        />
        <StatCard label="Enviados hoy" value={formatNumber(summary?.sentToday ?? 0)} tone="success" icon={<MailCheck />} loading={loading} />
        <StatCard label="Cancelados" value={formatNumber(summary?.cancelled ?? 0)} icon={<Ban />} loading={loading} />
      </div>
      {summary && (
        <Button
          to={ROUTES.panelModule(summary.exhausted > 0 ? 'integraciones?pestana=correos&estado=Exhausted' : 'integraciones?pestana=correos')}
          variant="outline"
          leftIcon={<MailCheck />}
        >
          {summary.exhausted > 0 ? 'Ver los correos agotados' : 'Ver los correos'}
        </Button>
      )}
    </div>
  );
}
