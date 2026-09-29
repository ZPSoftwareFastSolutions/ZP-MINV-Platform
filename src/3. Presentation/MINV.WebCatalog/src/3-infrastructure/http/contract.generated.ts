// archivo generado: no editar; regenerar con minv contrato-web
//
// Contrato del RPC del servidor en la nube para la web (regla P-07). Lo escribe `minv contrato-web` por reflexión
// sobre RpcCatalog, con la serialización de RpcJson.Options:
//   dotnet run --project "src/4. Tools/MINV.Cli" -- contrato-web
// Nombres en camelCase; enumeraciones como texto; tuplas como { item1, item2 }; Guid, fechas, horas y byte[] (base64)
// como texto; T? como T | null. En una PETICIÓN, un parámetro con valor por defecto es opcional y las propiedades
// calculadas no viajan; en una RESPUESTA viajan todas las propiedades, también las calculadas. Una prueba del servidor
// falla si este archivo no coincide con el código. Su ÚNICO importador es ./contract.ts: el resto de la web usa los
// nombres de ese adaptador.

// ---------------------------------------------------------------------------------------------------- tipos de las peticiones y las respuestas

/** MINV.Application.Accounting.AccountRow */
export interface AccountRow {
  balance: number;
  code: string;
  credit: number;
  debit: number;
  isPostable: boolean;
  level: number;
  name: string;
  parentCode: string | null;
  type: AccountType;
}

/** MINV.Domain.Accounting.AccountType */
export type AccountType = 'Asset' | 'Equity' | 'Expense' | 'Liability' | 'Revenue';

/** MINV.Application.Iam.ActivityRow */
export interface ActivityRow {
  action: string;
  details: string | null;
  occurredAt: string;
  outcome: AuditOutcome;
  userEmail: string | null;
  userName: string | null;
}

/** MINV.Application.Tech.AddWarrantyClaimNoteCommand */
export interface AddWarrantyClaimNoteCommand {
  note: string;
  number: string;
}

/** MINV.Domain.Inventory.AlertRow */
export interface AlertRow {
  category: string;
  lastMovement: string | null;
  maximum: number;
  minimum: number;
  name: string;
  position: number;
  shortfall: number;
  sku: string;
  status: StockStatusCode;
  stock: number;
  suggestedQuantity: number;
  supplier: string;
  unit: string;
}

/** MINV.Application.Integration.ApiKeyRow */
export interface ApiKeyRow {
  branch: string | null;
  createdAt: string;
  expiresAt: string | null;
  id: string;
  isUsable: boolean;
  lastUsedAt: string | null;
  name: string;
  owner: string;
  prefix: string;
  revokedAt: string | null;
  scopes: string[];
}

/** MINV.Application.Integration.ApiProduct */
export interface ApiProduct {
  barcodes: string[];
  category: string;
  isActive: boolean;
  name: string;
  price: number;
  sku: string;
  unit: string;
}

/** MINV.Application.Integration.ApiStock */
export interface ApiStock {
  available: number;
  branchCode: string;
  onHand: number;
  reserved: number;
  sku: string;
  warehouseCode: string;
}

/** MINV.Application.Purchasing.ApprovePurchaseOrderCommand */
export interface ApprovePurchaseOrderCommand {
  id: string;
}

/** MINV.Application.Corporate.AssignUserBranchesCommand */
export interface AssignUserBranchesCommand {
  branchCodes: string[];
  email: string;
}

/** MINV.Domain.Iam.AuditOutcome */
export type AuditOutcome = 'Failed' | 'Rejected' | 'Succeeded';

/** MINV.Application.Billing.BillingAccessView */
export interface BillingAccessView {
  configured: boolean;
  enabled: boolean;
  environment: number;
  moduleActive: boolean;
}

/** MINV.Application.Inventory.Queries.BinItem */
export interface BinItem {
  code: string;
  warehouseCode: string;
  zone: string;
}

/** MINV.Application.Iam.BranchAccess */
export interface BranchAccess {
  active: BranchInfo | null;
  activeBranchId: string | null;
  allBranches: boolean;
  branches: BranchInfo[];
}

/** MINV.Application.Iam.BranchInfo */
export interface BranchInfo {
  code: string;
  id: string;
  name: string;
}

/** MINV.Application.Corporate.BranchReport */
export interface BranchReport {
  branches: BranchReportRow[];
  days: BranchReportDay[];
  from: string;
  inTransitValue: number;
  refreshedAt: string | null;
  to: string;
  totalRevenue: number;
  totalStockValue: number;
}

/** MINV.Application.Corporate.BranchReportDay */
export interface BranchReportDay {
  day: string;
  revenueByBranch: number[];
}

/** MINV.Application.Corporate.BranchReportRow */
export interface BranchReportRow {
  averageTicket: number;
  code: string;
  name: string;
  revenue: number;
  sharePercent: number;
  stockValue: number;
  tax: number;
  tickets: number;
}

/** MINV.Application.Corporate.BranchRow */
export interface BranchRow {
  code: string;
  id: string;
  isActive: boolean;
  isVisible: boolean;
  name: string;
  stockValue: number | null;
  transfersIn: number;
  transfersOut: number;
  users: number;
  warehouses: string[];
}

/** MINV.Application.Accounts.CancelMyReservationCommand */
export interface CancelMyReservationCommand {
  number: string;
}

/** MINV.Application.Tech.CancelPcBuildCommand */
export interface CancelPcBuildCommand {
  number: string;
  reason?: string | null;
}

/** MINV.Application.Inventory.PhysicalCounts.CancelPhysicalCountCommand */
export interface CancelPhysicalCountCommand {
  physicalCountId: string;
}

/** MINV.Application.Purchasing.CancelPurchaseOrderCommand */
export interface CancelPurchaseOrderCommand {
  id: string;
}

/** MINV.Application.Storefront.CancelStorefrontReservationCommand */
export interface CancelStorefrontReservationCommand {
  number: string;
  phone: string;
}

/** MINV.Application.Inventory.Transfers.CancelTransferCommand */
export interface CancelTransferCommand {
  id: string;
  reason: string;
}

/** MINV.Application.Tech.CartItemInput */
export interface CartItemInput {
  quantity?: number;
  sku: string;
}

/** MINV.Application.Catalog.CatalogItem */
export interface CatalogItem {
  barcode: string | null;
  binCode: string | null;
  category: string;
  categoryCode: string;
  description: string | null;
  hasImage: boolean;
  isActive: boolean;
  maximum: number;
  minimum: number;
  name: string;
  salePrice: number;
  sku: string;
  supplier: string | null;
  supplierCode: string | null;
  unit: string;
  unitCost: number;
  variantId: string;
}

/** MINV.Application.Catalog.CatalogOptions */
export interface CatalogOptions {
  categories: OptionItem[];
  priceListName: string;
  suppliers: OptionItem[];
  taxRate: number;
  units: UnitOption[];
  vatOnInvoicedAmount: boolean;
}

/** MINV.Application.Iam.ChangePasswordCommand */
export interface ChangePasswordCommand {
  currentPassword: string;
  newPassword: string;
}

/** MINV.Application.Billing.CheckFiscalDocumentStatusCommand */
export interface CheckFiscalDocumentStatusCommand {
  documentId: string;
}

/** MINV.Application.Tech.CheckPcBuildQuery */
export interface CheckPcBuildQuery {
  items: PcBuildItemInput[];
}

/** MINV.Application.Billing.CheckSiatCommunicationCommand */
export interface CheckSiatCommunicationCommand {
  pointOfSaleId?: string | null;
}

/** MINV.Application.Sales.CheckoutCommand */
export interface CheckoutCommand {
  buyer?: FiscalBuyerInput | null;
  cardNumber?: string | null;
  cashReceived?: number | null;
  customerCode: string;
  lines: SaleLineInput[];
  paymentMethodCode: string;
  paymentReference?: string | null;
}

/** MINV.Application.Sales.CheckoutResult */
export interface CheckoutResult {
  change: number;
  cuf: string | null;
  customer: string;
  fiscalDocumentId: string | null;
  fiscalNumber: number | null;
  fiscalStatus: FiscalDocumentStatus | null;
  invoiceNumber: string;
  issuedAt: string;
  lines: ReceiptLine[];
  orderNumber: string;
  paymentMethod: string;
  tax: number;
  total: number;
}

/** MINV.Application.Sales.ClosePosSessionCommand */
export interface ClosePosSessionCommand {
  countedCash: number;
  sessionId: string;
}

/** MINV.Application.Billing.CloseSiatPointOfSaleCommand */
export interface CloseSiatPointOfSaleCommand {
  pointOfSaleId: string;
}

/** MINV.Application.Iam.CompanySettings */
export interface CompanySettings {
  alertMargin: number;
  code: string;
  daysWithoutRotation: number;
  legalName: string;
  minBusinessDate: string;
  taxId: string | null;
  timeZoneId: string;
}

/** MINV.Application.Corporate.ConsolidatedStock */
export interface ConsolidatedStock {
  branches: BranchInfo[];
  inTransitValue: number;
  rows: ConsolidatedStockRow[];
  totalValue: number;
  valueByBranch: number[];
}

/** MINV.Application.Corporate.ConsolidatedStockQuery */
export interface ConsolidatedStockQuery {
  search?: string | null;
}

/** MINV.Application.Corporate.ConsolidatedStockRow */
export interface ConsolidatedStockRow {
  byBranch: number[];
  category: string;
  inTransit: number;
  name: string;
  sku: string;
  total: number;
  unit: string;
  value: number;
}

/** MINV.Application.Billing.ContingencyCodeRow */
export interface ContingencyCodeRow {
  branchCode: string;
  code: string;
  documentSector: number;
  id: string;
  isActive: boolean;
  numberFrom: number;
  numberTo: number;
  used: number;
  validUntil: string | null;
}

/** MINV.Application.Accounting.CreateAccountCommand */
export interface CreateAccountCommand {
  code: string;
  name: string;
  parentCode: string;
}

/** MINV.Application.Integration.CreateApiKeyCommand */
export interface CreateApiKeyCommand {
  branchCode?: string | null;
  expiresInDays?: number | null;
  name: string;
  scopes: string[];
}

/** MINV.Application.Corporate.CreateBranchCommand */
export interface CreateBranchCommand {
  code: string;
  createPosRegister?: boolean;
  name: string;
  warehouseCode: string;
  warehouseName: string;
}

/** MINV.Application.Integration.CreateExternalOrderCommand */
export interface CreateExternalOrderCommand {
  buyer?: FiscalBuyerInput | null;
  customerCode: string;
  externalId: string;
  lines: SaleLineInput[];
  paymentMethodCode: string;
  paymentReference?: string | null;
  warehouseCode?: string | null;
}

/** MINV.Application.Accounting.CreateJournalEntryCommand */
export interface CreateJournalEntryCommand {
  branchId?: string | null;
  date: string;
  description: string;
  lines: JournalLineSpec[];
}

/** MINV.Application.Accounts.CreateMyReservationCommand */
export interface CreateMyReservationCommand {
  holdDays?: number | null;
  kind?: PcBuildKind;
  lines: StorefrontReservationLineInput[];
  name?: string | null;
  notes?: string | null;
}

/** MINV.Application.Purchasing.CreatePurchaseOrderCommand */
export interface CreatePurchaseOrderCommand {
  expectedDate: string | null;
  lines: PurchaseLineInput[];
  notes: string | null;
  supplierCode: string;
}

/** MINV.Application.Billing.CreateSalesReturnCommand */
export interface CreateSalesReturnCommand {
  defective?: boolean;
  invoiceNumber: string;
  lines: ReturnLineInput[];
  reason: string;
  refundPaymentMethodCode: string;
}

/** MINV.Application.Storefront.CreateStorefrontReservationCommand */
export interface CreateStorefrontReservationCommand {
  buyer?: ReservationBuyerInput | null;
  contact: StorefrontContactInput;
  holdDays?: number | null;
  idempotencyKey: string;
  kind?: PcBuildKind;
  lines: StorefrontReservationLineInput[];
  name?: string | null;
  notes: string | null;
}

/** MINV.Application.Purchasing.CreateSuggestedPurchaseOrdersCommand */
export type CreateSuggestedPurchaseOrdersCommand = Record<string, never>;

/** MINV.Application.Inventory.Transfers.CreateTransferCommand */
export interface CreateTransferCommand {
  fromWarehouseCode?: string | null;
  lines: TransferLineInput[];
  notes?: string | null;
  toWarehouseCode: string;
}

/** MINV.Application.Integration.CreateWebhookCommand */
export interface CreateWebhookCommand {
  branchCode?: string | null;
  description?: string | null;
  events: string[];
  url: string;
}

/** MINV.Application.Integration.CreatedApiKey */
export interface CreatedApiKey {
  effectivePermissions: string[];
  id: string;
  name: string;
  prefix: string;
  token: string;
}

/** MINV.Application.Integration.CreatedWebhook */
export interface CreatedWebhook {
  id: string;
  message: string;
  secret: string;
  url: string;
}

/** MINV.Application.Billing.CustomerFiscalIdentityRow */
export interface CustomerFiscalIdentityRow {
  code: string;
  complement: string | null;
  documentNumber: string | null;
  documentType: number | null;
}

/** MINV.Application.Partners.CustomerRow */
export interface CustomerRow {
  category: string;
  categoryCode: string;
  code: string;
  email: string | null;
  isActive: boolean;
  lastPurchase: string | null;
  name: string;
  phone: string | null;
  purchases: number;
  taxId: string | null;
  total: number;
}

/** MINV.Application.Partners.CustomersView */
export interface CustomersView {
  categories: OptionItem[];
  customers: CustomerRow[];
}

/** MINV.Application.Integration.DisableWebhookCommand */
export interface DisableWebhookCommand {
  id: string;
}

/** MINV.Application.Billing.DispatchFiscalDocumentsCommand */
export interface DispatchFiscalDocumentsCommand {
  documentId?: string | null;
  max?: number;
}

/** MINV.Application.Billing.DispatchResult */
export interface DispatchResult {
  documents: FiscalDocumentRow[];
  messages: string[];
  rejected: number;
  sent: number;
  valid: number;
  wentOffline: number;
}

/** MINV.Application.Inventory.Transfers.DispatchTransferCommand */
export interface DispatchTransferCommand {
  id: string;
}

/** MINV.Application.Tech.DisposeSerialCommand */
export interface DisposeSerialCommand {
  disposal: SerialDisposal;
  reason: string;
  serial: string;
  sku?: string | null;
}

/** MINV.Application.Billing.EndContingencyCommand */
export interface EndContingencyCommand {
  endedAt?: string | null;
  pointOfSaleId: string;
}

/** MINV.Application.Storefront.ExpirePcBuildReservationsCommand */
export type ExpirePcBuildReservationsCommand = Record<string, never>;

/** MINV.Application.Billing.ExportFiscalBookQuery */
export interface ExportFiscalBookQuery {
  format?: string;
  month: number;
  purchases: boolean;
  year: number;
}

/** MINV.Application.Integration.ExternalOrderResult */
export interface ExternalOrderResult {
  branchCode: string;
  cuf: string | null;
  externalId: string;
  fiscalNumber: number | null;
  invoiceNumber: string;
  issuedAt: string;
  orderNumber: string;
  replayed: boolean;
  tax: number;
  total: number;
}

/** MINV.Application.Billing.FindFiscalBuyerQuery */
export interface FindFiscalBuyerQuery {
  complement?: string | null;
  documentNumber: string;
  documentType: number;
}

/** MINV.Application.Billing.FiscalBuyerInput */
export interface FiscalBuyerInput {
  complement: string | null;
  documentNumber: string;
  documentType: number;
  email: string | null;
  exceptionRequested?: boolean;
  name: string | null;
}

/** MINV.Application.Billing.FiscalBuyerLookup */
export interface FiscalBuyerLookup {
  complement: string | null;
  customerCode: string | null;
  documentNumber: string;
  documentType: number;
  email: string | null;
  found: boolean;
  name: string | null;
  nitValid: boolean | null;
}

/** MINV.Domain.Billing.FiscalDeliveryChannel */
export type FiscalDeliveryChannel = 'Email' | 'Pdf' | 'Print';

/** MINV.Application.Billing.FiscalDeliveryView */
export interface FiscalDeliveryView {
  channel: FiscalDeliveryChannel;
  error: string | null;
  occurredAt: string;
  recipient: string | null;
  succeeded: boolean;
}

/** MINV.Domain.Billing.FiscalDocumentAction */
export type FiscalDocumentAction = 'Accepted' | 'Delivered' | 'Discarded' | 'Issued' | 'NoResponse' | 'PackageRejected' | 'PackageValidated' | 'Packaged' | 'Printed' | 'Reissued' | 'Rejected' | 'RevertFailed' | 'Reverted' | 'Sent' | 'StatusChecked' | 'VoidFailed' | 'VoidRequested' | 'Voided';

/** MINV.Application.Billing.FiscalDocumentDetail */
export interface FiscalDocumentDetail {
  buyerEmail: string | null;
  cafc: string | null;
  cardNumberMasked: string | null;
  deliveries: FiscalDeliveryView[];
  events: FiscalDocumentEventView[];
  exceptionCode: number;
  legend: string;
  lines: FiscalDocumentLineView[];
  original: FiscalPrintOriginal | null;
  paymentMethod: string | null;
  receptionCode: string | null;
  replacedByDocumentId: string | null;
  replacesDocumentId: string | null;
  row: FiscalDocumentRow;
  taxAmount: number;
  voidReason: string | null;
  xml: string;
}

/** MINV.Application.Billing.FiscalDocumentEventView */
export interface FiscalDocumentEventView {
  action: FiscalDocumentAction;
  description: string | null;
  messages: string | null;
  occurredAt: string;
  receptionCode: string | null;
  siatCode: number | null;
  user: string | null;
}

/** MINV.Domain.Billing.FiscalDocumentKind */
export type FiscalDocumentKind = 'CreditDebitNote' | 'Invoice';

/** MINV.Application.Billing.FiscalDocumentLineView */
export interface FiscalDocumentLineView {
  activityCode: string;
  description: string;
  discount: number;
  lineNumber: number;
  productCode: string;
  quantity: number;
  serialsText: string | null;
  sinProductCode: number;
  sinUnitCode: number;
  subtotal: number;
  transactionCode: number | null;
  unit: string;
  unitPrice: number;
}

/** MINV.Application.Billing.FiscalDocumentRow */
export interface FiscalDocumentRow {
  branchCode: string;
  branchId: string;
  buyerDocument: string;
  buyerName: string;
  canCreditNote: boolean;
  canRevert: boolean;
  canVoid: boolean;
  cuf: string;
  emissionType: number;
  id: string;
  isReverted: boolean;
  issuedAt: string;
  kind: FiscalDocumentKind;
  lastSiatCode: number | null;
  number: number;
  pointOfSaleCode: number;
  saleNumber: string | null;
  status: FiscalDocumentStatus;
  total: number;
  voidDeadline: string;
}

