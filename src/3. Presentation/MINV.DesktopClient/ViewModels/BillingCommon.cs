using System.Globalization;
using MINV.Application.Billing;
using MINV.DesktopClient.Controls;
using MINV.DesktopClient.Mvvm;
using MINV.DesktopClient.Services;
using MINV.Domain.Billing;
using MINV.Domain.Iam;

namespace MINV.DesktopClient.ViewModels;

/// <summary>V4.1 · Textos y colores (claves de la paleta) de la facturación, en español claro para cajeros y contadores.</summary>
public static class FiscalText
{
    public static string Status(FiscalDocumentStatus status, bool isReverted = false) => status switch
    {
        FiscalDocumentStatus.Pending => "Pendiente",
        FiscalDocumentStatus.Valid => isReverted ? "Válida (anulación revertida)" : "Válida",
        FiscalDocumentStatus.Rejected => "Rechazada",
        FiscalDocumentStatus.NoResponse => "Sin respuesta",
        FiscalDocumentStatus.Offline => "Fuera de línea",
        FiscalDocumentStatus.InPackage => "En paquete",
        FiscalDocumentStatus.PackageRejected => "Observada",
        FiscalDocumentStatus.DuplicateToVoid => "Duplicada: anular",
        FiscalDocumentStatus.Voided => "Anulada",
        _ => "Descartada",
    };

    /// <summary>Qué significa cada estado (ayuda del filtro y del detalle).</summary>
    public static string StatusHelp(FiscalDocumentStatus status) => status switch
    {
        FiscalDocumentStatus.Pending => "Emitida en línea; se está enviando al SIN.",
        FiscalDocumentStatus.Valid => "El SIN la recibió y la validó.",
        FiscalDocumentStatus.Rejected => "El SIN la rechazó: la venta recibe una factura nueva (vea los mensajes del SIN).",
        FiscalDocumentStatus.NoResponse => "Se perdió la respuesta del SIN: la venta se re-emitió fuera de línea y esta se verifica al volver la conexión.",
        FiscalDocumentStatus.Offline => "Emitida sin conexión con el SIN: se envía sola en un paquete al volver la comunicación.",
        FiscalDocumentStatus.InPackage => "Enviada en un paquete de contingencia; falta la validación del SIN.",
        FiscalDocumentStatus.PackageRejected => "El SIN la observó al validar el paquete: se re-emite.",
        FiscalDocumentStatus.DuplicateToVoid => "El SIN la registró y la venta ya tiene otra factura: hay que anularla.",
        FiscalDocumentStatus.Voided => "Anulada ante el SIN.",
        _ => "Nunca llegó al SIN y se reemplazó por otra: queda solo como historia.",
    };

    public static string Brush(FiscalDocumentStatus status) => status switch
    {
        FiscalDocumentStatus.Valid => "Success",
        FiscalDocumentStatus.Pending or FiscalDocumentStatus.NoResponse => "Warning",
        FiscalDocumentStatus.Offline or FiscalDocumentStatus.InPackage => "Info",
        FiscalDocumentStatus.Rejected or FiscalDocumentStatus.PackageRejected or FiscalDocumentStatus.DuplicateToVoid => "Danger",
        _ => "StatusInactive",
    };

    public static string Kind(FiscalDocumentKind kind) => kind == FiscalDocumentKind.Invoice ? "Factura" : "Nota crédito-débito";

    public static string Mode(SiatConnectionMode mode) => mode switch
    {
        SiatConnectionMode.Online => "En línea",
        SiatConnectionMode.Offline => "Fuera de línea",
        SiatConnectionMode.ManualContingency => "Contingencia manual",
        _ => "Recuperando",
    };

    public static string ModeBrush(SiatConnectionMode mode) => mode switch
    {
        SiatConnectionMode.Online => "Success",
        SiatConnectionMode.Offline => "Warning",
        SiatConnectionMode.ManualContingency => "Danger",
        _ => "Info",
    };

