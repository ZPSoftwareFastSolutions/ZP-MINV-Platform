// Módulo «Configuración» · funciones puras (sin React): parámetros de la empresa (margen de alerta y días sin rotación),
// facturación SIAT (estado, qué falta para facturar, datos del Padrón, conexión por ambiente, sucursales del Padrón,
// puntos de venta y actividades) y el correo de la empresa. Validación con las mismas reglas del servidor y pedidos
// EXACTOS de cada comando. Los textos siguen al escritorio (UsersViewModel › Empresa, BillingSettingsViewModel,
// SiatStatusViewModel).
//
// Los tipos del servidor SALEN DEL CONTRATO (regla P-07). Los secretos (token del SIN y contraseña del correo) solo se
// ESCRIBEN: el servidor nunca los devuelve y esta pantalla nunca los muestra (reglas B-11 y F-12).

import type { RpcRequestOf, RpcResponseOf } from '@/4-presentation/app/contract';
import { defineStatuses, statusOf, type SelectOption } from '@/4-presentation/panel/kit';
import { formatDate, formatDateTime, formatNumber, matchesSearch, toDate, type CsvColumn } from '@/4-presentation/panel/lib';

/** Datos y parámetros de la empresa (`GetCompanySettingsQuery`). */
export type CompanyData = RpcResponseOf<'GetCompanySettingsQuery'>;
/** Configuración de la facturación (`GetSiatSettingsQuery`). */
export type SiatSettingsData = RpcResponseOf<'GetSiatSettingsQuery'>;
export type SiatProfileData = SiatSettingsData['profiles'][number];
export type SiatBranchData = SiatSettingsData['branches'][number];
export type MailData = NonNullable<SiatSettingsData['mail']>;
export type EndpointsData = SiatProfileData['endpoints'];
/** Estado de la facturación con los puntos de venta (`GetSiatStatusQuery`). */
export type SiatStatusData = RpcResponseOf<'GetSiatStatusQuery'>;
export type PointRecord = SiatStatusData['points'][number];
/** Actividad económica del Padrón (`GetSiatActivitiesQuery`). */
export type EconomicActivity = RpcResponseOf<'GetSiatActivitiesQuery'>[number];
/** Resultado de «Preparar SIAT». */
export type MaintenanceData = RpcResponseOf<'PrepareSiatCommand'>;
/** Resultado de «Sincronizar catálogos». */
export type SyncData = RpcResponseOf<'SyncSiatCatalogsCommand'>;
/** Una caja de la empresa (`GetPosStateQuery`). */
export type RegisterOption = RpcResponseOf<'GetPosStateQuery'>['registers'][number];

/** El mensaje del servidor sin la marca «✔ » o «✖ » del escritorio. */
export function plainMessage(text: string): string {
  return text.replace(/^[✔✖\s]+/u, '').trim();
}

// ---------------------------------------------------------------------------------------------------- empresa

/** Opciones del escritorio (UsersViewModel): el valor actual se agrega si no está entre ellas. */
export const MARGIN_CHOICES: readonly number[] = [0.1, 0.15, 0.2, 0.25, 0.3, 0.4, 0.5];
export const DAYS_CHOICES: readonly number[] = [15, 30, 45, 60, 90, 120, 180];

/** 0,2 → «20 %». */
export function marginText(value: number): string {
  return `${formatNumber(value * 100, { maxDecimals: 1 })} %`;
}

export function marginOptions(current: number | null): SelectOption[] {
  const values = current !== null && !MARGIN_CHOICES.includes(current) ? [...MARGIN_CHOICES, current].sort((a, b) => a - b) : MARGIN_CHOICES;
  return values.map((value) => ({ value: String(value), label: `${marginText(value)} sobre el mínimo` }));
}

export function daysOptions(current: number | null): SelectOption[] {
  const values = current !== null && !DAYS_CHOICES.includes(current) ? [...DAYS_CHOICES, current].sort((a, b) => a - b) : DAYS_CHOICES;
  return values.map((value) => ({ value: String(value), label: `${formatNumber(value)} días` }));
}

