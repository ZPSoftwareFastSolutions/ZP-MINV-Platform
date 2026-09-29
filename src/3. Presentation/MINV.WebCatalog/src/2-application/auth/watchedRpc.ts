// Vigila la sesión en CADA pedido RPC: cuando el servidor responde 401 (`authentication`) la sesión venció o se cerró, y
// la web tiene que limpiarla y mandar a la persona a ingresar. Es un decorador del puerto IRpcGateway: el resto de la
// web usa el RPC sin enterarse.
//
// Excepción: algunas operaciones responden 401 porque una CREDENCIAL del formulario es incorrecta (la contraseña actual
// al cambiarla), no porque la sesión haya vencido. En esas se pregunta al servidor si la sesión sigue viva antes de
// darla por vencida; si sigue, el error llega a la pantalla como cualquier otro.

import { isWebApiError } from '@/1-domain/auth/errors';
import type { IRpcGateway, RpcOperationMap, RpcOutcome, RpcSendOptions } from '@/1-domain/ports/IRpcGateway';

export interface SessionWatchOptions {
  /** ¿El servidor todavía reconoce la sesión? (`GET /session`). Si no se puede saber, se asume que sí. */
  isSessionAlive(): Promise<boolean>;
  /** La sesión venció: limpiar y mandar a ingresar. */
  onExpired(): void;
  /** Nombres cortos de las operaciones que responden 401 por una credencial incorrecta. */
  credentialOperations?: readonly string[];
}

export function watchSession<TOps extends RpcOperationMap<TOps>>(rpc: IRpcGateway<TOps>, options: SessionWatchOptions): IRpcGateway<TOps> {
  const credentialOperations = new Set(options.credentialOperations ?? []);

  async function expiredFor(operation: string): Promise<boolean> {
    if (!credentialOperations.has(operation)) return true;
    try {
      return !(await options.isSessionAlive());
    } catch {
      return false;
    }
  }

  async function call<K extends keyof TOps & string>(operation: K, payload: TOps[K]['request'], sendOptions?: RpcSendOptions): Promise<RpcOutcome<TOps[K]['response']>> {
    try {
      return await rpc.call(operation, payload, sendOptions);
    } catch (error) {
      if (isWebApiError(error) && error.kind === 'authentication' && (await expiredFor(operation))) options.onExpired();
      throw error;
    }
  }

  return {
    call,
    send: async (operation, payload, sendOptions) => (await call(operation, payload, sendOptions)).result,
  };
}
