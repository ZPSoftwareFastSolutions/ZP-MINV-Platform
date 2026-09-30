// Puerto de RESERVAS (V6): crear, consultar y liberar una reserva de armado en la tienda (V7: consultar con el código O el
// teléfono). Lo implementa la infraestructura (HTTP contra /storefront/v1/reservations, o una pasarela en memoria para el
// mock y las pruebas).
// Toda falla llega como StorefrontError (`insufficient_stock`, `not_found`, `domain`, `network`…).

import type { Reservation, ReservationRequest } from '@/1-domain/storefront/types';

export interface IReservationGateway {
  /** Reserva el armado: crea la cotización web y reserva el stock de cada pieza (todo o nada, regla S-03). */
  create(request: ReservationRequest): Promise<Reservation>;
  /**
   * Estado de una reserva por su número (regla S-06). Con el teléfono con que se hizo, la vista completa; V7: sin teléfono,
   * la misma reserva con el contacto enmascarado (`masked`). Si no existe o el teléfono no coincide, `not_found`.
   */
  get(number: string, phone?: string): Promise<Reservation>;
  /** V7 · Reservas hechas con un teléfono (las más nuevas primero, como máximo 10), con el contacto enmascarado. */
  findByPhone(phone: string): Promise<Reservation[]>;
  /** El cliente libera su reserva con el número Y el teléfono; el stock vuelve a estar disponible. */
  cancel(number: string, phone: string): Promise<Reservation>;
}