export function companyParametersPayload(margin: string, days: string): RpcRequestOf<'UpdateCompanySettingsCommand'> {
  return { alertMargin: Number(margin), daysWithoutRotation: Number(days) };
}

// ---------------------------------------------------------------------------------------------------- facturación: estado

export const PRODUCTION = 1;
export const TESTING = 2;

export function environmentName(environment: number): string {
  return environment === PRODUCTION ? 'Producción (1)' : 'Pruebas y piloto (2)';
}

export const BILLING_STATES = defineStatuses({
  'sin-configurar': { label: 'Sin configurar', tone: 'neutral' },
  activa: { label: 'Facturación activa', tone: 'success' },
  desactivada: { label: 'Guardada, desactivada', tone: 'warning' },
});

export function billingStateOf(view: SiatSettingsData): keyof typeof BILLING_STATES {
  if (!view.configured) return 'sin-configurar';
  return view.isEnabled ? 'activa' : 'desactivada';
}

/** En qué está la facturación (como el escritorio). */
export function billingStateText(view: SiatSettingsData): string {
  if (!view.configured) return 'Sin configurar: complete los datos del Padrón y guárdelos.';
  if (view.isEnabled) return `Activa en el ambiente ${environmentName(view.environment).toLowerCase()}: las ventas emiten factura del SIN.`;
  return 'Guardada pero desactivada: las ventas salen sin documento fiscal.';
}

export function profileOf(view: SiatSettingsData, environment: number): SiatProfileData | undefined {
  return view.profiles.find((profile) => profile.environment === environment);
}

export interface ReadinessCheck {
  key: string;
  label: string;
  ok: boolean;
}

/** Qué hace falta para activar la facturación (lo mismo que exige el servidor al activarla). */
export function readinessChecks(view: SiatSettingsData, environment: number = view.environment): ReadinessCheck[] {
  return [
    { key: 'padron', label: 'Datos del Padrón: NIT, razón social y código de sistema', ok: view.configured },
    { key: 'token', label: `Conexión del ambiente ${environmentName(environment).toLowerCase()} con su token delegado`, ok: profileOf(view, environment)?.hasToken === true },
    { key: 'matriz', label: 'Casa matriz con el código 0 del Padrón', ok: view.branches.some((branch) => branch.siatCode === 0) },
    { key: 'modulo', label: 'Módulo «Facturación SIAT» en la licencia de la empresa', ok: view.moduleActive },
  ];
}

export function clockText(view: SiatSettingsData): string {
  if (!view.clockSyncedAt) return 'La hora todavía no se sincronizó con el SIN: use «Preparar SIAT».';
  const seconds = Math.abs(view.clockOffsetMs) / 1000;
  const offset = seconds < 1 ? 'menos de 1 segundo' : `${formatNumber(seconds, { maxDecimals: 1 })} s`;
  return `Hora sincronizada con el SIN el ${formatDateTime(view.clockSyncedAt)} (diferencia de ${offset}); las facturas llevan la hora del SIN.`;
}

/** El token nunca vuelve del servidor: solo si hay uno guardado, hasta cuándo vale y cuándo se cargó. */
export function tokenStatusText(profile: SiatProfileData | undefined): string {
  if (!profile?.hasToken) return 'No hay token guardado para este ambiente: genérelo en el Portal SIAT (Token Delegado) y cárguelo aquí.';
  const until = profile.tokenValidUntil ? `, vigente hasta el ${formatDate(profile.tokenValidUntil)}` : '';
  const loaded = profile.tokenUpdatedAt ? ` · cargado el ${formatDateTime(profile.tokenUpdatedAt)}` : '';
  return `Hay un token guardado (cifrado)${until}${loaded}. Escriba uno nuevo solo para reemplazarlo.`;
}

