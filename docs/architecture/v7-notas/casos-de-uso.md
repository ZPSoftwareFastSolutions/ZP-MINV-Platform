# Mapa V7 - Casos de uso de MINV.Application (generado por script)

Total: 186 - comandos (IAuditableRequest): 92 - consultas: 94

> Paquete B1 (carrito, V7): cambian `CreateStorefrontReservationCommand`, `SavePcBuildCommand` y `GetPcBuildsQuery` (campos
> opcionales al final) y se agrega `ReserveCartCommand`. `PcBuildItemInput.Slot` y `PcBuildItemView.Slot` admiten nulo.

El nombre con que viaja por RPC es el FullName: `<namespace>.<Tipo>`. C = comando, Q = consulta.

## Accounting

| Caso de uso (namespace MINV.Application.Accounting) | Tipo | Atributos | Respuesta | Archivo |
|---|---|---|---|---|
| `CreateAccountCommand(string Code, string Name, string ParentCode)` | C | [RequiresPermission(PermissionCodes.AccountingManage)] | `string` | Accounting/AccountingUseCases.cs |
| `CreateJournalEntryCommand(DateOnly Date, string Description, IReadOnlyList<JournalLineSpec> Lines, Guid? BranchId = null)` | C | [RequiresPermission(PermissionCodes.AccountingManage)] | `string` | Accounting/AccountingUseCases.cs |
| `GetChartOfAccountsQuery(DateOnly? From = null, DateOnly? To = null)` | Q | [RequiresPermission(PermissionCodes.AccountingManage)] | `IReadOnlyList<AccountRow>` | Accounting/AccountingUseCases.cs |
| `GetIncomeStatementQuery(DateOnly From, DateOnly To)` | Q | [RequiresPermission(PermissionCodes.AccountingManage)] | `IncomeStatement` | Accounting/AccountingUseCases.cs |
| `GetJournalQuery(DateOnly From, DateOnly To)` | Q | [RequiresPermission(PermissionCodes.AccountingManage)] | `IReadOnlyList<JournalEntryRow>` | Accounting/AccountingUseCases.cs |

## Billing

