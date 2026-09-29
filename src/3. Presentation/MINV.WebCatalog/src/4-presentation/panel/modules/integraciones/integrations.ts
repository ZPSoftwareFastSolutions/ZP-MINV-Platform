// Módulo «Integraciones» · funciones puras (sin React): API Keys, webhooks, entregas y la cola de correos de las
// reservas. Estados en palabras, filtros de cada lista, columnas del CSV, validación de los formularios con las mismas
// reglas del servidor y los pedidos EXACTOS de los comandos. Los textos siguen al escritorio (IntegrationsViewModel).
//
// Los tipos del servidor SALEN DEL CONTRATO (regla P-07): se derivan del nombre de la operación.

import { permissionName, type RpcRequestOf, type RpcResponseOf } from '@/4-presentation/app/contract';
import { defineStatuses, statusOf, type SelectOption } from '@/4-presentation/panel/kit';
import { formatDateTime, formatNumber, inRange, laPazToday, matchesSearch, toDate, toIsoDate, type CsvColumn } from '@/4-presentation/panel/lib';

/** Una llave de `GetApiKeysQuery` (nunca trae el token: solo el prefijo). */
export type ApiKeyRecord = RpcResponseOf<'GetApiKeysQuery'>[number];
/** Un webhook de `GetWebhooksQuery`. */
export type WebhookRecord = RpcResponseOf<'GetWebhooksQuery'>[number];
/** Un intento de entrega de `GetWebhookDeliveriesQuery`. */
export type DeliveryRecord = RpcResponseOf<'GetWebhookDeliveriesQuery'>[number];
/** Un correo de la cola (`GetOutgoingMailsQuery`). */
export type MailRecord = RpcResponseOf<'GetOutgoingMailsQuery'>[number];
/** Alcances y eventos disponibles (`GetIntegrationCatalogQuery`). */
export type CatalogData = RpcResponseOf<'GetIntegrationCatalogQuery'>;
/** Una sucursal de `GetBranchesQuery`. */
export type BranchRecord = RpcResponseOf<'GetBranchesQuery'>[number];
/** Llave recién creada (con su token, que se muestra UNA vez). */
export type CreatedKey = RpcResponseOf<'CreateApiKeyCommand'>;
/** Webhook recién creado o con el secreto rotado (con su secreto, que se muestra UNA vez). */
export type CreatedHook = RpcResponseOf<'CreateWebhookCommand'>;
/** Estado de un correo de la cola, como lo pide `GetOutgoingMailsQuery`. */
export type MailStatus = NonNullable<RpcRequestOf<'GetOutgoingMailsQuery'>['status']>;

/** Valor de «Sucursal» para lo que vale en todas las sucursales de quien lo creó. */
export const ALL_BRANCHES = '_todas';

// ---------------------------------------------------------------------------------------------------- comunes

export interface NamedOption {
  code: string;
  name: string;
}

/** Alcances de una llave, con su descripción del servidor. */
export function catalogScopes(catalog: CatalogData | undefined): NamedOption[] {
  return (catalog?.scopes ?? []).map((scope) => ({ code: scope.item1, name: scope.item2 }));
}

/** Eventos de los webhooks, con su descripción del servidor. */
export function catalogEvents(catalog: CatalogData | undefined): NamedOption[] {
  return (catalog?.events ?? []).map((event) => ({ code: event.item1, name: event.item2 }));
}

/** Nombre de un código (alcance o evento); uno desconocido se muestra tal cual. */
export function nameOf(options: readonly NamedOption[], code: string): string {
  return options.find((option) => option.code === code)?.name ?? code;
}

export function optionsOf(options: readonly NamedOption[]): SelectOption[] {
  return options.map((option) => ({ value: option.code, label: option.name }));
}

/** Sucursales de una lista (las de las filas) como opciones, más «Todas sus sucursales» para las que no tienen. */
export function branchOptions(codes: readonly (string | null)[], allLabel: string): SelectOption[] {
  const known = [...new Set(codes.filter((code): code is string => code !== null))].sort((a, b) => a.localeCompare(b, 'es'));
  const options = known.map((code) => ({ value: code, label: code }));
  return codes.some((code) => code === null) ? [{ value: ALL_BRANCHES, label: allLabel }, ...options] : options;
}

