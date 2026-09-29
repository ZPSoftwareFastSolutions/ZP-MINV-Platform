// Módulo «Usuarios» · resumen de las cuentas (los indicadores del escritorio: activos, bloqueados, ingresos de hoy y
// personal por rol). Regla P-10: vive PLEGADO, en «Inicio › Ver estadísticas» (UsersStat) y en la pantalla dentro de
// «Ver resumen de los usuarios»; se calcula recién al abrirse.

import { KeyRound, Lock, UserCheck, Users } from 'lucide-react';
import { useMemo, useState } from 'react';
import { ROUTES } from '@/4-presentation/app/routes';
import { BarList, Button, StatCard } from '@/4-presentation/panel/kit';
import { formatNumber } from '@/4-presentation/panel/lib';
import { usersSummary, type UserRecord } from './users';

export interface UsersSummaryProps {
  /** undefined mientras llegan. */
  users: readonly UserRecord[] | undefined;
  /** Muestra el enlace a la lista (en el tablero). */
  showLink?: boolean;
}

export function UsersSummary({ users, showLink = false }: UsersSummaryProps) {
  // «Hoy» se fija al abrir (un resumen abierto a medianoche no cambia de día solo).
  const [now] = useState(() => new Date());
  const summary = useMemo(() => (users ? usersSummary(users, now) : null), [users, now]);
  const loading = summary === null;
  return (
    <div className="space-y-4" data-testid="resumen-usuarios">
      <div className="grid grid-cols-[repeat(auto-fit,minmax(9rem,1fr))] gap-3">
        <StatCard
          label="Personal activo"
          value={formatNumber(summary?.staffActive ?? 0)}
          hint={summary ? `de ${formatNumber(summary.staff)} del personal` : undefined}
          icon={<Users />}
          loading={loading}
        />
        <StatCard
          label="Bloqueados"
          value={formatNumber(summary?.locked ?? 0)}
          hint="Por intentos fallidos"
          tone={summary && summary.locked > 0 ? 'danger' : 'default'}
          icon={<Lock />}
          loading={loading}
        />
        <StatCard
          label="Deben cambiar la contraseña"
          value={formatNumber(summary?.mustChange ?? 0)}
          tone={summary && summary.mustChange > 0 ? 'warning' : 'default'}
          icon={<KeyRound />}
          loading={loading}
        />
        <StatCard label="Ingresaron hoy" value={formatNumber(summary?.today ?? 0)} icon={<UserCheck />} loading={loading} />
      </div>
      {summary && summary.byRole.length > 0 && <BarList label="Personal activo por rol" items={summary.byRole} format={(value) => formatNumber(value)} />}
      {summary && <p className="text-sm text-text-muted">Clientes con cuenta en la tienda web: {formatNumber(summary.customers)}.</p>}
      {summary && showLink && (
        <Button
          to={ROUTES.panelModule(summary.locked > 0 ? 'usuarios?tipo=&estado=bloqueado' : 'usuarios')}
          variant="outline"
          leftIcon={summary.locked > 0 ? <Lock /> : <Users />}
        >
          {summary.locked > 0 ? 'Ver los bloqueados' : 'Ver los usuarios'}
        </Button>
      )}
    </div>
  );
}
