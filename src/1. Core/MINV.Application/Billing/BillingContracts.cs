using MediatR;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Domain.Billing;
using MINV.Domain.Iam;

namespace MINV.Application.Billing;

// =====================================================================================================================
// V4.1 · Contratos de la facturación SIAT (comandos, consultas y vistas). Los manejadores viven en los demás archivos de
// esta carpeta. Todo lo que habla con el SIN lo hace por ISiatGateway; la emisión NUNCA llama al SIN dentro de la
// transacción de la venta (regla F-03): la caja envía DispatchFiscalDocumentsCommand después del COMMIT.
// =====================================================================================================================

// --------------------------------------------------------------------------------------------------- comprador
/// <summary>Datos de facturación que la caja captura para el comprador (nominatividad: el número es obligatorio).</summary>
public sealed record FiscalBuyerInput(int DocumentType, string DocumentNumber, string? Complement, string? Name, string? Email,
    bool ExceptionRequested = false);

// --------------------------------------------------------------------------------------------------- configuración
public sealed record SiatProfileView(int Environment, SiatEndpointSet Endpoints, string QrBaseUrl, int TimeoutSeconds, bool HasToken,
    DateOnly? TokenValidUntil, DateTimeOffset? TokenUpdatedAt);

public sealed record SiatBranchView(Guid BranchId, string BranchCode, string BranchName, int? SiatCode, string? Municipality, string? Phone);

public sealed record MailSettingsView(string Host, int Port, bool UseSsl, string? UserName, bool HasPassword, string FromAddress, string FromName,
    bool IsEnabled);

public sealed record SiatSettingsView(bool Configured, long? Nit, string? BusinessName, string? SystemCode, int Environment, bool IsEnabled,
    string OnlineLegend, string OfflineLegend, DateTimeOffset? ClockSyncedAt, long ClockOffsetMs, IReadOnlyList<SiatProfileView> Profiles,
    IReadOnlyList<SiatBranchView> Branches, MailSettingsView? Mail, bool ModuleActive);

[RequiresPermission(PermissionCodes.BillingView)]
public sealed record GetSiatSettingsQuery : IRequest<SiatSettingsView>;

/// <summary>Datos del Padrón y del sistema autorizado. El ambiente 1 (producción) exige haber completado el piloto.</summary>
[RequiresPermission(PermissionCodes.BillingConfigure)]
[RequiresModule(LicenseModuleCodes.FiscalSiat)]
public sealed record SaveSiatSettingsCommand(long Nit, string BusinessName, string SystemCode, int Environment, string? OnlineLegend,
    string? OfflineLegend, bool Enabled) : IRequest<string>, IAuditableRequest
{
    public object AuditDetails => new { Nit, BusinessName, SystemCode, Environment, Enabled };
}

/// <summary>URL, namespace, QR y token delegado de un ambiente. El token llega en claro, se cifra y NUNCA se devuelve ni
/// se audita; <see cref="NewToken"/> = null conserva el guardado.</summary>
[RequiresPermission(PermissionCodes.BillingConfigure)]
[RequiresModule(LicenseModuleCodes.FiscalSiat)]
public sealed record SaveSiatProfileCommand(int Environment, SiatEndpointSet Endpoints, string QrBaseUrl, int TimeoutSeconds, string? NewToken,
    DateOnly? TokenValidUntil) : IRequest<string>, IAuditableRequest
{
    public object AuditDetails => new { Environment, Endpoints, QrBaseUrl, TimeoutSeconds, TokenChanged = NewToken is not null, TokenValidUntil };

    public override string ToString() => $"SaveSiatProfileCommand ambiente {Environment}";
}

[RequiresPermission(PermissionCodes.BillingConfigure)]
[RequiresModule(LicenseModuleCodes.FiscalSiat)]
public sealed record SaveSiatBranchCommand(string BranchCode, int SiatCode, string Municipality, string? Phone) : IRequest<string>, IAuditableRequest
{
    public object AuditDetails => new { BranchCode, SiatCode, Municipality, Phone };
}

[RequiresPermission(PermissionCodes.BillingConfigure)]
[RequiresModule(LicenseModuleCodes.FiscalSiat)]
public sealed record SaveMailSettingsCommand(string Host, int Port, bool UseSsl, string? UserName, string? NewPassword, string FromAddress,
    string FromName, bool Enabled) : IRequest<string>, IAuditableRequest
{
    public object AuditDetails => new { Host, Port, UseSsl, UserName, PasswordChanged = NewPassword is not null, FromAddress, FromName, Enabled };

    public override string ToString() => $"SaveMailSettingsCommand {Host}:{Port}";
}

