// Módulo «Usuarios» · la ESTADÍSTICA que ofrece al tablero («Usuarios del sistema»). Vive plegada detrás de «Ver
// estadísticas» (regla P-10): se descarga y consulta recién al abrirse, con su propio estado de carga y de error.

import { useRpcQuery } from '@/4-presentation/panel/hooks';
import { ErrorState } from '@/4-presentation/panel/kit';
import { UsersSummary } from './UsersSummary';

export function UsersStat() {
  const users = useRpcQuery('GetUsersQuery', {});
  if (users.error) return <ErrorState error={users.error} operation="GetUsersQuery" onRetry={users.reload} retrying={users.fetching} />;
  return <UsersSummary users={users.data} showLink />;
}
