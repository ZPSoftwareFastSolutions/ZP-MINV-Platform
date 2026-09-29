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
/// recibe como opcionales: sin registro valen los predeterminados (48 h). V7: <see cref="MaxReservationHours"/> (72) es el tope
/// de cualquier reserva de la tienda; quien reserva puede pedir de 1 a <see cref="MaxHoldDays"/> días para recogerla.</summary>
public sealed record StorefrontOptions(int ReservationHours = StorefrontOptions.DefaultReservationHours,
    int MaxReservationHours = StorefrontOptions.DefaultMaxReservationHours)
{
    public const int DefaultReservationHours = 48;

    /// <summary>V7 · Tope predeterminado de una reserva: 72 horas.</summary>
    public const int DefaultMaxReservationHours = 72;

    /// <summary>V7 · Días que admite el contrato en <c>holdDays</c> (1 a 3: 24, 48 o 72 horas).</summary>
    public const int HoldDaysLimit = 3;

    /// <summary>V7 · Tope efectivo en horas (de 1 hora a 30 días).</summary>
    public int EffectiveMaxHours => Math.Clamp(MaxReservationHours, 1, 24 * 30);

    /// <summary>Horas de una reserva que no indica los días (nunca más que el tope).</summary>
    public int EffectiveHours => Math.Clamp(ReservationHours, 1, EffectiveMaxHours);

    /// <summary>V7 · Días que puede pedir quien reserva: los que caben en el tope, de 1 a <see cref="HoldDaysLimit"/>.</summary>
    public int MaxHoldDays => Math.Clamp(EffectiveMaxHours / 24, 1, HoldDaysLimit);

    /// <summary>V7 · Horas de la reserva: los días pedidos (24 h cada uno, <c>storefront.hold_days</c> si pasan de
    /// <see cref="MaxHoldDays"/>) o, sin días, <see cref="EffectiveHours"/>.</summary>
    public int HoursFor(int? holdDays)
    {
        if (holdDays is not { } days)
        {
            return EffectiveHours;
        }
        Guard.That(days >= 1 && days <= MaxHoldDays, "storefront.hold_days",
            MaxHoldDays == 1 ? "La reserva se guarda 1 día." : $"La reserva se guarda de 1 a {MaxHoldDays} días.");
        return Math.Min(days * 24, EffectiveMaxHours);
    }
}

/// <summary>V7 · Tipo de reserva en el contrato público: <c>"build"</c> (armado de PC, por defecto) o <c>"cart"</c> (carrito).</summary>
public static class StorefrontKinds
{
    public const string Build = "build";
    public const string Cart = "cart";

    public static string Text(PcBuildKind kind) => kind == PcBuildKind.Cart ? Cart : Build;

    /// <summary>Del texto del contrato al tipo del dominio; vacío = armado. Otro valor es un dato inválido (400).</summary>
    public static PcBuildKind Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" or Build => PcBuildKind.Build,
        Cart => PcBuildKind.Cart,
        _ => throw new RequestValidationException([$"El tipo de reserva «{value}» no existe: use «{Build}» o «{Cart}»."]),
    };
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

/// <summary>Instantánea del catálogo. V7: <paramref name="ReservationHours"/> (horas de una reserva que no indica los días) y
/// <paramref name="MaxHoldDays"/> (días que puede pedir quien reserva) salen de la configuración del servidor: la web no los fija.</summary>
public sealed record StorefrontCatalogView(StorefrontCompany Company, StorefrontBranch Branch, IReadOnlyList<StorefrontCategory> Categories,
    IReadOnlyList<StorefrontBrand> Brands, IReadOnlyList<StorefrontProduct> Products, IReadOnlyList<StorefrontPreset> Presets, DateTimeOffset GeneratedAt,
    int ReservationHours = StorefrontOptions.DefaultReservationHours, int MaxHoldDays = StorefrontOptions.HoldDaysLimit);

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

/// <summary>V7 · Datos para la factura que deja quien reserva (opcionales; instantánea del visitante): tipo de documento del
/// SIN (1 CI, 2 CEX, 3 PAS, 4 OD, 5 NIT), número (CI y NIT solo dígitos), complemento (solo con CI) y nombre o razón social.
/// La caja los precarga al cobrar. Siguen la regla S-06: enmascarados en la auditoría y nunca en la API pública.</summary>
public sealed record ReservationBuyerInput(int DocumentType, string DocumentNumber, string? Complement = null, string? Name = null)
{
    /// <summary>Para la auditoría: el número como el teléfono (solo los 3 últimos caracteres) y el resto oculto.</summary>
    internal object Masked => new
    {
        DocumentType, DocumentNumber = CreateStorefrontReservationCommand.Mask(DocumentNumber?.Trim()),
        Complement = string.IsNullOrWhiteSpace(Complement) ? null : "***", Name = string.IsNullOrWhiteSpace(Name) ? null : "***",
    };

    public override string ToString() => $"ReservationBuyerInput {{ DocumentType = {DocumentType} }}";
}

/// <summary>Línea de una reserva. V7: <paramref name="Slot"/> es null en el producto sin ranura de un carrito.</summary>
public sealed record StorefrontReservationLine(string? Slot, string Sku, string Name, int Quantity, decimal UnitPrice, decimal Subtotal);