    public static string EventStatus(SignificantEventStatus status) => status switch
    {
        SignificantEventStatus.Open => "Abierto",
        SignificantEventStatus.Closed => "Cerrado (sin registrar)",
        SignificantEventStatus.Registered => "Registrado en el SIN",
        SignificantEventStatus.PackagesSent => "Paquetes enviados",
        SignificantEventStatus.Reconciled => "Conciliado",
        _ => "Con observaciones",
    };

    public static string EventBrush(SignificantEventStatus status) => status switch
    {
        SignificantEventStatus.Open => "Warning",
        SignificantEventStatus.Closed => "Danger",
        SignificantEventStatus.Registered or SignificantEventStatus.PackagesSent => "Info",
        SignificantEventStatus.Reconciled => "Success",
        _ => "Danger",
    };

    public static string PackageStatus(FiscalPackageStatus status) => status switch
    {
        FiscalPackageStatus.Sent => "Enviado, por validar",
        FiscalPackageStatus.Validated => "Validado",
        FiscalPackageStatus.Observed => "Observado",
        _ => "Rechazado",
    };

    public static string PackageBrush(FiscalPackageStatus status) => status switch
    {
        FiscalPackageStatus.Sent => "Info",
        FiscalPackageStatus.Validated => "Success",
        FiscalPackageStatus.Observed => "Warning",
        _ => "Danger",
    };

    public static string Action(FiscalDocumentAction action) => action switch
    {
        FiscalDocumentAction.Issued => "Emitido",
        FiscalDocumentAction.Sent => "Enviado al SIN",
        FiscalDocumentAction.Accepted => "Validado por el SIN",
        FiscalDocumentAction.Rejected => "Rechazado por el SIN",
        FiscalDocumentAction.NoResponse => "Sin respuesta del SIN",
        FiscalDocumentAction.Reissued => "Re-emitido",
        FiscalDocumentAction.Packaged => "Incluido en un paquete",
        FiscalDocumentAction.PackageValidated => "Paquete validado",
        FiscalDocumentAction.PackageRejected => "Observado en el paquete",
        FiscalDocumentAction.StatusChecked => "Estado verificado en el SIN",
        FiscalDocumentAction.VoidRequested => "Anulación solicitada",
        FiscalDocumentAction.Voided => "Anulado en el SIN",
        FiscalDocumentAction.VoidFailed => "Anulación no aceptada",
        FiscalDocumentAction.Reverted => "Anulación revertida",
        FiscalDocumentAction.RevertFailed => "Reversión no aceptada",
        FiscalDocumentAction.Discarded => "Descartado",
        FiscalDocumentAction.Delivered => "Entregado al comprador",
        _ => "Impreso",
    };

    public static string ActionBrush(FiscalDocumentAction action) => action switch
    {
        FiscalDocumentAction.Accepted or FiscalDocumentAction.PackageValidated or FiscalDocumentAction.Reverted => "Success",
        FiscalDocumentAction.Rejected or FiscalDocumentAction.PackageRejected or FiscalDocumentAction.VoidFailed or FiscalDocumentAction.RevertFailed => "Danger",
        FiscalDocumentAction.NoResponse or FiscalDocumentAction.Voided or FiscalDocumentAction.Discarded => "Warning",
        FiscalDocumentAction.Delivered or FiscalDocumentAction.Printed => "Info",
        _ => "Brand",
    };

    public static string Channel(FiscalDeliveryChannel channel) => channel switch
    {
        FiscalDeliveryChannel.Email => "Correo",
        FiscalDeliveryChannel.Print => "Impresión en rollo",
        _ => "PDF",
    };

    /// <summary>«25/09/2026 10:32:05» (hora fiscal, sin zona).</summary>
    public static string FiscalTime(DateTime value) => value.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>CUF abreviado para las tablas (el completo va en el detalle, con copiar).</summary>
    public static string ShortCuf(string cuf) => cuf.Length > 16 ? cuf[..8] + "…" + cuf[^6..] : cuf;

