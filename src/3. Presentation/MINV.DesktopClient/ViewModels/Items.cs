using System.Text.Json;
using MINV.Application.Iam;
using MINV.Application.Inventory.PhysicalCounts;
using MINV.Application.Inventory.Queries;
using MINV.DesktopClient.Controls;
using MINV.DesktopClient.Mvvm;
using MINV.DesktopClient.Services;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;

namespace MINV.DesktopClient.ViewModels;

// Filas y tarjetas que muestran las pantallas: datos de la aplicación ya formateados para la vista (sin lógica de negocio).

/// <summary>Chip de filtro con contador («Agotado 3»).</summary>
public sealed class FilterChip(string label, object? value, int count, string? brushKey = null) : ObservableObject
{
    private bool _selected;

    public string Label { get; } = label;

    public object? Value { get; } = value;

    public int Count { get; } = count;

    public string? BrushKey { get; } = brushKey;

    public bool HasColor => BrushKey is not null;

    public bool IsSelected
    {
        get => _selected;
        set => Set(ref _selected, value);
    }
}

/// <summary>Fila de la pantalla de stock.</summary>
public sealed class StockItem(StockRow r)
{
    public StockRow Row { get; } = r;

    public string Sku => Row.Sku;

    public string Name => Row.Name;

    public string Category => Row.Category;

    public string Supplier => Row.Supplier;

    public string Unit => Row.Unit;

    public decimal Stock => Row.Stock;

    public string StockText => Fmt.Qty(Row.Stock);

    public string MinMaxText => Row.Maximum > 0 || Row.Minimum > 0 ? $"{Fmt.Qty(Row.Minimum)} / {Fmt.Qty(Row.Maximum)}" : "—";

    public double Level => (double)(Row.Level ?? (Row.Stock > 0 ? 1m : 0m));

    public StockStatusCode Status => Row.Status;

    public int StatusOrder => (int)Row.Status;

    public int CoverageSort => Row.CoverageDays ?? int.MaxValue;

    public string CoverageText => Fmt.Coverage(Row.CoverageDays);

    public decimal Sales30 => Row.Sales30Days;

    public string Sales30Text => Row.Sales30Days > 0 ? Fmt.Qty(Row.Sales30Days) : "—";

    public decimal Value => Row.InventoryValue;

    public string ValueText => Fmt.Money(Row.InventoryValue);

    public string LastMovementText => Row.LastMovement is { } d
        ? Row.DaysWithoutMovement is 0 ? "hoy" : Row.DaysWithoutMovement == 1 ? "ayer" : $"hace {Row.DaysWithoutMovement} días"
        : "sin movimientos";

    public int LastMovementSort => Row.DaysWithoutMovement ?? int.MaxValue;

    public string RankText => Row.SalesRank is { } rank ? $"#{rank}" : "";

    public bool IsActive => Row.IsActive;
}

/// <summary>Alerta priorizada (16_ALERTAS de la V2.1) con la acción sugerida.</summary>
public sealed class AlertItem(AlertRow a)
{
    private static readonly Dictionary<StockStatusCode, string> Actions = StockRules.Defaults.ToDictionary(d => d.Status, d => d.Action);

    public AlertRow Row { get; } = a;

    public int Position => Row.Position;

    public StockStatusCode Status => Row.Status;

    public string Sku => Row.Sku;

    public string Name => Row.Name;

    public string Category => Row.Category;

    public string Supplier => string.IsNullOrWhiteSpace(Row.Supplier) ? "Sin proveedor" : Row.Supplier;

    public string StockText => $"{Fmt.Qty(Row.Stock)} {Row.Unit}";

    public string MinimumText => $"mín. {Fmt.Qty(Row.Minimum)}";

    public string ShortfallText => Row.Shortfall > 0 ? $"faltan {Fmt.Qty(Row.Shortfall)} {Row.Unit}" : "";

    public string SuggestedText => Row.SuggestedQuantity > 0 ? $"{Fmt.Qty(Row.SuggestedQuantity)} {Row.Unit}" : "—";

    public bool HasSuggestion => Row.SuggestedQuantity > 0;

    public double Level => Row.Maximum > 0 ? (double)Math.Clamp(Row.Stock / Row.Maximum, 0, 1) : Row.Stock > 0 ? 1 : 0;

    public string Action => Actions.GetValueOrDefault(Row.Status, "Revisar");

    public string LastMovementText => Row.LastMovement is { } d ? "Último movimiento " + Fmt.Date(d) : "Sin movimientos";

    public bool IsReplenishable => Row.Status is StockStatusCode.OutOfStock or StockStatusCode.Critical or StockStatusCode.Low;
}

/// <summary>Línea del pedido sugerido.</summary>
public sealed class OrderLineItem(SuggestedOrderLine l)
{
    public SuggestedOrderLine Line { get; } = l;

