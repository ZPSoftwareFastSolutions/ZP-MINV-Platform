// Aplicación de la sesión: navegación (a dónde va cada sesión, `volver` seguro), casos de uso sobre puertos simulados,
// identificador de los pedidos y vigilancia del 401. Sin red.

import { describe, expect, it, vi } from 'vitest';
import type { CustomerAccount } from '@/1-domain/account/types';
import { WebApiError } from '@/1-domain/auth/errors';
import type { Session } from '@/1-domain/auth/types';
import type { IAccountGateway } from '@/1-domain/ports/IAccountGateway';
import type { IRpcGateway } from '@/1-domain/ports/IRpcGateway';
import type { ISessionGateway } from '@/1-domain/ports/ISessionGateway';
import type { Reservation } from '@/1-domain/storefront/types';
import { isUuid } from '@/shared/ids';
import { ALL_STATUSES, createAccountUseCases, filterReservations, isReservationStatusFilter, sortReservations } from './account';
import {
  canVisit,
  changePasswordPath,
  destinationAfterLogin,
  destinationAfterPasswordChange,
  homeFor,
  isProtectedPath,
  isUnder,
  loginPath,
  registerPath,
  requiredKind,
  safeReturnPath,
} from './navigation';
import { AttemptKey, newRequestId } from './requestId';
import { createSessionUseCases } from './session';
import { watchSession } from './watchedRpc';

const SESSION: Session = {
  displayName: 'Valentina Aguirre',
  email: 'valentina@correo.example',
  roles: ['CLIENTE'],
  permissions: ['account.manage', 'account.reserve'],
  mustChangePassword: false,
  access: { allBranches: false, branches: [], activeBranchId: null },
  kind: 'customer',
  expiresAt: new Date('2026-09-28T20:00:00Z'),
  serverVersion: '7.0.0',
  company: 'Tech Zone Gaming S.R.L.',
};

describe('volver: solo rutas internas', () => {
  it('acepta rutas internas con consulta y ancla', () => {
    expect(safeReturnPath('/mi-cuenta/reservas')).toBe('/mi-cuenta/reservas');
    expect(safeReturnPath('/catalogo?q=rtx%205070&tags=oferta#lista')).toBe('/catalogo?q=rtx%205070&tags=oferta#lista');
    expect(safeReturnPath('/panel')).toBe('/panel');
    expect(safeReturnPath('/')).toBe('/');
  });

  it.each([
    ['otro sitio con doble barra', '//evil.example'],
    ['otro sitio con doble barra y ruta', '//evil.example/mi-cuenta'],
    ['URL absoluta https', 'https://evil.example/panel'],
    ['URL absoluta http', 'http://evil.example'],
    ['esquema javascript', 'javascript:alert(1)'],
    ['esquema data', 'data:text/html,<script>alert(1)</script>'],
    ['barra invertida (los navegadores la leen como «//»)', '/\\evil.example'],
    ['barra invertida doble', '\\\\evil.example'],
    ['tabulación escondida entre las barras', '/\t/evil.example'],
    ['salto de línea escondido', '/\n/evil.example'],
    ['doble barra después de normalizar', '/..//evil.example'],
    ['ruta relativa', 'mi-cuenta'],
    ['espacio inicial', ' /mi-cuenta'],
    ['vacío', ''],
    ['demasiado largo', `/${'a'.repeat(600)}`],
  ])('ignora %s', (_caso, value) => {
    expect(safeReturnPath(value)).toBeNull();
  });

  it('ignora lo que no es texto y las pantallas de acceso (no se vuelve a ingresar ni a registrarse)', () => {
    expect(safeReturnPath(null)).toBeNull();
    expect(safeReturnPath(undefined)).toBeNull();
    expect(safeReturnPath('/ingresar')).toBeNull();
    expect(safeReturnPath('/ingresar?volver=/panel')).toBeNull();
    expect(safeReturnPath('/registrarse')).toBeNull();
    expect(safeReturnPath('/ingresarse')).toBe('/ingresarse');
  });

  it('arma los enlaces de acceso conservando solo un `volver` seguro', () => {
    expect(loginPath('/mi-cuenta/reservas')).toBe('/ingresar?volver=%2Fmi-cuenta%2Freservas');
    expect(loginPath('/catalogo?q=rtx')).toBe('/ingresar?volver=%2Fcatalogo%3Fq%3Drtx');
    expect(loginPath('//evil.example')).toBe('/ingresar');
    expect(loginPath('https://evil.example')).toBe('/ingresar');
    expect(loginPath(null)).toBe('/ingresar');
    expect(registerPath('/carrito')).toBe('/registrarse?volver=%2Fcarrito');
    expect(changePasswordPath('/panel/ventas')).toBe('/cambiar-contrasena?volver=%2Fpanel%2Fventas');
    expect(changePasswordPath('/cambiar-contrasena?volver=/panel')).toBe('/cambiar-contrasena');
  });
});