| Caso de uso (namespace MINV.Application.Billing) | Tipo | Atributos | Respuesta | Archivo |
|---|---|---|---|---|
| `CheckFiscalDocumentStatusCommand(Guid DocumentId)` | C | [RequiresPermission(PermissionCodes.BillingView)] [RequiresModule(LicenseModuleCodes.FiscalSiat)] | `string` | Billing/BillingContracts.cs |
| `CheckSiatCommunicationCommand(Guid? PointOfSaleId = null)` | C | [RequiresPermission(PermissionCodes.BillingView)] [RequiresModule(LicenseModuleCodes.FiscalSiat)] | `string` | Billing/BillingContracts.cs |
| `CloseSiatPointOfSaleCommand(Guid PointOfSaleId)` | C | [RequiresPermission(PermissionCodes.BillingConfigure)] [RequiresModule(LicenseModuleCodes.FiscalSiat)] | `string` | Billing/BillingContracts.cs |
| `CreateSalesReturnCommand(string InvoiceNumber, string Reason, string RefundPaymentMethodCode, IReadOnlyList<ReturnLineInput> Lines, bool Defective = false)` | C | [RequiresPermission(PermissionCodes.PosOperate)] [RequiresPermission(PermissionCodes.BillingVoid)] | `SalesReturnResult` | Billing/BillingContracts.cs |
| `DispatchFiscalDocumentsCommand(Guid? DocumentId = null, int Max = 50)` | C | [RequiresPermission(PermissionCodes.BillingIssue)] [RequiresModule(LicenseModuleCodes.FiscalSiat)] | `DispatchResult` | Billing/BillingContracts.cs |
| `EndContingencyCommand(Guid PointOfSaleId, DateTime? EndedAt = null)` | C | [RequiresPermission(PermissionCodes.BillingContingency)] [RequiresModule(LicenseModuleCodes.FiscalSiat)] | `string` | Billing/BillingContracts.cs |
| `ExportFiscalBookQuery(int Year, int Month, bool Purchases, string Format = "csv")` | Q | [RequiresPermission(PermissionCodes.BillingView)] | `FiscalFile` | Billing/BillingContracts.cs |
| `FindFiscalBuyerQuery(int DocumentType, string DocumentNumber, string? Complement = null)` | Q | [RequiresPermission(PermissionCodes.BillingIssue)] | `FiscalBuyerLookup` | Billing/BillingContracts.cs |
| `GetContingencyCodesQuery` | Q | [RequiresPermission(PermissionCodes.BillingView)] | `IReadOnlyList<ContingencyCodeRow>` | Billing/BillingContracts.cs |
| `GetFiscalDocumentQuery(Guid DocumentId)` | Q | [RequiresPermission(PermissionCodes.BillingView)] | `FiscalDocumentDetail` | Billing/BillingContracts.cs |
| `GetFiscalDocumentsQuery(DateOnly From, DateOnly To, FiscalDocumentStatus? Status = null, FiscalDocumentKind? Kind = null, string? Search = null)` | Q | [RequiresPermission(PermissionCodes.BillingView)] | `IReadOnlyList<FiscalDocumentRow>` | Billing/BillingContracts.cs |
| `GetFiscalPackagesQuery(Guid? EventId = null)` | Q | [RequiresPermission(PermissionCodes.BillingView)] | `IReadOnlyList<FiscalPackageRow>` | Billing/BillingContracts.cs |
| `GetFiscalPrintModelQuery(Guid DocumentId)` | Q | [RequiresPermission(PermissionCodes.BillingView)] | `FiscalPrintModel` | Billing/BillingContracts.cs |
| `GetHomologationQuery` | Q | [RequiresPermission(PermissionCodes.BillingView)] | `HomologationView` | Billing/BillingContracts.cs |
| `GetPosFiscalStateQuery` | Q | [RequiresPermission(PermissionCodes.PosOperate)] | `PosFiscalState` | Billing/BillingContracts.cs |
| `GetPurchasesBookQuery(int Year, int Month)` | Q | [RequiresPermission(PermissionCodes.BillingView)] | `PurchasesBookView` | Billing/BillingContracts.cs |
| `GetReceiptsWithoutInvoiceQuery` | Q | [RequiresPermission(PermissionCodes.PurchasingManage)] | `IReadOnlyList<PendingSupplierInvoiceRow>` | Billing/BillingContracts.cs |
| `GetReturnableLinesQuery(string InvoiceNumber)` | Q | [RequiresPermission(PermissionCodes.SalesView)] | `IReadOnlyList<ReturnableLine>` | Billing/BillingContracts.cs |
| `GetSalesBookQuery(int Year, int Month)` | Q | [RequiresPermission(PermissionCodes.BillingView)] | `SalesBookView` | Billing/BillingContracts.cs |
| `GetSalesReturnsQuery(DateOnly From, DateOnly To)` | Q | [RequiresPermission(PermissionCodes.SalesView)] | `IReadOnlyList<SalesReturnRow>` | Billing/BillingContracts.cs |
| `GetSiatActivitiesQuery` | Q | [RequiresPermission(PermissionCodes.BillingView)] | `IReadOnlyList<SiatActivityView>` | Billing/BillingContracts.cs |
| `GetSiatCatalogQuery(string Catalog)` | Q | [RequiresPermission(PermissionCodes.BillingView)] | `IReadOnlyList<SiatCatalogItemView>` | Billing/BillingContracts.cs |
| `GetSiatServiceCallsQuery(DateOnly From, DateOnly To, string? Operation = null, int Max = 500)` | Q | [RequiresPermission(PermissionCodes.BillingConfigure)] | `IReadOnlyList<SiatServiceCallRow>` | Billing/BillingContracts.cs |
| `GetSiatSettingsQuery` | Q | [RequiresPermission(PermissionCodes.BillingView)] | `SiatSettingsView` | Billing/BillingContracts.cs |
| `GetSiatStatusQuery` | Q | [RequiresPermission(PermissionCodes.BillingView)] | `SiatStatusView` | Billing/BillingContracts.cs |
| `GetSignificantEventsQuery(DateOnly From, DateOnly To)` | Q | [RequiresPermission(PermissionCodes.BillingView)] | `IReadOnlyList<SignificantEventRow>` | Billing/BillingContracts.cs |
| `GetSupplierInvoicesQuery(DateOnly From, DateOnly To)` | Q | [RequiresPermission(PermissionCodes.PurchasingManage)] | `IReadOnlyList<SupplierInvoiceRow>` | Billing/BillingContracts.cs |
| `GetTaxSummaryQuery(int Year, int Month)` | Q | [RequiresPermission(PermissionCodes.BillingView)] | `TaxSummaryView` | Billing/BillingContracts.cs |
| `GoOfflineCommand(Guid PointOfSaleId, int? EventCode = null)` | C | [RequiresPermission(PermissionCodes.BillingContingency)] [RequiresModule(LicenseModuleCodes.FiscalSiat)] | `string` | Billing/BillingContracts.cs |
| `LinkPointOfSaleRegisterCommand(Guid PointOfSaleId, string? RegisterCode)` | C | [RequiresPermission(PermissionCodes.BillingConfigure)] [RequiresModule(LicenseModuleCodes.FiscalSiat)] | `string` | Billing/BillingContracts.cs |
| `PrepareSiatCommand` | C | [RequiresPermission(PermissionCodes.BillingConfigure)] [RequiresModule(LicenseModuleCodes.FiscalSiat)] | `SiatMaintenanceResult` | Billing/BillingContracts.cs |
| `RecordFiscalDeliveryCommand(Guid DocumentId, FiscalDeliveryChannel Channel, string? Recipient = null)` | C | [RequiresPermission(PermissionCodes.BillingView)] | `string` | Billing/BillingContracts.cs |
| `RecoverPointOfSaleCommand(Guid PointOfSaleId)` | C | [RequiresPermission(PermissionCodes.BillingContingency)] [RequiresModule(LicenseModuleCodes.FiscalSiat)] | `string` | Billing/BillingContracts.cs |
| `RegisterContingencyCodeCommand(string BranchCode, int DocumentSector, string Code, long NumberFrom, long NumberTo, DateOnly? ValidUntil)` | C | [RequiresPermission(PermissionCodes.BillingContingency)] [RequiresModule(LicenseModuleCodes.FiscalSiat)] | `string` | Billing/BillingContracts.cs |
| `RegisterSiatPointOfSaleCommand(string BranchCode, string Name, string? Description, string? RegisterCode, int TypeCode = SiatCodes.PointOfSaleCashier)` | C | [RequiresPermission(PermissionCodes.BillingConfigure)] [RequiresModule(LicenseModuleCodes.FiscalSiat)] | `SiatPointOfSaleStatus` | Billing/BillingContracts.cs |
| `RegisterSupplierInvoiceCommand(string ReceiptNumber, string InvoiceNumber, string AuthorizationCode, DateOnly InvoiceDate, decimal TotalAmount, decimal Discounts = 0, decimal NotSubjectToVat = 0, int PurchaseType = 1, string? ControlCode = null)` | C | [RequiresPermission(PermissionCodes.PurchasingManage)] | `string` | Billing/BillingContracts.cs |
| `ReissueFiscalDocumentCommand(Guid DocumentId, FiscalBuyerInput? Buyer = null)` | C | [RequiresPermission(PermissionCodes.BillingIssue)] [RequiresModule(LicenseModuleCodes.FiscalSiat)] | `FiscalDocumentRow` | Billing/BillingContracts.cs |
| `RenderFiscalDocumentQuery(Guid DocumentId, FiscalDeliveryChannel Format = FiscalDeliveryChannel.Pdf, int Columns = 48)` | Q | [RequiresPermission(PermissionCodes.BillingView)] | `FiscalFile` | Billing/BillingContracts.cs |
| `RequestCufdCommand(Guid PointOfSaleId)` | C | [RequiresPermission(PermissionCodes.BillingConfigure)] [RequiresModule(LicenseModuleCodes.FiscalSiat)] | `string` | Billing/BillingContracts.cs |
| `RequestCuisCommand(Guid PointOfSaleId)` | C | [RequiresPermission(PermissionCodes.BillingConfigure)] [RequiresModule(LicenseModuleCodes.FiscalSiat)] | `string` | Billing/BillingContracts.cs |
| `RevertFiscalVoidCommand(Guid DocumentId)` | C | [RequiresPermission(PermissionCodes.BillingVoid)] [RequiresModule(LicenseModuleCodes.FiscalSiat)] | `string` | Billing/BillingContracts.cs |
| `RunSiatWorkCommand(bool Maintain = true)` | C | [RequiresPermission(PermissionCodes.BillingIssue)] [RequiresModule(LicenseModuleCodes.FiscalSiat)] | `SiatWorkResult` | Billing/BillingContracts.cs |
| `SaveMailSettingsCommand(string Host, int Port, bool UseSsl, string? UserName, string? NewPassword, string FromAddress, string FromName, bool Enabled)` | C | [RequiresPermission(PermissionCodes.BillingConfigure)] [RequiresModule(LicenseModuleCodes.FiscalSiat)] | `string` | Billing/BillingContracts.cs |
| `SavePaymentMethodHomologationCommand(string PaymentMethodCode, int SinCode)` | C | [RequiresPermission(PermissionCodes.BillingConfigure)] [RequiresModule(LicenseModuleCodes.FiscalSiat)] | `string` | Billing/BillingContracts.cs |
| `SaveProductHomologationCommand(IReadOnlyList<ProductHomologationInput> Items)` | C | [RequiresPermission(PermissionCodes.BillingConfigure)] [RequiresModule(LicenseModuleCodes.FiscalSiat)] | `string` | Billing/BillingContracts.cs |
| `SaveSiatBranchCommand(string BranchCode, int SiatCode, string Municipality, string? Phone)` | C | [RequiresPermission(PermissionCodes.BillingConfigure)] [RequiresModule(LicenseModuleCodes.FiscalSiat)] | `string` | Billing/BillingContracts.cs |
| `SaveSiatProfileCommand(int Environment, SiatEndpointSet Endpoints, string QrBaseUrl, int TimeoutSeconds, string? NewToken, DateOnly? TokenValidUntil)` | C | [RequiresPermission(PermissionCodes.BillingConfigure)] [RequiresModule(LicenseModuleCodes.FiscalSiat)] | `string` | Billing/BillingContracts.cs |
| `SaveSiatSettingsCommand(long Nit, string BusinessName, string SystemCode, int Environment, string? OnlineLegend, string? OfflineLegend, bool Enabled)` | C | [RequiresPermission(PermissionCodes.BillingConfigure)] [RequiresModule(LicenseModuleCodes.FiscalSiat)] | `string` | Billing/BillingContracts.cs |
| `SaveUnitHomologationCommand(string UnitCode, int SinUnitCode)` | C | [RequiresPermission(PermissionCodes.BillingConfigure)] [RequiresModule(LicenseModuleCodes.FiscalSiat)] | `string` | Billing/BillingContracts.cs |
| `SearchSiatProductsQuery(string? ActivityCode, string? Text, int Max = 100)` | Q | [RequiresPermission(PermissionCodes.BillingView)] | `IReadOnlyList<SiatProductView>` | Billing/BillingContracts.cs |
| `SendFiscalDocumentEmailCommand(Guid DocumentId, string? Email = null)` | C | [RequiresPermission(PermissionCodes.BillingIssue)] [RequiresModule(LicenseModuleCodes.FiscalSiat)] | `string` | Billing/BillingContracts.cs |
| `StartManualContingencyCommand(Guid PointOfSaleId, int EventCode, string? Description, DateTime? StartedAt, string CafcCode)` | C | [RequiresPermission(PermissionCodes.BillingContingency)] [RequiresModule(LicenseModuleCodes.FiscalSiat)] | `string` | Billing/BillingContracts.cs |
| `SuggestProductHomologationQuery(string ActivityCode)` | Q | [RequiresPermission(PermissionCodes.BillingConfigure)] [RequiresModule(LicenseModuleCodes.FiscalSiat)] | `IReadOnlyList<ProductHomologationInput>` | Billing/BillingContracts.cs |
| `SyncSiatCatalogsCommand(string? Catalog = null)` | C | [RequiresPermission(PermissionCodes.BillingConfigure)] [RequiresModule(LicenseModuleCodes.FiscalSiat)] | `SiatSyncResult` | Billing/BillingContracts.cs |
| `TranscribeManualInvoiceCommand(Guid SignificantEventId, long Number, DateTime IssuedAt, FiscalBuyerInput Buyer, string PaymentMethodCode, IReadOnlyList<Sales.SaleLineInput> Lines)` | C | [RequiresPermission(PermissionCodes.BillingContingency)] [RequiresModule(LicenseModuleCodes.FiscalSiat)] | `FiscalDocumentRow` | Billing/BillingContracts.cs |
| `VerifyNitCommand(long Nit, string? CustomerCode = null)` | C | [RequiresPermission(PermissionCodes.BillingIssue)] [RequiresModule(LicenseModuleCodes.FiscalSiat)] | `NitCheckResult` | Billing/BillingContracts.cs |
| `VoidFiscalDocumentCommand(Guid DocumentId, int ReasonCode, bool ReturnGoods, string? Note = null)` | C | [RequiresPermission(PermissionCodes.BillingVoid)] [RequiresModule(LicenseModuleCodes.FiscalSiat)] | `string` | Billing/BillingContracts.cs |
| `GetBillingAccessQuery` | Q | **SIN ATRIBUTOS** | `BillingAccessView` | Billing/DesktopQueries.cs |
| `GetCustomerFiscalIdentitiesQuery` | Q | [RequiresPermission(PermissionCodes.StockView)] | `IReadOnlyList<CustomerFiscalIdentityRow>` | Billing/DesktopQueries.cs |
| `GetSalesFiscalStatusQuery(DateOnly From, DateOnly To)` | Q | [RequiresPermission(PermissionCodes.SalesView)] | `IReadOnlyList<SaleFiscalStatusRow>` | Billing/DesktopQueries.cs |
| `SaveCustomerFiscalIdentityCommand(string Code, int? DocumentType, string? DocumentNumber, string? Complement)` | C | [RequiresPermission(PermissionCodes.CustomersManage)] | `string` | Billing/DesktopQueries.cs |

