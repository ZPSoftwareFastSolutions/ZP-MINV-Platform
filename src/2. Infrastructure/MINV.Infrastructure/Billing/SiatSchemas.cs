using System.Xml;
using System.Xml.Schema;
using MINV.Domain.Billing;

namespace MINV.Infrastructure.Billing;

/// <summary>
/// V4.1 · XSD oficiales del SIN (modalidad computarizada) embebidos en el ensamblado: factura Compra Venta (sector 1) y
/// nota Crédito-Débito (sector 24). Los usan el serializador (antes de guardar) y el simulador (al recibir).
/// </summary>
public static class SiatSchemas
{
    private static readonly Lazy<XmlSchemaSet> PurchaseSale = new(() => Load("facturaComputarizadaCompraVenta.xsd"));
    private static readonly Lazy<XmlSchemaSet> CreditDebitNote = new(() => Load("notaComputarizadaCreditoDebito.xsd"));

    /// <summary>Nombre del elemento raíz y del archivo XSD de cada sector.</summary>
    public static (string Root, string Xsd) For(int documentSector) => documentSector switch
    {
        SiatCodes.SectorPurchaseSale => ("facturaComputarizadaCompraVenta", "facturaComputarizadaCompraVenta.xsd"),
        SiatCodes.SectorCreditDebitNote => ("notaFiscalComputarizadaCreditoDebito", "notaComputarizadaCreditoDebito.xsd"),
        _ => throw new NotSupportedException($"El documento sector {documentSector} no está implementado en la V4.1."),
    };

    public static XmlSchemaSet SchemaSet(int documentSector) => documentSector switch
    {
        SiatCodes.SectorPurchaseSale => PurchaseSale.Value,
        SiatCodes.SectorCreditDebitNote => CreditDebitNote.Value,
        _ => throw new NotSupportedException($"El documento sector {documentSector} no está implementado en la V4.1."),
    };

    /// <summary>Valida un XML: devuelve la lista de errores (vacía = válido). Las advertencias cuentan como error.</summary>
    public static IReadOnlyList<string> Validate(string xml, int documentSector)
    {
        var errors = new List<string>();
        var settings = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema,
            Schemas = SchemaSet(documentSector),
            ValidationFlags = XmlSchemaValidationFlags.ReportValidationWarnings | XmlSchemaValidationFlags.ProcessIdentityConstraints,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
        };
        settings.ValidationEventHandler += (_, e) => errors.Add($"{e.Severity}: {e.Message} (línea {e.Exception?.LineNumber})");
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), settings);
            while (reader.Read())
            {
            }
        }
        catch (XmlException ex)
        {
            errors.Add($"XML mal formado: {ex.Message}");
        }
        return errors;
    }

    private static XmlSchemaSet Load(string fileName)
    {
        var assembly = typeof(SiatSchemas).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith("." + fileName, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        var set = new XmlSchemaSet { XmlResolver = null };
        using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
        {
            set.Add(null, reader);
        }
        set.Compile();
        return set;
    }
}
