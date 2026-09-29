// Módulo «Caja» · la ESTADÍSTICA que ofrece al tablero: «Ventas de mi turno». Vive plegada detrás de «Ver
// estadísticas» (regla P-10): se descarga y consulta (`GetPosStateQuery`) recién al abrirla, con su propio estado de
// carga y de error. El título lo pone el tablero.

import { Banknote, Receipt, ShoppingCart, Wallet } from 'lucide-react';
import { ROUTES } from '@/4-presentation/app/routes';
import { useRpcQuery } from '@/4-presentation/panel/hooks';
import { Button, ErrorState, StatCard } from '@/4-presentation/panel/kit';
import { formatMoney, formatNumber, formatTime } from '@/4-presentation/panel/lib';
import { registerLabel } from './session';

export function ShiftSalesStat() {
  const pos = useRpcQuery('GetPosStateQuery', {});
  if (pos.error) return <ErrorState error={pos.error} operation="GetPosStateQuery" onRetry={pos.reload} retrying={pos.fetching} />;

  const loading = pos.data === undefined;
  const session = pos.data?.session ?? null;
  if (!loading && !session) {
    return (
      <div className="space-y-3" data-testid="ventas-de-mi-turno">
        <p className="text-sm text-text-muted">No tiene un turno de caja abierto. Las ventas del turno aparecen aquí cuando abre la caja.</p>
        <Button to={ROUTES.panelModule('caja')} variant="outline" leftIcon={<ShoppingCart />}>
          Ir a la caja
        </Button>
      </div>
    );
  }
  return (
    <div className="space-y-4" data-testid="ventas-de-mi-turno">
      <div className="grid grid-cols-[repeat(auto-fit,minmax(9rem,1fr))] gap-3">
        <StatCard
          label="Ventas"
          value={formatNumber(session?.tickets ?? 0)}
          hint={session ? `${registerLabel(session)} · desde las ${formatTime(session.openedAt)}` : undefined}
          icon={<Receipt />}
          loading={loading}
        />
        <StatCard label="Total vendido" value={formatMoney(session?.sales ?? 0)} icon={<Wallet />} loading={loading} />
        <StatCard label="En efectivo" value={formatMoney(session?.cashSales ?? 0)} icon={<Banknote />} loading={loading} />
        <StatCard label="Efectivo esperado en la caja" value={formatMoney(session?.expectedCash ?? 0)} hint={session ? `Fondo inicial ${formatMoney(session.openingCash)}` : undefined} icon={<Banknote />} loading={loading} />
      </div>
      {session && (
        <Button to={ROUTES.panelModule('caja')} variant="outline" leftIcon={<ShoppingCart />}>
          Ir a la caja
        </Button>
      )}
    </div>
  );
}
