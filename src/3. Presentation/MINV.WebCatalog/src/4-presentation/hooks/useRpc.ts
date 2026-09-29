import { useContext } from 'react';
import type { AccountUseCases } from '@/2-application';
import type { WebRpc, WebServices } from '@/4-presentation/app/container';
import { WebServicesContext } from '@/4-presentation/state/SessionContext';

function useWebServices(hook: string): WebServices {
  const services = useContext(WebServicesContext);
  if (!services) throw new Error(`${hook} debe usarse dentro de <SessionProvider> y en una ruta con sesión (detrás de <RequireSession>).`);
  return services;
}

/**
 * RPC tipado con el contrato del servidor: `const rpc = useRpc(); await rpc.send('GetMyReservationsQuery', {})`.
 * Si el servidor responde 401, la sesión se limpia sola y la persona va a ingresar.
 */
export function useRpc(): WebRpc {
  return useWebServices('useRpc()').rpc;
}

/** Casos de uso de la cuenta del cliente: mis datos, mis reservas y mi contraseña. */
export function useAccount(): AccountUseCases {
  return useWebServices('useAccount()').account;
}

/**
 * Casos de uso de la cuenta, o null mientras los servicios de la sesión no están listos. Para las páginas PÚBLICAS que
 * cambian si hay una cuenta de cliente (la reserva): no lanza fuera de una ruta con sesión.
 */
export function useOptionalAccount(): AccountUseCases | null {
  return useContext(WebServicesContext)?.account ?? null;
}