/** MINV.Domain.Billing.FiscalDocumentStatus */
export type FiscalDocumentStatus = 'Discarded' | 'DuplicateToVoid' | 'InPackage' | 'NoResponse' | 'Offline' | 'PackageRejected' | 'Pending' | 'Rejected' | 'Valid' | 'Voided';

/** MINV.Application.Billing.FiscalFile */
export interface FiscalFile {
  content: string;
  contentType: string;
  fileName: string;
}

/** MINV.Application.Billing.FiscalPackageRow */
export interface FiscalPackageRow {
  branchCode: string;
  cafc: string | null;
  documentSector: number;
  documents: number;
  eventId: string;
  id: string;
  lastSiatCode: number | null;
  messages: string | null;
  pointOfSaleCode: number;
  receptionCode: string | null;
  sentAt: string;
  status: FiscalPackageStatus;
  validatedAt: string | null;
}

/** MINV.Domain.Billing.FiscalPackageStatus */
export type FiscalPackageStatus = 'Observed' | 'Rejected' | 'Sent' | 'Validated';

/** MINV.Application.Abstractions.FiscalPrintLine */
export interface FiscalPrintLine {
  description: string;
  discount: number;
  productCode: string;
  quantity: number;
  serialsText: string | null;
  subtotal: number;
  transactionCode: number | null;
  unit: string;
  unitPrice: number;
  warrantyUntil: string | null;
}

/** MINV.Application.Abstractions.FiscalPrintModel */
export interface FiscalPrintModel {
  address: string;
  amountInWords: string;
  amountToPay: number;
  branchLabel: string;
  buyerDocument: string;
  buyerName: string;
  cashier: string | null;
  creditDebitAmount: number | null;
  cuf: string;
  customerCode: string;
  discount: number;
  giftCard: number;
  isOffline: boolean;
  isTest: boolean;
  isVoided: boolean;
  issuedAt: string;
  issuerName: string;
  issuerNit: number;
  legends: string[];
  lines: FiscalPrintLine[];
  municipality: string;
  number: number;
  original: FiscalPrintOriginal | null;
  paymentMethod: string | null;
  phone: string | null;
  pointOfSaleCode: number;
  qrUrl: string;
  returnedTotal: number | null;
  saleNumber: string | null;
  subtitle: string;
  subtotal: number;
  taxBase: number;
  title: string;
  total: number;
}

/** MINV.Application.Abstractions.FiscalPrintOriginal */
export interface FiscalPrintOriginal {
  cuf: string;
  issuedAt: string;
  number: number;
}

/** MINV.Application.Iam.GetActivityQuery */
export interface GetActivityQuery {
  take?: number;
}

/** MINV.Application.Integration.GetApiCatalogQuery */
export interface GetApiCatalogQuery {
  pageNumber?: number;
  pageSize?: number;
  search?: string | null;
}

/** MINV.Application.Integration.GetApiKeysQuery */
export type GetApiKeysQuery = Record<string, never>;

/** MINV.Application.Integration.GetApiStockQuery */
export interface GetApiStockQuery {
  branchCode?: string | null;
  pageNumber?: number;
  pageSize?: number;
  sku?: string | null;
}

/** MINV.Application.Tech.GetAvailableSerialsQuery */
export interface GetAvailableSerialsQuery {
  sku: string;
  warehouseCode?: string | null;
}

/** MINV.Application.Billing.GetBillingAccessQuery */
export type GetBillingAccessQuery = Record<string, never>;

/** MINV.Application.Inventory.Queries.GetBinsQuery */
export interface GetBinsQuery {
  warehouseCode?: string | null;
}

/** MINV.Application.Corporate.GetBranchReportQuery */
export interface GetBranchReportQuery {
  from: string;
  to: string;
}

/** MINV.Application.Corporate.GetBranchesQuery */
export type GetBranchesQuery = Record<string, never>;

/** MINV.Application.Catalog.GetCatalogOptionsQuery */
export type GetCatalogOptionsQuery = Record<string, never>;

/** MINV.Application.Catalog.GetCatalogQuery */
export interface GetCatalogQuery {
  categoryCode?: string | null;
  specFilters?: SpecFilter[] | null;
}

/** MINV.Application.Accounting.GetChartOfAccountsQuery */
export interface GetChartOfAccountsQuery {
  from?: string | null;
  to?: string | null;
}

/** MINV.Application.Iam.GetCompanySettingsQuery */
export type GetCompanySettingsQuery = Record<string, never>;

/** MINV.Application.Billing.GetContingencyCodesQuery */
export type GetContingencyCodesQuery = Record<string, never>;

/** MINV.Application.Billing.GetCustomerFiscalIdentitiesQuery */
export type GetCustomerFiscalIdentitiesQuery = Record<string, never>;

/** MINV.Application.Partners.GetCustomersQuery */
export type GetCustomersQuery = Record<string, never>;

/** MINV.Application.Integration.GetExternalOrderQuery */
export interface GetExternalOrderQuery {
  externalId: string;
}

/** MINV.Application.Billing.GetFiscalDocumentQuery */
export interface GetFiscalDocumentQuery {
  documentId: string;
}

/** MINV.Application.Billing.GetFiscalDocumentsQuery */
export interface GetFiscalDocumentsQuery {
  from: string;
  kind?: FiscalDocumentKind | null;
  search?: string | null;
  status?: FiscalDocumentStatus | null;
  to: string;
}

/** MINV.Application.Billing.GetFiscalPackagesQuery */
export interface GetFiscalPackagesQuery {
  eventId?: string | null;
}

/** MINV.Application.Billing.GetFiscalPrintModelQuery */
export interface GetFiscalPrintModelQuery {
  documentId: string;
}

/** MINV.Application.Billing.GetHomologationQuery */
export type GetHomologationQuery = Record<string, never>;

/** MINV.Application.Accounting.GetIncomeStatementQuery */
export interface GetIncomeStatementQuery {
  from: string;
  to: string;
}

/** MINV.Application.Integration.GetIntegrationCatalogQuery */
export type GetIntegrationCatalogQuery = Record<string, never>;

/** MINV.Application.Accounting.GetJournalQuery */
export interface GetJournalQuery {
  from: string;
  to: string;
}

/** MINV.Application.Inventory.Queries.GetMovementTrendQuery */
export interface GetMovementTrendQuery {
  days?: number;
}

/** MINV.Application.Inventory.Queries.GetMovementTypesQuery */
export type GetMovementTypesQuery = Record<string, never>;

/** MINV.Application.Reports.GetMovementsReportQuery */
export interface GetMovementsReportQuery {
  from: string;
  to: string;
  typeCode?: string | null;
}

/** MINV.Application.Accounts.GetMyAccountQuery */
export type GetMyAccountQuery = Record<string, never>;

/** MINV.Application.Accounts.GetMyReservationsQuery */
export type GetMyReservationsQuery = Record<string, never>;

/** MINV.Application.Inventory.PhysicalCounts.GetOpenPhysicalCountQuery */
export interface GetOpenPhysicalCountQuery {
  warehouseCode?: string | null;
}

/** MINV.Application.Integration.GetOutgoingMailsQuery */
export interface GetOutgoingMailsQuery {
  number?: string | null;
  status?: OutgoingMailStatus | null;
  take?: number;
}

/** MINV.Application.Tech.GetPcBuildCandidatesQuery */
export interface GetPcBuildCandidatesQuery {
  categoryCode?: string | null;
  current: PcBuildItemInput[];
  onlyInStock?: boolean;
  slot: PcSlot;
  text?: string | null;
}

/** MINV.Application.Tech.GetPcBuildQuery */
export interface GetPcBuildQuery {
  number: string;
}

/** MINV.Application.Tech.GetPcBuildsQuery */
export interface GetPcBuildsQuery {
  channel?: PcBuildChannel | null;
  kind?: PcBuildKind | null;
  status?: PcBuildStatus | null;
}

/** MINV.Application.Billing.GetPosFiscalStateQuery */
export type GetPosFiscalStateQuery = Record<string, never>;

/** MINV.Application.Sales.GetPosStateQuery */
export type GetPosStateQuery = Record<string, never>;

/** MINV.Application.Inventory.Queries.GetProductCardQuery */
export interface GetProductCardQuery {
  skuOrBarcode: string;
  take?: number;
}

/** MINV.Application.Catalog.GetProductImagesQuery */
export interface GetProductImagesQuery {
  variantIds?: string[] | null;
}

/** MINV.Application.Inventory.Queries.GetProductLookupQuery */
export interface GetProductLookupQuery {
  includeInactive?: boolean;
}

/** MINV.Application.Tech.GetProductTechQuery */
export interface GetProductTechQuery {
  sku: string;
}

/** MINV.Application.Purchasing.GetPurchaseOrderQuery */
export interface GetPurchaseOrderQuery {
  id: string;
}

/** MINV.Application.Purchasing.GetPurchaseOrdersQuery */
export interface GetPurchaseOrdersQuery {
  status?: PurchaseOrderStatus | null;
}

/** MINV.Application.Billing.GetPurchasesBookQuery */
export interface GetPurchasesBookQuery {
  month: number;
  year: number;
}

/** MINV.Application.Reports.GetPurchasesReportQuery */
export interface GetPurchasesReportQuery {
  from: string;
  to: string;
}

/** MINV.Application.Billing.GetReceiptsWithoutInvoiceQuery */
export type GetReceiptsWithoutInvoiceQuery = Record<string, never>;

/** MINV.Application.Inventory.Queries.GetRecentMovementsQuery */
export interface GetRecentMovementsQuery {
  take?: number;
}

/** MINV.Application.Billing.GetReturnableLinesQuery */
export interface GetReturnableLinesQuery {
  invoiceNumber: string;
}

/** MINV.Application.Iam.GetRolesQuery */
export type GetRolesQuery = Record<string, never>;

/** MINV.Application.Sales.GetSaleLinesQuery */
export interface GetSaleLinesQuery {
  invoiceNumber: string;
}

/** MINV.Application.Billing.GetSalesBookQuery */
export interface GetSalesBookQuery {
  month: number;
  year: number;
}

/** MINV.Application.Billing.GetSalesFiscalStatusQuery */
export interface GetSalesFiscalStatusQuery {
  from: string;
  to: string;
}

/** MINV.Application.Sales.GetSalesQuery */
export interface GetSalesQuery {
  from: string;
  to: string;
}

/** MINV.Application.Reports.GetSalesReportQuery */
export interface GetSalesReportQuery {
  from: string;
  to: string;
}

/** MINV.Application.Billing.GetSalesReturnsQuery */
export interface GetSalesReturnsQuery {
  from: string;
  to: string;
}

/** MINV.Application.Sales.GetSellableProductsQuery */
export type GetSellableProductsQuery = Record<string, never>;

/** MINV.Application.Tech.GetSerialSummaryQuery */
export type GetSerialSummaryQuery = Record<string, never>;

/** MINV.Application.Tech.GetSerialTraceQuery */
export interface GetSerialTraceQuery {
  serial: string;
  sku?: string | null;
}

/** MINV.Application.Billing.GetSiatActivitiesQuery */
export type GetSiatActivitiesQuery = Record<string, never>;

/** MINV.Application.Billing.GetSiatCatalogQuery */
export interface GetSiatCatalogQuery {
  catalog: string;
}

/** MINV.Application.Billing.GetSiatServiceCallsQuery */
export interface GetSiatServiceCallsQuery {
  from: string;
  max?: number;
  operation?: string | null;
  to: string;
}

/** MINV.Application.Billing.GetSiatSettingsQuery */
export type GetSiatSettingsQuery = Record<string, never>;

/** MINV.Application.Billing.GetSiatStatusQuery */
export type GetSiatStatusQuery = Record<string, never>;

/** MINV.Application.Billing.GetSignificantEventsQuery */
export interface GetSignificantEventsQuery {
  from: string;
  to: string;
}

/** MINV.Application.Tech.GetSpecDefinitionsQuery */
export interface GetSpecDefinitionsQuery {
  categoryCode?: string | null;
}

/** MINV.Application.Tech.GetSpecFacetsQuery */
export interface GetSpecFacetsQuery {
  categoryCode: string;
}

/** MINV.Application.Inventory.Queries.GetStockProjectionQuery */
export interface GetStockProjectionQuery {
  warehouseCode?: string | null;
}

/** MINV.Application.Inventory.Queries.GetStockReservationsQuery */
export interface GetStockReservationsQuery {
  warehouseCode?: string | null;
}

/** MINV.Application.Storefront.GetStorefrontCatalogQuery */
export type GetStorefrontCatalogQuery = Record<string, never>;

/** MINV.Application.Storefront.GetStorefrontPresetsQuery */
export type GetStorefrontPresetsQuery = Record<string, never>;

/** MINV.Application.Storefront.GetStorefrontProductImageQuery */
export interface GetStorefrontProductImageQuery {
  sku: string;
}

/** MINV.Application.Storefront.GetStorefrontProductQuery */
export interface GetStorefrontProductQuery {
  slug: string;
}

/** MINV.Application.Storefront.GetStorefrontReservationQuery */
export interface GetStorefrontReservationQuery {
  number: string;
  phone: string;
}

/** MINV.Application.Billing.GetSupplierInvoicesQuery */
export interface GetSupplierInvoicesQuery {
  from: string;
  to: string;
}

/** MINV.Application.Partners.GetSuppliersQuery */
export type GetSuppliersQuery = Record<string, never>;

/** MINV.Application.Billing.GetTaxSummaryQuery */
export interface GetTaxSummaryQuery {
  month: number;
  year: number;
}

/** MINV.Application.Tech.GetTechDashboardQuery */
export interface GetTechDashboardQuery {
  consoleCategoryCode?: string;
  days?: number;
  gpuCategoryCode?: string;
}

/** MINV.Application.Inventory.Transfers.GetTransferQuery */
export interface GetTransferQuery {
  id: string;
}

/** MINV.Application.Inventory.Transfers.GetTransfersQuery */
export interface GetTransfersQuery {
  status?: TransferStatus | null;
  take?: number;
}

/** MINV.Application.Iam.GetUsersQuery */
export type GetUsersQuery = Record<string, never>;

/** MINV.Application.Tech.GetWarrantyClaimQuery */
export interface GetWarrantyClaimQuery {
  number: string;
}

/** MINV.Application.Tech.GetWarrantyClaimsQuery */
export interface GetWarrantyClaimsQuery {
  onlyOpen?: boolean;
  status?: WarrantyClaimStatus | null;
}

/** MINV.Application.Tech.GetWarrantyStatusQuery */
export interface GetWarrantyStatusQuery {
  serial: string;
  sku?: string | null;
}

/** MINV.Application.Integration.GetWebhookDeliveriesQuery */
export interface GetWebhookDeliveriesQuery {
  endpointId?: string | null;
  take?: number;
}

/** MINV.Application.Integration.GetWebhooksQuery */
export type GetWebhooksQuery = Record<string, never>;

/** MINV.Application.Inventory.Queries.GetWorkspaceQuery */
export type GetWorkspaceQuery = Record<string, never>;

/** MINV.Application.Billing.GoOfflineCommand */
export interface GoOfflineCommand {
  eventCode?: number | null;
  pointOfSaleId: string;
}

/** MINV.Application.Reports.GroupTotal */
export interface GroupTotal {
  amount: number;
  count: number;
  key: string;
  name: string;
  profit: number;
  quantity: number;
}

/** MINV.Application.Billing.HomologationView */
export interface HomologationView {
  activities: SiatActivityView[];
  paymentMethods: PaymentMethodHomologationRow[];
  pendingProducts: number;
  products: ProductHomologationRow[];
  sinPaymentMethods: SiatCatalogItemView[];
  sinUnits: SiatCatalogItemView[];
  units: UnitHomologationRow[];
}

/** MINV.Application.Accounting.IncomeStatement */
export interface IncomeStatement {
  costs: AccountRow[];
  expenses: AccountRow[];
  from: string;
  grossProfit: number;
  netIncome: number;
  revenue: AccountRow[];
  to: string;
  totalCosts: number;
  totalExpenses: number;
  totalRevenue: number;
}

/** MINV.Application.Integration.IntegrationCatalog */
export interface IntegrationCatalog {
  events: { item1: string; item2: string }[];
  scopes: { item1: string; item2: string }[];
}

/** MINV.Domain.Sales.InvoiceStatus */
export type InvoiceStatus = 'Draft' | 'Issued' | 'Voided';

/** MINV.Application.Tech.IssueWarrantyReplacementCommand */
export interface IssueWarrantyReplacementCommand {
  number: string;
  replacementSerial: string;
  resolution?: string | null;
}

/** MINV.Application.Accounting.JournalEntryRow */
export interface JournalEntryRow {
  date: string;
  description: string;
  lines: JournalLineRow[];
  number: string;
  status: JournalEntryStatus;
  total: number;
}

/** MINV.Domain.Accounting.JournalEntryStatus */
export type JournalEntryStatus = 'Draft' | 'Posted';

/** MINV.Application.Accounting.JournalLineRow */
export interface JournalLineRow {
  accountCode: string;
  accountName: string;
  credit: number;
  debit: number;
  memo: string | null;
}

/** MINV.Application.Common.JournalLineSpec */
export interface JournalLineSpec {
  accountCode: string;
  credit: number;
  debit: number;
  memo?: string | null;
}

/** MINV.Application.Inventory.Queries.KardexLine */
export interface KardexLine {
  balance: number;
  binCode: string;
  businessDate: string;
  document: string | null;
  notes: string | null;
  recordedAt: string;
  signed: number;
  typeCode: string;
  typeName: string;
  userName: string | null;
}

/** MINV.Application.Billing.LinkPointOfSaleRegisterCommand */
export interface LinkPointOfSaleRegisterCommand {
  pointOfSaleId: string;
  registerCode: string | null;
}

/** MINV.Application.Iam.LogoutCommand */
export interface LogoutCommand {
  sessionId: string;
}

/** MINV.Application.Billing.MailSettingsView */
export interface MailSettingsView {
  fromAddress: string;
  fromName: string;
  hasPassword: boolean;
  host: string;
  isEnabled: boolean;
  port: number;
  useSsl: boolean;
  userName: string | null;
}

/** MINV.Application.Tech.MoveWarrantyClaimCommand */
export interface MoveWarrantyClaimCommand {
  next: WarrantyClaimStatus;
  note?: string | null;
  number: string;
  resolution?: string | null;
  supplierCode?: string | null;
}

/** MINV.Domain.Inventory.MovementDomain */
export type MovementDomain = 'Sales' | 'Warehouse';

/** MINV.Application.Reports.MovementReportRow */
export interface MovementReportRow {
  binCode: string;
  date: string;
  document: string | null;
  name: string;
  notes: string | null;
  recordedAt: string;
  signed: number;
  sku: string;
  typeCode: string;
  typeName: string;
  unit: string;
  user: string | null;
}

/** MINV.Application.Inventory.Queries.MovementTrendDay */
export interface MovementTrendDay {
  date: string;
  entries: number;
  issues: number;
  movements: number;
  opening: number;
}

