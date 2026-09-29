// Módulo «Actividad» · funciones puras (sin React): cómo se presenta cada fila de la auditoría, los filtros de la lista,
// las columnas del CSV, el resumen de hoy (estadística del tablero) y la contraseña temporal. Los textos de las acciones y
// el resumen del detalle son los mismos del escritorio (Formats.Action y ActivityItem.Summarize).
//
// Los tipos del servidor SALEN DEL CONTRATO (regla P-07): no se declaran a mano. Se derivan del nombre de la operación,
// así funcionan igual con el contrato provisional y con el generado.

import type { RpcResponseOf } from '@/4-presentation/app/contract';
import { defineStatuses, statusOf } from '@/4-presentation/panel/kit';
import { formatDate, formatDateTime, formatMoney, inRange, laPazToday, matchesSearch, toDate, toIsoDate, type CsvColumn } from '@/4-presentation/panel/lib';

/** Una fila de `GetActivityQuery` tal como la manda el servidor. */
export type ActivityRecord = RpcResponseOf<'GetActivityQuery'>[number];

/** Cuántas filas se piden al servidor (el servidor acepta de 1 a 5000; la lista filtra en la página). */
export const TAKE_OPTIONS: readonly { value: string; label: string }[] = [
  { value: '200', label: 'Últimos 200' },
  { value: '500', label: 'Últimos 500' },
  { value: '1000', label: 'Últimos 1.000' },
  { value: '5000', label: 'Últimos 5.000' },
];
export const DEFAULT_TAKE = '500';

/** `?registros=` de la dirección → cantidad para el servidor (un valor raro vuelve al de siempre). */
export function takeOf(value: string): number {
  return Number(TAKE_OPTIONS.some((option) => option.value === value) ? value : DEFAULT_TAKE);
}

/** Resultado de la operación (enumeración `AuditOutcome` del servidor). */
export const OUTCOMES = defineStatuses({
  Succeeded: { label: 'Correcto', tone: 'success' },
  Rejected: { label: 'Rechazado', tone: 'warning' },
  Failed: { label: 'Con error', tone: 'danger' },
});

/** Filtros de la lista (en la dirección de la página). */
export const ACTIVITY_FILTERS = { q: '', usuario: '', resultado: '', desde: '', hasta: '', registros: DEFAULT_TAKE };
export type ActivityFilters = typeof ACTIVITY_FILTERS;

/** Valor del filtro «Usuario» para lo que hizo el sistema (trabajos automáticos, sin usuario). */
export const SYSTEM_USER = '_sistema';

// ---------------------------------------------------------------------------------------------------- acción

