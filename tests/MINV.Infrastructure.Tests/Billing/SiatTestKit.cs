using System.Collections.Concurrent;
using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using MINV.Application.Abstractions;
using MINV.Domain.Billing;
using MINV.Infrastructure.Billing.Simulator;
using MINV.Infrastructure.Billing.Soap;

namespace MINV.Tests.Siat;

/// <summary>Reloj manual para el simulador (plazos, vigencias).</summary>
internal sealed class FakeTime(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>Bitácora técnica en memoria (para comprobar qué se registra y que nunca aparece el token).</summary>
internal sealed class RecordingCallLog : ISiatCallLog
{
    public ConcurrentQueue<SiatCallRecord> Records { get; } = new();

    public Task RecordAsync(SiatCallRecord call, CancellationToken cancellationToken = default)
    {
        Records.Enqueue(call);
        return Task.CompletedTask;
    }
}

/// <summary>Fábrica de clientes HTTP de prueba (opcionalmente con un manejador propio).</summary>
internal sealed class TestHttpClientFactory(HttpMessageHandler? handler = null) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) =>
        handler is null ? new HttpClient { Timeout = Timeout.InfiniteTimeSpan } : new HttpClient(handler, false) { Timeout = Timeout.InfiniteTimeSpan };
}

/// <summary>Reloj del puerto de aplicación para los gateways.</summary>
internal sealed class TestClock(TimeProvider? time = null) : IClock
{
    public DateTimeOffset UtcNow => (time ?? TimeProvider.System).GetUtcNow();

    public DateOnly TodayIn(string timeZoneId) => DateOnly.FromDateTime(UtcNow.ToOffset(TimeSpan.FromHours(-4)).DateTime);
}

/// <summary>
/// Datos y documentos de prueba del SIN. Los XML se arman desde los EJEMPLOS OFICIALES del SIN
/// (Billing/Xsd/facturaComputarizadaCompraVenta.xml y notaComputarizadaCreditoDebito.xml) reemplazando emisor, lugar,
/// CUFD, fecha, número y CUF (generado con <see cref="Cuf.Generate"/>): no dependen del serializador de la aplicación.
/// </summary>
internal static class SiatTestKit
{
    public const string Token = "token-delegado-de-prueba-0001";
    public const long Nit = 1003579028;
    public const string SystemCode = "SIS-MINV-PRUEBA";

    public static readonly DateTimeOffset Start = new(2026, 9, 25, 14, 0, 0, TimeSpan.Zero);   // 10:00 en Bolivia

    public static SiatSimulatorCaller Caller => new(SiatCodes.EnvironmentTest, SiatCodes.ModalityComputerized, Nit, SystemCode);

    public static SiatConnection Connection(string baseUrl = "http://127.0.0.1:5095", string token = Token, TimeSpan? timeout = null, long nit = Nit) =>
        new(SiatCodes.EnvironmentTest, nit, SystemCode, token, SiatEndpointSet.ForBaseUrl(baseUrl), timeout ?? TimeSpan.FromSeconds(15));

