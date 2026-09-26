using System.IO;
using MINV.Application.Abstractions;
using MINV.Application.Billing;
using MINV.Application.Sales;
using MINV.DesktopClient.Controls;
using MINV.DesktopClient.Mvvm;
using MINV.DesktopClient.Services;
using MINV.Domain.Billing;
using MINV.Domain.Iam;

namespace MINV.DesktopClient.ViewModels;

/// <summary>
/// V4.1 · Resultado fiscal de un cobro en la caja: el documento DEFINITIVO (si el SIN no respondió y se re-emitió fuera de
/// línea, ese), su número, CUF y estado, los mensajes del SIN si lo rechazó (con la opción de corregir el comprador y
/// re-emitir), el ticket fiscal en pantalla con su QR y las acciones de entrega (imprimir otra vez, PDF, correo).
/// </summary>
public sealed class PosFiscalResult : ObservableObject
{
    private readonly AppServices _app;
    private readonly bool _openDrawer;
    private FiscalDocumentDetail _detail;
    private FiscalPrintModel? _model;
    private string? _printText;
    private string? _notice;

    public PosFiscalResult(AppServices app, CheckoutResult sale, FiscalDocumentDetail detail, FiscalPrintModel? model, bool printed, string? notice,
        bool openDrawer)
    {
        _app = app;
        Sale = sale;
        _detail = detail;
        _model = model;
        _openDrawer = openDrawer;
        _notice = notice;
        _printText = printed ? "✔ Ticket fiscal impreso en la impresora de la caja." : FiscalOutput.HasPrinter(app.Settings)
            ? "No se imprimió: revise la impresora e intente «Imprimir otra vez»."
            : "Esta caja no tiene impresora de rollo: entregue el PDF o envíe la factura por correo.";
        PrintAgain = new AsyncRelayCommand(PrintAsync);
        ViewPdf = new AsyncRelayCommand(PdfAsync);
        SendEmail = new AsyncRelayCommand(EmailAsync, () => app.Session.Can(PermissionCodes.BillingIssue));
        Reissue = new AsyncRelayCommand(ReissueAsync, () => IsRejected && app.Session.Can(PermissionCodes.BillingIssue));
        OpenDocument = new RelayCommand(() =>
        {
            Closed?.Invoke(this, EventArgs.Empty);
            app.Navigator.Navigate("documentos-fiscales", new FiscalDocumentFocus(_detail.Row.Id));
        }, () => app.Session.Can(PermissionCodes.BillingView));
    }

    public CheckoutResult Sale { get; }

    /// <summary>La caja cierra el resultado («Siguiente venta» o ir al documento).</summary>
    public event EventHandler? Closed;

    public FiscalDocumentRow Row => _detail.Row;

    public string Title => Row.Status switch
    {
        FiscalDocumentStatus.Valid => $"Factura N° {Row.Number} válida",
        FiscalDocumentStatus.Offline => $"Factura N° {Row.Number} emitida fuera de línea",
        FiscalDocumentStatus.Rejected or FiscalDocumentStatus.PackageRejected => $"Factura N° {Row.Number} rechazada por el SIN",
        FiscalDocumentStatus.Pending => $"Factura N° {Row.Number} pendiente de envío",
        _ => $"Factura N° {Row.Number} · {FiscalText.Status(Row.Status).ToLower(Fmt.Culture)}",
    };

    public string Explanation => Row.Status switch
    {
        FiscalDocumentStatus.Valid => "El SIN recibió y validó la factura.",
        FiscalDocumentStatus.Offline => "No hubo comunicación con el SIN: la factura es válida para el cliente y se enviará sola en un paquete al volver la conexión.",
        FiscalDocumentStatus.Rejected or FiscalDocumentStatus.PackageRejected =>
            "El SIN no aceptó la factura. Revise los mensajes, corrija los datos del comprador y re-emita (la venta ya está cobrada).",
        FiscalDocumentStatus.Pending => "Se envía al SIN automáticamente en los próximos segundos.",
        _ => FiscalText.StatusHelp(Row.Status),
    };

    public string StatusText => FiscalText.Status(Row.Status, Row.IsReverted);

    public string StatusBrush => FiscalText.Brush(Row.Status);

    public string StatusSoftBrush => StatusBrush + "Soft";

    public string Glyph => Row.Status switch
    {
        FiscalDocumentStatus.Valid => Glyphs.CheckCircle,
        FiscalDocumentStatus.Offline => Glyphs.Offline,
        FiscalDocumentStatus.Rejected or FiscalDocumentStatus.PackageRejected => Glyphs.Error,
        _ => Glyphs.Clock,
    };

