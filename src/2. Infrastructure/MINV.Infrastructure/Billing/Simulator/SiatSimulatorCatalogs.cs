using System.Globalization;
using System.Text;
using MINV.Application.Abstractions;
using MINV.Domain.Billing;

namespace MINV.Infrastructure.Billing.Simulator;

/// <summary>
/// V4.1 · Catálogos del simulador del SIN. SON DATOS DE SIMULACIÓN (regla F-16): verosímiles para una ferretería, pero
/// en producción mandan los que devuelve la sincronización real. Los códigos que la investigación documenta están tal
/// cual (tipos de documento de identidad, tipos de punto de venta, tipo de emisión, tipos de factura, 57/58 de unidad,
/// eventos significativos con la numeración del Excel de la Etapa V); los demás son códigos plausibles de prueba.
/// El catálogo de productos del SIN es el subconjunto de ferretería y construcción de la investigación (CSV embebido).
/// </summary>
public static class SiatSimulatorCatalogs
{
    /// <summary>Actividad principal simulada del NIT (CAEB, texto de 7 dígitos).</summary>
    public const string MainActivity = "4752100";

    /// <summary>Actividad secundaria simulada.</summary>
    public const string SecondaryActivity = "4752400";

    private static readonly Lazy<IReadOnlyList<SiatCatalogRow>> ProductRows = new(LoadProducts);

    /// <summary>Textos de los códigos de respuesta que usa el simulador (literales de la tabla oficial, investigación 04 §4;
    /// el 3012 sale de la página de reversión). También forman el catálogo «Mensajes Servicios».</summary>
    public static readonly IReadOnlyDictionary<int, string> Messages = new Dictionary<int, string>
    {
        [901] = "Recepción Pendiente",
        [902] = "Recepción Rechazada",
        [904] = "Recepción Observada",
        [905] = "Anulación Confirmada",
        [906] = "Anulación Rechazada",
        [907] = "Reversión De Anulación Confirmada",
        [908] = "Recepción Validada",
        [909] = "Reversión De Anulación Rechazada",
        [910] = "El Parámetro Ambiente Es Invalido",
        [911] = "El Parámetro Código De Sistema Es Invalido",
        [913] = "Código Único De Inicio De Sistema (Cuis) Invalido",
        [914] = "Código Único De Facturación Diaria (Cufd) Invalido",
        [915] = "El Parámetro Tipo Factura Documento Es Invalido",
        [916] = "El Parámetro Tipo De Emisión Es Invalido",
        [917] = "El Parámetro Modalidad Es Invalido",
        [918] = "El Parámetro Sucursal Es Invalido",
        [919] = "El Parámetro NIT Es Invalido",
        [920] = "El Parámetro Archivo Es Invalido",
        [923] = "El Parámetro Código De Recepción Es Invalido",
        [924] = "La Factura o Nota, No Existe En La Base De Datos Del Sin",
        [925] = "El Parámetro Motivo De Anulación Es Invalido",
        [926] = "Comunicación Exitosa",
        [929] = "El Código Único De Inicio De Sistema (Cuis) No Esta Vigente",
        [930] = "El Código Único De Inicio De Sistema (Cuis) No Corresponde A La Sucursal/Punto Venta",
        [931] = "El Parámetro Código Documento Sector Es Invalido",
        [932] = "El Parámetro Código Documento Sector No Corresponde Al Servicio",
        [933] = "El Punto De Venta Es Inexistente o Invalido",
        [934] = "La Solicitud De Anulación De La Factura o Nota De Crédito-Débito Se Encuentra Fuera De Plazo",
        [935] = "El Parámetro Fecha De Envío Es Invalido",
        [936] = "La Factura o Nota De Crédito-Débito Ya Se Encuentra Anulada",
        [939] = "La Factura o Nota De Crédito - Débito No Cumple Con El Formato Del Xsd Especificado",
        [940] = "El NIT No Tiene Habilitado El Documento Sector",
        [941] = "La Factura o Nota De Crédito - Débito No Se Encuentra Disponible Para Ser Anulada",
        [942] = "El Código De Recepción De Evento Significativo No Se Encuentra En La Base De Datos Del Sin",
        [944] = "El Código De Recepción No Se Encuentra En La Base De Datos Del Sin",
        [947] = "El Parámetro Tipo De Punto De Venta Es Invalido",
        [948] = "El Parámetro Nombre De Punto De Venta No Puede Ser Vacío",
        [949] = "El Parámetro Descripción De Punto De Venta No Puede Ser Vacío",
        [950] = "El Parámetro Código De Evento Significativo No Puede Ser Vacío",
        [951] = "El Parámetro Descripción De Evento Significativo No Puede Ser Vacío",
        [952] = "El Código Único De Factura (Cuf) Ya Se Encuentra Registrado En La Base De Datos Del Sin",
        [953] = "El Código Único De Facturación Diaria (Cufd) No Se Encuentra Vigente",
        [954] = "La Cantidad De Facturas En El Paquete Emitido Por Contingencia Ha Excedido El Máximo Permitido",
        [960] = "El Parámetro Fin De Evento Es Requerido",
        [968] = "La Anulación De La Factura o Nota De Crédito - Débito Ya Se Encuentra Revertida",
        [969] = "El Parámetro Hash Es Invalido",
        [974] = "El Rango De Fechas Del Evento Significativo Para Registrar Es Inválido",
        [976] = "El Código Del Evento Es Incorrecto",
        [981] = "La Factura o Nota De Crédito-Débito No Se Encuentra Disponible Para Reversión",
        [982] = "No Existe Puntos De Venta Asociados",
        [984] = "El Evento Significativo No Corresponde Al Cufd Del Evento Registrado",
        [985] = "La Cantidad De Facturas Es Diferente A La Declarada",
        [986] = "NIT Activo",
        [987] = "NIT Inactivo",
        [989] = "Token Invalido",
        [994] = "NIT Inexistente",
        [1001] = "El NIT Enviado En El XML Es Inexistente O No Corresponde Al Cufd",
        [1002] = "El Código Único De Factura (Cuf) Enviado En El XML Es Invalido",
        [1003] = "El Código Único De Facturación Diaria (Cufd) Enviado En El XML Es Invalido",
        [1004] = "La Sucursal Enviada En El XML No Corresponde A Los Datos Del Cufd",
        [1006] = "El Cufd Enviado No Corresponde Al Evento Asociado Al Paquete Enviado",
        [1008] = "El Punto De Venta Enviado En El XML Es Inexistente O Invalido",
        [1009] = "La Fecha De Emisión Enviada En El XML No Es Valida Para Emisión En Linea",
        [1013] = "El Calculo Del Monto Total Es Erróneo",
        [1014] = "El Calculo Del Monto Total Moneda Es Erróneo",
        [1018] = "El Calculo Del Subtotal Es Erróneo",
        [1029] = "El Monto Total Devuelto Enviado Es Erróneo",
        [1030] = "El Monto Total Original Enviado Es Erróneo",
        [1031] = "El Monto Efectivo De Crédito O Débito Devuelto Enviado Es Erróneo",
        [1040] = "Fecha De Emisión No Se Encuentra En El Rango De Contingencia",
        [1045] = "Valor De Cafc No Valido Para La Factura",
        [1058] = "El Monto Total Sujeto Iva Es Erróneo",
        [3008] = "Advertencia: El Cuis Esta A Punto De Caducar, Genere Un Nuevo Cuis Por Favor",
        [3012] = "La Solicitud De Reversión Se Encuentra Fuera De Plazo",
    };

