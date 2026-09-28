// Puerto de RESERVAS (V6): crear, consultar y liberar una reserva de armado en la tienda. Lo implementa la
// infraestructura (HTTP contra /storefront/v1/reservations, o una pasarela en memoria para el mock y las pruebas).
// Toda falla llega como StorefrontError (`insufficient_stock`, `not_found`, `domain`, `network`…).

import type { Reservation, ReservationRequest } from '@/1-domain/storefront/types';

export interface IReservationGateway {
  /** Reserva el armado: crea la cotización web y reserva el stock de cada pieza (todo o nada, regla S-03). */
  create(request: ReservationRequest): Promise<Reservation>;
  /** Estado de una reserva; exige el número Y el teléfono con que se hizo (regla S-06). */
  get(number: string, phone: string): Promise<Reservation>;
  /** El cliente libera su reserva; el stock vuelve a estar disponible. */
  cancel(number: string, phone: string): Promise<Reservation>;
}
