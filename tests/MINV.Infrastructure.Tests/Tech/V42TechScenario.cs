using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MINV.Application.Abstractions;
using MINV.Application.Billing;
using MINV.Application.Catalog;
using MINV.Application.Partners;
using MINV.Application.Purchasing;
using MINV.Application.Tech;
using MINV.Domain.Billing;
using MINV.Domain.Catalog;
using MINV.Domain.Inventory;
using MINV.Infrastructure.Persistence;
using MINV.Infrastructure.Tests.Billing;

namespace MINV.Infrastructure.Tests.Tech;

/// <summary>
/// V4.2 · Tienda de tecnología mínima sobre la empresa que factura del agente E (<see cref="E_BillingTestHost"/>): categorías
/// con subcategorías, especificaciones con claves de compatibilidad (herencia de «condición» desde Componentes), productos
/// serializados (serie o IMEI) con su ficha técnica, homologación con el SIN y un proveedor. TODO con los casos de uso
/// (regla A-13); las existencias entran con recepciones de compra con series.
/// </summary>
internal sealed class V42TechScenario
{
    public const string Supplier = "P001";

    private V42TechScenario(E_BillingTestHost host) => Host = host;

    public E_BillingTestHost Host { get; }

    /// <summary>Productos: SKU, nombre, categoría, costo, precio, tipo de serie, garantía (meses) y ficha técnica.</summary>
    public static readonly (string Sku, string Name, string Category, decimal Cost, decimal Price, SerialKind Kind, int Warranty,
        (string Code, string[] Values)[] Specs)[] Products =
    [
        ("CPU-7600", "Procesador AMD Ryzen 5 7600", "CPU", 1702m, 2049m, SerialKind.Serial, 36,
            [("condicion", ["Nuevo"]), ("socket", ["AM5"]), ("tdp", ["65"]), ("graficos_integrados", ["Sí"])]),
        ("CPU-14400F", "Procesador Intel Core i5-14400F", "CPU", 1150m, 1399m, SerialKind.Serial, 36,
            [("condicion", ["Nuevo"]), ("socket", ["LGA1700"]), ("tdp", ["65"]), ("graficos_integrados", ["No"])]),
        ("MB-B650", "Placa madre B650 ATX DDR5", "MB", 1300m, 1599m, SerialKind.Serial, 36,
            [("condicion", ["Nuevo"]), ("socket", ["AM5"]), ("tipo_ram", ["DDR5"]), ("formato", ["ATX"]), ("ranuras_ram", ["4"]), ("ram_max", ["192"]),
                ("ranuras_m2", ["2"])]),
        ("RAM-D5-32", "Memoria DDR5 32 GB (2x16) 6000 MHz", "RAM", 700m, 899m, SerialKind.Serial, 60,
            [("condicion", ["Nuevo"]), ("tipo_ram", ["DDR5"]), ("capacidad_total", ["32"]), ("modulos", ["2"])]),
        ("RAM-D4-16", "Memoria DDR4 16 GB (2x8) 3200 MHz", "RAM", 350m, 449m, SerialKind.Serial, 60,
            [("condicion", ["Nuevo"]), ("tipo_ram", ["DDR4"]), ("capacidad_total", ["16"]), ("modulos", ["2"])]),
        ("GPU-4060", "Tarjeta de video RTX 4060 8 GB", "GPU", 2700m, 3199m, SerialKind.Serial, 36,
            [("condicion", ["Nuevo"]), ("vram", ["8"]), ("largo", ["240"]), ("consumo", ["115"])]),
        ("GPU-4090", "Tarjeta de video RTX 4090 24 GB", "GPU", 13500m, 15999m, SerialKind.Serial, 36,
            [("condicion", ["Nuevo"]), ("vram", ["24"]), ("largo", ["357"]), ("consumo", ["450"])]),
        ("SSD-1TB", "SSD NVMe 1 TB PCIe 4.0", "STO", 700m, 879m, SerialKind.Serial, 60,
            [("condicion", ["Nuevo"]), ("interfaz", ["NVMe PCIe 4.0"])]),
        ("PSU-650", "Fuente de poder 650 W 80 PLUS Bronze", "PSU", 500m, 649m, SerialKind.Serial, 60,
            [("condicion", ["Nuevo"]), ("potencia", ["650"])]),
        ("CASE-ATX", "Gabinete ATX con 3 ventiladores", "CASE", 600m, 749m, SerialKind.Serial, 12,
            [("condicion", ["Nuevo"]), ("formatos_placa", ["ATX", "Micro-ATX"]), ("largo_max_gpu", ["330"])]),
        ("PS5-SLIM", "Consola PlayStation 5 Slim", "CON", 4200m, 4999m, SerialKind.Serial, 12, [("plataforma", ["PS5"])]),
        ("TAB-LTE", "Tableta 10 pulgadas 4G LTE", "MOV", 2400m, 2999m, SerialKind.Imei, 12, []),
    ];

