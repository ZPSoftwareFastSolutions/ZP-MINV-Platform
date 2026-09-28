using FluentValidation;
using MediatR;
using MINV.Application.Common;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Sales;

namespace MINV.Application.Storefront;

// =====================================================================================================================
// V6 · Tienda web conectada (reglas S-01 a S-06): contratos de la API pública de tienda (/storefront/v1). Todo lo que la
// web muestra sale de aquí; los manejadores viven en StorefrontCatalog.cs y StorefrontReservations.cs. Los DTO son el
// contrato JSON exacto de docs/integration/storefront-api-v1.md (camelCase al serializar). NUNCA exponen costos, clientes,
// ventas ni usuarios.
// =====================================================================================================================

/// <summary>Parámetros de la tienda que fija el servidor (el gateway los toma de <c>Minv:Storefront</c>). El manejador los
/// recibe como opcionales: sin registro valen los predeterminados (48 h).</summary>
public sealed record StorefrontOptions(int ReservationHours = StorefrontOptions.DefaultReservationHours)
{
    public const int DefaultReservationHours = 48;

    public int EffectiveHours => Math.Clamp(ReservationHours, 1, 24 * 30);
}

// --------------------------------------------------------------------------------------------------- instantánea
public sealed record StorefrontBranch(string Code, string Name);

public sealed record StorefrontCompany(string Code, string Name, IReadOnlyList<StorefrontBranch> Branches);

/// <summary>Categoría del árbol (con el ícono de Lucide que usa la web y la cantidad de productos, incluidas sus subcategorías).</summary>
public sealed record StorefrontCategory(string Code, string Name, string Slug, string? Parent, string Icon, string Description, int ProductCount);

public sealed record StorefrontBrand(string Code, string Name, int ProductCount);

/// <summary>Especificación de la ficha: <see cref="Value"/> es número, texto o lista de textos (multivalor); <see cref="Text"/>
/// ya viene formateado («5,1 GHz», «DDR5»).</summary>
public sealed record StorefrontSpec(string Key, string Label, object Value, string Text, string? Unit, bool Filterable);

/// <summary>Producto del catálogo web. Disponibilidad = existencias − reservado en la sucursal de la tienda.</summary>
public sealed record StorefrontProduct(string Sku, string Slug, string Name, string ShortName, string Category, string CategoryName, string CategoryPath,
    string Brand, decimal Price, decimal? ListPrice, string? Image, decimal Available, decimal Reserved, decimal OnHand, string Condition,
    int WarrantyMonths, bool Serialized, int Popularity, IReadOnlyList<string> Tags, string Description, IReadOnlyList<string> Highlights,
    IReadOnlyList<StorefrontSpec> Specs);

public sealed record StorefrontPresetLine(string Slot, string Sku, int Quantity, decimal UnitPrice);

/// <summary>Armado sugerido (publicado desde el escritorio) con sus piezas a los precios cotizados.</summary>
public sealed record StorefrontPreset(string Id, string Number, string Name, string Tier, decimal Total, bool Available, IReadOnlyList<StorefrontPresetLine> Lines);

public sealed record StorefrontCatalogView(StorefrontCompany Company, StorefrontBranch Branch, IReadOnlyList<StorefrontCategory> Categories,
    IReadOnlyList<StorefrontBrand> Brands, IReadOnlyList<StorefrontProduct> Products, IReadOnlyList<StorefrontPreset> Presets, DateTimeOffset GeneratedAt);

/// <summary>Instantánea completa del catálogo web de la sucursal de la tienda (empresa, sucursales, categorías, marcas,
/// productos con ficha y disponibilidad, armados publicados).</summary>
[RequiresPermission(PermissionCodes.StorefrontRead)]
public sealed record GetStorefrontCatalogQuery : IRequest<StorefrontCatalogView>;

/// <summary>Un producto por su <c>slug</c> (el SKU en minúsculas) con la disponibilidad fresca.</summary>
[RequiresPermission(PermissionCodes.StorefrontRead)]
public sealed record GetStorefrontProductQuery(string Slug) : IRequest<StorefrontProduct>;

public sealed record StorefrontImage(byte[] Content, string ContentType, string ETag);

/// <summary>Imagen del producto (PNG o JPEG) con su ETag (el id de la imagen).</summary>
[RequiresPermission(PermissionCodes.StorefrontRead)]
public sealed record GetStorefrontProductImageQuery(string Sku) : IRequest<StorefrontImage>;

[RequiresPermission(PermissionCodes.StorefrontRead)]
public sealed record GetStorefrontPresetsQuery : IRequest<IReadOnlyList<StorefrontPreset>>;

// --------------------------------------------------------------------------------------------------- reservas
/// <summary>Pieza a reservar. <see cref="Slot"/> es la ranura de la web (cpu, motherboard, ram, gpu, storage, psu, case, cooler,
/// monitor, peripherals, software); si falta se deduce de la ficha o la categoría del producto.</summary>
public sealed record StorefrontReservationLineInput(string Sku, int Quantity = 1, string? Slot = null);

public sealed record StorefrontContactInput(string Name, string Phone, string? Email = null);

public sealed record StorefrontReservationLine(string Slot, string Sku, string Name, int Quantity, decimal UnitPrice, decimal Subtotal);

/// <summary>Estado de una reserva web tal como lo ve el cliente. <see cref="Status"/>: Reserved, Sold, Cancelled o Expired;
/// <see cref="StatusText"/> en español. Nunca incluye el teléfono ni el correo completos.</summary>
public sealed record StorefrontReservationView(string Number, string Status, string StatusText, DateTimeOffset CreatedAt, DateTimeOffset? ReservedUntil,
    decimal Total, string ContactName, string Branch, string? Notes, bool HasCompatibilityWarnings, IReadOnlyList<StorefrontReservationLine> Lines,
    string? CancelReason = null);