/** Texto de cada acción, como en el escritorio (Formats.Action) más las de la V6 y la V7. */
const ACTIONS: Readonly<Record<string, string>> = {
  // Inventario, toma física y catálogo
  RegisterMovement: 'Registró un movimiento',
  OpenPhysicalCount: 'Abrió una toma física',
  RecordCount: 'Registró un conteo',
  PostPhysicalCount: 'Contabilizó una toma física',
  CancelPhysicalCount: 'Anuló una toma física',
  RemoveCount: 'Quitó un conteo',
  SaveProduct: 'Guardó un producto',
  SetProductImage: 'Cambió la imagen de un producto',
  RemoveProductImage: 'Quitó la imagen de un producto',
  SaveCategory: 'Guardó una categoría',
  SaveSupplier: 'Guardó un proveedor',
  SaveCustomer: 'Guardó un cliente',
  SaveCustomerFiscalIdentity: 'Guardó los datos de facturación de un cliente',
  // Compras
  CreatePurchaseOrder: 'Creó una orden de compra',
  CreateSuggestedPurchaseOrders: 'Creó las órdenes del pedido sugerido',
  ApprovePurchaseOrder: 'Aprobó una orden de compra',
  CancelPurchaseOrder: 'Anuló una orden de compra',
  ReceivePurchaseOrder: 'Recibió una orden de compra',
  RegisterSupplierInvoice: 'Registró una factura de proveedor',
  // Caja y ventas
  OpenPosSession: 'Abrió caja',
  ClosePosSession: 'Cerró caja',
  Checkout: 'Cobró una venta',
  VoidSale: 'Anuló una venta',
  CreateSalesReturn: 'Registró una devolución',
  CreateExternalOrder: 'Registró un pedido de la tienda en línea',
  // Contabilidad
  CreateAccount: 'Creó una cuenta contable',
  CreateJournalEntry: 'Registró un asiento',
  // Usuarios, empresa y sucursales
  Login: 'Inició sesión',
  Logout: 'Cerró sesión',
  ChangePassword: 'Cambió su contraseña',
  ResetUserPassword: 'Restableció la contraseña de un usuario',
  SaveUser: 'Guardó un usuario',
  AssignUserBranches: 'Asignó sucursales a un usuario',
  SelectBranch: 'Cambió de sucursal activa',
  UpdateCompanySettings: 'Cambió la configuración de la empresa',
  CreateBranch: 'Creó una sucursal',
  UpdateBranch: 'Modificó una sucursal',
  // Transferencias
  CreateTransfer: 'Solicitó una transferencia',
  DispatchTransfer: 'Despachó una transferencia',
  ReceiveTransfer: 'Recibió una transferencia',
  CancelTransfer: 'Anuló una transferencia',
  // Integraciones
  CreateApiKey: 'Creó una API Key',
  RevokeApiKey: 'Revocó una API Key',
  CreateWebhook: 'Registró un webhook',
  DisableWebhook: 'Desactivó un webhook',
  RotateWebhookSecret: 'Rotó el secreto de un webhook',
  // Facturación SIAT
  SaveSiatProfile: 'Guardó la conexión con el SIN',
  SaveSiatSettings: 'Guardó la configuración de la facturación',
  SaveSiatBranch: 'Guardó una sucursal del Padrón',
  SaveMailSettings: 'Guardó el correo de envío de facturas',
  PrepareSiat: 'Preparó la facturación (CUIS, CUFD y catálogos)',
  RequestCuis: 'Solicitó un CUIS',
  RequestCufd: 'Solicitó un CUFD',
  SyncSiatCatalogs: 'Sincronizó los catálogos del SIN',
  CheckSiatCommunication: 'Verificó la comunicación con el SIN',
  RegisterSiatPointOfSale: 'Registró un punto de venta en el SIN',
  CloseSiatPointOfSale: 'Cerró un punto de venta en el SIN',
  LinkPointOfSaleRegister: 'Vinculó una caja a un punto de venta',
  SaveProductHomologation: 'Homologó productos con el SIN',
  SaveUnitHomologation: 'Homologó una unidad con el SIN',
  SavePaymentMethodHomologation: 'Homologó un medio de pago con el SIN',
  DispatchFiscalDocuments: 'Envió documentos fiscales al SIN',
  RunSiatWork: 'Ejecutó el trabajo automático de la facturación',
  CheckFiscalDocumentStatus: 'Verificó un documento fiscal en el SIN',
  VoidFiscalDocument: 'Anuló un documento fiscal',
  RevertFiscalVoid: 'Revirtió una anulación',
  ReissueFiscalDocument: 'Re-emitió un documento fiscal',
  SendFiscalDocumentEmail: 'Envió una factura por correo',
  RecordFiscalDelivery: 'Registró la entrega de una factura',
  GoOffline: 'Pasó a fuera de línea',
  RecoverPointOfSale: 'Recuperó un punto de venta (fuera de línea)',
  StartManualContingency: 'Inició una contingencia manual (CAFC)',
  EndContingency: 'Cerró una contingencia',
  RegisterContingencyCode: 'Registró un código CAFC',
  TranscribeManualInvoice: 'Transcribió una factura manual (CAFC)',
  VerifyNit: 'Verificó un NIT en el SIN',
  // Edición Tecnología (V4.2)
  SaveSpecDefinition: 'Guardó una especificación',
  SaveProductTech: 'Guardó la ficha técnica de un producto',
  RegisterStockSerials: 'Registró series de unidades en stock',
  DisposeSerial: 'Dio destino a una serie',
  OpenWarrantyClaim: 'Abrió un caso de garantía (RMA)',
  MoveWarrantyClaim: 'Cambió el estado de un caso RMA',
  AddWarrantyClaimNote: 'Agregó una nota a un caso RMA',
  IssueWarrantyReplacement: 'Repuso una unidad en garantía',
  SavePcBuild: 'Guardó un armado de PC',
  CancelPcBuild: 'Anuló un armado de PC',
  SellPcBuild: 'Vendió un armado de PC',
  // Tienda web y reservas (V6 y V7)
  ReservePcBuild: 'Reservó un armado de PC',
  ReserveCart: 'Reservó un carrito en mostrador',
  ReleasePcBuildReservation: 'Liberó una reserva',
  PublishPcBuild: 'Publicó un armado en la tienda',
  CreateStorefrontReservation: 'Reservó desde la tienda web',
  CancelStorefrontReservation: 'Canceló una reserva de la tienda web',
  ExpirePcBuildReservations: 'Venció reservas automáticamente',
  ResendReservationMail: 'Reenvió el correo de una reserva',
  RegisterCustomerAccount: 'Creó su cuenta de cliente',
  UpdateMyAccount: 'Actualizó los datos de su cuenta',
  CancelMyReservation: 'Liberó su reserva desde su cuenta',
  CreateMyReservation: 'Reservó desde su cuenta',
};

