using MediatR;
using MINV.Application.Common;
using MINV.Application.Sales;
using MINV.Domain.Catalog;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;
using MINV.Domain.Sales;
using MINV.Domain.Service;

namespace MINV.Application.Tech;

// =====================================================================================================================
// V4.2 · Edición Tecnología: contratos (comandos, consultas y vistas) de fichas técnicas, series e IMEI, garantías y
// RMA, armador de PC y tablero del rubro. Los manejadores viven en los demás archivos de esta carpeta. Las series de las
// operaciones de siempre (venta, devolución, recepción de compra, movimiento, transferencia) viajan en el parámetro
// opcional «Serials» de sus líneas (SaleLineInput, ReturnLineInput, TransferLineInput, RegisterMovementCommand) o en
// <see cref="SkuSerials"/> (recepción de compra y venta de un armado).
// =====================================================================================================================

/// <summary>Series (o IMEI) de un producto dentro de un documento: la recepción de una orden de compra, la venta de un
/// armado, etc. Tantas como unidades del producto en ese documento.</summary>
public sealed record SkuSerials(string Sku, IReadOnlyList<string> Serials);

/// <summary>Códigos de especificación con significado propio en la edición (regla T-07): la plataforma de un juego o una
/// consola, las plataformas que admite un accesorio y la condición del producto. Son especificaciones de tipo opción: los
/// filtros rápidos salen de sus opciones, nunca de listas fijas.</summary>
public static class TechSpecCodes
{
    public const string Platform = "plataforma";
    public const string Platforms = "plataformas";
    public const string Condition = "condicion";

    public static bool IsPlatform(string code) => code is Platform or Platforms;
}

// --------------------------------------------------------------------------------------------------- fichas técnicas
public sealed record SpecDefinitionView(Guid Id, string CategoryCode, string CategoryName, string Code, string Name, string? Unit,
    SpecDataType DataType, bool IsMultiValued, bool IsFilterable, bool IsRequired, string? CompatibilityKey, int SortOrder,
    IReadOnlyList<string> Options, bool IsInherited);

/// <summary>Especificaciones de una categoría (con las heredadas de sus madres, <c>IsInherited</c>) o de todas.</summary>
[RequiresPermission(PermissionCodes.StockView)]
public sealed record GetSpecDefinitionsQuery(string? CategoryCode = null) : IRequest<IReadOnlyList<SpecDefinitionView>>;

/// <summary>Crea o modifica la especificación <see cref="Code"/> de una categoría con sus opciones (en ese orden). El tipo y
/// el multivalor no cambian después de crearla; una opción usada por un producto no se puede quitar. Un código no se repite
/// entre una categoría y sus madres o hijas (herencia).</summary>
[RequiresPermission(PermissionCodes.SpecsManage)]
public sealed record SaveSpecDefinitionCommand(string CategoryCode, string Code, string Name, string? Unit, SpecDataType DataType,
    bool IsMultiValued, bool IsFilterable, bool IsRequired, string? CompatibilityKey, int SortOrder, IReadOnlyList<string> Options)
    : IRequest<string>, IAuditableRequest
{
    public object AuditDetails => new { CategoryCode, Code, Name, Unit, DataType, IsMultiValued, IsFilterable, IsRequired, CompatibilityKey, Options };
}

public sealed record ProductSpecView(string Code, string Name, string? Unit, SpecDataType DataType, IReadOnlyList<string> Values, string Display,
    string? CompatibilityKey, bool IsRequired);

public sealed record ProductTechView(string Sku, string Name, string Category, string? Brand, bool TrackSerials, SerialKind SerialKind,
    int WarrantyMonths, IReadOnlyList<ProductSpecView> Specs, int SerialsInStock);

[RequiresPermission(PermissionCodes.StockView)]
public sealed record GetProductTechQuery(string Sku) : IRequest<ProductTechView>;

/// <summary>Valores de una especificación: números con punto o coma decimal, opciones por su texto (sin distinguir
/// mayúsculas) y texto libre. Vacío = sin valor.</summary>
public sealed record ProductSpecInput(string Code, IReadOnlyList<string> Values);