    /// <summary>Actividades del NIT simulado (tipo P = principal, S = secundaria).</summary>
    public static readonly IReadOnlyList<SiatCatalogRow> Activities =
    [
        new(MainActivity, "VENTA AL POR MENOR DE ARTÍCULOS DE FERRETERÍA, FONTANERÍA Y CALEFACCIÓN", null, "P"),
        new(SecondaryActivity, "VENTA AL POR MENOR DE LADRILLO, MADERA, CEMENTO Y OTROS MATERIALES DE CONSTRUCCIÓN", null, "S"),
    ];

    /// <summary>Actividad ↔ documento sector (Code = sector, Extra = tipo de documento sector, que es también la descripción:
    /// la respuesta del SIN no trae otra).</summary>
    public static readonly IReadOnlyList<SiatCatalogRow> ActivitySectors =
    [
        new("1", "FACTURA", MainActivity, "FACTURA"),
        new("24", "DOCUMENTO AJUSTE", MainActivity, "DOCUMENTO AJUSTE"),
        new("1", "FACTURA", SecondaryActivity, "FACTURA"),
        new("24", "DOCUMENTO AJUSTE", SecondaryActivity, "DOCUMENTO AJUSTE"),
    ];

    /// <summary>Leyendas de la Ley N° 453 (textos reales conocidos) para cada actividad.</summary>
    public static readonly IReadOnlyList<string> LegendTexts =
    [
        "Ley N° 453: Tienes derecho a recibir información sobre las características y contenidos de los servicios que utilices.",
        "Ley N° 453: El proveedor deberá entregar el producto en las modalidades y términos ofertados o convenidos.",
        "Ley N° 453: Está prohibido importar, distribuir o comercializar productos expirados o prontos a expirar.",
        "Ley N° 453: Los productos deben suministrarse en condiciones de inocuidad, calidad y seguridad.",
        "Ley N° 453: El proveedor debe exhibir certificaciones de habilitación o documentos que acrediten las capacidades u ofertas de servicios especializados.",
        "Ley N° 453: Tienes derecho a un trato equitativo sin discriminación en la oferta de productos.",
    ];