/** «RegisterMovement» → «Registró un movimiento»; una acción nueva sin texto se separa en palabras («Save thing»). */
export function actionText(action: string): string {
  const known = ACTIONS[action];
  if (known) return known;
  const words = action.replace(/\.ts$/i, '').replace(/(?<=[a-záéíóúñ])([A-ZÁÉÍÓÚÑ])/g, ' $1').trim();
  return words.length === 0 ? action : `${words[0].toUpperCase()}${words.slice(1).toLowerCase()}`;
}

// ---------------------------------------------------------------------------------------------------- detalle

/** Nombres en español de los datos más frecuentes de la bitácora (como el escritorio). */
const FIELD_NAMES: Readonly<Record<string, string>> = {
  Sku: 'SKU',
  BinCode: 'Posición',
  MovementTypeCode: 'Tipo',
  Quantity: 'Cantidad',
  BusinessDate: 'Fecha',
  DocumentReference: 'Documento',
  LotNumber: 'Lote',
  WarehouseCode: 'Almacén',
  CountDate: 'Fecha del conteo',
  Confirmed: 'Confirmado',
  RegisterCode: 'Caja',
  OpeningCash: 'Fondo',
  CountedCash: 'Arqueo',
  Operacion: 'Operación',
  Name: 'Nombre',
  Code: 'Código',
  Number: 'Número',
  Email: 'Correo',
  Description: 'Descripción',
  Reason: 'Motivo',
  Note: 'Nota',
  Notes: 'Nota',
  BranchCode: 'Sucursal',
  SupplierCode: 'Proveedor',
  CustomerCode: 'Cliente',
  CategoryCode: 'Categoría',
  ParentCode: 'Categoría superior',
  PaymentMethodCode: 'Medio de pago',
  InvoiceNumber: 'Factura',
  InvoiceDate: 'Fecha de la factura',
  ReceiptNumber: 'Recepción',
  AuthorizationCode: 'Código de autorización',
  TotalAmount: 'Importe total',
  Discounts: 'Descuentos',
  NotSubjectToVat: 'No sujeto a crédito fiscal',
  PurchaseType: 'Tipo de compra',
  SupplierDocument: 'Documento del proveedor',
  ToWarehouseCode: 'Almacén de destino',
  FromWarehouseCode: 'Almacén de origen',
  UnitCost: 'Costo unitario',
  SalePrice: 'Precio de venta',
  Minimum: 'Mínimo',
  Maximum: 'Máximo',
  IsActive: 'Activo',
  Enabled: 'Activada',
  Serial: 'Serie',
  ReplacementSerial: 'Serie de reposición',
  Issue: 'Falla',
  Resolution: 'Resolución',
  Next: 'Estado nuevo',
  Disposal: 'Destino',
  Defective: 'Por falla',
  ChargeableRepair: 'Reparación con cargo',
  WarrantyMonths: 'Meses de garantía',
  TrackSerials: 'Lleva serie',
  SerialKind: 'Tipo de serie',
  ValidDays: 'Días de vigencia',
  Quote: 'Cotizar',
  AcceptIncompatible: 'Acepta incompatibilidades',
  Maintain: 'Mantenimiento',
  Nit: 'NIT',
  TaxId: 'NIT',
  Environment: 'Ambiente',
  Url: 'URL',
  Date: 'Fecha',
  // V7
  RoleCode: 'Rol',
  MustChange: 'Debe cambiarla',
  Kind: 'Tipo',
  HoldDays: 'Días para recoger',
  Phone: 'Teléfono',
  Count: 'Cantidad',
};

