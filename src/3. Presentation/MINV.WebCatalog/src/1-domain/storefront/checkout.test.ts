// Reglas puras de la reserva V7: texto libre en una línea, plazos que publica el servidor, tipo y horas reales de una
// reserva, y el formulario de datos del cliente (con y sin cuenta) con los datos opcionales para la factura.

import { describe, expect, it } from 'vitest';
import {
  checkoutFieldError,
  emptyCheckoutForm,
  firstInvalidField,
  toCheckoutContact,
  toCheckoutNotes,
  toReservationBuyer,
  validateCheckoutForm,
  type CheckoutFormInput,
  type CheckoutRules,
} from './checkoutForm';
import { toSingleLine } from './contact';
import {
  defaultHoldDays,
  holdDayChoices,
  isReservationNumber,
  normalizeReservationPolicy,
  parseReservationKind,
  reservationHeldHours,
  reservationKindOf,
} from './policy';
import { DEFAULT_RESERVATION_POLICY } from './types';

const GUEST: CheckoutRules = { mode: 'guest', policy: DEFAULT_RESERVATION_POLICY };
const ACCOUNT: CheckoutRules = { mode: 'account', policy: DEFAULT_RESERVATION_POLICY };

function form(changes: Partial<CheckoutFormInput> = {}): CheckoutFormInput {
  return { ...emptyCheckoutForm(DEFAULT_RESERVATION_POLICY), name: 'Valentina Aguirre', phone: '71234567', ...changes };
}

describe('texto libre en una línea', () => {
  it('une las líneas con un espacio y quita tabuladores y caracteres de control', () => {
    expect(toSingleLine('Paso el sábado\npor la mañana')).toBe('Paso el sábado por la mañana');
    expect(toSingleLine('  Uno\r\n\r\nDos\tTres  ')).toBe('Uno Dos Tres');
    expect(toSingleLine('\n\n')).toBe('');
    expect(toSingleLine('Sin cambios')).toBe('Sin cambios');
  });
});

describe('plazos de la reserva', () => {
  it('lee los plazos del catálogo con tolerancia (48 h y 3 días si no vienen) y acota los días a 3', () => {
    expect(normalizeReservationPolicy(undefined)).toEqual({ reservationHours: 48, maxHoldDays: 3 });
    expect(normalizeReservationPolicy({ reservationHours: 24, maxHoldDays: 1 })).toEqual({ reservationHours: 24, maxHoldDays: 1 });
    expect(normalizeReservationPolicy({ reservationHours: 'x', maxHoldDays: 9 })).toEqual({ reservationHours: 48, maxHoldDays: 3 });
    expect(normalizeReservationPolicy({ reservationHours: 0, maxHoldDays: -2 })).toEqual({ reservationHours: 48, maxHoldDays: 3 });
  });

  it('ofrece de 1 a maxHoldDays días y elige por defecto los que equivalen a las horas configuradas', () => {
    expect(holdDayChoices({ reservationHours: 48, maxHoldDays: 3 })).toEqual([1, 2, 3]);
    expect(holdDayChoices({ reservationHours: 24, maxHoldDays: 1 })).toEqual([1]);
    expect(defaultHoldDays({ reservationHours: 48, maxHoldDays: 3 })).toBe(2);
    expect(defaultHoldDays({ reservationHours: 72, maxHoldDays: 3 })).toBe(3);
    // 48 h pero solo se permite 1 día: el máximo permitido.
    expect(defaultHoldDays({ reservationHours: 48, maxHoldDays: 1 })).toBe(1);
    expect(defaultHoldDays({ reservationHours: 30, maxHoldDays: 2 })).toBe(2);
  });

  it('calcula las horas reales de una reserva y su tipo por el número', () => {
    const createdAt = new Date('2026-09-28T10:00:00Z');
    expect(reservationHeldHours({ createdAt, reservedUntil: new Date('2026-09-29T10:00:00Z') })).toBe(24);
    expect(reservationHeldHours({ createdAt, reservedUntil: new Date('2026-10-01T10:00:00Z') })).toBe(72);
    expect(reservationHeldHours({ createdAt, reservedUntil: new Date('no') })).toBe(0);
    expect(reservationKindOf('RES-WEB-000001')).toBe('cart');
    expect(reservationKindOf('ARM-WEB-000001')).toBe('build');
    expect(parseReservationKind('Cart', 'ARM-WEB-1')).toBe('cart');
    expect(parseReservationKind(undefined, 'res-web-000002')).toBe('cart');
    expect(parseReservationKind('otro', 'ARM-CM-000003')).toBe('build');
    expect(isReservationNumber('RES-WEB-000012')).toBe(true);
    expect(isReservationNumber(' arm-web-000001 ')).toBe(true);
    expect(isReservationNumber('XYZ-1')).toBe(false);
  });
});

