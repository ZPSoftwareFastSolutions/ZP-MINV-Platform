// Puerto de la SESIÓN WEB (V7, regla P-08): ingresar, registrarse, leer la sesión vigente y cerrarla. Lo implementa la
// infraestructura (HTTP contra /api/v1/web, o una sesión en memoria para el modo mock y las pruebas). El token nunca
// pasa por aquí: viaja en una cookie HttpOnly que el navegador adjunta solo. Toda falla llega como WebApiError.

import type { Credentials, Registration, Session } from '@/1-domain/auth/types';

export interface ISessionGateway {
  /** Inicia sesión. Credenciales incorrectas o cuenta bloqueada: WebApiError `authentication` con un mensaje único. */
  login(credentials: Credentials): Promise<Session>;
  /** Crea una cuenta de cliente y deja la sesión iniciada. Correo ya registrado: `domain` con código `account.email_taken`. */
  register(registration: Registration): Promise<Session>;
  /** La sesión vigente, o null si no hay ninguna (el servidor respondió 401). */
  current(): Promise<Session | null>;
  /** Cierra la sesión en el servidor (que borra la cookie). Cerrar una sesión ya cerrada no es un error. */
  logout(): Promise<void>;
}