    public static IReadOnlyList<SiatCatalogRow> Legends { get; } =
        [.. Activities.SelectMany(a => LegendTexts.Select(t => new SiatCatalogRow(a.Code, t, a.Code)))];

    /// <summary>Productos y servicios del SIN: TODAS las filas del CSV de ferretería y construcción de la investigación.</summary>
    public static IReadOnlyList<SiatCatalogRow> Products => ProductRows.Value;

    /// <summary>Eventos significativos con la numeración del Excel de la Etapa V (investigación 03 §6.2): 1–4 fuera de
    /// línea automático, 5–7 contingencia manual (CAFC).</summary>
    public static readonly IReadOnlyList<SiatCatalogRow> SignificantEvents =
    [
        Code(1, "CORTE DEL SERVICIO DE INTERNET"),
        Code(2, "INACCESIBILIDAD AL SERVICIO WEB DE LA ADMINISTRACIÓN TRIBUTARIA"),
        Code(3, "INGRESO A ZONAS SIN INTERNET POR DESPLIEGUE DE PUNTO DE VENTA EN VEHICULOS AUTOMOTORES"),
        Code(4, "VENTA EN LUGARES SIN INTERNET"),
        Code(5, "CORTE DE SUMINISTRO DE ENERGIA ELECTRICA"),
        Code(6, "VIRUS INFORMÁTICO O FALLA DE SOFTWARE"),
        Code(7, "CAMBIO DE INFRAESTRUCTURA DEL SISTEMA INFORMÁTICO DE FACTURACIÓN O FALLA DE HARDWARE"),
    ];

    /// <summary>Primer código de evento de contingencia MANUAL (CAFC) en la numeración simulada.</summary>
    public const int FirstManualEvent = 5;

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<SiatCatalogRow>> Parametric = new Dictionary<string, IReadOnlyList<SiatCatalogRow>>
    {
        [SiatCatalogNames.ServiceMessages] = [.. Messages.OrderBy(m => m.Key).Select(m => Code(m.Key, m.Value))],
        [SiatCatalogNames.SignificantEvents] = SignificantEvents,
        [SiatCatalogNames.VoidReasons] =
        [
            Code(1, "FACTURA MAL EMITIDA"),
            Code(2, "NOTA DE CREDITO-DEBITO MAL EMITIDA"),
            Code(3, "DATOS DE EMISION INCORRECTOS"),
            Code(4, "FACTURA O NOTA DE CREDITO-DEBITO DEVUELTA"),
        ],
        [SiatCatalogNames.Countries] =
        [
            Code(1, "BOLIVIA"), Code(2, "ARGENTINA"), Code(3, "BRASIL"), Code(4, "CHILE"), Code(5, "PARAGUAY"), Code(6, "PERU"),
        ],
        [SiatCatalogNames.IdentityDocumentTypes] =
        [
            Code(1, "CI - CEDULA DE IDENTIDAD"),
            Code(2, "CEX - CEDULA DE IDENTIDAD DE EXTRANJERO"),
            Code(3, "PAS - PASAPORTE"),
            Code(4, "OD - OTRO DOCUMENTO DE IDENTIDAD"),
            Code(5, "NIT - NÚMERO DE IDENTIFICACIÓN TRIBUTARIA"),
        ],
        [SiatCatalogNames.DocumentSectorTypes] =
        [
            Code(1, "FACTURA COMPRA VENTA"),
            Code(2, "FACTURA DE ALQUILER DE BIENES INMUEBLES"),
            Code(3, "FACTURA COMERCIAL DE EXPORTACION"),
            Code(11, "FACTURA SECTORES EDUCATIVOS"),
            Code(24, "NOTA DE CREDITO-DEBITO"),
            Code(29, "NOTA DE CONCILIACION"),
            Code(35, "FACTURA COMPRA VENTA BONIFICACIONES"),
            Code(41, "FACTURA COMPRA VENTA TASAS"),
            Code(47, "NOTA CREDITO DEBITO DESCUENTOS"),
        ],
        [SiatCatalogNames.EmissionTypes] = [Code(1, "EN LINEA"), Code(2, "FUERA DE LINEA"), Code(3, "MASIVA")],
        [SiatCatalogNames.RoomTypes] = [Code(1, "SIMPLE"), Code(2, "DOBLE"), Code(3, "TRIPLE"), Code(4, "SUITE")],
        [SiatCatalogNames.PaymentMethods] =
        [
            Code(1, "EFECTIVO"), Code(2, "TARJETA"), Code(3, "CHEQUE"), Code(4, "VALES"), Code(5, "OTROS"), Code(6, "PAGO POSTERIOR (CREDITO)"),
            Code(7, "TRANSFERENCIA BANCARIA"), Code(8, "DEPOSITO EN CUENTA"), Code(10, "EFECTIVO - TARJETA"), Code(27, "GIFT-CARD"),
            Code(33, "PAGO ONLINE (QR)"),
        ],
        [SiatCatalogNames.Currencies] = [Code(1, "BOLIVIANO"), Code(2, "DOLAR"), Code(6, "EURO")],
        [SiatCatalogNames.PointOfSaleTypes] =
        [
            Code(1, "PUNTO VENTA COMISIONISTA"), Code(2, "PUNTO VENTA VENTANILLA DE COBRANZA"), Code(3, "PUNTO DE VENTA MOVILES"),
            Code(4, "PUNTO DE VENTA YPFB"), Code(5, "PUNTO DE VENTA CAJEROS"), Code(6, "PUNTO DE VENTA CONJUNTA"),
        ],
        [SiatCatalogNames.InvoiceTypes] =
        [
            Code(1, "FACTURA CON DERECHO A CREDITO FISCAL"), Code(2, "FACTURA SIN DERECHO A CREDITO FISCAL"), Code(3, "DOCUMENTO DE AJUSTE"),
            Code(4, "DOCUMENTO EQUIVALENTE"),
        ],
        [SiatCatalogNames.UnitsOfMeasure] =
        [
            Code(2, "BALDE"), Code(4, "BOLSA"), Code(6, "CAJA"), Code(14, "DOCENA"), Code(16, "GALON"), Code(17, "GRAMO"), Code(21, "JUEGO"),
            Code(22, "KILOGRAMO"), Code(23, "LITRO"), Code(30, "METRO"), Code(31, "METRO CUADRADO"), Code(32, "METRO CUBICO"),
            Code(42, "PAQUETE"), Code(43, "PAR"), Code(47, "PIEZA"), Code(55, "TONELADA"), Code(57, "UNIDAD (BIENES)"),
            Code(58, "UNIDAD (SERVICIOS)"), Code(62, "OTRO"), Code(68, "ROLLO"), Code(70, "BARRA"),
        ],
    };

