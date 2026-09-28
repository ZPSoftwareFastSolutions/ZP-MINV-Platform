// Reglas puras del contacto de una reserva: teléfono boliviano, correo opcional y el formulario completo.

import { describe, expect, it } from 'vitest';
import { formatBolivianPhone, isBolivianPhone, isValidEmail, normalizeBolivianPhone, toReservationContact, validateReservationForm } from './contact';
import { asStorefrontError, describeStorefrontError, StorefrontError } from './errors';
import { isReservationActive } from './types';

describe('teléfono de Bolivia', () => {
  it('acepta 7 u 8 dígitos con o sin +591, espacios o guiones', () => {
    expect(normalizeBolivianPhone('+591 71234567')).toBe('71234567');
    expect(normalizeBolivianPhone('591-7123-4567')).toBe('71234567');
    expect(normalizeBolivianPhone('7123 4567')).toBe('71234567');
    expect(normalizeBolivianPhone('2200000')).toBe('2200000');
    expect(formatBolivianPhone('71234567')).toBe('+591 71234567');
    expect(isBolivianPhone('(591) 71234567')).toBe(true);
  });

  it('rechaza lo que no es un teléfono boliviano', () => {
    expect(normalizeBolivianPhone('')).toBeNull();
    expect(normalizeBolivianPhone('123')).toBeNull();
    expect(normalizeBolivianPhone('+1 555 123 4567')).toBeNull();
    expect(normalizeBolivianPhone('712345678')).toBeNull();
    expect(formatBolivianPhone(' abc ')).toBe('abc');
  });
});

describe('formulario de reserva', () => {
  it('exige nombre y teléfono; el correo y las notas son opcionales pero se validan si se escriben', () => {
    expect(validateReservationForm({ name: '', phone: '', email: '', notes: '' })).toEqual({
      name: expect.stringContaining('nombre'),
      phone: expect.stringContaining('teléfono'),
    });
    expect(validateReservationForm({ name: 'Ana', phone: '123', email: 'sin-arroba', notes: 'x'.repeat(501) })).toEqual({
      phone: 'El teléfono debe tener 7 u 8 dígitos (Bolivia), con o sin +591.',
      email: expect.stringContaining('correo'),
      notes: expect.stringContaining('500'),
    });
    expect(validateReservationForm({ name: 'a'.repeat(121), phone: '71234567', email: '', notes: '' }).name).toContain('120');
    expect(validateReservationForm({ name: 'Valentina Aguirre', phone: '+591 71234567', email: 'v@correo.example', notes: '' })).toEqual({});
  });

  it('arma el contacto recortado, con el teléfono normalizado y el correo solo si se escribió', () => {
    expect(toReservationContact({ name: ' Valentina Aguirre ', phone: '7123-4567', email: ' ' })).toEqual({ name: 'Valentina Aguirre', phone: '+591 71234567' });
    expect(toReservationContact({ name: 'Ana', phone: '71234567', email: 'ana@correo.example' })).toEqual({ name: 'Ana', phone: '+591 71234567', email: 'ana@correo.example' });
    expect(isValidEmail('ana@correo')).toBe(false);
  });
});

describe('errores de la tienda', () => {
  it('envuelve cualquier fallo y elige el texto según la clase', () => {
    const network = new StorefrontError({ kind: 'network', status: 0, detail: 'No hubo respuesta de la tienda.' });
    expect(describeStorefrontError(network)).toContain('conexión');
    expect(describeStorefrontError(new StorefrontError({ kind: 'rate_limited', status: 429, detail: '' }))).toContain('minuto');
    expect(describeStorefrontError(new StorefrontError({ kind: 'unavailable', status: 503, detail: 'x' }))).toContain('no está disponible');
    expect(describeStorefrontError(new StorefrontError({ kind: 'domain', status: 422, detail: 'Texto del servidor' }))).toBe('Texto del servidor');
    expect(asStorefrontError(network)).toBe(network);
    expect(asStorefrontError(new Error('boom'))).toMatchObject({ kind: 'unknown', detail: 'boom' });
    expect(asStorefrontError('raro').kind).toBe('unknown');
  });

  it('solo una reserva «Reserved» sigue activa', () => {
    expect(isReservationActive({ status: 'Reserved' })).toBe(true);
    expect(isReservationActive({ status: 'Sold' })).toBe(false);
    expect(isReservationActive({ status: 'Expired' })).toBe(false);
  });
});