/** MINV.Application.Inventory.Queries.MovementTypeItem */
export interface MovementTypeItem {
  code: string;
  description: string | null;
  domain: MovementDomain;
  isInitialBalance: boolean;
  name: string;
  requiresNotes: boolean;
  stockFactor: number;
}

/** MINV.Application.Accounts.MyAccountView */
export interface MyAccountView {
  complement: string | null;
  customerCode: string;
  documentNumber: string | null;
  documentType: number | null;
  email: string;
  name: string;
  phone: string | null;
}

/** MINV.Application.Tech.NamedAmount */
export interface NamedAmount {
  amount: number;
  name: string;
  quantity: number;
}

/** MINV.Application.Tech.NamedCount */
export interface NamedCount {
  count: number;
  name: string;
}

/** MINV.Application.Billing.NitCheckResult */
export interface NitCheckResult {
  checked: boolean;
  description: string;
  isValid: boolean;
  nit: number;
  siatCode: number | null;
}

/** MINV.Application.Inventory.PhysicalCounts.OpenPhysicalCountCommand */
export interface OpenPhysicalCountCommand {
  countDate?: string | null;
  notes?: string | null;
  warehouseCode: string;
}

/** MINV.Application.Inventory.PhysicalCounts.OpenPhysicalCountResult */
export interface OpenPhysicalCountResult {
  number: string;
  physicalCountId: string;
}

/** MINV.Application.Sales.OpenPosSessionCommand */
export interface OpenPosSessionCommand {
  openingCash: number;
  registerCode: string;
}

/** MINV.Application.Tech.OpenWarrantyClaimCommand */
export interface OpenWarrantyClaimCommand {
  chargeableRepair?: boolean;
  customerCode?: string | null;
  issue: string;
  serial: string;
  sku?: string | null;
}

/** MINV.Application.Catalog.OptionItem */
export interface OptionItem {
  code: string;
  name: string;
}

/** MINV.Domain.Integration.OutgoingMailKind */
export type OutgoingMailKind = 'ReservationConfirmed';

/** MINV.Application.Integration.OutgoingMailRow */
export interface OutgoingMailRow {
  attempts: number;
  branchCode: string;
  completedAt: string | null;
  id: string;
  kind: OutgoingMailKind;
  kindText: string;
  lastAttemptAt: string | null;
  lastError: string | null;
  maxAttempts: number;
  nextAttemptAt: string | null;
  recipient: string;
  requestedAt: string;
  reservation: string;
  reservationKind: PcBuildKind;
  status: OutgoingMailStatus;
  statusText: string;
}

/** MINV.Domain.Integration.OutgoingMailStatus */
export type OutgoingMailStatus = 'Cancelled' | 'Exhausted' | 'Pending' | 'Sent';

/** MINV.Application.Integration.Page<MINV.Application.Integration.ApiProduct> */
export interface PageOfApiProduct {
  items: ApiProduct[];
  pageNumber: number;
  pageSize: number;
  total: number;
}

/** MINV.Application.Integration.Page<MINV.Application.Integration.ApiStock> */
export interface PageOfApiStock {
  items: ApiStock[];
  pageNumber: number;
  pageSize: number;
  total: number;
}

/** MINV.Application.Billing.PaymentMethodHomologationRow */
export interface PaymentMethodHomologationRow {
  code: string;
  name: string;
  paymentMethodId: string;
  sinCode: number | null;
  sinDescription: string | null;
}

/** MINV.Application.Tech.PcBuildCandidate */
export interface PcBuildCandidate {
  brand: string | null;
  imageId: string | null;
  isCompatible: boolean;
  keySpecs: string[];
  name: string;
  price: number;
  reason: string | null;
  sku: string;
  stock: number;
}

/** MINV.Domain.Sales.PcBuildChannel */
export type PcBuildChannel = 'Desktop' | 'Web';

/** MINV.Application.Tech.PcBuildCheckView */
export interface PcBuildCheckView {
  estimatedDrawW: number;
  isCompatible: boolean;
  issues: PcIssue[];
  items: PcBuildItemView[];
  psuW: number | null;
  recommendedPsuW: number;
  total: number;
}

/** MINV.Application.Tech.PcBuildDetail */
export interface PcBuildDetail {
  build: PcBuildRow;
  check: PcBuildCheckView;
  history: PcBuildEventView[] | null;
  quotedItems: PcBuildItemView[];
}

/** MINV.Domain.Sales.PcBuildEventAction */
export type PcBuildEventAction = 'Cancelled' | 'Created' | 'Expired' | 'Published' | 'Quoted' | 'Released' | 'Reserved' | 'Sold' | 'Unpublished';

/** MINV.Application.Tech.PcBuildEventView */
export interface PcBuildEventView {
  action: PcBuildEventAction;
  detail: string;
  occurredAt: string;
  status: PcBuildStatus;
  user: string;
}

/** MINV.Application.Tech.PcBuildItemInput */
export interface PcBuildItemInput {
  quantity?: number;
  sku: string;
  slot: PcSlot | null;
}

/** MINV.Application.Tech.PcBuildItemView */
export interface PcBuildItemView {
  imageId: string | null;
  keySpecs: string[];
  name: string;
  quantity: number;
  sku: string;
  slot: PcSlot | null;
  stock: number;
  subtotal: number;
  unitPrice: number;
}

/** MINV.Domain.Sales.PcBuildKind */
export type PcBuildKind = 'Build' | 'Cart';

/** MINV.Application.Tech.PcBuildRow */
export interface PcBuildRow {
  branchCode: string;
  buyerComplement: string | null;
  buyerDocumentNumber: string | null;
  buyerDocumentType: number | null;
  buyerName: string | null;
  cancelReason: string | null;
  channel: PcBuildChannel;
  contactEmail: string | null;
  contactName: string | null;
  contactPhone: string | null;
  createdAt: string;
  customer: string | null;
  id: string;
  invoiceNumber: string | null;
  isCompatible: boolean;
  isExpired: boolean;
  items: number;
  kind: PcBuildKind;
  name: string;
  notes: string | null;
  number: string;
  publishedToWeb: boolean;
  quotedWithErrors: boolean;
  reserved: number;
  reservedUntil: string | null;
  status: PcBuildStatus;
  total: number;
  validUntil: string;
}

/** MINV.Domain.Sales.PcBuildStatus */
export type PcBuildStatus = 'Cancelled' | 'Draft' | 'Quoted' | 'Reserved' | 'Sold';

/** MINV.Domain.Catalog.PcIssue */
export interface PcIssue {
  code: string;
  isError: boolean;
  message: string;
}

/** MINV.Domain.Catalog.PcSlot */
export type PcSlot = 'Case' | 'Cooler' | 'Cpu' | 'Gpu' | 'Monitor' | 'Motherboard' | 'Peripheral' | 'Psu' | 'Ram' | 'Service' | 'Software' | 'Storage';

/** MINV.Application.Billing.PendingSupplierInvoiceRow */
export interface PendingSupplierInvoiceRow {
  receiptNumber: string;
  receivedOn: string;
  supplier: string;
  supplierCode: string;
  total: number;
}

/** MINV.Application.Inventory.PhysicalCounts.PhysicalCountSheet */
export interface PhysicalCountSheet {
  countDate: string;
  id: string;
  lines: PhysicalCountSheetLine[];
  notes: string | null;
  number: string;
  warehouseCode: string;
}

/** MINV.Application.Inventory.PhysicalCounts.PhysicalCountSheetLine */
export interface PhysicalCountSheetLine {
  binCode: string;
  countedAt: string;
  countedBy: string | null;
  countedQuantity: number;
  difference: number;
  lotNumber: string;
  name: string;
  sku: string;
  systemQuantity: number;
  unit: string;
}

/** MINV.Application.Billing.PosFiscalState */
export interface PosFiscalState {
  billingEnabled: boolean;
  documentTypes: SiatCatalogItemView[];
  message: string;
  mode: SiatConnectionMode | null;
  pendingHomologation: number;
  pointOfSaleCode: number | null;
  ready: boolean;
}

/** MINV.Application.Sales.PosOption */
export interface PosOption {
  code: string;
  name: string;
  opensCashDrawer: boolean;
}

/** MINV.Application.Sales.PosSessionInfo */
export interface PosSessionInfo {
  cashSales: number;
  expectedCash: number;
  id: string;
  openedAt: string;
  openingCash: number;
  registerCode: string;
  registerName: string;
  sales: number;
  tickets: number;
}

/** MINV.Application.Sales.PosState */
export interface PosState {
  branchName: string;
  companyName: string;
  customers: PosOption[];
  paymentMethods: PosOption[];
  registers: PosOption[];
  session: PosSessionInfo | null;
  suggestedRegister: string | null;
  taxId: string | null;
  taxRate: number;
  vatOnInvoicedAmount: boolean;
}

/** MINV.Application.Inventory.PhysicalCounts.PostPhysicalCountCommand */
export interface PostPhysicalCountCommand {
  confirmed: boolean;
  physicalCountId: string;
}

/** MINV.Application.Inventory.PhysicalCounts.PostPhysicalCountResult */
export interface PostPhysicalCountResult {
  initialBalances: number;
  matching: number;
  message: string;
  movements: number;
  number: string;
  shortages: number;
  surpluses: number;
}

/** MINV.Application.Billing.PrepareSiatCommand */
export type PrepareSiatCommand = Record<string, never>;

/** MINV.Application.Inventory.Queries.ProductBinStock */
export interface ProductBinStock {
  available: number;
  binCode: string;
  lotNumber: string;
  onHand: number;
  reserved: number;
}

/** MINV.Application.Inventory.Queries.ProductCard */
export interface ProductCard {
  allowsDecimals: boolean;
  available: number;
  barcodes: string[];
  bins: ProductBinStock[];
  category: string;
  isActive: boolean;
  maximum: number;
  minimum: number;
  movements: KardexLine[];
  name: string;
  onHand: number;
  primaryBin: string | null;
  reserved: number;
  sku: string;
  status: StockStatusCode;
  supplier: string | null;
  totalMovements: number;
  unit: string;
  unitCost: number;
  variantId: string;
}

/** MINV.Application.Billing.ProductHomologationInput */
export interface ProductHomologationInput {
  activityCode: string;
  sinProductCode: number;
  sku: string;
}

/** MINV.Application.Billing.ProductHomologationRow */
export interface ProductHomologationRow {
  activityCode: string | null;
  category: string;
  isActive: boolean;
  name: string;
  productId: string;
  sinProductCode: number | null;
  sinProductDescription: string | null;
  sku: string;
}

/** MINV.Application.Catalog.ProductImageData */
export interface ProductImageData {
  content: string;
  contentType: string;
  sku: string;
  variantId: string;
}

/** MINV.Application.Inventory.Queries.ProductLookupItem */
export interface ProductLookupItem {
  allowsDecimals: boolean;
  barcodes: string[];
  category: string;
  isActive: boolean;
  name: string;
  primaryBin: string | null;
  sku: string;
  unit: string;
  variantId: string;
}

/** MINV.Application.Tech.ProductSpecInput */
export interface ProductSpecInput {
  code: string;
  values: string[];
}

/** MINV.Application.Tech.ProductSpecView */
export interface ProductSpecView {
  code: string;
  compatibilityKey: string | null;
  dataType: SpecDataType;
  display: string;
  isRequired: boolean;
  name: string;
  unit: string | null;
  values: string[];
}

/** MINV.Application.Tech.ProductTechView */
export interface ProductTechView {
  brand: string | null;
  category: string;
  name: string;
  serialKind: SerialKind;
  serialsInStock: number;
  sku: string;
  specs: ProductSpecView[];
  trackSerials: boolean;
  warrantyMonths: number;
}

/** MINV.Application.Tech.PublishPcBuildCommand */
export interface PublishPcBuildCommand {
  number: string;
  published?: boolean;
}

/** MINV.Application.Purchasing.PurchaseLineInput */
export interface PurchaseLineInput {
  quantity: number;
  sku: string;
  unitCost: number;
}

/** MINV.Application.Purchasing.PurchaseOrderDetail */
export interface PurchaseOrderDetail {
  lines: PurchaseOrderLineRow[];
  order: PurchaseOrderRow;
  receipts: string[];
}

/** MINV.Application.Purchasing.PurchaseOrderLineRow */
export interface PurchaseOrderLineRow {
  lineId: string;
  name: string;
  quantity: number;
  received: number;
  sku: string;
  subtotal: number;
  unit: string;
  unitCost: number;
}

/** MINV.Application.Purchasing.PurchaseOrderRow */
export interface PurchaseOrderRow {
  expectedDate: string | null;
  id: string;
  lines: number;
  notes: string | null;
  number: string;
  orderDate: string;
  receivedPercent: number;
  status: PurchaseOrderStatus;
  supplier: string;
  supplierCode: string;
  total: number;
}

/** MINV.Domain.Purchasing.PurchaseOrderStatus */
export type PurchaseOrderStatus = 'Approved' | 'Cancelled' | 'Draft' | 'PartiallyReceived' | 'Received';

/** MINV.Application.Billing.PurchasesBookRow */
export interface PurchasesBookRow {
  authorizationCode: string;
  branchCode: string;
  controlCode: string;
  date: string;
  discounts: number;
  exempt: number;
  fees: number;
  giftCard: number;
  ice: number;
  iehd: number;
  invoiceNumber: string;
  ipj: number;
  otherNotSubject: number;
  purchaseType: string;
  row: number;
  subtotal: number;
  supplierName: string;
  supplierNit: string;
  taxBase: number;
  taxCredit: number;
  total: number;
  zeroRate: number;
}

/** MINV.Application.Billing.PurchasesBookView */
export interface PurchasesBookView {
  month: number;
  rows: PurchasesBookRow[];
  taxBase: number;
  taxCredit: number;
  total: number;
  year: number;
}

/** MINV.Application.Reports.PurchasesReport */
export interface PurchasesReport {
  byDay: SeriesPoint[];
  bySupplier: GroupTotal[];
  openAmount: number;
  openOrders: number;
  receipts: number;
  received: number;
}

/** MINV.Application.Abstractions.ReceiptLine */
export interface ReceiptLine {
  amount: number;
  description: string;
  quantity: number;
  serialsText: string | null;
  unitPrice: number;
  warrantyUntil: string | null;
}

/** MINV.Application.Purchasing.ReceiptResult */
export interface ReceiptResult {
  journalNumber: string;
  lines: number;
  orderNumber: string;
  receiptNumber: string;
  total: number;
}

/** MINV.Application.Purchasing.ReceivePurchaseOrderCommand */
export interface ReceivePurchaseOrderCommand {
  id: string;
  serials?: SkuSerials[] | null;
  supplierDocument?: string | null;
}

/** MINV.Application.Inventory.Transfers.ReceiveTransferCommand */
export interface ReceiveTransferCommand {
  id: string;
  lines?: TransferReceiptInput[] | null;
}

/** MINV.Application.Inventory.Queries.RecentMovement */
export interface RecentMovement {
  binCode: string;
  businessDate: string;
  document: string | null;
  name: string;
  quantity: number;
  recordedAt: string;
  sku: string;
  stockFactor: number;
  typeCode: string;
  typeName: string;
  unit: string;
  userName: string | null;
}

/** MINV.Application.Inventory.PhysicalCounts.RecordCountCommand */
export interface RecordCountCommand {
  binCode: string;
  countedQuantity: number;
  lotNumber?: string | null;
  physicalCountId: string;
  sku: string;
}

/** MINV.Application.Billing.RecordFiscalDeliveryCommand */
export interface RecordFiscalDeliveryCommand {
  channel: FiscalDeliveryChannel;
  documentId: string;
  recipient?: string | null;
}

/** MINV.Application.Billing.RecoverPointOfSaleCommand */
export interface RecoverPointOfSaleCommand {
  pointOfSaleId: string;
}

/** MINV.Application.Billing.RegisterContingencyCodeCommand */
export interface RegisterContingencyCodeCommand {
  branchCode: string;
  code: string;
  documentSector: number;
  numberFrom: number;
  numberTo: number;
  validUntil: string | null;
}

/** MINV.Application.Inventory.Movements.RegisterMovementCommand */
export interface RegisterMovementCommand {
  adjustmentReasonCode?: string | null;
  binCode: string;
  businessDate?: string | null;
  documentReference?: string | null;
  lotNumber?: string | null;
  movementTypeCode: string;
  notes?: string | null;
  quantity: number;
  serials?: string[] | null;
  sku: string;
}

/** MINV.Application.Inventory.Movements.RegisterMovementResult */
export interface RegisterMovementResult {
  available: number;
  message: string;
  movementId: string;
  quantityOnHand: number;
  stockLevelId: string;
}

/** MINV.Application.Billing.RegisterSiatPointOfSaleCommand */
export interface RegisterSiatPointOfSaleCommand {
  branchCode: string;
  description: string | null;
  name: string;
  registerCode: string | null;
  typeCode?: number;
}

/** MINV.Application.Tech.RegisterStockSerialsCommand */
export interface RegisterStockSerialsCommand {
  note?: string | null;
  serials: string[];
  sku: string;
}

/** MINV.Application.Billing.RegisterSupplierInvoiceCommand */
export interface RegisterSupplierInvoiceCommand {
  authorizationCode: string;
  controlCode?: string | null;
  discounts?: number;
  invoiceDate: string;
  invoiceNumber: string;
  notSubjectToVat?: number;
  purchaseType?: number;
  receiptNumber: string;
  totalAmount: number;
}

/** MINV.Application.Billing.ReissueFiscalDocumentCommand */
export interface ReissueFiscalDocumentCommand {
  buyer?: FiscalBuyerInput | null;
  documentId: string;
}

/** MINV.Application.Tech.ReleasePcBuildReservationCommand */
export interface ReleasePcBuildReservationCommand {
  number: string;
  reason: string;
}

/** MINV.Application.Inventory.PhysicalCounts.RemoveCountCommand */
export interface RemoveCountCommand {
  binCode: string;
  lotNumber?: string | null;
  physicalCountId: string;
  sku: string;
}

/** MINV.Application.Catalog.RemoveProductImageCommand */
export interface RemoveProductImageCommand {
  sku: string;
}

/** MINV.Application.Billing.RenderFiscalDocumentQuery */
export interface RenderFiscalDocumentQuery {
  columns?: number;
  documentId: string;
  format?: FiscalDeliveryChannel;
}

/** MINV.Application.Billing.RequestCufdCommand */
export interface RequestCufdCommand {
  pointOfSaleId: string;
}

/** MINV.Application.Billing.RequestCuisCommand */
export interface RequestCuisCommand {
  pointOfSaleId: string;
}

/** MINV.Application.Integration.ResendReservationMailCommand */
export interface ResendReservationMailCommand {
  email?: string | null;
  number: string;
}

/** MINV.Application.Storefront.ReservationBuyerInput */
export interface ReservationBuyerInput {
  complement?: string | null;
  documentNumber: string;
  documentType: number;
  name?: string | null;
}

