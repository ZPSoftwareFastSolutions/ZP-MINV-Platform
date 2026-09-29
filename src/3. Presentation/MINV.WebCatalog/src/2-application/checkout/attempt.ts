// Llave de idempotencia de la reserva (V7, reglas S-05 y B-09): UNA llave por intento de la persona.
//   · Si el envío falla por la RED (no se sabe si la tienda alcanzó a reservar), el reintento viaja con la MISMA llave:
//     el servidor devuelve la reserva ya creada en vez de reservar dos veces.
//   · Si la persona cambia algo (productos, cantidades, datos, días, factura o notas), la llave es OTRA: con la misma
//     llave y otro contenido el servidor respondería 422.
//   · Cuando el servidor respondió (éxito o error), el intento se cierra y el próximo envío estrena llave.
// Sirve para las dos vías: la cabecera `Idempotency-Key` de la tienda pública y el `requestId` del RPC de la cuenta.

import { isWebApiError } from '@/1-domain/auth/errors';
import { isStorefrontError } from '@/1-domain/storefront/errors';
import { newUuid } from '@/shared/ids';

/** ¿La falla fue de red (sin respuesta del servidor)? Solo entonces se conserva la llave para reintentar. */
export function isNetworkFailure(error: unknown): boolean {
  if (isStorefrontError(error)) return error.kind === 'network';
  if (isWebApiError(error)) return error.kind === 'network';
  return false;
}

/**
 * Huella del contenido de un envío: JSON con las claves ordenadas (dos objetos iguales dan la misma huella aunque sus
 * claves estén en otro orden). Los `undefined` no cuentan.
 */
export function contentFingerprint(value: unknown): string {
  const normalize = (item: unknown): unknown => {
    if (Array.isArray(item)) return item.map(normalize);
    if (item && typeof item === 'object') {
      return Object.fromEntries(
        Object.entries(item as Record<string, unknown>)
          .filter(([, entry]) => entry !== undefined)
          .sort(([a], [b]) => a.localeCompare(b))
          .map(([key, entry]) => [key, normalize(entry)]),
      );
    }
    return item;
  };
  return JSON.stringify(normalize(value)) ?? '';
}

export class IdempotentAttempt {
  private key: string | null = null;
  private fingerprint: string | null = null;
  private readonly createKey: () => string;

  constructor(createKey: () => string = newUuid) {
    this.createKey = createKey;
  }

  /** Llave para enviar este contenido: la misma mientras el contenido no cambie y no se haya cerrado el intento. */
  take(fingerprint: string): string {
    if (this.key === null || this.fingerprint !== fingerprint) {
      this.key = this.createKey();
      this.fingerprint = fingerprint;
    }
    return this.key;
  }

  /** Cierra el intento cuando el servidor respondió; lo conserva si la falla fue de red. */
  settle(error?: unknown): void {
    if (isNetworkFailure(error)) return;
    this.key = null;
    this.fingerprint = null;
  }

  /** Llave del intento en curso (null si no hay ninguno abierto). */
  current(): string | null {
    return this.key;
  }

  /** Ejecuta `action` con la llave de este contenido y cierra el intento según el resultado. */
  async run<T>(fingerprint: string, action: (key: string) => Promise<T>): Promise<T> {
    const key = this.take(fingerprint);
    try {
      const result = await action(key);
      this.settle();
      return result;
    } catch (error) {
      this.settle(error);
      throw error;
    }
  }
}
