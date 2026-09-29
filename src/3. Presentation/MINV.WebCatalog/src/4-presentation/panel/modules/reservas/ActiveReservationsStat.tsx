// Módulo «Reservas» · la ESTADÍSTICA que ofrece al tablero: «Reservas activas y su valor». Vive plegada detrás de «Ver
// estadísticas» (regla P-10): se descarga y consulta recién al abrirse, con su propio estado de carga y de error. La misma
// vista se usa dentro de la pantalla, en «Ver reservas activas y su valor» (cerrado al entrar).

import { CalendarClock, CircleDollarSign, Hourglass, TriangleAlert } from 'lucide-react';
import { useState } from 'react';
import { ROUTES } from '@/4-presentation/app/routes';
import { useRpcQuery } from '@/4-presentation/panel/hooks';
import { BarList, Button, ErrorState, StatCard } from '@/4-presentation/panel/kit';
import { formatMoney, formatNumber } from '@/4-presentation/panel/lib';
import { activeReservationsSummary } from './reservations';

/** Lo que muestra el tablero (con el enlace a la lista filtrada). */
export function ActiveReservationsStat() {
  return <ActiveReservationsSummary showLink />;
}

export interface ActiveReservationsSummaryProps {
  /** Muestra «Ver las reservas activas» (en el tablero; dentro de la pantalla no hace falta). */
  showLink: boolean;
}

export function ActiveReservationsSummary({ showLink }: ActiveReservationsSummaryProps) {
  const reserved = useRpcQuery('GetPcBuildsQuery', { status: 'Reserved', channel: null, kind: null });
  // «Ahora» se fija al abrir (el resumen no cambia solo mientras se mira).
  const [now] = useState(() => new Date());

  if (reserved.error) return <ErrorState error={reserved.error} operation="GetPcBuildsQuery" onRetry={reserved.reload} retrying={reserved.fetching} />;

  const summary = reserved.data ? activeReservationsSummary(reserved.data, now) : null;
  const loading = summary === null;
  return (
    <div className="space-y-4" data-testid="reservas-activas">
      <div className="grid grid-cols-[repeat(auto-fit,minmax(9rem,1fr))] gap-3">
        <StatCard label="Reservas activas" value={formatNumber(summary?.active ?? 0)} icon={<CalendarClock />} loading={loading} />
        <StatCard label="Valor reservado" value={formatMoney(summary?.value ?? 0)} hint="A los precios congelados de cada reserva." icon={<CircleDollarSign />} loading={loading} />
        <StatCard
          label="Vencen en menos de 6 h"
          value={formatNumber(summary?.soon ?? 0)}
          tone={summary && summary.soon > 0 ? 'warning' : 'default'}
          icon={<Hourglass />}
          loading={loading}
        />
        <StatCard
          label="Vencidas sin liberar"
          value={formatNumber(summary?.expired ?? 0)}
          hint={summary && summary.expired > 0 ? 'Retienen stock hasta que se cierren.' : undefined}
          tone={summary && summary.expired > 0 ? 'danger' : 'default'}
          icon={<TriangleAlert />}
          loading={loading}
        />
      </div>
      {summary && summary.groups.length > 0 && (
        <BarList label="Reservas activas por tipo y canal" items={summary.groups} format={(value) => formatNumber(value)} tone="accent" />
      )}
      {summary && showLink && (
        <Button to={ROUTES.panelModule('reservas?estado=reservada')} variant="outline" leftIcon={<CalendarClock />}>
          Ver las reservas activas
        </Button>
      )}
    </div>
  );
}