function branchMatches(branch: string | null, filter: string): boolean {
  if (!filter) return true;
  return filter === ALL_BRANCHES ? branch === null : branch === filter;
}

/** Sucursales para elegir en un formulario: las visibles y activas (como el escritorio). */
export function formBranchOptions(branches: readonly BranchRecord[] | undefined): SelectOption[] {
  return (branches ?? []).filter((branch) => branch.isVisible && branch.isActive).map((branch) => ({ value: branch.code, label: `${branch.code} · ${branch.name}` }));
}

/** El mensaje del servidor sin la marca «✔ » o «✖ » del escritorio. */
export function plainMessage(text: string): string {
  return text.replace(/^[✔✖\s]+/u, '').trim();
}

// ---------------------------------------------------------------------------------------------------- API Keys

export const KEY_STATUSES = defineStatuses({
  activa: { label: 'Activa', tone: 'success' },
  vencida: { label: 'Vencida', tone: 'warning' },
  revocada: { label: 'Revocada', tone: 'neutral' },
});
export type KeyStatus = keyof typeof KEY_STATUSES;

export function keyStatusOf(key: ApiKeyRecord): KeyStatus {
  if (key.revokedAt) return 'revocada';
  return key.isUsable ? 'activa' : 'vencida';
}

/** Cómo se reconoce la llave sin mostrarla: «minv_ab12cd34_••••». */
export function keyPrefixText(prefix: string): string {
  return `minv_${prefix}_••••`;
}

export const KEY_FILTERS = { q: '', estado: '', alcance: '', sucursal: '' };
export type KeyFilters = typeof KEY_FILTERS;

export function filterKeys(keys: readonly ApiKeyRecord[], filters: KeyFilters, scopes: readonly NamedOption[]): ApiKeyRecord[] {
  return keys.filter(
    (key) =>
      (!filters.estado || keyStatusOf(key) === filters.estado) &&
      (!filters.alcance || key.scopes.includes(filters.alcance)) &&
      branchMatches(key.branch, filters.sucursal) &&
      matchesSearch(filters.q, [key.name, keyPrefixText(key.prefix), key.owner, key.branch, ...key.scopes.map((scope) => nameOf(scopes, scope))]),
  );
}

export function keyCsvColumns(scopes: readonly NamedOption[]): CsvColumn<ApiKeyRecord>[] {
  return [
    { header: 'Nombre', value: (key) => key.name },
    { header: 'Llave', value: (key) => keyPrefixText(key.prefix) },
    { header: 'Estado', value: (key) => statusOf(KEY_STATUSES, keyStatusOf(key)).label },
    { header: 'Alcances', value: (key) => key.scopes.map((scope) => nameOf(scopes, scope)).join(' · ') },
    { header: 'Sucursal', value: (key) => key.branch ?? 'Todas sus sucursales' },
    { header: 'Creada por', value: (key) => key.owner },
    { header: 'Creada', value: (key) => toDate(key.createdAt) },
    { header: 'Vence', value: (key) => toDate(key.expiresAt) },
    { header: 'Revocada', value: (key) => toDate(key.revokedAt) },
    { header: 'Último uso', value: (key) => toDate(key.lastUsedAt) },
  ];
}

/** Vencimiento de una llave nueva (como el escritorio; el servidor acepta de 1 a 730 días). */
export const KEY_EXPIRY_OPTIONS: readonly SelectOption[] = [
  { value: '30', label: '30 días' },
  { value: '90', label: '90 días' },
  { value: '365', label: '1 año' },
];
export const DEFAULT_KEY_EXPIRY = '90';
/** Alcances marcados al abrir el formulario (leer el catálogo y el stock). */
export const DEFAULT_KEY_SCOPES: readonly string[] = ['catalog:read', 'stock:read'];

export interface KeyForm {
  name: string;
  scopes: string[];
  /** '' = todas las sucursales de quien la crea. */
  branch: string;
  /** '' = sin vencimiento; si no, días. */
  expiry: string;
}

export function initialKeyForm(scopes: readonly NamedOption[]): KeyForm {
  const available = scopes.map((scope) => scope.code);
  return { name: 'Tienda en línea', scopes: DEFAULT_KEY_SCOPES.filter((scope) => available.includes(scope)), branch: '', expiry: DEFAULT_KEY_EXPIRY };
}

