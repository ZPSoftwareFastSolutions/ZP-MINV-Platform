// Reglas puras de la sesión: permisos, errores y validación de los formularios de acceso.

import { describe, expect, it } from 'vitest';
import { asWebApiError, describeWebApiError, isAccountLocked, isEmailTaken, isWebApiError, WebApiError } from './errors';
import { activeBranch, can, canAll, canAny, hasRole, initialsOf, isCustomer, isStaff } from './permissions';
import type { Session } from './types';
import {
  hasControlChars,
  isStrongPassword,
  passwordRules,
  personNameError,
  phoneError,
  toCredentials,
  toRegistration,
  validateChangePasswordForm,
  validateLoginForm,
  validateRegisterForm,
} from './validation';

const STAFF: Session = {
  displayName: 'Andrea Quiroga',
  email: 'andrea@techzone.example',
  roles: ['VENTAS'],
  permissions: ['sales.view', 'sales.pos.operate'],
  mustChangePassword: false,
  access: {
    allBranches: false,
    branches: [
      { id: 'b-1', code: 'CM', name: 'Casa matriz' },
      { id: 'b-2', code: 'CB', name: 'Cochabamba' },
    ],
    activeBranchId: 'b-2',
  },
  kind: 'staff',
  expiresAt: new Date('2026-09-28T20:00:00Z'),
  serverVersion: '7.0.0',
  company: 'Tech Zone Gaming S.R.L.',
};

const CUSTOMER: Session = { ...STAFF, displayName: 'Valentina', roles: ['CLIENTE'], permissions: ['account.manage', 'account.reserve'], kind: 'customer' };

describe('permisos de la sesión', () => {
  it('can: solo con sesión y con el permiso exacto', () => {
    expect(can(STAFF, 'sales.view')).toBe(true);
    expect(can(STAFF, 'sales')).toBe(false);
    expect(can(STAFF, 'iam.users.manage')).toBe(false);
    expect(can(null, 'sales.view')).toBe(false);
    expect(can(undefined, 'sales.view')).toBe(false);
  });

  it('canAny y canAll: una lista vacía significa «basta con tener sesión»', () => {
    expect(canAny(STAFF, ['iam.users.manage', 'sales.view'])).toBe(true);
    expect(canAny(STAFF, ['iam.users.manage'])).toBe(false);
    expect(canAny(STAFF, [])).toBe(true);
    expect(canAny(null, [])).toBe(false);
    expect(canAll(STAFF, ['sales.view', 'sales.pos.operate'])).toBe(true);
    expect(canAll(STAFF, ['sales.view', 'reports.view'])).toBe(false);
    expect(canAll(null, [])).toBe(false);
  });

  it('distingue al personal del cliente, el rol y la sucursal activa', () => {
    expect(isStaff(STAFF)).toBe(true);
    expect(isCustomer(STAFF)).toBe(false);
    expect(isCustomer(CUSTOMER)).toBe(true);
    expect(isStaff(null)).toBe(false);
    expect(hasRole(CUSTOMER, 'CLIENTE')).toBe(true);
    expect(hasRole(STAFF, 'CLIENTE')).toBe(false);
    expect(activeBranch(STAFF)?.code).toBe('CB');
    expect(activeBranch({ access: { ...STAFF.access, activeBranchId: null } })).toBeNull();
    expect(activeBranch(null)).toBeNull();
  });

  it('arma las iniciales del avatar', () => {
    expect(initialsOf(STAFF)).toBe('AQ');
    expect(initialsOf(CUSTOMER)).toBe('VA');
    expect(initialsOf({ displayName: 'Ana María de la Fuente', email: 'a@b.example' })).toBe('AF');
    expect(initialsOf({ displayName: '  ', email: 'zoe@correo.example' })).toBe('ZO');
  });
});

