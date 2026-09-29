// =====================================================================================================================
// ARCHIVO PROVISIONAL · escrito a mano por el paquete W1 mientras no existe la herramienta del servidor.
//
// El definitivo lo genera `minv contrato-web` (por reflexión sobre RpcCatalog) y REEMPLAZA este archivo completo; desde
// ese momento no se edita a mano y una prueba falla si queda desactualizado (regla P-07).
//
// Tiene la MISMA forma que tendrá el generado, pero solo con las operaciones que la web ya usa (sesión y cuenta del
// cliente) y, desde W3b, las del módulo de ejemplo del panel (Administración › Actividad: `GetActivityQuery` y
// `ResetUserPasswordCommand`, copiadas del C# de MINV.Application.Iam). Nadie lo importa directamente: todo pasa por el
// adaptador `./contract.ts`. Si el generado nombra distinto alguna lista u operación, se ajusta SOLO ese adaptador.
// =====================================================================================================================

// ---------------------------------------------------------------------------------------------------- sesión y RPC
export interface BranchInfo {
  id: string;
  code: string;
  name: string;
}

export interface BranchAccess {
  allBranches: boolean;
  branches: BranchInfo[];
  activeBranchId: string | null;
}

/** Respuesta de `/api/v1/web/session*` y `/account/register`. NUNCA lleva el token (regla P-02). */
export interface WebSession {
  displayName: string;
  email: string;
  roles: string[];
  permissions: string[];
  mustChangePassword: boolean;
  access: BranchAccess;
  kind: 'staff' | 'customer';
  expiresAt: string;
  serverVersion: string;
  company: string;
}

export interface RpcRequest {
  requestId: string;
  type: string;
  payload: unknown;
}

export interface RpcError {
  kind: string;
  message: string;
  errors?: string[] | null;
  code?: string | null;
}

export interface RpcResponse<TResult = unknown> {
  ok: boolean;
  result?: TResult | null;
  error?: RpcError | null;
  replayed?: boolean;
}

// ---------------------------------------------------------------------------------------------------- cuentas (MINV.Application.Accounts)
export type GetMyAccountQuery = Record<string, never>;

export interface MyAccountView {
  name: string;
  email: string;
  phone: string;
  documentType: number | null;
  documentNumber: string | null;
  complement: string | null;
}

export interface UpdateMyAccountCommand {
  name: string;
  phone: string;
  documentType?: number | null;
  documentNumber?: string | null;
  complement?: string | null;
}

export type GetMyReservationsQuery = Record<string, never>;

export interface StorefrontReservationLine {
  slot: string | null;
  sku: string;
  name: string;
  quantity: number;
  unitPrice: number;
  subtotal: number;
}

export interface StorefrontReservationView {
  number: string;
  status: string;
  statusText: string;
  createdAt: string;
  reservedUntil: string;
  total: number;
  contactName: string;
  branch: string;
  notes: string | null;
  hasCompatibilityWarnings: boolean;
  lines: StorefrontReservationLine[];
  cancelReason: string | null;
  kind?: string;
  mailQueued?: boolean;
}

export interface CancelMyReservationCommand {
  number: string;
}

export interface MyReservationLine {
  sku: string;
  quantity: number;
  slot?: string | null;
}

export interface CreateMyReservationCommand {
  lines: MyReservationLine[];
  kind: string;
  holdDays?: number | null;
  notes?: string | null;
  name?: string | null;
}

// ---------------------------------------------------------------------------------------------------- sesión (MINV.Application.Iam)
export interface ChangePasswordCommand {
  currentPassword: string;
  newPassword: string;
}

export interface SelectBranchCommand {
  sessionId: string;
  branchId: string | null;
}

// ---------------------------------------------------------------------------------------------------- actividad y usuarios (MINV.Application.Iam)
/** Resultado de una operación auditada (enumeración `AuditOutcome`, viaja como texto). */
export type AuditOutcome = 'Succeeded' | 'Rejected' | 'Failed';