/** MINV.Application.Tech.ReserveCartCommand */
export interface ReserveCartCommand {
  buyer?: ReservationBuyerInput | null;
  contactEmail?: string | null;
  contactName: string;
  contactPhone: string;
  customerCode?: string | null;
  holdDays?: number | null;
  items: CartItemInput[];
  name?: string | null;
  notes?: string | null;
}

/** MINV.Application.Tech.ReservePcBuildCommand */
export interface ReservePcBuildCommand {
  hours?: number;
  number: string;
}

/** MINV.Application.Inventory.Queries.ReservedStockRow */
export interface ReservedStockRow {
  reserved: number;
  sku: string;
}

/** MINV.Application.Iam.ResetUserPasswordCommand */
export interface ResetUserPasswordCommand {
  email: string;
  mustChange?: boolean;
  newPassword: string;
}

/** MINV.Application.Billing.ReturnLineInput */
export interface ReturnLineInput {
  quantity: number;
  serials?: string[] | null;
  sku: string;
}

/** MINV.Application.Billing.ReturnableLine */
export interface ReturnableLine {
  discountPercent: number;
  name: string;
  returned: number;
  sku: string;
  sold: number;
  unit: string;
  unitPrice: number;
}

/** MINV.Application.Billing.RevertFiscalVoidCommand */
export interface RevertFiscalVoidCommand {
  documentId: string;
}

/** MINV.Application.Integration.RevokeApiKeyCommand */
export interface RevokeApiKeyCommand {
  id: string;
}

/** MINV.Application.Iam.RoleRow */
export interface RoleRow {
  code: string;
  name: string;
  permissions: string[];
  users: number;
}

/** MINV.Application.Iam.RolesView */
export interface RolesView {
  permissions: { item1: string; item2: string }[];
  roles: RoleRow[];
}

/** MINV.Application.Integration.RotateWebhookSecretCommand */
export interface RotateWebhookSecretCommand {
  id: string;
}

/** MINV.Application.Remote.RpcError */
export interface RpcError {
  code: string | null;
  errors: string[] | null;
  kind: string;
  message: string;
}

/** MINV.Application.Remote.RpcRequest */
export interface RpcRequest {
  payload: unknown;
  requestId: string;
  type: string;
}

/** MINV.Application.Remote.RpcResponse */
export interface RpcResponse {
  error: RpcError | null;
  ok: boolean;
  replayed: boolean;
  result: unknown;
}

/** MINV.Application.Billing.RunSiatWorkCommand */
export interface RunSiatWorkCommand {
  maintain?: boolean;
}

/** MINV.Application.Billing.SaleFiscalStatusRow */
export interface SaleFiscalStatusRow {
  canCreditNote: boolean;
  canVoid: boolean;
  cuf: string;
  documentId: string;
  emissionType: number;
  invoiceNumber: string;
  isReverted: boolean;
  number: number;
  status: FiscalDocumentStatus;
  voidDeadline: string;
}

/** MINV.Application.Sales.SaleLineInput */
export interface SaleLineInput {
  discountPercent?: number;
  quantity: number;
  serials?: string[] | null;
  sku: string;
}

/** MINV.Application.Sales.SaleLineRow */
export interface SaleLineRow {
  amount: number;
  discountPercent: number;
  name: string;
  quantity: number;
  serials: string[] | null;
  sku: string;
  unit: string;
  unitPrice: number;
}

/** MINV.Application.Sales.SaleRow */
export interface SaleRow {
  cashier: string;
  customer: string;
  customerCode: string;
  date: string;
  invoiceNumber: string;
  issuedAt: string;
  items: number;
  orderNumber: string;
  paymentMethod: string;
  status: InvoiceStatus;
  tax: number;
  total: number;
  voidReason: string | null;
}

/** MINV.Application.Billing.SalesBookRow */
export interface SalesBookRow {
  branchCode: string;
  buyerDocument: string;
  buyerName: string;
  complement: string | null;
  controlCode: string;
  cuf: string;
  date: string;
  discounts: number;
  documentSector: number;
  exports: number;
  fees: number;
  giftCard: number;
  ice: number;
  iehd: number;
  ipj: number;
  number: number;
  otherNotSubject: number;
  row: number;
  status: string;
  subtotal: number;
  taxBase: number;
  taxDebit: number;
  total: number;
  zeroRate: number;
}

/** MINV.Application.Billing.SalesBookView */
export interface SalesBookView {
  month: number;
  rows: SalesBookRow[];
  taxBase: number;
  taxDebit: number;
  total: number;
  valid: number;
  voided: number;
  year: number;
}

/** MINV.Application.Reports.SalesReport */
export interface SalesReport {
  averageTicket: number;
  byCashier: GroupTotal[];
  byCategory: GroupTotal[];
  byCustomer: GroupTotal[];
  byDay: SeriesPoint[];
  byPaymentMethod: GroupTotal[];
  byProduct: GroupTotal[];
  cost: number;
  from: string;
  grossProfit: number;
  marginPercent: number;
  netRevenue: number;
  revenue: number;
  tax: number;
  tickets: number;
  to: string;
  units: number;
  voided: number;
}

/** MINV.Application.Billing.SalesReturnResult */
export interface SalesReturnResult {
  creditNoteId: string | null;
  creditNoteNumber: number | null;
  creditNoteStatus: FiscalDocumentStatus | null;
  message: string;
  number: string;
  refund: number;
}

/** MINV.Application.Billing.SalesReturnRow */
export interface SalesReturnRow {
  creditNote: string | null;
  creditNoteStatus: FiscalDocumentStatus | null;
  customer: string;
  invoiceNumber: string;
  number: string;
  reason: string;
  refund: number;
  returnedAt: string;
}

/** MINV.Application.Catalog.SaveCategoryCommand */
export interface SaveCategoryCommand {
  code: string;
  name: string;
  parentCode?: string | null;
}

/** MINV.Application.Partners.SaveCustomerCommand */
export interface SaveCustomerCommand {
  categoryCode: string;
  code: string | null;
  email: string | null;
  isActive: boolean;
  name: string;
  phone: string | null;
  taxId: string | null;
}

/** MINV.Application.Billing.SaveCustomerFiscalIdentityCommand */
export interface SaveCustomerFiscalIdentityCommand {
  code: string;
  complement: string | null;
  documentNumber: string | null;
  documentType: number | null;
}

/** MINV.Application.Billing.SaveMailSettingsCommand */
export interface SaveMailSettingsCommand {
  enabled: boolean;
  fromAddress: string;
  fromName: string;
  host: string;
  newPassword: string | null;
  port: number;
  useSsl: boolean;
  userName: string | null;
}

/** MINV.Application.Billing.SavePaymentMethodHomologationCommand */
export interface SavePaymentMethodHomologationCommand {
  paymentMethodCode: string;
  sinCode: number;
}

/** MINV.Application.Tech.SavePcBuildCommand */
export interface SavePcBuildCommand {
  acceptIncompatible?: boolean;
  customerCode: string | null;
  id: string | null;
  items: PcBuildItemInput[];
  kind?: PcBuildKind;
  name: string;
  quote?: boolean;
  validDays?: number;
}

/** MINV.Application.Catalog.SaveProductCommand */
export interface SaveProductCommand {
  barcode: string | null;
  binCode?: string | null;
  categoryCode: string;
  description: string | null;
  isActive: boolean;
  maximum: number;
  minimum: number;
  name: string;
  originalSku: string | null;
  salePrice: number;
  sku: string;
  supplierCode: string | null;
  unitCode: string;
  unitCost: number;
}

/** MINV.Application.Billing.SaveProductHomologationCommand */
export interface SaveProductHomologationCommand {
  items: ProductHomologationInput[];
}

/** MINV.Application.Tech.SaveProductTechCommand */
export interface SaveProductTechCommand {
  serialKind: SerialKind;
  sku: string;
  specs: ProductSpecInput[];
  trackSerials: boolean;
  warrantyMonths: number;
}

/** MINV.Application.Billing.SaveSiatBranchCommand */
export interface SaveSiatBranchCommand {
  branchCode: string;
  municipality: string;
  phone: string | null;
  siatCode: number;
}

/** MINV.Application.Billing.SaveSiatProfileCommand */
export interface SaveSiatProfileCommand {
  endpoints: SiatEndpointSet;
  environment: number;
  newToken: string | null;
  qrBaseUrl: string;
  timeoutSeconds: number;
  tokenValidUntil: string | null;
}

/** MINV.Application.Billing.SaveSiatSettingsCommand */
export interface SaveSiatSettingsCommand {
  businessName: string;
  enabled: boolean;
  environment: number;
  nit: number;
  offlineLegend: string | null;
  onlineLegend: string | null;
  systemCode: string;
}

/** MINV.Application.Tech.SaveSpecDefinitionCommand */
export interface SaveSpecDefinitionCommand {
  categoryCode: string;
  code: string;
  compatibilityKey: string | null;
  dataType: SpecDataType;
  isFilterable: boolean;
  isMultiValued: boolean;
  isRequired: boolean;
  name: string;
  options: string[];
  sortOrder: number;
  unit: string | null;
}

/** MINV.Application.Partners.SaveSupplierCommand */
export interface SaveSupplierCommand {
  code: string | null;
  contactName: string | null;
  email: string | null;
  isActive: boolean;
  leadTimeDays: number;
  name: string;
  phone: string | null;
  taxId: string | null;
}

/** MINV.Application.Billing.SaveUnitHomologationCommand */
export interface SaveUnitHomologationCommand {
  sinUnitCode: number;
  unitCode: string;
}

/** MINV.Application.Iam.SaveUserCommand */
export interface SaveUserCommand {
  branchCodes?: string[] | null;
  email: string;
  isActive: boolean;
  name: string;
  newPassword: string | null;
  originalEmail: string | null;
  roleCode: string;
}

/** MINV.Application.Tech.SearchSerialsQuery */
export interface SearchSerialsQuery {
  max?: number;
  sku?: string | null;
  status?: SerialNumberStatus | null;
  text?: string | null;
}

/** MINV.Application.Billing.SearchSiatProductsQuery */
export interface SearchSiatProductsQuery {
  activityCode: string | null;
  max?: number;
  text: string | null;
}

/** MINV.Application.Tech.SearchTechProductsQuery */
export interface SearchTechProductsQuery {
  categoryCode?: string | null;
  filters?: SpecFilter[] | null;
  max?: number;
  onlyInStock?: boolean;
  platform?: string | null;
  text?: string | null;
}

/** MINV.Application.Iam.SelectBranchCommand */
export interface SelectBranchCommand {
  branchId: string | null;
  sessionId: string;
}

/** MINV.Application.Tech.SellPcBuildCommand */
export interface SellPcBuildCommand {
  buyer?: FiscalBuyerInput | null;
  cardNumber?: string | null;
  cashReceived?: number | null;
  customerCode?: string | null;
  number: string;
  paymentMethodCode: string;
  paymentReference?: string | null;
  serials?: SkuSerials[] | null;
}

/** MINV.Application.Sales.SellableProduct */
export interface SellableProduct {
  allowsDecimals: boolean;
  available: number;
  barcodes: string[];
  category: string;
  categoryCode: string;
  name: string;
  price: number;
  sku: string;
  unit: string;
  variantId: string;
}

/** MINV.Application.Billing.SendFiscalDocumentEmailCommand */
export interface SendFiscalDocumentEmailCommand {
  documentId: string;
  email?: string | null;
}

/** MINV.Application.Tech.SerialDisposal */
export type SerialDisposal = 'ReturnToSupplier' | 'Scrap';

/** MINV.Domain.Inventory.SerialEventAction */
export type SerialEventAction = 'Adjusted' | 'Received' | 'Repaired' | 'Replaced' | 'ReplacementIssued' | 'Restocked' | 'Returned' | 'ReturnedToCustomer' | 'ReturnedToSupplier' | 'RmaReceived' | 'Scrapped' | 'SentToSupplier' | 'Sold' | 'TransferDispatched' | 'TransferReceived';

/** MINV.Application.Tech.SerialEventView */
export interface SerialEventView {
  action: SerialEventAction;
  branch: string | null;
  documentNumber: string | null;
  note: string | null;
  occurredAt: string;
  user: string | null;
}

/** MINV.Domain.Catalog.SerialKind */
export type SerialKind = 'Imei' | 'Serial';

/** MINV.Domain.Inventory.SerialNumberStatus */
export type SerialNumberStatus = 'InRma' | 'InStock' | 'InTransit' | 'Reserved' | 'Returned' | 'ReturnedToSupplier' | 'Scrapped' | 'Sold';

/** MINV.Application.Tech.SerialRow */
export interface SerialRow {
  branch: string | null;
  customer: string | null;
  invoiceNumber: string | null;
  kind: SerialKind;
  product: string;
  receivedAt: string | null;
  serial: string;
  sku: string;
  soldAt: string | null;
  status: SerialNumberStatus;
  warehouse: string | null;
  warrantyUntil: string | null;
}

/** MINV.Application.Tech.SerialSummaryView */
export interface SerialSummaryView {
  inRmaOrReturned: number;
  inStock: number;
  inStockProducts: number;
  out: number;
  sold: number;
  soldInWarranty: number;
  total: number;
}

/** MINV.Application.Tech.SerialTraceView */
export interface SerialTraceView {
  claims: WarrantyClaimRow[];
  events: SerialEventView[];
  inWarranty: boolean;
  receiptNumber: string | null;
  serial: SerialRow;
  supplier: string | null;
  warrantyMonths: number;
}

/** MINV.Application.Reports.SeriesPoint */
export interface SeriesPoint {
  amount: number;
  count: number;
  date: string;
}

/** MINV.Application.Catalog.SetProductImageCommand */
export interface SetProductImageCommand {
  content: string;
  contentType: string;
  fileName: string | null;
  sku: string;
}

/** MINV.Application.Billing.SiatActivityView */
export interface SiatActivityView {
  activityType: string | null;
  code: string;
  description: string;
  isCurrent: boolean;
  sectors: number[];
}

/** MINV.Application.Billing.SiatAlert */
export interface SiatAlert {
  deadline: string | null;
  detail: string;
  severity: string;
  title: string;
}

/** MINV.Application.Billing.SiatBranchView */
export interface SiatBranchView {
  branchCode: string;
  branchId: string;
  branchName: string;
  municipality: string | null;
  phone: string | null;
  siatCode: number | null;
}

/** MINV.Application.Billing.SiatCatalogItemView */
export interface SiatCatalogItemView {
  catalog: string;
  code: number;
  description: string;
  isCurrent: boolean;
}

/** MINV.Domain.Billing.SiatConnectionMode */
export type SiatConnectionMode = 'ManualContingency' | 'Offline' | 'Online' | 'Recovering';

/** MINV.Domain.Billing.SiatEndpointSet */
export interface SiatEndpointSet {
  adjustment: string;
  codes: string;
  computerized: string;
  namespace: string;
  operations: string;
  purchaseSale: string;
  sync: string;
}

/** MINV.Application.Billing.SiatMaintenanceResult */
export interface SiatMaintenanceResult {
  cufdRequested: number;
  cuisRequested: number;
  documentsSent: number;
  messages: string[];
  packagesValidated: number;
  recovered: number;
}

/** MINV.Application.Billing.SiatPointOfSaleStatus */
export interface SiatPointOfSaleStatus {
  branchCode: string;
  branchId: string;
  branchName: string;
  code: number;
  cufdObtainedAt: string | null;
  cufdValidUntil: string | null;
  cuisValidUntil: string | null;
  environment: number;
  id: string;
  isClosed: boolean;
  lastContactAt: string | null;
  lastError: string | null;
  mode: SiatConnectionMode;
  modeSince: string;
  name: string;
  offlineDocuments: number;
  openEvent: SignificantEventRow | null;
  pendingDocuments: number;
  registerCode: string | null;
  retryAt: string | null;
  siatBranchCode: number;
}

/** MINV.Application.Billing.SiatProductView */
export interface SiatProductView {
  activityCode: string;
  description: string;
  isCurrent: boolean;
  productCode: number;
}

/** MINV.Application.Billing.SiatProfileView */
export interface SiatProfileView {
  endpoints: SiatEndpointSet;
  environment: number;
  hasToken: boolean;
  qrBaseUrl: string;
  timeoutSeconds: number;
  tokenUpdatedAt: string | null;
  tokenValidUntil: string | null;
}

/** MINV.Application.Billing.SiatServiceCallRow */
export interface SiatServiceCallRow {
  durationMs: number;
  error: string | null;
  httpStatus: number | null;
  occurredAt: string;
  operation: string;
  pointOfSaleCode: number | null;
  requestBody: string | null;
  resource: string;
  responseBody: string | null;
  siatCode: number | null;
  succeeded: boolean;
}

/** MINV.Application.Billing.SiatSettingsView */
export interface SiatSettingsView {
  branches: SiatBranchView[];
  businessName: string | null;
  clockOffsetMs: number;
  clockSyncedAt: string | null;
  configured: boolean;
  environment: number;
  isEnabled: boolean;
  mail: MailSettingsView | null;
  moduleActive: boolean;
  nit: number | null;
  offlineLegend: string;
  onlineLegend: string;
  profiles: SiatProfileView[];
  systemCode: string | null;
}

/** MINV.Application.Billing.SiatStatusView */
export interface SiatStatusView {
  alerts: SiatAlert[];
  billedToday: number;
  businessName: string | null;
  clockSyncedAt: string | null;
  configured: boolean;
  documentsToday: number;
  enabled: boolean;
  environment: number;
  hasToken: boolean;
  lastCatalogSync: string | null;
  nit: number | null;
  offlineDocuments: number;
  openEvents: number;
  pendingDocuments: number;
  points: SiatPointOfSaleStatus[];
  tokenValidUntil: string | null;
}

/** MINV.Application.Billing.SiatSyncResult */
export interface SiatSyncResult {
  catalogs: number;
  clockSyncedAt: string | null;
  errors: string[];
  items: number;
}

/** MINV.Application.Billing.SiatWorkResult */
export interface SiatWorkResult {
  dispatch: DispatchResult;
  maintenance: SiatMaintenanceResult | null;
}

/** MINV.Domain.Billing.SignificantEventKind */
export type SignificantEventKind = 'ManualCafc' | 'Offline';

/** MINV.Application.Billing.SignificantEventRow */
export interface SignificantEventRow {
  branchCode: string;
  branchId: string;
  cafc: string | null;
  description: string;
  documents: number;
  endedAt: string | null;
  eventCode: number;
  id: string;
  kind: SignificantEventKind;
  pointOfSaleCode: number;
  receptionCode: string | null;
  registrationDeadline: string | null;
  startedAt: string;
  status: SignificantEventStatus;
  transcriptionDeadline: string | null;
}

/** MINV.Domain.Billing.SignificantEventStatus */
export type SignificantEventStatus = 'Closed' | 'Open' | 'PackagesSent' | 'Reconciled' | 'Registered' | 'WithObservations';

/** MINV.Application.Tech.SkuSerials */
export interface SkuSerials {
  serials: string[];
  sku: string;
}

/** MINV.Domain.Catalog.SpecDataType */
export type SpecDataType = 'Number' | 'Option' | 'Text';

