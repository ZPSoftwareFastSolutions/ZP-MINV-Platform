// Reglas puras de la cuenta del cliente: documento para la factura y formulario «Mis datos».

import { describe, expect, it } from 'vitest';
import { DOCUMENT_TYPES, documentType, isDocumentTypeCode, parseDocumentType, validateBuyerDocument } from './documents';
import type { CustomerAccount } from './types';
import { toAccountForm, toAccountUpdate, validateAccountForm } from './validation';

describe('documento del comprador', () => {
  it('conoce los cinco tipos del SIN', () => {
    expect(DOCUMENT_TYPES.map((type) => [type.code, type.short])).toEqual([
      [1, 'CI'],
      [2, 'CEX'],
      [3, 'PAS'],
      [4, 'OD'],
      [5, 'NIT'],
    ]);
    expect(documentType(5)?.numeric).toBe(true);
    expect(documentType(3)?.numeric).toBe(false);
    expect(documentType(9)).toBeUndefined();
    expect(isDocumentTypeCode(1)).toBe(true);
    expect(isDocumentTypeCode('1')).toBe(false);
    expect(parseDocumentType('5')).toBe(5);
    expect(parseDocumentType('')).toBeNull();
    expect(parseDocumentType('6')).toBeNull();
  });

  it('es opcional: sin tipo y sin número no hay errores', () => {
    expect(validateBuyerDocument({ documentType: '', documentNumber: '', complement: '' })).toEqual({});
  });

  it('con número pide el tipo, y con tipo pide el número', () => {
    expect(validateBuyerDocument({ documentType: '', documentNumber: '1234567', complement: '' })).toEqual({ documentType: 'Elegí el tipo de documento.' });
    expect(validateBuyerDocument({ documentType: '1', documentNumber: ' ', complement: '' })).toEqual({ documentNumber: 'Falta el número de documento.' });
  });

  it('CI y NIT solo admiten dígitos; pasaporte y cédula de extranjero admiten letras', () => {
    expect(validateBuyerDocument({ documentType: '1', documentNumber: '1234567', complement: '' })).toEqual({});
    expect(validateBuyerDocument({ documentType: '1', documentNumber: '12345-6', complement: '' }).documentNumber).toMatch(/CI.*solo admite dígitos/);
    expect(validateBuyerDocument({ documentType: '5', documentNumber: '10203040A', complement: '' }).documentNumber).toMatch(/NIT.*solo admite dígitos/);
    expect(validateBuyerDocument({ documentType: '3', documentNumber: 'AB123456', complement: '' })).toEqual({});
    expect(validateBuyerDocument({ documentType: '2', documentNumber: 'E-1234567', complement: '' })).toEqual({});
    expect(validateBuyerDocument({ documentType: '5', documentNumber: '1'.repeat(21), complement: '' }).documentNumber).toMatch(/máximo 20/);
  });

  it('el complemento solo va con CI y tiene hasta 5 caracteres', () => {
    expect(validateBuyerDocument({ documentType: '1', documentNumber: '1234567', complement: '1A' })).toEqual({});
    expect(validateBuyerDocument({ documentType: '5', documentNumber: '1020304050', complement: '1A' })).toEqual({ complement: 'El complemento solo se usa con cédula de identidad.' });
    expect(validateBuyerDocument({ documentType: '1', documentNumber: '1234567', complement: '123456' }).complement).toMatch(/máximo 5/);
    expect(validateBuyerDocument({ documentType: '1', documentNumber: '1234567', complement: '1-A' }).complement).toMatch(/letras y números/);
    expect(validateBuyerDocument({ documentType: '', documentNumber: '', complement: '1A' })).toEqual({ complement: 'El complemento solo se usa con cédula de identidad.' });
  });
});

describe('formulario «Mis datos»', () => {
  const account: CustomerAccount = { name: 'Valentina Aguirre', email: 'valentina@correo.example', phone: '+591 71234567', documentType: 1, documentNumber: '1234567', complement: '1A' };

  it('se arma desde la cuenta y vuelve a los cambios listos para enviar', () => {
    const form = toAccountForm(account);
    expect(form).toEqual({ name: 'Valentina Aguirre', phone: '+591 71234567', documentType: '1', documentNumber: '1234567', complement: '1A' });
    expect(validateAccountForm(form)).toEqual({});
    expect(toAccountUpdate({ ...form, name: ' Valentina A. ', phone: '7123 4567', complement: '1a' })).toEqual({
      name: 'Valentina A.',
      phone: '+591 71234567',
      documentType: 1,
      documentNumber: '1234567',
      complement: '1A',
    });
  });

  it('sin documento no viaja ni número ni complemento; el complemento solo con CI', () => {
    const empty = toAccountForm({ ...account, documentType: null, documentNumber: null, complement: null });
    expect(empty).toMatchObject({ documentType: '', documentNumber: '', complement: '' });
    expect(toAccountUpdate(empty)).toMatchObject({ documentType: null, documentNumber: null, complement: null });
    expect(toAccountUpdate({ ...toAccountForm(account), documentType: '5', complement: '1A' })).toMatchObject({ documentType: 5, complement: null });
  });

  it('valida nombre, teléfono y documento juntos', () => {
    const errors = validateAccountForm({ name: '', phone: '12', documentType: '5', documentNumber: 'ABC', complement: '' });
    expect(Object.keys(errors).sort()).toEqual(['documentNumber', 'name', 'phone']);
    expect(validateAccountForm({ name: 'Ana\nGómez', phone: '71234567', documentType: '', documentNumber: '', complement: '' }).name).toMatch(/control/);
  });
});
