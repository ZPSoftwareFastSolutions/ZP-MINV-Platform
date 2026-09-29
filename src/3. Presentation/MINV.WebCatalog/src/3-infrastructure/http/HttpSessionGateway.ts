// ISessionGateway sobre `/api/v1/web`: ingresar, registrarse, leer la sesión y cerrarla. El servidor puede devolver el
// `WebSession` directo o envuelto en `{ ok, result }`: se aceptan las dos formas. El token no aparece nunca: la cookie
// HttpOnly la pone y la borra el servidor.

import { isWebApiError } from '@/1-domain/auth/errors';
import type { Credentials, Registration, Session } from '@/1-domain/auth/types';
import type { ISessionGateway } from '@/1-domain/ports/ISessionGateway';
import { unwrapResult, type WebApi } from './webApi';
import { toSession } from './webMappers';

export const SESSION_ROUTES = {
  login: '/session/login',
  current: '/session',
  logout: '/session/logout',
  register: '/account/register',
} as const;

export class HttpSessionGateway implements ISessionGateway {
  private readonly api: WebApi;

  constructor(api: WebApi) {
    this.api = api;
  }

  async login(credentials: Credentials): Promise<Session> {
    const { body } = await this.api.post(SESSION_ROUTES.login, { email: credentials.email, password: credentials.password });
    return toSession(unwrapResult(body).result);
  }

  async register(registration: Registration): Promise<Session> {
    const { body } = await this.api.post(SESSION_ROUTES.register, {
      name: registration.name,
      email: registration.email,
      phone: registration.phone,
      password: registration.password,
    });
    return toSession(unwrapResult(body).result);
  }

  async current(): Promise<Session | null> {
    try {
      const { body } = await this.api.get(SESSION_ROUTES.current);
      const { result } = unwrapResult(body);
      return result === null || result === undefined ? null : toSession(result);
    } catch (error) {
      // 401 = no hay sesión (nunca ingresó, venció o se cerró): no es una falla.
      if (isWebApiError(error) && error.kind === 'authentication') return null;
      throw error;
    }
  }

  async logout(): Promise<void> {
    try {
      await this.api.post(SESSION_ROUTES.logout);
    } catch (error) {
      // Cerrar una sesión que ya no existe deja el mismo resultado: sin sesión.
      if (isWebApiError(error) && error.kind === 'authentication') return;
      throw error;
    }
  }
}