/** MINV.Application.Tech.SpecDefinitionView */
export interface SpecDefinitionView {
  categoryCode: string;
  categoryName: string;
  code: string;
  compatibilityKey: string | null;
  dataType: SpecDataType;
  id: string;
  isFilterable: boolean;
  isInherited: boolean;
  isMultiValued: boolean;
  isRequired: boolean;
  name: string;
  options: string[];
  sortOrder: number;
  unit: string | null;
}

/** MINV.Application.Tech.SpecFacet */
export interface SpecFacet {
  code: string;
  dataType: SpecDataType;
  max: number | null;
  min: number | null;
  name: string;
  unit: string | null;
  values: SpecFacetValue[];
}

/** MINV.Application.Tech.SpecFacetValue */
export interface SpecFacetValue {
  count: number;
  value: string;
}

/** MINV.Application.Tech.SpecFilter */
export interface SpecFilter {
  code: string;
  max?: number | null;
  min?: number | null;
  values?: string[] | null;
}

/** MINV.Application.Billing.StartManualContingencyCommand */
export interface StartManualContingencyCommand {
  cafcCode: string;
  description: string | null;
  eventCode: number;
  pointOfSaleId: string;
  startedAt: string | null;
}

/** MINV.Domain.Inventory.StockProjectionResult */
export interface StockProjectionResult {
  alerts: AlertRow[];
  movements: number;
  order: SuggestedOrderLine[];
  stock: StockRow[];
}

/** MINV.Application.Inventory.Queries.StockProjectionView */
export interface StockProjectionView {
  result: StockProjectionResult;
  today: string;
  warehouseCode: string;
}

/** MINV.Domain.Inventory.StockRow */
export interface StockRow {
  category: string;
  coverageDays: number | null;
  daysWithoutMovement: number | null;
  entries: number;
  inventoryValue: number;
  isActive: boolean;
  issues: number;
  lastMovement: string | null;
  level: number | null;
  maximum: number;
  minimum: number;
  name: string;
  sales30Days: number;
  salesRank: number | null;
  sku: string;
  status: StockStatusCode;
  stock: number;
  supplier: string;
  unit: string;
  unitCost: number;
  variantId: string;
}

/** MINV.Domain.Inventory.StockStatusCode */
export type StockStatusCode = 'Critical' | 'Inactive' | 'Inconsistent' | 'Low' | 'Optimal' | 'OutOfStock' | 'Overstock';

/** MINV.Application.Storefront.StorefrontBranch */
export interface StorefrontBranch {
  code: string;
  name: string;
}

/** MINV.Application.Storefront.StorefrontBrand */
export interface StorefrontBrand {
  code: string;
  name: string;
  productCount: number;
}

/** MINV.Application.Storefront.StorefrontCatalogView */
export interface StorefrontCatalogView {
  branch: StorefrontBranch;
  brands: StorefrontBrand[];
  categories: StorefrontCategory[];
  company: StorefrontCompany;
  generatedAt: string;
  maxHoldDays: number;
  presets: StorefrontPreset[];
  products: StorefrontProduct[];
  reservationHours: number;
}

/** MINV.Application.Storefront.StorefrontCategory */
export interface StorefrontCategory {
  code: string;
  description: string;
  icon: string;
  name: string;
  parent: string | null;
  productCount: number;
  slug: string;
}

/** MINV.Application.Storefront.StorefrontCompany */
export interface StorefrontCompany {
  branches: StorefrontBranch[];
  code: string;
  name: string;
}

/** MINV.Application.Storefront.StorefrontContactInput */
export interface StorefrontContactInput {
  email?: string | null;
  name: string;
  phone: string;
}

/** MINV.Application.Storefront.StorefrontImage */
export interface StorefrontImage {
  content: string;
  contentType: string;
  eTag: string;
}

/** MINV.Application.Storefront.StorefrontPreset */
export interface StorefrontPreset {
  available: boolean;
  id: string;
  lines: StorefrontPresetLine[];
  name: string;
  number: string;
  tier: string;
  total: number;
}

/** MINV.Application.Storefront.StorefrontPresetLine */
export interface StorefrontPresetLine {
  quantity: number;
  sku: string;
  slot: string;
  unitPrice: number;
}

/** MINV.Application.Storefront.StorefrontProduct */
export interface StorefrontProduct {
  available: number;
  brand: string;
  category: string;
  categoryName: string;
  categoryPath: string;
  condition: string;
  description: string;
  highlights: string[];
  image: string | null;
  listPrice: number | null;
  name: string;
  onHand: number;
  popularity: number;
  price: number;
  reserved: number;
  serialized: boolean;
  shortName: string;
  sku: string;
  slug: string;
  specs: StorefrontSpec[];
  tags: string[];
  warrantyMonths: number;
}

/** MINV.Application.Storefront.StorefrontReservationLine */
export interface StorefrontReservationLine {
  name: string;
  quantity: number;
  sku: string;
  slot: string | null;
  subtotal: number;
  unitPrice: number;
}

/** MINV.Application.Storefront.StorefrontReservationLineInput */
export interface StorefrontReservationLineInput {
  quantity?: number;
  sku: string;
  slot?: string | null;
}

/** MINV.Application.Storefront.StorefrontReservationResult */
export interface StorefrontReservationResult {
  replayed: boolean;
  reservation: StorefrontReservationView;
}

/** MINV.Application.Storefront.StorefrontReservationView */
export interface StorefrontReservationView {
  branch: string;
  cancelReason: string | null;
  contactName: string;
  createdAt: string;
  hasCompatibilityWarnings: boolean;
  kind: string;
  lines: StorefrontReservationLine[];
  mailQueued: boolean;
  notes: string | null;
  number: string;
  reservedUntil: string | null;
  status: string;
  statusText: string;
  total: number;
}

/** MINV.Application.Storefront.StorefrontSpec */
export interface StorefrontSpec {
  filterable: boolean;
  key: string;
  label: string;
  text: string;
  unit: string | null;
  value: unknown;
}

/** MINV.Application.Billing.SuggestProductHomologationQuery */
export interface SuggestProductHomologationQuery {
  activityCode: string;
}

/** MINV.Domain.Inventory.SuggestedOrderLine */
export interface SuggestedOrderLine {
  contact: string;
  email: string;
  estimatedDelivery: string | null;
  leadTimeDays: number | null;
  maximum: number;
  minimum: number;
  name: string;
  phone: string;
  quantityToOrder: number;
  sku: string;
  status: StockStatusCode;
  stock: number;
  subtotal: number;
  supplier: string;
  unit: string;
  unitCost: number;
}

/** MINV.Application.Billing.SupplierInvoiceRow */
export interface SupplierInvoiceRow {
  authorizationCode: string;
  branchCode: string;
  id: string;
  invoiceDate: string;
  number: string;
  receiptNumber: string | null;
  status: string;
  supplier: string;
  supplierCode: string;
  supplierNit: string | null;
  taxBase: number;
  taxCredit: number;
  totalAmount: number;
}

/** MINV.Application.Partners.SupplierRow */
export interface SupplierRow {
  code: string;
  contact: string | null;
  email: string | null;
  isActive: boolean;
  leadTimeDays: number;
  name: string;
  openOrders: number;
  phone: string | null;
  products: number;
  purchased: number;
  taxId: string | null;
}

/** MINV.Application.Billing.SyncSiatCatalogsCommand */
export interface SyncSiatCatalogsCommand {
  catalog?: string | null;
}

/** MINV.Application.Billing.TaxSummaryView */
export interface TaxSummaryView {
  creditNotes: number;
  grossSales: number;
  invoices: number;
  month: number;
  notes: number;
  taxCreditNotes: number;
  taxCreditPurchases: number;
  taxDebit: number;
  transactionTax: number;
  transactionTaxBase: number;
  vatCarryForward: number;
  vatPayable: number;
  voidedInvoices: number;
  year: number;
}

/** MINV.Application.Tech.TechDashboardView */
export interface TechDashboardView {
  buildsSold: number;
  buildsSoldValue: number;
  claimsOutOfWarranty: number;
  openClaims: number;
  openClaimsByStatus: NamedCount[];
  quotesOpen: number;
  quotesValue: number;
  salesByCategory: NamedAmount[];
  salesByPlatform: NamedAmount[];
  serializedWithoutSerials: number;
  serialsInStock: number;
  serialsInStockByCategory: NamedCount[];
  topConsoles: NamedAmount[];
  topGpus: NamedAmount[];
  webReservationsActive: number;
  webReservationsValue: number;
}

/** MINV.Application.Tech.TechProductRow */
export interface TechProductRow {
  brand: string | null;
  category: string;
  categoryCode: string;
  imageId: string | null;
  keySpecs: string;
  name: string;
  platforms: string[];
  price: number;
  serialKind: SerialKind;
  sku: string;
  stock: number;
  trackSerials: boolean;
  warrantyMonths: number;
}

/** MINV.Application.Billing.TranscribeManualInvoiceCommand */
export interface TranscribeManualInvoiceCommand {
  buyer: FiscalBuyerInput;
  issuedAt: string;
  lines: SaleLineInput[];
  number: number;
  paymentMethodCode: string;
  significantEventId: string;
}

/** MINV.Application.Inventory.Transfers.TransferDetail */
export interface TransferDetail {
  header: TransferRow;
  history: TransferHistoryRow[];
  lines: TransferLineRow[];
}

/** MINV.Application.Inventory.Transfers.TransferHistoryRow */
export interface TransferHistoryRow {
  detail: string;
  occurredAt: string;
  statusLabel: string;
  user: string;
}

/** MINV.Application.Inventory.Transfers.TransferLineInput */
export interface TransferLineInput {
  quantity: number;
  serials?: string[] | null;
  sku: string;
}

/** MINV.Application.Inventory.Transfers.TransferLineRow */
export interface TransferLineRow {
  lineId: string;
  lots: string[];
  name: string;
  quantity: number;
  received: number;
  serials: string[] | null;
  shortage: number;
  shortageReason: string | null;
  sku: string;
  unit: string;
  unitCost: number | null;
}

/** MINV.Application.Inventory.Transfers.TransferReceiptInput */
export interface TransferReceiptInput {
  missingSerials?: string[] | null;
  receivedQuantity: number;
  shortageReason?: string | null;
  sku: string;
}

/** MINV.Application.Inventory.Transfers.TransferRef */
export interface TransferRef {
  id: string;
  message: string;
  number: string;
}

/** MINV.Application.Inventory.Transfers.TransferRow */
export interface TransferRow {
  canCancel: boolean;
  canDispatch: boolean;
  canReceive: boolean;
  dispatchedAt: string | null;
  fromBranch: string;
  fromBranchCode: string;
  fromWarehouse: string;
  id: string;
  lines: number;
  notes: string | null;
  number: string;
  quantity: number;
  receivedAt: string | null;
  requestedAt: string;
  shortage: number;
  status: TransferStatus;
  statusLabel: string;
  toBranch: string;
  toBranchCode: string;
  toWarehouse: string;
  value: number;
}

/** MINV.Domain.Inventory.TransferStatus */
export type TransferStatus = 'Cancelled' | 'Dispatched' | 'Pending' | 'Received';

/** MINV.Application.Billing.UnitHomologationRow */
export interface UnitHomologationRow {
  code: string;
  name: string;
  sinUnitCode: number | null;
  sinUnitDescription: string | null;
  unitId: string;
}

/** MINV.Application.Catalog.UnitOption */
export interface UnitOption {
  allowsDecimals: boolean;
  code: string;
  name: string;
}

/** MINV.Application.Corporate.UpdateBranchCommand */
export interface UpdateBranchCommand {
  code: string;
  isActive: boolean;
  name: string;
}

/** MINV.Application.Iam.UpdateCompanySettingsCommand */
export interface UpdateCompanySettingsCommand {
  alertMargin: number;
  daysWithoutRotation: number;
}

/** MINV.Application.Accounts.UpdateMyAccountCommand */
export interface UpdateMyAccountCommand {
  complement?: string | null;
  documentNumber?: string | null;
  documentType?: number | null;
  name: string;
  phone: string;
}

/** MINV.Application.Iam.UserRow */
export interface UserRow {
  branchCodes: string[];
  email: string;
  failedAttempts: number;
  hasPassword: boolean;
  isActive: boolean;
  isLocked: boolean;
  lastAccess: string | null;
  mustChangePassword: boolean;
  name: string;
  roles: string[];
}

/** MINV.Application.Billing.VerifyNitCommand */
export interface VerifyNitCommand {
  customerCode?: string | null;
  nit: number;
}

/** MINV.Application.Billing.VoidFiscalDocumentCommand */
export interface VoidFiscalDocumentCommand {
  documentId: string;
  note?: string | null;
  reasonCode: number;
  returnGoods: boolean;
}

/** MINV.Application.Sales.VoidSaleCommand */
export interface VoidSaleCommand {
  invoiceNumber: string;
  reason: string;
}

/** MINV.Domain.Service.WarrantyClaimAction */
export type WarrantyClaimAction = 'Closed' | 'NoteAdded' | 'Opened' | 'ReplacementIssued' | 'StatusChanged';

/** MINV.Application.Tech.WarrantyClaimDetail */
export interface WarrantyClaimDetail {
  claim: WarrantyClaimRow;
  events: WarrantyClaimEventView[];
  invoiceNumber: string | null;
  nextStatuses: WarrantyClaimStatus[];
  warranty: WarrantyStatusView;
}

/** MINV.Application.Tech.WarrantyClaimEventView */
export interface WarrantyClaimEventView {
  action: WarrantyClaimAction;
  note: string | null;
  occurredAt: string;
  status: WarrantyClaimStatus;
  user: string;
}

/** MINV.Application.Tech.WarrantyClaimRow */
export interface WarrantyClaimRow {
  branchCode: string;
  closedAt: string | null;
  customer: string;
  daysOpen: number;
  id: string;
  isInWarranty: boolean;
  issue: string;
  number: string;
  product: string;
  receivedAt: string;
  replacementSerial: string | null;
  resolution: string | null;
  serial: string;
  sku: string;
  status: WarrantyClaimStatus;
  supplier: string | null;
  warrantyUntil: string | null;
}

/** MINV.Domain.Service.WarrantyClaimStatus */
export type WarrantyClaimStatus = 'Delivered' | 'Diagnosing' | 'Received' | 'Rejected' | 'Repaired' | 'Replaced' | 'SentToSupplier';

/** MINV.Application.Tech.WarrantyStatusView */
export interface WarrantyStatusView {
  customer: string | null;
  customerCode: string | null;
  inWarranty: boolean;
  invoiceNumber: string | null;
  openClaim: string | null;
  product: string;
  serial: string;
  sku: string;
  soldOn: string | null;
  status: SerialNumberStatus;
  warrantyMonths: number;
  warrantyUntil: string | null;
}

/** MINV.Application.Remote.WebSession */
export interface WebSession {
  access: BranchAccess;
  company: string;
  displayName: string;
  email: string;
  expiresAt: string;
  kind: string;
  mustChangePassword: boolean;
  permissions: string[];
  roles: string[];
  serverVersion: string;
}

/** MINV.Application.Integration.WebhookDeliveryRow */
export interface WebhookDeliveryRow {
  attempt: number;
  attemptedAt: string;
  durationMs: number;
  error: string | null;
  eventType: string;
  statusCode: number | null;
  succeeded: boolean;
  url: string;
}

/** MINV.Application.Integration.WebhookRow */
export interface WebhookRow {
  branch: string | null;
  createdAt: string;
  delivered: number;
  description: string | null;
  events: string[];
  failed: number;
  id: string;
  isActive: boolean;
  lastAttemptAt: string | null;
  lastError: string | null;
  secretVersion: number;
  url: string;
}

/** MINV.Application.Inventory.Queries.WorkspaceInfo */
export interface WorkspaceInfo {
  alertMargin: number;
  companyName: string;
  currencyCode: string;
  currencyDecimals: number;
  currencySymbol: string;
  daysWithoutRotation: number;
  taxId: string | null;
  tenantCode: string;
  timeZoneId: string;
  today: string;
  warehouseCode: string;
  warehouseName: string;
}

// ---------------------------------------------------------------------------------------------------- operaciones del RPC