export function maintenanceText(result: MaintenanceData): string {
  return [
    `CUIS pedidos: ${formatNumber(result.cuisRequested)}`,
    `CUFD pedidos: ${formatNumber(result.cufdRequested)}`,
    `documentos enviados: ${formatNumber(result.documentsSent)}`,
    `paquetes validados: ${formatNumber(result.packagesValidated)}`,
    `puntos recuperados: ${formatNumber(result.recovered)}`,
  ].join(' · ');
}

export function syncText(result: SyncData): string {
  return `${formatNumber(result.catalogs)} catálogos · ${formatNumber(result.items)} filas`;
}

// ---------------------------------------------------------------------------------------------------- facturación: datos del Padrón

export interface SiatSettingsForm {
  nit: string;
  businessName: string;
  systemCode: string;
  environment: string;
  onlineLegend: string;
  offlineLegend: string;
  enabled: boolean;
}

export function siatSettingsFormOf(view: SiatSettingsData, companyName: string): SiatSettingsForm {
  return {
    nit: view.nit === null ? '' : String(view.nit),
    businessName: view.businessName ?? companyName,
    systemCode: view.systemCode ?? '',
    environment: String(view.environment === PRODUCTION ? PRODUCTION : TESTING),
    onlineLegend: view.onlineLegend,
    offlineLegend: view.offlineLegend,
    enabled: view.isEnabled,
  };
}

export type SiatSettingsField = 'nit' | 'businessName' | 'systemCode' | 'onlineLegend' | 'offlineLegend';

export function siatSettingsProblems(form: SiatSettingsForm): Partial<Record<SiatSettingsField, string>> {
  const problems: Partial<Record<SiatSettingsField, string>> = {};
  const nit = form.nit.trim();
  if (!/^\d{1,13}$/.test(nit) || Number(nit) === 0) problems.nit = 'El NIT lleva solo números (de 1 a 13 cifras), tal como figura en el Padrón.';
  const name = form.businessName.trim();
  if (!name) problems.businessName = 'Indique la razón social tal como figura en el Padrón.';
  else if (name.length > 200) problems.businessName = 'La razón social tiene como máximo 200 caracteres.';
  const system = form.systemCode.trim();
  if (!system) problems.systemCode = 'Indique el código de sistema que asignó el SIN.';
  else if (system.length > 50) problems.systemCode = 'El código de sistema tiene como máximo 50 caracteres.';
  if (form.onlineLegend.trim().length > 250) problems.onlineLegend = 'La leyenda tiene como máximo 250 caracteres.';
  if (form.offlineLegend.trim().length > 250) problems.offlineLegend = 'La leyenda tiene como máximo 250 caracteres.';
  return problems;
}

/** Leyendas vacías = las oficiales por defecto (las pone el servidor). */
export function siatSettingsPayload(form: SiatSettingsForm): RpcRequestOf<'SaveSiatSettingsCommand'> {
  return {
    nit: Number(form.nit.trim()),
    businessName: form.businessName.trim(),
    systemCode: form.systemCode.trim(),
    environment: Number(form.environment),
    onlineLegend: form.onlineLegend.trim() || null,
    offlineLegend: form.offlineLegend.trim() || null,
    enabled: form.enabled,
  };
}

/** ¿Este guardado pasa la empresa a PRODUCCIÓN? (pide una confirmación aparte). */
export function goesToProduction(view: SiatSettingsData, form: SiatSettingsForm): boolean {
  return Number(form.environment) === PRODUCTION && view.environment !== PRODUCTION;
}

// ---------------------------------------------------------------------------------------------------- facturación: conexión