/// <summary>Estado de una reserva web tal como lo ve el cliente. <see cref="Status"/>: Reserved, Sold, Cancelled o Expired;
/// <see cref="StatusText"/> en español. Nunca incluye el teléfono ni el correo completos, ni los datos para la factura. V7:
/// <see cref="Kind"/> (<c>build</c> o <c>cart</c>) y <see cref="MailQueued"/> (se encoló el correo de confirmación).</summary>
public sealed record StorefrontReservationView(string Number, string Status, string StatusText, DateTimeOffset CreatedAt, DateTimeOffset? ReservedUntil,
    decimal Total, string ContactName, string Branch, string? Notes, bool HasCompatibilityWarnings, IReadOnlyList<StorefrontReservationLine> Lines,
    string? CancelReason = null, string Kind = StorefrontKinds.Build, bool MailQueued = false);

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
/// <para>V7 (regla P-05): con <see cref="Kind"/> = <c>Cart</c> reserva un CARRITO (cualquier producto, líneas sin ranura salvo
/// que la web la mande, sin ranura única ni compatibilidad, número RES-WEB-000001). <see cref="HoldDays"/> (1 a 3) son los días
/// para recogerla; sin valor, las horas configuradas. <see cref="Buyer"/> son los datos para la factura (opcionales).</para>
/// </summary>
[RequiresPermission(PermissionCodes.StorefrontReserve)]
public sealed record CreateStorefrontReservationCommand(IReadOnlyList<StorefrontReservationLineInput> Lines, StorefrontContactInput Contact, string? Notes,
    string IdempotencyKey, string? Name = null, PcBuildKind Kind = PcBuildKind.Build, int? HoldDays = null, ReservationBuyerInput? Buyer = null)
    : IRequest<StorefrontReservationResult>, IAuditableRequest
{
    // El teléfono, el correo y los datos para la factura del cliente no van a la auditoría completos (reglas S-06 y P-05)
    public object AuditDetails => new
    {
        Lines, Contact = new { Contact.Name, Phone = Mask(Contact.Phone), Email = Contact.Email is null ? null : "***" }, Notes, IdempotencyKey, Name,
        Kind, HoldDays, Buyer = Buyer?.Masked,
    };

    internal static string Mask(string? phone) => phone is { Length: > 3 } ? new string('*', phone.Length - 3) + phone[^3..] : "***";
}

/// <summary>V7 · Reglas de entrada comunes a toda reserva (tienda, mostrador y cuenta de cliente): textos de una línea, días
/// para recogerla y datos para la factura. Las reglas de negocio siguen en el dominio (<see cref="PcBuild"/>).</summary>
public static class ReservationRules
{
    public const string ControlCharacters = "no admite saltos de línea, tabuladores ni otros caracteres de control.";

    public const string HoldDaysMessage = "Los días para recoger la reserva van de 1 a 3.";

    /// <summary>¿Texto de una línea? (los espacios y saltos de los extremos se recortan al guardar).</summary>
    public static bool IsPlain(string? value) => !Guard.HasControlCharacters(value?.Trim());
}

/// <summary>V7 · Forma de los datos para la factura de una reserva (las reglas del SIN las aplica el dominio).</summary>
public sealed class ReservationBuyerValidator : AbstractValidator<ReservationBuyerInput>
{
    public ReservationBuyerValidator()
    {
        RuleFor(x => x.DocumentType).InclusiveBetween(1, 5).WithMessage("El tipo de documento para la factura va de 1 (CI) a 5 (NIT).");
        RuleFor(x => x.DocumentNumber).NotEmpty().WithMessage("Indique el número de documento (CI o NIT) para la factura.")
            .MaximumLength(20).WithMessage("El número de documento supera 20 caracteres.")
            .Must(ReservationRules.IsPlain).WithMessage("El número de documento " + ReservationRules.ControlCharacters);
        RuleFor(x => x.Complement).MaximumLength(5).WithMessage("El complemento tiene como máximo 5 caracteres.")
            .Must(ReservationRules.IsPlain).WithMessage("El complemento " + ReservationRules.ControlCharacters);
        RuleFor(x => x.Name).MaximumLength(150).WithMessage("El nombre o razón social supera 150 caracteres.")
            .Must(ReservationRules.IsPlain).WithMessage("El nombre o razón social " + ReservationRules.ControlCharacters);
    }
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
            RuleFor(x => x.Contact.Name).NotEmpty().WithMessage("Indique el nombre de quien reserva.").MaximumLength(120)
                .Must(ReservationRules.IsPlain).WithMessage("El nombre de contacto " + ReservationRules.ControlCharacters);
            RuleFor(x => x.Contact.Phone).NotEmpty().WithMessage("Indique un teléfono o WhatsApp para confirmar la reserva.").MaximumLength(30);
            RuleFor(x => x.Contact.Email).MaximumLength(254);
        });
        RuleFor(x => x.Notes).MaximumLength(500).WithMessage("Las notas superan 500 caracteres.")
            .Must(ReservationRules.IsPlain).WithMessage("Las notas van en una sola línea: " + ReservationRules.ControlCharacters);
        RuleFor(x => x.Name).MaximumLength(150)
            .Must(ReservationRules.IsPlain).WithMessage("El nombre de la reserva " + ReservationRules.ControlCharacters);
        RuleFor(x => x.IdempotencyKey).NotEmpty().WithMessage("Falta la cabecera Idempotency-Key.").MaximumLength(100);
        RuleFor(x => x.Kind).IsInEnum().WithMessage("El tipo de reserva no existe.");
        RuleFor(x => x.HoldDays).InclusiveBetween(1, StorefrontOptions.HoldDaysLimit).WithMessage(ReservationRules.HoldDaysMessage);
        RuleFor(x => x.Buyer!).SetValidator(new ReservationBuyerValidator()).When(x => x.Buyer is not null);
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