/// <summary>Ficha técnica completa (reemplaza la anterior), garantía y control por serie o IMEI de un producto. Cambiar
/// el control por serie afecta a todas las sucursales: exige una sesión con todas las sucursales, y pasar a «lleva serie»
/// exige que cada unidad en stock ya tenga su serie (<see cref="RegisterStockSerialsCommand"/>).</summary>
[RequiresPermission(PermissionCodes.SpecsManage)]
public sealed record SaveProductTechCommand(string Sku, bool TrackSerials, SerialKind SerialKind, int WarrantyMonths,
    IReadOnlyList<ProductSpecInput> Specs) : IRequest<string>, IAuditableRequest
{
    public object AuditDetails => new { Sku, TrackSerials, SerialKind, WarrantyMonths, Specs = Specs.Count };
}

/// <summary>Filtro por especificación: una de las opciones o valores (<see cref="Values"/>) y/o un rango numérico.</summary>
public sealed record SpecFilter(string Code, IReadOnlyList<string>? Values = null, decimal? Min = null, decimal? Max = null);

/// <summary>Producto del catálogo técnico. V4.2 · <see cref="SerialKind"/>: si lleva serie, número de serie o IMEI (insignia de la
/// galería y de la caja).</summary>
public sealed record TechProductRow(string Sku, string Name, string CategoryCode, string Category, string? Brand, decimal Price, decimal Stock,
    bool TrackSerials, int WarrantyMonths, string KeySpecs, IReadOnlyList<string> Platforms, Guid? ImageId, SerialKind SerialKind = SerialKind.Serial);

/// <summary>Búsqueda por texto (SKU, nombre o código de barras), categoría (incluye subcategorías), plataforma y
/// especificaciones (opciones o rangos), con el stock disponible de la sucursal activa.</summary>
[RequiresPermission(PermissionCodes.StockView)]
public sealed record SearchTechProductsQuery(string? Text = null, string? CategoryCode = null, string? Platform = null,
    IReadOnlyList<SpecFilter>? Filters = null, bool OnlyInStock = false, int Max = 300) : IRequest<IReadOnlyList<TechProductRow>>;

public sealed record SpecFacetValue(string Value, int Count);

public sealed record SpecFacet(string Code, string Name, string? Unit, SpecDataType DataType, IReadOnlyList<SpecFacetValue> Values, decimal? Min,
    decimal? Max);

/// <summary>Filtros de una categoría (especificaciones filtrables propias y heredadas) con la cantidad de productos activos
/// por opción o valor y, en las numéricas, el rango.</summary>
[RequiresPermission(PermissionCodes.StockView)]
public sealed record GetSpecFacetsQuery(string CategoryCode) : IRequest<IReadOnlyList<SpecFacet>>;

// --------------------------------------------------------------------------------------------------- series e IMEI
/// <summary>Registra series de unidades que ya están en stock SIN serie en la sucursal activa (inventario inicial o antes de
/// pasar el producto a «lleva serie»): nunca más series que unidades sin serie.</summary>
[RequiresPermission(PermissionCodes.SerialsManage)]
public sealed record RegisterStockSerialsCommand(string Sku, IReadOnlyList<string> Serials, string? Note = null) : IRequest<string>, IAuditableRequest
{
    public object AuditDetails => new { Sku, Count = Serials.Count, Note };
}

/// <summary>Destino final de una unidad que NO está en stock (devuelta por falla o que quedó en un RMA ya resuelto).</summary>
public enum SerialDisposal
{
    /// <summary>Se devuelve al proveedor (reemplazo o nota de crédito del proveedor: hecho nuevo aparte).</summary>
    ReturnToSupplier,

    /// <summary>Baja definitiva (irreparable, destruida).</summary>
    Scrap,
}

/// <summary>Da destino a una unidad devuelta por falla o que quedó en garantía después de reemplazarla: al proveedor o de
/// baja. Las unidades EN STOCK se dan de baja con un ajuste negativo con su serie (<c>RegisterMovementCommand</c>).</summary>
[RequiresPermission(PermissionCodes.SerialsManage)]
public sealed record DisposeSerialCommand(string Serial, SerialDisposal Disposal, string Reason, string? Sku = null) : IRequest<string>, IAuditableRequest
{
    public object AuditDetails => new { Serial, Disposal, Reason, Sku };
}