describe('a dónde va cada sesión', () => {
  it('el personal va al panel y el cliente a su cuenta', () => {
    expect(homeFor('staff')).toBe('/panel');
    expect(homeFor('customer')).toBe('/mi-cuenta');
    expect(destinationAfterLogin({ kind: 'staff', mustChangePassword: false })).toBe('/panel');
    expect(destinationAfterLogin({ kind: 'customer', mustChangePassword: false })).toBe('/mi-cuenta');
  });

  it('respeta `volver` si es interno y esa sesión puede verlo', () => {
    expect(destinationAfterLogin({ kind: 'customer', mustChangePassword: false }, '/mi-cuenta/reservas')).toBe('/mi-cuenta/reservas');
    expect(destinationAfterLogin({ kind: 'customer', mustChangePassword: false }, '/carrito')).toBe('/carrito');
    expect(destinationAfterLogin({ kind: 'staff', mustChangePassword: false }, '/panel/ventas?estado=abierta')).toBe('/panel/ventas?estado=abierta');
  });

  it('un `volver` malicioso o de otro tipo de sesión se ignora', () => {
    expect(destinationAfterLogin({ kind: 'customer', mustChangePassword: false }, '//evil.example')).toBe('/mi-cuenta');
    expect(destinationAfterLogin({ kind: 'staff', mustChangePassword: false }, 'https://evil.example/panel')).toBe('/panel');
    expect(destinationAfterLogin({ kind: 'customer', mustChangePassword: false }, '/panel/usuarios')).toBe('/mi-cuenta');
    expect(destinationAfterLogin({ kind: 'staff', mustChangePassword: false }, '/mi-cuenta')).toBe('/panel');
  });

  it('si debe cambiar la contraseña pasa primero por esa pantalla y después sigue a su destino', () => {
    expect(destinationAfterLogin({ kind: 'staff', mustChangePassword: true })).toBe('/cambiar-contrasena?volver=%2Fpanel');
    expect(destinationAfterLogin({ kind: 'customer', mustChangePassword: true }, '/mi-cuenta/datos')).toBe('/cambiar-contrasena?volver=%2Fmi-cuenta%2Fdatos');
    expect(destinationAfterLogin({ kind: 'customer', mustChangePassword: true }, '//evil.example')).toBe('/cambiar-contrasena?volver=%2Fmi-cuenta');
    expect(destinationAfterPasswordChange({ kind: 'staff' }, '/panel/ventas')).toBe('/panel/ventas');
    expect(destinationAfterPasswordChange({ kind: 'staff' }, null)).toBe('/panel');
    expect(destinationAfterPasswordChange({ kind: 'customer' }, '/cambiar-contrasena')).toBe('/mi-cuenta');
    expect(destinationAfterPasswordChange({ kind: 'customer' }, 'https://evil.example')).toBe('/mi-cuenta');
  });

  it('sabe qué rutas exigen sesión y de qué tipo', () => {
    expect(requiredKind('/panel')).toBe('staff');
    expect(requiredKind('/panel/ventas/caja')).toBe('staff');
    expect(requiredKind('/mi-cuenta/reservas')).toBe('customer');
    expect(requiredKind('/cambiar-contrasena')).toBe('any');
    expect(requiredKind('/catalogo')).toBeNull();
    expect(requiredKind('/paneles')).toBeNull();
    expect(isProtectedPath('/mi-cuenta')).toBe(true);
    expect(isProtectedPath('/carrito')).toBe(false);
    expect(isUnder('/panel/ventas', '/panel')).toBe(true);
    expect(isUnder('/panelito', '/panel')).toBe(false);
    expect(canVisit('customer', '/panel')).toBe(false);
    expect(canVisit('staff', '/panel?x=1')).toBe(true);
    expect(canVisit('staff', '/mi-cuenta')).toBe(false);
    expect(canVisit('customer', '/catalogo')).toBe(true);
    expect(canVisit('staff', '/cambiar-contrasena')).toBe(true);
  });
});