## Catalog

| Caso de uso (namespace MINV.Application.Catalog) | Tipo | Atributos | Respuesta | Archivo |
|---|---|---|---|---|
| `GetCatalogOptionsQuery` | Q | [RequiresPermission(PermissionCodes.StockView)] | `CatalogOptions` | Catalog/CatalogUseCases.cs |
| `GetCatalogQuery(IReadOnlyList<SpecFilter>? SpecFilters = null, string? CategoryCode = null)` | Q | [RequiresPermission(PermissionCodes.StockView)] | `IReadOnlyList<CatalogItem>` | Catalog/CatalogUseCases.cs |
| `GetProductImagesQuery(IReadOnlyList<Guid>? VariantIds = null)` | Q | [RequiresPermission(PermissionCodes.StockView)] | `IReadOnlyList<ProductImageData>` | Catalog/CatalogUseCases.cs |
| `RemoveProductImageCommand(string Sku)` | C | [RequiresPermission(PermissionCodes.CatalogManage)] | `bool` | Catalog/CatalogUseCases.cs |
| `SaveCategoryCommand(string Code, string Name, string? ParentCode = null)` | C | [RequiresPermission(PermissionCodes.CatalogManage)] | `string` | Catalog/CatalogUseCases.cs |
| `SaveProductCommand( string? OriginalSku, string Sku, string Name, string? Description, string CategoryCode, string UnitCode, string? SupplierCode, decimal Minimum, decimal Maximum, decimal UnitCost, decimal SalePrice, string? Barcode, bool IsActive, string? BinCode = null)` | C | [RequiresPermission(PermissionCodes.CatalogManage)] | `string` | Catalog/CatalogUseCases.cs |
| `SetProductImageCommand(string Sku, byte[] Content, string ContentType, string? FileName)` | C | [RequiresPermission(PermissionCodes.CatalogManage)] | `bool` | Catalog/CatalogUseCases.cs |

