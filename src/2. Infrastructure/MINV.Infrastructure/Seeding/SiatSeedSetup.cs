using System.Globalization;
using System.Text;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using MINV.Application.Abstractions;
using MINV.Application.Billing;
using MINV.Application.Iam;
using MINV.Domain.Billing;
using MINV.Infrastructure.Seeding.Tecnologia;

namespace MINV.Infrastructure.Seeding;

/// <summary>V4.1 · Sesión que envía casos de uso con la tubería completa (datos de prueba y demostración).</summary>
internal interface ISeedSession
{
    Task<T> Send<T>(IRequest<T> request, CancellationToken ct);
}

/// <summary>V4.1 · Sesión iniciada con <see cref="LoginCommand"/> en un scope propio: como el transporte local del
/// escritorio, cada caso de uso parte con el rastreo limpio.</summary>
internal sealed class SeedScopeSession(IServiceScope scope) : ISeedSession, IDisposable
{
    public IServiceScope Scope => scope;

    public static async Task<SeedScopeSession> SignInAsync(IServiceScopeFactory scopes, string tenantCode, string email, string password,
        CancellationToken ct)
    {
        var scope = scopes.CreateScope();
        try
        {
            await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new LoginCommand(tenantCode, email, password, Environment.MachineName,
                "demostracion"), ct);
            return new SeedScopeSession(scope);
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    public Task<T> Send<T>(IRequest<T> request, CancellationToken ct)
    {
        scope.ServiceProvider.GetRequiredService<IMinvDbContext>().ClearTracking();
        return scope.ServiceProvider.GetRequiredService<IMediator>().Send(request, ct);
    }

    public void Dispose() => scope.Dispose();
}

/// <summary>V4.1 · Empresa emisora de prueba: NIT y razón social del Padrón simulado, código de sistema, token de
/// simulación (se cifra con la clave maestra al guardarlo) y URL base del simulador del SIN.</summary>
internal sealed record SiatSeedProfile(long Nit, string BusinessName, string SystemCode, string Token, string SimulatorBaseUrl, DateOnly TokenValidUntil);

/// <summary>V4.1 · Sucursal de M-INV ↔ sucursal del Padrón (0 = casa matriz).</summary>
internal sealed record SiatSeedBranch(string BranchCode, int SiatCode, string Municipality, string? Phone);

/// <summary>V4.1 · Caja que factura con su propio punto de venta del SIN (registroPuntoVenta).</summary>
internal sealed record SiatSeedRegister(string BranchCode, string RegisterCode, string Name);

/// <summary>
/// V4.1 · Configuración de la facturación SIAT de una empresa de prueba a través de los MISMOS casos de uso que usa la
/// pantalla «Configuración › Facturación SIAT» (regla A-13): datos del Padrón, conexión del ambiente 2 con el token
/// cifrado, sucursales del Padrón, puntos de venta por caja (además del punto 0), catálogos sincronizados, CUIS y CUFD
/// del día, homologación de productos (V4.2: el producto SIN de cada producto del catálogo de tecnología, de su actividad
/// 4741100, 4741200 o 4742100; la sugerencia del SIN para cualquier otro), unidades (UND → 57, SERV → 58, por la
/// descripción del catálogo sincronizado) y medios de pago, y al final la activación. La usan los datos de prueba y la
/// demostración (<see cref="LocalDataSeeder"/>).
/// </summary>
internal static class SiatSeedSetup
{
    /// <summary>Consulta por QR del ambiente piloto del SIN (la representación gráfica de pruebas lleva «SIN VALOR LEGAL»).</summary>
    public const string PilotQrBaseUrl = "https://pilotosiat.impuestos.gob.bo/consulta/QR";

    /// <summary>Simulador HTTP del SIN de este equipo (src/4. Tools/MINV.SiatSimulator).</summary>
    public const string LocalSimulatorUrl = "http://localhost:5095";