/** Los seis servicios del SIN con el camino conocido de cada uno (SiatEndpointSet.ForBaseUrl del servidor). */
export const ENDPOINT_FIELDS = [
  { key: 'codes', label: 'Servicio de Códigos (CUIS, CUFD y NIT)', path: '/v2/FacturacionCodigos' },
  { key: 'sync', label: 'Servicio de Sincronización (catálogos y hora)', path: '/v2/FacturacionSincronizacion' },
  { key: 'operations', label: 'Servicio de Operaciones (puntos de venta y eventos)', path: '/v2/FacturacionOperaciones' },
  { key: 'purchaseSale', label: 'Servicio Factura Compra Venta', path: '/v2/ServicioFacturacionCompraVenta' },
  { key: 'computerized', label: 'Servicio de Facturación Computarizada', path: '/v2/ServicioFacturacionComputarizada' },
  { key: 'adjustment', label: 'Servicio de Documentos de Ajuste (notas crédito-débito)', path: '/v2/ServicioFacturacionDocumentoAjuste' },
] as const;
export type EndpointKey = (typeof ENDPOINT_FIELDS)[number]['key'];

export interface ProfileForm {
  endpoints: Record<EndpointKey, string>;
  namespace: string;
  qrBaseUrl: string;
  timeout: number | null;
  /** Token nuevo ('' = conservar el guardado). Solo se escribe: nunca se muestra el guardado. */
  token: string;
  /** Vigencia del token («2026-12-31») o ''. */
  tokenValidUntil: string;
}

/** El namespace de otro ambiente sirve de base (es el mismo en pruebas y producción). */
export function knownNamespace(view: SiatSettingsData): string {
  return view.profiles.find((profile) => profile.endpoints.namespace)?.endpoints.namespace ?? '';
}

export function profileFormOf(profile: SiatProfileData | undefined, fallbackNamespace: string): ProfileForm {
  const endpoints = profile?.endpoints;
  return {
    endpoints: {
      codes: endpoints?.codes ?? '',
      sync: endpoints?.sync ?? '',
      operations: endpoints?.operations ?? '',
      purchaseSale: endpoints?.purchaseSale ?? '',
      computerized: endpoints?.computerized ?? '',
      adjustment: endpoints?.adjustment ?? '',
    },
    namespace: endpoints?.namespace || fallbackNamespace,
    qrBaseUrl: profile?.qrBaseUrl ?? '',
    timeout: profile?.timeoutSeconds ?? 15,
    token: '',
    tokenValidUntil: profile?.tokenValidUntil ?? '',
  };
}

const LOOPBACK = new Set(['localhost', '127.0.0.1', '[::1]']);

function parseUrl(value: string): URL | null {
  try {
    return new URL(value);
  } catch {
    return null;
  }
}

/** Una dirección de servicio del SIN: https (http solo en este mismo equipo), como exige el servidor. */
export function serviceUrlProblem(value: string): string | null {
  const text = value.trim();
  if (!text) return 'Escriba la dirección.';
  if (text.length > 400) return 'La dirección tiene como máximo 400 caracteres.';
  const url = parseUrl(text);
  if (!url) return 'Escriba la dirección completa, empezando con https.';
  if (url.protocol !== 'https:' && !LOOPBACK.has(url.hostname)) return 'Debe ser https (http solo en este mismo equipo).';
  return null;
}

/** Las seis direcciones a partir de la dirección base del SIN (o del simulador); null si la base no sirve. */
export function endpointsFromBase(base: string): Record<EndpointKey, string> | null {
  if (serviceUrlProblem(base) !== null) return null;
  const root = base.trim().replace(/\/+$/, '');
  return Object.fromEntries(ENDPOINT_FIELDS.map((field) => [field.key, `${root}${field.path}`])) as Record<EndpointKey, string>;
}

export type ProfileField = EndpointKey | 'namespace' | 'qrBaseUrl' | 'timeout' | 'token';