// --------------------------------------------------------------------------------------------------- puntos de venta y códigos
/// <summary>Registra un punto de venta en el SIN (registroPuntoVenta) y lo vincula a una caja (opcional). Crea también,
/// si falta, el punto 0 de la sucursal y su CUIS.</summary>
[RequiresPermission(PermissionCodes.BillingConfigure)]
[RequiresModule(LicenseModuleCodes.FiscalSiat)]
public sealed record RegisterSiatPointOfSaleCommand(string BranchCode, string Name, string? Description, string? RegisterCode,
    int TypeCode = SiatCodes.PointOfSaleCashier) : IRequest<SiatPointOfSaleStatus>, IAuditableRequest
{
    public object AuditDetails => new { BranchCode, Name, Description, RegisterCode, TypeCode };
}

[RequiresPermission(PermissionCodes.BillingConfigure)]
[RequiresModule(LicenseModuleCodes.FiscalSiat)]
public sealed record LinkPointOfSaleRegisterCommand(Guid PointOfSaleId, string? RegisterCode) : IRequest<string>, IAuditableRequest
{
    public object AuditDetails => new { PointOfSaleId, RegisterCode };
}

/// <summary>Cierre DEFINITIVO de un punto de venta en el SIN (doble confirmación en la interfaz).</summary>
[RequiresPermission(PermissionCodes.BillingConfigure)]
[RequiresModule(LicenseModuleCodes.FiscalSiat)]
public sealed record CloseSiatPointOfSaleCommand(Guid PointOfSaleId) : IRequest<string>, IAuditableRequest
{
    public object AuditDetails => new { PointOfSaleId };
}

[RequiresPermission(PermissionCodes.BillingConfigure)]
[RequiresModule(LicenseModuleCodes.FiscalSiat)]
public sealed record RequestCuisCommand(Guid PointOfSaleId) : IRequest<string>, IAuditableRequest
{
    public object AuditDetails => new { PointOfSaleId };
}

[RequiresPermission(PermissionCodes.BillingConfigure)]
[RequiresModule(LicenseModuleCodes.FiscalSiat)]
public sealed record RequestCufdCommand(Guid PointOfSaleId) : IRequest<string>, IAuditableRequest
{
    public object AuditDetails => new { PointOfSaleId };
}

/// <summary>Deja todo listo para facturar: hora del SIN, CUIS vigente y CUFD del día en cada punto de venta (es lo que
/// hace el mantenimiento diario automático; el botón «Preparar SIAT» lo ejecuta ya).</summary>
[RequiresPermission(PermissionCodes.BillingConfigure)]
[RequiresModule(LicenseModuleCodes.FiscalSiat)]
public sealed record PrepareSiatCommand : IRequest<SiatMaintenanceResult>, IAuditableRequest
{
    public object AuditDetails => new { };
}

/// <summary>Sincroniza los catálogos del SIN (los 18, o uno).</summary>
[RequiresPermission(PermissionCodes.BillingConfigure)]
[RequiresModule(LicenseModuleCodes.FiscalSiat)]
public sealed record SyncSiatCatalogsCommand(string? Catalog = null) : IRequest<SiatSyncResult>, IAuditableRequest
{
    public object AuditDetails => new { Catalog };
}

public sealed record SiatSyncResult(int Catalogs, int Items, IReadOnlyList<string> Errors, DateTimeOffset? ClockSyncedAt);

public sealed record SiatMaintenanceResult(int CuisRequested, int CufdRequested, int Recovered, int PackagesValidated, int DocumentsSent,
    IReadOnlyList<string> Messages);

[RequiresPermission(PermissionCodes.BillingView)]
[RequiresModule(LicenseModuleCodes.FiscalSiat)]
public sealed record CheckSiatCommunicationCommand(Guid? PointOfSaleId = null) : IRequest<string>;

/// <summary>Verifica un NIT contra el Padrón (verificarNit) y guarda el resultado.</summary>
[RequiresPermission(PermissionCodes.BillingIssue)]
[RequiresModule(LicenseModuleCodes.FiscalSiat)]
public sealed record VerifyNitCommand(long Nit, string? CustomerCode = null) : IRequest<NitCheckResult>;

