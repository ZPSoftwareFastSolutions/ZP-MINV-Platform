using System.Globalization;
using System.Text;
using System.Text.Json;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Domain.Catalog;
using MINV.Infrastructure.Billing.Simulator;

namespace MINV.Infrastructure.Seeding.Tecnologia;

// =====================================================================================================================
// V4.2 · Datos del rubro de la empresa de prueba Tech Zone Gaming S.R.L. (regla T-09): catalogo-tecnologia.json,
// catalogo-productos-sin-tecnologia.csv e Imagenes/*.png son recursos EMBEBIDOS de MINV.Infrastructure. El lector los
// convierte en records internos tipados y los VALIDA al cargarlos (categorías, especificaciones, opciones, marcas,
// proveedores, clientes, armados, homologación con el SIN e imágenes): un JSON inconsistente falla con la lista de
// errores antes de tocar la base, nunca a mitad de la carga.
// =====================================================================================================================

/// <summary>Empresa del JSON (ficticia) con sus sucursales y las actividades del SIN que factura.</summary>
internal sealed record TechCompany(string Name, string Code, IReadOnlyList<TechBranchSeed> Branches, IReadOnlyList<TechActivity> Activities);

internal sealed record TechBranchSeed(string Code, string Name, string City);

internal sealed record TechActivity(int Code, string Description);

/// <summary>Unidad de medida con su unidad del SIN (código y descripción del catálogo «unidades de medida»).</summary>
internal sealed record TechUnit(string Code, string Name, bool AllowsDecimals, string Description, int SinCode, string SinDescription, bool IsNew);

/// <summary>Categoría del árbol (<see cref="Parent"/> null = raíz).</summary>
internal sealed record TechCategory(string Code, string Name, string? Parent, int Level, bool IsLeaf);

/// <summary>Especificación técnica de una categoría (la heredan sus subcategorías).</summary>
internal sealed record TechSpec(string Category, int Order, string Code, string Name, string? Unit, SpecDataType DataType, bool IsMultiValued,
    bool IsFilterable, string? CompatibilityKey, bool IsRequired, IReadOnlyList<string> Options);

internal sealed record TechBrand(string Code, string Name, bool IsOwn);

/// <summary>Producto SIN con el que se homologa el producto (actividad económica y código del catálogo del SIN).</summary>
internal sealed record TechSinCode(string Activity, int Product);

/// <summary>Producto del catálogo con precio (IVA incluido), costo NETO de IVA (ver <see cref="TechSeedCatalog.NetCost"/>),
/// margen de lista del JSON, mínimos/máximos, popularidad (1 a 10), perfil técnico (serie o IMEI y garantía), ficha técnica
/// (código → valores; números en cultura invariante) e ilustración.</summary>
internal sealed record TechProduct(string Sku, string Name, string Category, string Brand, string Unit, decimal Cost, decimal Price, int Minimum,
    int Maximum, int Popularity, bool TracksSerials, SerialKind SerialKind, int WarrantyMonths, IReadOnlyDictionary<string, IReadOnlyList<string>> Specs,
    TechSinCode Sin, string Image, decimal ListMargin)
{
    public bool IsService => Unit == TechSeedCatalog.ServiceUnit;
}

internal sealed record TechSupplier(string Code, string Name, string TaxId, string Contact, string Phone, string Email, string City, int LeadTimeDays,
    IReadOnlyList<string> Categories, IReadOnlyList<string> Brands);

internal sealed record TechCustomerCategory(string Code, string Name, string Description, bool IsNew);

/// <summary>Cliente ficticio: persona con CI o empresa/institución con NIT, su sucursal habitual y su categoría.</summary>
internal sealed record TechCustomer(string Code, string Kind, string Name, string DocumentType, string DocumentNumber, string? Email, string? Phone,
    string City, string Branch, string Category)
{
    /// <summary>Factura con NIT (empresas e instituciones); las personas, con CI.</summary>
    public bool IsCompany => DocumentType == "NIT";
}

/// <summary>Armado de PC del JSON: sucursal, cliente, vigencia, estado y piezas por ranura.</summary>
internal sealed record TechBuild(string Number, string Branch, string Profile, string Name, string Customer, int ValidDays, string Status,
    bool MarkedIncompatible, bool Compatible, IReadOnlyList<TechBuildLine> Lines);

internal sealed record TechBuildLine(string Slot, string Sku, int Quantity);