## Corporate

| Caso de uso (namespace MINV.Application.Corporate) | Tipo | Atributos | Respuesta | Archivo |
|---|---|---|---|---|
| `GetBranchReportQuery(DateOnly From, DateOnly To)` | Q | [RequiresModule(LicenseModuleCodes.GlobalAudit)] [RequiresPermission(PermissionCodes.ReportsView)] | `BranchReport` | Corporate/BranchReportUseCases.cs |
| `AssignUserBranchesCommand(string Email, IReadOnlyList<string> BranchCodes)` | C | [RequiresModule(LicenseModuleCodes.MultiBranch)] [RequiresPermission(PermissionCodes.BranchesManage)] | `string` | Corporate/BranchUseCases.cs |
| `ConsolidatedStockQuery(string? Search = null)` | Q | [RequiresPermission(PermissionCodes.StockView)] | `ConsolidatedStock` | Corporate/BranchUseCases.cs |
| `CreateBranchCommand(string Code, string Name, string WarehouseCode, string WarehouseName, bool CreatePosRegister = true)` | C | [RequiresModule(LicenseModuleCodes.MultiBranch)] [RequiresPermission(PermissionCodes.BranchesManage)] | `string` | Corporate/BranchUseCases.cs |
| `GetBranchesQuery` | Q | [RequiresPermission(PermissionCodes.StockView)] | `IReadOnlyList<BranchRow>` | Corporate/BranchUseCases.cs |
| `UpdateBranchCommand(string Code, string Name, bool IsActive)` | C | [RequiresModule(LicenseModuleCodes.MultiBranch)] [RequiresPermission(PermissionCodes.BranchesManage)] | `string` | Corporate/BranchUseCases.cs |

## Iam

| Caso de uso (namespace MINV.Application.Iam) | Tipo | Atributos | Respuesta | Archivo |
|---|---|---|---|---|
| `GetCompanySettingsQuery` | Q | [RequiresPermission(PermissionCodes.StockView)] | `CompanySettings` | Iam/AdminUseCases.cs |
| `GetRolesQuery` | Q | [RequiresPermission(PermissionCodes.UsersManage)] | `RolesView` | Iam/AdminUseCases.cs |
| `GetUsersQuery` | Q | [RequiresPermission(PermissionCodes.UsersManage)] | `IReadOnlyList<UserRow>` | Iam/AdminUseCases.cs |
| `ResetUserPasswordCommand(string Email, string NewPassword, bool MustChange = true)` | C | [RequiresPermission(PermissionCodes.UsersManage)] | `bool` | Iam/AdminUseCases.cs |
| `SaveUserCommand(string? OriginalEmail, string Email, string Name, string RoleCode, bool IsActive, string? NewPassword, IReadOnlyList<string>? BranchCodes = null)` | C | [RequiresPermission(PermissionCodes.UsersManage)] | `string` | Iam/AdminUseCases.cs |
| `UpdateCompanySettingsCommand(decimal AlertMargin, int DaysWithoutRotation)` | C | [RequiresPermission(PermissionCodes.UsersManage)] | `bool` | Iam/AdminUseCases.cs |
| `ChangePasswordCommand(string CurrentPassword, string NewPassword)` | C | **SIN ATRIBUTOS** | `bool` | Iam/IamUseCases.cs |
| `GetActivityQuery(int Take = 200)` | Q | [RequiresPermission(PermissionCodes.AuditView)] | `IReadOnlyList<ActivityRow>` | Iam/IamUseCases.cs |
| `LoginCommand(string TenantCode, string Email, string Password, string MachineName, string ClientVersion, string? HardwareFingerprint = null)` | Q | **SIN ATRIBUTOS** | `LoginResult` | Iam/IamUseCases.cs |
| `LogoutCommand(Guid SessionId)` | C | **SIN ATRIBUTOS** | `bool` | Iam/IamUseCases.cs |
| `SelectBranchCommand(Guid SessionId, Guid? BranchId)` | C | **SIN ATRIBUTOS** | `BranchAccess` | Iam/UserAccess.cs |

## Integration