    public string Sku => Line.Sku;

    public string Name => Line.Name;

    public StockStatusCode Status => Line.Status;

    public string StockText => $"{Fmt.Qty(Line.Stock)} {Line.Unit}";

    public string MinMaxText => $"{Fmt.Qty(Line.Minimum)} / {Fmt.Qty(Line.Maximum)}";

    public string QuantityText => $"{Fmt.Qty(Line.QuantityToOrder)} {Line.Unit}";

    public string UnitCostText => Fmt.Money(Line.UnitCost);

    public string SubtotalText => Fmt.Money(Line.Subtotal);
}

/// <summary>Pedido sugerido de un proveedor (tarjeta con sus líneas).</summary>
public sealed class SupplierGroup(string name, IReadOnlyList<OrderLineItem> lines)
{
    private SuggestedOrderLine First => Lines[0].Line;

    public string Name { get; } = name;

    public IReadOnlyList<OrderLineItem> Lines { get; } = lines;

    public string Initials => Fmt.Initials(Name);

    public string Contact => string.IsNullOrWhiteSpace(First.Contact) ? "Sin contacto registrado" : First.Contact;

    public string Phone => First.Phone;

    public string Email => First.Email;

    public bool HasPhone => !string.IsNullOrWhiteSpace(Phone);

    public bool HasEmail => !string.IsNullOrWhiteSpace(Email);

    public string LeadTimeText => First.LeadTimeDays is { } d ? $"Entrega en {d} día{(d == 1 ? "" : "s")}" : "Plazo de entrega sin definir";

    public string DeliveryText => First.EstimatedDelivery is { } d ? "Llegaría el " + Fmt.Date(d) : "";

    public string DeliveryLine => DeliveryText.Length > 0 ? $"{LeadTimeText} · {DeliveryText}" : LeadTimeText;

    public decimal Subtotal => Lines.Sum(l => l.Line.Subtotal);

    public string SubtotalText => Fmt.Money(Subtotal);

    public string CountText => Lines.Count == 1 ? "1 producto" : $"{Lines.Count} productos";
}

/// <summary>Movimiento (últimos registrados o registrados en esta sesión).</summary>
public sealed class MovementItem(RecentMovement m, DateTimeOffset now)
{
    public RecentMovement Movement { get; } = m;

    public string Sku => Movement.Sku;

    public string Name => Movement.Name;

    public string TypeName => Movement.TypeName;

    public bool IsIn => Movement.StockFactor > 0;

    public string Glyph => Fmt.MovementGlyph(Movement.TypeCode, Movement.StockFactor);

    public string QuantityText => (IsIn ? "+" : "−") + Fmt.Qty(Movement.Quantity) + " " + Movement.Unit;

    public string WhenText { get; } = Fmt.Relative(m.RecordedAt, now);

    public string Detail => string.Join(" · ", new[] { Movement.BinCode, Movement.UserName, Movement.Document }.Where(x => !string.IsNullOrWhiteSpace(x)));
}

/// <summary>Registro de auditoría (14_ACTIVIDAD de la V2.1).</summary>
public sealed class ActivityItem(ActivityRow r, DateTimeOffset now)
{
    public ActivityRow Row { get; } = r;

    public string UserName => Row.UserName ?? Row.UserEmail ?? "Sistema";

    public string Initials => Fmt.Initials(UserName);

    public string ActionText => Fmt.Action(Row.Action);

    public AuditOutcome Outcome => Row.Outcome;

    public string OutcomeText => Fmt.Outcome(Row.Outcome);

    public string Glyph => Row.Outcome switch
    {
        AuditOutcome.Succeeded => Glyphs.CheckCircle,
        AuditOutcome.Rejected => Glyphs.Warning,
        _ => Glyphs.Error,
    };

    public string WhenText { get; } = Fmt.Relative(r.OccurredAt, now);

    public string ExactTime => Fmt.DateTime(Row.OccurredAt);

    public string Details { get; } = Summarize(r.Details);

    public bool HasDetails => Details.Length > 0;

    public string SearchText => $"{UserName} {Row.UserEmail} {ActionText} {Row.Action} {Details}";