describe('casos de uso de la sesión', () => {
  function gateway(): ISessionGateway & { login: ReturnType<typeof vi.fn>; register: ReturnType<typeof vi.fn> } {
    return {
      login: vi.fn().mockResolvedValue(SESSION),
      register: vi.fn().mockResolvedValue(SESSION),
      current: vi.fn().mockResolvedValue(null),
      logout: vi.fn().mockResolvedValue(undefined),
    };
  }

  it('ingresa con el correo recortado', async () => {
    const port = gateway();
    await expect(createSessionUseCases(port).login({ email: ' valentina@correo.example ', password: 'Clave123' })).resolves.toBe(SESSION);
    expect(port.login).toHaveBeenCalledWith({ email: 'valentina@correo.example', password: 'Clave123' });
  });

  it('sin correo o sin contraseña no viaja al servidor', async () => {
    const port = gateway();
    await expect(createSessionUseCases(port).login({ email: '', password: '' })).rejects.toMatchObject({ kind: 'validation', errors: ['Indicá tu correo.', 'Indicá tu contraseña.'] });
    expect(port.login).not.toHaveBeenCalled();
  });

  it('registra con los datos normalizados y SIN rol (el servidor crea siempre un cliente)', async () => {
    const port = gateway();
    await createSessionUseCases(port).register({ name: ' Valentina Aguirre ', email: 'valentina@correo.example', phone: '7123 4567', password: 'Clave123' });
    expect(port.register).toHaveBeenCalledWith({ name: 'Valentina Aguirre', email: 'valentina@correo.example', phone: '+591 71234567', password: 'Clave123' });
    expect(Object.keys(port.register.mock.calls[0][0]).sort()).toEqual(['email', 'name', 'password', 'phone']);
  });

  it('una contraseña débil no viaja al servidor', async () => {
    const port = gateway();
    await expect(createSessionUseCases(port).register({ name: 'Valentina', email: 'valentina@correo.example', phone: '71234567', password: 'debil' })).rejects.toMatchObject({
      kind: 'validation',
    });
    expect(port.register).not.toHaveBeenCalled();
  });
});

describe('identificador de los pedidos', () => {
  it('crea un UUID nuevo cada vez', () => {
    const first = newRequestId();
    expect(isUuid(first)).toBe(true);
    expect(newRequestId()).not.toBe(first);
  });

  it('conserva el id mientras la falla es de red y lo renueva cuando el servidor respondió', async () => {
    const attempt = new AttemptKey();
    const seen: string[] = [];
    const network = new WebApiError({ kind: 'network', message: 'sin red' });
    await expect(attempt.run((id) => (seen.push(id), Promise.reject(network)))).rejects.toBe(network);
    await expect(attempt.run((id) => (seen.push(id), Promise.reject(network)))).rejects.toBe(network);
    await expect(attempt.run((id) => (seen.push(id), Promise.resolve('ok')))).resolves.toBe('ok');
    expect(seen[0]).toBe(seen[1]);
    expect(seen[1]).toBe(seen[2]);

    await expect(attempt.run((id) => (seen.push(id), Promise.reject(new WebApiError({ kind: 'domain', message: 'regla' }))))).rejects.toMatchObject({ kind: 'domain' });
    await attempt.run((id) => (seen.push(id), Promise.resolve('ok')));
    expect(seen[3]).not.toBe(seen[2]);
    expect(seen[4]).not.toBe(seen[3]);
  });
});