    public static async Task<IReadOnlyList<SiatPointOfSaleStatus>> ConfigureAsync(ISeedSession admin, SiatSeedProfile profile,
        IReadOnlyList<SiatSeedBranch> branches, IReadOnlyList<SiatSeedRegister> registers, Action<string> log, CancellationToken ct)
    {
        await admin.Send(new SaveSiatSettingsCommand(profile.Nit, profile.BusinessName, profile.SystemCode, SiatCodes.EnvironmentTest, null, null,
            Enabled: false), ct);
        await admin.Send(new SaveSiatProfileCommand(SiatCodes.EnvironmentTest, SiatEndpointSet.ForBaseUrl(profile.SimulatorBaseUrl), PilotQrBaseUrl, 15,
            profile.Token, profile.TokenValidUntil), ct);
        foreach (var branch in branches)
        {
            await admin.Send(new SaveSiatBranchCommand(branch.BranchCode, branch.SiatCode, branch.Municipality, branch.Phone), ct);
        }
        foreach (var register in registers)
        {
            await admin.Send(new RegisterSiatPointOfSaleCommand(register.BranchCode, register.Name, $"{register.Name} · {register.RegisterCode}",
                register.RegisterCode), ct);
        }
        var sync = await admin.Send(new SyncSiatCatalogsCommand(), ct);
        var prepared = await admin.Send(new PrepareSiatCommand(), ct);
        var homologated = await HomologateAsync(admin, ct);
        await admin.Send(new SaveSiatSettingsCommand(profile.Nit, profile.BusinessName, profile.SystemCode, SiatCodes.EnvironmentTest, null, null,
            Enabled: true), ct);
        var status = await admin.Send(new GetSiatStatusQuery(), ct);
        log($"Facturación SIAT: NIT {profile.Nit} · {profile.BusinessName} · ambiente 2 (pruebas) contra el simulador {profile.SimulatorBaseUrl}; " +
            $"{sync.Items} filas de {sync.Catalogs} catálogos, {status.Points.Count} puntos de venta ({prepared.CufdRequested} CUFD del día), " +
            $"{homologated} productos homologados.");
        return status.Points;
    }

