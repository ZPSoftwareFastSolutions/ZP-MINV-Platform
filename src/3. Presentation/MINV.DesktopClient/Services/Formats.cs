using System.Globalization;
using System.Text.RegularExpressions;
using MINV.DesktopClient.Controls;
using MINV.Domain.Inventory;

namespace MINV.DesktopClient.Services;

/// <summary>Formatos en español para la interfaz (números, dinero, fechas, tiempo relativo y nombres de acciones).</summary>
public static partial class Fmt
{
    /// <summary>Cultura de la interfaz: la de Windows si es español; si no, español de Bolivia.</summary>
    public static CultureInfo Culture { get; } = CultureInfo.CurrentCulture.TwoLetterISOLanguageName == "es"
        ? CultureInfo.CurrentCulture
        : CultureInfo.GetCultureInfo("es-BO");

    /// <summary>Símbolo de la moneda de la empresa de la sesión (Bs, $, US$…).</summary>
    public static string CurrencySymbol { get; set; } = "Bs";

    public static int CurrencyDecimals { get; set; } = 2;

    public static string Qty(decimal value) => value.ToString(decimal.Truncate(value) == value ? "#,##0" : "#,##0.###", Culture);

    public static string Qty(decimal value, string unit) => Qty(value) + " " + unit;

    public static string Signed(decimal value) => (value > 0 ? "+" : value < 0 ? "−" : "") + Qty(Math.Abs(value));

    public static string Money(decimal value) => CurrencySymbol + " " + value.ToString("N" + CurrencyDecimals, Culture);

    /// <summary>Dinero abreviado para tarjetas (Bs 1,2 M · Bs 845 k).</summary>
    public static string MoneyShort(decimal value) => Math.Abs(value) switch
    {
        >= 1_000_000_000 => CurrencySymbol + " " + (value / 1_000_000_000).ToString("0.##", Culture) + " mil M",
        >= 1_000_000 => CurrencySymbol + " " + (value / 1_000_000).ToString("0.##", Culture) + " M",
        >= 100_000 => CurrencySymbol + " " + (value / 1000).ToString("0", Culture) + " k",
        _ => Money(value),
    };

    public static string Date(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    public static string Date(DateOnly? date) => date is { } d ? Date(d) : "—";

    public static string LongDate(DateOnly date)
    {
        var text = date.ToString("dddd d 'de' MMMM 'de' yyyy", Culture);
        return char.ToUpper(text[0], Culture) + text[1..];
    }

    public static string DateTime(DateTimeOffset instant) => instant.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    /// <summary>«hace 5 min», «ayer 14:30», «12/09/2026».</summary>
    public static string Relative(DateTimeOffset instant, DateTimeOffset now)
    {
        var span = now - instant;
        if (span < TimeSpan.FromMinutes(1))
        {
            return "hace un momento";
        }
        if (span < TimeSpan.FromHours(1))
        {
            return $"hace {(int)span.TotalMinutes} min";
        }
        var local = instant.ToLocalTime();
        var today = now.ToLocalTime().Date;
        if (local.Date == today)
        {
            return $"hoy {local:HH:mm}";
        }
        if (local.Date == today.AddDays(-1))
        {
            return $"ayer {local:HH:mm}";
        }
        return span < TimeSpan.FromDays(7) ? local.ToString("dddd HH:mm", Culture) : local.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
    }

    public static string Coverage(int? days) => days switch
    {
        null => "—",
        0 => "0 días",
        1 => "1 día",
        > 999 => "+999 días",
        _ => $"{days} días",
    };

    /// <summary>«DEVOLUCIÓN DE CLIENTE» → «Devolución de cliente» (conserva siglas como POS).</summary>
    public static string SentenceCase(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }
        var words = text.Trim().ToLower(Culture).Split(' ');
        for (var i = 0; i < words.Length; i++)
        {
            if (words[i] is "pos" or "iva" or "nit")
            {
                words[i] = words[i].ToUpper(Culture);
            }
        }
        var result = string.Join(' ', words);
        return char.ToUpper(result[0], Culture) + result[1..];
    }