export function profileProblems(form: ProfileForm): Partial<Record<ProfileField, string>> {
  const problems: Partial<Record<ProfileField, string>> = {};
  for (const field of ENDPOINT_FIELDS) {
    const problem = serviceUrlProblem(form.endpoints[field.key]);
    if (problem) problems[field.key] = problem;
  }
  const namespace = form.namespace.trim();
  if (!namespace) problems.namespace = 'Indique el namespace de los servicios (lo dice el WSDL del SIN).';
  else if (namespace.length > 200) problems.namespace = 'El namespace tiene como máximo 200 caracteres.';
  const qr = serviceUrlProblem(form.qrBaseUrl);
  if (qr) problems.qrBaseUrl = qr === 'Escriba la dirección.' ? 'Indique la dirección de la consulta por QR.' : qr;
  if (form.timeout === null || form.timeout < 3 || form.timeout > 120) problems.timeout = 'El tiempo de espera va de 3 a 120 segundos.';
  const token = form.token.trim();
  if (token && token.length < 10) problems.token = 'El token delegado tiene al menos 10 caracteres.';
  return problems;
}

export function profilePayload(environment: number, form: ProfileForm): RpcRequestOf<'SaveSiatProfileCommand'> {
  const token = form.token.trim();
  return {
    environment,
    endpoints: {
      codes: form.endpoints.codes.trim(),
      sync: form.endpoints.sync.trim(),
      operations: form.endpoints.operations.trim(),
      purchaseSale: form.endpoints.purchaseSale.trim(),
      computerized: form.endpoints.computerized.trim(),
      adjustment: form.endpoints.adjustment.trim(),
      namespace: form.namespace.trim(),
    },
    qrBaseUrl: form.qrBaseUrl.trim(),
    timeoutSeconds: form.timeout ?? 15,
    newToken: token || null,
    tokenValidUntil: form.tokenValidUntil || null,
  };
}

// ---------------------------------------------------------------------------------------------------- facturación: sucursales del Padrón

export const PADRON_STATES = defineStatuses({
  matriz: { label: 'Casa matriz (código 0)', tone: 'success' },
  sucursal: { label: 'Sucursal del Padrón', tone: 'success' },
  'sin-codigo': { label: 'Sin código del Padrón', tone: 'warning' },
});

export function padronStateOf(branch: SiatBranchData): keyof typeof PADRON_STATES {
  if (branch.siatCode === null) return 'sin-codigo';
  return branch.siatCode === 0 ? 'matriz' : 'sucursal';
}

/** Filtros de la lista (en la dirección, con el prefijo `suc_`). */
export const PADRON_FILTERS = { q: '', estado: '' };
export type PadronFilters = typeof PADRON_FILTERS;

export function filterPadron(branches: readonly SiatBranchData[], filters: PadronFilters): SiatBranchData[] {
  return branches.filter(
    (branch) => (!filters.estado || padronStateOf(branch) === filters.estado) && matchesSearch(filters.q, [branch.branchCode, branch.branchName, branch.municipality, branch.phone]),
  );
}

export const PADRON_CSV_COLUMNS: readonly CsvColumn<SiatBranchData>[] = [
  { header: 'Sucursal', value: (branch) => branch.branchCode },
  { header: 'Nombre', value: (branch) => branch.branchName },
  { header: 'Código del Padrón', value: (branch) => branch.siatCode },
  { header: 'Municipio', value: (branch) => branch.municipality },
  { header: 'Teléfono', value: (branch) => branch.phone },
  { header: 'Estado', value: (branch) => PADRON_STATES[padronStateOf(branch)].label },
];

export interface PadronForm {
  siatCode: number | null;
  municipality: string;
  phone: string;
}

export function padronFormOf(branch: SiatBranchData): PadronForm {
  return { siatCode: branch.siatCode, municipality: branch.municipality ?? '', phone: branch.phone ?? '' };
}

export function padronProblems(form: PadronForm): Partial<Record<keyof PadronForm, string>> {
  const problems: Partial<Record<keyof PadronForm, string>> = {};
  if (form.siatCode === null) problems.siatCode = 'Escriba el código de la sucursal en el Padrón (0 = casa matriz).';
  else if (form.siatCode < 0 || form.siatCode > 9999) problems.siatCode = 'El código del Padrón va de 0 (casa matriz) a 9999.';
  const municipality = form.municipality.trim();
  if (!municipality) problems.municipality = 'Indique el municipio que va en la factura.';
  else if (municipality.length > 25) problems.municipality = 'El municipio tiene como máximo 25 caracteres.';
  if (form.phone.trim().length > 25) problems.phone = 'El teléfono tiene como máximo 25 caracteres.';
  return problems;
}

