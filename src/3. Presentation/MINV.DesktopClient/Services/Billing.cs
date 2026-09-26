using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Threading;
using MINV.Application.Abstractions;
using MINV.Application.Billing;
using MINV.DesktopClient.ViewModels;
using MINV.Domain.Billing;
using MINV.Domain.Iam;
using MINV.Hardware;
using MINV.Hardware.EscPos;

namespace MINV.DesktopClient.Services;

/// <summary>
/// V4.1 · Trabajo automático de la facturación en ESTE equipo (modo Base local y Demostración; en modo Nube lo hace el
/// servidor): cada 20 segundos envía <see cref="RunSiatWorkCommand"/> (documentos pendientes, recuperación fuera de línea,
/// y cada tercera vez el mantenimiento: CUFD/CUIS, hora, catálogos y correos). Nunca interrumpe al usuario: los errores
/// van al registro y, si se repiten, a un aviso discreto.
/// </summary>
public sealed class BillingWorkService : IDisposable
{
    /// <summary>Intervalo entre dos rondas de trabajo.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(20);

    private readonly SerialMediator _mediator;
    private readonly SessionContext _session;
    private readonly NotificationService _notify;
    private readonly DispatcherTimer _timer = new() { Interval = Interval };
    private int _ticks;
    private int _failures;
    private bool _running;

    public BillingWorkService(SerialMediator mediator, SessionContext session, NotificationService notify)
    {
        _mediator = mediator;
        _session = session;
        _notify = notify;
        _timer.Tick += async (_, _) => await RunOnceAsync();
    }

    /// <summary>Terminó una ronda (la banda fiscal de la caja y el estado SIAT se actualizan).</summary>
    public event EventHandler? Completed;

    public bool IsActive => _timer.IsEnabled;

    public SiatWorkResult? LastResult { get; private set; }

    public DateTimeOffset? LastRunAt { get; private set; }

    /// <summary>¿Este equipo hace el trabajo? Solo con conexión directa (base local o demostración), el permiso de emitir y
    /// el módulo licenciado. La activación se revisa en cada ronda.</summary>
    public bool Applies => !_session.IsCloud && _session.Can(PermissionCodes.BillingIssue) && _session.HasBillingModule;

    public void Start()
    {
        if (Applies && !_timer.IsEnabled)
        {
            _timer.Start();
        }
    }

    public void Stop() => _timer.Stop();

    /// <summary>Una ronda: se salta si la anterior sigue corriendo o si la facturación no está activa.</summary>
    public async Task RunOnceAsync()
    {
        if (_running || !Applies)
        {
            return;
        }
        _running = true;
        try
        {
            _ticks++;
            if (_ticks % 15 == 0)
            {
                await _session.RefreshBillingAsync(_mediator);   // cada 5 minutos: ¿la activaron o la desactivaron en otro equipo?
            }
            if (!_session.IsBillingEnabled)
            {
                return;
            }
            LastResult = await _mediator.SendAsync(new RunSiatWorkCommand(Maintain: _ticks % 3 == 0));
            LastRunAt = DateTimeOffset.UtcNow;
            _failures = 0;
            Completed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _failures++;
            System.Diagnostics.Trace.TraceWarning("M-INV · trabajo automático de la facturación: {0}", ex.Message);
            if (_failures == 3)
            {
                _notify.Warning("Facturación: el envío automático no se completó",
                    AppServices.Describe(ex) + " Se sigue reintentando solo; revise Facturación › Estado SIAT.");
            }
        }
        finally
        {
            _running = false;
        }
    }

    public void Dispose() => _timer.Stop();
}

/// <summary>
/// V4.1 · Salida de los documentos fiscales en este equipo: rollo ESC/POS en la impresora de la caja, PDF guardado en la
/// carpeta temporal y abierto con el visor predeterminado, archivos exportados. Cada entrega queda registrada
/// (<see cref="RecordFiscalDeliveryCommand"/>) como evidencia para el SIN.
/// </summary>
public static class FiscalOutput
{
    /// <summary>Carpeta de los PDF y XML generados en este equipo.</summary>
    public static string Folder => Path.Combine(Path.GetTempPath(), "M-INV", "documentos-fiscales");