describe('errores de la sesión web', () => {
  it('conserva la clase, el mensaje, los errores y el código', () => {
    const error = new WebApiError({ kind: 'validation', status: 400, message: 'Datos no válidos', errors: ['Indique su correo.'], code: null });
    expect(error).toBeInstanceOf(Error);
    expect(isWebApiError(error)).toBe(true);
    expect(error).toMatchObject({ kind: 'validation', status: 400, message: 'Datos no válidos', errors: ['Indique su correo.'], code: null, requestId: null });
  });

  it('convierte una falla desconocida sin mostrar detalles técnicos', () => {
    const converted = asWebApiError(new TypeError('Cannot read properties of undefined'));
    expect(converted.kind).toBe('unknown');
    expect(converted.message).not.toContain('undefined');
    const original = new WebApiError({ kind: 'server', message: 'x' });
    expect(asWebApiError(original)).toBe(original);
  });

  it('reconoce la cuenta bloqueada y el correo ya registrado', () => {
    const locked = new WebApiError({ kind: 'authentication', status: 401, message: 'Cuenta bloqueada por 5 intentos fallidos: espere 15 minutos.' });
    const wrong = new WebApiError({ kind: 'authentication', status: 401, message: 'Correo o contraseña incorrectos.' });
    expect(isAccountLocked(locked)).toBe(true);
    expect(isAccountLocked(wrong)).toBe(false);
    expect(isAccountLocked(new WebApiError({ kind: 'authentication', message: 'x', code: 'iam.account_locked' }))).toBe(true);
    expect(isAccountLocked(new WebApiError({ kind: 'domain', message: 'El producto está bloqueado.' }))).toBe(false);
    expect(isEmailTaken(new WebApiError({ kind: 'domain', status: 422, message: 'x', code: 'account.email_taken' }))).toBe(true);
    expect(isEmailTaken(wrong)).toBe(false);
  });

  it('describe el error con la voz de la tienda o del panel; el mensaje del servidor tiene prioridad', () => {
    const network = new WebApiError({ kind: 'network', message: 'No hubo respuesta del servidor.' });
    expect(describeWebApiError(network)).toMatch(/Revisá tu conexión/);
    expect(describeWebApiError(network, 'panel')).toMatch(/Revise la conexión/);
    expect(describeWebApiError(new WebApiError({ kind: 'rate_limited', status: 429, message: 'x' }))).toMatch(/Esperá un minuto/);
    expect(describeWebApiError(new WebApiError({ kind: 'authentication', status: 401, message: 'Correo o contraseña incorrectos.' }))).toBe('Correo o contraseña incorrectos.');
  });
});

describe('contraseña', () => {
  it('exige de 8 a 128 caracteres con letras y números', () => {
    expect(isStrongPassword('Clave123')).toBe(true);
    expect(isStrongPassword('ñandú2026')).toBe(true);
    expect(isStrongPassword('corta1')).toBe(false);
    expect(isStrongPassword('sololetras')).toBe(false);
    expect(isStrongPassword('1234567890')).toBe(false);
    expect(isStrongPassword(`a1${'x'.repeat(126)}`)).toBe(true);
    expect(isStrongPassword(`a1${'x'.repeat(127)}`)).toBe(false);
  });

  it('informa el estado de cada requisito para el indicador', () => {
    expect(passwordRules('').map((rule) => rule.met)).toEqual([false, false, false]);
    expect(passwordRules('abcdefgh').map((rule) => [rule.key, rule.met])).toEqual([
      ['length', true],
      ['letter', true],
      ['digit', false],
    ]);
    expect(passwordRules('Clave123').every((rule) => rule.met)).toBe(true);
  });
});

describe('formulario de ingreso', () => {
  it('exige correo con forma válida y contraseña, sin revelar las reglas de la contraseña', () => {
    expect(validateLoginForm({ email: '', password: '' })).toEqual({ email: 'Indicá tu correo.', password: 'Indicá tu contraseña.' });
    expect(validateLoginForm({ email: 'sin-arroba', password: 'x' })).toEqual({ email: 'Revisá el correo: debe tener la forma nombre@dominio.' });
    // Una contraseña corta NO se marca al ingresar: solo el servidor dice si las credenciales son correctas.
    expect(validateLoginForm({ email: 'ana@correo.example', password: '1' })).toEqual({});
    expect(validateLoginForm({ email: '', password: '' }, 'panel')).toEqual({ email: 'Indique su correo.', password: 'Indique su contraseña.' });
  });

  it('recorta el correo y deja la contraseña intacta', () => {
    expect(toCredentials({ email: '  ana@correo.example ', password: ' Clave 123 ' })).toEqual({ email: 'ana@correo.example', password: ' Clave 123 ' });
  });
});

