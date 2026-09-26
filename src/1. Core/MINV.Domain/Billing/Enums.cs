namespace MINV.Domain.Billing;

/// <summary>Clase de documento fiscal que emite M-INV. Se guarda como texto.</summary>
public enum FiscalDocumentKind
{
    /// <summary>Factura Compra Venta (sector 1, tipo 1).</summary>
    Invoice,

    /// <summary>Nota Crédito-Débito (sector 24, tipo 3: documento de ajuste).</summary>
    CreditDebitNote,
}

/// <summary>Estado de un documento fiscal frente al SIN (ver docs/architecture/facturacion-siat-v4.1.md §3). Se guarda como texto.</summary>
public enum FiscalDocumentStatus
{
    /// <summary>Emitido en línea (tipo de emisión 1), todavía sin respuesta del SIN.</summary>
    Pending,

    /// <summary>Recepción validada (908) o validado dentro de un paquete.</summary>
    Valid,

    /// <summary>902/904 en línea: no es válido; la venta recibe un documento nuevo.</summary>
    Rejected,

    /// <summary>Se perdió la respuesta al enviarlo en línea (timeout o error del servicio): se re-emite fuera de línea.</summary>
    NoResponse,

    /// <summary>Emitido fuera de línea (tipo de emisión 2) durante un evento significativo.</summary>
    Offline,

    /// <summary>Enviado en un paquete de contingencia, esperando la validación.</summary>
    InPackage,

    /// <summary>La validación del paquete observó este documento.</summary>
    PackageRejected,

    /// <summary>Estaba registrado en el SIN y además se re-emitió fuera de línea: hay que anularlo.</summary>
    DuplicateToVoid,

    /// <summary>Anulado en el SIN (905 o 936).</summary>
    Voided,

    /// <summary>Nunca llegó al SIN y se re-emitió: queda solo como historia.</summary>
    Discarded,
}

/// <summary>Modo de operación de un punto de venta del SIN. Se guarda como texto.</summary>
public enum SiatConnectionMode
{
    Online,
    Offline,
    ManualContingency,
    Recovering,
}

/// <summary>Clase de evento significativo: fuera de línea automático o contingencia manual con CAFC. Se guarda como texto.</summary>
public enum SignificantEventKind
{
    Offline,
    ManualCafc,
}

/// <summary>Estado de un evento significativo. Se guarda como texto.</summary>
public enum SignificantEventStatus
{
    Open,
    Closed,
    Registered,
    PackagesSent,
    Reconciled,
    WithObservations,
}

/// <summary>Estado de un paquete de contingencia. Se guarda como texto.</summary>
public enum FiscalPackageStatus
{
    Sent,
    Validated,
    Observed,
    Rejected,
}

/// <summary>Medio por el que se entregó el documento al comprador. Se guarda como texto.</summary>
public enum FiscalDeliveryChannel
{
    Email,
    Print,
    Pdf,
}

/// <summary>Acción registrada en la bitácora de un documento fiscal. Se guarda como texto.</summary>
public enum FiscalDocumentAction
{
    Issued,
    Sent,
    Accepted,
    Rejected,
    NoResponse,
    Reissued,
    Packaged,
    PackageValidated,
    PackageRejected,
    StatusChecked,
    VoidRequested,
    Voided,
    VoidFailed,
    Reverted,
    RevertFailed,
    Discarded,
    Delivered,
    Printed,
}
