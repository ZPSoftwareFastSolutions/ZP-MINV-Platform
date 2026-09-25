using System.Globalization;

namespace MINV.Infrastructure.Importing.V21;

/// <summary>Filas de la V2.1 ya tipadas (nombres de columna tal como están en el libro colaborativo).</summary>
public sealed record V21User(string Email, string Name, string Role, bool Active);

public sealed record V21Category(string Code, string Name);

public sealed record V21Unit(string Code, string Name, bool AllowsDecimals, string? Description);

public sealed record V21Supplier(int Index, string Name, string? TaxId, string? Contact, string? Phone, string? Email, int? LeadTimeDays);

public sealed record V21Product(int Index, string Sku, string Name, string Category, string Unit, decimal Minimum, decimal Maximum,
    decimal UnitCost, string Supplier, string Location, bool Active);

public sealed record V21Movement(string Id, string Type, DateOnly BusinessDate, string Sku, decimal Quantity, string? Document,
    string? Notes, string Status, string RecordedBy, string UserEmail, double TimestampSerial, bool IsSalesFragment)
{
    public bool IsConsolidated => Status.StartsWith('✔');
}

public sealed record V21Activity(string Id, double TimestampSerial, string UserEmail, string Name, string Script, string Result, string Detail);

public sealed record V21Count(string Sku, decimal Counted);

/// <summary>Fila de la instantánea 15_STOCK (para la verificación de paridad).</summary>
public sealed record V21StockRow(string Sku, decimal Entries, decimal Issues, decimal Stock, string Status, decimal Sales30Days,
    int? CoverageDays, int? Rank);

/// <summary>
/// Libro colaborativo de la V2.1 leído a objetos. Toma los datos de las tablas oficiales (nunca de las instantáneas,
/// que son derivadas) y los parámetros de 01_CONFIG por su nombre definido.
/// </summary>
public sealed class V21Workbook
{
    private readonly XlsxTableReader _xlsx;

    public V21Workbook(XlsxTableReader xlsx)
    {
        _xlsx = xlsx;
        foreach (var t in new[] { "tblUsuarios", "tblProductos", "tblProveedores", "tblCategorias", "tblUnidades", "tblEntradas", "tblSalidas" })
        {
            _ = xlsx.Table(t);
        }
    }

    public static V21Workbook Open(string path) => new(XlsxTableReader.Open(path));

    public string CompanyName => Text(_xlsx.Name("cfgEmpresa")) ?? "EMPRESA V2.1";

    public string? TaxId => Text(_xlsx.Name("cfgNIT"));

    public string WarehouseName => Text(_xlsx.Name("cfgBodega")) ?? "Bodega principal";

    public string? CurrencyCode => Text(_xlsx.Name("cfgMoneda"));

    public decimal AlertMargin => Number(_xlsx.Name("cfgMargenAlerta")) ?? 0.20m;

    public int DaysWithoutRotation => (int)(Number(_xlsx.Name("cfgDiasSinRotacion")) ?? 60m);

    public DateOnly MinBusinessDate => Date(_xlsx.Name("cfgFechaMin")) ?? new DateOnly(2020, 1, 1);

    /// <summary>Momento del último «Recalcular stock» (serial local de Excel) o null si nunca se calculó.</summary>
    public double? SnapshotSerial => _xlsx.Name("stkActualizado") is double d && d > 0 ? d : null;

    public IReadOnlyList<V21User> Users() =>
        Rows("tblUsuarios").Where(r => Text(r["Correo"]) is not null).Select(r => new V21User(
            Text(r["Correo"])!.ToLowerInvariant(), Text(r["Nombre"]) ?? Text(r["Correo"])!, (Text(r["Rol"]) ?? "CONSULTA").ToUpperInvariant(),
            !string.Equals(Text(r["Activo"]), "NO", StringComparison.OrdinalIgnoreCase))).ToList();

    public IReadOnlyList<V21Category> Categories() =>
        Rows("tblCategorias").Where(r => Text(r["Categoría"]) is not null)
            .Select(r => new V21Category(Text(r["Código"]) ?? Text(r["Categoría"])!, Text(r["Categoría"])!)).ToList();

    public IReadOnlyList<V21Unit> Units() =>
        Rows("tblUnidades").Where(r => Text(r["Código"]) is not null).Select(r => new V21Unit(Text(r["Código"])!,
            Text(r["Unidad"]) ?? Text(r["Código"])!, string.Equals(Text(r["Decimales"]), "SI", StringComparison.OrdinalIgnoreCase),
            Text(r["Descripción"]))).ToList();

    public IReadOnlyList<V21Supplier> Suppliers() =>
        Rows("tblProveedores").Select((r, i) => (r, i)).Where(x => Text(x.r["Proveedor"]) is not null).Select(x => new V21Supplier(
            x.i, Text(x.r["Proveedor"])!, Text(x.r["NIT"]), Text(x.r["Contacto"]), Text(x.r["Teléfono"]), Text(x.r["Correo"]),
            Number(x.r["DiasEntrega"]) is { } d ? (int)d : null)).ToList();

