// Casos de uso de la sesión web sobre el puerto ISessionGateway: ingresar, registrarse, leer la sesión y cerrarla.
// Sin React ni red. Revisan los datos antes de viajar (con las mismas reglas del formulario) y dejan pasar los errores
// del servidor como WebApiError.

import { WebApiError } from '@/1-domain/auth/errors';
import type { Credentials, Registration, Session } from '@/1-domain/auth/types';
import { toCredentials, toRegistration, validateLoginForm, validateRegisterForm } from '@/1-domain/auth/validation';
import type { ISessionGateway } from '@/1-domain/ports/ISessionGateway';

export interface SessionUseCases {
  login(credentials: Credentials): Promise<Session>;
  register(registration: Registration): Promise<Session>;
  /** La sesión vigente o null. */
  current(): Promise<Session | null>;
  logout(): Promise<void>;
}

function rejectInvalid(errors: Partial<Record<string, string>>): void {
  const messages = Object.values(errors).filter((message): message is string => Boolean(message));
  if (messages.length === 0) return;
  throw new WebApiError({ kind: 'validation', status: 400, message: messages[0], errors: messages });
}

export function createSessionUseCases(gateway: ISessionGateway): SessionUseCases {
  return {
    async login(credentials) {
      rejectInvalid(validateLoginForm(credentials));
      return gateway.login(toCredentials(credentials));
    },
    async register(registration) {
      const form = { ...registration, confirm: registration.password };
      rejectInvalid(validateRegisterForm(form));
      return gateway.register(toRegistration(form));
    },
    current: () => gateway.current(),
    logout: () => gateway.logout(),
  };
}
