import { createContext } from 'react';
import type { WebApiError } from '@/1-domain/auth/errors';
import type { Credentials, Registration, Session, SessionEnd } from '@/1-domain/auth/types';
import type { CatalogMode, DemoUser, WebServices } from '@/4-presentation/app/container';

export interface SessionApi {
  /** `loading` mientras se pregunta al servidor si hay sesión (al arrancar); después, `ready`. */
  status: 'loading' | 'ready';
  /** La sesión vigente, o null. Vive SOLO en la memoria de la página: nada se guarda en el navegador (regla P-02). */
  session: Session | null;
  /** No se pudo preguntar por la sesión (sin conexión o servidor caído): no se sabe si hay sesión. */
  loadError: WebApiError | null;
  /** Por qué dejó de haber sesión (venció o se cerró), hasta que la navegación lo atienda. */
  ended: SessionEnd | null;
  mode: CatalogMode;
  /** Usuarios de muestra del modo mock (vacío contra el servidor). */
  demoUsers: readonly DemoUser[];
  login(credentials: Credentials): Promise<Session>;
  register(registration: Registration): Promise<Session>;
  logout(): Promise<void>;
  /** Vuelve a preguntar al servidor por la sesión (después de cambiar la contraseña o los datos). */
  refresh(): Promise<Session | null>;
  /** La navegación ya atendió el fin de la sesión. */
  acknowledgeEnd(): void;
  /** ¿La sesión tiene el permiso? Solo para mostrar u ocultar: el servidor decide en cada pedido (regla P-01). */
  can(permission: string): boolean;
}

export const SessionContext = createContext<SessionApi | null>(null);

/** Servicios de la sesión web (RPC y cuenta), disponibles cuando el proveedor terminó de prepararlos. */
export const WebServicesContext = createContext<WebServices | null>(null);
