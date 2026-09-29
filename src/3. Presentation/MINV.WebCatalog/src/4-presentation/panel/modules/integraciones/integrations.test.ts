// Módulo «Integraciones» · funciones puras: estados de llaves, webhooks, entregas y correos; filtros de cada lista;
// validación de los formularios (dirección https, alcances, eventos, reenvío) y pedidos EXACTOS de los comandos;
// columnas del CSV y los resúmenes de las estadísticas.

import { describe, expect, it } from 'vitest';
import { buildCsv } from '@/4-presentation/panel/lib';
import {
  ALL_BRANCHES,
  DELIVERY_FILTERS,
  KEY_FILTERS,
  MAIL_CSV_COLUMNS,
  MAIL_FILTERS,
  WEBHOOK_FILTERS,
  branchOptions,
  catalogEvents,
  catalogScopes,
  createKeyPayload,
  createWebhookPayload,
  deliveriesQueryOf,
  deliveryKeys,
  deliveryResultText,
  filterDeliveries,
  filterKeys,
  filterMails,
  filterWebhooks,
  initialKeyForm,
  initialWebhookForm,
  integrationsSummary,
  keyCsvColumns,
  keyFormProblems,
  keyPrefixText,
  keyStatusOf,
  mailNextText,
  mailsQueryOf,
  mailsSummary,
  plainMessage,
  resendPayload,
  resendProblems,
  webhookFormProblems,
  webhookUrlProblem,
  type ApiKeyRecord,
  type CatalogData,
  type DeliveryRecord,
  type MailRecord,
  type WebhookRecord,
} from './integrations';

const CATALOG: CatalogData = {
  scopes: [
    { item1: 'catalog:read', item2: 'Leer productos, precios, códigos de barras y sucursales' },
    { item1: 'stock:read', item2: 'Leer existencias por sucursal y consolidadas' },
    { item1: 'orders:write', item2: 'Registrar pedidos de e-commerce como ventas' },
    { item1: 'reports:read', item2: 'Leer reportes gerenciales por sucursal' },
  ],
  events: [
    { item1: 'sale.completed', item2: 'Venta cobrada (POS o e-commerce)' },
    { item1: 'sale.voided', item2: 'Venta anulada (el stock volvió)' },
    { item1: 'pcbuild.reserved', item2: 'Armado de PC reservado' },
  ],
};
const SCOPES = catalogScopes(CATALOG);
const EVENTS = catalogEvents(CATALOG);

function key(overrides: Partial<ApiKeyRecord> & Pick<ApiKeyRecord, 'id' | 'name'>): ApiKeyRecord {
  return {
    prefix: 'ab12cd34',
    owner: 'admin@techzone.example',
    branch: null,
    scopes: ['catalog:read'],
    createdAt: '2026-09-20T15:00:00Z',
    expiresAt: null,
    revokedAt: null,
    lastUsedAt: null,
    isUsable: true,
    ...overrides,
  };
}

const KEYS: ApiKeyRecord[] = [
  key({ id: 'k-1', name: 'Tienda en línea', scopes: ['catalog:read', 'stock:read'], lastUsedAt: '2026-09-29T12:00:00Z' }),
  key({ id: 'k-2', name: 'ERP contable', prefix: 'ef56gh78', branch: 'CB', scopes: ['reports:read'], revokedAt: '2026-09-25T10:00:00Z', isUsable: false }),
  key({ id: 'k-3', name: 'Prueba vieja', prefix: 'zz99yy88', expiresAt: '2026-09-01T00:00:00Z', isUsable: false }),
];

function hook(overrides: Partial<WebhookRecord> & Pick<WebhookRecord, 'id' | 'url'>): WebhookRecord {
  return {
    description: null,
    events: ['sale.completed'],
    branch: null,
    isActive: true,
    createdAt: '2026-09-10T15:00:00Z',
    secretVersion: 1,
    delivered: 0,
    failed: 0,
    lastAttemptAt: null,
    lastError: null,
    ...overrides,
  };
}

const HOOKS: WebhookRecord[] = [
  hook({ id: 'h-1', url: 'https://erp.example/hooks/minv', description: 'ERP', events: ['sale.completed', 'sale.voided'], delivered: 12, failed: 2, lastError: '500: Error interno' }),
  hook({ id: 'h-2', url: 'https://tienda.example/avisos', events: ['pcbuild.reserved'], branch: 'CM', isActive: false, delivered: 3 }),
];