public sealed record SerialRow(string Serial, SerialKind Kind, string Sku, string Product, SerialNumberStatus Status, string? Branch,
    string? Warehouse, DateTimeOffset? ReceivedAt, DateTimeOffset? SoldAt, string? InvoiceNumber, string? Customer, DateOnly? WarrantyUntil);

/// <summary>Series disponibles (en stock) de un producto en la sucursal activa (para la caja y las transferencias).</summary>
[RequiresPermission(PermissionCodes.SerialsView)]
public sealed record GetAvailableSerialsQuery(string Sku, string? WarehouseCode = null) : IRequest<IReadOnlyList<SerialRow>>;

/// <summary>Búsqueda de series e IMEI de la empresa (por texto de la serie, estado o producto): las <see cref="Max"/> que
/// ingresaron más recientemente.</summary>
[RequiresPermission(PermissionCodes.SerialsView)]
public sealed record SearchSerialsQuery(string? Text = null, SerialNumberStatus? Status = null, string? Sku = null, int Max = 500)
    : IRequest<IReadOnlyList<SerialRow>>;

/// <summary>V4.2 · Totales de las series de la empresa (todas, no solo las listadas): en stock (y de cuántos productos),
/// vendidas y cuántas con garantía vigente (derivada, T-04), en RMA o devueltas, y fuera del inventario (baja o devueltas
/// al proveedor).</summary>
public sealed record SerialSummaryView(int Total, int InStock, int InStockProducts, int Sold, int SoldInWarranty, int InRmaOrReturned, int Out);

[RequiresPermission(PermissionCodes.SerialsView)]
public sealed record GetSerialSummaryQuery : IRequest<SerialSummaryView>;

public sealed record SerialEventView(DateTimeOffset OccurredAt, SerialEventAction Action, string? Branch, string? DocumentNumber, string? Note,
    string? User);

public sealed record SerialTraceView(SerialRow Serial, string? Supplier, string? ReceiptNumber, int WarrantyMonths, bool InWarranty,
    IReadOnlyList<SerialEventView> Events, IReadOnlyList<WarrantyClaimRow> Claims);

/// <summary>Trazabilidad completa de una serie o IMEI: cada hecho con su sucursal, documento y usuario, la garantía derivada
/// (regla T-04) y sus casos RMA. Si la misma serie existe en dos productos, indique el SKU.</summary>
[RequiresPermission(PermissionCodes.SerialsView)]
public sealed record GetSerialTraceQuery(string Serial, string? Sku = null) : IRequest<SerialTraceView>;

// --------------------------------------------------------------------------------------------------- garantías y RMA
public sealed record WarrantyStatusView(string Serial, string Sku, string Product, SerialNumberStatus Status, DateOnly? SoldOn,
    string? InvoiceNumber, string? CustomerCode, string? Customer, int WarrantyMonths, DateOnly? WarrantyUntil, bool InWarranty, string? OpenClaim);

/// <summary>Garantía de una unidad: fecha de la venta (o de la reposición) + meses del producto, calculada al consultar.</summary>
[RequiresPermission(PermissionCodes.SerialsView)]
public sealed record GetWarrantyStatusQuery(string Serial, string? Sku = null) : IRequest<WarrantyStatusView>;

public sealed record WarrantyClaimRow(Guid Id, string Number, string BranchCode, string Serial, string Sku, string Product, string Customer,
    string Issue, WarrantyClaimStatus Status, bool IsInWarranty, DateOnly? WarrantyUntil, DateTimeOffset ReceivedAt, DateTimeOffset? ClosedAt,
    string? Supplier, string? Resolution, string? ReplacementSerial, int DaysOpen);

public sealed record WarrantyClaimEventView(DateTimeOffset OccurredAt, WarrantyClaimAction Action, WarrantyClaimStatus Status, string? Note,
    string User);

public sealed record WarrantyClaimDetail(WarrantyClaimRow Claim, WarrantyStatusView Warranty, IReadOnlyList<WarrantyClaimEventView> Events,
    string? InvoiceNumber, IReadOnlyList<WarrantyClaimStatus> NextStatuses);

[RequiresPermission(PermissionCodes.SerialsView)]
public sealed record GetWarrantyClaimsQuery(WarrantyClaimStatus? Status = null, bool OnlyOpen = false) : IRequest<IReadOnlyList<WarrantyClaimRow>>;

