// IRpcGateway sobre `POST /api/v1/web/rpc`: envía `{ requestId, type, payload }` y devuelve `result`. `type` es el
// nombre COMPLETO del caso de uso, que sale del contrato (RPC_META) a partir del nombre corto. `requestId` es un UUID:
// nuevo por cada intento de la persona y el mismo si se reintenta por una falla de red (el servidor es idempotente).

import { WebApiError } from '@/1-domain/auth/errors';
import type { IRpcGateway, RpcOutcome, RpcSendOptions } from '@/1-domain/ports/IRpcGateway';
import { newUuid } from '@/shared/ids';
import { rpcOperation, type RpcOperationName, type RpcOperations, type RpcRequest, type RpcRequestOf, type RpcResponseOf } from './contract';
import { unwrapResult, type WebApi } from './webApi';

export const RPC_ROUTE = '/rpc';

export class HttpRpcGateway implements IRpcGateway<RpcOperations> {
  private readonly api: WebApi;

  constructor(api: WebApi) {
    this.api = api;
  }

  async call<K extends RpcOperationName>(operation: K, payload: RpcRequestOf<K>, options: RpcSendOptions = {}): Promise<RpcOutcome<RpcResponseOf<K>>> {
    const info = rpcOperation(operation);
    if (!info) {
      throw new WebApiError({ kind: 'unsupported', status: 0, message: `La página no conoce la operación «${operation}». Actualice la página.` });
    }
    const requestId = options.requestId ?? newUuid();
    const request: RpcRequest = { requestId, type: info.type, payload: payload ?? {} };
    const { body } = await this.api.post(RPC_ROUTE, request, { signal: options.signal, requestId });
    const { result, replayed } = unwrapResult(body);
    return { result: result as RpcResponseOf<K>, replayed, requestId };
  }

  async send<K extends RpcOperationName>(operation: K, payload: RpcRequestOf<K>, options?: RpcSendOptions): Promise<RpcResponseOf<K>> {
    return (await this.call(operation, payload, options)).result;
  }
}
