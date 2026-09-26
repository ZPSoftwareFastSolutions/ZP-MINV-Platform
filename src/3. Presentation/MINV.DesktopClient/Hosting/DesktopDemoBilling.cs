using System.Globalization;
using System.Text;
using MINV.Application.Billing;
using MINV.DesktopClient.ViewModels;
using MINV.Domain.Billing;

namespace MINV.DesktopClient.Hosting;

/// <summary>
/// V4.1 · Facturación SIAT de la DEMOSTRACIÓN (solo memoria): conexión con el simulador del SIN en proceso, sucursales en
/// el Padrón, «Preparar SIAT» (CUIS, CUFD, hora y catálogos), homologación de productos, unidades y medios de pago, y
/// activación. Todo con los mismos casos de uso que un administrador usaría en la pantalla. Lo usan las capturas y las
/// pruebas de la interfaz; nunca toca una base real (se niega fuera de la demostración).
/// </summary>
public static class DesktopDemoBilling
{
    /// <summary>NIT del ejemplo oficial del SIN (documentación del QR): solo para la demostración, ambiente de pruebas.</summary>
    public const long Nit = 1003579028;

    public const string SystemCode = "SIS-MINV-DEMO";

    public static async Task<string> ConfigureAsync(AppServices app)
    {
        if (!app.Session.IsDemo)
        {
            throw new InvalidOperationException("La configuración de prueba de la facturación solo se aplica a la demostración en memoria.");
        }
        var settings = await app.SendAsync(new GetSiatSettingsQuery());
        if (settings.IsEnabled)
        {
            return "La facturación de la demostración ya estaba activa.";
        }
        var today = BillingClock.Today(app);
        await app.SendAsync(new SaveSiatProfileCommand(SiatCodes.EnvironmentTest, SiatEndpointSet.ForBaseUrl(BillingSettingsViewModel.SimulatorUrl),
            BillingSettingsViewModel.PilotQrUrl, 15, DemoToken(), today.AddYears(1)));
        var code = 0;
        foreach (var branch in settings.Branches.OrderBy(b => b.BranchCode, StringComparer.Ordinal))
        {
            await app.SendAsync(new SaveSiatBranchCommand(branch.BranchCode, code++, "La Paz", "2800000"));
        }
        await app.SendAsync(new SaveSiatSettingsCommand(Nit, app.Session.Workspace.CompanyName, SystemCode, SiatCodes.EnvironmentTest, null, null, true));
        var prepared = await app.SendAsync(new PrepareSiatCommand());
        await HomologateAsync(app);
        await app.RefreshBillingAsync();
        return $"Facturación de prueba activa (simulador del SIN): CUIS {prepared.CuisRequested}, CUFD {prepared.CufdRequested}.";
    }

    /// <summary>Homologa todo lo pendiente: productos (sugerencias del catálogo del SIN o, si no hay, el primer producto de la
    /// actividad), unidades y medios de pago (por nombre; si no coincide, «unidad» y «otros»).</summary>
    public static async Task HomologateAsync(AppServices app)
    {
        var view = await app.SendAsync(new GetHomologationQuery());
        var activity = view.Activities.FirstOrDefault(a => a.IsCurrent && a.Sectors.Contains(SiatCodes.SectorPurchaseSale))
                       ?? view.Activities.FirstOrDefault(a => a.IsCurrent);
        if (activity is not null && view.PendingProducts > 0)
        {
            var suggestions = (await app.SendAsync(new SuggestProductHomologationQuery(activity.Code))).ToDictionary(s => s.Sku, StringComparer.OrdinalIgnoreCase);
            var fallback = (await app.SendAsync(new SearchSiatProductsQuery(activity.Code, null, 1))).FirstOrDefault();
            var items = view.Products.Where(p => p.IsActive && p.SinProductCode is null)
                .Select(p => suggestions.GetValueOrDefault(p.Sku) ?? (fallback is null ? null : new ProductHomologationInput(p.Sku, fallback.ActivityCode, fallback.ProductCode)))
                .OfType<ProductHomologationInput>().ToList();
            if (items.Count > 0)
            {
                await app.SendAsync(new SaveProductHomologationCommand(items));
            }
        }
        foreach (var unit in view.Units.Where(u => u.SinUnitCode is null))
        {
            if (Match(view.SinUnits, unit.Name, "UNIDAD (BIENES)", "UNIDAD") is { } sin)
            {
                await app.SendAsync(new SaveUnitHomologationCommand(unit.Code, sin.Code));
            }
        }
        foreach (var method in view.PaymentMethods.Where(m => m.SinCode is null))
        {
            if (Match(view.SinPaymentMethods, method.Name, "OTROS", "OTRO") is { } sin)
            {
                await app.SendAsync(new SavePaymentMethodHomologationCommand(method.Code, sin.Code));
            }
        }
    }

    /// <summary>Token delegado de la demostración: azar de cada ejecución (el simulador acepta cualquiera de 10 o más caracteres; nunca se versiona uno).</summary>
    private static string DemoToken() => "demo." + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));

    private static SiatCatalogItemView? Match(IReadOnlyList<SiatCatalogItemView> items, string name, params string[] fallbacks)
    {
        var key = Plain(name);
        return items.FirstOrDefault(i => Plain(i.Description) == key)
               ?? items.FirstOrDefault(i => key.Length >= 4 && Plain(i.Description).StartsWith(key, StringComparison.Ordinal))
               ?? items.FirstOrDefault(i => key.Length >= 4 && Plain(i.Description).Contains(key, StringComparison.Ordinal))
               ?? fallbacks.Select(f => items.FirstOrDefault(i => Plain(i.Description).StartsWith(Plain(f), StringComparison.Ordinal))).FirstOrDefault(i => i is not null)
               ?? items.FirstOrDefault();
    }

    private static string Plain(string text)
    {
        var decomposed = (text ?? string.Empty).Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark))
        {
            sb.Append(char.ToUpperInvariant(ch));
        }
        return sb.ToString().Trim();
    }
}
