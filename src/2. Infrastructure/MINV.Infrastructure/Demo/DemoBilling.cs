using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using MINV.Application.Billing;
using MINV.Application.Partners;
using MINV.Application.Sales;
using MINV.Domain.Billing;
using MINV.Domain.Iam;
using MINV.Infrastructure.Seeding;
using MINV.Infrastructure.Services;

namespace MINV.Infrastructure.Demo;

/// <summary>V4.1 · Facturación de la demostración: NIT de simulación, documentos de ejemplo emitidos, líneas vendidas en
/// ellos (movimientos de stock que se suman a los de la V2.1) y el día en que se emitieron.</summary>
public sealed record DemoBilling(long Nit, string BusinessName, int Documents, int ValidDocuments, int SampleSaleLines, DateOnly SamplesOn);

/// <summary>
/// V4.1 · La empresa de demostración también FACTURA, con el simulador del SIN EN MEMORIA (sin red, sin disco, sin token
/// real): configuración mínima con los mismos casos de uso de la pantalla (datos del Padrón, conexión con un token de
/// demostración aleatorio, casa matriz = sucursal 0 del Padrón, punto 0 y el punto de venta de la caja CAJA01, catálogos,
/// CUIS/CUFD y homologación) y algunas facturas de ejemplo de hace una semana (una con el NIT especial 99003 de ventas
/// menores, que queda como identidad fiscal del consumidor final, otra con CI y otra con NIT y tarjeta), enviadas y
/// válidas: quedan fuera de los «últimos 7 días» de la pantalla de ventas, así la demostración sigue empezando sin ventas
/// recientes. Al terminar el reloj vuelve al «hoy» de la demostración con el CUFD del día.
/// </summary>
internal static class DemoBillingSetup
{
    public const long DemoNit = 1023456027L;

    /// <summary>Días antes de «hoy» en que se emiten las facturas de ejemplo.</summary>
    public const int SampleDaysAgo = 8;

    public static async Task<DemoBilling> PrepareAsync(IServiceScopeFactory scopes, DemoClock clock, string tenantCode, string companyName, string password,
        IReadOnlyList<DemoUser> users, CancellationToken ct)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(DemoWorkspace.TimeZoneId);
        var resume = clock.UtcNow;
        var sampleDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(resume, zone).DateTime).AddDays(-SampleDaysAgo);
        void At(int hour, int minute)
        {
            var local = sampleDay.ToDateTime(new TimeOnly(hour, minute));
            clock.StartAt(new DateTimeOffset(local, zone.GetUtcOffset(local)));
        }

        try
        {
            using var admin = await SeedScopeSession.SignInAsync(scopes, tenantCode, DemoWorkspace.AdminEmail, password, ct);
            At(8, 0);
            // Consumidor final: factura con el NIT especial 99003 (ventas menores) cuando el comprador no da datos
            await admin.Send(new SaveCustomerCommand("CF", "Consumidor final", SiatCodes.SpecialMinorSales, null, null, "GENERAL", true), ct);
            var token = "DEMO-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(20));   // de demostración: solo en memoria
            var businessName = companyName.Trim().ToUpperInvariant();
            await SiatSeedSetup.ConfigureAsync(admin, new SiatSeedProfile(DemoNit, businessName, "MINV-DEMO-0041", token, SiatSeedSetup.LocalSimulatorUrl,
                    sampleDay.AddYears(1)),
                [new SiatSeedBranch("CM", 0, "La Paz", null)], [new SiatSeedRegister("CM", "CAJA01", "Caja 1")], _ => { }, ct);

            // Facturas de ejemplo de hace una semana en la caja 1 (así «Hoy» y los últimos 7 días empiezan sin ventas)
            var seller = users.FirstOrDefault(u => u.RoleCode == RoleCodes.Sales)?.Email ?? DemoWorkspace.AdminEmail;
            using var cashier = await SeedScopeSession.SignInAsync(scopes, tenantCode, seller, password, ct);
            At(9, 0);
            await cashier.Send(new OpenPosSessionCommand("CAJA01", 200m), ct);
            var products = (await cashier.Send(new GetSellableProductsQuery(), ct)).Where(p => p.Available >= 10 && p.Price > 0)
                .OrderByDescending(p => p.Available).Take(8).ToList();
            var samples = new (int Hour, int Minute, string Method, string? Reference, string? Card, FiscalBuyerInput? Buyer)[]
            {
                (9, 40, "EFECTIVO", null, null, new FiscalBuyerInput(SiatCodes.DocumentNit, SiatCodes.SpecialMinorSales, null, null, null)),
                (11, 15, "QR", "QR-100245", null, new FiscalBuyerInput(SiatCodes.DocumentCi, "4455667", null, "Juan Pérez Quispe", "juan.perez@correo.example")),
                (15, 5, "TARJETA", "TJ-200311", "4111111111111111",
                    new FiscalBuyerInput(SiatCodes.DocumentNit, "1020703023", null, "CONSTRUCTORA ANDINA S.A.", "compras@constructoraandina.example")),
                (17, 30, "EFECTIVO", null, null, null),
            };
            var documents = 0;
            var valid = 0;
            var lines = 0;
            for (var i = 0; i < samples.Length && products.Count > 0; i++)
            {
                var (hour, minute, method, reference, card, buyer) = samples[i];
                At(hour, minute);
                var sale = new List<SaleLineInput> { new(products[i % products.Count].Sku, 1) };
                if (i % 2 == 1 && products.Count > 1)
                {
                    sale.Add(new SaleLineInput(products[(i + 3) % products.Count].Sku, products[(i + 3) % products.Count].AllowsDecimals ? 1.5m : 2m));
                }
                var result = await cashier.Send(new CheckoutCommand("CF", method, sale, null, reference, buyer, card), ct);
                lines += sale.Count;
                if (result.FiscalDocumentId is { } id)
                {
                    documents++;
                    var sent = await cashier.Send(new DispatchFiscalDocumentsCommand(id), ct);
                    valid += sent.Documents.Count(d => d.Status == FiscalDocumentStatus.Valid);
                }
            }
            At(18, 0);
            if ((await cashier.Send(new GetPosStateQuery(), ct)).Session is { } session)
            {
                await cashier.Send(new ClosePosSessionCommand(session.Id, session.ExpectedCash), ct);
            }

            // De vuelta al «hoy» de la demostración: CUFD del día para facturar en la caja
            clock.StartAt(resume);
            await admin.Send(new PrepareSiatCommand(), ct);
            return new DemoBilling(DemoNit, businessName, documents, valid, lines, sampleDay);
        }
        finally
        {
            if (clock.UtcNow < resume)
            {
                clock.StartAt(resume);
            }
        }
    }
}