/** Nombre corto de cada operación → su petición y su respuesta. */
export interface RpcOperations {
  AddWarrantyClaimNoteCommand: { request: AddWarrantyClaimNoteCommand; response: WarrantyClaimRow };
  ApprovePurchaseOrderCommand: { request: ApprovePurchaseOrderCommand; response: string };
  AssignUserBranchesCommand: { request: AssignUserBranchesCommand; response: string };
  CancelMyReservationCommand: { request: CancelMyReservationCommand; response: StorefrontReservationView };
  CancelPcBuildCommand: { request: CancelPcBuildCommand; response: string };
  CancelPhysicalCountCommand: { request: CancelPhysicalCountCommand; response: string };
  CancelPurchaseOrderCommand: { request: CancelPurchaseOrderCommand; response: string };
  CancelStorefrontReservationCommand: { request: CancelStorefrontReservationCommand; response: StorefrontReservationView };
  CancelTransferCommand: { request: CancelTransferCommand; response: TransferRef };
  ChangePasswordCommand: { request: ChangePasswordCommand; response: boolean };
  CheckFiscalDocumentStatusCommand: { request: CheckFiscalDocumentStatusCommand; response: string };
  CheckPcBuildQuery: { request: CheckPcBuildQuery; response: PcBuildCheckView };
  CheckSiatCommunicationCommand: { request: CheckSiatCommunicationCommand; response: string };
  CheckoutCommand: { request: CheckoutCommand; response: CheckoutResult };
  ClosePosSessionCommand: { request: ClosePosSessionCommand; response: number };
  CloseSiatPointOfSaleCommand: { request: CloseSiatPointOfSaleCommand; response: string };
  ConsolidatedStockQuery: { request: ConsolidatedStockQuery; response: ConsolidatedStock };
  CreateAccountCommand: { request: CreateAccountCommand; response: string };
  CreateApiKeyCommand: { request: CreateApiKeyCommand; response: CreatedApiKey };
  CreateBranchCommand: { request: CreateBranchCommand; response: string };
  CreateExternalOrderCommand: { request: CreateExternalOrderCommand; response: ExternalOrderResult };
  CreateJournalEntryCommand: { request: CreateJournalEntryCommand; response: string };
  CreateMyReservationCommand: { request: CreateMyReservationCommand; response: StorefrontReservationView };
  CreatePurchaseOrderCommand: { request: CreatePurchaseOrderCommand; response: PurchaseOrderRow };
  CreateSalesReturnCommand: { request: CreateSalesReturnCommand; response: SalesReturnResult };
  CreateStorefrontReservationCommand: { request: CreateStorefrontReservationCommand; response: StorefrontReservationResult };
  CreateSuggestedPurchaseOrdersCommand: { request: CreateSuggestedPurchaseOrdersCommand; response: string[] };
  CreateTransferCommand: { request: CreateTransferCommand; response: TransferRef };
  CreateWebhookCommand: { request: CreateWebhookCommand; response: CreatedWebhook };
  DisableWebhookCommand: { request: DisableWebhookCommand; response: string };
  DispatchFiscalDocumentsCommand: { request: DispatchFiscalDocumentsCommand; response: DispatchResult };
  DispatchTransferCommand: { request: DispatchTransferCommand; response: TransferRef };
  DisposeSerialCommand: { request: DisposeSerialCommand; response: string };
  EndContingencyCommand: { request: EndContingencyCommand; response: string };
  ExpirePcBuildReservationsCommand: { request: ExpirePcBuildReservationsCommand; response: number };
  ExportFiscalBookQuery: { request: ExportFiscalBookQuery; response: FiscalFile };
  FindFiscalBuyerQuery: { request: FindFiscalBuyerQuery; response: FiscalBuyerLookup };
  GetActivityQuery: { request: GetActivityQuery; response: ActivityRow[] };
  GetApiCatalogQuery: { request: GetApiCatalogQuery; response: PageOfApiProduct };
  GetApiKeysQuery: { request: GetApiKeysQuery; response: ApiKeyRow[] };
  GetApiStockQuery: { request: GetApiStockQuery; response: PageOfApiStock };
  GetAvailableSerialsQuery: { request: GetAvailableSerialsQuery; response: SerialRow[] };
  GetBillingAccessQuery: { request: GetBillingAccessQuery; response: BillingAccessView };
  GetBinsQuery: { request: GetBinsQuery; response: BinItem[] };
  GetBranchReportQuery: { request: GetBranchReportQuery; response: BranchReport };
  GetBranchesQuery: { request: GetBranchesQuery; response: BranchRow[] };
  GetCatalogOptionsQuery: { request: GetCatalogOptionsQuery; response: CatalogOptions };
  GetCatalogQuery: { request: GetCatalogQuery; response: CatalogItem[] };
  GetChartOfAccountsQuery: { request: GetChartOfAccountsQuery; response: AccountRow[] };
  GetCompanySettingsQuery: { request: GetCompanySettingsQuery; response: CompanySettings };
  GetContingencyCodesQuery: { request: GetContingencyCodesQuery; response: ContingencyCodeRow[] };
  GetCustomerFiscalIdentitiesQuery: { request: GetCustomerFiscalIdentitiesQuery; response: CustomerFiscalIdentityRow[] };
  GetCustomersQuery: { request: GetCustomersQuery; response: CustomersView };
  GetExternalOrderQuery: { request: GetExternalOrderQuery; response: ExternalOrderResult };
  GetFiscalDocumentQuery: { request: GetFiscalDocumentQuery; response: FiscalDocumentDetail };
  GetFiscalDocumentsQuery: { request: GetFiscalDocumentsQuery; response: FiscalDocumentRow[] };
  GetFiscalPackagesQuery: { request: GetFiscalPackagesQuery; response: FiscalPackageRow[] };
  GetFiscalPrintModelQuery: { request: GetFiscalPrintModelQuery; response: FiscalPrintModel };
  GetHomologationQuery: { request: GetHomologationQuery; response: HomologationView };
  GetIncomeStatementQuery: { request: GetIncomeStatementQuery; response: IncomeStatement };
  GetIntegrationCatalogQuery: { request: GetIntegrationCatalogQuery; response: IntegrationCatalog };
  GetJournalQuery: { request: GetJournalQuery; response: JournalEntryRow[] };
  GetMovementTrendQuery: { request: GetMovementTrendQuery; response: MovementTrendDay[] };
  GetMovementTypesQuery: { request: GetMovementTypesQuery; response: MovementTypeItem[] };
  GetMovementsReportQuery: { request: GetMovementsReportQuery; response: MovementReportRow[] };
  GetMyAccountQuery: { request: GetMyAccountQuery; response: MyAccountView };
  GetMyReservationsQuery: { request: GetMyReservationsQuery; response: StorefrontReservationView[] };
  GetOpenPhysicalCountQuery: { request: GetOpenPhysicalCountQuery; response: PhysicalCountSheet | null };
  GetOutgoingMailsQuery: { request: GetOutgoingMailsQuery; response: OutgoingMailRow[] };
  GetPcBuildCandidatesQuery: { request: GetPcBuildCandidatesQuery; response: PcBuildCandidate[] };
  GetPcBuildQuery: { request: GetPcBuildQuery; response: PcBuildDetail };
  GetPcBuildsQuery: { request: GetPcBuildsQuery; response: PcBuildRow[] };
  GetPosFiscalStateQuery: { request: GetPosFiscalStateQuery; response: PosFiscalState };
  GetPosStateQuery: { request: GetPosStateQuery; response: PosState };
  GetProductCardQuery: { request: GetProductCardQuery; response: ProductCard };
  GetProductImagesQuery: { request: GetProductImagesQuery; response: ProductImageData[] };
  GetProductLookupQuery: { request: GetProductLookupQuery; response: ProductLookupItem[] };
  GetProductTechQuery: { request: GetProductTechQuery; response: ProductTechView };
  GetPurchaseOrderQuery: { request: GetPurchaseOrderQuery; response: PurchaseOrderDetail };
  GetPurchaseOrdersQuery: { request: GetPurchaseOrdersQuery; response: PurchaseOrderRow[] };
  GetPurchasesBookQuery: { request: GetPurchasesBookQuery; response: PurchasesBookView };
  GetPurchasesReportQuery: { request: GetPurchasesReportQuery; response: PurchasesReport };
  GetReceiptsWithoutInvoiceQuery: { request: GetReceiptsWithoutInvoiceQuery; response: PendingSupplierInvoiceRow[] };
  GetRecentMovementsQuery: { request: GetRecentMovementsQuery; response: RecentMovement[] };
  GetReturnableLinesQuery: { request: GetReturnableLinesQuery; response: ReturnableLine[] };
  GetRolesQuery: { request: GetRolesQuery; response: RolesView };
  GetSaleLinesQuery: { request: GetSaleLinesQuery; response: SaleLineRow[] };
  GetSalesBookQuery: { request: GetSalesBookQuery; response: SalesBookView };
  GetSalesFiscalStatusQuery: { request: GetSalesFiscalStatusQuery; response: SaleFiscalStatusRow[] };
  GetSalesQuery: { request: GetSalesQuery; response: SaleRow[] };
  GetSalesReportQuery: { request: GetSalesReportQuery; response: SalesReport };
  GetSalesReturnsQuery: { request: GetSalesReturnsQuery; response: SalesReturnRow[] };
  GetSellableProductsQuery: { request: GetSellableProductsQuery; response: SellableProduct[] };
  GetSerialSummaryQuery: { request: GetSerialSummaryQuery; response: SerialSummaryView };
  GetSerialTraceQuery: { request: GetSerialTraceQuery; response: SerialTraceView };
  GetSiatActivitiesQuery: { request: GetSiatActivitiesQuery; response: SiatActivityView[] };
  GetSiatCatalogQuery: { request: GetSiatCatalogQuery; response: SiatCatalogItemView[] };
  GetSiatServiceCallsQuery: { request: GetSiatServiceCallsQuery; response: SiatServiceCallRow[] };
  GetSiatSettingsQuery: { request: GetSiatSettingsQuery; response: SiatSettingsView };
  GetSiatStatusQuery: { request: GetSiatStatusQuery; response: SiatStatusView };
  GetSignificantEventsQuery: { request: GetSignificantEventsQuery; response: SignificantEventRow[] };
  GetSpecDefinitionsQuery: { request: GetSpecDefinitionsQuery; response: SpecDefinitionView[] };
  GetSpecFacetsQuery: { request: GetSpecFacetsQuery; response: SpecFacet[] };
  GetStockProjectionQuery: { request: GetStockProjectionQuery; response: StockProjectionView };
  GetStockReservationsQuery: { request: GetStockReservationsQuery; response: ReservedStockRow[] };
  GetStorefrontCatalogQuery: { request: GetStorefrontCatalogQuery; response: StorefrontCatalogView };
  GetStorefrontPresetsQuery: { request: GetStorefrontPresetsQuery; response: StorefrontPreset[] };
  GetStorefrontProductImageQuery: { request: GetStorefrontProductImageQuery; response: StorefrontImage };
  GetStorefrontProductQuery: { request: GetStorefrontProductQuery; response: StorefrontProduct };
  GetStorefrontReservationQuery: { request: GetStorefrontReservationQuery; response: StorefrontReservationView };
  GetSupplierInvoicesQuery: { request: GetSupplierInvoicesQuery; response: SupplierInvoiceRow[] };
  GetSuppliersQuery: { request: GetSuppliersQuery; response: SupplierRow[] };
  GetTaxSummaryQuery: { request: GetTaxSummaryQuery; response: TaxSummaryView };
  GetTechDashboardQuery: { request: GetTechDashboardQuery; response: TechDashboardView };
  GetTransferQuery: { request: GetTransferQuery; response: TransferDetail };
  GetTransfersQuery: { request: GetTransfersQuery; response: TransferRow[] };
  GetUsersQuery: { request: GetUsersQuery; response: UserRow[] };
  GetWarrantyClaimQuery: { request: GetWarrantyClaimQuery; response: WarrantyClaimDetail };
  GetWarrantyClaimsQuery: { request: GetWarrantyClaimsQuery; response: WarrantyClaimRow[] };
  GetWarrantyStatusQuery: { request: GetWarrantyStatusQuery; response: WarrantyStatusView };
  GetWebhookDeliveriesQuery: { request: GetWebhookDeliveriesQuery; response: WebhookDeliveryRow[] };
  GetWebhooksQuery: { request: GetWebhooksQuery; response: WebhookRow[] };
  GetWorkspaceQuery: { request: GetWorkspaceQuery; response: WorkspaceInfo };
  GoOfflineCommand: { request: GoOfflineCommand; response: string };
  IssueWarrantyReplacementCommand: { request: IssueWarrantyReplacementCommand; response: WarrantyClaimRow };
  LinkPointOfSaleRegisterCommand: { request: LinkPointOfSaleRegisterCommand; response: string };
  LogoutCommand: { request: LogoutCommand; response: boolean };
  MoveWarrantyClaimCommand: { request: MoveWarrantyClaimCommand; response: WarrantyClaimRow };
  OpenPhysicalCountCommand: { request: OpenPhysicalCountCommand; response: OpenPhysicalCountResult };
  OpenPosSessionCommand: { request: OpenPosSessionCommand; response: string };
  OpenWarrantyClaimCommand: { request: OpenWarrantyClaimCommand; response: WarrantyClaimRow };
  PostPhysicalCountCommand: { request: PostPhysicalCountCommand; response: PostPhysicalCountResult };
  PrepareSiatCommand: { request: PrepareSiatCommand; response: SiatMaintenanceResult };
  PublishPcBuildCommand: { request: PublishPcBuildCommand; response: PcBuildRow };
  ReceivePurchaseOrderCommand: { request: ReceivePurchaseOrderCommand; response: ReceiptResult };
  ReceiveTransferCommand: { request: ReceiveTransferCommand; response: TransferRef };
  RecordCountCommand: { request: RecordCountCommand; response: string };
  RecordFiscalDeliveryCommand: { request: RecordFiscalDeliveryCommand; response: string };
  RecoverPointOfSaleCommand: { request: RecoverPointOfSaleCommand; response: string };
  RegisterContingencyCodeCommand: { request: RegisterContingencyCodeCommand; response: string };
  RegisterMovementCommand: { request: RegisterMovementCommand; response: RegisterMovementResult };
  RegisterSiatPointOfSaleCommand: { request: RegisterSiatPointOfSaleCommand; response: SiatPointOfSaleStatus };
  RegisterStockSerialsCommand: { request: RegisterStockSerialsCommand; response: string };
  RegisterSupplierInvoiceCommand: { request: RegisterSupplierInvoiceCommand; response: string };
  ReissueFiscalDocumentCommand: { request: ReissueFiscalDocumentCommand; response: FiscalDocumentRow };
  ReleasePcBuildReservationCommand: { request: ReleasePcBuildReservationCommand; response: PcBuildRow };
  RemoveCountCommand: { request: RemoveCountCommand; response: boolean };
  RemoveProductImageCommand: { request: RemoveProductImageCommand; response: boolean };
  RenderFiscalDocumentQuery: { request: RenderFiscalDocumentQuery; response: FiscalFile };
  RequestCufdCommand: { request: RequestCufdCommand; response: string };
  RequestCuisCommand: { request: RequestCuisCommand; response: string };
  ResendReservationMailCommand: { request: ResendReservationMailCommand; response: OutgoingMailRow };
  ReserveCartCommand: { request: ReserveCartCommand; response: PcBuildRow };
  ReservePcBuildCommand: { request: ReservePcBuildCommand; response: PcBuildRow };
  ResetUserPasswordCommand: { request: ResetUserPasswordCommand; response: boolean };
  RevertFiscalVoidCommand: { request: RevertFiscalVoidCommand; response: string };
  RevokeApiKeyCommand: { request: RevokeApiKeyCommand; response: string };
  RotateWebhookSecretCommand: { request: RotateWebhookSecretCommand; response: CreatedWebhook };
  RunSiatWorkCommand: { request: RunSiatWorkCommand; response: SiatWorkResult };
  SaveCategoryCommand: { request: SaveCategoryCommand; response: string };
  SaveCustomerCommand: { request: SaveCustomerCommand; response: string };
  SaveCustomerFiscalIdentityCommand: { request: SaveCustomerFiscalIdentityCommand; response: string };
  SaveMailSettingsCommand: { request: SaveMailSettingsCommand; response: string };
  SavePaymentMethodHomologationCommand: { request: SavePaymentMethodHomologationCommand; response: string };
  SavePcBuildCommand: { request: SavePcBuildCommand; response: PcBuildRow };
  SaveProductCommand: { request: SaveProductCommand; response: string };
  SaveProductHomologationCommand: { request: SaveProductHomologationCommand; response: string };
  SaveProductTechCommand: { request: SaveProductTechCommand; response: string };
  SaveSiatBranchCommand: { request: SaveSiatBranchCommand; response: string };
  SaveSiatProfileCommand: { request: SaveSiatProfileCommand; response: string };
  SaveSiatSettingsCommand: { request: SaveSiatSettingsCommand; response: string };
  SaveSpecDefinitionCommand: { request: SaveSpecDefinitionCommand; response: string };
  SaveSupplierCommand: { request: SaveSupplierCommand; response: string };
  SaveUnitHomologationCommand: { request: SaveUnitHomologationCommand; response: string };
  SaveUserCommand: { request: SaveUserCommand; response: string };
  SearchSerialsQuery: { request: SearchSerialsQuery; response: SerialRow[] };
  SearchSiatProductsQuery: { request: SearchSiatProductsQuery; response: SiatProductView[] };
  SearchTechProductsQuery: { request: SearchTechProductsQuery; response: TechProductRow[] };
  SelectBranchCommand: { request: SelectBranchCommand; response: BranchAccess };
  SellPcBuildCommand: { request: SellPcBuildCommand; response: CheckoutResult };
  SendFiscalDocumentEmailCommand: { request: SendFiscalDocumentEmailCommand; response: string };
  SetProductImageCommand: { request: SetProductImageCommand; response: boolean };
  StartManualContingencyCommand: { request: StartManualContingencyCommand; response: string };
  SuggestProductHomologationQuery: { request: SuggestProductHomologationQuery; response: ProductHomologationInput[] };
  SyncSiatCatalogsCommand: { request: SyncSiatCatalogsCommand; response: SiatSyncResult };
  TranscribeManualInvoiceCommand: { request: TranscribeManualInvoiceCommand; response: FiscalDocumentRow };
  UpdateBranchCommand: { request: UpdateBranchCommand; response: string };
  UpdateCompanySettingsCommand: { request: UpdateCompanySettingsCommand; response: boolean };
  UpdateMyAccountCommand: { request: UpdateMyAccountCommand; response: MyAccountView };
  VerifyNitCommand: { request: VerifyNitCommand; response: NitCheckResult };
  VoidFiscalDocumentCommand: { request: VoidFiscalDocumentCommand; response: string };
  VoidSaleCommand: { request: VoidSaleCommand; response: string };
}

/** Datos de una operación del RPC. */
export interface RpcOperationMeta {
  /** Nombre completo del caso de uso: es lo que viaja en `RpcRequest.type`. */
  type: string;
  /** Comando que modifica datos (auditado e idempotente por `requestId`). */
  command: boolean;
  /** Permisos que exige: todos. */
  permissions: readonly string[];
  /** Módulos comerciales que exige: todos. */
  modules: readonly string[];
  /** Lo puede ejecutar una sesión de cliente (regla P-04). */
  customer: boolean;
}