    /// <summary>
    /// Homologación completa: unidades y medios de pago contra los catálogos sincronizados (por descripción, regla F-07) y
    /// productos: los del catálogo de tecnología con el producto SIN del JSON (actividad y código, validados contra lo
    /// sincronizado por <see cref="SaveProductHomologationCommand"/>); cualquier otro, con la sugerencia del SIN
    /// (<see cref="SuggestProductHomologationQuery"/>) en la actividad principal. Devuelve cuántos productos se homologaron.
    /// </summary>
    public static async Task<int> HomologateAsync(ISeedSession admin, CancellationToken ct)
    {
        var catalog = TechSeedCatalog.Current;
        var view = await admin.Send(new GetHomologationQuery(), ct);
        foreach (var unit in view.Units.Where(u => u.SinUnitCode is null))
        {
            // UND → «UNIDAD (BIENES)» y SERV → «UNIDAD (SERVICIOS)» (descripciones del catálogo de tecnología)
            var tech = catalog.Units.FirstOrDefault(u => u.Code == unit.Code);
            if ((tech is null ? UnitCode(view.SinUnits, unit.Code, unit.Name) : Find(view.SinUnits, tech.SinDescription)) is { } code)
            {
                await admin.Send(new SaveUnitHomologationCommand(unit.Code, code), ct);
            }
        }
        foreach (var method in view.PaymentMethods.Where(m => m.SinCode is null))
        {
            if (PaymentCode(view.SinPaymentMethods, method.Code, method.Name) is { } code)
            {
                await admin.Send(new SavePaymentMethodHomologationCommand(method.Code, code), ct);
            }
        }
        var pending = view.Products.Where(p => p.IsActive && p.SinProductCode is null).ToList();
        if (pending.Count == 0)
        {
            return 0;
        }
        var current = view.Activities.Where(a => a.IsCurrent).Select(a => a.Code).ToHashSet(StringComparer.Ordinal);
        var items = new List<ProductHomologationInput>();
        var others = new List<string>();
        foreach (var product in pending)
        {
            if (catalog.HasProduct(product.Sku) && catalog.Product(product.Sku).Sin is { } sin && current.Contains(sin.Activity))
            {
                items.Add(new ProductHomologationInput(product.Sku, sin.Activity, sin.Product));
            }
            else
            {
                others.Add(product.Sku);
            }
        }
        var main = view.Activities.FirstOrDefault(a => a.IsCurrent && a.ActivityType == "P") ?? view.Activities.FirstOrDefault(a => a.IsCurrent);
        if (others.Count > 0 && main is not null)
        {
            var suggestions = (await admin.Send(new SuggestProductHomologationQuery(main.Code), ct))
                .GroupBy(s => s.Sku, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            items.AddRange(others.Where(suggestions.ContainsKey).Select(sku => suggestions[sku]));
        }
        foreach (var chunk in items.Chunk(200))
        {
            await admin.Send(new SaveProductHomologationCommand(chunk), ct);
        }
        return items.Count;
    }

    /// <summary>Unidad del SIN de una unidad de M-INV (por descripción del catálogo sincronizado, nunca un código fijo).</summary>
    public static int? UnitCode(IReadOnlyList<SiatCatalogItemView> sinUnits, string code, string name)
    {
        var key = Plain(code) + " " + Plain(name);
        string[] wanted = key switch
        {
            _ when Has(key, "KG", "KILO") => ["KILOGRAMO"],
            _ when Has(key, "GL", "GALON") => ["GALON"],
            _ when Has(key, "LT", "LITRO") => ["LITRO"],
            _ when Has(key, "M2") => ["METRO CUADRADO"],
            _ when Has(key, "MT", "M", "METRO") => ["METRO"],
            _ when Has(key, "CAJA", "CJ") => ["CAJA"],
            _ when Has(key, "PAQ", "PAQUETE", "BOLSA") => ["PAQUETE", "BOLSA"],
            _ when Has(key, "PAR") => ["PAR"],
            _ when Has(key, "ROLLO") => ["ROLLO"],
            _ when Has(key, "DOC", "DOCENA") => ["DOCENA"],
            _ when Has(key, "JGO", "JUEGO") => ["JUEGO"],
            _ => ["UNIDAD (BIENES)", "UNIDAD"],
        };
        return Find(sinUnits, wanted) ?? Find(sinUnits, "UNIDAD (BIENES)") ?? Find(sinUnits, "OTRO");
    }

    /// <summary>Método de pago del SIN de un medio de pago de M-INV (por descripción del catálogo sincronizado).</summary>
    public static int? PaymentCode(IReadOnlyList<SiatCatalogItemView> sinMethods, string code, string name)
    {
        var key = Plain(code) + " " + Plain(name);
        return key switch
        {
            _ when Has(key, "QR", "BILLETERA") => FindContaining(sinMethods, "QR") ?? Find(sinMethods, "OTROS"),
            _ when Has(key, "TARJETA") => Find(sinMethods, "TARJETA"),
            _ when Has(key, "TRANSFERENCIA") => FindStarting(sinMethods, "TRANSFERENCIA"),
            _ when Has(key, "CHEQUE") => Find(sinMethods, "CHEQUE"),
            _ when Has(key, "EFECTIVO") => Find(sinMethods, "EFECTIVO"),
            _ => null,
        } ?? Find(sinMethods, "OTROS");
    }

    /// <summary>Código de un catálogo cuya descripción (sin tildes, en mayúsculas) contiene el texto; null si no hay.</summary>
    public static int? CodeContaining(IReadOnlyList<SiatCatalogItemView> items, string text) => FindContaining(items, text);

    /// <summary>Código de un catálogo cuya descripción (sin tildes, en mayúsculas) es exactamente uno de los textos.</summary>
    public static int? CodeFor(IReadOnlyList<SiatCatalogItemView> items, params string[] texts) => Find(items, texts);

    private static int? Find(IReadOnlyList<SiatCatalogItemView> items, params string[] texts) =>
        texts.Select(t => items.FirstOrDefault(i => i.IsCurrent && Plain(i.Description) == Plain(t))).FirstOrDefault(i => i is not null)?.Code;

    private static int? FindContaining(IReadOnlyList<SiatCatalogItemView> items, string text) =>
        items.FirstOrDefault(i => i.IsCurrent && Plain(i.Description).Contains(Plain(text), StringComparison.Ordinal))?.Code;

    private static int? FindStarting(IReadOnlyList<SiatCatalogItemView> items, string text) =>
        items.FirstOrDefault(i => i.IsCurrent && Plain(i.Description).StartsWith(Plain(text), StringComparison.Ordinal))?.Code;

    private static bool Has(string key, params string[] words) =>
        words.Any(w => key.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(k => k == w || (w.Length >= 4 && k.StartsWith(w, StringComparison.Ordinal))));

    /// <summary>Sin tildes y en mayúsculas («Galón» → «GALON»).</summary>
    public static string Plain(string? text)
    {
        var decomposed = (text ?? string.Empty).Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(char.ToUpperInvariant(ch));
            }
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