    /// <summary>Resume el JSON de auditoría («SKU FER-001 · cantidad 5 · …»); el texto de la V2.1 se deja igual.</summary>
    private static string Summarize(string? details)
    {
        if (string.IsNullOrWhiteSpace(details))
        {
            return string.Empty;
        }
        if (!details.TrimStart().StartsWith('{'))
        {
            return details.Trim();
        }
        try
        {
            using var doc = JsonDocument.Parse(details);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
            {
                return error.GetString() ?? string.Empty;
            }
            if (root.TryGetProperty("detalle", out var legacy) && legacy.ValueKind == JsonValueKind.String)
            {
                // Actividad migrada de la V2.1 (14_ACTIVIDAD): se muestra su detalle original
                return legacy.GetString()?.Trim() ?? string.Empty;
            }
            var parts = new List<string>();
            if (root.TryGetProperty("request", out var request) && request.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in request.EnumerateObject())
                {
                    if (p.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Object or JsonValueKind.Array || p.Name.EndsWith("Id", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    parts.Add($"{Friendly(p.Name)}: {p.Value}");
                }
            }
            if (root.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object
                && result.TryGetProperty("Message", out var message) && message.ValueKind == JsonValueKind.String)
            {
                parts.Add(message.GetString()!.TrimStart('✔', ' '));
            }
            return string.Join(" · ", parts);
        }
        catch (JsonException)
        {
            return details;
        }
    }

    private static string Friendly(string name) => name switch
    {
        "Sku" => "SKU",
        "BinCode" => "Posición",
        "MovementTypeCode" => "Tipo",
        "Quantity" => "Cantidad",
        "BusinessDate" => "Fecha",
        "DocumentReference" => "Documento",
        "LotNumber" => "Lote",
        "WarehouseCode" => "Almacén",
        "CountDate" => "Fecha del conteo",
        "Confirmed" => "Confirmado",
        "RegisterCode" => "Caja",
        "OpeningCash" => "Fondo",
        "CountedCash" => "Arqueo",
        "Operacion" => "Operación",
        _ => name,
    };
}

/// <summary>Línea del kardex de un producto.</summary>
public sealed class KardexItem(KardexLine l)
{
    public KardexLine Line { get; } = l;

    public string DateText => Fmt.Date(Line.BusinessDate);

    public string TimeText => Line.RecordedAt.ToLocalTime().ToString("HH:mm", Fmt.Culture);

    public string TypeName => Line.TypeName;

    public bool IsIn => Line.Signed > 0;

    public string Glyph => Fmt.MovementGlyph(Line.TypeCode, (short)Math.Sign(Line.Signed));

    public string SignedText => Fmt.Signed(Line.Signed);

    public string BalanceText => Fmt.Qty(Line.Balance);

    public string Detail => string.Join(" · ", new[] { Line.BinCode, Line.Document, Line.UserName }.Where(x => !string.IsNullOrWhiteSpace(x)));

    public string? Notes => Line.Notes;

    public bool HasNotes => !string.IsNullOrWhiteSpace(Line.Notes);
}

/// <summary>Línea de la toma física: contado frente al sistema.</summary>
public sealed class CountLineItem(PhysicalCountSheetLine l, DateTimeOffset now)
{
    public PhysicalCountSheetLine Line { get; } = l;

    public string Sku => Line.Sku;

    public string Name => Line.Name;

    public string BinCode => Line.BinCode;

    public string SystemText => Fmt.Qty(Line.SystemQuantity, Line.Unit);

    public string CountedText => Fmt.Qty(Line.CountedQuantity, Line.Unit);

    public string DifferenceText => Line.Difference == 0 ? "Cuadra" : Fmt.Signed(Line.Difference) + " " + Line.Unit;

    /// <summary>1 sobrante, −1 faltante, 0 cuadra.</summary>
    public int Kind => Math.Sign(Line.Difference);

    public string CountedBy => $"{Line.CountedBy ?? "—"} · {Fmt.Relative(Line.CountedAt, now)}";
}

/// <summary>Tipo de movimiento como tarjeta seleccionable.</summary>
public sealed class MovementTypeOption(MovementTypeItem t) : ObservableObject
{
    private bool _selected;

    public MovementTypeItem Type { get; } = t;

    public string Code => Type.Code;

    public string Name => Type.Name;

    /// <summary>Nombre en tipo oración para la tarjeta («Devolución de cliente»).</summary>
    public string Title => Fmt.SentenceCase(Type.Name);

    public string Description => Type.Description ?? "";

    public bool Increases => Type.StockFactor > 0;

    public string SignText => Increases ? "Suma al stock" : "Resta del stock";

    public string Glyph => Fmt.MovementGlyph(Type.Code, Type.StockFactor);

    public bool RequiresNotes => Type.RequiresNotes;

    public bool IsSelected
    {
        get => _selected;
        set => Set(ref _selected, value);
    }
}

/// <summary>Leyenda del gráfico de estados.</summary>
public sealed record LegendItem(string Label, int Count, string PercentText, string BrushKey, StockStatusCode Status);

/// <summary>Producto del ranking de ventas de 30 días.</summary>
public sealed record TopSellerItem(int Rank, string Sku, string Name, string SalesText, double Share);

/// <summary>Atajo de teclado o paso de guía (pantalla de ayuda).</summary>
public sealed record HelpEntry(string Title, string Text, string Glyph = "");

public sealed record HelpGuide(string Title, string Role, string Glyph, IReadOnlyList<string> Steps);