public sealed record StorefrontReservationResult(StorefrontReservationView Reservation, bool Replayed);

/// <summary>Pieza que no alcanza para reservar (respuesta 409 <c>storefront.insufficient_stock</c>).</summary>
public sealed record StorefrontShortage(string Sku, string Name, int Requested, decimal Available);

/// <summary>V6 · Falta stock para una o más piezas: no se reserva nada (todo o nada, regla S-03) y se informa qué falta.</summary>
public sealed class StorefrontStockException(IReadOnlyList<StorefrontShortage> shortages)
    : DomainException(ErrorCode, $"No hay stock suficiente para {shortages.Count} pieza(s): " +
                            string.Join("; ", shortages.Select(s => $"{s.Sku} (pedido {s.Requested}, disponible {Quantities.Format(s.Available)})")))
{
    public const string ErrorCode = "storefront.insufficient_stock";

    public IReadOnlyList<StorefrontShortage> Shortages { get; } = shortages;
}

/// <summary>
/// Crea la reserva desde la tienda web (regla S-03): un armado del canal Web cotizado a los precios vigentes (la
/// compatibilidad se evalúa y se informa, nunca bloquea) y una reserva de stock por línea en la sucursal de la tienda, todo
/// en UNA transacción con reintento optimista; si falta stock de cualquier pieza no se reserva nada (409 con el detalle).
/// Idempotente por <see cref="IdempotencyKey"/> (<c>processed_requests</c>, regla S-05): repetir igual devuelve la misma
/// reserva; la misma llave con otro contenido se rechaza (422).
/// </summary>
[RequiresPermission(PermissionCodes.StorefrontReserve)]
public sealed record CreateStorefrontReservationCommand(IReadOnlyList<StorefrontReservationLineInput> Lines, StorefrontContactInput Contact, string? Notes,
    string IdempotencyKey, string? Name = null) : IRequest<StorefrontReservationResult>, IAuditableRequest
{
    // El teléfono y el correo del cliente no van a la auditoría completos (regla S-06)
    public object AuditDetails => new
    {
        Lines, Contact = new { Contact.Name, Phone = Mask(Contact.Phone), Email = Contact.Email is null ? null : "***" }, Notes, IdempotencyKey, Name,
    };

    internal static string Mask(string? phone) => phone is { Length: > 3 } ? new string('*', phone.Length - 3) + phone[^3..] : "***";
}

public sealed class CreateStorefrontReservationValidator : AbstractValidator<CreateStorefrontReservationCommand>
{
    public CreateStorefrontReservationValidator()
    {
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Agregue al menos una pieza al armado.")
            .Must(l => l.Count <= PcBuild.MaxLines).WithMessage($"Una reserva admite como máximo {PcBuild.MaxLines} líneas.");
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.Sku).NotEmpty().WithMessage("Cada pieza necesita su SKU.").MaximumLength(60);
            l.RuleFor(x => x.Quantity).InclusiveBetween(1, PcBuild.MaxQuantity).WithMessage($"La cantidad de cada pieza va de 1 a {PcBuild.MaxQuantity}.");
        });
        RuleFor(x => x.Contact).NotNull().WithMessage("Indique sus datos de contacto.");
        When(x => x.Contact is not null, () =>
        {
            RuleFor(x => x.Contact.Name).NotEmpty().WithMessage("Indique el nombre de quien reserva.").MaximumLength(120);
            RuleFor(x => x.Contact.Phone).NotEmpty().WithMessage("Indique un teléfono o WhatsApp para confirmar la reserva.").MaximumLength(30);
            RuleFor(x => x.Contact.Email).MaximumLength(254);
        });
        RuleFor(x => x.Notes).MaximumLength(500).WithMessage("Las notas superan 500 caracteres.");
        RuleFor(x => x.Name).MaximumLength(150);
        RuleFor(x => x.IdempotencyKey).NotEmpty().WithMessage("Falta la cabecera Idempotency-Key.").MaximumLength(100);
    }
}

/// <summary>El cliente libera su reserva (número y el teléfono con que la hizo, regla S-06): el armado pasa a Anulado y el
/// stock vuelve a estar disponible.</summary>
[RequiresPermission(PermissionCodes.StorefrontReserve)]
public sealed record CancelStorefrontReservationCommand(string Number, string Phone) : IRequest<StorefrontReservationView>, IAuditableRequest
{
    public object AuditDetails => new { Number, Phone = CreateStorefrontReservationCommand.Mask(Phone) };
}

/// <summary>Estado de una reserva: solo con el número Y el teléfono con que se hizo (regla S-06).</summary>
[RequiresPermission(PermissionCodes.StorefrontRead)]
public sealed record GetStorefrontReservationQuery(string Number, string Phone) : IRequest<StorefrontReservationView>;

/// <summary>Trabajo del sistema (cada 5 min en el gateway): cierra las reservas vencidas (<c>Cancelled</c> con motivo
/// «Vencida») y devuelve el stock (regla S-04: una reserva vencida nunca se cierra «al leer»). Devuelve cuántas cerró.</summary>
[RequiresPermission(PermissionCodes.StorefrontReserve)]
public sealed record ExpirePcBuildReservationsCommand : IRequest<int>, IAuditableRequest
{
    public object AuditDetails => new { Operacion = "vencimiento de reservas de armados" };
}