export function keyFormProblems(form: KeyForm): Partial<Record<'name' | 'scopes', string>> {
  const problems: Partial<Record<'name' | 'scopes', string>> = {};
  const name = form.name.trim();
  if (!name) problems.name = 'Indique para qué es la llave (por ejemplo «Tienda en línea»).';
  else if (name.length > 100) problems.name = 'El nombre tiene como máximo 100 caracteres.';
  if (form.scopes.length === 0) problems.scopes = 'Elija al menos un alcance.';
  return problems;
}

export function createKeyPayload(form: KeyForm, scopeOrder: readonly NamedOption[]): RpcRequestOf<'CreateApiKeyCommand'> {
  const order = scopeOrder.map((scope) => scope.code);
  return {
    name: form.name.trim(),
    scopes: [...form.scopes].sort((a, b) => order.indexOf(a) - order.indexOf(b)),
    branchCode: form.branch || null,
    expiresInDays: form.expiry ? Number(form.expiry) : null,
  };
}

/** Permisos efectivos de una llave nueva, en palabras. */
export function effectivePermissionsText(created: CreatedKey): string {
  return created.effectivePermissions.map(permissionName).join(' · ');
}

// ---------------------------------------------------------------------------------------------------- webhooks

export const WEBHOOK_STATUSES = defineStatuses({
  activo: { label: 'Activo', tone: 'success' },
  desactivado: { label: 'Desactivado', tone: 'neutral' },
});

export function webhookStatusOf(hook: WebhookRecord): keyof typeof WEBHOOK_STATUSES {
  return hook.isActive ? 'activo' : 'desactivado';
}

export const WEBHOOK_FILTERS = { q: '', estado: '', evento: '', sucursal: '' };
export type WebhookFilters = typeof WEBHOOK_FILTERS;

export function filterWebhooks(hooks: readonly WebhookRecord[], filters: WebhookFilters): WebhookRecord[] {
  return hooks.filter(
    (hook) =>
      (!filters.estado || webhookStatusOf(hook) === filters.estado) &&
      (!filters.evento || hook.events.includes(filters.evento)) &&
      branchMatches(hook.branch, filters.sucursal) &&
      matchesSearch(filters.q, [hook.url, hook.description, hook.branch, hook.lastError]),
  );
}

export function deliveriesText(hook: WebhookRecord): string {
  return `${formatNumber(hook.delivered)} correctas · ${formatNumber(hook.failed)} fallidas`;
}

export function webhookCsvColumns(events: readonly NamedOption[]): CsvColumn<WebhookRecord>[] {
  return [
    { header: 'Dirección', value: (hook) => hook.url },
    { header: 'Descripción', value: (hook) => hook.description },
    { header: 'Estado', value: (hook) => statusOf(WEBHOOK_STATUSES, webhookStatusOf(hook)).label },
    { header: 'Eventos', value: (hook) => hook.events.map((event) => nameOf(events, event)).join(' · ') },
    { header: 'Sucursal', value: (hook) => hook.branch ?? 'Todas sus sucursales' },
    { header: 'Entregas correctas', value: (hook) => hook.delivered },
    { header: 'Entregas fallidas', value: (hook) => hook.failed },
    { header: 'Último intento', value: (hook) => toDate(hook.lastAttemptAt) },
    { header: 'Último error', value: (hook) => hook.lastError },
    { header: 'Creado', value: (hook) => toDate(hook.createdAt) },
  ];
}

const LOOPBACK = new Set(['localhost', '127.0.0.1', '[::1]']);

/** Por qué no sirve la dirección de un webhook (las reglas del servidor: https, http solo en este equipo, sin usuario). */
export function webhookUrlProblem(url: string): string | null {
  const text = url.trim();
  if (!text) return 'Indique la dirección (URL) que recibirá los avisos.';
  if (text.length > 500) return 'La dirección tiene como máximo 500 caracteres.';
  let parsed: URL;
  try {
    parsed = new URL(text);
  } catch {
    return 'Escriba la dirección completa, empezando con https.';
  }
  const local = LOOPBACK.has(parsed.hostname) && parsed.protocol === 'http:';
  if (parsed.protocol !== 'https:' && !local) return 'El webhook debe usar https (http solo para pruebas en este mismo equipo).';
  if (parsed.username || parsed.password) return 'La dirección no debe incluir usuario ni contraseña.';
  return null;
}

