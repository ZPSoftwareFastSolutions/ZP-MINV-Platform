namespace MINV.Domain.Billing;

/// <summary>
/// V4.1 · Códigos fijos del SIAT que usa M-INV (modalidad Facturación Computarizada en Línea). Los valores de los
/// catálogos (métodos de pago, unidades, motivos de anulación, eventos…) NO se fijan aquí: salen de la sincronización
/// diaria (regla F-07). Solo están los códigos que la documentación del SIN publica como constantes de protocolo.
/// </summary>
public static class SiatCodes
{
    // ---------------------------------------------------------------- ambiente y modalidad
    public const int EnvironmentProduction = 1;
    public const int EnvironmentTest = 2;

    public const int ModalityElectronic = 1;
    public const int ModalityComputerized = 2;

    // ---------------------------------------------------------------- tipo de emisión (codigoEmision)
    public const int EmissionOnline = 1;
    public const int EmissionOffline = 2;
    public const int EmissionMassive = 3;

    // ---------------------------------------------------------------- tipo de factura / documento de ajuste
    public const int InvoiceWithTaxCredit = 1;
    public const int InvoiceWithoutTaxCredit = 2;
    public const int AdjustmentDocument = 3;

    // ---------------------------------------------------------------- documentos sector
    public const int SectorPurchaseSale = 1;
    public const int SectorCreditDebitNote = 24;

    // ---------------------------------------------------------------- tipo de documento de identidad (XSD: 1 a 5)
    public const int DocumentCi = 1;
    public const int DocumentCex = 2;
    public const int DocumentPassport = 3;
    public const int DocumentOther = 4;
    public const int DocumentNit = 5;

    /// <summary>NIT especiales (van con tipo NIT y código de excepción 1).</summary>
    public const string SpecialConsulates = "99001";
    public const string SpecialTaxControl = "99002";
    public const string SpecialMinorSales = "99003";

    public static bool IsSpecialDocument(string number) =>
        number is SpecialConsulates or SpecialTaxControl or SpecialMinorSales;

    // ---------------------------------------------------------------- tipos de punto de venta
    public const int PointOfSaleCashier = 5;

    // ---------------------------------------------------------------- unidad de medida de servicios
    public const int UnitService = 58;

    // ---------------------------------------------------------------- códigos de respuesta del SIN
    public const int ReceptionPending = 901;
    public const int ReceptionRejected = 902;
    public const int ReceptionProcessed = 903;
    public const int ReceptionObserved = 904;
    public const int VoidConfirmed = 905;
    public const int VoidRejected = 906;
    public const int RevertConfirmed = 907;
    public const int ReceptionValidated = 908;
    public const int RevertRejected = 909;
    public const int CommunicationOk = 926;
    public const int VoidOutOfTime = 934;
    public const int AlreadyVoided = 936;
    public const int RevertConfirmedAlt = 978;
    public const int NitActive = 986;
    public const int NitInactive = 987;
    public const int NitNotFound = 994;
    public const int InvalidToken = 989;
    public const int CuisAboutToExpire = 3008;

    /// <summary>Límites de los paquetes de contingencia.</summary>
    public const int MaxDocumentsPerPackage = 500;
    public const int MaxLinesPerDocument = 500;

    /// <summary>IVA incluido en el precio (13 %): base del crédito o débito fiscal.</summary>
    public const decimal VatRate = 0.13m;

    /// <summary>Impuesto a las Transacciones (3 % de los ingresos brutos).</summary>
    public const decimal TransactionTaxRate = 0.03m;
}
