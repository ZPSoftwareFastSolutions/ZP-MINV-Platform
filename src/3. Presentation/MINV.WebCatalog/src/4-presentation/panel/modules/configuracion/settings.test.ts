// Módulo «Configuración» · funciones puras: parámetros de la empresa, estado de la facturación y qué falta para
// facturar, formularios del Padrón, de la conexión (direcciones https y el token que solo se escribe), de las sucursales
// del Padrón, de los puntos de venta y del correo, con los pedidos EXACTOS de cada comando; filtros y CSV.

import { describe, expect, it } from 'vitest';
import { buildCsv } from '@/4-presentation/panel/lib';
import {
  ACTIVITY_FILTERS,
  POINT_CSV_COLUMNS,
  POINT_FILTERS,
  PADRON_FILTERS,
  applyGmail,
  billingStateOf,
  billingStateText,
  clockText,
  companyParametersPayload,
  daysOptions,
  endpointsFromBase,
  filterActivities,
  filterPadron,
  filterPoints,
  goesToProduction,
  knownNamespace,
  linkPayload,
  mailFormOf,
  mailPayload,
  mailProblems,
  maintenanceText,
  marginOptions,
  marginText,
  padronPayload,
  padronProblems,
  pointName,
  profileFormOf,
  profilePayload,
  profileProblems,
  readinessChecks,
  registerPointFormOf,
  registerPointPayload,
  registerPointProblems,
  serviceUrlProblem,
  siatSettingsFormOf,
  siatSettingsPayload,
  siatSettingsProblems,
  syncText,
  tokenStatusText,
  validityText,
  type EconomicActivity,
  type PointRecord,
  type SiatSettingsData,
} from './settings';

const BASE = 'https://pilotosiatservicios.impuestos.gob.bo';

function view(overrides: Partial<SiatSettingsData> = {}): SiatSettingsData {
  return {
    configured: true,
    nit: 1023456029,
    businessName: 'Tech Zone Gaming S.R.L.',
    systemCode: 'ABC123',
    environment: 2,
    isEnabled: false,
    onlineLegend: 'Leyenda en línea',
    offlineLegend: 'Leyenda fuera de línea',
    clockSyncedAt: '2026-09-29T12:00:00Z',
    clockOffsetMs: 1500,
    profiles: [
      {
        environment: 2,
        endpoints: {
          codes: `${BASE}/v2/FacturacionCodigos`,
          sync: `${BASE}/v2/FacturacionSincronizacion`,
          operations: `${BASE}/v2/FacturacionOperaciones`,
          purchaseSale: `${BASE}/v2/ServicioFacturacionCompraVenta`,
          computerized: `${BASE}/v2/ServicioFacturacionComputarizada`,
          adjustment: `${BASE}/v2/ServicioFacturacionDocumentoAjuste`,
          namespace: 'https://siat.impuestos.gob.bo/',
        },
        qrBaseUrl: 'https://pilotosiat.impuestos.gob.bo/consulta/QR',
        timeoutSeconds: 15,
        hasToken: true,
        tokenValidUntil: '2026-12-31',
        tokenUpdatedAt: '2026-09-01T12:00:00Z',
      },
    ],
    branches: [
      { branchId: 'b-CM', branchCode: 'CM', branchName: 'Casa matriz', siatCode: 0, municipality: 'La Paz', phone: '2-2440000' },
      { branchId: 'b-CB', branchCode: 'CB', branchName: 'Sucursal Cochabamba', siatCode: null, municipality: null, phone: null },
      { branchId: 'b-SC', branchCode: 'SC', branchName: 'Sucursal Santa Cruz', siatCode: 2, municipality: 'Santa Cruz', phone: null },
    ],
    mail: { host: 'smtp.gmail.com', port: 587, useSsl: true, userName: 'facturas@techzone.example', hasPassword: true, fromAddress: 'facturas@techzone.example', fromName: 'Tech Zone', isEnabled: true },
    moduleActive: true,
    ...overrides,
  };
}

function point(overrides: Partial<PointRecord> & Pick<PointRecord, 'id' | 'code' | 'name'>): PointRecord {
  return {
    branchCode: 'CM',
    branchId: 'b-CM',
    branchName: 'Casa matriz',
    cufdObtainedAt: null,
    cufdValidUntil: '2026-09-30T12:00:00Z',
    cuisValidUntil: '2027-09-01T00:00:00Z',
    environment: 2,
    isClosed: false,
    lastContactAt: null,
    lastError: null,
    mode: 'Online',
    modeSince: '2026-09-01T12:00:00Z',
    offlineDocuments: 0,
    openEvent: null,
    pendingDocuments: 0,
    registerCode: null,
    retryAt: null,
    siatBranchCode: 0,
    ...overrides,
  };
}

