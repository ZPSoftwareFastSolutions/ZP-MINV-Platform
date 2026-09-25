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
}