/** `GetActivityQuery(int Take = 200)`: la actividad más reciente (el servidor acota `take` entre 1 y 5000). */
export interface GetActivityQuery {
  take?: number;
}

/** `ActivityRow`: una fila de la auditoría (quién hizo qué, cuándo y con qué resultado). */
export interface ActivityRow {
  occurredAt: string;
  userEmail: string | null;
  userName: string | null;
  action: string;
  outcome: AuditOutcome;
  details: string | null;
}

/** `ResetUserPasswordCommand(string Email, string NewPassword, bool MustChange = true)`: contraseña temporal y desbloqueo. */
export interface ResetUserPasswordCommand {
  email: string;
  newPassword: string;
  mustChange?: boolean;
}

// ---------------------------------------------------------------------------------------------------- operaciones
/** Nombre corto → petición y respuesta. */
export interface RpcOperations {
  GetMyAccountQuery: { request: GetMyAccountQuery; response: MyAccountView };
  UpdateMyAccountCommand: { request: UpdateMyAccountCommand; response: MyAccountView };
  GetMyReservationsQuery: { request: GetMyReservationsQuery; response: StorefrontReservationView[] };
  CancelMyReservationCommand: { request: CancelMyReservationCommand; response: StorefrontReservationView };
  CreateMyReservationCommand: { request: CreateMyReservationCommand; response: StorefrontReservationView };
  ChangePasswordCommand: { request: ChangePasswordCommand; response: boolean };
  SelectBranchCommand: { request: SelectBranchCommand; response: BranchAccess };
  GetActivityQuery: { request: GetActivityQuery; response: ActivityRow[] };
  ResetUserPasswordCommand: { request: ResetUserPasswordCommand; response: boolean };
}

export interface RpcOperationMeta {
  /** Nombre completo del caso de uso: es lo que viaja en `RpcRequest.type`. */
  type: string;
  /** Comando que modifica datos (idempotente por `requestId`). */
  command: boolean;
  /** Permisos que exige. */
  permissions: readonly string[];
  /** Módulos comerciales que exige. */
  modules: readonly string[];
  /** Lo puede ejecutar una sesión de cliente. */
  customer: boolean;
}

export const RPC_META = {
  GetMyAccountQuery: { type: 'MINV.Application.Accounts.GetMyAccountQuery', command: false, permissions: ['account.manage'], modules: [], customer: true },
  UpdateMyAccountCommand: { type: 'MINV.Application.Accounts.UpdateMyAccountCommand', command: true, permissions: ['account.manage'], modules: [], customer: true },
  GetMyReservationsQuery: { type: 'MINV.Application.Accounts.GetMyReservationsQuery', command: false, permissions: ['account.manage'], modules: [], customer: true },
  CancelMyReservationCommand: { type: 'MINV.Application.Accounts.CancelMyReservationCommand', command: true, permissions: ['account.manage'], modules: [], customer: true },
  CreateMyReservationCommand: { type: 'MINV.Application.Accounts.CreateMyReservationCommand', command: true, permissions: ['account.reserve'], modules: [], customer: true },
  ChangePasswordCommand: { type: 'MINV.Application.Iam.ChangePasswordCommand', command: true, permissions: [], modules: [], customer: true },
  SelectBranchCommand: { type: 'MINV.Application.Iam.SelectBranchCommand', command: true, permissions: [], modules: [], customer: false },
  GetActivityQuery: { type: 'MINV.Application.Iam.GetActivityQuery', command: false, permissions: ['iam.audit.view'], modules: [], customer: false },
  ResetUserPasswordCommand: { type: 'MINV.Application.Iam.ResetUserPasswordCommand', command: true, permissions: ['iam.users.manage'], modules: [], customer: false },
} as const satisfies Record<keyof RpcOperations, RpcOperationMeta>;

