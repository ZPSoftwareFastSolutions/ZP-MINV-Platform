using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using MINV.Application.Abstractions;
using MINV.Domain.Billing;
using MINV.Infrastructure.Billing.Soap;

namespace MINV.Infrastructure.Billing.Simulator;

/// <summary>V4.1 · Servicios de facturación del simulador: recepción individual, paquetes, anulación, reversión y estado.</summary>
public sealed partial class SiatSimulatorEngine
{
    /// <summary>Tamaño máximo descomprimido que acepta el simulador (defensa contra archivos desbordados).</summary>
    public const int MaxUncompressedBytes = 200 * 1024 * 1024;

    /// <summary>Resultado del control de un XML: errores (vacío = válido) y el documento listo para registrar.</summary>
    private sealed record XmlCheck(List<SiatMessage> Errors, SimDocument? Document);

    // ================================================================================================ recepción individual
    /// <summary>
    /// recepcionFactura / recepcionDocumentoAjuste (emisión 1). Valida token, parámetros, documento sector acorde al recurso
    /// (932), CUIS del lugar, CUFD emitido y vigente, hash SHA-256 del GZIP (969), GZIP legible (920), XML contra el XSD
    /// oficial (939), coherencia del XML con la solicitud, CUF (decodificable, coherente y terminado en el código de control
    /// de ESE CUFD), fórmulas y CUF duplicado (952). Éxito: 908 con código de recepción; rechazo: 902 con los mensajes.
    /// </summary>
    public SiatReply ReceiveDocument(string? token, SiatSimulatorDocumentCall call, byte[]? file, string? hash, DateTime? sentAt)
    {
        ArgumentNullException.ThrowIfNull(call);
        EnsureAvailable();
        SimCufd cufd;
        lock (_gate)
        {
            if (DocumentCallError(token, call, SiatCodes.EmissionOnline) is { } error)
            {
                return Rejected(error);
            }
            cufd = FindCufd(call.Caller, call.Cufd)!;
        }
        if (sentAt is null)
        {
            return Rejected(Message(935));
        }
        if (FileError(file, hash) is { } fileError)
        {
            return Rejected(fileError);
        }
        if (Gunzip(file!) is not { } xml)
        {
            return Rejected(Message(920));
        }
        var check = CheckXml(xml, call, cufd, SiatCodes.EmissionOnline, null, null, null);
        if (check.Errors.Count > 0)
        {
            return Rejected([.. check.Errors]);
        }
        lock (_gate)
        {
            var document = check.Document!;
            var key = DocumentKey(document.Environment, document.Nit, document.Cuf);
            if (_documents.ContainsKey(key))
            {
                return Rejected(Message(952));
            }
            _documents[key] = document;
            SaveState();
            return new SiatReply(true, SiatCodes.ReceptionValidated, "VALIDADA", document.ReceptionCode, []);
        }
    }