    public bool IsRejected => Row.Status is FiscalDocumentStatus.Rejected or FiscalDocumentStatus.PackageRejected;

    public string Cuf => Row.Cuf;

    public string BuyerText => $"{Row.BuyerName} · {Row.BuyerDocument}";

    public string SaleText => $"Venta {Sale.InvoiceNumber} · {Fmt.Money(Sale.Total)} · {Sale.PaymentMethod}" +
                              (Sale.Change > 0 ? $" · vuelto {Fmt.Money(Sale.Change)}" : string.Empty);

    /// <summary>Mensajes del SIN del rechazo (o de la última respuesta).</summary>
    public string? Messages => _detail.Events.Where(e => e.Messages is not null).OrderByDescending(e => e.OccurredAt).Select(e => e.Messages).FirstOrDefault();

    public bool HasMessages => IsRejected && Messages is not null;

    public string? Notice { get => _notice; private set => Set(ref _notice, value); }

    public string? PrintText { get => _printText; private set => Set(ref _printText, value); }

    public string TicketText => _model is { } m ? FiscalTicketText.Render(m, 42) : string.Empty;

    public string QrUrl => _model?.QrUrl ?? string.Empty;

    public AsyncRelayCommand PrintAgain { get; }

    public AsyncRelayCommand ViewPdf { get; }

    public AsyncRelayCommand SendEmail { get; }

    public AsyncRelayCommand Reissue { get; }

    public RelayCommand OpenDocument { get; }

    private async Task PrintAsync()
    {
        try
        {
            PrintText = await FiscalOutput.PrintRollAsync(_app, Row.Id, _openDrawer)
                ? "✔ Ticket fiscal impreso otra vez."
                : "Esta caja no tiene impresora de rollo (Configuración › Impresora de tickets).";
        }
        catch (Exception ex) when (AppServices.IsExpected(ex) || ex is IOException or UnauthorizedAccessException or TimeoutException
                                       or OperationCanceledException or System.Net.Sockets.SocketException or ArgumentException)
        {
            PrintText = "✖ No se pudo imprimir: " + (AppServices.IsExpected(ex) ? AppServices.Describe(ex) : ex.Message);
        }
    }

    private async Task PdfAsync()
    {
        try
        {
            var path = await FiscalOutput.SavePdfAsync(_app, Row.Id);
            Notice = $"PDF guardado: {Path.GetFileName(path)}";
        }
        catch (Exception ex) when (AppServices.IsExpected(ex) || ex is IOException or UnauthorizedAccessException)
        {
            Notice = "✖ No se pudo generar el PDF: " + (AppServices.IsExpected(ex) ? AppServices.Describe(ex) : ex.Message);
        }
    }

    private async Task EmailAsync()
    {
        var email = await _app.Dialogs.PromptAsync("Enviar la factura por correo", "Se envían el XML y el PDF al comprador.", "Correo del comprador",
            _detail.BuyerEmail is { } e ? [e] : [], "Enviar", _detail.BuyerEmail, "correo@ejemplo.com", glyph: Glyphs.Mail);
        if (string.IsNullOrWhiteSpace(email))
        {
            return;
        }
        try
        {
            Notice = FiscalText.Plain(await _app.SendAsync(new SendFiscalDocumentEmailCommand(Row.Id, email)));
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            Notice = "✖ " + AppServices.Describe(ex);
        }
    }

    private async Task ReissueAsync()
    {
        IReadOnlyList<SiatCatalogItemView> types;
        try
        {
            types = await _app.SendAsync(new GetSiatCatalogQuery(SiatCatalogNames.IdentityDocumentTypes));
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            types = [];
        }
        var dialog = new ReissueDialog(_app, _detail, types);
        if (!await _app.Dialogs.ShowAsync(dialog) || dialog.Result is not { } row)
        {
            return;
        }
        try
        {
            _detail = await _app.SendAsync(new GetFiscalDocumentQuery(row.Id));
            _model = await _app.SendAsync(new GetFiscalPrintModelQuery(row.Id));
            Notice = dialog.DispatchMessage;
            if (FiscalOutput.HasPrinter(_app.Settings))
            {
                await PrintAsync();
            }
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            Notice = "✖ " + AppServices.Describe(ex);
        }
        OnPropertiesChanged(nameof(Row), nameof(Title), nameof(Explanation), nameof(StatusText), nameof(StatusBrush), nameof(StatusSoftBrush), nameof(Glyph),
            nameof(IsRejected), nameof(Cuf), nameof(BuyerText), nameof(Messages), nameof(HasMessages), nameof(TicketText), nameof(QrUrl));
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }
}