/** Datos que son importes: se muestran como dinero («Bs 35.212,54»). */
const MONEY_FIELDS = new Set(['TotalAmount', 'Discounts', 'NotSubjectToVat', 'UnitCost', 'SalePrice', 'OpeningCash', 'CountedCash', 'Amount', 'Price', 'CashReceived', 'Total', 'Refund']);

export interface DetailField {
  label: string;
  value: string;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

/** Un dato en español: sí/no, fechas con la hora de La Paz e importes como dinero. */
function fieldValue(name: string, value: unknown): string {
  if (typeof value === 'boolean') return value ? 'sí' : 'no';
  if (typeof value === 'number') return MONEY_FIELDS.has(name) ? formatMoney(value) : String(value);
  if (typeof value === 'string') {
    if (/^\d{4}-\d{2}-\d{2}$/.test(value)) return formatDate(value);
    if (/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}/.test(value) && toDate(value)) return formatDateTime(value);
    return value;
  }
  return String(value);
}

function parse(details: string | null | undefined): unknown {
  if (!details || !details.trimStart().startsWith('{')) return null;
  try {
    return JSON.parse(details) as unknown;
  } catch {
    return null;
  }
}

/** Datos registrados del pedido (sin identificadores internos, listas ni objetos), en español. */
export function detailFields(details: string | null | undefined): DetailField[] {
  const parsed = parse(details);
  if (!isRecord(parsed) || !isRecord(parsed.request)) return [];
  const used = new Set<string>();
  return Object.entries(parsed.request)
    .filter(([name, value]) => value !== null && value !== undefined && typeof value !== 'object' && !name.endsWith('Id'))
    .map(([name, value]) => {
      // Dos datos con el mismo nombre en español («Note» y «Notes») no se pisan.
      const friendly = FIELD_NAMES[name] ?? name;
      const label = used.has(friendly) ? `${friendly} (${name})` : friendly;
      used.add(label);
      return { label, value: fieldValue(name, value) };
    });
}

/**
 * Resumen del detalle en una línea, como el escritorio: el error si lo hubo; si no, los datos del pedido
 * («SKU: MOU-LOG-G502 · Cantidad: 5») y el mensaje del resultado. Un texto que no es JSON (actividad migrada de la
 * V2.1) se muestra tal cual.
 */
export function summarizeDetails(details: string | null | undefined): string {
  if (!details || details.trim().length === 0) return '';
  const parsed = parse(details);
  if (parsed === null) return details.trim().startsWith('{') ? details : details.trim();
  if (!isRecord(parsed)) return '';
  if (typeof parsed.error === 'string' && parsed.error.trim()) return parsed.error.trim();
  if (typeof parsed.detalle === 'string') return parsed.detalle.trim();
  const parts = detailFields(details).map((field) => `${field.label}: ${field.value}`);
  if (isRecord(parsed.result) && typeof parsed.result.Message === 'string') parts.push(parsed.result.Message.replace(/^[✔\s]+/u, ''));
  return parts.join(' · ');
}

// ---------------------------------------------------------------------------------------------------- lista

/** Una fila lista para la tabla: lo del servidor más cómo se muestra. */
export interface ActivityItem {
  /** Identificador estable en la página (el servidor no manda uno). */
  key: string;
  record: ActivityRecord;
  /** Quién: el nombre, el correo o «Sistema». */
  who: string;
  email: string | null;
  /** Valor del filtro «Usuario» (el correo o `SYSTEM_USER`). */
  userValue: string;
  actionText: string;
  summary: string;
}

export function toActivityItems(records: readonly ActivityRecord[]): ActivityItem[] {
  const repeated = new Map<string, number>();
  return records.map((record) => {
    const base = `${record.occurredAt}|${record.userEmail ?? ''}|${record.action}`;
    const count = repeated.get(base) ?? 0;
    repeated.set(base, count + 1);
    const email = record.userEmail ?? null;
    return {
      key: count === 0 ? base : `${base}|${count}`,
      record,
      who: record.userName ?? email ?? 'Sistema',
      email,
      userValue: email ?? SYSTEM_USER,
      actionText: actionText(record.action),
      summary: summarizeDetails(record.details),
    };
  });
}

/** Opciones del filtro «Usuario»: quienes aparecen en la actividad cargada, por nombre. */
export function userOptions(items: readonly ActivityItem[]): { value: string; label: string }[] {
  const byValue = new Map<string, string>();
  for (const item of items) {
    if (byValue.has(item.userValue)) continue;
    byValue.set(item.userValue, item.userValue === SYSTEM_USER ? 'Sistema (trabajos automáticos)' : item.email && item.who !== item.email ? `${item.who} (${item.email})` : item.who);
  }
  return [...byValue.entries()].map(([value, label]) => ({ value, label })).sort((a, b) => a.label.localeCompare(b.label, 'es'));
}