    // ================================================================================================ paquetes
    /// <summary>
    /// recepcionPaqueteFactura (emisión 2): evento registrado para ese lugar (<paramref name="eventReceptionCode"/> = código
    /// de recepción del evento), CUFD nuevo vigente, GZIP(TAR) legible, hash, <c>cantidadFacturas</c> exacta (985) y ≤ 500.
    /// Responde 901 con el código de recepción del paquete; cada XML se controla (tipo de emisión 2 en el CUF, fecha dentro del
    /// evento → 1040, CAFC si el evento es manual → 1045…) y el resultado queda para la validación.
    /// </summary>
    public SiatReply ReceivePackage(string? token, SiatSimulatorDocumentCall call, byte[]? file, string? hash, DateTime? sentAt, int documentCount,
        string? eventReceptionCode, string? cafc)
    {
        ArgumentNullException.ThrowIfNull(call);
        EnsureAvailable();
        SimEvent evt;
        SimCufd eventCufd;
        lock (_gate)
        {
            var error = DocumentCallError(token, call, SiatCodes.EmissionOffline)
                        ?? (call.Resource == SiatResource.Adjustment ? Message(932) : null);
            if (error is not null)
            {
                return Rejected(error);
            }
            var found = FindEvent(call.Caller, call.Place, eventReceptionCode);
            if (found is null || FindCufd(call.Caller, found.EventCufd) is not { } used)
            {
                return Rejected(Message(942));
            }
            evt = found;
            eventCufd = used;
        }
        if (sentAt is null)
        {
            return Rejected(Message(935));
        }
        if (FileError(file, hash) is { } fileError)
        {
            return Rejected(fileError);
        }
        if (Gunzip(file!) is not { } tar || ReadTar(tar) is not { } entries)
        {
            return Rejected(Message(920));
        }
        if (entries.Count > SiatCodes.MaxDocumentsPerPackage)
        {
            return Rejected(Message(954));
        }
        if (documentCount != entries.Count)
        {
            return Rejected(Message(985));
        }
        var checks = entries.Select((xml, i) => CheckXml(xml, call, eventCufd, SiatCodes.EmissionOffline, i + 1, evt, cafc)).ToList();
        lock (_gate)
        {
            var errors = new List<SimMessage>();
            var inPackage = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < checks.Count; i++)
            {
                if (checks[i].Errors.Count > 0)
                {
                    errors.AddRange(checks[i].Errors.Select(SimMessage.From));
                    continue;
                }
                var document = checks[i].Document!;
                var key = DocumentKey(document.Environment, document.Nit, document.Cuf);
                if (_documents.ContainsKey(key) || !inPackage.Add(key))
                {
                    errors.Add(SimMessage.From(Message(952, i + 1)));
                    continue;
                }
                _documents[key] = document;
            }
            var package = new SimPackage
            {
                ReceptionCode = Guid.NewGuid().ToString(),
                DocumentSector = call.DocumentSector,
                EventReceptionCode = evt.ReceptionCode,
                Count = entries.Count,
                Errors = errors,
                ReceivedAt = UtcNow,
            };
            package.Place(call.Caller, call.Place);
            _state.Packages.Add(package);
            SaveState();
            return new SiatReply(true, SiatCodes.ReceptionPending, "PENDIENTE", package.ReceptionCode, []);
        }
    }

    /// <summary>validacionRecepcionPaqueteFactura: 908 si todos los documentos están bien; 904 con los mensajes por
    /// <c>numeroArchivo</c> (base 1) si algunos fallan; 902 si fallan todos.</summary>
    public SiatReply ValidatePackage(string? token, SiatSimulatorDocumentCall call, string? receptionCode)
    {
        ArgumentNullException.ThrowIfNull(call);
        EnsureAvailable();
        lock (_gate)
        {
            if (DocumentCallError(token, call, SiatCodes.EmissionOffline) is { } error)
            {
                return Rejected(error);
            }
            if (string.IsNullOrWhiteSpace(receptionCode))
            {
                return Rejected(Message(923));
            }
            var package = _state.Packages.FirstOrDefault(p => p.ReceptionCode == receptionCode.Trim() && p.IsAt(call.Caller, call.Place)
                                                              && p.DocumentSector == call.DocumentSector);
            if (package is null)
            {
                return Rejected(Message(944));
            }
            var messages = package.Errors.Select(m => m.ToMessage()).ToList();
            if (messages.Count == 0)
            {
                return new SiatReply(true, SiatCodes.ReceptionValidated, "VALIDADA", package.ReceptionCode, []);
            }
            var failed = messages.Select(m => m.FileNumber).Distinct().Count();
            return failed >= package.Count
                ? new SiatReply(false, SiatCodes.ReceptionRejected, "RECHAZADA", package.ReceptionCode, messages)
                : new SiatReply(true, SiatCodes.ReceptionObserved, "OBSERVADA", package.ReceptionCode, messages);
        }
    }

    // ================================================================================================ anulación y reversión
    /// <summary>anulacionFactura / anulacionDocumentoAjuste: 905; 925 motivo inexistente; 924 no existe; 936 ya anulada;
    /// 941 revertida (no se vuelve a anular); 934 pasado el fin del día 9 del mes siguiente a la emisión (hora de Bolivia).</summary>
    public SiatReply VoidDocument(string? token, SiatSimulatorDocumentCall call, string? cuf, int reasonCode)
    {
        ArgumentNullException.ThrowIfNull(call);
        EnsureAvailable();
        lock (_gate)
        {
            var document = FindDocument(call, cuf);
            var error = DocumentCallError(token, call, SiatCodes.EmissionOnline)
                        ?? (SiatSimulatorCatalogs.Contains(SiatCatalogNames.VoidReasons, reasonCode) ? null : Message(925))
                        ?? (document is null ? Message(924) : null)
                        ?? (document!.Voided ? Message(SiatCodes.AlreadyVoided) : null)
                        ?? (document.Reverted ? Message(941) : null)
                        ?? (BoliviaNow > FiscalRules.VoidDeadline(document.IssuedAt) ? Message(SiatCodes.VoidOutOfTime) : null);
            if (error is not null)
            {
                return new SiatReply(false, SiatCodes.VoidRejected, "ANULACION RECHAZADA", null, [error]);
            }
            document!.Voided = true;
            document.VoidReason = reasonCode;
            SaveState();
            return new SiatReply(true, SiatCodes.VoidConfirmed, "ANULACION CONFIRMADA", null, []);
        }
    }

    /// <summary>reversionAnulacionFactura / reversionAnulacionDocumentoAjuste: 907 una sola vez; 968 si ya se revirtió; 981 si
    /// no está anulada; 3012 fuera de plazo (misma regla del día 9).</summary>
    public SiatReply RevertVoid(string? token, SiatSimulatorDocumentCall call, string? cuf)
    {
        ArgumentNullException.ThrowIfNull(call);
        EnsureAvailable();
        lock (_gate)
        {
            var document = FindDocument(call, cuf);
            var error = DocumentCallError(token, call, SiatCodes.EmissionOnline)
                        ?? (document is null ? Message(924) : null)
                        ?? (document!.Reverted ? Message(968) : null)
                        ?? (!document.Voided ? Message(981) : null)
                        ?? (BoliviaNow > FiscalRules.VoidDeadline(document.IssuedAt) ? Message(3012) : null);
            if (error is not null)
            {
                return new SiatReply(false, SiatCodes.RevertRejected, "REVERSION DE ANULACION RECHAZADA", null, [error]);
            }
            document!.Voided = false;
            document.Reverted = true;
            SaveState();
            return new SiatReply(true, SiatCodes.RevertConfirmed, "REVERSION DE ANULACION CONFIRMADA", null, []);
        }
    }

    /// <summary>verificacionEstadoFactura / verificacionEstadoDocumentoAjuste: 908 «VALIDA», 905 «ANULADA»; si no existe,
    /// transacción false con 924.</summary>
    public SiatReply CheckDocumentStatus(string? token, SiatSimulatorDocumentCall call, string? cuf)
    {
        ArgumentNullException.ThrowIfNull(call);
        EnsureAvailable();
        lock (_gate)
        {
            if (DocumentCallError(token, call, SiatCodes.EmissionOnline) is { } error)
            {
                return new SiatReply(false, null, null, null, [error]);
            }
            var document = FindDocument(call, cuf);
            if (document is null)
            {
                return new SiatReply(false, null, null, null, [Message(924)]);
            }
            return document.Voided
                ? new SiatReply(true, SiatCodes.VoidConfirmed, "ANULADA", document.ReceptionCode, [])
                : new SiatReply(true, SiatCodes.ReceptionValidated, "VALIDA", document.ReceptionCode, []);
        }
    }

    // ================================================================================================ controles
    /// <summary>Parámetros comunes de los servicios de facturación (con el lugar, el CUIS y el CUFD vigente de quien llama).</summary>
    private SiatMessage? DocumentCallError(string? token, SiatSimulatorDocumentCall call, int emission) =>
        CallerError(token, call.Caller, true)
        ?? (call.Emission != emission ? Message(916) : null)
        ?? SectorError(call)
        ?? PlaceError(call.Caller, call.Place)
        ?? CuisError(call.Caller, call.Place, call.Cuis, false)
        ?? CufdError(call.Caller, call.Place, call.Cufd, true);

    /// <summary>Documento sector acorde al recurso (932) y al tipo (915); el NIT simulado solo tiene habilitados 1 y 24 (940).</summary>
    private static SiatMessage? SectorError(SiatSimulatorDocumentCall call)
    {
        if (call.DocumentSector is < 1 or > 99)
        {
            return Message(931);
        }
        var adjustment = call.DocumentSector is SiatCodes.SectorCreditDebitNote or 29 or 47 or 48;
        var routed = call.Resource switch
        {
            SiatResource.PurchaseSale => call.DocumentSector == SiatCodes.SectorPurchaseSale,
            SiatResource.Adjustment => adjustment,
            SiatResource.Computerized => call.DocumentSector != SiatCodes.SectorPurchaseSale && !adjustment,
            _ => false,
        };
        if (!routed)
        {
            return Message(932);
        }
        var expectedType = adjustment ? SiatCodes.AdjustmentDocument : SiatCodes.InvoiceWithTaxCredit;
        if (call.DocumentType != expectedType)
        {
            return Message(915);
        }
        return call.DocumentSector is SiatCodes.SectorPurchaseSale or SiatCodes.SectorCreditDebitNote ? null : Message(940);
    }

    /// <summary>Archivo presente (920) y hash = SHA-256 hexadecimal minúscula de esos bytes (969).</summary>
    private static SiatMessage? FileError(byte[]? file, string? hash)
    {
        if (file is null || file.Length == 0)
        {
            return Message(920);
        }
        var expected = Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant();
        return string.Equals(expected, hash?.Trim(), StringComparison.Ordinal) ? null : Message(969);
    }

    private SimEvent? FindEvent(SiatSimulatorCaller caller, SiatPlace place, string? receptionCode) =>
        string.IsNullOrWhiteSpace(receptionCode)
            ? null
            : _state.Events.FirstOrDefault(e => e.ReceptionCode == receptionCode.Trim() && e.IsAt(caller, place));

    private SimDocument? FindDocument(SiatSimulatorDocumentCall call, string? cuf) =>
        !string.IsNullOrWhiteSpace(cuf)
        && _documents.TryGetValue(DocumentKey(call.Caller.Environment, call.Caller.Nit, cuf.Trim()), out var document)
        && document.DocumentSector == call.DocumentSector
            ? document
            : null;

    private static SiatReply Rejected(params SiatMessage[] messages) =>
        new(false, SiatCodes.ReceptionRejected, "RECHAZADA", null, messages);

    /// <summary>
    /// Control de un XML (individual o dentro de un paquete): XSD, coherencia con la solicitud, CUF, fechas, CAFC y fórmulas.
    /// <paramref name="cufd"/> es el CUFD con el que se emitió (el de la solicitud en línea, el del evento en un paquete).
    /// </summary>
    private XmlCheck CheckXml(byte[] bytes, SiatSimulatorDocumentCall call, SimCufd cufd, int emission, int? fileNumber, SimEvent? evt, string? cafc)
    {
        var errors = new List<SiatMessage>();
        var xml = DecodeUtf8(bytes);
        var schemaErrors = SiatSchemas.Validate(xml, call.DocumentSector);
        if (schemaErrors.Count > 0)
        {
            errors.Add(Message(939, fileNumber, null, string.Join(" | ", schemaErrors.Take(3))));
            return new XmlCheck(errors, null);
        }
        XElement root;
        try
        {
            root = SiatSoapEnvelope.Load(xml).Root!;
        }
        catch (XmlException ex)
        {
            errors.Add(Message(939, fileNumber, null, ex.Message));
            return new XmlCheck(errors, null);
        }
        var header = root.Element("cabecera")!;
        var nit = Long(header, "nitEmisor");
        var branch = Int(header, "codigoSucursal");
        var pointOfSale = Int(header, "codigoPuntoVenta") ?? 0;
        var sector = Int(header, "codigoDocumentoSector");
        var number = Long(header, call.DocumentSector == SiatCodes.SectorCreditDebitNote ? "numeroNotaCreditoDebito" : "numeroFactura");
        var issuedAt = DateTime.TryParse(Value(header, "fechaEmision"), CultureInfo.InvariantCulture, DateTimeStyles.None,
            out var parsed) ? DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified) : (DateTime?)null;
        var cuf = Value(header, "cuf") ?? string.Empty;

        // Coherencia del XML con la solicitud (02 §3.6)
        Add(errors, nit != call.Caller.Nit, 1001, fileNumber, "nitEmisor distinto del NIT de la solicitud");
        var expectedCufd = evt?.EventCufd ?? call.Cufd?.Trim();
        Add(errors, Value(header, "cufd") != expectedCufd, evt is null ? 1003 : 1006, fileNumber,
            evt is null ? "el cufd del XML no es el de la solicitud" : "el cufd del XML no es el CUFD del evento");
        Add(errors, branch != call.Place.BranchCode, 1004, fileNumber, "codigoSucursal distinto del de la solicitud");
        Add(errors, pointOfSale != call.Place.PointOfSaleCode, 1008, fileNumber, "codigoPuntoVenta distinto del de la solicitud");
        Add(errors, sector != call.DocumentSector, 931, fileNumber, "codigoDocumentoSector distinto del de la solicitud");

        // CUF: decodificable, coherente con el XML y terminado en el código de control de ESE CUFD
        var parts = cuf.EndsWith(cufd.ControlCode, StringComparison.Ordinal) ? Cuf.DecodeFull(cuf, cufd.ControlCode) : null;
        if (parts is null)
        {
            errors.Add(Message(1002, fileNumber, null, "no se puede decodificar o no termina en el código de control del CUFD"));
        }
        else
        {
            var mismatches = new List<string>();
            AddIf(mismatches, parts.Nit != nit, "NIT");
            AddIf(mismatches, issuedAt is null || parts.IssuedAt != issuedAt, "fecha de emisión");
            AddIf(mismatches, parts.Modality != SiatCodes.ModalityComputerized, "modalidad");
            AddIf(mismatches, parts.EmissionType != emission, "tipo de emisión");
            AddIf(mismatches, parts.DocumentType != call.DocumentType, "tipo de factura/documento");
            AddIf(mismatches, parts.DocumentSector != call.DocumentSector, "documento sector");
            AddIf(mismatches, parts.Number != number, "número");
            AddIf(mismatches, parts.BranchCode != branch, "sucursal");
            AddIf(mismatches, parts.PointOfSaleCode != pointOfSale, "punto de venta");
            if (mismatches.Count > 0)
            {
                errors.Add(Message(1002, fileNumber, null, "no coincide con el XML en " + string.Join(", ", mismatches)));
            }
        }

        // Fechas: en línea no en el futuro; fuera de línea dentro del evento
        if (evt is null)
        {
            Add(errors, issuedAt is null || issuedAt > BoliviaNow + ClockTolerance, 1009, fileNumber, "fecha de emisión posterior a la hora del SIN");
        }
        else
        {
            Add(errors, issuedAt is null || issuedAt < evt.StartedAt || issuedAt > evt.EndedAt, 1040, fileNumber, null);
            if (evt.IsManual)
            {
                var xmlCafc = Value(header, "cafc");
                Add(errors, string.IsNullOrWhiteSpace(xmlCafc) || (!string.IsNullOrWhiteSpace(cafc) && xmlCafc != cafc.Trim()), 1045, fileNumber,
                    "el evento es de contingencia manual: el XML debe llevar el CAFC del paquete");
            }
        }

        // Fórmulas (05 §3 y §4)
        CheckFormulas(root, header, call.DocumentSector, fileNumber, errors);

        if (errors.Count > 0)
        {
            return new XmlCheck(errors, null);
        }
        var document = new SimDocument
        {
            Cuf = cuf,
            DocumentSector = call.DocumentSector,
            DocumentType = call.DocumentType,
            Emission = emission,
            Number = number ?? 0,
            IssuedAt = issuedAt!.Value,
            ReceptionCode = Guid.NewGuid().ToString(),
            ReceivedAt = UtcNow,
        };
        document.Place(call.Caller, call.Place);
        return new XmlCheck(errors, document);
    }

    /// <summary>V1 subtotal por línea (1018), V2 monto total (1013), V3 monto en moneda (1014), V4/V5 sujeto a IVA (1058);
    /// en la nota: N1 original (1030), N2 devuelto (1029) y N3 crédito-débito efectivo (1031). Tolerancia 0,01.</summary>
    private static void CheckFormulas(XElement root, XElement header, int documentSector, int? fileNumber, List<SiatMessage> errors)
    {
        var lines = root.Elements("detalle").ToList();
        decimal sum = 0, original = 0, returned = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var subtotal = Dec(line, "subTotal") ?? 0;
            var expected = (Dec(line, "cantidad") ?? 0) * (Dec(line, "precioUnitario") ?? 0) - (Dec(line, "montoDescuento") ?? 0);
            if (Math.Abs(subtotal - expected) > Tolerance)
            {
                errors.Add(Message(1018, fileNumber, i + 1));
            }
            sum += subtotal;
            switch (Int(line, "codigoDetalleTransaccion"))
            {
                case 1:
                    original += subtotal;
                    break;
                case 2:
                    returned += subtotal;
                    break;
            }
        }
        if (documentSector == SiatCodes.SectorCreditDebitNote)
        {
            var devuelto = Dec(header, "montoTotalDevuelto") ?? 0;
            Add(errors, Math.Abs((Dec(header, "montoTotalOriginal") ?? 0) - original) > Tolerance, 1030, fileNumber, null);
            Add(errors, Math.Abs(devuelto - (returned - (Dec(header, "montoDescuentoCreditoDebito") ?? 0))) > Tolerance, 1029, fileNumber, null);
            Add(errors, Math.Abs((Dec(header, "montoEfectivoCreditoDebito") ?? 0) - FiscalRules.Vat(devuelto)) > Tolerance, 1031, fileNumber, null);
            return;
        }
        var total = Dec(header, "montoTotal") ?? 0;
        Add(errors, Math.Abs(total - (sum - (Dec(header, "descuentoAdicional") ?? 0))) > Tolerance, 1013, fileNumber, null);
        var rate = Dec(header, "tipoCambio") ?? 0;
        Add(errors, rate <= 0 || Math.Abs((Dec(header, "montoTotalMoneda") ?? 0) - total / rate) > Tolerance, 1014, fileNumber, null);
        Add(errors, Math.Abs((Dec(header, "montoTotalSujetoIva") ?? 0) - (total - (Dec(header, "montoGiftCard") ?? 0))) > Tolerance, 1058, fileNumber,
            null);
    }

    // ================================================================================================ archivos
    /// <summary>GZIP → bytes (null si no es un GZIP legible o supera el máximo).</summary>
    private static byte[]? Gunzip(byte[] data)
    {
        try
        {
            using var input = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (output.Length + read > MaxUncompressedBytes)
                {
                    return null;
                }
                output.Write(buffer, 0, read);
            }
            return output.Length == 0 ? null : output.ToArray();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            return null;
        }
    }

    /// <summary>Archivos regulares de un TAR, en su orden (el SIN informa por número de archivo, base 1).</summary>
    private static List<byte[]>? ReadTar(byte[] tar)
    {
        try
        {
            var files = new List<byte[]>();
            using var reader = new TarReader(new MemoryStream(tar));
            while (reader.GetNextEntry() is { } entry)
            {
                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || entry.DataStream is null)
                {
                    continue;
                }
                using var content = new MemoryStream();
                entry.DataStream.CopyTo(content);
                files.Add(content.ToArray());
            }
            return files.Count == 0 ? null : files;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null; // un TAR ilegible es un archivo inválido (920), no una falla del simulador
        }
    }

    private static string DecodeUtf8(byte[] bytes)
    {
        var text = new UTF8Encoding(false).GetString(bytes);
        return text.Length > 0 && text[0] == '﻿' ? text[1..] : text;
    }

    // ================================================================================================ lectura del XML
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    private static string? Value(XElement parent, string name)
    {
        var element = parent.Element(name);
        return element is null || string.Equals((string?)element.Attribute(Xsi + "nil"), "true", StringComparison.OrdinalIgnoreCase)
            ? null
            : element.Value.Trim();
    }

    private static decimal? Dec(XElement parent, string name) =>
        decimal.TryParse(Value(parent, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static int? Int(XElement parent, string name) =>
        int.TryParse(Value(parent, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static long? Long(XElement parent, string name) =>
        long.TryParse(Value(parent, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static void Add(List<SiatMessage> errors, bool condition, int code, int? fileNumber, string? detail)
    {
        if (condition)
        {
            errors.Add(Message(code, fileNumber, null, detail));
        }
    }

    private static void AddIf(List<string> list, bool condition, string text)
    {
        if (condition)
        {
            list.Add(text);
        }
    }
}
