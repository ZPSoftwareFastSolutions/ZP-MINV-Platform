using MINV.Application.Catalog;
using MINV.Application.Inventory.Movements;
using MINV.Application.Inventory.Queries;
using MINV.Application.Tech;
using MINV.Application.Billing;
using MINV.Domain.Billing;
using MINV.DesktopClient.ViewModels;
using MINV.Domain.Catalog;
using MINV.Domain.Inventory;

namespace MINV.DesktopClient.Tests;

/// <summary>Producto de prueba de la edición Tecnología con sus series en stock.</summary>
internal sealed record TechSampleProduct(string Sku, string Name, bool Serialized, SerialKind Kind, IReadOnlyList<string> Serials);

/// <summary>
/// Catálogo técnico MÍNIMO y controlado sobre la demostración de Tech Zone Gaming, creado con los casos de uso reales como
/// lo haría un administrador: categoría «Tecnología de prueba» con subcategorías, especificaciones con las claves de
/// compatibilidad del armador, productos con ficha, garantía y control por serie o IMEI, su saldo inicial con series
/// conocidas y la homologación del SIN (la demostración factura). Así las pruebas de flujo conocen cada serie, precio y
/// compatibilidad sin depender de los datos generados de la demostración.
/// </summary>
internal static class TechSeed
{
    public const string Root = "TZT";

    public static readonly TechSampleProduct Cpu = new("TZ-CPU-R5", "Procesador Ryzen 5 7600 (prueba)", true, SerialKind.Serial, ["R57600A001", "R57600A002", "R57600A003"]);
    public static readonly TechSampleProduct CpuIntel = new("TZ-CPU-I5", "Procesador Core i5 14400F (prueba)", false, SerialKind.Serial, []);
    public static readonly TechSampleProduct Board = new("TZ-MB-B650", "Placa madre B650 ATX (prueba)", true, SerialKind.Serial, ["B650MB0001", "B650MB0002"]);
    public static readonly TechSampleProduct Ram = new("TZ-RAM-32", "Memoria DDR5 32 GB 2x16 (prueba)", false, SerialKind.Serial, []);
    public static readonly TechSampleProduct Ssd = new("TZ-SSD-1T", "SSD NVMe 1 TB (prueba)", true, SerialKind.Serial, ["SSD1T00001", "SSD1T00002", "SSD1T00003"]);
    public static readonly TechSampleProduct Psu = new("TZ-PSU-750", "Fuente 750 W 80 Plus Gold (prueba)", false, SerialKind.Serial, []);
    public static readonly TechSampleProduct Case = new("TZ-CASE-ATX", "Gabinete ATX con vidrio (prueba)", false, SerialKind.Serial, []);
    public static readonly TechSampleProduct Gpu = new("TZ-GPU-4060", "Tarjeta de video RTX 4060 8 GB (prueba)", true, SerialKind.Serial, ["GPU4060A01", "GPU4060A02"]);
    public static readonly TechSampleProduct Phone = new("TZ-CEL-A15", "Smartphone Galaxy A15 128 GB (prueba)", true, SerialKind.Imei,
        [Imei("35209900176148"), Imei("35209900176149"), Imei("35209900176150")]);
    public static readonly TechSampleProduct Console = new("TZ-PS5", "Consola PlayStation 5 Slim (prueba)", true, SerialKind.Serial, ["PS5SLIM001", "PS5SLIM002"]);

    /// <summary>IMEI válido: 14 dígitos + dígito de Luhn.</summary>
    public static string Imei(string first14) => first14 + MINV.Domain.Inventory.Imei.CheckDigit(first14);