| Caso de uso (namespace MINV.Application.Integration) | Tipo | Atributos | Respuesta | Archivo |
|---|---|---|---|---|
| `CreateExternalOrderCommand(string ExternalId, string CustomerCode, string PaymentMethodCode, IReadOnlyList<SaleLineInput> Lines, string? PaymentReference = null, string? WarehouseCode = null, Billing.FiscalBuyerInput? Buyer = null)` | C | [RequiresModule(LicenseModuleCodes.ApiIntegrations)] [RequiresPermission(PermissionCodes.PosOperate)] [RequiresPermission(PermissionCodes.MovementsRegisterSales)] | `ExternalOrderResult` | Integration/ExternalOrderUseCases.cs |
| `GetApiCatalogQuery(int PageNumber = 1, int PageSize = 100, string? Search = null)` | Q | [RequiresPermission(PermissionCodes.StockView)] | `Page<ApiProduct>` | Integration/ExternalOrderUseCases.cs |
| `GetApiStockQuery(string? BranchCode = null, string? Sku = null, int PageNumber = 1, int PageSize = 100)` | Q | [RequiresPermission(PermissionCodes.StockView)] | `Page<ApiStock>` | Integration/ExternalOrderUseCases.cs |
| `GetExternalOrderQuery(string ExternalId)` | Q | [RequiresModule(LicenseModuleCodes.ApiIntegrations)] [RequiresPermission(PermissionCodes.SalesView)] | `ExternalOrderResult` | Integration/ExternalOrderUseCases.cs |
| `CreateApiKeyCommand(string Name, IReadOnlyList<string> Scopes, string? BranchCode = null, int? ExpiresInDays = null)` | C | [RequiresModule(LicenseModuleCodes.ApiIntegrations)] [RequiresPermission(PermissionCodes.IntegrationManage)] | `CreatedApiKey` | Integration/IntegrationUseCases.cs |
| `CreateWebhookCommand(string Url, IReadOnlyList<string> Events, string? Description = null, string? BranchCode = null)` | C | [RequiresModule(LicenseModuleCodes.ApiIntegrations)] [RequiresPermission(PermissionCodes.IntegrationManage)] | `CreatedWebhook` | Integration/IntegrationUseCases.cs |
| `DisableWebhookCommand(Guid Id)` | C | [RequiresPermission(PermissionCodes.IntegrationManage)] | `string` | Integration/IntegrationUseCases.cs |
| `GetApiKeysQuery` | Q | [RequiresPermission(PermissionCodes.IntegrationManage)] | `IReadOnlyList<ApiKeyRow>` | Integration/IntegrationUseCases.cs |
| `GetIntegrationCatalogQuery` | Q | [RequiresPermission(PermissionCodes.IntegrationManage)] | `IntegrationCatalog` | Integration/IntegrationUseCases.cs |
| `GetWebhookDeliveriesQuery(Guid? EndpointId = null, int Take = 200)` | Q | [RequiresPermission(PermissionCodes.IntegrationManage)] | `IReadOnlyList<WebhookDeliveryRow>` | Integration/IntegrationUseCases.cs |
| `GetWebhooksQuery` | Q | [RequiresPermission(PermissionCodes.IntegrationManage)] | `IReadOnlyList<WebhookRow>` | Integration/IntegrationUseCases.cs |
| `RevokeApiKeyCommand(Guid Id)` | C | [RequiresPermission(PermissionCodes.IntegrationManage)] | `string` | Integration/IntegrationUseCases.cs |
| `RotateWebhookSecretCommand(Guid Id)` | C | [RequiresPermission(PermissionCodes.IntegrationManage)] | `CreatedWebhook` | Integration/IntegrationUseCases.cs |

## Inventory

| Caso de uso (namespace MINV.Application.Inventory.Movements) | Tipo | Atributos | Respuesta | Archivo |
|---|---|---|---|---|
| `RegisterMovementCommand( string Sku, string BinCode, string MovementTypeCode, decimal Quantity, DateOnly? BusinessDate = null, string? DocumentReference = null, string? Notes = null, string? LotNumber = null, string? AdjustmentReasonCode = null, IReadOnlyList<string>? Serials = null)` | C | **SIN ATRIBUTOS** | `RegisterMovementResult` | Inventory/Movements/RegisterMovement.cs |
| `OpenPhysicalCountCommand(string WarehouseCode, DateOnly? CountDate = null, string? Notes = null)` | C | [RequiresPermission(PermissionCodes.PhysicalCountRecord)] | `OpenPhysicalCountResult` | Inventory/PhysicalCounts/PhysicalCountCommands.cs |
| `PostPhysicalCountCommand(Guid PhysicalCountId, bool Confirmed)` | C | [RequiresPermission(PermissionCodes.PhysicalCountPost)] | `PostPhysicalCountResult` | Inventory/PhysicalCounts/PhysicalCountCommands.cs |
| `RecordCountCommand(Guid PhysicalCountId, string Sku, string BinCode, decimal CountedQuantity, string? LotNumber = null)` | Q | [RequiresPermission(PermissionCodes.PhysicalCountRecord)] | `Guid` | Inventory/PhysicalCounts/PhysicalCountCommands.cs |
| `CancelPhysicalCountCommand(Guid PhysicalCountId)` | C | [RequiresPermission(PermissionCodes.PhysicalCountPost)] | `string` | Inventory/PhysicalCounts/PhysicalCountQueries.cs |
| `GetOpenPhysicalCountQuery(string? WarehouseCode = null)` | Q | [RequiresPermission(PermissionCodes.PhysicalCountRecord)] | `PhysicalCountSheet?` | Inventory/PhysicalCounts/PhysicalCountQueries.cs |
| `RemoveCountCommand(Guid PhysicalCountId, string Sku, string BinCode, string? LotNumber = null)` | C | [RequiresPermission(PermissionCodes.PhysicalCountRecord)] | `bool` | Inventory/PhysicalCounts/PhysicalCountQueries.cs |
| `GetStockProjectionQuery(string? WarehouseCode = null)` | Q | [RequiresPermission(PermissionCodes.StockView)] | `StockProjectionView` | Inventory/Queries/GetStockProjection.cs |
| `GetStockReservationsQuery(string? WarehouseCode = null)` | Q | [RequiresPermission(PermissionCodes.StockView)] | `IReadOnlyList<ReservedStockRow>` | Inventory/Queries/GetStockReservations.cs |
| `GetBinsQuery(string? WarehouseCode = null)` | Q | [RequiresPermission(PermissionCodes.StockView)] | `IReadOnlyList<BinItem>` | Inventory/Queries/InventoryReadQueries.cs |
| `GetMovementTrendQuery(int Days = 14)` | Q | [RequiresPermission(PermissionCodes.StockView)] | `IReadOnlyList<MovementTrendDay>` | Inventory/Queries/InventoryReadQueries.cs |
| `GetMovementTypesQuery` | Q | [RequiresPermission(PermissionCodes.StockView)] | `IReadOnlyList<MovementTypeItem>` | Inventory/Queries/InventoryReadQueries.cs |
| `GetProductCardQuery(string SkuOrBarcode, int Take = 200)` | Q | [RequiresPermission(PermissionCodes.StockView)] | `ProductCard` | Inventory/Queries/InventoryReadQueries.cs |
| `GetProductLookupQuery(bool IncludeInactive = false)` | Q | [RequiresPermission(PermissionCodes.StockView)] | `IReadOnlyList<ProductLookupItem>` | Inventory/Queries/InventoryReadQueries.cs |
| `GetRecentMovementsQuery(int Take = 20)` | Q | [RequiresPermission(PermissionCodes.StockView)] | `IReadOnlyList<RecentMovement>` | Inventory/Queries/InventoryReadQueries.cs |
| `GetWorkspaceQuery` | Q | [RequiresPermission(PermissionCodes.StockView)] | `WorkspaceInfo` | Inventory/Queries/InventoryReadQueries.cs |
| `CancelTransferCommand(Guid Id, string Reason)` | C | [RequiresModule(LicenseModuleCodes.MultiBranch)] [RequiresPermission(PermissionCodes.TransfersManage)] | `TransferRef` | Inventory/Transfers/TransferUseCases.cs |
| `CreateTransferCommand(string ToWarehouseCode, IReadOnlyList<TransferLineInput> Lines, string? Notes = null, string? FromWarehouseCode = null)` | C | [RequiresModule(LicenseModuleCodes.MultiBranch)] [RequiresPermission(PermissionCodes.TransfersManage)] | `TransferRef` | Inventory/Transfers/TransferUseCases.cs |
| `DispatchTransferCommand(Guid Id)` | C | [RequiresModule(LicenseModuleCodes.MultiBranch)] [RequiresPermission(PermissionCodes.TransfersManage)] | `TransferRef` | Inventory/Transfers/TransferUseCases.cs |
| `GetTransferQuery(Guid Id)` | Q | [RequiresPermission(PermissionCodes.StockView)] | `TransferDetail` | Inventory/Transfers/TransferUseCases.cs |
| `GetTransfersQuery(TransferStatus? Status = null, int Take = 300)` | Q | [RequiresPermission(PermissionCodes.StockView)] | `IReadOnlyList<TransferRow>` | Inventory/Transfers/TransferUseCases.cs |
| `ReceiveTransferCommand(Guid Id, IReadOnlyList<TransferReceiptInput>? Lines = null)` | C | [RequiresModule(LicenseModuleCodes.MultiBranch)] [RequiresPermission(PermissionCodes.TransfersManage)] | `TransferRef` | Inventory/Transfers/TransferUseCases.cs |