describe('vigilancia de la sesión en el RPC', () => {
  type Ops = { Consulta: { request: { id: number }; response: string }; CambiarClave: { request: { clave: string }; response: boolean } };

  function rpcThatFails(error: unknown): IRpcGateway<Ops> {
    return { call: vi.fn().mockRejectedValue(error), send: vi.fn().mockRejectedValue(error) };
  }

  it('deja pasar el resultado cuando todo va bien', async () => {
    const inner: IRpcGateway<Ops> = { call: vi.fn().mockResolvedValue({ result: 'hola', replayed: false, requestId: 'r-1' }), send: vi.fn() };
    const onExpired = vi.fn();
    const rpc = watchSession(inner, { isSessionAlive: async () => true, onExpired });
    await expect(rpc.send('Consulta', { id: 1 }, { requestId: 'r-1' })).resolves.toBe('hola');
    await expect(rpc.call('Consulta', { id: 1 })).resolves.toMatchObject({ result: 'hola', requestId: 'r-1' });
    expect(inner.call).toHaveBeenCalledWith('Consulta', { id: 1 }, { requestId: 'r-1' });
    expect(onExpired).not.toHaveBeenCalled();
  });

  it('un 401 en cualquier operación avisa que la sesión venció y deja pasar el error', async () => {
    const expired = new WebApiError({ kind: 'authentication', status: 401, message: 'La sesión venció o se cerró: vuelva a iniciar sesión.' });
    const onExpired = vi.fn();
    const rpc = watchSession(rpcThatFails(expired), { isSessionAlive: async () => true, onExpired });
    await expect(rpc.send('Consulta', { id: 1 })).rejects.toBe(expired);
    expect(onExpired).toHaveBeenCalledTimes(1);
  });

  it('otros errores no tocan la sesión', async () => {
    const onExpired = vi.fn();
    for (const kind of ['access_denied', 'validation', 'domain', 'network', 'server'] as const) {
      const rpc = watchSession(rpcThatFails(new WebApiError({ kind, message: 'x' })), { isSessionAlive: async () => true, onExpired });
      await expect(rpc.send('Consulta', { id: 1 })).rejects.toMatchObject({ kind });
    }
    expect(onExpired).not.toHaveBeenCalled();
  });

  it('en un cambio de contraseña, un 401 con la sesión viva es «contraseña actual incorrecta», no una sesión vencida', async () => {
    const wrong = new WebApiError({ kind: 'authentication', status: 401, message: 'La contraseña actual no es correcta.' });
    const onExpired = vi.fn();
    const isSessionAlive = vi.fn().mockResolvedValue(true);
    const rpc = watchSession(rpcThatFails(wrong), { isSessionAlive, onExpired, credentialOperations: ['CambiarClave'] });
    await expect(rpc.send('CambiarClave', { clave: 'x' })).rejects.toBe(wrong);
    expect(isSessionAlive).toHaveBeenCalledTimes(1);
    expect(onExpired).not.toHaveBeenCalled();

    isSessionAlive.mockResolvedValue(false);
    await expect(rpc.send('CambiarClave', { clave: 'x' })).rejects.toBe(wrong);
    expect(onExpired).toHaveBeenCalledTimes(1);

    // Si no se puede comprobar (sin red), no se da por vencida.
    isSessionAlive.mockRejectedValue(new WebApiError({ kind: 'network', message: 'sin red' }));
    await expect(rpc.send('CambiarClave', { clave: 'x' })).rejects.toBe(wrong);
    expect(onExpired).toHaveBeenCalledTimes(1);
  });
});