/** Operaciones del RPC por nombre corto. El servidor vuelve a comprobar todo en cada petición (regla P-01). */
export const RPC_META = {
  AddWarrantyClaimNoteCommand: { type: 'MINV.Application.Tech.AddWarrantyClaimNoteCommand', command: true, permissions: ['service.rma.open'], modules: [], customer: false },
  ApprovePurchaseOrderCommand: { type: 'MINV.Application.Purchasing.ApprovePurchaseOrderCommand', command: true, permissions: ['purchasing.manage'], modules: [], customer: false },
  AssignUserBranchesCommand: { type: 'MINV.Application.Corporate.AssignUserBranchesCommand', command: true, permissions: ['corporate.branches.manage'], modules: ['MULTI_BRANCH'], customer: false },
  CancelMyReservationCommand: { type: 'MINV.Application.Accounts.CancelMyReservationCommand', command: true, permissions: ['account.manage'], modules: [], customer: true },
  CancelPcBuildCommand: { type: 'MINV.Application.Tech.CancelPcBuildCommand', command: true, permissions: ['sales.pcbuild.manage'], modules: [], customer: false },
  CancelPhysicalCountCommand: { type: 'MINV.Application.Inventory.PhysicalCounts.CancelPhysicalCountCommand', command: true, permissions: ['inventory.counts.post'], modules: [], customer: false },
  CancelPurchaseOrderCommand: { type: 'MINV.Application.Purchasing.CancelPurchaseOrderCommand', command: true, permissions: ['purchasing.manage'], modules: [], customer: false },
  CancelStorefrontReservationCommand: { type: 'MINV.Application.Storefront.CancelStorefrontReservationCommand', command: true, permissions: ['storefront.reserve'], modules: [], customer: false },
  CancelTransferCommand: { type: 'MINV.Application.Inventory.Transfers.CancelTransferCommand', command: true, permissions: ['inventory.transfers.manage'], modules: ['MULTI_BRANCH'], customer: false },
  ChangePasswordCommand: { type: 'MINV.Application.Iam.ChangePasswordCommand', command: true, permissions: [], modules: [], customer: true },
  CheckFiscalDocumentStatusCommand: { type: 'MINV.Application.Billing.CheckFiscalDocumentStatusCommand', command: true, permissions: ['billing.view'], modules: ['FISCAL_SIAT'], customer: false },
  CheckPcBuildQuery: { type: 'MINV.Application.Tech.CheckPcBuildQuery', command: false, permissions: ['inventory.stock.view'], modules: [], customer: false },
  CheckSiatCommunicationCommand: { type: 'MINV.Application.Billing.CheckSiatCommunicationCommand', command: true, permissions: ['billing.view'], modules: ['FISCAL_SIAT'], customer: false },
  CheckoutCommand: { type: 'MINV.Application.Sales.CheckoutCommand', command: true, permissions: ['inventory.movements.register.sales', 'sales.pos.operate'], modules: [], customer: false },
  ClosePosSessionCommand: { type: 'MINV.Application.Sales.ClosePosSessionCommand', command: true, permissions: ['sales.pos.operate'], modules: ['POS_HARDWARE'], customer: false },
  CloseSiatPointOfSaleCommand: { type: 'MINV.Application.Billing.CloseSiatPointOfSaleCommand', command: true, permissions: ['billing.configure'], modules: ['FISCAL_SIAT'], customer: false },
  ConsolidatedStockQuery: { type: 'MINV.Application.Corporate.ConsolidatedStockQuery', command: false, permissions: ['inventory.stock.view'], modules: [], customer: false },
  CreateAccountCommand: { type: 'MINV.Application.Accounting.CreateAccountCommand', command: true, permissions: ['accounting.manage'], modules: [], customer: false },
  CreateApiKeyCommand: { type: 'MINV.Application.Integration.CreateApiKeyCommand', command: true, permissions: ['integration.manage'], modules: ['API_INTEGRATIONS'], customer: false },
  CreateBranchCommand: { type: 'MINV.Application.Corporate.CreateBranchCommand', command: true, permissions: ['corporate.branches.manage'], modules: ['MULTI_BRANCH'], customer: false },
  CreateExternalOrderCommand: { type: 'MINV.Application.Integration.CreateExternalOrderCommand', command: true, permissions: ['inventory.movements.register.sales', 'sales.pos.operate'], modules: ['API_INTEGRATIONS'], customer: false },
  CreateJournalEntryCommand: { type: 'MINV.Application.Accounting.CreateJournalEntryCommand', command: true, permissions: ['accounting.manage'], modules: [], customer: false },
  CreateMyReservationCommand: { type: 'MINV.Application.Accounts.CreateMyReservationCommand', command: true, permissions: ['account.reserve'], modules: [], customer: true },
  CreatePurchaseOrderCommand: { type: 'MINV.Application.Purchasing.CreatePurchaseOrderCommand', command: true, permissions: ['purchasing.manage'], modules: [], customer: false },
  CreateSalesReturnCommand: { type: 'MINV.Application.Billing.CreateSalesReturnCommand', command: true, permissions: ['billing.void', 'sales.pos.operate'], modules: [], customer: false },
  CreateStorefrontReservationCommand: { type: 'MINV.Application.Storefront.CreateStorefrontReservationCommand', command: true, permissions: ['storefront.reserve'], modules: [], customer: false },
  CreateSuggestedPurchaseOrdersCommand: { type: 'MINV.Application.Purchasing.CreateSuggestedPurchaseOrdersCommand', command: true, permissions: ['purchasing.manage'], modules: [], customer: false },
  CreateTransferCommand: { type: 'MINV.Application.Inventory.Transfers.CreateTransferCommand', command: true, permissions: ['inventory.transfers.manage'], modules: ['MULTI_BRANCH'], customer: false },
  CreateWebhookCommand: { type: 'MINV.Application.Integration.CreateWebhookCommand', command: true, permissions: ['integration.manage'], modules: ['API_INTEGRATIONS'], customer: false },
  DisableWebhookCommand: { type: 'MINV.Application.Integration.DisableWebhookCommand', command: true, permissions: ['integration.manage'], modules: [], customer: false },
  DispatchFiscalDocumentsCommand: { type: 'MINV.Application.Billing.DispatchFiscalDocumentsCommand', command: true, permissions: ['billing.issue'], modules: ['FISCAL_SIAT'], customer: false },
  DispatchTransferCommand: { type: 'MINV.Application.Inventory.Transfers.DispatchTransferCommand', command: true, permissions: ['inventory.transfers.manage'], modules: ['MULTI_BRANCH'], customer: false },
  DisposeSerialCommand: { type: 'MINV.Application.Tech.DisposeSerialCommand', command: true, permissions: ['inventory.serials.manage'], modules: [], customer: false },
  EndContingencyCommand: { type: 'MINV.Application.Billing.EndContingencyCommand', command: true, permissions: ['billing.contingency'], modules: ['FISCAL_SIAT'], customer: false },
  ExpirePcBuildReservationsCommand: { type: 'MINV.Application.Storefront.ExpirePcBuildReservationsCommand', command: true, permissions: ['storefront.reserve'], modules: [], customer: false },
  ExportFiscalBookQuery: { type: 'MINV.Application.Billing.ExportFiscalBookQuery', command: false, permissions: ['billing.view'], modules: [], customer: false },
  FindFiscalBuyerQuery: { type: 'MINV.Application.Billing.FindFiscalBuyerQuery', command: false, permissions: ['billing.issue'], modules: [], customer: false },
  GetActivityQuery: { type: 'MINV.Application.Iam.GetActivityQuery', command: false, permissions: ['iam.audit.view'], modules: [], customer: false },
  GetApiCatalogQuery: { type: 'MINV.Application.Integration.GetApiCatalogQuery', command: false, permissions: ['inventory.stock.view'], modules: [], customer: false },
  GetApiKeysQuery: { type: 'MINV.Application.Integration.GetApiKeysQuery', command: false, permissions: ['integration.manage'], modules: [], customer: false },
  GetApiStockQuery: { type: 'MINV.Application.Integration.GetApiStockQuery', command: false, permissions: ['inventory.stock.view'], modules: [], customer: false },
  GetAvailableSerialsQuery: { type: 'MINV.Application.Tech.GetAvailableSerialsQuery', command: false, permissions: ['inventory.serials.view'], modules: [], customer: false },
  GetBillingAccessQuery: { type: 'MINV.Application.Billing.GetBillingAccessQuery', command: false, permissions: [], modules: [], customer: false },
  GetBinsQuery: { type: 'MINV.Application.Inventory.Queries.GetBinsQuery', command: false, permissions: ['inventory.stock.view'], modules: [], customer: false },
  GetBranchReportQuery: { type: 'MINV.Application.Corporate.GetBranchReportQuery', command: false, permissions: ['reports.view'], modules: ['GLOBAL_AUDIT'], customer: false },
  GetBranchesQuery: { type: 'MINV.Application.Corporate.GetBranchesQuery', command: false, permissions: ['inventory.stock.view'], modules: [], customer: false },
  GetCatalogOptionsQuery: { type: 'MINV.Application.Catalog.GetCatalogOptionsQuery', command: false, permissions: ['inventory.stock.view'], modules: [], customer: false },
  GetCatalogQuery: { type: 'MINV.Application.Catalog.GetCatalogQuery', command: false, permissions: ['inventory.stock.view'], modules: [], customer: false },
  GetChartOfAccountsQuery: { type: 'MINV.Application.Accounting.GetChartOfAccountsQuery', command: false, permissions: ['accounting.manage'], modules: [], customer: false },
  GetCompanySettingsQuery: { type: 'MINV.Application.Iam.GetCompanySettingsQuery', command: false, permissions: ['inventory.stock.view'], modules: [], customer: false },
  GetContingencyCodesQuery: { type: 'MINV.Application.Billing.GetContingencyCodesQuery', command: false, permissions: ['billing.view'], modules: [], customer: false },
  GetCustomerFiscalIdentitiesQuery: { type: 'MINV.Application.Billing.GetCustomerFiscalIdentitiesQuery', command: false, permissions: ['inventory.stock.view'], modules: [], customer: false },
  GetCustomersQuery: { type: 'MINV.Application.Partners.GetCustomersQuery', command: false, permissions: ['inventory.stock.view'], modules: [], customer: false },
  GetExternalOrderQuery: { type: 'MINV.Application.Integration.GetExternalOrderQuery', command: false, permissions: ['sales.view'], modules: ['API_INTEGRATIONS'], customer: false },
  GetFiscalDocumentQuery: { type: 'MINV.Application.Billing.GetFiscalDocumentQuery', command: false, permissions: ['billing.view'], modules: [], customer: false },
  GetFiscalDocumentsQuery: { type: 'MINV.Application.Billing.GetFiscalDocumentsQuery', command: false, permissions: ['billing.view'], modules: [], customer: false },
  GetFiscalPackagesQuery: { type: 'MINV.Application.Billing.GetFiscalPackagesQuery', command: false, permissions: ['billing.view'], modules: [], customer: false },
  GetFiscalPrintModelQuery: { type: 'MINV.Application.Billing.GetFiscalPrintModelQuery', command: false, permissions: ['billing.view'], modules: [], customer: false },
  GetHomologationQuery: { type: 'MINV.Application.Billing.GetHomologationQuery', command: false, permissions: ['billing.view'], modules: [], customer: false },
  GetIncomeStatementQuery: { type: 'MINV.Application.Accounting.GetIncomeStatementQuery', command: false, permissions: ['accounting.manage'], modules: [], customer: false },
  GetIntegrationCatalogQuery: { type: 'MINV.Application.Integration.GetIntegrationCatalogQuery', command: false, permissions: ['integration.manage'], modules: [], customer: false },
  GetJournalQuery: { type: 'MINV.Application.Accounting.GetJournalQuery', command: false, permissions: ['accounting.manage'], modules: [], customer: false },
  GetMovementTrendQuery: { type: 'MINV.Application.Inventory.Queries.GetMovementTrendQuery', command: false, permissions: ['inventory.stock.view'], modules: [], customer: false },
  GetMovementTypesQuery: { type: 'MINV.Application.Inventory.Queries.GetMovementTypesQuery', command: false, permissions: ['inventory.stock.view'], modules: [], customer: false },
  GetMovementsReportQuery: { type: 'MINV.Application.Reports.GetMovementsReportQuery', command: false, permissions: ['reports.view'], modules: [], customer: false },
  GetMyAccountQuery: { type: 'MINV.Application.Accounts.GetMyAccountQuery', command: false, permissions: ['account.manage'], modules: [], customer: true },
  GetMyReservationsQuery: { type: 'MINV.Application.Accounts.GetMyReservationsQuery', command: false, permissions: ['account.manage'], modules: [], customer: true },
  GetOpenPhysicalCountQuery: { type: 'MINV.Application.Inventory.PhysicalCounts.GetOpenPhysicalCountQuery', command: false, permissions: ['inventory.counts.record'], modules: [], customer: false },
  GetOutgoingMailsQuery: { type: 'MINV.Application.Integration.GetOutgoingMailsQuery', command: false, permissions: ['sales.pcbuild.manage'], modules: [], customer: false },
  GetPcBuildCandidatesQuery: { type: 'MINV.Application.Tech.GetPcBuildCandidatesQuery', command: false, permissions: ['inventory.stock.view'], modules: [], customer: false },
  GetPcBuildQuery: { type: 'MINV.Application.Tech.GetPcBuildQuery', command: false, permissions: ['sales.view'], modules: [], customer: false },
  GetPcBuildsQuery: { type: 'MINV.Application.Tech.GetPcBuildsQuery', command: false, permissions: ['sales.view'], modules: [], customer: false },
  GetPosFiscalStateQuery: { type: 'MINV.Application.Billing.GetPosFiscalStateQuery', command: false, permissions: ['sales.pos.operate'], modules: [], customer: false },
  GetPosStateQuery: { type: 'MINV.Application.Sales.GetPosStateQuery', command: false, permissions: ['sales.pos.operate'], modules: [], customer: false },
  GetProductCardQuery: { type: 'MINV.Application.Inventory.Queries.GetProductCardQuery', command: false, permissions: ['inventory.stock.view'], modules: [], customer: false },
  GetProductImagesQuery: { type: 'MINV.Application.Catalog.GetProductImagesQuery', command: false, permissions: ['inventory.stock.view'], modules: [], customer: false },
  GetProductLookupQuery: { type: 'MINV.Application.Inventory.Queries.GetProductLookupQuery', command: false, permissions: ['inventory.stock.view'], modules: [], customer: false },
  GetProductTechQuery: { type: 'MINV.Application.Tech.GetProductTechQuery', command: false, permissions: ['inventory.stock.view'], modules: [], customer: false },
  GetPurchaseOrderQuery: { type: 'MINV.Application.Purchasing.GetPurchaseOrderQuery', command: false, permissions: ['inventory.stock.view'], modules: [], customer: false },
  GetPurchaseOrdersQuery: { type: 'MINV.Application.Purchasing.GetPurchaseOrdersQuery', command: false, permissions: ['inventory.stock.view'], modules: [], customer: false },
  GetPurchasesBookQuery: { type: 'MINV.Application.Billing.GetPurchasesBookQuery', command: false, permissions: ['billing.view'], modules: [], customer: false },
  GetPurchasesReportQuery: { type: 'MINV.Application.Reports.GetPurchasesReportQuery', command: false, permissions: ['reports.view'], modules: [], customer: false },
  GetReceiptsWithoutInvoiceQuery: { type: 'MINV.Application.Billing.GetReceiptsWithoutInvoiceQuery', command: false, permissions: ['purchasing.manage'], modules: [], customer: false },
  GetRecentMovementsQuery: { type: 'MINV.Application.Inventory.Queries.GetRecentMovementsQuery', command: false, permissions: ['inventory.stock.view'], modules: [], customer: false },
  GetReturnableLinesQuery: { type: 'MINV.Application.Billing.GetReturnableLinesQuery', command: false, permissions: ['sales.view'], modules: [], customer: false },
  GetRolesQuery: { type: 'MINV.Application.Iam.GetRolesQuery', command: false, permissions: ['iam.users.manage'], modules: [], customer: false },
  GetSaleLinesQuery: { type: 'MINV.Application.Sales.GetSaleLinesQuery', command: false, permissions: ['sales.view'], modules: [], customer: false },
  GetSalesBookQuery: { type: 'MINV.Application.Billing.GetSalesBookQuery', command: false, permissions: ['billing.view'], modules: [], customer: false },
  GetSalesFiscalStatusQuery: { type: 'MINV.Application.Billing.GetSalesFiscalStatusQuery', command: false, permissions: ['sales.view'], modules: [], customer: false },
  GetSalesQuery: { type: 'MINV.Application.Sales.GetSalesQuery', command: false, permissions: ['sales.view'], modules: [], customer: false },
  GetSalesReportQuery: { type: 'MINV.Application.Reports.GetSalesReportQuery', command: false, permissions: ['reports.view'], modules: [], customer: false },
  GetSalesReturnsQuery: { type: 'MINV.Application.Billing.GetSalesReturnsQuery', command: false, permissions: ['sales.view'], modules: [], customer: false },
  GetSellableProductsQuery: { type: 'MINV.Application.Sales.GetSellableProductsQuery', command: false, permissions: ['inventory.stock.view'], modules: [], customer: false },
  GetSerialSummaryQuery: { type: 'MINV.Application.Tech.GetSerialSummaryQuery', command: false, permissions: ['inventory.serials.view'], modules: [], customer: false },
  GetSerialTraceQuery: { type: 'MINV.Application.Tech.GetSerialTraceQuery', command: false, permissions: ['inventory.serials.view'], modules: [], customer: false },
  GetSiatActivitiesQuery: { type: 'MINV.Application.Billing.GetSiatActivitiesQuery', command: false, permissions: ['billing.view'], modules: [], customer: false },
  GetSiatCatalogQuery: { type: 'MINV.Application.Billing.GetSiatCatalogQuery', command: false, permissions: ['billing.view'], modules: [], customer: false },
  GetSiatServiceCallsQuery: { type: 'MINV.Application.Billing.GetSiatServiceCallsQuery', command: false, permissions: ['billing.configure'], modules: [], customer: false },
  GetSiatSettingsQuery: { type: 'MINV.Application.Billing.GetSiatSettingsQuery', command: false, permissions: ['billing.view'], modules: [], customer: false },
  GetSiatStatusQuery: { type: 'MINV.Application.Billing.GetSiatStatusQuery', command: false, permissions: ['billing.view'], modules: [], customer: false },
  GetSignificantEventsQuery: { type: 'MINV.Application.Billing.GetSignificantEventsQuery', command: false, permissions: ['billing.view'], modules: [], customer: false },
  GetSpecDefinitionsQuery: { type: 'MINV.Application.Tech.GetSpecDefinitionsQuery', command: false, permissions: ['inventory.stock.view'], modules: [], customer: false },
  GetSpecFacetsQuery: { type: 'MINV.Application.Tech.GetSpecFacetsQuery', command: false, permissions: ['inventory.stock.view'], modules: [], customer: false },
  GetStockProjectionQuery: { type: 'MINV.Application.Inventory.Queries.GetStockProjectionQuery', command: false, permissions: ['inventory.stock.view'], modules: [], customer: false },
  GetStockReservationsQuery: { type: 'MINV.Application.Inventory.Queries.GetStockReservationsQuery', command: false, permissions: ['inventory.stock.view'], modules: [], customer: false },
  GetStorefrontCatalogQuery: { type: 'MINV.Application.Storefront.GetStorefrontCatalogQuery', command: false, permissions: ['storefront.read'], modules: [], customer: false },
  GetStorefrontPresetsQuery: { type: 'MINV.Application.Storefront.GetStorefrontPresetsQuery', command: false, permissions: ['storefront.read'], modules: [], customer: false },
  GetStorefrontProductImageQuery: { type: 'MINV.Application.Storefront.GetStorefrontProductImageQuery', command: false, permissions: ['storefront.read'], modules: [], customer: false },
  GetStorefrontProductQuery: { type: 'MINV.Application.Storefront.GetStorefrontProductQuery', command: false, permissions: ['storefront.read'], modules: [], customer: false },
  GetStorefrontReservationQuery: { type: 'MINV.Application.Storefront.GetStorefrontReservationQuery', command: false, permissions: ['storefront.read'], modules: [], customer: false },
  GetSupplierInvoicesQuery: { type: 'MINV.Application.Billing.GetSupplierInvoicesQuery', command: false, permissions: ['purchasing.manage'], modules: [], customer: false },
  GetSuppliersQuery: { type: 'MINV.Application.Partners.GetSuppliersQuery', command: false, permissions: ['inventory.stock.view'], modules: [], customer: false },
  GetTaxSummaryQuery: { type: 'MINV.Application.Billing.GetTaxSummaryQuery', command: false, permissions: ['billing.view'], modules: [], customer: false },
  GetTechDashboardQuery: { type: 'MINV.Application.Tech.GetTechDashboardQuery', command: false, permissions: ['reports.view'], modules: [], customer: false },
  GetTransferQuery: { type: 'MINV.Application.Inventory.Transfers.GetTransferQuery', command: false, permissions: ['inventory.stock.view'], modules: [], customer: false },
  GetTransfersQuery: { type: 'MINV.Application.Inventory.Transfers.GetTransfersQuery', command: false, permissions: ['inventory.stock.view'], modules: [], customer: false },
  GetUsersQuery: { type: 'MINV.Application.Iam.GetUsersQuery', command: false, permissions: ['iam.users.manage'], modules: [], customer: false },
  GetWarrantyClaimQuery: { type: 'MINV.Application.Tech.GetWarrantyClaimQuery', command: false, permissions: ['inventory.serials.view'], modules: [], customer: false },
  GetWarrantyClaimsQuery: { type: 'MINV.Application.Tech.GetWarrantyClaimsQuery', command: false, permissions: ['inventory.serials.view'], modules: [], customer: false },
  GetWarrantyStatusQuery: { type: 'MINV.Application.Tech.GetWarrantyStatusQuery', command: false, permissions: ['inventory.serials.view'], modules: [], customer: false },
  GetWebhookDeliveriesQuery: { type: 'MINV.Application.Integration.GetWebhookDeliveriesQuery', command: false, permissions: ['integration.manage'], modules: [], customer: false },
  GetWebhooksQuery: { type: 'MINV.Application.Integration.GetWebhooksQuery', command: false, permissions: ['integration.manage'], modules: [], customer: false },
  GetWorkspaceQuery: { type: 'MINV.Application.Inventory.Queries.GetWorkspaceQuery', command: false, permissions: ['inventory.stock.view'], modules: [], customer: false },
  GoOfflineCommand: { type: 'MINV.Application.Billing.GoOfflineCommand', command: true, permissions: ['billing.contingency'], modules: ['FISCAL_SIAT'], customer: false },
  IssueWarrantyReplacementCommand: { type: 'MINV.Application.Tech.IssueWarrantyReplacementCommand', command: true, permissions: ['service.rma.manage'], modules: [], customer: false },
  LinkPointOfSaleRegisterCommand: { type: 'MINV.Application.Billing.LinkPointOfSaleRegisterCommand', command: true, permissions: ['billing.configure'], modules: ['FISCAL_SIAT'], customer: false },
  LogoutCommand: { type: 'MINV.Application.Iam.LogoutCommand', command: true, permissions: [], modules: [], customer: true },
  MoveWarrantyClaimCommand: { type: 'MINV.Application.Tech.MoveWarrantyClaimCommand', command: true, permissions: ['service.rma.manage'], modules: [], customer: false },
  OpenPhysicalCountCommand: { type: 'MINV.Application.Inventory.PhysicalCounts.OpenPhysicalCountCommand', command: true, permissions: ['inventory.counts.record'], modules: [], customer: false },
  OpenPosSessionCommand: { type: 'MINV.Application.Sales.OpenPosSessionCommand', command: true, permissions: ['sales.pos.operate'], modules: ['POS_HARDWARE'], customer: false },
  OpenWarrantyClaimCommand: { type: 'MINV.Application.Tech.OpenWarrantyClaimCommand', command: true, permissions: ['service.rma.open'], modules: [], customer: false },
  PostPhysicalCountCommand: { type: 'MINV.Application.Inventory.PhysicalCounts.PostPhysicalCountCommand', command: true, permissions: ['inventory.counts.post'], modules: [], customer: false },
  PrepareSiatCommand: { type: 'MINV.Application.Billing.PrepareSiatCommand', command: true, permissions: ['billing.configure'], modules: ['FISCAL_SIAT'], customer: false },
  PublishPcBuildCommand: { type: 'MINV.Application.Tech.PublishPcBuildCommand', command: true, permissions: ['sales.pcbuild.manage'], modules: [], customer: false },
  ReceivePurchaseOrderCommand: { type: 'MINV.Application.Purchasing.ReceivePurchaseOrderCommand', command: true, permissions: ['inventory.movements.register.warehouse', 'purchasing.manage'], modules: [], customer: false },
  ReceiveTransferCommand: { type: 'MINV.Application.Inventory.Transfers.ReceiveTransferCommand', command: true, permissions: ['inventory.transfers.manage'], modules: ['MULTI_BRANCH'], customer: false },
  RecordCountCommand: { type: 'MINV.Application.Inventory.PhysicalCounts.RecordCountCommand', command: false, permissions: ['inventory.counts.record'], modules: [], customer: false },
  RecordFiscalDeliveryCommand: { type: 'MINV.Application.Billing.RecordFiscalDeliveryCommand', command: true, permissions: ['billing.view'], modules: [], customer: false },
  RecoverPointOfSaleCommand: { type: 'MINV.Application.Billing.RecoverPointOfSaleCommand', command: true, permissions: ['billing.contingency'], modules: ['FISCAL_SIAT'], customer: false },
  RegisterContingencyCodeCommand: { type: 'MINV.Application.Billing.RegisterContingencyCodeCommand', command: true, permissions: ['billing.contingency'], modules: ['FISCAL_SIAT'], customer: false },
  RegisterMovementCommand: { type: 'MINV.Application.Inventory.Movements.RegisterMovementCommand', command: true, permissions: [], modules: [], customer: false },
  RegisterSiatPointOfSaleCommand: { type: 'MINV.Application.Billing.RegisterSiatPointOfSaleCommand', command: true, permissions: ['billing.configure'], modules: ['FISCAL_SIAT'], customer: false },
  RegisterStockSerialsCommand: { type: 'MINV.Application.Tech.RegisterStockSerialsCommand', command: true, permissions: ['inventory.serials.manage'], modules: [], customer: false },
  RegisterSupplierInvoiceCommand: { type: 'MINV.Application.Billing.RegisterSupplierInvoiceCommand', command: true, permissions: ['purchasing.manage'], modules: [], customer: false },
  ReissueFiscalDocumentCommand: { type: 'MINV.Application.Billing.ReissueFiscalDocumentCommand', command: true, permissions: ['billing.issue'], modules: ['FISCAL_SIAT'], customer: false },
  ReleasePcBuildReservationCommand: { type: 'MINV.Application.Tech.ReleasePcBuildReservationCommand', command: true, permissions: ['sales.pcbuild.manage'], modules: [], customer: false },
  RemoveCountCommand: { type: 'MINV.Application.Inventory.PhysicalCounts.RemoveCountCommand', command: true, permissions: ['inventory.counts.record'], modules: [], customer: false },
  RemoveProductImageCommand: { type: 'MINV.Application.Catalog.RemoveProductImageCommand', command: true, permissions: ['catalog.manage'], modules: [], customer: false },
  RenderFiscalDocumentQuery: { type: 'MINV.Application.Billing.RenderFiscalDocumentQuery', command: false, permissions: ['billing.view'], modules: [], customer: false },
  RequestCufdCommand: { type: 'MINV.Application.Billing.RequestCufdCommand', command: true, permissions: ['billing.configure'], modules: ['FISCAL_SIAT'], customer: false },
  RequestCuisCommand: { type: 'MINV.Application.Billing.RequestCuisCommand', command: true, permissions: ['billing.configure'], modules: ['FISCAL_SIAT'], customer: false },
  ResendReservationMailCommand: { type: 'MINV.Application.Integration.ResendReservationMailCommand', command: true, permissions: ['sales.pcbuild.manage'], modules: [], customer: false },
  ReserveCartCommand: { type: 'MINV.Application.Tech.ReserveCartCommand', command: true, permissions: ['sales.pcbuild.manage'], modules: [], customer: false },
  ReservePcBuildCommand: { type: 'MINV.Application.Tech.ReservePcBuildCommand', command: true, permissions: ['sales.pcbuild.manage'], modules: [], customer: false },
  ResetUserPasswordCommand: { type: 'MINV.Application.Iam.ResetUserPasswordCommand', command: true, permissions: ['iam.users.manage'], modules: [], customer: false },
  RevertFiscalVoidCommand: { type: 'MINV.Application.Billing.RevertFiscalVoidCommand', command: true, permissions: ['billing.void'], modules: ['FISCAL_SIAT'], customer: false },
  RevokeApiKeyCommand: { type: 'MINV.Application.Integration.RevokeApiKeyCommand', command: true, permissions: ['integration.manage'], modules: [], customer: false },
  RotateWebhookSecretCommand: { type: 'MINV.Application.Integration.RotateWebhookSecretCommand', command: true, permissions: ['integration.manage'], modules: [], customer: false },
  RunSiatWorkCommand: { type: 'MINV.Application.Billing.RunSiatWorkCommand', command: true, permissions: ['billing.issue'], modules: ['FISCAL_SIAT'], customer: false },
  SaveCategoryCommand: { type: 'MINV.Application.Catalog.SaveCategoryCommand', command: true, permissions: ['catalog.manage'], modules: [], customer: false },
  SaveCustomerCommand: { type: 'MINV.Application.Partners.SaveCustomerCommand', command: true, permissions: ['sales.customers.manage'], modules: [], customer: false },
  SaveCustomerFiscalIdentityCommand: { type: 'MINV.Application.Billing.SaveCustomerFiscalIdentityCommand', command: true, permissions: ['sales.customers.manage'], modules: [], customer: false },
  SaveMailSettingsCommand: { type: 'MINV.Application.Billing.SaveMailSettingsCommand', command: true, permissions: ['billing.configure'], modules: ['FISCAL_SIAT'], customer: false },
  SavePaymentMethodHomologationCommand: { type: 'MINV.Application.Billing.SavePaymentMethodHomologationCommand', command: true, permissions: ['billing.configure'], modules: ['FISCAL_SIAT'], customer: false },
  SavePcBuildCommand: { type: 'MINV.Application.Tech.SavePcBuildCommand', command: true, permissions: ['sales.pcbuild.manage'], modules: [], customer: false },
  SaveProductCommand: { type: 'MINV.Application.Catalog.SaveProductCommand', command: true, permissions: ['catalog.manage'], modules: [], customer: false },
  SaveProductHomologationCommand: { type: 'MINV.Application.Billing.SaveProductHomologationCommand', command: true, permissions: ['billing.configure'], modules: ['FISCAL_SIAT'], customer: false },
  SaveProductTechCommand: { type: 'MINV.Application.Tech.SaveProductTechCommand', command: true, permissions: ['catalog.specs.manage'], modules: [], customer: false },
  SaveSiatBranchCommand: { type: 'MINV.Application.Billing.SaveSiatBranchCommand', command: true, permissions: ['billing.configure'], modules: ['FISCAL_SIAT'], customer: false },
  SaveSiatProfileCommand: { type: 'MINV.Application.Billing.SaveSiatProfileCommand', command: true, permissions: ['billing.configure'], modules: ['FISCAL_SIAT'], customer: false },
  SaveSiatSettingsCommand: { type: 'MINV.Application.Billing.SaveSiatSettingsCommand', command: true, permissions: ['billing.configure'], modules: ['FISCAL_SIAT'], customer: false },
  SaveSpecDefinitionCommand: { type: 'MINV.Application.Tech.SaveSpecDefinitionCommand', command: true, permissions: ['catalog.specs.manage'], modules: [], customer: false },
  SaveSupplierCommand: { type: 'MINV.Application.Partners.SaveSupplierCommand', command: true, permissions: ['purchasing.manage'], modules: [], customer: false },
  SaveUnitHomologationCommand: { type: 'MINV.Application.Billing.SaveUnitHomologationCommand', command: true, permissions: ['billing.configure'], modules: ['FISCAL_SIAT'], customer: false },
  SaveUserCommand: { type: 'MINV.Application.Iam.SaveUserCommand', command: true, permissions: ['iam.users.manage'], modules: [], customer: false },
  SearchSerialsQuery: { type: 'MINV.Application.Tech.SearchSerialsQuery', command: false, permissions: ['inventory.serials.view'], modules: [], customer: false },
  SearchSiatProductsQuery: { type: 'MINV.Application.Billing.SearchSiatProductsQuery', command: false, permissions: ['billing.view'], modules: [], customer: false },
  SearchTechProductsQuery: { type: 'MINV.Application.Tech.SearchTechProductsQuery', command: false, permissions: ['inventory.stock.view'], modules: [], customer: false },
  SelectBranchCommand: { type: 'MINV.Application.Iam.SelectBranchCommand', command: true, permissions: [], modules: [], customer: false },
  SellPcBuildCommand: { type: 'MINV.Application.Tech.SellPcBuildCommand', command: true, permissions: ['inventory.movements.register.sales', 'sales.pos.operate'], modules: [], customer: false },
  SendFiscalDocumentEmailCommand: { type: 'MINV.Application.Billing.SendFiscalDocumentEmailCommand', command: true, permissions: ['billing.issue'], modules: ['FISCAL_SIAT'], customer: false },
  SetProductImageCommand: { type: 'MINV.Application.Catalog.SetProductImageCommand', command: true, permissions: ['catalog.manage'], modules: [], customer: false },
  StartManualContingencyCommand: { type: 'MINV.Application.Billing.StartManualContingencyCommand', command: true, permissions: ['billing.contingency'], modules: ['FISCAL_SIAT'], customer: false },
  SuggestProductHomologationQuery: { type: 'MINV.Application.Billing.SuggestProductHomologationQuery', command: false, permissions: ['billing.configure'], modules: ['FISCAL_SIAT'], customer: false },
  SyncSiatCatalogsCommand: { type: 'MINV.Application.Billing.SyncSiatCatalogsCommand', command: true, permissions: ['billing.configure'], modules: ['FISCAL_SIAT'], customer: false },
  TranscribeManualInvoiceCommand: { type: 'MINV.Application.Billing.TranscribeManualInvoiceCommand', command: true, permissions: ['billing.contingency'], modules: ['FISCAL_SIAT'], customer: false },
  UpdateBranchCommand: { type: 'MINV.Application.Corporate.UpdateBranchCommand', command: true, permissions: ['corporate.branches.manage'], modules: ['MULTI_BRANCH'], customer: false },
  UpdateCompanySettingsCommand: { type: 'MINV.Application.Iam.UpdateCompanySettingsCommand', command: true, permissions: ['iam.users.manage'], modules: [], customer: false },
  UpdateMyAccountCommand: { type: 'MINV.Application.Accounts.UpdateMyAccountCommand', command: true, permissions: ['account.manage'], modules: [], customer: true },
  VerifyNitCommand: { type: 'MINV.Application.Billing.VerifyNitCommand', command: true, permissions: ['billing.issue'], modules: ['FISCAL_SIAT'], customer: false },
  VoidFiscalDocumentCommand: { type: 'MINV.Application.Billing.VoidFiscalDocumentCommand', command: true, permissions: ['billing.void'], modules: ['FISCAL_SIAT'], customer: false },
  VoidSaleCommand: { type: 'MINV.Application.Sales.VoidSaleCommand', command: true, permissions: ['sales.pos.operate', 'sales.view'], modules: [], customer: false },
} as const satisfies Record<keyof RpcOperations, RpcOperationMeta>;