public sealed record NitCheckResult(long Nit, bool IsValid, int? SiatCode, string Description, bool Checked);

// --------------------------------------------------------------------------------------------------- estado SIAT
public sealed record SiatAlert(string Severity, string Title, string Detail, DateTimeOffset? Deadline = null);

public sealed record SignificantEventRow(Guid Id, Guid BranchId, string BranchCode, int PointOfSaleCode, SignificantEventKind Kind, int EventCode,
    string Description, DateTime StartedAt, DateTime? EndedAt, SignificantEventStatus Status, string? ReceptionCode, int Documents,
    DateTime? RegistrationDeadline, DateTime? TranscriptionDeadline, string? Cafc);

public sealed record SiatPointOfSaleStatus(Guid Id, Guid BranchId, string BranchCode, string BranchName, int SiatBranchCode, int Environment,
    int Code, string Name, string? RegisterCode, SiatConnectionMode Mode, DateTimeOffset ModeSince, DateTimeOffset? LastContactAt,
    string? LastError, DateTimeOffset? RetryAt, DateTimeOffset? CuisValidUntil, DateTimeOffset? CufdValidUntil, DateTimeOffset? CufdObtainedAt,
    int PendingDocuments, int OfflineDocuments, bool IsClosed, SignificantEventRow? OpenEvent);

public sealed record SiatStatusView(bool Configured, bool Enabled, int Environment, long? Nit, string? BusinessName, DateTimeOffset? ClockSyncedAt,
    DateTimeOffset? LastCatalogSync, DateOnly? TokenValidUntil, bool HasToken, IReadOnlyList<SiatPointOfSaleStatus> Points,
    IReadOnlyList<SiatAlert> Alerts, int PendingDocuments, int OfflineDocuments, int OpenEvents, int DocumentsToday, decimal BilledToday);

[RequiresPermission(PermissionCodes.BillingView)]
public sealed record GetSiatStatusQuery : IRequest<SiatStatusView>;

// --------------------------------------------------------------------------------------------------- catálogos y homologación
public sealed record SiatCatalogItemView(string Catalog, int Code, string Description, bool IsCurrent);

[RequiresPermission(PermissionCodes.BillingView)]
public sealed record GetSiatCatalogQuery(string Catalog) : IRequest<IReadOnlyList<SiatCatalogItemView>>;

public sealed record SiatActivityView(string Code, string Description, string? ActivityType, bool IsCurrent, IReadOnlyList<int> Sectors);

[RequiresPermission(PermissionCodes.BillingView)]
public sealed record GetSiatActivitiesQuery : IRequest<IReadOnlyList<SiatActivityView>>;

public sealed record SiatProductView(string ActivityCode, int ProductCode, string Description, bool IsCurrent);

/// <summary>Busca productos genéricos del SIN (por texto y actividad) para homologar.</summary>
[RequiresPermission(PermissionCodes.BillingView)]
public sealed record SearchSiatProductsQuery(string? ActivityCode, string? Text, int Max = 100) : IRequest<IReadOnlyList<SiatProductView>>;

public sealed record ProductHomologationRow(Guid ProductId, string Sku, string Name, string Category, string? ActivityCode, int? SinProductCode,
    string? SinProductDescription, bool IsActive);

public sealed record UnitHomologationRow(Guid UnitId, string Code, string Name, int? SinUnitCode, string? SinUnitDescription);

public sealed record PaymentMethodHomologationRow(Guid PaymentMethodId, string Code, string Name, int? SinCode, string? SinDescription);

public sealed record HomologationView(IReadOnlyList<ProductHomologationRow> Products, IReadOnlyList<UnitHomologationRow> Units,
    IReadOnlyList<PaymentMethodHomologationRow> PaymentMethods, IReadOnlyList<SiatCatalogItemView> SinUnits,
    IReadOnlyList<SiatCatalogItemView> SinPaymentMethods, IReadOnlyList<SiatActivityView> Activities, int PendingProducts);

[RequiresPermission(PermissionCodes.BillingView)]
public sealed record GetHomologationQuery : IRequest<HomologationView>;

public sealed record ProductHomologationInput(string Sku, string ActivityCode, int SinProductCode);