describe('configuración · empresa', () => {
  it('ofrece los márgenes y días del escritorio (y el valor actual si es otro) y arma el pedido', () => {
    expect(marginText(0.15)).toBe('15 %');
    expect(marginOptions(0.2).map((option) => option.value)).toEqual(['0.1', '0.15', '0.2', '0.25', '0.3', '0.4', '0.5']);
    expect(marginOptions(0.35).map((option) => option.value)).toContain('0.35');
    expect(marginOptions(null)[2].label).toBe('20 % sobre el mínimo');
    expect(daysOptions(75).map((option) => option.label)).toContain('75 días');
    expect(companyParametersPayload('0.3', '90')).toEqual({ alertMargin: 0.3, daysWithoutRotation: 90 });
  });
});

describe('configuración · facturación', () => {
  it('dice en qué está la facturación y qué falta para activarla', () => {
    expect(billingStateOf(view())).toBe('desactivada');
    expect(billingStateOf(view({ configured: false }))).toBe('sin-configurar');
    expect(billingStateText(view({ isEnabled: true }))).toBe('Activa en el ambiente pruebas y piloto (2): las ventas emiten factura del SIN.');
    expect(readinessChecks(view()).map((check) => [check.key, check.ok])).toEqual([
      ['padron', true],
      ['token', true],
      ['matriz', true],
      ['modulo', true],
    ]);
    // En producción todavía no hay conexión con token.
    expect(readinessChecks(view(), 1).find((check) => check.key === 'token')?.ok).toBe(false);
    expect(clockText(view())).toBe('Hora sincronizada con el SIN el 29/09/2026 08:00 (diferencia de 1,5 s); las facturas llevan la hora del SIN.');
    expect(clockText(view({ clockSyncedAt: null }))).toBe('La hora todavía no se sincronizó con el SIN: use «Preparar SIAT».');
  });

  it('el token nunca se muestra: solo si hay uno, su vigencia y cuándo se cargó', () => {
    expect(tokenStatusText(view().profiles[0])).toBe('Hay un token guardado (cifrado), vigente hasta el 31/12/2026 · cargado el 01/09/2026 08:00. Escriba uno nuevo solo para reemplazarlo.');
    expect(tokenStatusText(undefined)).toBe('No hay token guardado para este ambiente: genérelo en el Portal SIAT (Token Delegado) y cárguelo aquí.');
  });

  it('los datos del Padrón: valida con las reglas del servidor y arma el pedido (leyendas vacías = las oficiales)', () => {
    const form = siatSettingsFormOf(view(), 'Otra');
    expect(form).toMatchObject({ nit: '1023456029', businessName: 'Tech Zone Gaming S.R.L.', systemCode: 'ABC123', environment: '2', enabled: false });
    expect(siatSettingsProblems({ ...form, nit: '12a', businessName: ' ', systemCode: '' })).toEqual({
      nit: 'El NIT lleva solo números (de 1 a 13 cifras), tal como figura en el Padrón.',
      businessName: 'Indique la razón social tal como figura en el Padrón.',
      systemCode: 'Indique el código de sistema que asignó el SIN.',
    });
    expect(siatSettingsPayload({ ...form, onlineLegend: ' ', environment: '1', enabled: true })).toEqual({
      nit: 1023456029,
      businessName: 'Tech Zone Gaming S.R.L.',
      systemCode: 'ABC123',
      environment: 1,
      onlineLegend: null,
      offlineLegend: 'Leyenda fuera de línea',
      enabled: true,
    });
    expect(goesToProduction(view(), { ...form, environment: '1' })).toBe(true);
    expect(goesToProduction(view({ environment: 1 }), { ...form, environment: '1' })).toBe(false);
    expect(siatSettingsFormOf(view({ configured: false, nit: null, businessName: null, systemCode: null }), 'Tech Zone')).toMatchObject({ nit: '', businessName: 'Tech Zone', systemCode: '' });
  });

  it('la conexión: direcciones https (http solo en este equipo), completar desde la base y el token solo si se escribe', () => {
    expect(serviceUrlProblem(`${BASE}/v2/FacturacionCodigos`)).toBeNull();
    expect(serviceUrlProblem('http://localhost:5095/v2/FacturacionCodigos')).toBeNull();
    expect(serviceUrlProblem('http://siat.example/v2')).toBe('Debe ser https (http solo en este mismo equipo).');
    expect(serviceUrlProblem('siat.example')).toBe('Escriba la dirección completa, empezando con https.');
    expect(endpointsFromBase(`${BASE}/`)).toEqual({
      codes: `${BASE}/v2/FacturacionCodigos`,
      sync: `${BASE}/v2/FacturacionSincronizacion`,
      operations: `${BASE}/v2/FacturacionOperaciones`,
      purchaseSale: `${BASE}/v2/ServicioFacturacionCompraVenta`,
      computerized: `${BASE}/v2/ServicioFacturacionComputarizada`,
      adjustment: `${BASE}/v2/ServicioFacturacionDocumentoAjuste`,
    });
    expect(endpointsFromBase('ftp://algo')).toBeNull();

    const empty = profileFormOf(undefined, knownNamespace(view()));
    expect(empty.namespace).toBe('https://siat.impuestos.gob.bo/');
    expect(Object.keys(profileProblems({ ...empty, timeout: 200, token: 'corto' })).sort()).toEqual(
      ['adjustment', 'codes', 'computerized', 'operations', 'purchaseSale', 'qrBaseUrl', 'sync', 'timeout', 'token'].sort(),
    );
    const form = profileFormOf(view().profiles[0], '');
    expect(form.token).toBe('');
    expect(profileProblems(form)).toEqual({});
    expect(profilePayload(2, form)).toEqual({
      environment: 2,
      endpoints: { ...view().profiles[0].endpoints },
      qrBaseUrl: 'https://pilotosiat.impuestos.gob.bo/consulta/QR',
      timeoutSeconds: 15,
      newToken: null,
      tokenValidUntil: '2026-12-31',
    });
    expect(profilePayload(2, { ...form, token: ' TOKEN-NUEVO-123 ', tokenValidUntil: '' })).toMatchObject({ newToken: 'TOKEN-NUEVO-123', tokenValidUntil: null });
  });

  it('las sucursales del Padrón: filtros, validación y pedido', () => {
    const codes = (filters: Partial<typeof PADRON_FILTERS>) => filterPadron(view().branches, { ...PADRON_FILTERS, ...filters }).map((branch) => branch.branchCode);
    expect(codes({ estado: 'sin-codigo' })).toEqual(['CB']);
    expect(codes({ estado: 'matriz' })).toEqual(['CM']);
    expect(codes({ q: 'santa' })).toEqual(['SC']);
    expect(padronProblems({ siatCode: null, municipality: ' ', phone: '' })).toEqual({
      siatCode: 'Escriba el código de la sucursal en el Padrón (0 = casa matriz).',
      municipality: 'Indique el municipio que va en la factura.',
    });
    expect(padronProblems({ siatCode: 10000, municipality: 'X', phone: '' })).toEqual({ siatCode: 'El código del Padrón va de 0 (casa matriz) a 9999.' });
    expect(padronPayload('CB', { siatCode: 1, municipality: ' Cochabamba ', phone: ' ' })).toEqual({ branchCode: 'CB', siatCode: 1, municipality: 'Cochabamba', phone: null });
  });

  it('los puntos de venta: nombre, vigencias, filtros (por defecto los abiertos), CSV y pedidos', () => {
    const points = [
      point({ id: 'p-0', code: 0, name: 'Sin punto de venta' }),
      point({ id: 'p-1', code: 1, name: 'Caja 1', registerCode: 'CM-CAJA1' }),
      point({ id: 'p-2', code: 1, name: 'Caja SC', branchCode: 'SC', mode: 'Offline', lastError: 'Sin conexión' }),
      point({ id: 'p-3', code: 2, name: 'Caja vieja', isClosed: true }),
    ];
    expect(pointName(points[0])).toBe('Sin punto de venta (0)');
    expect(pointName(points[1])).toBe('Punto 1 · Caja 1');
    const ids = (filters: Partial<typeof POINT_FILTERS>) => filterPoints(points, { ...POINT_FILTERS, ...filters }).map((item) => item.id);
    expect(ids({})).toEqual(['p-0', 'p-1', 'p-2']);
    expect(ids({ estado: 'cerrados' })).toEqual(['p-3']);
    expect(ids({ estado: '', modo: 'Offline' })).toEqual(['p-2']);
    expect(ids({ sucursal: 'SC' })).toEqual(['p-2']);
    expect(ids({ q: 'cm-caja1' })).toEqual(['p-1']);
    const now = new Date('2026-09-30T13:00:00Z');
    expect(validityText('2026-09-30T12:00:00Z', now)).toBe('Vencido (30/09/2026 08:00)');
    expect(validityText('2026-10-01T12:00:00Z', now)).toBe('Hasta 01/10/2026 08:00');
    expect(validityText(null, now)).toBe('Sin código');
    expect(buildCsv(POINT_CSV_COLUMNS, points.slice(3), { bom: false }).split('\r\n')[1]).toContain('"CM";0;2;"Caja vieja";;"Cerrado en el SIN"');

    const form = registerPointFormOf(view().branches.filter((branch) => branch.siatCode !== null), 'b-SC');
    expect(form).toEqual({ branchCode: 'SC', name: 'Caja', description: '', registerCode: '', typeCode: 5 });
    expect(registerPointProblems({ ...form, branchCode: '', name: ' ', typeCode: 0 })).toEqual({
      branchCode: 'Elija la sucursal (debe tener su código del Padrón).',
      name: 'El nombre es obligatorio (el SIN lo rechaza vacío).',
      typeCode: 'El tipo de punto de venta va de 1 a 99 (5 = cajeros).',
    });
    expect(registerPointPayload({ ...form, registerCode: 'sc-caja1', description: ' ' })).toEqual({ branchCode: 'SC', name: 'Caja', description: null, registerCode: 'SC-CAJA1', typeCode: 5 });
    expect(linkPayload('p-1', ' ')).toEqual({ pointOfSaleId: 'p-1', registerCode: null });
    expect(linkPayload('p-1', 'cm-caja2')).toEqual({ pointOfSaleId: 'p-1', registerCode: 'CM-CAJA2' });
  });

  it('los resultados de preparar y sincronizar, y las actividades', () => {
    expect(maintenanceText({ cuisRequested: 1, cufdRequested: 3, documentsSent: 0, messages: [], packagesValidated: 0, recovered: 0 })).toBe(
      'CUIS pedidos: 1 · CUFD pedidos: 3 · documentos enviados: 0 · paquetes validados: 0 · puntos recuperados: 0',
    );
    expect(syncText({ catalogs: 18, items: 1234, errors: [], clockSyncedAt: null })).toBe('18 catálogos · 1.234 filas');
    const activities: EconomicActivity[] = [
      { code: '474100', description: 'Venta al por menor de computadoras', activityType: 'P', isCurrent: true, sectors: [1, 24] },
      { code: '620100', description: 'Programación informática', activityType: 'S', isCurrent: false, sectors: [1] },
    ];
    expect(filterActivities(activities, { ...ACTIVITY_FILTERS, vigente: 'no' }).map((item) => item.code)).toEqual(['620100']);
    expect(filterActivities(activities, { ...ACTIVITY_FILTERS, q: 'computadoras' }).map((item) => item.code)).toEqual(['474100']);
  });
});