    /// <summary>Filas de un catálogo (<see cref="SiatCatalogNames"/>, salvo la fecha y hora), o null si no existe.</summary>
    public static IReadOnlyList<SiatCatalogRow>? Rows(string catalog) => catalog switch
    {
        SiatCatalogNames.Activities => Activities,
        SiatCatalogNames.ActivitySectors => ActivitySectors,
        SiatCatalogNames.Legends => Legends,
        SiatCatalogNames.Products => Products,
        _ => Parametric.GetValueOrDefault(catalog),
    };

    /// <summary>¿El código existe en una paramétrica?</summary>
    public static bool Contains(string catalog, int code) =>
        Rows(catalog)?.Any(r => r.Code == code.ToString(CultureInfo.InvariantCulture)) == true;

    /// <summary>Texto de un código de respuesta.</summary>
    public static string MessageText(int code) => Messages.TryGetValue(code, out var text) ? text : $"Código {code}";

    /// <summary>
    /// Lee un CSV separado por «;» (RFC 4180: campos entre comillas con «;», comillas dobles y saltos de línea adentro).
    /// Devuelve las filas sin la cabecera.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> ParseCsv(string text, char separator = ';')
    {
        ArgumentNullException.ThrowIfNull(text);
        var rows = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var start = text.Length > 0 && text[0] == '﻿' ? 1 : 0;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"' && field.Length == 0)
            {
                quoted = true;
            }
            else if (c == separator)
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }
                row.Add(field.ToString());
                field.Clear();
                if (row.Any(f => f.Length > 0))
                {
                    rows.Add(row);
                }
                row = [];
            }
            else
            {
                field.Append(c);
            }
        }
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            if (row.Any(f => f.Length > 0))
            {
                rows.Add(row);
            }
        }
        return rows.Count > 0 ? rows.Skip(1).ToList() : rows;
    }

    private static SiatCatalogRow Code(int code, string description) => new(code.ToString(CultureInfo.InvariantCulture), description);

    private static IReadOnlyList<SiatCatalogRow> LoadProducts()
    {
        var assembly = typeof(SiatSimulatorCatalogs).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith(".productos-sin-ferreteria.csv", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return ParseCsv(reader.ReadToEnd())
            .Where(r => r.Count >= 3 && r[0].Trim().Length > 0)
            .Select(r => new SiatCatalogRow(r[0].Trim(), r[1].Trim(), r[2].Trim()))
            .ToList();
    }
}