    /// <summary>¿Hay impresora de rollo configurada en esta estación?</summary>
    public static bool HasPrinter(ClientSettings settings) => settings.PrinterTarget is { Length: > 0 };

    /// <summary>Imprime el rollo fiscal del documento (y abre el cajón si se pide). false si la estación no tiene impresora.</summary>
    public static async Task<bool> PrintRollAsync(AppServices app, Guid documentId, bool openDrawer = false)
    {
        var s = app.Settings;
        IReceiptPrinter? printer;
        try
        {
            printer = ReceiptPrinters.Create(new HardwareOptions(s.PrinterKind, s.PrinterTarget, s.BaudRate));
        }
        catch (InvalidOperationException)
        {
            printer = null;
        }
        if (printer is null)
        {
            return false;
        }
        var file = await app.SendAsync(new RenderFiscalDocumentQuery(documentId, FiscalDeliveryChannel.Print, Math.Clamp(s.PrinterColumns, 32, 64)));
        byte[] bytes = openDrawer ? [.. file.Content, .. new EscPosDocument(s.PrinterColumns).OpenCashDrawer().ToArray()] : file.Content;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await Task.Run(() => printer.PrintAsync(bytes, timeout.Token), timeout.Token);
        await app.SendAsync(new RecordFiscalDeliveryCommand(documentId, FiscalDeliveryChannel.Print, printer.Name));
        return true;
    }

    /// <summary>Genera el PDF (representación gráfica), lo guarda en la carpeta temporal, registra la entrega y lo abre.</summary>
    public static async Task<string> SavePdfAsync(AppServices app, Guid documentId, bool open = true)
    {
        var file = await app.SendAsync(new RenderFiscalDocumentQuery(documentId, FiscalDeliveryChannel.Pdf));
        var path = Save(file.FileName, file.Content);
        await app.SendAsync(new RecordFiscalDeliveryCommand(documentId, FiscalDeliveryChannel.Pdf));
        if (open)
        {
            Open(path, app.Settings);
        }
        return path;
    }

