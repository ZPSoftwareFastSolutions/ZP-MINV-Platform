// Módulo «Ventas» · ESTADÍSTICA del tablero «Medios de pago»: lo cobrado por medio de pago en los últimos 7 días (sin las
// anuladas). Plegada detrás de «Ver estadísticas» (regla P-10): consulta recién al abrirse, con su propio estado. El título
// lo pone el tablero.

import { CreditCard } from 'lucide-react';
import { useState } from 'react';
import { ROUTES } from '@/4-presentation/app/routes';
import { useRpcQuery } from '@/4-presentation/panel/hooks';
import { BarList, Button, ErrorState, LoadingState } from '@/4-presentation/panel/kit';
import { formatMoney, formatNumber } from '@/4-presentation/panel/lib';
import { lastDays, totalsByPaymentMethod } from './sales';

export function PaymentMethodsStat() {
  const [range] = useState(() => lastDays(7));
  const sales = useRpcQuery('GetSalesQuery', range);

  if (sales.error) return <ErrorState error={sales.error} operation="GetSalesQuery" onRetry={sales.reload} retrying={sales.fetching} />;
  if (!sales.data) return <LoadingState label="Cargando los medios de pago…" rows={1} />;

  const methods = totalsByPaymentMethod(sales.data);
  return (
    <div className="space-y-4" data-testid="medios-de-pago">
      <BarList
        label="Cobrado por medio de pago (últimos 7 días, sin anuladas)"
        items={methods.map((method) => ({ label: method.label, value: method.value, hint: method.count === 1 ? '1 venta' : `${formatNumber(method.count)} ventas` }))}
        format={(value) => formatMoney(value)}
        tone="accent"
        emptyText="Sin ventas en los últimos 7 días."
      />
      <Button to={ROUTES.panelModule(`ventas?desde=${range.from}&hasta=${range.to}`)} variant="outline" leftIcon={<CreditCard />}>
        Ver las ventas por medio de pago
      </Button>
    </div>
  );
}