[RequiresPermission(PermissionCodes.SerialsView)]
public sealed record GetWarrantyClaimQuery(string Number) : IRequest<WarrantyClaimDetail>;

/// <summary>Abre un caso RMA en la sucursal activa para una unidad vendida (la serie pasa a «en RMA», sin entrada de stock).
/// Fuera de garantía solo se abre como reparación con cargo si <see cref="ChargeableRepair"/> = true (queda marcado). El
/// cliente sale de la venta de la serie; si se vendió en otra sucursal, indíquelo en <see cref="CustomerCode"/>.</summary>
[RequiresPermission(PermissionCodes.ServiceOpen)]
public sealed record OpenWarrantyClaimCommand(string Serial, string Issue, bool ChargeableRepair = false, string? CustomerCode = null,
    string? Sku = null) : IRequest<WarrantyClaimRow>, IAuditableRequest
{
    public object AuditDetails => new { Serial, Issue, ChargeableRepair, CustomerCode, Sku };
}

/// <summary>Avanza el caso según la tabla de transiciones (diagnóstico, enviado al proveedor, reparado, reemplazado,
/// rechazado, entregado) con su nota. Enviar al proveedor exige el proveedor (o el preferido del producto); reemplazar
/// exige la unidad de reemplazo y, si el equipo estaba en el proveedor, la serie pasa a «devuelta al proveedor»; entregar
/// un equipo reparado o rechazado lo devuelve al cliente (vuelve a «vendida»).</summary>
[RequiresPermission(PermissionCodes.ServiceManage)]
public sealed record MoveWarrantyClaimCommand(string Number, WarrantyClaimStatus Next, string? Resolution = null, string? SupplierCode = null,
    string? Note = null) : IRequest<WarrantyClaimRow>, IAuditableRequest
{
    public object AuditDetails => new { Number, Next, Resolution, SupplierCode, Note };
}

/// <summary>Nota del técnico o de la atención en un caso abierto.</summary>
[RequiresPermission(PermissionCodes.ServiceOpen)]
public sealed record AddWarrantyClaimNoteCommand(string Number, string Note) : IRequest<WarrantyClaimRow>, IAuditableRequest
{
    public object AuditDetails => new { Number, Note };
}

/// <summary>Entrega una unidad nueva del mismo producto en reemplazo: sale del stock de la sucursal del caso (movimiento
/// REPOSICIÓN POR GARANTÍA con su asiento Debe 5.1.10 / Haber 1.1.05 al costo promedio) y queda vendida al cliente del
/// caso; la defectuosa sigue en RMA. Con <see cref="Resolution"/> el caso pasa además a «reemplazado».</summary>
[RequiresPermission(PermissionCodes.ServiceManage)]
public sealed record IssueWarrantyReplacementCommand(string Number, string ReplacementSerial, string? Resolution = null)
    : IRequest<WarrantyClaimRow>, IAuditableRequest
{
    public object AuditDetails => new { Number, ReplacementSerial, Resolution };
}

// --------------------------------------------------------------------------------------------------- armador de PC
/// <summary>Pieza de un armado. V7: <paramref name="Slot"/> es null en el producto sin ranura de un carrito (en un armado la
/// ranura es obligatoria: <c>pcbuild.slot</c>).</summary>
public sealed record PcBuildItemInput(PcSlot? Slot, string Sku, int Quantity = 1);

/// <summary>V7: <paramref name="Slot"/> es null en el producto sin ranura de un carrito.</summary>
public sealed record PcBuildItemView(PcSlot? Slot, string Sku, string Name, int Quantity, decimal UnitPrice, decimal Subtotal, decimal Stock,
    Guid? ImageId, IReadOnlyList<string> KeySpecs);

public sealed record PcBuildCheckView(IReadOnlyList<PcBuildItemView> Items, IReadOnlyList<PcIssue> Issues, bool IsCompatible, int EstimatedDrawW,
    int RecommendedPsuW, int? PsuW, decimal Total);

/// <summary>Revisa la compatibilidad de un armado (sin guardarlo) con los precios de la lista vigente y el stock de la
/// sucursal activa. La compatibilidad la decide SOLO <see cref="PcCompatibility.Check"/> (regla T-06).</summary>
[RequiresPermission(PermissionCodes.StockView)]
public sealed record CheckPcBuildQuery(IReadOnlyList<PcBuildItemInput> Items) : IRequest<PcBuildCheckView>;

