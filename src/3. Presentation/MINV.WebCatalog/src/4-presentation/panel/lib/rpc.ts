// Ayudas puras del RPC del panel: la llave de un contenido (para saber si una consulta o un comando cambió), el
// identificador de cada INTENTO de un comando (idempotencia, regla B-09) y el texto de un error en palabras, con el
// permiso que falta cuando el servidor rechaza por permisos (regla P-01: el servidor decide; la página lo explica).

import { asWebApiError, describeWebApiError, type WebApiError } from '@/1-domain/auth/errors';
import { newRequestId } from '@/2-application/auth/requestId';
import { permissionName, rpcOperation } from '@/4-presentation/app/contract';

/** Texto estable de un contenido: el mismo para dos objetos iguales aunque sus claves vengan en otro orden. */
export function stableKey(value: unknown): string {
  return JSON.stringify(value ?? null, (_key, current: unknown) => {
    if (current && typeof current === 'object' && !Array.isArray(current)) {
      const record = current as Record<string, unknown>;
      return Object.fromEntries(Object.keys(record).sort().map((key) => [key, record[key]]));
    }
    return current;
  });
}

/**
 * Identificador de los intentos de UN comando. Mientras la persona repite lo mismo (por ejemplo «Reintentar» después de
 * una falla de red) viaja el MISMO id: si el servidor ya lo había ejecutado, devuelve la respuesta guardada en vez de
 * repetir la operación. Después de un éxito, o si cambia el contenido, el id es nuevo.
 */
export class CommandAttempt {
  private id: string | null = null;
  private key: string | null = null;

  /** Id para enviar este contenido. */
  take(contentKey: string): string {
    if (this.id === null || this.key !== contentKey) {
      this.id = newRequestId();
      this.key = contentKey;
    }
    return this.id;
  }

  /** El servidor confirmó el comando: el próximo intento lleva otro id. */
  succeeded(): void {
    this.id = null;
    this.key = null;
  }

  /** Id del intento en curso (null si no hay ninguno pendiente). */
  get current(): string | null {
    return this.id;
  }
}

/** Código de permiso dentro del mensaje del servidor («Su rol no tiene el permiso sales.view.»). */
const PERMISSION_IN_MESSAGE = /\bpermiso\s+[«"']?([a-z][a-z0-9_-]*(?:\.[a-z0-9_-]+)+)/gi;

/** Códigos de permiso que menciona un mensaje del servidor. */
export function permissionsInMessage(message: string): string[] {
  return [...new Set([...message.matchAll(PERMISSION_IN_MESSAGE)].map((match) => match[1].toLowerCase()))];
}

export interface ErrorContext {
  /** Operación del RPC que falló (nombre corto): de ella salen los permisos que exige. */
  operation?: string | null;
  /** Permisos de la sesión. */
  granted?: readonly string[];
}

/**
 * Permisos que faltan según un rechazo `access_denied`: los que nombra el servidor o, si no nombra ninguno, los que
 * exige la operación y la sesión no tiene. Vacío si el rechazo es por otra razón (sucursal, módulo sin licencia).
 */
export function missingPermissions(error: WebApiError, context: ErrorContext = {}): string[] {
  if (error.kind !== 'access_denied') return [];
  const named = permissionsInMessage(error.message);
  if (named.length > 0) return named;
  const required = context.operation ? (rpcOperation(context.operation)?.permissions ?? []) : [];
  const granted = context.granted ?? [];
  return required.filter((permission) => !granted.includes(permission));
}

function listOf(items: readonly string[]): string {
  if (items.length <= 1) return items[0] ?? '';
  return `${items.slice(0, -1).join(', ')} y ${items[items.length - 1]}`;
}

/** «Falta el permiso «Consultar el historial de ventas y facturas». Pida al administrador que se lo asigne.» */
export function missingPermissionsText(codes: readonly string[]): string {
  const names = codes.map((code) => `«${permissionName(code)}»`);
  return codes.length === 1
    ? `Falta el permiso ${names[0]}. Pida al administrador que se lo asigne.`
    : `Faltan los permisos ${listOf(names)}. Pida al administrador que se los asigne.`;
}

/** Título de un rechazo por permisos. */
export const ACCESS_DENIED_TITLE = 'No tiene permiso para esta operación';

/**
 * Texto de un error para el panel (español neutro). Un rechazo por permisos dice QUÉ permiso falta en palabras; el
 * resto usa el mensaje del servidor (o uno propio si el servidor no pudo explicar la falla).
 */
export function describePanelError(error: unknown, context: ErrorContext = {}): string {
  const failure = asWebApiError(error);
  if (failure.kind === 'access_denied') {
    const missing = missingPermissions(failure, context);
    if (missing.length > 0) return `${ACCESS_DENIED_TITLE}. ${missingPermissionsText(missing)}`;
  }
  return describeWebApiError(failure, 'panel');
}

/** ¿Es la cancelación de un pedido (al desmontar o al cambiar la consulta)? No es un error para mostrar. */
export function isAbort(error: unknown): boolean {
  return (error instanceof DOMException || error instanceof Error) && error.name === 'AbortError';
}