function delivery(overrides: Partial<DeliveryRecord> & Pick<DeliveryRecord, 'attemptedAt' | 'eventType'>): DeliveryRecord {
  return { url: 'https://erp.example/hooks/minv', attempt: 1, statusCode: 200, succeeded: true, error: null, durationMs: 120, ...overrides };
}

const DELIVERIES: DeliveryRecord[] = [
  delivery({ attemptedAt: '2026-09-29T14:00:00Z', eventType: 'sale.completed' }),
  delivery({ attemptedAt: '2026-09-29T13:00:00Z', eventType: 'sale.voided', attempt: 3, statusCode: 500, succeeded: false, error: 'Error interno', durationMs: 900 }),
  delivery({ attemptedAt: '2026-09-28T02:00:00Z', eventType: 'pcbuild.reserved', url: 'https://tienda.example/avisos', statusCode: null, succeeded: false, error: 'Tiempo de espera agotado', durationMs: 10000 }),
];

function mail(overrides: Partial<MailRecord> & Pick<MailRecord, 'id' | 'reservation' | 'status'>): MailRecord {
  return {
    reservationKind: 'Build',
    branchCode: 'CM',
    kind: 'ReservationConfirmed',
    kindText: 'Confirmación de reserva',
    recipient: 'valentina@cliente.example',
    statusText: '',
    attempts: 1,
    maxAttempts: 5,
    lastError: null,
    requestedAt: '2026-09-29T12:00:00Z',
    nextAttemptAt: null,
    lastAttemptAt: null,
    completedAt: null,
    ...overrides,
  };
}

const MAILS: MailRecord[] = [
  mail({ id: 'm-1', reservation: 'ARM-WEB-000007', status: 'Sent', lastAttemptAt: '2026-09-29T12:00:05Z', completedAt: '2026-09-29T12:00:05Z' }),
  mail({ id: 'm-2', reservation: 'RES-WEB-000012', reservationKind: 'Cart', branchCode: 'CB', status: 'Pending', attempts: 2, lastError: 'Buzón lleno', nextAttemptAt: '2026-09-29T15:00:00Z' }),
  mail({ id: 'm-3', reservation: 'ARM-CM-000100', status: 'Exhausted', attempts: 5, lastError: 'Dirección inexistente', requestedAt: '2026-09-27T12:00:00Z', completedAt: '2026-09-28T20:00:00Z' }),
];

describe('integraciones · API Keys', () => {
  it('el estado sale de la revocación y de si todavía sirve; el prefijo se muestra sin el token', () => {
    expect(KEYS.map(keyStatusOf)).toEqual(['activa', 'revocada', 'vencida']);
    expect(keyPrefixText('ab12cd34')).toBe('minv_ab12cd34_••••');
  });

  it('filtra por estado, alcance, sucursal (también «todas») y búsqueda', () => {
    const names = (filters: Partial<typeof KEY_FILTERS>) => filterKeys(KEYS, { ...KEY_FILTERS, ...filters }, SCOPES).map((item) => item.name);
    expect(names({ estado: 'activa' })).toEqual(['Tienda en línea']);
    expect(names({ alcance: 'reports:read' })).toEqual(['ERP contable']);
    expect(names({ sucursal: 'CB' })).toEqual(['ERP contable']);
    expect(names({ sucursal: ALL_BRANCHES })).toEqual(['Tienda en línea', 'Prueba vieja']);
    expect(names({ q: 'existencias' })).toEqual(['Tienda en línea']);
    expect(names({ q: 'zz99' })).toEqual(['Prueba vieja']);
    expect(branchOptions(KEYS.map((item) => item.branch), 'Todas sus sucursales')).toEqual([
      { value: ALL_BRANCHES, label: 'Todas sus sucursales' },
      { value: 'CB', label: 'CB' },
    ]);
  });

  it('el formulario marca leer catálogo y stock, valida y arma el pedido exacto (alcances en el orden del catálogo)', () => {
    const form = initialKeyForm(SCOPES);
    expect(form).toEqual({ name: 'Tienda en línea', scopes: ['catalog:read', 'stock:read'], branch: '', expiry: '90' });
    expect(keyFormProblems({ ...form, name: ' ', scopes: [] })).toEqual({
      name: 'Indique para qué es la llave (por ejemplo «Tienda en línea»).',
      scopes: 'Elija al menos un alcance.',
    });
    expect(createKeyPayload({ name: ' ERP ', scopes: ['orders:write', 'catalog:read'], branch: 'CB', expiry: '365' }, SCOPES)).toEqual({
      name: 'ERP',
      scopes: ['catalog:read', 'orders:write'],
      branchCode: 'CB',
      expiresInDays: 365,
    });
    expect(createKeyPayload({ ...form, expiry: '' }, SCOPES)).toMatchObject({ branchCode: null, expiresInDays: null });
  });

  it('el CSV de las llaves nombra los alcances en palabras', () => {
    const [header, first] = buildCsv(keyCsvColumns(SCOPES), KEYS.slice(0, 1), { bom: false }).split('\r\n');
    expect(header).toBe('"Nombre";"Llave";"Estado";"Alcances";"Sucursal";"Creada por";"Creada";"Vence";"Revocada";"Último uso"');
    expect(first).toContain('"Leer productos, precios, códigos de barras y sucursales · Leer existencias por sucursal y consolidadas";"Todas sus sucursales"');
  });
});