[RequiresPermission(PermissionCodes.BillingConfigure)]
[RequiresModule(LicenseModuleCodes.FiscalSiat)]
public sealed record SaveProductHomologationCommand(IReadOnlyList<ProductHomologationInput> Items) : IRequest<string>, IAuditableRequest
{
    public object AuditDetails => new { Items = Items.Count, Skus = Items.Take(20).Select(i => i.Sku) };
}

[RequiresPermission(PermissionCodes.BillingConfigure)]
[RequiresModule(LicenseModuleCodes.FiscalSiat)]
public sealed record SaveUnitHomologationCommand(string UnitCode, int SinUnitCode) : IRequest<string>, IAuditableRequest
{
    public object AuditDetails => new { UnitCode, SinUnitCode };
}

[RequiresPermission(PermissionCodes.BillingConfigure)]
[RequiresModule(LicenseModuleCodes.FiscalSiat)]
public sealed record SavePaymentMethodHomologationCommand(string PaymentMethodCode, int SinCode) : IRequest<string>, IAuditableRequest
{
    public object AuditDetails => new { PaymentMethodCode, SinCode };
}

/// <summary>Propone la homologación de los productos pendientes buscando en el catálogo SIN de la actividad por
/// palabras de la categoría y el nombre (el usuario revisa y confirma).</summary>
[RequiresPermission(PermissionCodes.BillingConfigure)]
[RequiresModule(LicenseModuleCodes.FiscalSiat)]
public sealed record SuggestProductHomologationQuery(string ActivityCode) : IRequest<IReadOnlyList<ProductHomologationInput>>;

// --------------------------------------------------------------------------------------------------- documentos fiscales
public sealed record FiscalDocumentRow(Guid Id, FiscalDocumentKind Kind, long Number, string Cuf, DateTime IssuedAt, Guid BranchId,
    string BranchCode, int PointOfSaleCode, string BuyerName, string BuyerDocument, decimal Total, FiscalDocumentStatus Status, bool IsReverted,
    int EmissionType, string? SaleNumber, int? LastSiatCode, bool CanVoid, bool CanRevert, bool CanCreditNote, DateTime VoidDeadline);

[RequiresPermission(PermissionCodes.BillingView)]
public sealed record GetFiscalDocumentsQuery(DateOnly From, DateOnly To, FiscalDocumentStatus? Status = null, FiscalDocumentKind? Kind = null,
    string? Search = null) : IRequest<IReadOnlyList<FiscalDocumentRow>>;

public sealed record FiscalDocumentLineView(int LineNumber, string ProductCode, string Description, decimal Quantity, int SinUnitCode,
    string Unit, decimal UnitPrice, decimal Discount, decimal Subtotal, string ActivityCode, int SinProductCode, int? TransactionCode);

public sealed record FiscalDocumentEventView(DateTimeOffset OccurredAt, FiscalDocumentAction Action, int? SiatCode, string? Description,
    string? ReceptionCode, string? Messages, string? User);

public sealed record FiscalDeliveryView(DateTimeOffset OccurredAt, FiscalDeliveryChannel Channel, string? Recipient, bool Succeeded, string? Error);

public sealed record FiscalDocumentDetail(FiscalDocumentRow Row, IReadOnlyList<FiscalDocumentLineView> Lines,
    IReadOnlyList<FiscalDocumentEventView> Events, IReadOnlyList<FiscalDeliveryView> Deliveries, string Xml, string? ReceptionCode, string? Cafc,
    int ExceptionCode, string? PaymentMethod, string? CardNumberMasked, string Legend, string? VoidReason, FiscalPrintOriginal? Original,
    Guid? ReplacesDocumentId, Guid? ReplacedByDocumentId, decimal TaxAmount, string? BuyerEmail);

[RequiresPermission(PermissionCodes.BillingView)]
public sealed record GetFiscalDocumentQuery(Guid DocumentId) : IRequest<FiscalDocumentDetail>;

[RequiresPermission(PermissionCodes.BillingView)]
public sealed record GetFiscalPrintModelQuery(Guid DocumentId) : IRequest<FiscalPrintModel>;

/// <summary>Representación gráfica: PDF (media carta) o ESC/POS de rollo.</summary>
public sealed record FiscalFile(string FileName, string ContentType, byte[] Content);

[RequiresPermission(PermissionCodes.BillingView)]
public sealed record RenderFiscalDocumentQuery(Guid DocumentId, FiscalDeliveryChannel Format = FiscalDeliveryChannel.Pdf, int Columns = 48)
    : IRequest<FiscalFile>;