## Partners

| Caso de uso (namespace MINV.Application.Partners) | Tipo | Atributos | Respuesta | Archivo |
|---|---|---|---|---|
| `GetCustomersQuery` | Q | [RequiresPermission(PermissionCodes.StockView)] | `CustomersView` | Partners/PartnerUseCases.cs |
| `GetSuppliersQuery` | Q | [RequiresPermission(PermissionCodes.StockView)] | `IReadOnlyList<SupplierRow>` | Partners/PartnerUseCases.cs |
| `SaveCustomerCommand(string? Code, string Name, string? TaxId, string? Email, string? Phone, string CategoryCode, bool IsActive)` | C | [RequiresPermission(PermissionCodes.CustomersManage)] | `string` | Partners/PartnerUseCases.cs |
| `SaveSupplierCommand(string? Code, string Name, string? TaxId, int LeadTimeDays, string? ContactName, string? Phone, string? Email, bool IsActive)` | C | [RequiresPermission(PermissionCodes.PurchasingManage)] | `string` | Partners/PartnerUseCases.cs |

## Purchasing

| Caso de uso (namespace MINV.Application.Purchasing) | Tipo | Atributos | Respuesta | Archivo |
|---|---|---|---|---|
| `ApprovePurchaseOrderCommand(Guid Id)` | C | [RequiresPermission(PermissionCodes.PurchasingManage)] | `string` | Purchasing/PurchasingUseCases.cs |
| `CancelPurchaseOrderCommand(Guid Id)` | C | [RequiresPermission(PermissionCodes.PurchasingManage)] | `string` | Purchasing/PurchasingUseCases.cs |
| `CreatePurchaseOrderCommand(string SupplierCode, DateOnly? ExpectedDate, string? Notes, IReadOnlyList<PurchaseLineInput> Lines)` | C | [RequiresPermission(PermissionCodes.PurchasingManage)] | `PurchaseOrderRow` | Purchasing/PurchasingUseCases.cs |
| `CreateSuggestedPurchaseOrdersCommand` | C | [RequiresPermission(PermissionCodes.PurchasingManage)] | `IReadOnlyList<string>` | Purchasing/PurchasingUseCases.cs |
| `GetPurchaseOrderQuery(Guid Id)` | Q | [RequiresPermission(PermissionCodes.StockView)] | `PurchaseOrderDetail` | Purchasing/PurchasingUseCases.cs |
| `GetPurchaseOrdersQuery(PurchaseOrderStatus? Status = null)` | Q | [RequiresPermission(PermissionCodes.StockView)] | `IReadOnlyList<PurchaseOrderRow>` | Purchasing/PurchasingUseCases.cs |
| `ReceivePurchaseOrderCommand(Guid Id, string? SupplierDocument = null, IReadOnlyList<SkuSerials>? Serials = null)` | C | [RequiresPermission(PermissionCodes.PurchasingManage)] [RequiresPermission(PermissionCodes.MovementsRegisterWarehouse)] | `ReceiptResult` | Purchasing/PurchasingUseCases.cs |

## Reports

| Caso de uso (namespace MINV.Application.Reports) | Tipo | Atributos | Respuesta | Archivo |
|---|---|---|---|---|
| `GetMovementsReportQuery(DateOnly From, DateOnly To, string? TypeCode = null)` | Q | [RequiresPermission(PermissionCodes.ReportsView)] | `IReadOnlyList<MovementReportRow>` | Reports/ReportQueries.cs |
| `GetPurchasesReportQuery(DateOnly From, DateOnly To)` | Q | [RequiresPermission(PermissionCodes.ReportsView)] | `PurchasesReport` | Reports/ReportQueries.cs |
| `GetSalesReportQuery(DateOnly From, DateOnly To)` | Q | [RequiresPermission(PermissionCodes.ReportsView)] | `SalesReport` | Reports/ReportQueries.cs |