describe('integraciones · webhooks', () => {
  it('la dirección debe ser https (http solo en este equipo) y sin usuario ni contraseña', () => {
    expect(webhookUrlProblem('https://erp.example/hooks')).toBeNull();
    expect(webhookUrlProblem('http://localhost:5000/hooks')).toBeNull();
    expect(webhookUrlProblem('http://erp.example/hooks')).toBe('El webhook debe usar https (http solo para pruebas en este mismo equipo).');
    expect(webhookUrlProblem('https://yo:clave@erp.example/hooks')).toBe('La dirección no debe incluir usuario ni contraseña.');
    expect(webhookUrlProblem('erp.example/hooks')).toBe('Escriba la dirección completa, empezando con https.');
    expect(webhookUrlProblem('')).toBe('Indique la dirección (URL) que recibirá los avisos.');
  });

  it('el formulario marca «Venta cobrada», valida y arma el pedido exacto', () => {
    const form = initialWebhookForm(EVENTS);
    expect(form.events).toEqual(['sale.completed']);
    expect(webhookFormProblems({ ...form, events: [] })).toEqual({ url: 'Indique la dirección (URL) que recibirá los avisos.', events: 'Elija al menos un evento.' });
    expect(createWebhookPayload({ url: ' https://erp.example/h ', description: ' ', events: ['pcbuild.reserved', 'sale.completed'], branch: '' }, EVENTS)).toEqual({
      url: 'https://erp.example/h',
      events: ['sale.completed', 'pcbuild.reserved'],
      description: null,
      branchCode: null,
    });
  });

  it('filtra por estado, evento, sucursal y búsqueda', () => {
    const urls = (filters: Partial<typeof WEBHOOK_FILTERS>) => filterWebhooks(HOOKS, { ...WEBHOOK_FILTERS, ...filters }).map((item) => item.id);
    expect(urls({ estado: 'desactivado' })).toEqual(['h-2']);
    expect(urls({ evento: 'sale.voided' })).toEqual(['h-1']);
    expect(urls({ sucursal: 'CM' })).toEqual(['h-2']);
    expect(urls({ sucursal: ALL_BRANCHES })).toEqual(['h-1']);
    expect(urls({ q: 'error interno' })).toEqual(['h-1']);
  });
});

describe('integraciones · entregas', () => {
  it('el resultado dice el código de respuesta o que no hubo respuesta', () => {
    expect(DELIVERIES.map(deliveryResultText)).toEqual(['Correcta (200)', 'Fallida (500)', 'Fallida (sin respuesta)']);
  });

  it('el webhook y cuántas revisar van al servidor (un valor raro vuelve a 200)', () => {
    expect(deliveriesQueryOf({ ...DELIVERY_FILTERS, webhook: 'h-1', registros: '500' })).toEqual({ endpointId: 'h-1', take: 500 });
    expect(deliveriesQueryOf({ ...DELIVERY_FILTERS, registros: '99999' })).toEqual({ endpointId: null, take: 200 });
  });

  it('en la página filtra por resultado, evento, días de La Paz y búsqueda', () => {
    const times = (filters: Partial<typeof DELIVERY_FILTERS>) => filterDeliveries(DELIVERIES, { ...DELIVERY_FILTERS, ...filters }, EVENTS).map((item) => item.attemptedAt);
    expect(times({ resultado: 'fallida' })).toEqual(['2026-09-29T13:00:00Z', '2026-09-28T02:00:00Z']);
    expect(times({ evento: 'sale.voided' })).toEqual(['2026-09-29T13:00:00Z']);
    // 2026-09-28T02:00Z todavía es el 27 en La Paz.
    expect(times({ desde: '2026-09-27', hasta: '2026-09-27' })).toEqual(['2026-09-28T02:00:00Z']);
    expect(times({ q: 'armado' })).toEqual(['2026-09-28T02:00:00Z']);
  });

  it('cada entrega tiene una clave única aunque se repitan hora, dirección y evento', () => {
    const twice = [DELIVERIES[0], { ...DELIVERIES[0] }];
    const keys = deliveryKeys(twice);
    expect(new Set(keys.values()).size).toBe(2);
  });
});