export interface WebhookForm {
  url: string;
  description: string;
  events: string[];
  /** '' = todas las sucursales de quien lo registra. */
  branch: string;
}

/** Evento marcado al abrir el formulario (como el escritorio). */
export const DEFAULT_WEBHOOK_EVENT = 'sale.completed';

export function initialWebhookForm(events: readonly NamedOption[]): WebhookForm {
  return { url: '', description: '', events: events.some((event) => event.code === DEFAULT_WEBHOOK_EVENT) ? [DEFAULT_WEBHOOK_EVENT] : [], branch: '' };
}

export function webhookFormProblems(form: WebhookForm): Partial<Record<'url' | 'description' | 'events', string>> {
  const problems: Partial<Record<'url' | 'description' | 'events', string>> = {};
  const url = webhookUrlProblem(form.url);
  if (url) problems.url = url;
  if (form.description.trim().length > 200) problems.description = 'La descripción tiene como máximo 200 caracteres.';
  if (form.events.length === 0) problems.events = 'Elija al menos un evento.';
  return problems;
}

export function createWebhookPayload(form: WebhookForm, eventOrder: readonly NamedOption[]): RpcRequestOf<'CreateWebhookCommand'> {
  const order = eventOrder.map((event) => event.code);
  return {
    url: form.url.trim(),
    events: [...form.events].sort((a, b) => order.indexOf(a) - order.indexOf(b)),
    description: form.description.trim() || null,
    branchCode: form.branch || null,
  };
}

// ---------------------------------------------------------------------------------------------------- entregas

export const DELIVERY_RESULTS = defineStatuses({
  correcta: { label: 'Correcta', tone: 'success' },
  fallida: { label: 'Fallida', tone: 'danger' },
});

export function deliveryResultOf(delivery: DeliveryRecord): keyof typeof DELIVERY_RESULTS {
  return delivery.succeeded ? 'correcta' : 'fallida';
}

/** «Correcta (200)», «Fallida (500)» o «Fallida (sin respuesta)». */
export function deliveryResultText(delivery: DeliveryRecord): string {
  const label = statusOf(DELIVERY_RESULTS, deliveryResultOf(delivery)).label;
  if (delivery.statusCode !== null) return `${label} (${delivery.statusCode})`;
  return delivery.succeeded ? label : `${label} (sin respuesta)`;
}

/** Cuántas entregas se piden al servidor (acepta de 1 a 1000; los demás filtros se aplican sobre ellas). */
export const DELIVERY_TAKE_OPTIONS: readonly SelectOption[] = [
  { value: '100', label: 'Últimas 100' },
  { value: '200', label: 'Últimas 200' },
  { value: '500', label: 'Últimas 500' },
  { value: '1000', label: 'Últimas 1.000' },
];
export const DEFAULT_DELIVERY_TAKE = '200';

export function deliveryTakeOf(value: string): number {
  return Number(DELIVERY_TAKE_OPTIONS.some((option) => option.value === value) ? value : DEFAULT_DELIVERY_TAKE);
}

/** Filtros de las entregas: `webhook` y `registros` van al servidor; el resto se aplica en la página. */
export const DELIVERY_FILTERS = { q: '', resultado: '', evento: '', webhook: '', registros: DEFAULT_DELIVERY_TAKE, desde: '', hasta: '' };
export type DeliveryFilters = typeof DELIVERY_FILTERS;

export function deliveriesQueryOf(filters: DeliveryFilters): RpcRequestOf<'GetWebhookDeliveriesQuery'> {
  return { endpointId: filters.webhook || null, take: deliveryTakeOf(filters.registros) };
}

export function filterDeliveries(deliveries: readonly DeliveryRecord[], filters: DeliveryFilters, events: readonly NamedOption[]): DeliveryRecord[] {
  const range = { from: filters.desde || null, to: filters.hasta || null };
  return deliveries.filter(
    (delivery) =>
      (!filters.resultado || deliveryResultOf(delivery) === filters.resultado) &&
      (!filters.evento || delivery.eventType === filters.evento) &&
      inRange(delivery.attemptedAt, range) &&
      matchesSearch(filters.q, [delivery.url, delivery.error, delivery.eventType, nameOf(events, delivery.eventType), delivery.statusCode]),
  );
}