## Sales

| Caso de uso (namespace MINV.Application.Sales) | Tipo | Atributos | Respuesta | Archivo |
|---|---|---|---|---|
| `ClosePosSessionCommand(Guid SessionId, decimal CountedCash)` | C | [RequiresModule(LicenseModuleCodes.PosHardware)] [RequiresPermission(PermissionCodes.PosOperate)] | `decimal` | Sales/PosUseCases.cs |
| `OpenPosSessionCommand(string RegisterCode, decimal OpeningCash)` | C | [RequiresModule(LicenseModuleCodes.PosHardware)] [RequiresPermission(PermissionCodes.PosOperate)] | `Guid` | Sales/PosUseCases.cs |
| `CheckoutCommand(string CustomerCode, string PaymentMethodCode, IReadOnlyList<SaleLineInput> Lines, decimal? CashReceived = null, string? PaymentReference = null, Billing.FiscalBuyerInput? Buyer = null, string? CardNumber = null)` | C | [RequiresPermission(PermissionCodes.PosOperate)] [RequiresPermission(PermissionCodes.MovementsRegisterSales)] | `CheckoutResult` | Sales/SalesUseCases.cs |
| `GetPosStateQuery` | Q | [RequiresPermission(PermissionCodes.PosOperate)] | `PosState` | Sales/SalesUseCases.cs |
| `GetSaleLinesQuery(string InvoiceNumber)` | Q | [RequiresPermission(PermissionCodes.SalesView)] | `IReadOnlyList<SaleLineRow>` | Sales/SalesUseCases.cs |
| `GetSalesQuery(DateOnly From, DateOnly To)` | Q | [RequiresPermission(PermissionCodes.SalesView)] | `IReadOnlyList<SaleRow>` | Sales/SalesUseCases.cs |
| `GetSellableProductsQuery` | Q | [RequiresPermission(PermissionCodes.StockView)] | `IReadOnlyList<SellableProduct>` | Sales/SalesUseCases.cs |
| `VoidSaleCommand(string InvoiceNumber, string Reason)` | C | [RequiresPermission(PermissionCodes.PosOperate)] [RequiresPermission(PermissionCodes.SalesView)] | `string` | Sales/SalesUseCases.cs |

## Storefront

| Caso de uso (namespace MINV.Application.Storefront) | Tipo | Atributos | Respuesta | Archivo |
|---|---|---|---|---|
| `CancelStorefrontReservationCommand(string Number, string Phone)` | C | [RequiresPermission(PermissionCodes.StorefrontReserve)] | `StorefrontReservationView` | Storefront/StorefrontContracts.cs |
| `CreateStorefrontReservationCommand(IReadOnlyList<StorefrontReservationLineInput> Lines, StorefrontContactInput Contact, string? Notes, string IdempotencyKey, string? Name = null, PcBuildKind Kind = PcBuildKind.Build, int? HoldDays = null, ReservationBuyerInput? Buyer = null)` | C | [RequiresPermission(PermissionCodes.StorefrontReserve)] | `StorefrontReservationResult` | Storefront/StorefrontContracts.cs |
| `ExpirePcBuildReservationsCommand` | C | [RequiresPermission(PermissionCodes.StorefrontReserve)] | `int` | Storefront/StorefrontContracts.cs |
| `GetStorefrontCatalogQuery` | Q | [RequiresPermission(PermissionCodes.StorefrontRead)] | `StorefrontCatalogView` | Storefront/StorefrontContracts.cs |
| `GetStorefrontPresetsQuery` | Q | [RequiresPermission(PermissionCodes.StorefrontRead)] | `IReadOnlyList<StorefrontPreset>` | Storefront/StorefrontContracts.cs |
| `GetStorefrontProductImageQuery(string Sku)` | Q | [RequiresPermission(PermissionCodes.StorefrontRead)] | `StorefrontImage` | Storefront/StorefrontContracts.cs |
| `GetStorefrontProductQuery(string Slug)` | Q | [RequiresPermission(PermissionCodes.StorefrontRead)] | `StorefrontProduct` | Storefront/StorefrontContracts.cs |
| `GetStorefrontReservationQuery(string Number, string Phone)` | Q | [RequiresPermission(PermissionCodes.StorefrontRead)] | `StorefrontReservationView` | Storefront/StorefrontContracts.cs |

## Tech