/// <summary>Registra que el documento se imprimió o se entregó en PDF (evidencia de la entrega al comprador).</summary>
[RequiresPermission(PermissionCodes.BillingView)]
public sealed record RecordFiscalDeliveryCommand(Guid DocumentId, FiscalDeliveryChannel Channel, string? Recipient = null) : IRequest<string>;

/// <summary>Envía el XML y el PDF al correo del comprador (o al indicado).</summary>
[RequiresPermission(PermissionCodes.BillingIssue)]
[RequiresModule(LicenseModuleCodes.FiscalSiat)]
public sealed record SendFiscalDocumentEmailCommand(Guid DocumentId, string? Email = null) : IRequest<string>, IAuditableRequest
{
    public object AuditDetails => new { DocumentId, Email };
}

/// <summary>
/// Envía al SIN los documentos pendientes (uno o hasta <see cref="Max"/>): 908 → válido; 902/904 → rechazado y se
/// re-emite; sin comunicación → el punto de venta pasa a fuera de línea y la venta se re-emite fuera de línea. Lo usa la
/// caja inmediatamente después de cobrar (para imprimir el documento definitivo) y el despachador en segundo plano.
/// </summary>
[RequiresPermission(PermissionCodes.BillingIssue)]
[RequiresModule(LicenseModuleCodes.FiscalSiat)]
public sealed record DispatchFiscalDocumentsCommand(Guid? DocumentId = null, int Max = 50) : IRequest<DispatchResult>;

public sealed record DispatchResult(int Sent, int Valid, int Rejected, int WentOffline, IReadOnlyList<FiscalDocumentRow> Documents,
    IReadOnlyList<string> Messages);

[RequiresPermission(PermissionCodes.BillingView)]
[RequiresModule(LicenseModuleCodes.FiscalSiat)]
public sealed record CheckFiscalDocumentStatusCommand(Guid DocumentId) : IRequest<string>;

/// <summary>Anula en el SIN (motivo del catálogo; hasta el día 9 del mes siguiente). Con <see cref="ReturnGoods"/> además
/// devuelve TODA la mercadería (stock, reembolso y asiento inverso); sin ella la venta sigue y se puede re-facturar.</summary>
[RequiresPermission(PermissionCodes.BillingVoid)]
[RequiresModule(LicenseModuleCodes.FiscalSiat)]
public sealed record VoidFiscalDocumentCommand(Guid DocumentId, int ReasonCode, bool ReturnGoods, string? Note = null)
    : IRequest<string>, IAuditableRequest
{
    public object AuditDetails => new { DocumentId, ReasonCode, ReturnGoods, Note };
}

/// <summary>Revierte la anulación (una sola vez, dentro del mismo plazo).</summary>
[RequiresPermission(PermissionCodes.BillingVoid)]
[RequiresModule(LicenseModuleCodes.FiscalSiat)]
public sealed record RevertFiscalVoidCommand(Guid DocumentId) : IRequest<string>, IAuditableRequest
{
    public object AuditDetails => new { DocumentId };
}

/// <summary>Emite un documento nuevo para la misma venta (tras anular por datos del comprador erróneos o un rechazo).</summary>
[RequiresPermission(PermissionCodes.BillingIssue)]
[RequiresModule(LicenseModuleCodes.FiscalSiat)]
public sealed record ReissueFiscalDocumentCommand(Guid DocumentId, FiscalBuyerInput? Buyer = null) : IRequest<FiscalDocumentRow>, IAuditableRequest
{
    public object AuditDetails => new { DocumentId, Buyer };
}

// --------------------------------------------------------------------------------------------------- devoluciones y notas
public sealed record ReturnLineInput(string Sku, decimal Quantity);

/// <summary>Devolución parcial o total de una venta: stock de vuelta (DEVOLUCIÓN DE CLIENTE), reembolso, asiento y, si la
/// venta tiene factura válida, nota crédito-débito (sector 24).</summary>
[RequiresPermission(PermissionCodes.PosOperate)]
[RequiresPermission(PermissionCodes.BillingVoid)]
public sealed record CreateSalesReturnCommand(string InvoiceNumber, string Reason, string RefundPaymentMethodCode,
    IReadOnlyList<ReturnLineInput> Lines) : IRequest<SalesReturnResult>, IAuditableRequest
{
    public object AuditDetails => new { InvoiceNumber, Reason, RefundPaymentMethodCode, Lines };
}

