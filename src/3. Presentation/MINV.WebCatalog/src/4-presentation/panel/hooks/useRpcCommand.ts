// Comando del servidor desde el panel (guardar, anular, vender, liberar…), con su estado y sus avisos.
//
//   const anular = useRpcCommand('VoidSaleCommand', { success: 'Venta anulada' });
//   const resultado = await anular.run({ number: venta.number });
//   if (resultado.ok) ventas.reload();
//
// - `run` NUNCA lanza: devuelve `{ ok: true, result }` o `{ ok: false, error, message }`. Mientras se envía, `sending`
//   es verdadero y un segundo `run` devuelve el mismo pedido (no se envía dos veces por un doble clic).
// - Idempotencia (regla B-09): cada intento viaja con un `requestId`. Repetir el MISMO contenido (por ejemplo
//   «Reintentar» tras una falla de red) usa el mismo id, y el servidor devuelve la respuesta guardada si ya lo había
//   ejecutado. Después de un éxito, o si cambia el contenido, el id es nuevo.
// - Avisos: al terminar bien, el texto de `success` (sin `success` no hay aviso); al fallar, el mensaje del servidor. Si
//   el servidor rechaza por permisos (`access_denied`), el aviso dice QUÉ permiso falta en palabras. Un 401 no avisa:
//   la sesión venció y la página manda a ingresar sola.
// - `errorText` sirve para mostrar el error dentro de un formulario o de un diálogo (con `notifyError: false`).

import { useCallback, useEffect, useRef, useState } from 'react';
import { asWebApiError, type WebApiError } from '@/1-domain/auth/errors';
import type { RpcOperationName, RpcRequestOf, RpcResponseOf } from '@/4-presentation/app/contract';
import { useRpc } from '@/4-presentation/hooks/useRpc';
import { useSession } from '@/4-presentation/hooks/useSession';
import { useToast } from '@/4-presentation/hooks/useToast';
import { ACCESS_DENIED_TITLE, CommandAttempt, describePanelError, missingPermissions, missingPermissionsText, stableKey } from '../lib/rpc';

export interface RpcCommandOptions<K extends RpcOperationName> {
  /** Aviso al terminar bien: un texto o una función del resultado. Sin valor (o false), no hay aviso. */
  success?: string | false | ((result: RpcResponseOf<K>, payload: RpcRequestOf<K>) => string);
  /** Título del aviso de error (el detalle es el mensaje del servidor). Por defecto «No se pudo completar la operación». */
  errorTitle?: string;
  /** false = no avisa el error (la pantalla lo muestra con `errorText`, por ejemplo dentro de un formulario). */
  notifyError?: boolean;
  /** Después de un éxito (recargar la lista, cerrar el diálogo…). */
  onSuccess?: (result: RpcResponseOf<K>, payload: RpcRequestOf<K>) => void;
}

export type RpcCommandOutcome<T> =
  | { ok: true; result: T; replayed: boolean }
  | { ok: false; error: WebApiError; message: string };

export interface RpcCommand<K extends RpcOperationName> {
  /** Envía el comando. Nunca lanza: mirar `ok`. */
  run(payload: RpcRequestOf<K>): Promise<RpcCommandOutcome<RpcResponseOf<K>>>;
  /** Hay un envío en curso (botón ocupado). */
  sending: boolean;
  /** Última falla (null después de un éxito o de `reset`). */
  error: WebApiError | null;
  /** Última falla en palabras (con el permiso que falta, si fue por permisos). */
  errorText: string | null;
  /** Identificador del intento pendiente: el mismo al reintentar lo mismo; null después de un éxito. */
  requestId: string | null;
  /** Olvida el último error (al cerrar el diálogo o al cambiar el formulario). */
  reset(): void;
}

interface CommandState {
  sending: boolean;
  error: WebApiError | null;
  errorText: string | null;
  requestId: string | null;
}

const IDLE: CommandState = { sending: false, error: null, errorText: null, requestId: null };
const DEFAULT_ERROR_TITLE = 'No se pudo completar la operación';

export function useRpcCommand<K extends RpcOperationName>(operation: K, options: RpcCommandOptions<K> = {}): RpcCommand<K> {
  const rpc = useRpc();
  const toast = useToast();
  const { session } = useSession();
  const [attempt] = useState(() => new CommandAttempt());
  const [state, setState] = useState<CommandState>(IDLE);
  const pending = useRef<Promise<RpcCommandOutcome<RpcResponseOf<K>>> | null>(null);

  // Opciones y permisos vigentes sin volver a crear `run` en cada dibujo.
  const optionsRef = useRef(options);
  const grantedRef = useRef<readonly string[]>(session?.permissions ?? []);
  useEffect(() => {
    optionsRef.current = options;
    grantedRef.current = session?.permissions ?? [];
  });

  const run = useCallback(
    (payload: RpcRequestOf<K>): Promise<RpcCommandOutcome<RpcResponseOf<K>>> => {
      if (pending.current) return pending.current;
      const requestId = attempt.take(stableKey(payload));
      setState({ sending: true, error: null, errorText: null, requestId });
      const task = (async (): Promise<RpcCommandOutcome<RpcResponseOf<K>>> => {
        try {
          const outcome = await rpc.call(operation, payload, { requestId });
          attempt.succeeded();
          setState(IDLE);
          const { success, onSuccess } = optionsRef.current;
          const text = typeof success === 'function' ? success(outcome.result, payload) : success;
          if (text) toast.notify({ tone: 'success', title: text });
          onSuccess?.(outcome.result, payload);
          return { ok: true, result: outcome.result, replayed: outcome.replayed };
        } catch (caught) {
          const error = asWebApiError(caught);
          const context = { operation, granted: grantedRef.current };
          const message = describePanelError(error, context);
          setState({ sending: false, error, errorText: message, requestId: attempt.current });
          const { notifyError = true, errorTitle } = optionsRef.current;
          if (notifyError && error.kind !== 'authentication') {
            const missing = missingPermissions(error, context);
            toast.notify({
              tone: 'danger',
              title: missing.length > 0 ? ACCESS_DENIED_TITLE : (errorTitle ?? DEFAULT_ERROR_TITLE),
              description: missing.length > 0 ? missingPermissionsText(missing) : message,
              duration: 8000,
            });
          }
          return { ok: false, error, message };
        } finally {
          pending.current = null;
        }
      })();
      pending.current = task;
      return task;
    },
    [rpc, operation, attempt, toast],
  );

  const reset = useCallback(() => {
    setState((current) => (current.sending ? current : { ...current, error: null, errorText: null }));
  }, []);

  return { run, sending: state.sending, error: state.error, errorText: state.errorText, requestId: state.requestId, reset };
}