// ---------------------------------------------------------------------------------------------------- permisos y roles
export const PERMISSIONS = [
  { code: 'catalog.manage', name: 'Crear y modificar productos, categorías, unidades y proveedores' },
  { code: 'iam.users.manage', name: 'Administrar usuarios, roles y permisos' },
  { code: 'inventory.movements.register.warehouse', name: 'Registrar entradas, saldo inicial y ajustes' },
  { code: 'inventory.movements.register.sales', name: 'Registrar salidas' },
  { code: 'inventory.stock.view', name: 'Consultar stock, alertas y pedido sugerido' },
  { code: 'inventory.counts.record', name: 'Registrar conteos de la toma física' },
  { code: 'inventory.counts.post', name: 'Generar los ajustes de la toma física' },
  { code: 'purchasing.manage', name: 'Órdenes de compra, recepciones y devoluciones' },
  { code: 'sales.pos.operate', name: 'Abrir y cerrar caja, vender y cobrar' },
  { code: 'iam.audit.view', name: 'Consultar la auditoría y la actividad' },
  { code: 'accounting.manage', name: 'Asientos, períodos y costos' },
  { code: 'reports.view', name: 'Consultar reportes de ventas, compras, inventario y rentabilidad' },
  { code: 'sales.customers.manage', name: 'Crear y modificar clientes' },
  { code: 'sales.view', name: 'Consultar el historial de ventas y facturas' },
  { code: 'corporate.branches.all', name: 'Ver y operar todas las sucursales (gerencia global)' },
  { code: 'corporate.branches.manage', name: 'Crear y modificar sucursales y asignar usuarios a sucursales' },
  { code: 'inventory.transfers.manage', name: 'Crear, despachar y recibir transferencias entre sucursales' },
  { code: 'integration.manage', name: 'Administrar API Keys y webhooks de integración B2B' },
  { code: 'billing.view', name: 'Consultar documentos fiscales, estado del SIAT y libros de ventas y compras' },
  { code: 'billing.issue', name: 'Emitir facturas (al vender) y reenviar documentos fiscales' },
  { code: 'billing.void', name: 'Anular y revertir documentos fiscales y emitir notas crédito-débito' },
  { code: 'billing.contingency', name: 'Gestionar eventos significativos, paquetes de contingencia y CAFC' },
  { code: 'billing.configure', name: 'Configurar la facturación SIAT' },
  { code: 'catalog.specs.manage', name: 'Fichas técnicas, garantía y control por serie o IMEI' },
  { code: 'inventory.serials.view', name: 'Consultar series e IMEI, su trazabilidad, la garantía y los casos RMA' },
  { code: 'inventory.serials.manage', name: 'Registrar series e IMEI y dar de baja unidades serializadas' },
  { code: 'service.rma.open', name: 'Abrir casos de garantía (RMA)' },
  { code: 'service.rma.manage', name: 'Garantías y RMA: diagnosticar, enviar al proveedor, reponer y entregar' },
  { code: 'sales.pcbuild.manage', name: 'Armador de PC y reservas: armar, cotizar, reservar y anular' },
  { code: 'storefront.read', name: 'Tienda web: leer el catálogo público' },
  { code: 'storefront.reserve', name: 'Tienda web: reservar y consultar o cancelar una reserva' },
  { code: 'account.manage', name: 'Cuenta de cliente: mis datos y mis reservas' },
  { code: 'account.reserve', name: 'Cuenta de cliente: reservar con los datos de la cuenta' },
] as const;

export const ROLES = [
  { code: 'ADMIN', name: 'Administrador' },
  { code: 'BODEGA', name: 'Bodega' },
  { code: 'VENTAS', name: 'Ventas' },
  { code: 'CONSULTA', name: 'Consulta' },
  { code: 'CAJERO', name: 'Cajero' },
  { code: 'GERENCIA', name: 'Gerencia' },
  { code: 'TIENDA_WEB', name: 'Tienda web' },
  { code: 'CLIENTE', name: 'Cliente web' },
] as const;