    /// <summary>IMEI válidos (dígito de Luhn) para la tableta.</summary>
    public static string ImeiFor(int n)
    {
        var first = "35209900" + n.ToString("000000", CultureInfo.InvariantCulture);
        return first + Imei.CheckDigit(first).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Series de prueba: «GPU-4060-S0001»…</summary>
    public static string[] Serials(string sku, int from, int count) =>
        Enumerable.Range(from, count).Select(i => $"{sku}-S{i:0000}").ToArray();

    public static async Task<V42TechScenario> CreateAsync(E_BillingTestHost host)
    {
        using var admin = await host.AdminAsync();
        Assert.Equal(Supplier, await admin.Send(new SaveSupplierCommand(null, "Mayorista Tecnológico S.R.L.", "1023456027", 3, "Ventas", null,
            "ventas@mayorista-tec.example", true)));
        await admin.Send(new SaveCategoryCommand("COMP", "Componentes"));
        foreach (var (code, name) in new[]
                 {
                     ("CPU", "Procesadores"), ("MB", "Placas madre"), ("RAM", "Memorias RAM"), ("GPU", "Tarjetas de video"), ("STO", "Almacenamiento"),
                     ("PSU", "Fuentes de poder"), ("CASE", "Gabinetes"),
                 })
        {
            await admin.Send(new SaveCategoryCommand(code, name, "COMP"));
        }
        await admin.Send(new SaveCategoryCommand("CON", "Consolas"));
        await admin.Send(new SaveCategoryCommand("MOV", "Móviles"));

        async Task Spec(string category, string code, string name, string? unit, SpecDataType type, string? key, string[] options, bool multi = false,
            bool required = false, int order = 10) =>
            await admin.Send(new SaveSpecDefinitionCommand(category, code, name, unit, type, multi, true, required, key, order, options));
        await Spec("COMP", "condicion", "Condición", null, SpecDataType.Option, null, ["Nuevo", "Reacondicionado", "Usado"], required: true, order: 1);
        await Spec("CPU", "socket", "Socket", null, SpecDataType.Option, CompatibilityKeys.CpuSocket, ["AM5", "LGA1700"]);
        await Spec("CPU", "tdp", "TDP", "W", SpecDataType.Number, CompatibilityKeys.CpuTdpW, []);
        await Spec("CPU", "graficos_integrados", "Gráficos integrados", null, SpecDataType.Option, CompatibilityKeys.IntegratedGraphics, ["Sí", "No"]);
        await Spec("MB", "socket", "Socket", null, SpecDataType.Option, CompatibilityKeys.CpuSocket, ["AM5", "LGA1700"]);
        await Spec("MB", "tipo_ram", "Tipo de RAM", null, SpecDataType.Option, CompatibilityKeys.RamType, ["DDR4", "DDR5"]);
        await Spec("MB", "formato", "Formato", null, SpecDataType.Option, CompatibilityKeys.FormFactor, ["ATX", "Micro-ATX", "Mini-ITX"]);
        await Spec("MB", "ranuras_ram", "Ranuras de RAM", null, SpecDataType.Number, CompatibilityKeys.RamSlots, []);
        await Spec("MB", "ram_max", "RAM máxima", "GB", SpecDataType.Number, CompatibilityKeys.RamMaxGb, []);
        await Spec("MB", "ranuras_m2", "Ranuras M.2", null, SpecDataType.Number, CompatibilityKeys.M2Slots, []);
        await Spec("RAM", "tipo_ram", "Tipo de RAM", null, SpecDataType.Option, CompatibilityKeys.RamType, ["DDR4", "DDR5"]);
        await Spec("RAM", "capacidad_total", "Capacidad total", "GB", SpecDataType.Number, CompatibilityKeys.RamCapacityGb, []);
        await Spec("RAM", "modulos", "Módulos", null, SpecDataType.Number, CompatibilityKeys.RamModules, []);
        await Spec("GPU", "vram", "Memoria de video", "GB", SpecDataType.Number, null, [], order: 1);
        await Spec("GPU", "largo", "Largo", "mm", SpecDataType.Number, CompatibilityKeys.GpuLengthMm, []);
        await Spec("GPU", "consumo", "Consumo", "W", SpecDataType.Number, CompatibilityKeys.PowerDrawW, []);
        await Spec("STO", "interfaz", "Interfaz", null, SpecDataType.Option, CompatibilityKeys.StorageInterface, ["NVMe PCIe 4.0", "SATA III"]);
        await Spec("PSU", "potencia", "Potencia", "W", SpecDataType.Number, CompatibilityKeys.PsuWatts, []);
        await Spec("CASE", "formatos_placa", "Formatos de placa", null, SpecDataType.Option, CompatibilityKeys.CaseFormFactors,
            ["ATX", "Micro-ATX", "Mini-ITX"], multi: true);
        await Spec("CASE", "largo_max_gpu", "Largo máximo de GPU", "mm", SpecDataType.Number, CompatibilityKeys.CaseMaxGpuMm, []);
        await Spec("CON", TechSpecCodes.Platform, "Plataforma", null, SpecDataType.Option, null, ["PS5", "Xbox Series X", "Nintendo Switch 2"]);

        foreach (var p in Products)
        {
            await admin.Send(new SaveProductCommand(null, p.Sku, p.Name, null, p.Category, "UND", Supplier, 1, 50, p.Cost, p.Price, null, true,
                E_BillingTestHost.Bin));
            await admin.Send(new SaveProductTechCommand(p.Sku, true, p.Kind, p.Warranty,
                p.Specs.Select(s => new ProductSpecInput(s.Code, s.Values)).ToList()));
        }

        // Homologación con el SIN de los productos nuevos (como la pantalla Facturación › Homologación)
        await host.DbAsync(async db =>
        {
            var sinProduct = await db.Set<SiatProduct>().Where(p => p.ActivityCode == E_BillingTestHost.Activity).OrderBy(p => p.ProductCode).FirstAsync();
            var homologated = await db.Set<ProductSiatCode>().Select(p => p.ProductId).ToListAsync();
            foreach (var product in await db.Set<Product>().Where(p => !homologated.Contains(p.Id)).ToListAsync())
            {
                db.Add(new ProductSiatCode(host.TenantId, product.Id, E_BillingTestHost.Activity, sinProduct.ProductCode));
            }
            return await db.SaveChangesAsync();
        });
        return new V42TechScenario(host);
    }

    /// <summary>Compra aprobada y recibida con las series de cada producto (una recepción de compra, regla T-02).</summary>
    public async Task<ReceiptResult> ReceiveAsync(params (string Sku, string[] Serials)[] items)
    {
        using var admin = await Host.AdminAsync();
        var order = await admin.Send(new CreatePurchaseOrderCommand(Supplier, null, "Compra de prueba V4.2",
            items.Select(i => new PurchaseLineInput(i.Sku, i.Serials.Length, Products.First(p => p.Sku == i.Sku).Cost)).ToList()));
        await admin.Send(new ApprovePurchaseOrderCommand(order.Id));
        return await admin.Send(new ReceivePurchaseOrderCommand(order.Id, "FAC-PROV-" + order.Number,
            items.Select(i => new SkuSerials(i.Sku, i.Serials)).ToList()));
    }

    /// <summary>Estado de una serie (consulta directa).</summary>
    public Task<SerialNumber> SerialAsync(string serial) =>
        Host.DbAsync(db => db.Set<SerialNumber>().AsNoTracking().FirstAsync(s => s.Serial == serial));

    /// <summary>Variantes serializadas cuyas series en stock no coinciden con su stock en alguna sucursal (lo que vigila la
    /// vista <c>inventory.v_serial_breaches</c> en PostgreSQL).</summary>
    public Task<int> BreachesAsync() => Host.DbAsync(async db =>
    {
        var stock = await (from l in db.Set<StockLevel>()
                           join b in db.Set<Batch>() on l.BatchId equals b.Id
                           join v in db.Set<ProductVariant>() on b.VariantId equals v.Id
                           join p in db.Set<Product>() on v.ProductId equals p.Id
                           where p.TrackingMode == TrackingMode.Serial
                           group l.QuantityOnHand by new { l.BranchId, b.VariantId } into g
                           select new { g.Key.BranchId, g.Key.VariantId, OnHand = g.Sum() }).ToListAsync();
        var serials = await (from s in db.Set<SerialNumber>()
                             join l in db.Set<StockLevel>() on s.StockLevelId equals (Guid?)l.Id
                             where s.Status == SerialNumberStatus.InStock
                             group s by new { l.BranchId, s.VariantId } into g
                             select new { g.Key.BranchId, g.Key.VariantId, Count = g.Count() }).ToListAsync();
        return stock.Count(s => s.OnHand != (serials.FirstOrDefault(x => x.BranchId == s.BranchId && x.VariantId == s.VariantId)?.Count ?? 0))
               + serials.Count(x => !stock.Any(s => s.BranchId == x.BranchId && s.VariantId == x.VariantId));
    });

    /// <summary>XML guardado de un documento fiscal.</summary>
    public Task<string> XmlAsync(Guid documentId) =>
        Host.DbAsync(db => db.Set<FiscalDocumentFile>().Where(f => f.DocumentId == documentId).Select(f => f.Xml).FirstAsync());

    /// <summary>El XML valida contra el XSD embebido.</summary>
    public void Validate(string xml, int sector) => Host.Services.GetRequiredService<IFiscalDocumentSerializer>().Validate(xml, sector);

    public Task<T> DbAsync<T>(Func<MinvWriteDbContext, Task<T>> action) => Host.DbAsync(action);
}