describe('configuración · correo de la empresa', () => {
  it('la contraseña guardada nunca vuelve: sin «Cambiar la contraseña» viaja null', () => {
    const form = mailFormOf(view().mail, 'Tech Zone');
    expect(form).toMatchObject({ host: 'smtp.gmail.com', port: 587, useSsl: true, changePassword: false, password: '' });
    expect(mailProblems(form, true)).toEqual({});
    expect(mailPayload(form)).toEqual({
      host: 'smtp.gmail.com',
      port: 587,
      useSsl: true,
      userName: 'facturas@techzone.example',
      newPassword: null,
      fromAddress: 'facturas@techzone.example',
      fromName: 'Tech Zone',
      enabled: true,
    });
    expect(mailProblems({ ...form, changePassword: true }, true)).toEqual({ password: 'Escriba la contraseña nueva o desmarque «Cambiar la contraseña».' });
    expect(mailPayload({ ...form, changePassword: true, password: 'abcd efgh ijkl mnop' }).newPassword).toBe('abcd efgh ijkl mnop');
  });

  it('valida por campo y Gmail completa servidor, puerto y STARTTLS', () => {
    const empty = mailFormOf(null, 'Tech Zone');
    expect(empty).toMatchObject({ host: '', port: 587, useSsl: true, changePassword: true, fromName: 'Tech Zone', enabled: true });
    expect(mailProblems({ ...empty, port: 0, fromName: ' ' }, false)).toEqual({
      host: 'Indique el servidor SMTP (por ejemplo smtp.gmail.com).',
      port: 'El puerto va de 1 a 65535 (587 con STARTTLS, 465 con SSL).',
      fromAddress: 'Indique el correo remitente, por ejemplo facturas@suempresa.com.',
      fromName: 'Indique el nombre del remitente.',
    });
    expect(applyGmail({ ...empty, host: 'otro', port: 25, useSsl: false, fromAddress: 'tienda@gmail.com' })).toMatchObject({
      host: 'smtp.gmail.com',
      port: 587,
      useSsl: true,
      userName: 'tienda@gmail.com',
    });
  });
});