describe('formulario de reserva', () => {
  it('sin cuenta exige nombre y teléfono boliviano; el correo es opcional pero válido', () => {
    expect(validateCheckoutForm(form(), GUEST)).toEqual({});
    const empty = validateCheckoutForm(emptyCheckoutForm(DEFAULT_RESERVATION_POLICY), GUEST);
    expect(empty.name).toBe('Indicá tu nombre y apellido.');
    expect(empty.phone).toBe('Indicá un teléfono o WhatsApp.');
    expect(firstInvalidField(empty)).toBe('name');
    expect(checkoutFieldError('phone', form({ phone: '123' }), GUEST)).toContain('7 u 8 dígitos');
    expect(checkoutFieldError('email', form({ email: 'mal@' }), GUEST)).toContain('nombre@dominio');
    expect(checkoutFieldError('email', form({ email: '' }), GUEST)).toBeUndefined();
    expect(checkoutFieldError('name', form({ name: 'Ana\tPérez' }), GUEST)).toContain('saltos de línea');
  });

  it('con cuenta solo valida los días y las notas (los datos salen de la cuenta)', () => {
    const blank = emptyCheckoutForm(DEFAULT_RESERVATION_POLICY);
    expect(validateCheckoutForm(blank, ACCOUNT)).toEqual({});
    expect(checkoutFieldError('name', blank, ACCOUNT)).toBeUndefined();
    expect(validateCheckoutForm({ ...blank, holdDays: '4' }, ACCOUNT)).toEqual({ holdDays: 'Elegí cuándo pasás a recogerlo.' });
    // Con una tienda que guarda solo 1 día, pedir 2 no vale.
    expect(checkoutFieldError('holdDays', { ...blank, holdDays: '2' }, { mode: 'account', policy: { reservationHours: 24, maxHoldDays: 1 } })).toBeDefined();
  });

  it('las notas cuentan su largo ya en una línea', () => {
    expect(checkoutFieldError('notes', form({ notes: `${'a'.repeat(300)}\n${'b'.repeat(199)}` }), GUEST)).toBeUndefined();
    expect(checkoutFieldError('notes', form({ notes: 'x'.repeat(501) }), GUEST)).toContain('500');
    expect(toCheckoutNotes({ notes: 'Paso el sábado\npor la mañana' })).toBe('Paso el sábado por la mañana');
    expect(toCheckoutNotes({ notes: ' \n ' })).toBeUndefined();
  });

  it('los datos para la factura son opcionales y siguen las reglas del SIN', () => {
    expect(validateCheckoutForm(form({ documentType: '5', documentNumber: '1020304050', buyerName: 'Tech SRL' }), GUEST)).toEqual({});
    expect(checkoutFieldError('documentNumber', form({ documentType: '5', documentNumber: '12AB' }), GUEST)).toBe('Con NIT el número de documento solo admite dígitos.');
    expect(checkoutFieldError('documentNumber', form({ documentType: '1', documentNumber: '' }), GUEST)).toBe('Falta el número de documento.');
    expect(checkoutFieldError('complement', form({ documentType: '5', documentNumber: '1', complement: '1A' }), GUEST)).toContain('cédula de identidad');
    expect(checkoutFieldError('documentType', form({ documentNumber: '123' }), GUEST)).toBe('Elegí el tipo de documento.');
    expect(checkoutFieldError('buyerName', form({ buyerName: 'Tech SRL' }), GUEST)).toContain('tipo y el número de documento');
    expect(checkoutFieldError('buyerName', form({ documentType: '2', documentNumber: 'E-123', buyerName: 'x'.repeat(151) }), GUEST)).toContain('150');
  });

  it('arma el contacto y los datos para la factura listos para enviar', () => {
    expect(toCheckoutContact(form({ name: ' Valentina  Aguirre ', phone: '7123 4567', email: ' v@correo.example ' }))).toEqual({
      name: 'Valentina Aguirre',
      phone: '+591 71234567',
      email: 'v@correo.example',
    });
    expect(toCheckoutContact(form())).not.toHaveProperty('email');
    expect(toReservationBuyer(form())).toBeUndefined();
    expect(toReservationBuyer(form({ documentType: '1', documentNumber: ' 1234567 ', complement: '1a', buyerName: 'Valentina\nAguirre' }))).toEqual({
      documentType: 1,
      documentNumber: '1234567',
      complement: '1A',
      name: 'Valentina Aguirre',
    });
    // Sin CI el complemento no viaja.
    expect(toReservationBuyer(form({ documentType: '5', documentNumber: '1020304050', complement: '1A' }))).toEqual({ documentType: 5, documentNumber: '1020304050' });
  });
});
