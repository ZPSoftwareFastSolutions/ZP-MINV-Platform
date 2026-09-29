// Puerto del RPC (V7, regla P-08): envía una operación TIPADA al servidor en la nube (`POST /api/v1/web/rpc`). La web
// es otro cliente del servidor: manda los mismos casos de uso que el escritorio y el servidor decide permisos, módulo,
// sucursal, validación y auditoría (regla P-01). El mapa de operaciones sale del contrato generado; aquí solo se
// describe su forma para que el dominio no dependa de la infraestructura. Toda falla llega como WebApiError.

export interface RpcOperationShape {
  request: unknown;
  response: unknown;
}

/** Mapa «nombre corto → petición y respuesta». Acepta interfaces (como el `RpcOperations` generado) y alias. */
export type RpcOperationMap<TOps> = { [K in keyof TOps]: RpcOperationShape };

export interface RpcSendOptions {
  /**
   * Identificador del pedido (UUID). Si falta, la pasarela crea uno nuevo. Al reintentar por una falla de RED hay que
   * enviar el MISMO id: el servidor devuelve la respuesta guardada en vez de repetir el comando (regla B-09).
   */
  requestId?: string;
  signal?: AbortSignal;
}

export interface RpcOutcome<TResult> {
  result: TResult;
  /** El servidor devolvió la respuesta guardada de un pedido anterior con el mismo id. */
  replayed: boolean;
  requestId: string;
}

export interface IRpcGateway<TOps extends RpcOperationMap<TOps> = Record<string, RpcOperationShape>> {
  /** Ejecuta la operación y devuelve su resultado. */
  send<K extends keyof TOps & string>(operation: K, payload: TOps[K]['request'], options?: RpcSendOptions): Promise<TOps[K]['response']>;
  /** Igual que `send`, con los datos del pedido (si fue una repetición idempotente y con qué id viajó). */
  call<K extends keyof TOps & string>(operation: K, payload: TOps[K]['request'], options?: RpcSendOptions): Promise<RpcOutcome<TOps[K]['response']>>;
}
