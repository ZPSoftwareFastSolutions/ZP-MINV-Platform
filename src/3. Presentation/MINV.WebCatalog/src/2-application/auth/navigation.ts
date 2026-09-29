// Reglas puras de navegación de la sesión: a dónde va cada tipo de sesión después de ingresar, qué rutas exigen sesión
// y cómo se valida el parámetro `volver`. Sin React ni enrutador: solo texto.
//
// SEGURIDAD: `volver` llega en la URL, así que cualquiera puede escribirlo. Solo se acepta una ruta INTERNA que empieza
// con una sola «/»; todo lo demás (`//evil.example`, `https://…`, `/\evil`, `javascript:…`) se ignora. Así un enlace
// malicioso a /ingresar no puede mandar a la persona a otro sitio después de ingresar (redirección abierta).

import type { Session, SessionKind } from '@/1-domain/auth/types';

/** Rutas de la sesión. `4-presentation/app/routes.ts` las publica en ROUTES: se escriben una sola vez, aquí. */
export const AUTH_PATHS = {
  login: '/ingresar',
  register: '/registrarse',
  changePassword: '/cambiar-contrasena',
  /** Panel del personal (`/panel/*`). */
  panel: '/panel',
  /** Cuenta del cliente (`/mi-cuenta/*`). */
  account: '/mi-cuenta',
} as const;

/** Nombre del parámetro con la ruta a la que volver después de ingresar (`/ingresar?volver=/mi-cuenta/reservas`). */
export const RETURN_PARAM = 'volver';

const RETURN_MAX_LENGTH = 512;
/** Origen ficticio para resolver la ruta: si al resolverla cambia de origen, no era interna. */
const INTERNAL_ORIGIN = 'https://interno.invalid';

/** ¿`pathname` es `base` o está debajo de `base`? (`/panel` y `/panel/ventas` sí; `/paneles` no). */
export function isUnder(pathname: string, base: string): boolean {
  return pathname === base || pathname.startsWith(`${base}/`);
}

/** Pantallas de acceso: nunca son un destino válido de `volver` (evita ir y volver sin fin). */
function isAccessScreen(pathname: string): boolean {
  return isUnder(pathname, AUTH_PATHS.login) || isUnder(pathname, AUTH_PATHS.register);
}

/**
 * La ruta interna a la que se puede volver, o null si el valor no es seguro. Acepta ruta, consulta y ancla
 * (`/catalogo?q=rtx#lista`); la devuelve normalizada.
 */
export function safeReturnPath(raw: string | null | undefined): string | null {
  if (typeof raw !== 'string' || raw.length === 0 || raw.length > RETURN_MAX_LENGTH) return null;
  // Una sola «/» al inicio: «//host» y «/\host» los navegadores los leen como otro sitio.
  if (!raw.startsWith('/') || raw.startsWith('//')) return null;
  // Barra invertida o caracteres de control (el navegador descarta tabulaciones y saltos: «/\t/host» sería «//host»).
  if (/[\\\p{Cc}]/u.test(raw)) return null;
  let url: URL;
  try {
    url = new URL(raw, INTERNAL_ORIGIN);
  } catch {
    return null;
  }
  if (url.origin !== INTERNAL_ORIGIN) return null;
  const path = `${url.pathname}${url.search}${url.hash}`;
  // Tras normalizar («/..//host») tampoco puede quedar una doble barra inicial.
  if (!path.startsWith('/') || path.startsWith('//')) return null;
  if (isAccessScreen(url.pathname)) return null;
  return path;
}

/** Solo la ruta (sin consulta ni ancla) de una ruta interna ya validada. */
function pathnameOf(path: string): string {
  return path.split(/[?#]/, 1)[0];
}

/** Inicio de cada tipo de sesión: el personal va al panel; el cliente, a su cuenta. */
export function homeFor(kind: SessionKind): string {
  return kind === 'staff' ? AUTH_PATHS.panel : AUTH_PATHS.account;
}

/** Tipo de sesión que exige una ruta: `staff` en /panel, `customer` en /mi-cuenta, `any` en cambiar contraseña; null si es pública. */
export function requiredKind(pathname: string): SessionKind | 'any' | null {
  if (isUnder(pathname, AUTH_PATHS.panel)) return 'staff';
  if (isUnder(pathname, AUTH_PATHS.account)) return 'customer';
  if (isUnder(pathname, AUTH_PATHS.changePassword)) return 'any';
  return null;
}

export function isProtectedPath(pathname: string): boolean {
  return requiredKind(pathname) !== null;
}

/** ¿Una sesión de este tipo puede entrar a la ruta? (un cliente no entra al panel; el personal no entra a «Mi cuenta»). */
export function canVisit(kind: SessionKind, path: string): boolean {
  const required = requiredKind(pathnameOf(path));
  return required === null || required === 'any' || required === kind;
}

function withReturn(base: string, returnTo: string | null | undefined): string {
  const safe = safeReturnPath(returnTo);
  return safe ? `${base}?${RETURN_PARAM}=${encodeURIComponent(safe)}` : base;
}

/** `/ingresar?volver=…` conservando a dónde iba la persona (si la ruta es segura). */
export function loginPath(returnTo?: string | null): string {
  return withReturn(AUTH_PATHS.login, returnTo);
}

export function registerPath(returnTo?: string | null): string {
  return withReturn(AUTH_PATHS.register, returnTo);
}

export function changePasswordPath(returnTo?: string | null): string {
  const safe = safeReturnPath(returnTo);
  return withReturn(AUTH_PATHS.changePassword, safe && isUnder(pathnameOf(safe), AUTH_PATHS.changePassword) ? null : safe);
}

/**
 * A dónde va la sesión que acaba de ingresar: a `volver` si es una ruta interna que su tipo de sesión puede ver; si no,
 * a su inicio. Si debe cambiar la contraseña, primero pasa por esa pantalla (que después la lleva al destino).
 */
export function destinationAfterLogin(session: Pick<Session, 'kind' | 'mustChangePassword'>, returnTo?: string | null): string {
  const safe = safeReturnPath(returnTo);
  const target = safe && canVisit(session.kind, safe) ? safe : homeFor(session.kind);
  if (!session.mustChangePassword) return target;
  return changePasswordPath(target);
}

/** A dónde va la sesión después de cambiar la contraseña (misma regla que al ingresar, ya sin el cambio pendiente). */
export function destinationAfterPasswordChange(session: Pick<Session, 'kind'>, returnTo?: string | null): string {
  const safe = safeReturnPath(returnTo);
  if (safe && isUnder(pathnameOf(safe), AUTH_PATHS.changePassword)) return homeFor(session.kind);
  return destinationAfterLogin({ kind: session.kind, mustChangePassword: false }, safe);
}