/** Una clave estable por fila (el servidor no manda identificador de la entrega). */
export function deliveryKeys(deliveries: readonly DeliveryRecord[]): Map<DeliveryRecord, string> {
  const seen = new Map<string, number>();
  const keys = new Map<DeliveryRecord, string>();
  for (const delivery of deliveries) {
    const base = `${delivery.attemptedAt}|${delivery.url}|${delivery.eventType}|${delivery.attempt}`;
    const count = seen.get(base) ?? 0;
    seen.set(base, count + 1);
    keys.set(delivery, count === 0 ? base : `${base}|${count}`);
  }
  return keys;
}

export function deliveryCsvColumns(events: readonly NamedOption[]): CsvColumn<DeliveryRecord>[] {
  return [
    { header: 'Fecha y hora', value: (delivery) => toDate(delivery.attemptedAt) },
    { header: 'Evento', value: (delivery) => nameOf(events, delivery.eventType) },
    { header: 'Código del evento', value: (delivery) => delivery.eventType },
    { header: 'Dirección', value: (delivery) => delivery.url },
    { header: 'Intento', value: (delivery) => delivery.attempt },
    { header: 'Resultado', value: (delivery) => deliveryResultText(delivery) },
    { header: 'Duración (ms)', value: (delivery) => delivery.durationMs },
    { header: 'Error', value: (delivery) => delivery.error },
  ];
}

// ---------------------------------------------------------------------------------------------------- correos de reservas

/** Estado de un correo de la cola (enumeración `OutgoingMailStatus` del servidor). */
export const MAIL_STATUSES = defineStatuses({
  Pending: { label: 'Pendiente', tone: 'warning' },
  Sent: { label: 'Enviado', tone: 'success' },
  Exhausted: { label: 'Agotado (sin enviar)', tone: 'danger' },
  Cancelled: { label: 'Cancelado', tone: 'neutral' },
});

export function isMailStatus(value: string): value is MailStatus {
  return Object.prototype.hasOwnProperty.call(MAIL_STATUSES, value);
}

export const RESERVATION_KINDS: Readonly<Record<string, string>> = { Build: 'Armado', Cart: 'Carrito' };

export function reservationKindText(kind: string): string {
  return RESERVATION_KINDS[kind] ?? kind;
}

/** Cuántos correos se piden al servidor (acepta de 1 a 500). */
export const MAIL_TAKE_OPTIONS: readonly SelectOption[] = [
  { value: '50', label: 'Últimos 50' },
  { value: '100', label: 'Últimos 100' },
  { value: '200', label: 'Últimos 200' },
  { value: '500', label: 'Últimos 500' },
];
export const DEFAULT_MAIL_TAKE = '200';

export function mailTakeOf(value: string): number {
  return Number(MAIL_TAKE_OPTIONS.some((option) => option.value === value) ? value : DEFAULT_MAIL_TAKE);
}

/** Filtros de la cola: `estado`, `reserva` y `registros` van al servidor; el resto se aplica en la página. */
export const MAIL_FILTERS = { q: '', estado: '', reserva: '', sucursal: '', tipo: '', registros: DEFAULT_MAIL_TAKE, desde: '', hasta: '' };
export type MailFilters = typeof MAIL_FILTERS;

export function mailsQueryOf(filters: MailFilters): RpcRequestOf<'GetOutgoingMailsQuery'> {
  const number = filters.reserva.trim().toUpperCase();
  return { status: isMailStatus(filters.estado) ? filters.estado : null, number: number || null, take: mailTakeOf(filters.registros) };
}

export function filterMails(mails: readonly MailRecord[], filters: MailFilters): MailRecord[] {
  const range = { from: filters.desde || null, to: filters.hasta || null };
  return mails.filter(
    (mail) =>
      (!filters.sucursal || mail.branchCode === filters.sucursal) &&
      (!filters.tipo || mail.reservationKind === filters.tipo) &&
      inRange(mail.requestedAt, range) &&
      matchesSearch(filters.q, [mail.reservation, mail.recipient, mail.lastError, mail.kindText, mail.statusText]),
  );
}