| Caso de uso (namespace MINV.Application.Tech) | Tipo | Atributos | Respuesta | Archivo |
|---|---|---|---|---|
| `AddWarrantyClaimNoteCommand(string Number, string Note)` | C | [RequiresPermission(PermissionCodes.ServiceOpen)] | `WarrantyClaimRow` | Tech/TechContracts.cs |
| `CancelPcBuildCommand(string Number, string? Reason = null)` | C | [RequiresPermission(PermissionCodes.PcBuildManage)] | `string` | Tech/TechContracts.cs |
| `CheckPcBuildQuery(IReadOnlyList<PcBuildItemInput> Items)` | Q | [RequiresPermission(PermissionCodes.StockView)] | `PcBuildCheckView` | Tech/TechContracts.cs |
| `DisposeSerialCommand(string Serial, SerialDisposal Disposal, string Reason, string? Sku = null)` | C | [RequiresPermission(PermissionCodes.SerialsManage)] | `string` | Tech/TechContracts.cs |
| `GetAvailableSerialsQuery(string Sku, string? WarehouseCode = null)` | Q | [RequiresPermission(PermissionCodes.SerialsView)] | `IReadOnlyList<SerialRow>` | Tech/TechContracts.cs |
| `GetPcBuildCandidatesQuery(PcSlot Slot, IReadOnlyList<PcBuildItemInput> Current, string? Text = null, bool OnlyInStock = false, string? CategoryCode = null)` | Q | [RequiresPermission(PermissionCodes.StockView)] | `IReadOnlyList<PcBuildCandidate>` | Tech/TechContracts.cs |
| `GetPcBuildQuery(string Number)` | Q | [RequiresPermission(PermissionCodes.SalesView)] | `PcBuildDetail` | Tech/TechContracts.cs |
| `GetPcBuildsQuery(PcBuildStatus? Status = null, PcBuildChannel? Channel = null, PcBuildKind? Kind = null)` | Q | [RequiresPermission(PermissionCodes.SalesView)] | `IReadOnlyList<PcBuildRow>` | Tech/TechContracts.cs |
| `GetProductTechQuery(string Sku)` | Q | [RequiresPermission(PermissionCodes.StockView)] | `ProductTechView` | Tech/TechContracts.cs |
| `GetSerialSummaryQuery` | Q | [RequiresPermission(PermissionCodes.SerialsView)] | `SerialSummaryView` | Tech/TechContracts.cs |
| `GetSerialTraceQuery(string Serial, string? Sku = null)` | Q | [RequiresPermission(PermissionCodes.SerialsView)] | `SerialTraceView` | Tech/TechContracts.cs |
| `GetSpecDefinitionsQuery(string? CategoryCode = null)` | Q | [RequiresPermission(PermissionCodes.StockView)] | `IReadOnlyList<SpecDefinitionView>` | Tech/TechContracts.cs |
| `GetSpecFacetsQuery(string CategoryCode)` | Q | [RequiresPermission(PermissionCodes.StockView)] | `IReadOnlyList<SpecFacet>` | Tech/TechContracts.cs |
| `GetTechDashboardQuery(int Days = 30, string GpuCategoryCode = "GPU", string ConsoleCategoryCode = "CON")` | Q | [RequiresPermission(PermissionCodes.ReportsView)] | `TechDashboardView` | Tech/TechContracts.cs |
| `GetWarrantyClaimQuery(string Number)` | Q | [RequiresPermission(PermissionCodes.SerialsView)] | `WarrantyClaimDetail` | Tech/TechContracts.cs |
| `GetWarrantyClaimsQuery(WarrantyClaimStatus? Status = null, bool OnlyOpen = false)` | Q | [RequiresPermission(PermissionCodes.SerialsView)] | `IReadOnlyList<WarrantyClaimRow>` | Tech/TechContracts.cs |
| `GetWarrantyStatusQuery(string Serial, string? Sku = null)` | Q | [RequiresPermission(PermissionCodes.SerialsView)] | `WarrantyStatusView` | Tech/TechContracts.cs |
| `IssueWarrantyReplacementCommand(string Number, string ReplacementSerial, string? Resolution = null)` | C | [RequiresPermission(PermissionCodes.ServiceManage)] | `WarrantyClaimRow` | Tech/TechContracts.cs |
| `MoveWarrantyClaimCommand(string Number, WarrantyClaimStatus Next, string? Resolution = null, string? SupplierCode = null, string? Note = null)` | C | [RequiresPermission(PermissionCodes.ServiceManage)] | `WarrantyClaimRow` | Tech/TechContracts.cs |
| `OpenWarrantyClaimCommand(string Serial, string Issue, bool ChargeableRepair = false, string? CustomerCode = null, string? Sku = null)` | C | [RequiresPermission(PermissionCodes.ServiceOpen)] | `WarrantyClaimRow` | Tech/TechContracts.cs |
| `PublishPcBuildCommand(string Number, bool Published = true)` | C | [RequiresPermission(PermissionCodes.PcBuildManage)] | `PcBuildRow` | Tech/TechContracts.cs |
| `RegisterStockSerialsCommand(string Sku, IReadOnlyList<string> Serials, string? Note = null)` | C | [RequiresPermission(PermissionCodes.SerialsManage)] | `string` | Tech/TechContracts.cs |
| `ReleasePcBuildReservationCommand(string Number, string Reason)` | C | [RequiresPermission(PermissionCodes.PcBuildManage)] | `PcBuildRow` | Tech/TechContracts.cs |
| `ReserveCartCommand(IReadOnlyList<CartItemInput> Items, string ContactName, string ContactPhone, string? ContactEmail = null, string? Notes = null, int? HoldDays = null, Storefront.ReservationBuyerInput? Buyer = null, string? CustomerCode = null, string? Name = null)` | C | [RequiresPermission(PermissionCodes.PcBuildManage)] | `PcBuildRow` | Tech/TechContracts.cs |
| `ReservePcBuildCommand(string Number, int Hours = 48)` | C | [RequiresPermission(PermissionCodes.PcBuildManage)] | `PcBuildRow` | Tech/TechContracts.cs |
| `SavePcBuildCommand(Guid? Id, string Name, string? CustomerCode, IReadOnlyList<PcBuildItemInput> Items, bool Quote = false, int ValidDays = 7, bool AcceptIncompatible = false, PcBuildKind Kind = PcBuildKind.Build)` | C | [RequiresPermission(PermissionCodes.PcBuildManage)] | `PcBuildRow` | Tech/TechContracts.cs |
| `SaveProductTechCommand(string Sku, bool TrackSerials, SerialKind SerialKind, int WarrantyMonths, IReadOnlyList<ProductSpecInput> Specs)` | C | [RequiresPermission(PermissionCodes.SpecsManage)] | `string` | Tech/TechContracts.cs |
| `SaveSpecDefinitionCommand(string CategoryCode, string Code, string Name, string? Unit, SpecDataType DataType, bool IsMultiValued, bool IsFilterable, bool IsRequired, string? CompatibilityKey, int SortOrder, IReadOnlyList<string> Options)` | C | [RequiresPermission(PermissionCodes.SpecsManage)] | `string` | Tech/TechContracts.cs |
| `SearchSerialsQuery(string? Text = null, SerialNumberStatus? Status = null, string? Sku = null, int Max = 500)` | Q | [RequiresPermission(PermissionCodes.SerialsView)] | `IReadOnlyList<SerialRow>` | Tech/TechContracts.cs |
| `SearchTechProductsQuery(string? Text = null, string? CategoryCode = null, string? Platform = null, IReadOnlyList<SpecFilter>? Filters = null, bool OnlyInStock = false, int Max = 300)` | Q | [RequiresPermission(PermissionCodes.StockView)] | `IReadOnlyList<TechProductRow>` | Tech/TechContracts.cs |
| `SellPcBuildCommand(string Number, string PaymentMethodCode, IReadOnlyList<SkuSerials>? Serials = null, decimal? CashReceived = null, string? PaymentReference = null, Billing.FiscalBuyerInput? Buyer = null, string? CardNumber = null, string? CustomerCode = null)` | C | [RequiresPermission(PermissionCodes.PosOperate)] [RequiresPermission(PermissionCodes.MovementsRegisterSales)] | `CheckoutResult` | Tech/TechContracts.cs |