    /// <summary>Cuenta regresiva hasta un plazo («vence en 5 h 20 min», «venció hace 2 h»).</summary>
    public static string Countdown(DateTimeOffset deadline, DateTimeOffset now)
    {
        var span = deadline - now;
        var past = span < TimeSpan.Zero;
        var abs = past ? -span : span;
        var text = abs.TotalDays >= 2 ? $"{(int)abs.TotalDays} días"
            : abs.TotalHours >= 1 ? $"{(int)abs.TotalHours} h {abs.Minutes} min"
            : $"{Math.Max(1, abs.Minutes)} min";
        return past ? $"venció hace {text}" : $"vence en {text}";
    }

    /// <summary>Diferencia entre la hora del SIN y la de este sistema, en palabras (la factura usa siempre la hora del SIN).</summary>
    public static string ClockOffset(long milliseconds)
    {
        var seconds = (long)Math.Round(Math.Abs(milliseconds) / 1000d);
        if (seconds == 0)
        {
            return "la hora coincide con la de este sistema";
        }
        var (h, m, sec) = (seconds / 3600, seconds % 3600 / 60, seconds % 60);
        var text = h > 0 ? (m > 0 ? $"{h} h {m} min" : $"{h} h")
            : m > 0 ? (sec > 0 ? $"{m} min {sec} s" : $"{m} min")
            : $"{sec} s";
        return milliseconds > 0 ? $"el SIN va {text} adelantado" : $"el SIN va {text} atrasado";
    }

    /// <summary>Quita el ✔ / ⚠ del principio de un mensaje del servidor (la pantalla pone su propio ícono).</summary>
    public static string Plain(string message) => message.TrimStart('✔', '⚠', '✖', ' ');
}

/// <summary>V4.1 · Tipo de documento de identidad del comprador (catálogo TIPO_DOCUMENTO_IDENTIDAD).</summary>
public sealed record DocumentTypeOption(int Code, string Short, string Label)
{
    public override string ToString() => Label;

    /// <summary>Tipos del catálogo sincronizado (o, si todavía no se sincronizó, los 5 del XSD del SIN).</summary>
    public static IReadOnlyList<DocumentTypeOption> From(IReadOnlyList<SiatCatalogItemView>? catalog)
    {
        var codes = catalog is { Count: > 0 } ? catalog.Where(c => c.IsCurrent).Select(c => c.Code).ToList() : [1, 2, 3, 4, 5];
        return codes.Distinct().Order().Select(code => code switch
        {
            SiatCodes.DocumentCi => new DocumentTypeOption(code, "CI", "CI · Cédula de identidad"),
            SiatCodes.DocumentCex => new DocumentTypeOption(code, "CEX", "CEX · Cédula de extranjero"),
            SiatCodes.DocumentPassport => new DocumentTypeOption(code, "PAS", "PAS · Pasaporte"),
            SiatCodes.DocumentOther => new DocumentTypeOption(code, "OD", "OD · Otro documento"),
            SiatCodes.DocumentNit => new DocumentTypeOption(code, "NIT", "NIT · Número de identificación tributaria"),
            _ => new DocumentTypeOption(code, code.ToString(CultureInfo.InvariantCulture),
                catalog?.FirstOrDefault(c => c.Code == code)?.Description ?? code.ToString(CultureInfo.InvariantCulture)),
        }).ToList();
    }
}

/// <summary>NIT especial del SIN (va con tipo NIT; el código de excepción lo pone la emisión).</summary>
public sealed record SpecialNit(string Code, string Label, string Explanation, string? DefaultName);