export function padronPayload(branchCode: string, form: PadronForm): RpcRequestOf<'SaveSiatBranchCommand'> {
  return { branchCode, siatCode: form.siatCode ?? 0, municipality: form.municipality.trim(), phone: form.phone.trim() || null };
}

// ---------------------------------------------------------------------------------------------------- facturación: puntos de venta

/** Modo de un punto de venta (enumeración `SiatConnectionMode`) o «cerrado en el SIN». */
export const POINT_STATES = defineStatuses({
  Online: { label: 'En línea', tone: 'success' },
  Offline: { label: 'Fuera de línea', tone: 'warning' },
  Recovering: { label: 'Recuperando', tone: 'info' },
  ManualContingency: { label: 'Contingencia manual', tone: 'danger' },
  Cerrado: { label: 'Cerrado en el SIN', tone: 'neutral' },
});

export function pointStateOf(point: PointRecord): string {
  return point.isClosed ? 'Cerrado' : point.mode;
}

export function pointStateLabel(point: PointRecord): string {
  return statusOf(POINT_STATES, pointStateOf(point)).label;
}

/** «Punto 1 · Caja 1» o «Sin punto de venta (0)». */
export function pointName(point: Pick<PointRecord, 'code' | 'name'>): string {
  return point.code === 0 ? 'Sin punto de venta (0)' : `Punto ${point.code} · ${point.name}`;
}

/** Filtros de la lista (en la dirección, con el prefijo `pv_`). Por defecto, solo los abiertos. */
export const POINT_FILTERS = { q: '', sucursal: '', modo: '', estado: 'abiertos' };
export type PointFilters = typeof POINT_FILTERS;

export const POINT_OPEN_OPTIONS: readonly SelectOption[] = [
  { value: 'abiertos', label: 'Abiertos' },
  { value: 'cerrados', label: 'Cerrados en el SIN' },
];

export const POINT_MODE_OPTIONS: readonly SelectOption[] = [
  { value: 'Online', label: 'En línea' },
  { value: 'Offline', label: 'Fuera de línea' },
  { value: 'Recovering', label: 'Recuperando' },
  { value: 'ManualContingency', label: 'Contingencia manual' },
];

export function filterPoints(points: readonly PointRecord[], filters: PointFilters): PointRecord[] {
  return points.filter(
    (point) =>
      (!filters.estado || (filters.estado === 'cerrados') === point.isClosed) &&
      (!filters.sucursal || point.branchCode === filters.sucursal) &&
      (!filters.modo || (!point.isClosed && point.mode === filters.modo)) &&
      matchesSearch(filters.q, [pointName(point), point.branchCode, point.branchName, point.registerCode, point.lastError]),
  );
}

/** «Hasta 29/09/2026 18:00», «Vencido» o «Sin código». */
export function validityText(until: string | null, now: Date): string {
  const date = toDate(until);
  if (!date) return 'Sin código';
  return date.getTime() <= now.getTime() ? `Vencido (${formatDateTime(date)})` : `Hasta ${formatDateTime(date)}`;
}

export const POINT_CSV_COLUMNS: readonly CsvColumn<PointRecord>[] = [
  { header: 'Sucursal', value: (point) => point.branchCode },
  { header: 'Código del Padrón', value: (point) => point.siatBranchCode },
  { header: 'Punto de venta', value: (point) => point.code },
  { header: 'Nombre', value: (point) => point.name },
  { header: 'Caja', value: (point) => point.registerCode },
  { header: 'Estado', value: (point) => pointStateLabel(point) },
  { header: 'CUIS vigente hasta', value: (point) => toDate(point.cuisValidUntil) },
  { header: 'CUFD vigente hasta', value: (point) => toDate(point.cufdValidUntil) },
  { header: 'Último contacto', value: (point) => toDate(point.lastContactAt) },
  { header: 'Documentos pendientes', value: (point) => point.pendingDocuments },
  { header: 'Fuera de línea', value: (point) => point.offlineDocuments },
  { header: 'Último error', value: (point) => point.lastError },
];