public sealed record SalesReturnResult(string Number, decimal Refund, Guid? CreditNoteId, long? CreditNoteNumber, FiscalDocumentStatus? CreditNoteStatus,
    string Message);

public sealed record SalesReturnRow(string Number, string InvoiceNumber, DateTimeOffset ReturnedAt, string Customer, string Reason, decimal Refund,
    string? CreditNote, FiscalDocumentStatus? CreditNoteStatus);

[RequiresPermission(PermissionCodes.SalesView)]
public sealed record GetSalesReturnsQuery(DateOnly From, DateOnly To) : IRequest<IReadOnlyList<SalesReturnRow>>;

/// <summary>Lo que todavía se puede devolver de una venta (vendido − devuelto por línea).</summary>
public sealed record ReturnableLine(string Sku, string Name, string Unit, decimal Sold, decimal Returned, decimal UnitPrice, decimal DiscountPercent);

[RequiresPermission(PermissionCodes.SalesView)]
public sealed record GetReturnableLinesQuery(string InvoiceNumber) : IRequest<IReadOnlyList<ReturnableLine>>;

// --------------------------------------------------------------------------------------------------- contingencia
[RequiresPermission(PermissionCodes.BillingView)]
public sealed record GetSignificantEventsQuery(DateOnly From, DateOnly To) : IRequest<IReadOnlyList<SignificantEventRow>>;

public sealed record FiscalPackageRow(Guid Id, Guid EventId, string BranchCode, int PointOfSaleCode, int DocumentSector, string? Cafc, int Documents,
    FiscalPackageStatus Status, string? ReceptionCode, DateTimeOffset SentAt, DateTimeOffset? ValidatedAt, int? LastSiatCode, string? Messages);

[RequiresPermission(PermissionCodes.BillingView)]
public sealed record GetFiscalPackagesQuery(Guid? EventId = null) : IRequest<IReadOnlyList<FiscalPackageRow>>;

/// <summary>El usuario declara una contingencia manual (energía, software, hardware): la caja deja de emitir en línea;
/// las facturas manuales del talonario CAFC se transcriben después.</summary>
[RequiresPermission(PermissionCodes.BillingContingency)]
[RequiresModule(LicenseModuleCodes.FiscalSiat)]
public sealed record StartManualContingencyCommand(Guid PointOfSaleId, int EventCode, string? Description, DateTime? StartedAt, string CafcCode)
    : IRequest<string>, IAuditableRequest
{
    public object AuditDetails => new { PointOfSaleId, EventCode, Description, StartedAt, CafcCode };
}

/// <summary>Declara el fin de la contingencia y ejecuta la recuperación: CUFD nuevo → registro del evento → paquetes.</summary>
[RequiresPermission(PermissionCodes.BillingContingency)]
[RequiresModule(LicenseModuleCodes.FiscalSiat)]
public sealed record EndContingencyCommand(Guid PointOfSaleId, DateTime? EndedAt = null) : IRequest<string>, IAuditableRequest
{
    public object AuditDetails => new { PointOfSaleId, EndedAt };
}

/// <summary>Fuerza ahora la verificación de comunicación y la recuperación de un punto fuera de línea.</summary>
[RequiresPermission(PermissionCodes.BillingContingency)]
[RequiresModule(LicenseModuleCodes.FiscalSiat)]
public sealed record RecoverPointOfSaleCommand(Guid PointOfSaleId) : IRequest<string>, IAuditableRequest
{
    public object AuditDetails => new { PointOfSaleId };
}

/// <summary>Pasa a fuera de línea a mano (p. ej. se sabe que no hay internet): evento automático de corte de internet.</summary>
[RequiresPermission(PermissionCodes.BillingContingency)]
[RequiresModule(LicenseModuleCodes.FiscalSiat)]
public sealed record GoOfflineCommand(Guid PointOfSaleId, int? EventCode = null) : IRequest<string>, IAuditableRequest
{
    public object AuditDetails => new { PointOfSaleId, EventCode };
}

public sealed record ContingencyCodeRow(Guid Id, string BranchCode, int DocumentSector, string Code, long NumberFrom, long NumberTo, DateOnly? ValidUntil,
    bool IsActive, int Used);

[RequiresPermission(PermissionCodes.BillingView)]
public sealed record GetContingencyCodesQuery : IRequest<IReadOnlyList<ContingencyCodeRow>>;