// ---------------------------------------------------------------------------------------------------- permisos y roles

/** Permisos del sistema con su nombre en español (PermissionCodes.All), por código. */
export const PERMISSIONS = [
  { code: 'account.manage', name: 'Cuenta de cliente: ver y actualizar sus datos y ver o cancelar sus propias reservas' },
  { code: 'account.reserve', name: 'Cuenta de cliente: reservar productos con los datos de su cuenta' },
  { code: 'accounting.manage', name: 'Asientos, períodos y costos' },
  { code: 'billing.configure', name: 'Configurar la facturación SIAT: NIT, token, sucursales, puntos de venta, CUIS, CUFD, catálogos y homologación' },
  { code: 'billing.contingency', name: 'Gestionar eventos significativos, paquetes de contingencia y CAFC' },
  { code: 'billing.issue', name: 'Emitir facturas (al vender) y reenviar documentos fiscales' },
  { code: 'billing.view', name: 'Consultar documentos fiscales, estado del SIAT y libros de ventas y compras' },
  { code: 'billing.void', name: 'Anular y revertir documentos fiscales y emitir notas crédito-débito' },
  { code: 'catalog.manage', name: 'Crear y modificar productos, categorías, unidades y proveedores' },
  { code: 'catalog.specs.manage', name: 'Fichas técnicas: especificaciones por categoría, valores de cada producto, garantía y control por serie o IMEI' },
  { code: 'corporate.branches.all', name: 'Ver y operar todas las sucursales (gerencia global)' },
  { code: 'corporate.branches.manage', name: 'Crear y modificar sucursales y asignar usuarios a sucursales' },
  { code: 'iam.audit.view', name: 'Consultar la auditoría y la actividad' },
  { code: 'iam.users.manage', name: 'Administrar usuarios, roles y permisos' },
  { code: 'integration.manage', name: 'Administrar API Keys y webhooks de integración B2B' },
  { code: 'inventory.counts.post', name: 'Generar los ajustes de la toma física' },
  { code: 'inventory.counts.record', name: 'Registrar conteos de la toma física' },
  { code: 'inventory.movements.register.sales', name: 'Registrar salidas' },
  { code: 'inventory.movements.register.warehouse', name: 'Registrar entradas, saldo inicial y ajustes' },
  { code: 'inventory.serials.manage', name: 'Registrar series e IMEI de unidades en stock (inventario inicial) y dar de baja unidades serializadas' },
  { code: 'inventory.serials.view', name: 'Consultar series e IMEI, su trazabilidad, la garantía de una unidad y los casos RMA' },
  { code: 'inventory.stock.view', name: 'Consultar stock, alertas y pedido sugerido' },
  { code: 'inventory.transfers.manage', name: 'Crear, despachar y recibir transferencias entre sucursales' },
  { code: 'purchasing.manage', name: 'Órdenes de compra, recepciones y devoluciones' },
  { code: 'reports.view', name: 'Consultar reportes de ventas, compras, inventario y rentabilidad' },
  { code: 'sales.customers.manage', name: 'Crear y modificar clientes' },
  { code: 'sales.pcbuild.manage', name: 'Armador de PC: armar, cotizar y anular armados (cotizaciones con precio congelado)' },
  { code: 'sales.pos.operate', name: 'Abrir y cerrar caja, vender y cobrar' },
  { code: 'sales.view', name: 'Consultar el historial de ventas y facturas' },
  { code: 'service.rma.manage', name: 'Garantías y RMA: diagnosticar, enviar al proveedor, reponer con otra unidad y entregar equipos' },
  { code: 'service.rma.open', name: 'Abrir casos de garantía (RMA) al recibir un equipo del cliente' },
  { code: 'storefront.read', name: 'Tienda web: leer el catálogo público (productos, precios, disponibilidad, imágenes y armados sugeridos)' },
  { code: 'storefront.reserve', name: 'Tienda web: reservar armados con reserva de stock y consultar o cancelar una reserva con su teléfono' },
] as const;

/** Roles del sistema con su nombre en español (RoleCodes.All), por código. */
export const ROLES = [
  { code: 'ADMIN', name: 'Administrador' },
  { code: 'BODEGA', name: 'Bodega' },
  { code: 'CAJERO', name: 'Cajero' },
  { code: 'CLIENTE', name: 'Cliente web' },
  { code: 'CONSULTA', name: 'Consulta' },
  { code: 'GERENCIA', name: 'Gerencia' },
  { code: 'TIENDA_WEB', name: 'Tienda web' },
  { code: 'VENTAS', name: 'Ventas' },
] as const;