/// <summary>Producto del catálogo del SIN de las actividades de la tienda (CSV de la investigación).</summary>
internal sealed record TechSinProductRow(int Code, string Description, string Activity, string ActivityDescription);

/// <summary>
/// V4.2 · Catálogo del rubro Tecnología (JSON + CSV del SIN + imágenes embebidos), validado al cargar. Una sola instancia por
/// proceso (<see cref="Current"/>): se lee una vez y la comparten los datos de prueba, la demostración y el simulador.
/// </summary>
internal sealed class TechSeedCatalog
{
    public const string ServiceUnit = "SERV";
    private const string ResourcePrefix = "MINV.Infrastructure.Seeding.Tecnologia.";
    public const string ImagePrefix = ResourcePrefix + "Imagenes.";
    private const string JsonResource = ResourcePrefix + "catalogo-tecnologia.json";
    private const string CsvResource = ResourcePrefix + "catalogo-productos-sin-tecnologia.csv";

    private static readonly Lazy<TechSeedCatalog> Loaded = new(() => Load(ReadResource(JsonResource), ReadResource(CsvResource)));

    private readonly Dictionary<string, TechCategory> _categories;
    private readonly Dictionary<string, TechProduct> _products;

    private TechSeedCatalog(TechCompany company, decimal vatRate, bool pricesIncludeVat, IReadOnlyList<TechUnit> units, IReadOnlyList<TechCategory> categories,
        IReadOnlyList<TechSpec> specs, IReadOnlyList<TechBrand> brands, IReadOnlyList<TechProduct> products, IReadOnlyList<TechSupplier> suppliers,
        IReadOnlyList<TechCustomerCategory> customerCategories, IReadOnlyList<TechCustomer> customers, IReadOnlyList<TechBuild> builds,
        IReadOnlyList<TechSinProductRow> sinProducts)
    {
        Company = company;
        VatRate = vatRate;
        PricesIncludeVat = pricesIncludeVat;
        Units = units;
        Categories = categories;
        Specs = specs;
        Brands = brands;
        Products = products;
        Suppliers = suppliers;
        CustomerCategories = customerCategories;
        Customers = customers;
        Builds = builds;
        SinProducts = sinProducts;
        // Los repetidos no rompen la lectura: los informa la validación
        _categories = categories.GroupBy(c => c.Code, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        _products = products.GroupBy(p => p.Sku, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
    }

    /// <summary>El catálogo embebido (se carga y valida la primera vez).</summary>
    public static TechSeedCatalog Current => Loaded.Value;

    public TechCompany Company { get; }

    /// <summary>IVA del JSON (fracción: 0,13 = 13 %), el mismo de la empresa en Bolivia.</summary>
    public decimal VatRate { get; }

    /// <summary>El JSON trae «precio» y «costo» con el IVA incluido («precios_con_iva»).</summary>
    public bool PricesIncludeVat { get; }

    public IReadOnlyList<TechUnit> Units { get; }

    /// <summary>Categorías de la raíz a las hojas (una madre siempre antes que sus hijas).</summary>
    public IReadOnlyList<TechCategory> Categories { get; }

    public IReadOnlyList<TechSpec> Specs { get; }

    public IReadOnlyList<TechBrand> Brands { get; }

    public IReadOnlyList<TechProduct> Products { get; }

    public IReadOnlyList<TechSupplier> Suppliers { get; }

    public IReadOnlyList<TechCustomerCategory> CustomerCategories { get; }

    public IReadOnlyList<TechCustomer> Customers { get; }

    public IReadOnlyList<TechBuild> Builds { get; }

    public IReadOnlyList<TechSinProductRow> SinProducts { get; }

    public TechProduct Product(string sku) => _products[sku];

    public bool HasProduct(string sku) => _products.ContainsKey(sku);

    public TechCategory Category(string code) => _categories[code];

    /// <summary>La categoría y sus madres (de la categoría hacia la raíz).</summary>
    public IReadOnlyList<string> Lineage(string category)
    {
        var result = new List<string>();
        for (var code = category; code is not null && _categories.TryGetValue(code, out var c); code = c.Parent)
        {
            result.Add(code);
        }
        return result;
    }

    /// <summary>Categoría raíz de una categoría.</summary>
    public string Root(string category) => Lineage(category)[^1];

    /// <summary>Proveedor preferido de un producto: el que atiende su categoría (o una madre) y, entre varios, el que trabaja
    /// su marca; si ninguno la trabaja, el primero. Los servicios de la tienda no tienen proveedor (null).</summary>
    public TechSupplier? SupplierOf(TechProduct product)
    {
        var lineage = Lineage(product.Category);
        var candidates = Suppliers.Where(s => s.Categories.Any(lineage.Contains)).ToList();
        return candidates.FirstOrDefault(s => s.Brands.Contains(product.Brand, StringComparer.OrdinalIgnoreCase)) ?? candidates.FirstOrDefault();
    }

    /// <summary>Especificaciones que aplican a una categoría (propias y de sus madres).</summary>
    public IEnumerable<TechSpec> SpecsOf(string category)
    {
        var lineage = Lineage(category);
        return Specs.Where(s => lineage.Contains(s.Category));
    }

    /// <summary>Bytes de una ilustración embebida (Imagenes/&lt;tipo&gt;.png) o null si no existe.</summary>
    public static byte[]? Image(string kind)
    {
        using var stream = typeof(TechSeedCatalog).Assembly.GetManifestResourceStream(ImagePrefix + kind + ".png");
        if (stream is null)
        {
            return null;
        }
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    /// <summary>Filas del CSV de productos del SIN de la tienda (código, descripción, actividad) para el simulador.</summary>
    public static IReadOnlyList<SiatCatalogRow> SimulatorProducts() =>
        Current.SinProducts.Select(p => new SiatCatalogRow(p.Code.ToString(CultureInfo.InvariantCulture), p.Description, p.Activity)).ToList();

    /// <summary>
    /// Costo NETO de IVA de un costo del JSON. En el JSON, «costo» es el costo de importación con el IVA incluido (como
    /// «precio») y «margen_pct» = (precio − costo) / precio. En Bolivia el IVA está incluido en el precio y se calcula sobre el
    /// importe facturado (Ley 843; ver <see cref="Domain.Accounting.VatRules"/>): el crédito fiscal de la factura del
    /// proveedor es el 13 % de su importe y el costo contable es el resto, el 87 %. Por eso el costo neto es costo × 0,87 (NO
    /// costo / 1,13, que es un 1,69 % más alto y dejaba al registrar la factura una diferencia entre el mayor 1.1.05 y el
    /// valor del stock: −2.193,23 en la carga de 60 días). El catálogo mide el margen con el mismo criterio (precio × 0,87
    /// contra el costo neto), así que muestra exactamente el «margen_pct» del JSON; el costo de ventas y los reportes usan ese
    /// costo promedio. La factura del proveedor de una de esas compras es recepción / 0,87 (el costo con IVA del JSON; ver
    /// <c>FiscalRules.InvoiceForNetCost</c>): su crédito fiscal es exactamente el IVA que se suma a la deuda y el inventario no
    /// cambia. Redondeo: <see cref="JournalPoster.Money"/> (2 decimales, mitad hacia arriba), el mismo de los demás importes.
    /// </summary>
    internal static decimal NetCost(decimal costWithVat, decimal vatRate) => JournalPoster.Money(costWithVat * (1 - vatRate));

    // ================================================================================================= lectura y validación
    /// <summary>Lee y valida el catálogo (internal para las pruebas: un JSON alterado debe fallar con sus errores).</summary>
    internal static TechSeedCatalog Load(string json, string csv)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var errors = new List<string>();

        var meta = root.GetProperty("metadatos");
        // Precios y costos del JSON con IVA incluido: el costo se guarda NETO (NetCost) y el precio de venta, con IVA
        var vatRate = meta.GetProperty("iva").GetDecimal();
        var pricesIncludeVat = meta.GetProperty("precios_con_iva").GetBoolean();
        var companyJson = meta.GetProperty("empresa");
        var company = new TechCompany(Str(companyJson, "nombre"), Str(companyJson, "codigo"),
            companyJson.GetProperty("sucursales").EnumerateArray().Select(b => new TechBranchSeed(Str(b, "codigo"), Str(b, "nombre"), Str(b, "ciudad")))
                .ToList(),
            meta.GetProperty("actividades_sin").EnumerateArray().Select(a => new TechActivity(a.GetProperty("codigo").GetInt32(), Str(a, "descripcion")))
                .ToList());

        var units = root.GetProperty("unidades").EnumerateArray().Select(u => new TechUnit(Str(u, "codigo"), Str(u, "nombre"),
            u.GetProperty("decimales").GetBoolean(), Str(u, "descripcion"), u.GetProperty("codigo_sin").GetInt32(), Str(u, "descripcion_sin"),
            u.GetProperty("nueva").GetBoolean())).ToList();

        var categories = root.GetProperty("categorias").EnumerateArray().Select(c => new TechCategory(Str(c, "codigo"), Str(c, "nombre"),
            OptStr(c, "padre"), c.GetProperty("nivel").GetInt32(), c.GetProperty("hoja").GetBoolean())).OrderBy(c => c.Level).ToList();

        var specs = root.GetProperty("especificaciones").EnumerateArray().Select(s => new TechSpec(Str(s, "categoria"), s.GetProperty("orden").GetInt32(),
            Str(s, "codigo"), Str(s, "nombre"), OptStr(s, "unidad"), Str(s, "tipo") switch
            {
                "opcion" => SpecDataType.Option,
                "numero" => SpecDataType.Number,
                "texto" => SpecDataType.Text,
                var other => throw new InvalidDataException($"Tipo de especificación desconocido «{other}» en {Str(s, "codigo")}."),
            }, s.GetProperty("multivalor").GetBoolean(), s.GetProperty("filtrable").GetBoolean(), OptStr(s, "clave_compatibilidad"),
            s.GetProperty("obligatoria").GetBoolean(), s.GetProperty("opciones").EnumerateArray().Select(o => o.GetString()!).ToList())).ToList();

        var brands = root.GetProperty("marcas").EnumerateArray().Select(b => new TechBrand(Str(b, "codigo"), Str(b, "nombre"),
            b.GetProperty("propia").GetBoolean())).ToList();

        var products = root.GetProperty("productos").EnumerateArray().Select(p =>
        {
            var serialType = OptStr(p, "tipo_serie");
            var sin = p.GetProperty("producto_sin");
            var values = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (var spec in p.GetProperty("especificaciones").EnumerateObject())
            {
                values[spec.Name] = spec.Value.ValueKind == JsonValueKind.Array
                    ? spec.Value.EnumerateArray().Select(Scalar).ToList()
                    : [Scalar(spec.Value)];
            }
            // «costo» del JSON con IVA → costo NETO del producto y de las compras (ver NetCost)
            var cost = p.GetProperty("costo").GetDecimal();
            return new TechProduct(Str(p, "sku"), Str(p, "nombre"), Str(p, "categoria"), Str(p, "marca"), Str(p, "unidad"),
                pricesIncludeVat ? NetCost(cost, vatRate) : cost, p.GetProperty("precio").GetDecimal(), p.GetProperty("minimo").GetInt32(),
                p.GetProperty("maximo").GetInt32(), p.GetProperty("popularidad").GetInt32(), p.GetProperty("lleva_serie").GetBoolean(),
                serialType == "imei" ? SerialKind.Imei : SerialKind.Serial, p.GetProperty("garantia_meses").GetInt32(), values,
                new TechSinCode(sin.GetProperty("codigo_actividad").GetInt32().ToString(CultureInfo.InvariantCulture),
                    sin.GetProperty("codigo_producto").GetInt32()), Str(p, "imagen"), p.GetProperty("margen_pct").GetDecimal() / 100);
        }).ToList();

        var suppliers = root.GetProperty("proveedores").EnumerateArray().Select(s => new TechSupplier(Str(s, "codigo"), Str(s, "razon_social"),
            Str(s, "nit"), Str(s, "contacto"), Str(s, "telefono"), Str(s, "email"), Str(s, "ciudad"), s.GetProperty("dias_entrega").GetInt32(),
            s.GetProperty("categorias").EnumerateArray().Select(x => x.GetString()!).ToList(),
            s.GetProperty("marcas").EnumerateArray().Select(x => x.GetString()!).ToList())).ToList();

        var customerCategories = root.GetProperty("categorias_cliente").EnumerateArray().Select(c => new TechCustomerCategory(Str(c, "codigo"),
            Str(c, "nombre"), Str(c, "descripcion"), c.GetProperty("nueva").GetBoolean())).ToList();

        var customers = root.GetProperty("clientes").EnumerateArray().Select(c => new TechCustomer(Str(c, "codigo"), Str(c, "tipo"), Str(c, "nombre"),
            Str(c, "tipo_documento"), Str(c, "numero_documento"), OptStr(c, "email"), OptStr(c, "telefono"), Str(c, "ciudad"), Str(c, "sucursal"),
            Str(c, "categoria"))).ToList();

        var builds = root.GetProperty("armados").EnumerateArray().Select(b => new TechBuild(Str(b, "numero"), Str(b, "sucursal"), Str(b, "perfil"),
            Str(b, "nombre"), Str(b, "cliente"), b.GetProperty("vigencia_dias").GetInt32(), Str(b, "estado"), b.GetProperty("marcado_incompatible").GetBoolean(),
            b.GetProperty("compatibilidad").GetProperty("compatible").GetBoolean(),
            b.GetProperty("lineas").EnumerateArray().Select(l => new TechBuildLine(Str(l, "ranura"), Str(l, "sku"), l.GetProperty("cantidad").GetInt32()))
                .ToList())).ToList();

        var sinProducts = SiatSimulatorCatalogs.ParseCsv(csv).Where(r => r.Count >= 4 && r[0].Trim().Length > 0)
            .Select(r => new TechSinProductRow(int.Parse(r[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture), r[1].Trim(), r[2].Trim(), r[3].Trim()))
            .ToList();

        var catalog = new TechSeedCatalog(company, vatRate, pricesIncludeVat, units, categories, specs, brands, products, suppliers, customerCategories,
            customers, builds, sinProducts);
        catalog.Validate(errors);
        if (errors.Count > 0)
        {
            throw new InvalidDataException("El catálogo de tecnología embebido no es válido:" + Environment.NewLine + " · " +
                                           string.Join(Environment.NewLine + " · ", errors.Take(40)));
        }
        return catalog;
    }

    private void Validate(List<string> errors)
    {
        void Check(bool ok, string message)
        {
            if (!ok)
            {
                errors.Add(message);
            }
        }

        Check(Company.Branches.Select(b => b.Code).SequenceEqual(["CM", "CB", "SC"]), "La empresa debe tener las sucursales CM, CB y SC (en ese orden).");
        Check(PricesIncludeVat && VatRate == 0.13m,
            "El catálogo debe traer precio y costo con el IVA del 13 % de Bolivia incluido («precios_con_iva» e «iva»): el costo se guarda neto de IVA.");
        Check(Units.Any(u => u.Code == "UND") && Units.Any(u => u.Code == ServiceUnit), "Faltan las unidades UND y SERV.");
        foreach (var dup in Duplicates(Categories.Select(c => c.Code)))
        {
            errors.Add($"Categoría repetida: {dup}.");
        }
        foreach (var c in Categories)
        {
            Check(c.Parent is null || _categories.ContainsKey(c.Parent), $"La categoría {c.Code} tiene una madre inexistente ({c.Parent}).");
            Check(c.Parent is null || _categories[c.Parent].Level == c.Level - 1, $"El nivel de la categoría {c.Code} no sigue al de su madre.");
            Check(c.Code.Length <= 20 && c.Name.Length <= 80, $"El código o el nombre de la categoría {c.Code} es demasiado largo.");
        }

        // Especificaciones: códigos válidos, sin repetirse entre una categoría y sus madres o hijas, opciones y claves correctas
        foreach (var s in Specs)
        {
            Check(_categories.ContainsKey(s.Category), $"La especificación {s.Code} es de una categoría inexistente ({s.Category}).");
            Check(s.Code.Length is > 0 and <= 40 && s.Code.All(ch => char.IsAsciiLetterLower(ch) || char.IsAsciiDigit(ch) || ch == '_'),
                $"Código de especificación inválido: «{s.Code}».");
            Check(s.DataType == SpecDataType.Option ? s.Options.Count > 0 : s.Options.Count == 0,
                $"{s.Category}/{s.Code}: solo las especificaciones de tipo opción llevan opciones.");
            Check(s.Options.Distinct(StringComparer.OrdinalIgnoreCase).Count() == s.Options.Count, $"{s.Category}/{s.Code}: opciones repetidas.");
            Check(s.CompatibilityKey is null || CompatibilityKeys.All.Contains(s.CompatibilityKey),
                $"{s.Category}/{s.Code}: la clave de compatibilidad «{s.CompatibilityKey}» no es una constante de CompatibilityKeys (regla T-01).");
        }
        foreach (var s in Specs)
        {
            var clash = Specs.FirstOrDefault(o => !ReferenceEquals(o, s) && o.Code == s.Code
                                                  && (Lineage(s.Category).Contains(o.Category) || Lineage(o.Category).Contains(s.Category)));
            Check(clash is null, $"La especificación «{s.Code}» se repite entre {s.Category} y {clash?.Category} (se hereda entre madres e hijas).");
        }

        // Productos: categoría hoja, unidad, marca, proveedor, precio, ficha técnica completa y homologación con el SIN
        foreach (var dup in Duplicates(Products.Select(p => p.Sku)))
        {
            errors.Add($"SKU repetido: {dup}.");
        }
        var sin = SinProducts.Select(p => (p.Activity, p.Code)).ToHashSet();
        var activities = Company.Activities.Select(a => a.Code.ToString(CultureInfo.InvariantCulture)).ToHashSet(StringComparer.Ordinal);
        foreach (var p in Products)
        {
            var where = $"Producto {p.Sku}";
            Check(p.Sku.Length <= 40 && p.Name.Length is >= 2 and <= 150, $"{where}: SKU o nombre fuera de largo.");
            Check(_categories.TryGetValue(p.Category, out var category) && category.IsLeaf, $"{where}: la categoría {p.Category} no existe o no es hoja.");
            Check(Units.Any(u => u.Code == p.Unit), $"{where}: unidad {p.Unit} desconocida.");
            Check(Brands.Any(b => b.Name == p.Brand), $"{where}: marca {p.Brand} desconocida.");
            // Costo NETO (NetCost) contra el precio neto (el 87 % del precio: el 13 % es el débito fiscal): el margen que mostrará
            // el catálogo es el «margen_pct» del JSON
            var netPrice = p.Price * (1 - VatRate);
            Check(p.Cost > 0 && netPrice > p.Cost, $"{where}: el precio sin IVA ({netPrice:N2}) debe ser mayor que el costo neto ({p.Cost}).");
            Check(netPrice <= 0 || Math.Abs((netPrice - p.Cost) / netPrice - p.ListMargin) <= 0.001m,
                $"{where}: el margen sobre el precio sin IVA ({(netPrice - p.Cost) / netPrice:P1}) no coincide con «margen_pct» ({p.ListMargin:P1}).");
            Check(p.Minimum >= 0 && (p.Maximum == 0 || p.Maximum >= p.Minimum), $"{where}: mínimo {p.Minimum} y máximo {p.Maximum} incoherentes.");
            Check(p.Popularity is >= 1 and <= 10, $"{where}: popularidad fuera de 1 a 10.");
            Check(!p.TracksSerials || !p.IsService, $"{where}: un servicio no lleva serie.");
            Check(p.WarrantyMonths is >= 0 and <= 120, $"{where}: garantía fuera de 0 a 120 meses.");
            Check(Image(p.Image) is not null, $"{where}: falta la ilustración Imagenes/{p.Image}.png.");
            Check(activities.Contains(p.Sin.Activity) && sin.Contains((p.Sin.Activity, p.Sin.Product)),
                $"{where}: el producto SIN {p.Sin.Product} no está en la actividad {p.Sin.Activity} del CSV.");
            Check(p.IsService || (_categories.ContainsKey(p.Category) && Suppliers.Any(s => s.Categories.Any(Lineage(p.Category).Contains))),
                $"{where}: ningún proveedor atiende la categoría {p.Category} (solo los servicios de la tienda no tienen proveedor).");
            if (!_categories.ContainsKey(p.Category))
            {
                continue;
            }
            var applicable = SpecsOf(p.Category).ToDictionary(s => s.Code, StringComparer.Ordinal);
            foreach (var (code, values) in p.Specs)
            {
                if (!applicable.TryGetValue(code, out var spec))
                {
                    errors.Add($"{where}: la especificación «{code}» no es de su categoría.");
                    continue;
                }
                Check(values.Count > 0 && (values.Count == 1 || spec.IsMultiValued), $"{where}: «{code}» admite un solo valor.");
                foreach (var value in values)
                {
                    Check(spec.DataType switch
                    {
                        SpecDataType.Number => decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _),
                        SpecDataType.Option => spec.Options.Contains(value, StringComparer.OrdinalIgnoreCase),
                        _ => value.Trim().Length > 0,
                    }, $"{where}: «{value}» no es un valor válido de «{code}».");
                }
            }
            foreach (var required in applicable.Values.Where(s => s.IsRequired && !p.Specs.ContainsKey(s.Code)))
            {
                errors.Add($"{where}: falta la especificación obligatoria «{required.Code}».");
            }
        }

        // Proveedores, clientes y armados
        foreach (var s in Suppliers)
        {
            Check(s.Categories.All(_categories.ContainsKey), $"El proveedor {s.Code} atiende una categoría inexistente.");
            Check(s.TaxId.All(char.IsAsciiDigit) && s.Email.EndsWith(".example", StringComparison.Ordinal),
                $"El proveedor {s.Code} debe tener NIT numérico y correo ficticio (.example).");
        }
        foreach (var c in Customers)
        {
            var where = $"Cliente {c.Code}";
            Check(CustomerCategories.Any(k => k.Code == c.Category), $"{where}: categoría {c.Category} desconocida.");
            Check(Company.Branches.Any(b => b.Code == c.Branch), $"{where}: sucursal {c.Branch} desconocida.");
            Check(c.DocumentType is "CI" or "NIT" && c.DocumentNumber.All(char.IsAsciiDigit), $"{where}: documento {c.DocumentType} {c.DocumentNumber} inválido.");
            Check(c.Email is null || c.Email.EndsWith(".example", StringComparison.Ordinal), $"{where}: el correo debe ser ficticio (.example).");
        }
        foreach (var dup in Duplicates(Customers.Select(c => c.DocumentNumber)))
        {
            errors.Add($"Documento de cliente repetido: {dup}.");
        }
        foreach (var b in Builds)
        {
            var where = $"Armado {b.Number}";
            Check(Company.Branches.Any(x => x.Code == b.Branch) && b.Number.StartsWith("ARM-" + b.Branch + "-", StringComparison.Ordinal),
                $"{where}: sucursal {b.Branch} desconocida o número de otra sucursal.");
            Check(Customers.Any(c => c.Code == b.Customer), $"{where}: cliente {b.Customer} desconocido.");
            Check(b.Lines.Count > 0 && b.Lines.All(l => _products.ContainsKey(l.Sku) && l.Quantity > 0), $"{where}: pieza desconocida o sin cantidad.");
            Check(b.Lines.All(l => SlotOf(l) is not null), $"{where}: ranura desconocida.");
            Check(b.Compatible != b.MarkedIncompatible, $"{where}: la marca de incompatible no coincide con la compatibilidad calculada.");
        }
    }

    /// <summary>Ranura del armador de una pieza del JSON (las «extra» por la categoría raíz del producto).</summary>
    public PcSlot? SlotOf(TechBuildLine line) => line.Slot switch
    {
        "cpu" => PcSlot.Cpu,
        "placa" => PcSlot.Motherboard,
        "ram" => PcSlot.Ram,
        "gpu" => PcSlot.Gpu,
        "almacenamiento" => PcSlot.Storage,
        "fuente" => PcSlot.Psu,
        "gabinete" => PcSlot.Case,
        "refrigeracion" => PcSlot.Cooler,
        "extra" when _products.TryGetValue(line.Sku, out var p) => Lineage(p.Category) switch
        {
            var l when l.Contains("SRV") => PcSlot.Service,
            var l when l.Contains("SOFT") => PcSlot.Software,
            var l when l.Contains("MON") => PcSlot.Monitor,
            _ => PcSlot.Peripheral,
        },
        _ => null,
    };

    // ================================================================================================= utilidades
    private static IEnumerable<string> Duplicates(IEnumerable<string> values) =>
        values.GroupBy(v => v, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key);

    private static string Str(JsonElement e, string name) =>
        e.GetProperty(name).GetString() ?? throw new InvalidDataException($"Falta «{name}» en el catálogo de tecnología.");

    private static string? OptStr(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Valor de una especificación como texto: números en cultura invariante (punto decimal).</summary>
    private static string Scalar(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Number => e.GetDecimal().ToString(CultureInfo.InvariantCulture),
        JsonValueKind.String => e.GetString()!,
        JsonValueKind.True => "Sí",
        JsonValueKind.False => "No",
        _ => throw new InvalidDataException($"Valor de especificación no admitido: {e.GetRawText()}."),
    };

    private static string ReadResource(string name)
    {
        using var stream = typeof(TechSeedCatalog).Assembly.GetManifestResourceStream(name)
                           ?? throw new InvalidOperationException($"Falta el recurso embebido {name}.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
