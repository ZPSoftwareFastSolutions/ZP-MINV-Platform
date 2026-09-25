using System.Globalization;
using MINV.Application.Abstractions;

namespace MINV.Hardware.EscPos;

/// <summary>Convierte un <see cref="Receipt"/> del punto de venta en un documento ESC/POS listo para imprimir.</summary>
public static class ReceiptRenderer
{
    public static byte[] Render(Receipt receipt, int columns = 48, bool openDrawer = false)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var doc = new EscPosDocument(columns)
            .Align(TextAlign.Center).Bold(true).Size(2, 2).Line(receipt.CompanyName).Size(1, 1).Bold(false);
        if (receipt.TaxId is { Length: > 0 } taxId)
        {
            doc.Line("NIT " + taxId);
        }
        doc.Line(receipt.BranchName)
            .Align(TextAlign.Left).Separator()
            .Columns2("Comprobante", receipt.Number)
            .Columns2("Fecha", receipt.IssuedAt.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture))
            .Columns2("Cajero", receipt.Cashier)
            .Separator();
        foreach (var line in receipt.Lines)
        {
            doc.Line(line.Description)
                .Columns2($"  {line.Quantity.ToString("0.###", CultureInfo.InvariantCulture)} x {EscPosDocument.Money(line.UnitPrice)}",
                    EscPosDocument.Money(line.Amount));
        }
        doc.Separator().Bold(true).Size(1, 2)
            .Columns2("TOTAL " + receipt.CurrencySymbol, EscPosDocument.Money(receipt.Total))
            .Size(1, 1).Bold(false);
        if (receipt.PaymentMethod is { Length: > 0 } method)
        {
            doc.Columns2("Pago", method);
        }
        if (receipt.Barcode is { Length: > 0 } code)
        {
            doc.Feed(1).Align(TextAlign.Center).Code128(code).Feed(1);
        }
        doc.Align(TextAlign.Center).Line("Gracias por su compra").Feed(3);
        if (openDrawer)
        {
            doc.OpenCashDrawer();
        }
        return doc.Cut().ToArray();
    }

    /// <summary>Página de prueba de la impresora (pantalla Configuración del cliente): acentos, negrita, tamaños,
    /// código de barras, QR y corte. Si sale completa, la impresora y la página de códigos están bien configuradas.</summary>
    public static byte[] RenderTestPage(string companyName, string printerName, DateTimeOffset printedAt, int columns = 48)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(companyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(printerName);
        return new EscPosDocument(columns)
            .Align(TextAlign.Center).Bold(true).Size(2, 2).Line("M-INV").Size(1, 1).Bold(false)
            .Line("Prueba de impresión")
            .Line(companyName)
            .Align(TextAlign.Left).Separator()
            .Columns2("Impresora", printerName)
            .Columns2("Fecha", printedAt.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture))
            .Columns2("Columnas", columns.ToString(CultureInfo.InvariantCulture))
            .Separator()
            .Line("Acentos: áéíóú ÁÉÍÓÚ ñÑ ü ¿? ¡!")
            .Bold(true).Line("Negrita").Bold(false)
            .Size(1, 2).Line("Doble alto").Size(1, 1)
            .Columns2("Importe", EscPosDocument.Money(1234.5m))
            .Feed(1).Align(TextAlign.Center).Code128("MINV-PRUEBA").Feed(1)
            .Qr("M-INV · prueba de impresión").Feed(1)
            .Line("Si lee todo esto, la impresora está lista.").Feed(3)
            .Cut().ToArray();
    }
}