public sealed record PcBuildCandidate(string Sku, string Name, string? Brand, decimal Price, decimal Stock, bool IsCompatible, string? Reason,
    Guid? ImageId, IReadOnlyList<string> KeySpecs);

/// <summary>Productos para una ranura (los que tienen la especificación que identifica la ranura; monitor, periféricos,
/// software y servicios por <see cref="CategoryCode"/>), marcando cuáles son compatibles con lo ya elegido y por qué no los
/// demás (solo cuentan los errores NUEVOS que agregaría el candidato).</summary>
[RequiresPermission(PermissionCodes.StockView)]
public sealed record GetPcBuildCandidatesQuery(PcSlot Slot, IReadOnlyList<PcBuildItemInput> Current, string? Text = null, bool OnlyInStock = false,
    string? CategoryCode = null) : IRequest<IReadOnlyList<PcBuildCandidate>>;

/// <summary>Fila de un armado. V6: canal (escritorio o web), contacto (teléfono y correo solo para quien tiene
/// <c>sales.pcbuild.manage</c>, regla S-06), reserva vigente hasta, publicado en la web, motivo del cierre y unidades reservadas.
/// V7: tipo (armado o carrito; en un carrito <see cref="IsCompatible"/> es siempre verdadero porque no se evalúa) y datos para
/// la factura de quien reserva (<c>Buyer…</c>, solo para quien tiene <c>sales.pcbuild.manage</c>, reglas S-06 y P-05).</summary>
public sealed record PcBuildRow(Guid Id, string Number, string Name, string BranchCode, string? Customer, PcBuildStatus Status, DateOnly ValidUntil,
    bool IsExpired, decimal Total, int Items, bool IsCompatible, DateTimeOffset CreatedAt, string? InvoiceNumber, bool QuotedWithErrors = false,
    PcBuildChannel Channel = PcBuildChannel.Desktop, string? ContactName = null, string? ContactPhone = null, string? ContactEmail = null,
    DateTimeOffset? ReservedUntil = null, bool PublishedToWeb = false, string? CancelReason = null, string? Notes = null, decimal Reserved = 0,
    PcBuildKind Kind = PcBuildKind.Build, int? BuyerDocumentType = null, string? BuyerDocumentNumber = null, string? BuyerComplement = null,
    string? BuyerName = null) : IAuditableResponse
{
    /// <summary>V6 · Reserva vigente (reservado y no vencido a <paramref name="now"/>).</summary>
    public bool IsReservationActive(DateTimeOffset now) => Status == PcBuildStatus.Reserved && ReservedUntil is { } until && until > now;

    /// <summary>V7 · En la auditoría el teléfono y el documento van enmascarados (solo los 3 últimos caracteres) y el correo, el
    /// complemento y la razón social ocultos, igual que en el comando de la tienda (reglas S-06 y P-05).</summary>
    object IAuditableResponse.AuditResult => this with
    {
        ContactPhone = ContactPhone is null ? null : Storefront.CreateStorefrontReservationCommand.Mask(ContactPhone),
        ContactEmail = ContactEmail is null ? null : "***",
        BuyerDocumentNumber = BuyerDocumentNumber is null ? null : Storefront.CreateStorefrontReservationCommand.Mask(BuyerDocumentNumber),
        BuyerComplement = BuyerComplement is null ? null : "***",
        BuyerName = BuyerName is null ? null : "***",
    };
}

/// <summary>Guarda el armado (nuevo en la sucursal activa o un borrador existente) con los precios de la lista vigente; con
/// Quote = true lo emite como cotización con vigencia de <see cref="ValidDays"/> días (precios congelados). Un armado con
/// errores de compatibilidad solo se cotiza con <see cref="AcceptIncompatible"/> = true (queda marcado, regla T-06).
/// V7: con <see cref="Kind"/> = <c>Cart</c> guarda un carrito (RES-&lt;sucursal&gt;-000001, productos sin ranura, sin
/// compatibilidad). El tipo se fija al crear: guardar un borrador existente con otro tipo se rechaza (<c>pcbuild.kind</c>).</summary>
[RequiresPermission(PermissionCodes.PcBuildManage)]
public sealed record SavePcBuildCommand(Guid? Id, string Name, string? CustomerCode, IReadOnlyList<PcBuildItemInput> Items, bool Quote = false,
    int ValidDays = 7, bool AcceptIncompatible = false, PcBuildKind Kind = PcBuildKind.Build) : IRequest<PcBuildRow>, IAuditableRequest
{
    public object AuditDetails => new { Id, Name, CustomerCode, Items, Quote, ValidDays, AcceptIncompatible, Kind };
}