/** Aplica los filtros de la página (usuario, resultado, fechas de La Paz y búsqueda sin acentos). */
export function filterActivity(items: readonly ActivityItem[], filters: ActivityFilters): ActivityItem[] {
  const range = { from: filters.desde || null, to: filters.hasta || null };
  return items.filter(
    (item) =>
      (!filters.usuario || item.userValue === filters.usuario) &&
      (!filters.resultado || item.record.outcome === filters.resultado) &&
      inRange(item.record.occurredAt, range) &&
      matchesSearch(filters.q, [item.who, item.email, item.actionText, item.record.action, item.summary]),
  );
}

export function outcomeLabel(outcome: string): string {
  return statusOf(OUTCOMES, outcome).label;
}

/** Columnas del CSV (más completas que la tabla: el correo y el código de la acción van aparte). */
export const CSV_COLUMNS: readonly CsvColumn<ActivityItem>[] = [
  { header: 'Fecha y hora', value: (item) => toDate(item.record.occurredAt) },
  { header: 'Usuario', value: (item) => item.who },
  { header: 'Correo', value: (item) => item.email },
  { header: 'Acción', value: (item) => item.actionText },
  { header: 'Código de la acción', value: (item) => item.record.action },
  { header: 'Resultado', value: (item) => outcomeLabel(item.record.outcome) },
  { header: 'Detalle', value: (item) => item.summary },
];

// ---------------------------------------------------------------------------------------------------- hoy

export interface TodaySummary {
  /** Día de La Paz («2026-09-29»). */
  day: string;
  total: number;
  rejected: number;
  failed: number;
  /** Quiénes operaron hoy y cuántas veces (de más a menos). */
  byUser: { label: string; value: number }[];
  /** Hora de la última operación de hoy. */
  lastAt: string | null;
  /** Todas las filas recibidas son de hoy: puede haber más que las que se pidieron. */
  capped: boolean;
}

/** Resumen de la actividad de hoy (La Paz) sobre las filas recibidas. */
export function todaySummary(records: readonly ActivityRecord[], now: Date, requested: number): TodaySummary {
  const day = laPazToday(now);
  const today = records.filter((record) => toIsoDate(record.occurredAt) === day);
  const users = new Map<string, number>();
  for (const record of today) {
    const who = record.userName ?? record.userEmail ?? 'Sistema';
    users.set(who, (users.get(who) ?? 0) + 1);
  }
  const lastAt = today.reduce<string | null>((latest, record) => (latest === null || record.occurredAt > latest ? record.occurredAt : latest), null);
  return {
    day,
    total: today.length,
    rejected: today.filter((record) => record.outcome === 'Rejected').length,
    failed: today.filter((record) => record.outcome === 'Failed').length,
    byUser: [...users.entries()].map(([label, value]) => ({ label, value })).sort((a, b) => b.value - a.value || a.label.localeCompare(b.label, 'es')),
    lastAt,
    capped: records.length >= requested && today.length === records.length,
  };
}

// ---------------------------------------------------------------------------------------------------- contraseña temporal

/** Por qué no sirve una contraseña temporal (las mismas reglas del servidor), o null si sirve. */
export function temporaryPasswordProblem(password: string): string | null {
  if (password.length < 8 || password.length > 128) return 'Use entre 8 y 128 caracteres.';
  if (!/\p{L}/u.test(password) || !/\p{N}/u.test(password)) return 'Combine letras y números.';
  return null;
}

const LETTERS = 'abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ';
const DIGITS = '23456789';

/** Una contraseña temporal fácil de dictar («kPxRt-4827»): letras y números sin los que se confunden (l, 1, O, 0). */
export function generateTemporaryPassword(random: (max: number) => number = secureRandom): string {
  const pick = (alphabet: string, length: number) => Array.from({ length }, () => alphabet[random(alphabet.length)]).join('');
  return `${pick(LETTERS, 5)}-${pick(DIGITS, 4)}`;
}

function secureRandom(max: number): number {
  const values = new Uint32Array(1);
  crypto.getRandomValues(values);
  return values[0] % max;
}