export interface RegisterPointForm {
  branchCode: string;
  name: string;
  description: string;
  /** '' = sin caja (se vincula después). */
  registerCode: string;
  typeCode: number | null;
}

/** Tipo de punto de venta que se propone: 5 = cajeros (el del escritorio). */
export const DEFAULT_POINT_TYPE = 5;

/** Sucursales donde se puede registrar un punto: las que tienen su código del Padrón. */
export function mappedBranches(view: SiatSettingsData): SiatBranchData[] {
  return view.branches.filter((branch) => branch.siatCode !== null);
}

export function registerPointFormOf(branches: readonly SiatBranchData[], activeBranchId: string | null): RegisterPointForm {
  const branch = branches.find((item) => item.branchId === activeBranchId) ?? branches[0];
  return { branchCode: branch?.branchCode ?? '', name: 'Caja', description: '', registerCode: '', typeCode: DEFAULT_POINT_TYPE };
}

export function registerPointProblems(form: RegisterPointForm): Partial<Record<keyof RegisterPointForm, string>> {
  const problems: Partial<Record<keyof RegisterPointForm, string>> = {};
  if (!form.branchCode) problems.branchCode = 'Elija la sucursal (debe tener su código del Padrón).';
  const name = form.name.trim();
  if (!name) problems.name = 'El nombre es obligatorio (el SIN lo rechaza vacío).';
  else if (name.length > 100) problems.name = 'El nombre tiene como máximo 100 caracteres.';
  if (form.description.trim().length > 200) problems.description = 'La descripción tiene como máximo 200 caracteres.';
  if (form.typeCode === null || form.typeCode < 1 || form.typeCode > 99) problems.typeCode = 'El tipo de punto de venta va de 1 a 99 (5 = cajeros).';
  return problems;
}

export function registerPointPayload(form: RegisterPointForm): RpcRequestOf<'RegisterSiatPointOfSaleCommand'> {
  return {
    branchCode: form.branchCode,
    name: form.name.trim(),
    description: form.description.trim() || null,
    registerCode: form.registerCode.trim().toUpperCase() || null,
    typeCode: form.typeCode ?? DEFAULT_POINT_TYPE,
  };
}

export function linkPayload(pointOfSaleId: string, registerCode: string): RpcRequestOf<'LinkPointOfSaleRegisterCommand'> {
  return { pointOfSaleId, registerCode: registerCode.trim().toUpperCase() || null };
}

export function registerOptions(registers: readonly RegisterOption[]): SelectOption[] {
  return registers.map((register) => ({ value: register.code, label: `${register.code} · ${register.name}` }));
}

// ---------------------------------------------------------------------------------------------------- facturación: actividades

/** Filtros de la lista (en la dirección, con el prefijo `act_`). */
export const ACTIVITY_FILTERS = { q: '', vigente: '' };
export type ActivityFilters = typeof ACTIVITY_FILTERS;

export const CURRENT_OPTIONS: readonly SelectOption[] = [
  { value: 'si', label: 'Vigentes' },
  { value: 'no', label: 'Retiradas por el SIN' },
];

export function filterActivities(activities: readonly EconomicActivity[], filters: ActivityFilters): EconomicActivity[] {
  return activities.filter(
    (activity) =>
      (!filters.vigente || activity.isCurrent === (filters.vigente === 'si')) &&
      matchesSearch(filters.q, [activity.code, activity.description, activity.activityType, ...activity.sectors.map(String)]),
  );
}