[RequiresPermission(PermissionCodes.BillingContingency)]
[RequiresModule(LicenseModuleCodes.FiscalSiat)]
public sealed record RegisterContingencyCodeCommand(string BranchCode, int DocumentSector, string Code, long NumberFrom, long NumberTo,
    DateOnly? ValidUntil) : IRequest<string>, IAuditableRequest
{
    public object AuditDetails => new { BranchCode, DocumentSector, Code, NumberFrom, NumberTo, ValidUntil };
}

/// <summary>Transcribe una factura manual del talonario CAFC emitida durante la contingencia: registra la venta (stock,
/// pago, asiento) y el documento fuera de línea con el CAFC, su número y su fecha (dentro del evento).</summary>
[RequiresPermission(PermissionCodes.BillingContingency)]
[RequiresModule(LicenseModuleCodes.FiscalSiat)]
public sealed record TranscribeManualInvoiceCommand(Guid SignificantEventId, long Number, DateTime IssuedAt, FiscalBuyerInput Buyer,
    string PaymentMethodCode, IReadOnlyList<Sales.SaleLineInput> Lines) : IRequest<FiscalDocumentRow>, IAuditableRequest
{
    public object AuditDetails => new { SignificantEventId, Number, IssuedAt, Buyer, PaymentMethodCode, Lines };
}

// --------------------------------------------------------------------------------------------------- libros y reportes
public sealed record SalesBookRow(int Row, DateTime Date, long Number, string Cuf, string BuyerDocument, string? Complement, string BuyerName,
    decimal Total, decimal Ice, decimal Iehd, decimal Ipj, decimal Fees, decimal OtherNotSubject, decimal Exports, decimal ZeroRate,
    decimal Subtotal, decimal Discounts, decimal GiftCard, decimal TaxBase, decimal TaxDebit, string Status, string ControlCode, int DocumentSector,
    string BranchCode);

public sealed record SalesBookView(int Year, int Month, IReadOnlyList<SalesBookRow> Rows, decimal Total, decimal TaxBase, decimal TaxDebit,
    int Valid, int Voided);

[RequiresPermission(PermissionCodes.BillingView)]
public sealed record GetSalesBookQuery(int Year, int Month) : IRequest<SalesBookView>;

public sealed record PurchasesBookRow(int Row, string SupplierNit, string SupplierName, string AuthorizationCode, string InvoiceNumber, DateOnly Date,
    decimal Total, decimal Ice, decimal Iehd, decimal Ipj, decimal Fees, decimal OtherNotSubject, decimal Exempt, decimal ZeroRate, decimal Subtotal,
    decimal Discounts, decimal GiftCard, decimal TaxBase, decimal TaxCredit, string PurchaseType, string ControlCode, string BranchCode);

public sealed record PurchasesBookView(int Year, int Month, IReadOnlyList<PurchasesBookRow> Rows, decimal Total, decimal TaxBase, decimal TaxCredit);

[RequiresPermission(PermissionCodes.BillingView)]
public sealed record GetPurchasesBookQuery(int Year, int Month) : IRequest<PurchasesBookView>;

/// <summary>Libro de ventas o compras exportado (CSV con el orden de columnas de las plantillas del SIN, o Excel).</summary>
[RequiresPermission(PermissionCodes.BillingView)]
public sealed record ExportFiscalBookQuery(int Year, int Month, bool Purchases, string Format = "csv") : IRequest<FiscalFile>;

public sealed record TaxSummaryView(int Year, int Month, decimal GrossSales, decimal CreditNotes, decimal TaxDebit, decimal TaxCreditPurchases,
    decimal TaxCreditNotes, decimal VatPayable, decimal VatCarryForward, decimal TransactionTaxBase, decimal TransactionTax, int Invoices,
    int VoidedInvoices, int Notes);

[RequiresPermission(PermissionCodes.BillingView)]
public sealed record GetTaxSummaryQuery(int Year, int Month) : IRequest<TaxSummaryView>;

public sealed record SiatServiceCallRow(DateTimeOffset OccurredAt, string Resource, string Operation, int? PointOfSaleCode, int DurationMs,
    int? HttpStatus, int? SiatCode, bool Succeeded, string? Error, string? RequestBody, string? ResponseBody);