    public static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "MINV.sln")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        return dir ?? throw new InvalidOperationException("No se encontró MINV.sln");
    }

    private static string Example(string fileName) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "src", "2. Infrastructure", "MINV.Infrastructure", "Billing", "Xsd", fileName), Encoding.UTF8);

    /// <summary>Hora de Bolivia (sin zona, con milisegundos) de un instante.</summary>
    public static DateTime Bolivia(DateTimeOffset instant) =>
        DateTime.SpecifyKind(instant.ToOffset(TimeSpan.FromHours(-4)).DateTime, DateTimeKind.Unspecified);

    /// <summary>Factura Compra Venta (sector 1) válida para ese lugar y CUFD (montos del ejemplo oficial: 100 − 1 = 99).</summary>
    public static string Invoice(string cufd, string controlCode, DateTime issuedAt, long number, int branch = 0, int pointOfSale = 0,
        int emission = SiatCodes.EmissionOnline, string? cafc = null, long nit = Nit, Action<XElement>? tamper = null)
    {
        var root = XDocument.Parse(Example("facturaComputarizadaCompraVenta.xml")).Root!;
        var header = root.Element("cabecera")!;
        Set(header, "nitEmisor", nit.ToString(CultureInfo.InvariantCulture));
        Set(header, "numeroFactura", number.ToString(CultureInfo.InvariantCulture));
        Set(header, "cufd", cufd);
        Set(header, "codigoSucursal", branch.ToString(CultureInfo.InvariantCulture));
        Set(header, "direccion", $"AV. SIMULADA N° {branch}");
        Set(header, "codigoPuntoVenta", pointOfSale.ToString(CultureInfo.InvariantCulture));
        Set(header, "fechaEmision", FiscalRules.FormatDateTime(issuedAt));
        if (cafc is not null)
        {
            Set(header, "cafc", cafc);
        }
        Set(header, "cuf", Cuf.Generate(new Cuf.Parts(nit, issuedAt, branch, SiatCodes.ModalityComputerized, emission, SiatCodes.InvoiceWithTaxCredit,
            SiatCodes.SectorPurchaseSale, number, pointOfSale), controlCode));
        tamper?.Invoke(root);
        return Serialize(root);
    }

    /// <summary>Nota Crédito-Débito (sector 24) válida: 775 original (tx 1), 75 devuelto (tx 2), 9.75 de crédito fiscal.</summary>
    public static string Note(string cufd, string controlCode, DateTime issuedAt, long number, int branch = 0, int pointOfSale = 0,
        Action<XElement>? tamper = null, long nit = Nit)
    {
        var root = XDocument.Parse(Example("notaComputarizadaCreditoDebito.xml")).Root!;
        var header = root.Element("cabecera")!;
        Set(header, "nitEmisor", nit.ToString(CultureInfo.InvariantCulture));
        Set(header, "numeroNotaCreditoDebito", number.ToString(CultureInfo.InvariantCulture));
        Set(header, "cufd", cufd);
        Set(header, "codigoSucursal", branch.ToString(CultureInfo.InvariantCulture));
        Set(header, "direccion", $"AV. SIMULADA N° {branch}");
        Set(header, "codigoPuntoVenta", pointOfSale.ToString(CultureInfo.InvariantCulture));
        Set(header, "fechaEmision", FiscalRules.FormatDateTime(issuedAt));
        Set(header, "fechaEmisionFactura", FiscalRules.FormatDateTime(issuedAt.AddDays(-3)));
        Set(header, "cuf", Cuf.Generate(new Cuf.Parts(nit, issuedAt, branch, SiatCodes.ModalityComputerized, SiatCodes.EmissionOnline,
            SiatCodes.AdjustmentDocument, SiatCodes.SectorCreditDebitNote, number, pointOfSale), controlCode));
        tamper?.Invoke(root);
        return Serialize(root);
    }

    /// <summary>Cambia el valor de un elemento (y le quita <c>xsi:nil</c>).</summary>
    public static void Set(XElement parent, string name, string value)
    {
        var element = parent.Element(name) ?? throw new InvalidOperationException($"El ejemplo oficial no tiene <{name}>.");
        element.Attributes().Where(a => a.Name.LocalName == "nil").Remove();
        element.Value = value;
    }

    public static string Serialize(XElement root) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" + root.ToString(SaveOptions.DisableFormatting);

    public static byte[] Gzip(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, true))
        {
            gzip.Write(data);
        }
        return output.ToArray();
    }

    public static byte[] Gzip(string xml) => Gzip(new UTF8Encoding(false).GetBytes(xml));

    public static byte[] Package(IReadOnlyList<string> xmls)
    {
        using var tar = new MemoryStream();
        using (var writer = new TarWriter(tar, TarEntryFormat.Ustar, true))
        {
            for (var i = 0; i < xmls.Count; i++)
            {
                writer.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, $"{i + 1}.xml")
                {
                    DataStream = new MemoryStream(new UTF8Encoding(false).GetBytes(xmls[i])),
                });
            }
        }
        return Gzip(tar.ToArray());
    }

    public static string Sha256(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
