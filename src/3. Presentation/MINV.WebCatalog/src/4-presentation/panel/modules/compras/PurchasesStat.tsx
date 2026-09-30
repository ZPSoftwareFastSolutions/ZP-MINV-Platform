// Módulo «Órdenes de compra» · la ESTADÍSTICA que ofrece al tablero («Compras en curso»). Vive plegada detrás de «Ver
// estadísticas» (regla P-10): se descarga y consulta recién al abrirla. El título lo pone el tablero.

import { PackageCheck } from 'lucide-react';
import { useState } from 'react';
import { ROUTES } from '@/4-presentation/app/routes';
import { useRpcQuery } from '@/4-presentation/panel/hooks';
import { Button, ErrorState } from '@/4-presentation/panel/kit';
import { laPazToday } from '@/4-presentation/panel/lib';
import { PurchasesOverview } from './PurchasesOverview';
import { TO_RECEIVE, purchaseSummary } from './purchasing';

export function PurchasesStat() {
  const orders = useRpcQuery('GetPurchaseOrdersQuery', { status: null });
  const [today] = useState(() => laPazToday());
  if (orders.error) return <ErrorState error={orders.error} operation="GetPurchaseOrdersQuery" onRetry={orders.reload} retrying={orders.fetching} />;
  const summary = orders.data ? purchaseSummary(orders.data, today) : null;
  return (
    <div className="space-y-4">
      <PurchasesOverview summary={summary} loading={summary === null} />
      {summary && (
        <Button to={ROUTES.panelModule(`compras?estado=${TO_RECEIVE}`)} variant="outline" leftIcon={<PackageCheck />}>
          Ver las órdenes por recibir
        </Button>
      )}
    </div>
  );
}