/// <summary>Armados de las sucursales visibles (los 500 más recientes), por estado y, V6, por canal (web o escritorio). V7: y
/// por tipo (armados de PC o carritos).</summary>
[RequiresPermission(PermissionCodes.SalesView)]
public sealed record GetPcBuildsQuery(PcBuildStatus? Status = null, PcBuildChannel? Channel = null, PcBuildKind? Kind = null)
    : IRequest<IReadOnlyList<PcBuildRow>>;

/// <summary>V6 · Fila de la bitácora del armado.</summary>
public sealed record PcBuildEventView(DateTimeOffset OccurredAt, PcBuildEventAction Action, PcBuildStatus Status, string Detail, string User);

public sealed record PcBuildDetail(PcBuildRow Build, PcBuildCheckView Check, IReadOnlyList<PcBuildItemView> QuotedItems,
    IReadOnlyList<PcBuildEventView>? History = null);

[RequiresPermission(PermissionCodes.SalesView)]
public sealed record GetPcBuildQuery(string Number) : IRequest<PcBuildDetail>;

/// <summary>Anula un borrador o una cotización. V6: un armado reservado se anula liberando su reserva (el stock vuelve).</summary>
[RequiresPermission(PermissionCodes.PcBuildManage)]
public sealed record CancelPcBuildCommand(string Number, string? Reason = null) : IRequest<string>, IAuditableRequest
{
    public object AuditDetails => new { Number, Reason };
}

/// <summary>V6 · Reserva el stock de una cotización vigente del escritorio por <see cref="Hours"/> horas (regla S-03): una
/// reserva por línea en la sucursal del armado, todo o nada (si falta stock informa qué piezas y cuánto hay).</summary>
[RequiresPermission(PermissionCodes.PcBuildManage)]
public sealed record ReservePcBuildCommand(string Number, int Hours = 48) : IRequest<PcBuildRow>, IAuditableRequest
{
    public object AuditDetails => new { Number, Hours };
}

/// <summary>V7 · Producto de un carrito de mostrador (sin ranura).</summary>
public sealed record CartItemInput(string Sku, int Quantity = 1);

/// <summary>
/// V7 · El personal crea y RESERVA un carrito en el mostrador para un cliente (regla P-05): cualquier producto, a los
/// precios vigentes, con el stock de cada línea reservado en la sucursal activa (todo o nada, misma transacción, regla
/// S-03), número RES-&lt;sucursal&gt;-000001. El nombre y el teléfono de quien lo recoge son obligatorios; el correo, las
/// notas y los datos para la factura, opcionales. <see cref="HoldDays"/> (1 a 3) son los días para recogerlo; sin valor,
/// las horas configuradas (48). <see cref="CustomerCode"/> lo liga a un cliente registrado.
/// </summary>
[RequiresPermission(PermissionCodes.PcBuildManage)]
public sealed record ReserveCartCommand(IReadOnlyList<CartItemInput> Items, string ContactName, string ContactPhone, string? ContactEmail = null,
    string? Notes = null, int? HoldDays = null, Storefront.ReservationBuyerInput? Buyer = null, string? CustomerCode = null, string? Name = null)
    : IRequest<PcBuildRow>, IAuditableRequest
{
    // El teléfono, el correo y los datos para la factura no van a la auditoría completos (reglas S-06 y P-05)
    public object AuditDetails => new
    {
        Items, ContactName, ContactPhone = Storefront.CreateStorefrontReservationCommand.Mask(ContactPhone), ContactEmail = ContactEmail is null ? null : "***",
        Notes, HoldDays, Buyer = Buyer?.Masked, CustomerCode, Name,
    };

    public override string ToString() => $"ReserveCartCommand {{ Items = {Items?.Count ?? 0} }}";
}