describe('integraciones · correos de reservas', () => {
  it('el estado, la reserva (en mayúsculas) y cuántos revisar van al servidor', () => {
    expect(mailsQueryOf({ ...MAIL_FILTERS, estado: 'Exhausted', reserva: ' arm-web-000007 ', registros: '50' })).toEqual({ status: 'Exhausted', number: 'ARM-WEB-000007', take: 50 });
    expect(mailsQueryOf({ ...MAIL_FILTERS, estado: 'Inventado' })).toEqual({ status: null, number: null, take: 200 });
  });

  it('en la página filtra por sucursal, tipo de reserva, fechas y búsqueda', () => {
    const ids = (filters: Partial<typeof MAIL_FILTERS>) => filterMails(MAILS, { ...MAIL_FILTERS, ...filters }).map((item) => item.id);
    expect(ids({ sucursal: 'CB' })).toEqual(['m-2']);
    expect(ids({ tipo: 'Cart' })).toEqual(['m-2']);
    expect(ids({ desde: '2026-09-29', hasta: '2026-09-29' })).toEqual(['m-1', 'm-2']);
    expect(ids({ q: 'inexistente' })).toEqual(['m-3']);
  });

  it('dice qué sigue con cada correo', () => {
    expect(MAILS.map(mailNextText)).toEqual(['Enviado el 29/09/2026 08:00', 'Próximo intento 29/09/2026 11:00', 'Agotado (sin enviar) el 28/09/2026 16:00']);
  });

  it('el reenvío valida la reserva y el correo, y arma el pedido exacto', () => {
    expect(resendProblems({ number: ' ', email: 'no-es-correo' })).toEqual({ number: 'Indique el número de la reserva.', email: 'Escriba un correo válido o déjelo vacío.' });
    expect(resendPayload({ number: ' arm-web-000007 ', email: '' })).toEqual({ number: 'ARM-WEB-000007', email: null });
    expect(resendPayload({ number: 'RES-WEB-000012', email: ' otro@cliente.example ' })).toEqual({ number: 'RES-WEB-000012', email: 'otro@cliente.example' });
  });

  it('el CSV de la cola lleva la reserva, el destinatario, el estado y los intentos', () => {
    const [header, first] = buildCsv(MAIL_CSV_COLUMNS, MAILS.slice(1, 2), { bom: false }).split('\r\n');
    expect(header.split(';').slice(0, 8)).toEqual(['"Solicitado"', '"Reserva"', '"Tipo de reserva"', '"Sucursal"', '"Correo"', '"Destinatario"', '"Estado"', '"Intentos"']);
    expect(first).toContain('"RES-WEB-000012";"Carrito";"CB";"Confirmación de reserva";"valentina@cliente.example";"Pendiente";2;5');
  });
});

describe('integraciones · resúmenes', () => {
  it('llaves activas y en uso, webhooks activos, suscripciones y entregas', () => {
    expect(integrationsSummary(KEYS, HOOKS)).toEqual({ keysActive: 1, keysUsed: 1, hooksActive: 1, subscriptions: 2, delivered: 15, failed: 2 });
  });

  it('correos pendientes, agotados, enviados hoy (La Paz) y cancelados', () => {
    expect(mailsSummary(MAILS, new Date('2026-09-29T20:00:00Z'))).toEqual({ pending: 1, exhausted: 1, sentToday: 1, cancelled: 0 });
  });

  it('quita la marca del servidor de los mensajes', () => {
    expect(plainMessage('✔ Webhook registrado.')).toBe('Webhook registrado.');
  });
});
