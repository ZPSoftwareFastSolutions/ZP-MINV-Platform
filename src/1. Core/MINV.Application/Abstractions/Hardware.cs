namespace MINV.Application.Abstractions;

/// <summary>Impresora de comprobantes (ESC/POS por COM, USB mediante la cola de Windows o red). Recibe el documento ya
/// codificado (bytes ESC/POS) para no atar la aplicación a un fabricante.</summary>
public interface IReceiptPrinter
{
    string Name { get; }

    Task PrintAsync(ReadOnlyMemory<byte> document, CancellationToken cancellationToken = default);
}

/// <summary>Lector de códigos de barras (puerto serie/USB-CDC o teclado).</summary>
public interface IBarcodeScanner : IAsyncDisposable
{
    event EventHandler<BarcodeScannedEventArgs>? Scanned;

    Task StartAsync(CancellationToken cancellationToken = default);
}

public sealed class BarcodeScannedEventArgs(string code, DateTimeOffset scannedAt) : EventArgs
{
    public string Code { get; } = code;

    public DateTimeOffset ScannedAt { get; } = scannedAt;
}

/// <summary>Comprobante de venta que el módulo de hardware convierte a ESC/POS.</summary>
public sealed record Receipt(
    string CompanyName, string? TaxId, string BranchName, string Number, DateTimeOffset IssuedAt, string Cashier,
    IReadOnlyList<ReceiptLine> Lines, decimal Total, string CurrencySymbol, string? PaymentMethod, string? Barcode);

public sealed record ReceiptLine(string Description, decimal Quantity, decimal UnitPrice)
{
    public decimal Amount => Quantity * UnitPrice;
}