    /// <summary>Guarda un archivo en la carpeta de documentos fiscales (nombre saneado) y devuelve la ruta.</summary>
    public static string Save(string fileName, byte[] content)
    {
        Directory.CreateDirectory(Folder);
        var safe = string.Concat(fileName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var path = Path.Combine(Folder, safe);
        File.WriteAllBytes(path, content);
        return path;
    }

    /// <summary>Abre un archivo con la aplicación predeterminada de Windows (no en las capturas ni en las pruebas).</summary>
    public static bool Open(string path, ClientSettings settings)
    {
        if (settings.IsReadOnly)
        {
            return false;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            System.Diagnostics.Trace.TraceWarning("M-INV · no se pudo abrir {0}: {1}", path, ex.Message);
            return false;
        }
    }

    /// <summary>Cuadro «Guardar como» para un archivo exportado (CSV o Excel). Null si el usuario cancela.</summary>
    public static string? AskSavePath(string suggestedName, string title)
    {
        var extension = Path.GetExtension(suggestedName);
        var filter = extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
            ? "Libro de Excel (*.xlsx)|*.xlsx"
            : "Archivo CSV (*.csv)|*.csv";
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = title,
            FileName = suggestedName,
            DefaultExt = extension,
            Filter = filter,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}

/// <summary>V4.1 · Vista previa en texto del ticket fiscal (la misma información del rollo, para la pantalla).</summary>
public static class FiscalTicketText
{
    public static string Render(FiscalPrintModel m, int width = 42)
    {
        ArgumentNullException.ThrowIfNull(m);
        var sb = new StringBuilder();
        void Center(string text)
        {
            foreach (var part in Wrap(text, width))
            {
                sb.AppendLine(new string(' ', Math.Max(0, (width - part.Length) / 2)) + part);
            }
        }
        void Pair(string left, string right) => sb.AppendLine(left.Length + right.Length + 1 > width
            ? left[..Math.Max(0, width - right.Length - 1)] + " " + right
            : left + new string(' ', width - left.Length - right.Length) + right);
        string Money(decimal value) => value.ToString("#,##0.00", Fmt.Culture);

        Center(m.Title);
        Center(m.Subtitle);
        Center(m.IssuerName);
        Center(m.BranchLabel + (m.PointOfSaleCode > 0 ? $" · Punto de venta {m.PointOfSaleCode}" : ""));
        Center(m.Address);
        if (m.Phone is { Length: > 0 } phone)
        {
            Center("Tel. " + phone);
        }
        Center(m.Municipality);
        sb.AppendLine(new string('-', width));
        Pair("NIT", m.IssuerNit.ToString(CultureInfo.InvariantCulture));
        Pair(m.Original is null ? "FACTURA N°" : "NOTA N°", m.Number.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine("CÓD. AUTORIZACIÓN (CUF)");
        foreach (var part in Wrap(m.Cuf, width))
        {
            sb.AppendLine(part);
        }
        sb.AppendLine(new string('-', width));
        Pair("Fecha", m.IssuedAt.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture));
        foreach (var part in Wrap("Nombre: " + m.BuyerName, width))
        {
            sb.AppendLine(part);
        }
        Pair("NIT/CI/CEX", m.BuyerDocument);
        if (m.Original is { } original)
        {
            foreach (var part in Wrap($"Factura original N° {original.Number} del {original.IssuedAt:dd/MM/yyyy}", width))
            {
                sb.AppendLine(part);
            }
        }
        sb.AppendLine(new string('-', width));
        foreach (var line in m.Lines)
        {
            foreach (var part in Wrap($"{line.ProductCode} - {line.Description}", width))
            {
                sb.AppendLine(part);
            }
            Pair($"  {Fmt.Qty(line.Quantity)} x {Money(line.UnitPrice)}" + (line.Discount > 0 ? $" - {Money(line.Discount)}" : ""), Money(line.Subtotal));
        }
        sb.AppendLine(new string('-', width));
        Pair("SUBTOTAL Bs", Money(m.Subtotal));
        if (m.Discount > 0)
        {
            Pair("DESCUENTO Bs", Money(m.Discount));
        }
        Pair("TOTAL Bs", Money(m.Total));
        Pair("MONTO A PAGAR Bs", Money(m.AmountToPay));
        Pair("IMPORTE BASE CRÉDITO FISCAL", Money(m.TaxBase));
        if (m.CreditDebitAmount is { } credit)
        {
            Pair("CRÉDITO-DÉBITO FISCAL 13%", Money(credit));
        }
        foreach (var part in Wrap("Son: " + m.AmountInWords, width))
        {
            sb.AppendLine(part);
        }
        sb.AppendLine(new string('-', width));
        foreach (var legend in m.Legends)
        {
            Center(legend);
            sb.AppendLine();
        }
        if (m.IsTest)
        {
            Center("*** SIN VALOR LEGAL (AMBIENTE DE PRUEBAS) ***");
        }
        if (m.IsVoided)
        {
            Center("*** ANULADA ***");
        }
        return sb.ToString().TrimEnd();
    }

    private static IEnumerable<string> Wrap(string text, int width)
    {
        var words = (text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var line = new StringBuilder();
        foreach (var word in words)
        {
            var w = word;
            while (w.Length > width)
            {
                if (line.Length > 0)
                {
                    yield return line.ToString();
                    line.Clear();
                }
                yield return w[..width];
                w = w[width..];
            }
            if (line.Length > 0 && line.Length + 1 + w.Length > width)
            {
                yield return line.ToString();
                line.Clear();
            }
            if (line.Length > 0)
            {
                line.Append(' ');
            }
            line.Append(w);
        }
        if (line.Length > 0)
        {
            yield return line.ToString();
        }
    }
}