export function mailStatusLabel(mail: MailRecord): string {
  return isMailStatus(mail.status) ? MAIL_STATUSES[mail.status].label : mail.statusText;
}

/** Qué sigue con un correo: el próximo intento, cuándo terminó o el último error. */
export function mailNextText(mail: MailRecord): string {
  if (mail.status === 'Pending') return mail.nextAttemptAt ? `Próximo intento ${formatDateTime(mail.nextAttemptAt)}` : 'Por enviar';
  if (mail.completedAt) return `${mailStatusLabel(mail)} el ${formatDateTime(mail.completedAt)}`;
  return '';
}

export const MAIL_CSV_COLUMNS: readonly CsvColumn<MailRecord>[] = [
  { header: 'Solicitado', value: (mail) => toDate(mail.requestedAt) },
  { header: 'Reserva', value: (mail) => mail.reservation },
  { header: 'Tipo de reserva', value: (mail) => reservationKindText(mail.reservationKind) },
  { header: 'Sucursal', value: (mail) => mail.branchCode },
  { header: 'Correo', value: (mail) => mail.kindText },
  { header: 'Destinatario', value: (mail) => mail.recipient },
  { header: 'Estado', value: (mail) => mailStatusLabel(mail) },
  { header: 'Intentos', value: (mail) => mail.attempts },
  { header: 'Intentos máximos', value: (mail) => mail.maxAttempts },
  { header: 'Último intento', value: (mail) => toDate(mail.lastAttemptAt) },
  { header: 'Próximo intento', value: (mail) => toDate(mail.nextAttemptAt) },
  { header: 'Terminado', value: (mail) => toDate(mail.completedAt) },
  { header: 'Último error', value: (mail) => mail.lastError },
];

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

export interface ResendForm {
  number: string;
  email: string;
}

/** Las reglas de `ResendReservationMailCommand` (el servidor vuelve a validar y decide si la reserva sigue vigente). */
export function resendProblems(form: ResendForm): Partial<Record<'number' | 'email', string>> {
  const problems: Partial<Record<'number' | 'email', string>> = {};
  const number = form.number.trim();
  if (!number) problems.number = 'Indique el número de la reserva.';
  else if (number.length > 40) problems.number = 'El número tiene como máximo 40 caracteres.';
  const email = form.email.trim();
  if (email && (email.length > 254 || !EMAIL.test(email))) problems.email = 'Escriba un correo válido o déjelo vacío.';
  return problems;
}

export function resendPayload(form: ResendForm): RpcRequestOf<'ResendReservationMailCommand'> {
  return { number: form.number.trim().toUpperCase(), email: form.email.trim() || null };
}

// ---------------------------------------------------------------------------------------------------- resúmenes (estadísticas)

export interface IntegrationsSummaryData {
  keysActive: number;
  keysUsed: number;
  hooksActive: number;
  subscriptions: number;
  delivered: number;
  failed: number;
}

/** Los indicadores del escritorio: llaves activas y en uso, webhooks activos, suscripciones y entregas. */
export function integrationsSummary(keys: readonly ApiKeyRecord[], hooks: readonly WebhookRecord[]): IntegrationsSummaryData {
  const active = hooks.filter((hook) => hook.isActive);
  return {
    keysActive: keys.filter((key) => key.isUsable).length,
    keysUsed: keys.filter((key) => key.lastUsedAt !== null).length,
    hooksActive: active.length,
    subscriptions: active.reduce((total, hook) => total + hook.events.length, 0),
    delivered: hooks.reduce((total, hook) => total + hook.delivered, 0),
    failed: hooks.reduce((total, hook) => total + hook.failed, 0),
  };
}

export interface MailsSummaryData {
  pending: number;
  exhausted: number;
  sentToday: number;
  cancelled: number;
}

export function mailsSummary(mails: readonly MailRecord[], now: Date): MailsSummaryData {
  const day = laPazToday(now);
  return {
    pending: mails.filter((mail) => mail.status === 'Pending').length,
    exhausted: mails.filter((mail) => mail.status === 'Exhausted').length,
    sentToday: mails.filter((mail) => mail.status === 'Sent' && toIsoDate(mail.completedAt ?? mail.lastAttemptAt) === day).length,
    cancelled: mails.filter((mail) => mail.status === 'Cancelled').length,
  };
}
