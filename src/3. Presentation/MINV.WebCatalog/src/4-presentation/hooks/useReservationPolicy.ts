import type { ReservationPolicy } from '@/1-domain/storefront/types';
import { useServices } from './useServices';

/**
 * Plazos de una reserva que publica el servidor con el catálogo: `const { reservationHours, maxHoldDays } =
 * useReservationPolicy()`. Sin valores del servidor, 48 h y hasta 3 días.
 */
export function useReservationPolicy(): ReservationPolicy {
  return useServices().catalog.getReservationPolicy();
}