    public static async Task CreateAsync(AppServices app)
    {
        var options = await app.SendAsync(new GetCatalogOptionsQuery());
        var unit = options.Units.FirstOrDefault(u => u.Code == "UND")?.Code ?? options.Units[0].Code;
        var bin = (await app.SendAsync(new GetBinsQuery()))[0].Code;

        await app.SendAsync(new SaveCategoryCommand(Root, "Tecnología de prueba"));
        await Sub("TZCPU", "Procesadores (prueba)");
        await Sub("TZMB", "Placas madre (prueba)");
        await Sub("TZRAM", "Memorias RAM (prueba)");
        await Sub("TZSTO", "Almacenamiento (prueba)");
        await Sub("TZPSU", "Fuentes de poder (prueba)");
        await Sub("TZCAS", "Gabinetes (prueba)");
        await Sub("TZGPU", "Tarjetas de video (prueba)");
        await Sub("TZCEL", "Celulares (prueba)");
        await Sub("TZCON", "Consolas (prueba)");

        // Especificaciones: la «condición» se hereda de la raíz; las demás, con la clave de compatibilidad del armador
        await Spec(Root, TechSpecCodes.Condition, "Condición", null, SpecDataType.Option, false, true, null, 5, ["Nuevo", "Reacondicionado"]);
        await Spec("TZCPU", "socket", "Socket", null, SpecDataType.Option, false, true, CompatibilityKeys.CpuSocket, 10, ["AM5", "LGA1700"]);
        await Spec("TZCPU", "tdp", "TDP", "W", SpecDataType.Number, false, true, CompatibilityKeys.CpuTdpW, 20, []);
        await Spec("TZCPU", "graficos", "Gráficos integrados", null, SpecDataType.Option, false, true, CompatibilityKeys.IntegratedGraphics, 30, ["Sí", "No"]);
        await Spec("TZMB", "socket", "Socket", null, SpecDataType.Option, false, true, CompatibilityKeys.CpuSocket, 10, ["AM5", "LGA1700"]);
        await Spec("TZMB", "tipo_ram", "Tipo de RAM", null, SpecDataType.Option, false, true, CompatibilityKeys.RamType, 20, ["DDR4", "DDR5"]);
        await Spec("TZMB", "ranuras_ram", "Ranuras de RAM", null, SpecDataType.Number, false, false, CompatibilityKeys.RamSlots, 30, []);
        await Spec("TZMB", "ram_max", "RAM máxima", "GB", SpecDataType.Number, false, false, CompatibilityKeys.RamMaxGb, 40, []);
        await Spec("TZMB", "formato", "Formato", null, SpecDataType.Option, false, true, CompatibilityKeys.FormFactor, 50, ["ATX", "Micro-ATX"]);
        await Spec("TZMB", "ranuras_m2", "Ranuras M.2", null, SpecDataType.Number, false, false, CompatibilityKeys.M2Slots, 60, []);
        await Spec("TZRAM", "tipo_ram", "Tipo de RAM", null, SpecDataType.Option, false, true, CompatibilityKeys.RamType, 10, ["DDR4", "DDR5"]);
        await Spec("TZRAM", "modulos", "Módulos", null, SpecDataType.Number, false, false, CompatibilityKeys.RamModules, 20, []);
        await Spec("TZRAM", "capacidad", "Capacidad", "GB", SpecDataType.Number, false, true, CompatibilityKeys.RamCapacityGb, 30, []);
        await Spec("TZSTO", "interfaz", "Interfaz", null, SpecDataType.Option, false, true, CompatibilityKeys.StorageInterface, 10, ["NVMe", "SATA"]);
        await Spec("TZPSU", "potencia", "Potencia", "W", SpecDataType.Number, false, true, CompatibilityKeys.PsuWatts, 10, []);
        await Spec("TZCAS", "formatos", "Formatos de placa", null, SpecDataType.Option, true, true, CompatibilityKeys.CaseFormFactors, 10, ["ATX", "Micro-ATX", "Mini-ITX"]);
        await Spec("TZCAS", "gpu_max", "Largo máximo de GPU", "mm", SpecDataType.Number, false, false, CompatibilityKeys.CaseMaxGpuMm, 20, []);
        await Spec("TZGPU", "largo", "Largo", "mm", SpecDataType.Number, false, false, CompatibilityKeys.GpuLengthMm, 10, []);
        await Spec("TZGPU", "consumo", "Consumo", "W", SpecDataType.Number, false, false, CompatibilityKeys.PowerDrawW, 20, []);
        await Spec("TZCEL", "almacenamiento", "Almacenamiento", "GB", SpecDataType.Number, false, true, null, 10, []);
        await Spec("TZCON", TechSpecCodes.Platform, "Plataforma", null, SpecDataType.Option, false, true, null, 10, ["PS5", "Xbox Series X", "Nintendo Switch 2"]);

        await Product(Cpu, "TZCPU", 1450m, 1890m, 36, [("socket", "AM5"), ("tdp", "65"), ("graficos", "Sí")]);
        await Product(CpuIntel, "TZCPU", 1500m, 1990m, 36, [("socket", "LGA1700"), ("tdp", "65"), ("graficos", "No")], stock: 2);
        await Product(Board, "TZMB", 1300m, 1690m, 36, [("socket", "AM5"), ("tipo_ram", "DDR5"), ("ranuras_ram", "4"), ("ram_max", "128"), ("formato", "ATX"), ("ranuras_m2", "2")]);
        await Product(Ram, "TZRAM", 700m, 890m, 12, [("tipo_ram", "DDR5"), ("modulos", "2"), ("capacidad", "32")], stock: 6);
        await Product(Ssd, "TZSTO", 520m, 690m, 36, [("interfaz", "NVMe")]);
        await Product(Psu, "TZPSU", 650m, 850m, 60, [("potencia", "750")], stock: 4);
        await Product(Case, "TZCAS", 480m, 650m, 12, [("formatos", "ATX"), ("formatos", "Micro-ATX"), ("gpu_max", "360")], stock: 4);
        await Product(Gpu, "TZGPU", 2600m, 3390m, 24, [("largo", "240"), ("consumo", "115")]);
        await Product(Phone, "TZCEL", 1100m, 1450m, 12, [("almacenamiento", "128")]);
        await Product(Console, "TZCON", 3900m, 4999m, 12, [(TechSpecCodes.Platform, "PS5")]);

        // La demostración factura: los productos nuevos se homologan con el SIN como lo haría el administrador
        await HomologateAsync(app);
        app.Data.Invalidate();
        await app.Data.LookupAsync(force: true);
        app.Images.Invalidate();

        static async Task HomologateAsync(AppServices app)
        {
            // Productos pendientes: la sugerencia del catálogo del SIN o, si no hay, el primer producto de la actividad principal
            var view = await app.SendAsync(new GetHomologationQuery());
            var activity = view.Activities.FirstOrDefault(a => a.IsCurrent && a.Sectors.Contains(SiatCodes.SectorPurchaseSale))
                           ?? view.Activities.First(a => a.IsCurrent);
            var suggestions = (await app.SendAsync(new SuggestProductHomologationQuery(activity.Code))).ToDictionary(s => s.Sku, StringComparer.OrdinalIgnoreCase);
            var fallback = (await app.SendAsync(new SearchSiatProductsQuery(activity.Code, null, 1))).First();
            var items = view.Products.Where(p => p.IsActive && p.SinProductCode is null)
                .Select(p => suggestions.GetValueOrDefault(p.Sku) ?? new ProductHomologationInput(p.Sku, fallback.ActivityCode, fallback.ProductCode)).ToList();
            if (items.Count > 0)
            {
                await app.SendAsync(new SaveProductHomologationCommand(items));
            }
        }

        async Task Sub(string code, string name) => await app.SendAsync(new SaveCategoryCommand(code, name, Root));

        async Task Spec(string category, string code, string name, string? specUnit, SpecDataType type, bool multi, bool filterable, string? key, int order,
            IReadOnlyList<string> values) =>
            await app.SendAsync(new SaveSpecDefinitionCommand(category, code, name, specUnit, type, multi, filterable, false, key, order, values));

        async Task Product(TechSampleProduct p, string category, decimal cost, decimal price, int warranty, (string Code, string Value)[] specs, int stock = 0)
        {
            await app.SendAsync(new SaveProductCommand(null, p.Sku, p.Name, null, category, unit, null, 1, 10, cost, price, null, true, bin));
            var inputs = specs.GroupBy(s => s.Code).Select(g => new ProductSpecInput(g.Key, g.Select(x => x.Value).ToList()))
                .Append(new ProductSpecInput(TechSpecCodes.Condition, ["Nuevo"])).ToList();
            await app.SendAsync(new SaveProductTechCommand(p.Sku, p.Serialized, p.Kind, warranty, inputs));
            var quantity = p.Serialized ? p.Serials.Count : stock;
            if (quantity > 0)
            {
                await app.SendAsync(new RegisterMovementCommand(p.Sku, bin, MovementTypeCodes.InitialBalance, quantity, null, "INV-INICIAL", "Saldo inicial de prueba",
                    Serials: p.Serialized ? p.Serials : null));
            }
        }
    }
}
