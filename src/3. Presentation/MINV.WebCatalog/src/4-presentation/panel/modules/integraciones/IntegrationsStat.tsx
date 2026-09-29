// Módulo «Integraciones» · la ESTADÍSTICA «Integraciones» (los indicadores del escritorio: llaves activas, webhooks
// activos y entregas correctas y fallidas). Vive plegada (regla P-10): en «Inicio › Ver estadísticas» y en la pantalla
// dentro de «Ver resumen de las integraciones»; se descarga y consulta recién al abrirse.

import { CircleCheck, KeyRound, TriangleAlert, Webhook } from 'lucide-react';
import { ROUTES } from '@/4-presentation/app/routes';
import { useRpcQuery } from '@/4-presentation/panel/hooks';
import { Button, ErrorState, StatCard } from '@/4-presentation/panel/kit';
import { formatNumber } from '@/4-presentation/panel/lib';
import { integrationsSummary } from './integrations';

export function IntegrationsStat() {
  const keys = useRpcQuery('GetApiKeysQuery', {});
  const hooks = useRpcQuery('GetWebhooksQuery', {});
  const failure = keys.error ?? hooks.error;
  if (failure) {
    const reload = () => {
      keys.reload();
      hooks.reload();
    };
    return <ErrorState error={failure} operation={keys.error ? 'GetApiKeysQuery' : 'GetWebhooksQuery'} onRetry={reload} retrying={keys.fetching || hooks.fetching} />;
  }
  const summary = keys.data && hooks.data ? integrationsSummary(keys.data, hooks.data) : null;
  const loading = summary === null;
  return (
    <div className="space-y-4" data-testid="resumen-integraciones">
      <div className="grid grid-cols-[repeat(auto-fit,minmax(9rem,1fr))] gap-3">
        <StatCard
          label="API Keys activas"
          value={formatNumber(summary?.keysActive ?? 0)}
          hint={summary ? (summary.keysUsed > 0 ? `${formatNumber(summary.keysUsed)} en uso` : 'Ninguna usada todavía') : undefined}
          icon={<KeyRound />}
          loading={loading}
        />
        <StatCard
          label="Webhooks activos"
          value={formatNumber(summary?.hooksActive ?? 0)}
          hint={summary ? `${formatNumber(summary.subscriptions)} suscripciones a eventos` : undefined}
          icon={<Webhook />}
          loading={loading}
        />
        <StatCard label="Entregas correctas" value={formatNumber(summary?.delivered ?? 0)} hint="Firmadas con HMAC-SHA256" tone="success" icon={<CircleCheck />} loading={loading} />
        <StatCard
          label="Entregas fallidas"
          value={formatNumber(summary?.failed ?? 0)}
          hint="Se reintentan hasta 8 veces"
          tone={summary && summary.failed > 0 ? 'danger' : 'default'}
          icon={<TriangleAlert />}
          loading={loading}
        />
      </div>
      {summary && summary.failed > 0 && (
        <Button to={ROUTES.panelModule('integraciones?pestana=entregas&resultado=fallida')} variant="outline" leftIcon={<TriangleAlert />}>
          Ver las entregas fallidas
        </Button>
      )}
    </div>
  );
}