/// <summary>
/// V4.1 · Datos de facturación del comprador (nominatividad): tipo y número de documento, complemento (solo con CI),
/// nombre o razón social, correo, verificación del NIT en el Padrón y búsqueda del cliente al salir del número. Lo usan
/// la caja, la re-emisión y la transcripción de facturas manuales. La validación que manda es la del dominio.
/// </summary>
public sealed class BuyerForm : ObservableObject
{
    public static readonly IReadOnlyList<SpecialNit> SpecialNits =
    [
        new(SiatCodes.SpecialMinorSales, "99003 · Menores", "Ventas menores del día: resumen de ventas pequeñas a compradores que no dieron documento.",
            "VENTAS MENORES DEL DIA"),
        new(SiatCodes.SpecialTaxControl, "99002 · Control", "Control tributario: cuando un funcionario del SIN hace una compra de control.",
            "CONTROL TRIBUTARIO"),
        new(SiatCodes.SpecialConsulates, "99001 · Consulados", "Consulados, embajadas y organismos internacionales: escriba su nombre.", null),
    ];

    private readonly AppServices _app;
    private DocumentTypeOption _type;
    private string _number = string.Empty;
    private string _complement = string.Empty;
    private string _name = string.Empty;
    private string _email = string.Empty;
    private bool _exception;
    private string? _nitStatus;
    private string _nitBrush = "Info";
    private string? _foundText;
    private bool _looking;

    public BuyerForm(AppServices app, IReadOnlyList<SiatCatalogItemView>? documentTypes)
    {
        _app = app;
        DocumentTypes = DocumentTypeOption.From(documentTypes);
        _type = DocumentTypes.FirstOrDefault(t => t.Code == SiatCodes.DocumentCi) ?? DocumentTypes[0];
        VerifyNit = new AsyncRelayCommand(VerifyNitAsync, () => IsNit && CanVerify && long.TryParse(_number.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out _));
        UseSpecial = new RelayCommand<SpecialNit>(ApplySpecial);
        Lookup = new AsyncRelayCommand(LookupAsync);
    }

    public IReadOnlyList<DocumentTypeOption> DocumentTypes { get; }

    public DocumentTypeOption DocumentType
    {
        get => _type;
        set
        {
            if (Set(ref _type, value ?? DocumentTypes[0]))
            {
                if (!IsCi)
                {
                    Complement = string.Empty;
                }
                if (!IsNit)
                {
                    ExceptionRequested = false;
                }
                NitStatus = null;
                OnPropertiesChanged(nameof(IsCi), nameof(IsNit), nameof(ShowVerify));
            }
        }
    }