/// <summary>Bitácora técnica de llamadas al SIN (sin token): evidencia de la inspección y soporte.</summary>
[RequiresPermission(PermissionCodes.BillingConfigure)]
public sealed record GetSiatServiceCallsQuery(DateOnly From, DateOnly To, string? Operation = null, int Max = 500)
    : IRequest<IReadOnlyList<SiatServiceCallRow>>;

// --------------------------------------------------------------------------------------------------- caja
/// <summary>Cliente existente con ese documento (para la caja: «¿ya compró antes?»).</summary>
public sealed record FiscalBuyerLookup(bool Found, string? CustomerCode, string? Name, string? Email, int DocumentType, string DocumentNumber,
    string? Complement, bool? NitValid);

[RequiresPermission(PermissionCodes.BillingIssue)]
public sealed record FindFiscalBuyerQuery(int DocumentType, string DocumentNumber, string? Complement = null) : IRequest<FiscalBuyerLookup>;

/// <summary>¿La caja del usuario factura? (módulo, configuración, punto de venta y modo) — se muestra en el punto de venta.</summary>
public sealed record PosFiscalState(bool BillingEnabled, bool Ready, string Message, SiatConnectionMode? Mode, int? PointOfSaleCode,
    IReadOnlyList<SiatCatalogItemView> DocumentTypes, int PendingHomologation);

[RequiresPermission(PermissionCodes.PosOperate)]
public sealed record GetPosFiscalStateQuery : IRequest<PosFiscalState>;

// --------------------------------------------------------------------------------------------------- trabajo en segundo plano
/// <summary>
/// V4.1 · Trabajo periódico de la facturación de UNA empresa (lo ejecuta el servicio en segundo plano del servidor en la
/// nube o del escritorio en modo local, con el tenant ya fijado y sin restricción de sucursal): envía pendientes,
/// recupera puntos fuera de línea (CUFD nuevo → evento → paquetes → validación), renueva CUFD/CUIS, sincroniza la hora
/// y los catálogos una vez al día y entrega los correos pendientes.
/// </summary>
public interface ISiatWorker
{
    Task<DispatchResult> DispatchAsync(Guid? documentId, int max, CancellationToken cancellationToken = default);

    Task<SiatMaintenanceResult> MaintainAsync(bool force, CancellationToken cancellationToken = default);
}

// --------------------------------------------------------------------------------------------------- facturas de proveedores
/// <summary>
/// V4.1 · Registra la factura del proveedor de una recepción (número, CUF/código de autorización, fecha, importe): entra
/// al libro de compras y reclasifica el crédito fiscal (Debe 1.1.04 IVA crédito fiscal / Haber 1.1.05 Inventario, 13 %
/// de la base). El costo promedio histórico no se recalcula (limitación documentada).
/// </summary>
[RequiresPermission(PermissionCodes.PurchasingManage)]
public sealed record RegisterSupplierInvoiceCommand(string ReceiptNumber, string InvoiceNumber, string AuthorizationCode, DateOnly InvoiceDate,
    decimal TotalAmount, decimal Discounts = 0, decimal NotSubjectToVat = 0, int PurchaseType = 1, string? ControlCode = null)
    : IRequest<string>, IAuditableRequest
{
    public object AuditDetails => new { ReceiptNumber, InvoiceNumber, AuthorizationCode, InvoiceDate, TotalAmount, Discounts, NotSubjectToVat, PurchaseType };
}

public sealed record SupplierInvoiceRow(Guid Id, string Number, string SupplierCode, string Supplier, string? SupplierNit, DateOnly InvoiceDate,
    string AuthorizationCode, decimal TotalAmount, decimal TaxBase, decimal TaxCredit, string Status, string? ReceiptNumber, string BranchCode);

[RequiresPermission(PermissionCodes.PurchasingManage)]
public sealed record GetSupplierInvoicesQuery(DateOnly From, DateOnly To) : IRequest<IReadOnlyList<SupplierInvoiceRow>>;

/// <summary>Recepciones sin factura de proveedor registrada (para completar el libro de compras).</summary>
public sealed record PendingSupplierInvoiceRow(string ReceiptNumber, DateOnly ReceivedOn, string SupplierCode, string Supplier, decimal Total);

[RequiresPermission(PermissionCodes.PurchasingManage)]
public sealed record GetReceiptsWithoutInvoiceQuery : IRequest<IReadOnlyList<PendingSupplierInvoiceRow>>;