describe('casos de uso de la cuenta', () => {
  const account: CustomerAccount = { name: 'Valentina Aguirre', email: 'valentina@correo.example', phone: '+591 71234567', documentType: null, documentNumber: null, complement: null };

  function reservation(number: string, status: Reservation['status'], createdAt: string): Reservation {
    return {
      number,
      status,
      statusText: status,
      createdAt: new Date(createdAt),
      reservedUntil: new Date(createdAt),
      total: 100,
      contactName: 'Valentina',
      branch: 'CM',
      notes: null,
      hasCompatibilityWarnings: false,
      lines: [],
      cancelReason: null,
      replayed: false,
    };
  }

  const LIST = [
    reservation('RES-WEB-000001', 'Expired', '2026-08-01T10:00:00Z'),
    reservation('RES-WEB-000003', 'Reserved', '2026-09-27T10:00:00Z'),
    reservation('ARM-WEB-000002', 'Sold', '2026-09-10T10:00:00Z'),
  ];

  function gateway(): IAccountGateway & Record<keyof IAccountGateway, ReturnType<typeof vi.fn>> {
    return {
      account: vi.fn().mockResolvedValue(account),
      updateAccount: vi.fn().mockResolvedValue(account),
      reservations: vi.fn().mockResolvedValue(LIST),
      cancelReservation: vi.fn().mockResolvedValue(reservation('RES-WEB-000003', 'Cancelled', '2026-09-27T10:00:00Z')),
      changePassword: vi.fn().mockResolvedValue(undefined),
    };
  }

  it('ordena las reservas de la más reciente a la más antigua y filtra por estado', async () => {
    const reservations = await createAccountUseCases(gateway()).reservations();
    expect(reservations.map((item) => item.number)).toEqual(['RES-WEB-000003', 'ARM-WEB-000002', 'RES-WEB-000001']);
    expect(filterReservations(reservations, ALL_STATUSES)).toHaveLength(3);
    expect(filterReservations(reservations, 'Reserved').map((item) => item.number)).toEqual(['RES-WEB-000003']);
    expect(filterReservations(reservations, 'Cancelled')).toEqual([]);
    expect(sortReservations([])).toEqual([]);
    expect(isReservationStatusFilter('Sold')).toBe(true);
    expect(isReservationStatusFilter('all')).toBe(true);
    expect(isReservationStatusFilter('Otro')).toBe(false);
  });

  it('libera por número (en mayúsculas) y conserva el id del pedido', async () => {
    const port = gateway();
    await createAccountUseCases(port).release(' res-web-000003 ', { requestId: 'r-9' });
    expect(port.cancelReservation).toHaveBeenCalledWith('RES-WEB-000003', { requestId: 'r-9' });
    await expect(createAccountUseCases(port).release('  ')).rejects.toMatchObject({ kind: 'validation' });
  });

  it('no envía datos inválidos de la cuenta ni una contraseña débil', async () => {
    const port = gateway();
    const cases = createAccountUseCases(port);
    await expect(cases.save({ name: '', phone: '71234567', documentType: null, documentNumber: null, complement: null })).rejects.toMatchObject({ kind: 'validation' });
    await expect(cases.save({ name: 'Valentina', phone: '71234567', documentType: 5, documentNumber: 'ABC', complement: null })).rejects.toMatchObject({ kind: 'validation' });
    expect(port.updateAccount).not.toHaveBeenCalled();
    await cases.save({ name: 'Valentina', phone: '71234567', documentType: 5, documentNumber: '1020304050', complement: null });
    expect(port.updateAccount).toHaveBeenCalledTimes(1);

    await expect(cases.changePassword({ currentPassword: 'Vieja123', newPassword: 'debil' })).rejects.toMatchObject({ kind: 'validation' });
    await expect(cases.changePassword({ currentPassword: 'Vieja123', newPassword: 'Vieja123' })).rejects.toMatchObject({ kind: 'validation' });
    expect(port.changePassword).not.toHaveBeenCalled();
    await cases.changePassword({ currentPassword: 'Vieja123', newPassword: 'Nueva456' });
    expect(port.changePassword).toHaveBeenCalledWith({ currentPassword: 'Vieja123', newPassword: 'Nueva456' }, undefined);
  });
});