    public static string Initials(string? name)
    {
        var parts = (name ?? "?").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..Math.Min(2, parts[0].Length)].ToUpper(Culture),
            _ => (parts[0][..1] + parts[1][..1]).ToUpper(Culture),
        };
    }

    public static string Greeting(DateTimeOffset now) => now.ToLocalTime().Hour switch
    {
        < 12 => "Buenos días",
        < 19 => "Buenas tardes",
        _ => "Buenas noches",
    };

    /// <summary>Nombre legible (en español) de una acción auditada: comandos de la V3 a la V4.2 y scripts de la V2.1.</summary>
    public static string Action(string action) => Actions.TryGetValue(action, out var text) ? text : SplitWords(action);

    private static readonly IReadOnlyDictionary<string, string> Actions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Inventario, toma física y catálogo
        ["RegisterMovement"] = "Registró un movimiento",
        ["OpenPhysicalCount"] = "Abrió una toma física",
        ["RecordCount"] = "Registró un conteo",
        ["PostPhysicalCount"] = "Contabilizó una toma física",
        ["CancelPhysicalCount"] = "Anuló una toma física",
        ["RemoveCount"] = "Quitó un conteo",
        ["SaveProduct"] = "Guardó un producto",
        ["SetProductImage"] = "Cambió la imagen de un producto",
        ["RemoveProductImage"] = "Quitó la imagen de un producto",
        ["SaveCategory"] = "Guardó una categoría",
        ["SaveSupplier"] = "Guardó un proveedor",
        ["SaveCustomer"] = "Guardó un cliente",
        ["SaveCustomerFiscalIdentity"] = "Guardó los datos de facturación de un cliente",
        // Compras
        ["CreatePurchaseOrder"] = "Creó una orden de compra",
        ["CreateSuggestedPurchaseOrders"] = "Creó las órdenes del pedido sugerido",
        ["ApprovePurchaseOrder"] = "Aprobó una orden de compra",
        ["CancelPurchaseOrder"] = "Anuló una orden de compra",
        ["ReceivePurchaseOrder"] = "Recibió una orden de compra",
        ["RegisterSupplierInvoice"] = "Registró una factura de proveedor",
        // Caja y ventas
        ["OpenPosSession"] = "Abrió caja",
        ["ClosePosSession"] = "Cerró caja",
        ["Checkout"] = "Cobró una venta",
        ["VoidSale"] = "Anuló una venta",
        ["CreateSalesReturn"] = "Registró una devolución",
        ["CreateExternalOrder"] = "Registró un pedido de la tienda en línea",
        // Contabilidad
        ["CreateAccount"] = "Creó una cuenta contable",
        ["CreateJournalEntry"] = "Registró un asiento",
        // Usuarios, empresa y sucursales
        ["Login"] = "Inició sesión",
        ["Logout"] = "Cerró sesión",
        ["ChangePassword"] = "Cambió su contraseña",
        ["ResetUserPassword"] = "Restableció la contraseña de un usuario",
        ["SaveUser"] = "Guardó un usuario",
        ["AssignUserBranches"] = "Asignó sucursales a un usuario",
        ["SelectBranch"] = "Cambió de sucursal activa",
        ["UpdateCompanySettings"] = "Cambió la configuración de la empresa",
        ["CreateBranch"] = "Creó una sucursal",
        ["UpdateBranch"] = "Modificó una sucursal",
        // Transferencias
        ["CreateTransfer"] = "Solicitó una transferencia",
        ["DispatchTransfer"] = "Despachó una transferencia",
        ["ReceiveTransfer"] = "Recibió una transferencia",
        ["CancelTransfer"] = "Anuló una transferencia",
        // Integraciones
        ["CreateApiKey"] = "Creó una API Key",
        ["RevokeApiKey"] = "Revocó una API Key",
        ["CreateWebhook"] = "Registró un webhook",
        ["DisableWebhook"] = "Desactivó un webhook",
        ["RotateWebhookSecret"] = "Rotó el secreto de un webhook",
        // Facturación SIAT
        ["SaveSiatProfile"] = "Guardó la conexión con el SIN",
        ["SaveSiatSettings"] = "Guardó la configuración de la facturación",
        ["SaveSiatBranch"] = "Guardó una sucursal del Padrón",
        ["SaveMailSettings"] = "Guardó el correo de envío de facturas",
        ["PrepareSiat"] = "Preparó la facturación (CUIS, CUFD y catálogos)",
        ["RequestCuis"] = "Solicitó un CUIS",
        ["RequestCufd"] = "Solicitó un CUFD",
        ["SyncSiatCatalogs"] = "Sincronizó los catálogos del SIN",
        ["CheckSiatCommunication"] = "Verificó la comunicación con el SIN",
        ["RegisterSiatPointOfSale"] = "Registró un punto de venta en el SIN",
        ["CloseSiatPointOfSale"] = "Cerró un punto de venta en el SIN",
        ["LinkPointOfSaleRegister"] = "Vinculó una caja a un punto de venta",
        ["SaveProductHomologation"] = "Homologó productos con el SIN",
        ["SaveUnitHomologation"] = "Homologó una unidad con el SIN",
        ["SavePaymentMethodHomologation"] = "Homologó un medio de pago con el SIN",
        ["DispatchFiscalDocuments"] = "Envió documentos fiscales al SIN",
        ["RunSiatWork"] = "Ejecutó el trabajo automático de la facturación",
        ["CheckFiscalDocumentStatus"] = "Verificó un documento fiscal en el SIN",
        ["VoidFiscalDocument"] = "Anuló un documento fiscal",
        ["RevertFiscalVoid"] = "Revirtió una anulación",
        ["ReissueFiscalDocument"] = "Re-emitió un documento fiscal",
        ["SendFiscalDocumentEmail"] = "Envió una factura por correo",
        ["RecordFiscalDelivery"] = "Registró la entrega de una factura",
        ["GoOffline"] = "Pasó a fuera de línea",
        ["RecoverPointOfSale"] = "Recuperó un punto de venta (fuera de línea)",
        ["StartManualContingency"] = "Inició una contingencia manual (CAFC)",
        ["EndContingency"] = "Cerró una contingencia",
        ["RegisterContingencyCode"] = "Registró un código CAFC",
        ["TranscribeManualInvoice"] = "Transcribió una factura manual (CAFC)",
        ["VerifyNit"] = "Verificó un NIT en el SIN",
        // Edición Tecnología (V4.2)
        ["SaveSpecDefinition"] = "Guardó una especificación",
        ["SaveProductTech"] = "Guardó la ficha técnica de un producto",
        ["RegisterStockSerials"] = "Registró series de unidades en stock",
        ["DisposeSerial"] = "Dio destino a una serie",
        ["OpenWarrantyClaim"] = "Abrió un caso de garantía (RMA)",
        ["MoveWarrantyClaim"] = "Cambió el estado de un caso RMA",
        ["AddWarrantyClaimNote"] = "Agregó una nota a un caso RMA",
        ["IssueWarrantyReplacement"] = "Repuso una unidad en garantía",
        ["SavePcBuild"] = "Guardó un armado de PC",
        ["CancelPcBuild"] = "Anuló un armado de PC",
        ["SellPcBuild"] = "Vendió un armado de PC",
    };

    public static string Outcome(MINV.Domain.Iam.AuditOutcome outcome) => outcome switch
    {
        MINV.Domain.Iam.AuditOutcome.Succeeded => "Correcto",
        MINV.Domain.Iam.AuditOutcome.Rejected => "Rechazado",
        _ => "Falló",
    };

    private static string SplitWords(string text)
    {
        var words = CamelCase().Replace(text.Replace(".ts", "", StringComparison.OrdinalIgnoreCase), " $1").Trim();
        return words.Length == 0 ? text : char.ToUpper(words[0], Culture) + words[1..].ToLower(Culture);
    }

    [GeneratedRegex("(?<=[a-záéíóúñ])([A-ZÁÉÍÓÚÑ])")]
    private static partial Regex CamelCase();

    /// <summary>
    /// Lee una cantidad tal como la escribe una persona: acepta coma o punto decimal («2,5» o «2.5») y separadores de
    /// miles si hay ambos («1.234,5» o «1,234.5»). Nunca interpreta «2.5» como 25.
    /// </summary>
    public static bool TryParseQuantity(string? text, out decimal value)
    {
        value = 0;
        var t = (text ?? "").Trim().Replace(" ", "", StringComparison.Ordinal);
        if (t.Length == 0)
        {
            return false;
        }
        var lastComma = t.LastIndexOf(',');
        var lastDot = t.LastIndexOf('.');
        if (lastComma >= 0 && lastDot >= 0)
        {
            var decimalSep = lastComma > lastDot ? ',' : '.';
            var thousands = decimalSep == ',' ? "." : ",";
            t = t.Replace(thousands, "", StringComparison.Ordinal).Replace(decimalSep, '.');
        }
        else
        {
            t = t.Replace(',', '.');
        }
        return decimal.TryParse(t, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Clave de paleta del color de un estado del semáforo.</summary>
    public static string StatusBrushKey(StockStatusCode status) => status switch
    {
        StockStatusCode.Inconsistent => "StatusInconsistent",
        StockStatusCode.OutOfStock => "StatusOutOfStock",
        StockStatusCode.Critical => "StatusCritical",
        StockStatusCode.Low => "StatusLow",
        StockStatusCode.Optimal => "StatusOptimal",
        StockStatusCode.Overstock => "StatusOverstock",
        _ => "StatusInactive",
    };

    /// <summary>Nombre del estado en minúsculas para frases («3 productos agotados»).</summary>
    public static string StatusName(StockStatusCode status) => status switch
    {
        StockStatusCode.Inconsistent => "Inconsistente",
        StockStatusCode.OutOfStock => "Agotado",
        StockStatusCode.Critical => "Crítico",
        StockStatusCode.Low => "Bajo",
        StockStatusCode.Optimal => "Óptimo",
        StockStatusCode.Overstock => "Sobrestock",
        _ => "Inactivo",
    };

    /// <summary>Ícono de un tipo de movimiento según su signo y dominio.</summary>
    public static string MovementGlyph(string typeCode, short factor) => typeCode switch
    {
        MovementTypeCodes.InitialBalance => Glyphs.Flag,
        MovementTypeCodes.AdjustmentIn or MovementTypeCodes.AdjustmentOut => Glyphs.Wrench,
        MovementTypeCodes.Sale or MovementTypeCodes.Issue => Glyphs.Cart,
        MovementTypeCodes.SaleReturn or MovementTypeCodes.PurchaseReturn => Glyphs.Refresh,
        MovementTypeCodes.TransferIn or MovementTypeCodes.TransferOut => Glyphs.Swap,
        _ => factor > 0 ? Glyphs.Download : Glyphs.Export,
    };
}