    /// <summary>Número de documento. Al salir del campo (<see cref="Lookup"/>) se busca si el comprador ya compró antes.</summary>
    public string DocumentNumber
    {
        get => _number;
        set
        {
            if (Set(ref _number, (value ?? string.Empty).Trim()))
            {
                NitStatus = null;
                FoundText = null;
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    public string Complement { get => _complement; set => Set(ref _complement, (value ?? string.Empty).Trim().ToUpperInvariant()); }

    public string Name { get => _name; set => Set(ref _name, value ?? string.Empty); }

    public string Email { get => _email; set => Set(ref _email, value ?? string.Empty); }

    /// <summary>«Facturar aunque el NIT no sea válido» (código de excepción 1, solo con NIT).</summary>
    public bool ExceptionRequested { get => _exception; set => Set(ref _exception, value); }

    public bool IsCi => _type.Code == SiatCodes.DocumentCi;

    public bool IsNit => _type.Code == SiatCodes.DocumentNit;

    public bool IsSpecial => IsNit && SiatCodes.IsSpecialDocument(_number);

    public bool CanVerify => _app.Session.Can(PermissionCodes.BillingIssue);

    /// <summary>El botón «Verificar NIT» se muestra solo con NIT y si el usuario puede emitir.</summary>
    public bool ShowVerify => IsNit && CanVerify;

    /// <summary>Resultado de la verificación del NIT en el Padrón (o de la última verificación guardada).</summary>
    public string? NitStatus
    {
        get => _nitStatus;
        private set
        {
            if (Set(ref _nitStatus, value))
            {
                OnPropertyChanged(nameof(HasNitStatus));
            }
        }
    }

    public bool HasNitStatus => _nitStatus is not null;

    public string NitStatusBrush { get => _nitBrush; private set => Set(ref _nitBrush, value); }

    public string NitStatusSoftBrush => _nitBrush + "Soft";

    /// <summary>«Cliente C-0012 · compró antes» (autocompletado).</summary>
    public string? FoundText { get => _foundText; private set => Set(ref _foundText, value); }

    public bool IsLooking { get => _looking; private set => Set(ref _looking, value); }

    public AsyncRelayCommand VerifyNit { get; }

    public RelayCommand<SpecialNit> UseSpecial { get; }

    /// <summary>Autocompletado al salir del número: nombre y correo del cliente que ya compró y su última verificación de NIT.</summary>
    public AsyncRelayCommand Lookup { get; }

    public IReadOnlyList<SpecialNit> Specials => SpecialNits;

    /// <summary>El comprador ya es cliente (código): la caja lo elige en el combo de clientes.</summary>
    public event EventHandler<string>? CustomerFound;

    public bool IsEmpty => _number.Length == 0;

    /// <summary>Datos para el caso de uso (null si no se escribió el número: se factura con los datos del cliente).</summary>
    public FiscalBuyerInput? ToInput() => IsEmpty
        ? null
        : new FiscalBuyerInput(_type.Code, _number, IsCi && _complement.Length > 0 ? _complement : null,
            string.IsNullOrWhiteSpace(_name) ? null : _name.Trim(), string.IsNullOrWhiteSpace(_email) ? null : _email.Trim(),
            IsNit && _exception);

    /// <summary>Guía rápida antes de enviar (la regla que manda es la del dominio).</summary>
    public string? Check()
    {
        if (IsEmpty)
        {
            return null;
        }
        if ((IsCi || IsNit) && !_number.All(char.IsAsciiDigit))
        {
            return $"El {(IsNit ? "NIT" : "CI")} lleva solo números (sin puntos ni guiones).";
        }
        if (_number.Length > 20)
        {
            return "El número de documento es demasiado largo.";
        }
        return null;
    }

    public void Prefill(int documentType, string number, string? complement, string? name, string? email)
    {
        _type = DocumentTypes.FirstOrDefault(t => t.Code == documentType) ?? _type;
        _number = number.Trim();
        _complement = complement ?? string.Empty;
        _name = name ?? string.Empty;
        _email = email ?? string.Empty;
        _exception = false;
        NitStatus = null;
        FoundText = null;
        OnPropertiesChanged(nameof(DocumentType), nameof(DocumentNumber), nameof(Complement), nameof(Name), nameof(Email), nameof(ExceptionRequested),
            nameof(IsCi), nameof(IsNit), nameof(IsEmpty));
    }

    public void Clear()
    {
        Prefill(SiatCodes.DocumentCi, string.Empty, null, null, null);
    }

    private void ApplySpecial(SpecialNit special)
    {
        DocumentType = DocumentTypes.FirstOrDefault(t => t.Code == SiatCodes.DocumentNit) ?? _type;
        _number = special.Code;
        OnPropertyChanged(nameof(DocumentNumber));
        Complement = string.Empty;
        ExceptionRequested = false;
        if (special.DefaultName is { } name)
        {
            Name = name;
        }
        NitStatus = special.Explanation;
        NitStatusBrush = "Info";
        OnPropertyChanged(nameof(NitStatusSoftBrush));
        FoundText = null;
    }

    private async Task LookupAsync()
    {
        if (IsEmpty || !_app.Session.Can(PermissionCodes.BillingIssue) || IsSpecial || Check() is not null)
        {
            return;
        }
        var number = _number;
        IsLooking = true;
        try
        {
            var found = await _app.SendAsync(new FindFiscalBuyerQuery(_type.Code, number, IsCi && _complement.Length > 0 ? _complement : null));
            if (number != _number)
            {
                return;
            }
            if (found.Found)
            {
                if (string.IsNullOrWhiteSpace(_name))
                {
                    Name = found.Name ?? string.Empty;
                }
                if (string.IsNullOrWhiteSpace(_email) && found.Email is { } email)
                {
                    Email = email;
                }
                FoundText = $"Cliente {found.CustomerCode}: ya compró antes";
                if (found.CustomerCode is { } code)
                {
                    CustomerFound?.Invoke(this, code);
                }
            }
            if (found.NitValid is { } valid)
            {
                NitStatus = valid ? "✔ NIT verificado en el Padrón (activo)" : "✖ La última verificación dio NIT no válido";
                NitStatusBrush = valid ? "Success" : "Danger";
                OnPropertyChanged(nameof(NitStatusSoftBrush));
            }
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            System.Diagnostics.Trace.TraceInformation("M-INV · búsqueda del comprador: {0}", ex.Message);
        }
        finally
        {
            IsLooking = false;
        }
    }

    private async Task VerifyNitAsync()
    {
        var nit = long.Parse(_number.Trim(), NumberStyles.None, CultureInfo.InvariantCulture);
        try
        {
            var result = await _app.SendAsync(new VerifyNitCommand(nit));
            NitStatus = result.IsValid
                ? $"✔ NIT activo en el Padrón · {FiscalText.Plain(result.Description)}"
                : $"✖ {FiscalText.Plain(result.Description)} · puede facturar marcando «Facturar aunque el NIT no sea válido».";
            NitStatusBrush = result.IsValid ? "Success" : "Danger";
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            NitStatus = "⚠ No se pudo verificar ahora: " + AppServices.Describe(ex);
            NitStatusBrush = "Warning";
        }
        OnPropertyChanged(nameof(NitStatusSoftBrush));
    }
}

/// <summary>V4.1 · Fila de la lista de documentos fiscales.</summary>
public sealed class FiscalDocumentItem(FiscalDocumentRow r)
{
    public FiscalDocumentRow Row { get; } = r;

    public Guid Id => Row.Id;

    public long Number => Row.Number;

    public string NumberText => Row.Number.ToString(CultureInfo.InvariantCulture);

    public string KindText => FiscalText.Kind(Row.Kind);

    public bool IsNote => Row.Kind == FiscalDocumentKind.CreditDebitNote;

    public DateTime IssuedAt => Row.IssuedAt;

    public string IssuedText => FiscalText.FiscalTime(Row.IssuedAt);

    public string PlaceText => $"{Row.BranchCode} · PV {Row.PointOfSaleCode}";

    public string DateText => Row.IssuedAt.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    /// <summary>Hora fiscal, sucursal y punto de venta (segunda línea de la columna de fecha).</summary>
    public string TimePlaceText => $"{Row.IssuedAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture)} · {PlaceText}";

    public string BuyerName => Row.BuyerName;

    public string BuyerDocument => Row.BuyerDocument;

    public decimal Total => Row.Total;

    public string TotalText => Fmt.Money(Row.Total);

    public FiscalDocumentStatus Status => Row.Status;

    public string StatusText => FiscalText.Status(Row.Status, Row.IsReverted);

    public string StatusBrush => FiscalText.Brush(Row.Status);

    public string StatusSoftBrush => StatusBrush + "Soft";

    public string StatusHelp => FiscalText.StatusHelp(Row.Status);

    public bool IsOffline => Row.EmissionType == SiatCodes.EmissionOffline;

    public string ShortCuf => FiscalText.ShortCuf(Row.Cuf);

    public string SaleText => Row.SaleNumber is { } s ? $"Venta {s}" : string.Empty;

    public string KindGlyph => IsNote ? Glyphs.Undo : Glyphs.Invoice;
}