describe('formulario de registro', () => {
  const valid = { name: 'Valentina Aguirre', email: 'valentina@correo.example', phone: '71234567', password: 'Clave123', confirm: 'Clave123' };

  it('acepta datos correctos y los deja listos para enviar', () => {
    expect(validateRegisterForm(valid)).toEqual({});
    expect(toRegistration({ ...valid, name: '  Valentina Aguirre ', email: ' valentina@correo.example ', phone: '591 7123 4567' })).toEqual({
      name: 'Valentina Aguirre',
      email: 'valentina@correo.example',
      phone: '+591 71234567',
      password: 'Clave123',
    });
  });

  it('marca cada campo con su error', () => {
    const errors = validateRegisterForm({ name: ' ', email: 'mal', phone: '123', password: 'corta', confirm: 'otra' });
    expect(Object.keys(errors).sort()).toEqual(['confirm', 'email', 'name', 'password', 'phone']);
    expect(errors.password).toMatch(/entre 8 y 128 caracteres/);
    expect(errors.confirm).toBe('Las contraseñas no coinciden.');
    expect(errors.phone).toBe('El teléfono debe tener 7 u 8 dígitos (Bolivia), con o sin +591.');
  });

  it('rechaza la contraseña débil y la repetición distinta', () => {
    expect(validateRegisterForm({ ...valid, password: 'sololetras', confirm: 'sololetras' })).toEqual({ password: expect.stringContaining('letras y números') });
    expect(validateRegisterForm({ ...valid, confirm: '' })).toEqual({ confirm: 'Repetí la contraseña.' });
    expect(validateRegisterForm({ ...valid, confirm: 'Clave124' })).toEqual({ confirm: 'Las contraseñas no coinciden.' });
  });

  it('el nombre no admite caracteres de control ni más de 120 caracteres', () => {
    expect(hasControlChars('Ana\nGómez')).toBe(true);
    expect(hasControlChars('Ana Gómez')).toBe(false);
    expect(personNameError('Ana\tGómez')).toMatch(/caracteres especiales de control/);
    expect(personNameError('Ana\u0000')).toMatch(/caracteres especiales de control/);
    expect(personNameError('x'.repeat(121))).toMatch(/120/);
    expect(personNameError('María José Ñandú')).toBeUndefined();
  });

  it('el teléfono es boliviano: 7 u 8 dígitos con o sin +591', () => {
    expect(phoneError('71234567')).toBeUndefined();
    expect(phoneError('2123456')).toBeUndefined();
    expect(phoneError('+591 71234567')).toBeUndefined();
    expect(phoneError('591-7123-4567')).toBeUndefined();
    expect(phoneError('')).toBe('Indicá un teléfono o WhatsApp.');
    expect(phoneError('123456')).toMatch(/7 u 8 dígitos/);
    expect(phoneError('+54 11 5555 5555')).toMatch(/7 u 8 dígitos/);
  });
});

describe('formulario de cambio de contraseña', () => {
  it('exige la actual, una nueva fuerte y distinta, y repetirla igual', () => {
    expect(validateChangePasswordForm({ current: 'Vieja123', next: 'Nueva456', confirm: 'Nueva456' })).toEqual({});
    expect(validateChangePasswordForm({ current: '', next: '', confirm: '' })).toEqual({
      current: 'Indicá tu contraseña actual.',
      next: 'Indicá la nueva contraseña.',
      confirm: 'Repetí la contraseña.',
    });
    expect(validateChangePasswordForm({ current: 'Vieja123', next: 'Vieja123', confirm: 'Vieja123' })).toEqual({ next: 'La nueva contraseña debe ser distinta de la actual.' });
    expect(validateChangePasswordForm({ current: 'Vieja123', next: 'debil', confirm: 'debil' }).next).toMatch(/entre 8 y 128/);
    expect(validateChangePasswordForm({ current: 'Vieja123', next: 'Nueva456', confirm: 'Nueva457' })).toEqual({ confirm: 'Las contraseñas no coinciden.' });
    expect(validateChangePasswordForm({ current: '', next: 'Nueva456', confirm: 'Nueva456' }, 'panel')).toEqual({ current: 'Indique su contraseña actual.' });
  });
});