    public IReadOnlyList<V21Product> Products() =>
        Rows("tblProductos").Select((r, i) => (r, i)).Where(x => Text(x.r["SKU"]) is not null).Select(x => new V21Product(
            x.i, Text(x.r["SKU"])!, Text(x.r["Producto"]) ?? Text(x.r["SKU"])!, Text(x.r["Categoría"]) ?? "GENERAL",
            Text(x.r["Unidad"]) ?? "UND", Number(x.r["StockMin"]) ?? 0, Number(x.r["StockMax"]) ?? 0, Number(x.r["CostoUnitario"]) ?? 0,
            Text(x.r["Proveedor"]) ?? string.Empty, Text(x.r["Ubicación"]) ?? string.Empty,
            !string.Equals(Text(x.r["Activo"]), "NO", StringComparison.OrdinalIgnoreCase))).ToList();

    public IReadOnlyList<V21Movement> Movements() =>
        Ledger("tblEntradas", false).Concat(Ledger("tblSalidas", true))
            .OrderBy(m => m.TimestampSerial).ThenBy(m => m.Id, StringComparer.Ordinal).ToList();

    public IReadOnlyList<V21Activity> Activity() =>
        !_xlsx.HasTable("tblActividad")
            ? []
            : Rows("tblActividad").Where(r => Text(r["ID"]) is not null).Select(r => new V21Activity(Text(r["ID"])!,
                (double)(Number(r["Timestamp"]) ?? 0), (Text(r["Usuario_O365"]) ?? string.Empty).ToLowerInvariant(),
                Text(r["Nombre"]) ?? string.Empty, Text(r["Script"]) ?? string.Empty, Text(r["Resultado"]) ?? string.Empty,
                Text(r["Detalle"]) ?? string.Empty)).ToList();

    public IReadOnlyList<V21Count> Counts() =>
        !_xlsx.HasTable("tblConteo")
            ? []
            : Rows("tblConteo").Where(r => Text(r["SKU"]) is not null && Number(r["Conteo"]) is not null)
                .Select(r => new V21Count(Text(r["SKU"])!, Number(r["Conteo"])!.Value)).ToList();

    public IReadOnlyList<V21StockRow> Snapshot() =>
        !_xlsx.HasTable("tblStock")
            ? []
            : Rows("tblStock").Where(r => Text(r["SKU"]) is not null).Select(r => new V21StockRow(Text(r["SKU"])!,
                Number(r["Entradas"]) ?? 0, Number(r["Salidas"]) ?? 0, Number(r["StockActual"]) ?? 0, Text(r["Estado"]) ?? string.Empty,
                Number(r["Salidas30d"]) ?? 0, Number(r["CoberturaDias"]) is { } c ? (int)c : null,
                Number(r["RankSalidas30d"]) is { } k ? (int)k : null)).ToList();

    /// <summary>SKU de las alertas de 16_ALERTAS, en su orden.</summary>
    public IReadOnlyList<string> SnapshotAlerts() =>
        !_xlsx.HasTable("tblAlertas") ? [] : Rows("tblAlertas").Select(r => Text(r["SKU"])).OfType<string>().ToList();

    /// <summary>(SKU, cantidad a pedir) de 18_PEDIDO, en su orden.</summary>
    public IReadOnlyList<(string Sku, decimal Quantity)> SnapshotOrder() =>
        !_xlsx.HasTable("tblPedido")
            ? []
            : Rows("tblPedido").Where(r => Text(r["SKU"]) is not null).Select(r => (Text(r["SKU"])!, Number(r["APedir"]) ?? 0)).ToList();

    private IEnumerable<V21Movement> Ledger(string table, bool sales) =>
        Rows(table).Where(r => Text(r["ID"]) is not null).Select(r => new V21Movement(Text(r["ID"])!, Text(r["Tipo"]) ?? string.Empty,
            Date(r["Fecha"]) ?? throw new InvalidDataException($"{table}: la fila {Text(r["ID"])} no tiene fecha."),
            Text(r["SKU"]) ?? string.Empty, Number(r["Cantidad"]) ?? 0, Text(r["Documento"]), Text(r["Observaciones"]),
            Text(r["Estado"]) ?? string.Empty, Text(r["Registró"]) ?? string.Empty, (Text(r["Usuario_O365"]) ?? string.Empty).ToLowerInvariant(),
            (double)(Number(r["Timestamp"]) ?? 0), sales));

    private IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows(string table) => _xlsx.Table(table).Rows;

    internal static string? Text(object? value) => value switch
    {
        null => null,
        string s => string.IsNullOrWhiteSpace(s) ? null : s.Trim(),
        double d => d.ToString(CultureInfo.InvariantCulture),
        bool b => b ? "SI" : "NO",
        _ => value.ToString(),
    };

    internal static decimal? Number(object? value) => value switch
    {
        double d => (decimal)d,
        string s when decimal.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) => n,
        _ => null,
    };

    internal static DateOnly? Date(object? value) =>
        value is double d && d > 0 ? DateOnly.FromDateTime(XlsxTableReader.FromSerial(Math.Floor(d))) : null;
}