/// <summary>V6 · Libera la reserva de un armado (web o escritorio): pasa a Anulado con motivo y el stock vuelve.</summary>
[RequiresPermission(PermissionCodes.PcBuildManage)]
public sealed record ReleasePcBuildReservationCommand(string Number, string Reason) : IRequest<PcBuildRow>, IAuditableRequest
{
    public object AuditDetails => new { Number, Reason };
}

/// <summary>V6 · Publica (o retira) un armado del escritorio como armado sugerido en la tienda web.</summary>
[RequiresPermission(PermissionCodes.PcBuildManage)]
public sealed record PublishPcBuildCommand(string Number, bool Published = true) : IRequest<PcBuildRow>, IAuditableRequest
{
    public object AuditDetails => new { Number, Published };
}

/// <summary>
/// Cobra en la caja (turno abierto del usuario, en la sucursal del armado) una cotización VIGENTE a sus precios cotizados,
/// con los casos de uso normales de venta (poka-yoke, series, factura del SIN, pago y asiento), y la marca vendida con la
/// venta vinculada, todo en la MISMA transacción. Las series de las piezas serializadas van en <see cref="Serials"/>. El
/// cliente es el del armado (o <see cref="CustomerCode"/>, o consumidor final). V7: vende también carritos y, si el cajero
/// no capturó comprador (<see cref="Buyer"/>), factura con los datos para la factura que dejó quien reservó.
/// </summary>
[RequiresPermission(PermissionCodes.PosOperate)]
[RequiresPermission(PermissionCodes.MovementsRegisterSales)]
public sealed record SellPcBuildCommand(string Number, string PaymentMethodCode, IReadOnlyList<SkuSerials>? Serials = null, decimal? CashReceived = null,
    string? PaymentReference = null, Billing.FiscalBuyerInput? Buyer = null, string? CardNumber = null, string? CustomerCode = null)
    : IRequest<CheckoutResult>, IAuditableRequest
{
    // La tarjeta se audita SOLO enmascarada (regla F-06)
    public object AuditDetails => new
    {
        Number, PaymentMethodCode, Serials = Serials?.Select(s => new { s.Sku, s.Serials }), CashReceived, CustomerCode,
        Buyer = Buyer is null ? null : new { Buyer.DocumentType, Buyer.DocumentNumber, Buyer.Complement, Buyer.Name },
        Card = CardNumber is null ? null : (CardNumber.Length >= 8 ? CardNumber[..4] + "…" + CardNumber[^4..] : "…"),
    };

    public override string ToString() => $"SellPcBuildCommand {Number} {PaymentMethodCode}";
}

// --------------------------------------------------------------------------------------------------- tablero del rubro
public sealed record NamedAmount(string Name, decimal Amount, decimal Quantity);

public sealed record NamedCount(string Name, int Count);

/// <summary>V6: <paramref name="WebReservationsActive"/> y <paramref name="WebReservationsValue"/> son las reservas web vigentes
/// (armados del canal Web reservados y no vencidos) y su total en Bs.</summary>
public sealed record TechDashboardView(IReadOnlyList<NamedAmount> SalesByCategory, IReadOnlyList<NamedAmount> SalesByPlatform,
    IReadOnlyList<NamedAmount> TopGpus, IReadOnlyList<NamedAmount> TopConsoles, IReadOnlyList<NamedCount> OpenClaimsByStatus,
    IReadOnlyList<NamedCount> SerialsInStockByCategory, int SerialsInStock, int OpenClaims, int ClaimsOutOfWarranty, int QuotesOpen,
    decimal QuotesValue, int BuildsSold, decimal BuildsSoldValue, int SerializedWithoutSerials, int WebReservationsActive = 0,
    decimal WebReservationsValue = 0);

/// <summary>Indicadores de la tienda de tecnología de los últimos <see cref="Days"/> días en las sucursales visibles: ventas
/// por categoría raíz y por plataforma, lo más vendido de las categorías de tarjetas de video y de consolas, casos RMA
/// abiertos por estado, series en stock por categoría y armados cotizados frente a vendidos.</summary>
[RequiresPermission(PermissionCodes.ReportsView)]
public sealed record GetTechDashboardQuery(int Days = 30, string GpuCategoryCode = "GPU", string ConsoleCategoryCode = "CON")
    : IRequest<TechDashboardView>;
