// Módulo «Documentos fiscales» · la ESTADÍSTICA que ofrece al tablero: «Facturación de hoy». Vive plegada detrás de «Ver
// estadísticas» (regla P-10): se descarga y consulta (`GetFiscalDocumentsQuery` del día) recién cuando se abre. El título
// lo pone el tablero.

import { CircleCheckBig, CircleX, Clock, FileText } from 'lucide-react';
import { useState } from 'react';
import { ROUTES } from '@/4-presentation/app/routes';
import { useRpcQuery } from '@/4-presentation/panel/hooks';
import { Button, ErrorState, StatCard } from '@/4-presentation/panel/kit';
import { formatMoney, formatNumber, laPazToday } from '@/4-presentation/panel/lib';
import { periodSummary } from './fiscal';

export function TodayDocumentsStat() {
  // «Hoy» se fija al abrir (una estadística abierta a medianoche no cambia de día sola).
  const [today] = useState(() => laPazToday());
  const documents = useRpcQuery('GetFiscalDocumentsQuery', { from: today, to: today, status: null, kind: null, search: null });

  if (documents.error) return <ErrorState error={documents.error} operation="GetFiscalDocumentsQuery" onRetry={documents.reload} retrying={documents.fetching} />;

  const summary = documents.data ? periodSummary(documents.data) : null;
  const loading = summary === null;
  return (
    <div className="space-y-4" data-testid="facturacion-de-hoy">
      <div className="grid grid-cols-[repeat(auto-fit,minmax(9rem,1fr))] gap-3">
        <StatCard
          label="Documentos"
          value={formatNumber(summary?.documents ?? 0)}
          hint={summary ? `${formatNumber(summary.invoices)} facturas · ${formatNumber(summary.notes)} notas` : undefined}
          icon={<FileText />}
          loading={loading}
        />
        <StatCard label="Facturado válido" value={formatMoney(summary?.validTotal ?? 0)} tone="success" icon={<CircleCheckBig />} loading={loading} />
        <StatCard
          label="Por enviar o validar"
          value={formatNumber(summary?.waiting ?? 0)}
          tone={summary && summary.waiting > 0 ? 'warning' : 'default'}
          icon={<Clock />}
          loading={loading}
        />
        <StatCard label="Anulados y rechazados" value={formatNumber(summary?.bad ?? 0)} tone={summary && summary.bad > 0 ? 'danger' : 'default'} icon={<CircleX />} loading={loading} />
      </div>
      {summary && (
        <Button to={ROUTES.panelModule('documentos-fiscales?periodo=hoy')} variant="outline" leftIcon={<FileText />}>
          Ver los documentos de hoy
        </Button>
      )}
    </div>
  );
}