export function sectorsText(activity: EconomicActivity): string {
  return activity.sectors.length > 0 ? activity.sectors.join(', ') : '—';
}

export const ACTIVITY_CSV_COLUMNS: readonly CsvColumn<EconomicActivity>[] = [
  { header: 'Código', value: (activity) => activity.code },
  { header: 'Descripción', value: (activity) => activity.description },
  { header: 'Tipo', value: (activity) => activity.activityType },
  { header: 'Documentos sector', value: (activity) => activity.sectors.join(', ') },
  { header: 'Vigente', value: (activity) => activity.isCurrent },
];

// ---------------------------------------------------------------------------------------------------- correo de la empresa

export interface MailForm {
  host: string;
  port: number | null;
  useSsl: boolean;
  userName: string;
  /** Cambiar (o cargar) la contraseña: si no, se conserva la guardada. */
  changePassword: boolean;
  /** Solo se escribe: la guardada nunca se muestra. */
  password: string;
  fromAddress: string;
  fromName: string;
  enabled: boolean;
}

export function mailFormOf(mail: MailData | null, companyName: string): MailForm {
  if (!mail) return { host: '', port: 587, useSsl: true, userName: '', changePassword: true, password: '', fromAddress: '', fromName: companyName, enabled: true };
  return {
    host: mail.host,
    port: mail.port,
    useSsl: mail.useSsl,
    userName: mail.userName ?? '',
    changePassword: !mail.hasPassword,
    password: '',
    fromAddress: mail.fromAddress,
    fromName: mail.fromName,
    enabled: mail.isEnabled,
  };
}

/** Gmail: smtp.gmail.com, puerto 587 con STARTTLS; el usuario es la cuenta de Gmail. */
export const GMAIL = { host: 'smtp.gmail.com', port: 587, useSsl: true } as const;

export function applyGmail(form: MailForm): MailForm {
  return { ...form, host: GMAIL.host, port: GMAIL.port, useSsl: GMAIL.useSsl, userName: form.userName || form.fromAddress };
}

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

export type MailField = 'host' | 'port' | 'userName' | 'password' | 'fromAddress' | 'fromName';

export function mailProblems(form: MailForm, hasSavedPassword: boolean): Partial<Record<MailField, string>> {
  const problems: Partial<Record<MailField, string>> = {};
  const host = form.host.trim();
  if (!host) problems.host = 'Indique el servidor SMTP (por ejemplo smtp.gmail.com).';
  else if (host.length > 200) problems.host = 'El servidor tiene como máximo 200 caracteres.';
  if (form.port === null || form.port < 1 || form.port > 65535) problems.port = 'El puerto va de 1 a 65535 (587 con STARTTLS, 465 con SSL).';
  if (form.userName.trim().length > 200) problems.userName = 'El usuario tiene como máximo 200 caracteres.';
  if (hasSavedPassword && form.changePassword && !form.password) problems.password = 'Escriba la contraseña nueva o desmarque «Cambiar la contraseña».';
  const from = form.fromAddress.trim();
  if (!EMAIL.test(from) || from.length > 254) problems.fromAddress = 'Indique el correo remitente, por ejemplo facturas@suempresa.com.';
  const name = form.fromName.trim();
  if (!name) problems.fromName = 'Indique el nombre del remitente.';
  else if (name.length > 100) problems.fromName = 'El nombre del remitente tiene como máximo 100 caracteres.';
  return problems;
}

/** Sin «Cambiar la contraseña» viaja `newPassword: null` y el servidor conserva la guardada. */
export function mailPayload(form: MailForm): RpcRequestOf<'SaveMailSettingsCommand'> {
  return {
    host: form.host.trim(),
    port: form.port ?? GMAIL.port,
    useSsl: form.useSsl,
    userName: form.userName.trim() || null,
    newPassword: form.changePassword && form.password ? form.password : null,
    fromAddress: form.fromAddress.trim(),
    fromName: form.fromName.trim(),
    enabled: form.enabled,
  };
}
