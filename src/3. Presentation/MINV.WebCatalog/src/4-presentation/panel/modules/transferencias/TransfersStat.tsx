// Módulo «Transferencias» · la ESTADÍSTICA que ofrece al tablero («Transferencias en curso»). Vive plegada detrás de
// «Ver estadísticas» (regla P-10): se descarga y consulta recién al abrirla. El título lo pone el tablero.

import { PackageCheck } from 'lucide-react';
import { useState } from 'react';
import { ROUTES } from '@/4-presentation/app/routes';
import { useRpcQuery } from '@/4-presentation/panel/hooks';
import { Button, ErrorState } from '@/4-presentation/panel/kit';
import { laPazToday } from '@/4-presentation/panel/lib';
import { TransfersOverview } from './TransfersOverview';
import { DEFAULT_TAKE, TO_RECEIVE, transfersSummary } from './transfers';

export function TransfersStat() {
  const transfers = useRpcQuery('GetTransfersQuery', { status: null, take: Number(DEFAULT_TAKE) });
  const [today] = useState(() => laPazToday());
  if (transfers.error) return <ErrorState error={transfers.error} operation="GetTransfersQuery" onRetry={transfers.reload} retrying={transfers.fetching} />;
  const summary = transfers.data ? transfersSummary(transfers.data, today) : null;
  return (
    <div className="space-y-4">
      <TransfersOverview summary={summary} loading={summary === null} />
      {summary && (
        <Button to={ROUTES.panelModule(`transferencias?pendiente=${TO_RECEIVE}`)} variant="outline" leftIcon={<PackageCheck />}>
          Ver las que llegan a mis sucursales
        </Button>
      )}
    </div>
  );
}
