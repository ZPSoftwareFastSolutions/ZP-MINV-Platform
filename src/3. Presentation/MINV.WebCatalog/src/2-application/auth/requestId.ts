// Identificador de los pedidos RPC (idempotencia, regla B-09): un UUID NUEVO por cada intento de la persona y el MISMO
// mientras se reintenta por una falla de red. Si el servidor alcanzó a ejecutar el comando pero la respuesta se perdió,
// el reintento con el mismo id devuelve la respuesta guardada en vez de repetir la operación.

import { isWebApiError } from '@/1-domain/auth/errors';
import { newUuid } from '@/shared/ids';

export function newRequestId(): string {
  return newUuid();
}

/**
 * Llave de un intento de la persona (guardar, liberar, reservar). `take()` da el id del intento en curso; `settle()` lo
 * cierra cuando el servidor respondió (éxito o error) y lo CONSERVA si la falla fue de red, para que «Reintentar» viaje
 * con el mismo id.
 *
 *   const id = attempt.take();
 *   try { await rpc.send('…', payload, { requestId: id }); attempt.settle(); }
 *   catch (error) { attempt.settle(error); throw error; }
 */
export class AttemptKey {
  private current: string | null = null;

  take(): string {
    this.current ??= newRequestId();
    return this.current;
  }

  settle(error?: unknown): void {
    if (isWebApiError(error) && error.kind === 'network') return;
    this.current = null;
  }

  /** Ejecuta `action` con el id del intento y lo cierra según el resultado. */
  async run<T>(action: (requestId: string) => Promise<T>): Promise<T> {
    const requestId = this.take();
    try {
      const result = await action(requestId);
      this.settle();
      return result;
    } catch (error) {
      this.settle(error);
      throw error;
    }
  }
}
