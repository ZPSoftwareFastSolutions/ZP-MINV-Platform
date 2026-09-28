// IReservationGateway sobre `/storefront/v1/reservations`: crear (con `Idempotency-Key`), consultar (número + teléfono
// en la URL, con el «+» codificado) y cancelar. Los errores del contrato llegan como StorefrontError desde el cliente.

import type { IReservationGateway } from '@/1-domain/ports/IReservationGateway';
import type { Reservation, ReservationRequest } from '@/1-domain/storefront/types';
import {
  IDEMPOTENCY_HEADER,
  IDEMPOTENT_REPLAYED_HEADER,
  toReservation,
  toReservationRequestDto,
  type StorefrontReservationViewDto,
} from '@/2-application/storefront';
import type { StorefrontApi } from './api';

export class HttpReservationGateway implements IReservationGateway {
  private readonly api: StorefrontApi;

  constructor(api: StorefrontApi) {
    this.api = api;
  }

  async create(request: ReservationRequest): Promise<Reservation> {
    const { body, headers } = await this.api.post<StorefrontReservationViewDto>('/reservations', toReservationRequestDto(request), {
      headers: { [IDEMPOTENCY_HEADER]: request.idempotencyKey },
    });
    const replayed = /^true$/i.test(headers.get(IDEMPOTENT_REPLAYED_HEADER) ?? '');
    return toReservation(body, replayed);
  }

  async get(number: string, phone: string): Promise<Reservation> {
    const { body } = await this.api.get<StorefrontReservationViewDto>(
      `/reservations/${encodeURIComponent(number)}?phone=${encodeURIComponent(phone)}`,
    );
    return toReservation(body);
  }

  async cancel(number: string, phone: string): Promise<Reservation> {
    const { body } = await this.api.post<StorefrontReservationViewDto>(`/reservations/${encodeURIComponent(number)}/cancel`, { phone });
    return toReservation(body);
  }
}
