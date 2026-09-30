// Módulo «Libros fiscales» · la ESTADÍSTICA que ofrece al tablero: «IVA del mes». Vive plegada detrás de «Ver estadísticas»
// (regla P-10): se descarga y consulta (`GetTaxSummaryQuery` del mes en curso) recién cuando se abre. El título lo pone el
// tablero.

import { BookOpen, Calculator, ShoppingBag, TrendingUp } from 'lucide-react';
import { useState } from 'react';
import { ROUTES } from '@/4-presentation/app/routes';
import { useRpcQuery } from '@/4-presentation/panel/hooks';
import { Button, ErrorState, StatCard } from '@/4-presentation/panel/kit';
import { formatMoney, laPazToday } from '@/4-presentation/panel/lib';
import { monthLabel, monthOf, parseMonth } from './books';

export function TaxStat() {
  // El mes se fija al abrir (una estadística abierta a fin de mes no cambia de mes sola).
  const [month] = useState(() => parseMonth(monthOf(laPazToday()))!);
  const summary = useRpcQuery('GetTaxSummaryQuery', { year: month.year, month: month.month });
  if (summary.error) return <ErrorState error={summary.error} operation="GetTaxSummaryQuery" onRetry={summary.reload} retrying={summary.fetching} />;

  const data = summary.data;
  const loading = !data;
  return (
    <div className="space-y-4" data-testid="iva-del-mes">
      <p className="text-sm text-text-muted">{monthLabel(month)}</p>
      <div className="grid grid-cols-[repeat(auto-fit,minmax(9rem,1fr))] gap-3">
        <StatCard label="Ventas del libro" value={formatMoney(data?.grossSales ?? 0)} icon={<ShoppingBag />} loading={loading} />
        <StatCard label="Débito fiscal" value={formatMoney(data?.taxDebit ?? 0)} icon={<TrendingUp />} loading={loading} />
        <StatCard
          label="IVA a pagar"
          value={formatMoney(Math.max(0, data?.vatPayable ?? 0))}
          hint={data && data.vatCarryForward > 0 ? `Saldo a favor ${formatMoney(data.vatCarryForward)}` : undefined}
          tone="warning"
          icon={<Calculator />}
          loading={loading}
        />
      </div>
      {data && (
        <Button to={ROUTES.panelModule('libros?pestana=resumen')} variant="outline" leftIcon={<BookOpen />}>
          Ver el resumen IVA / IT
        </Button>
      )}
    </div>
  );
}
