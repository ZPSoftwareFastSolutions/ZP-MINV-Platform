using MINV.Application.Abstractions;
using MINV.Domain.Billing;
using F = MINV.Infrastructure.Billing.Soap.SiatSoapContract.Fields;

namespace MINV.Infrastructure.Billing.Soap;

/// <summary>
/// V4.1 · Traduce las respuestas SOAP del SIN (leídas con <see cref="SiatSoapReader"/>, por nombre local y sin depender
/// del envoltorio) a las respuestas del puerto. Si falta <c>transaccion</c>, se deduce de lo que sí llegó.
/// </summary>
public static class SiatSoapReplies
{
    /// <summary>Recepción, paquetes, validación, anulación, reversión y verificación de estado.</summary>
    public static SiatReply Service(SiatSoapReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var status = reader.Int(F.Status);
        return new SiatReply(reader.Transaction ?? status is SiatCodes.ReceptionValidated or SiatCodes.ReceptionPending,
            status, reader.Text(F.StatusDescription, F.MessageDescription), reader.Text(F.ReceptionCode), reader.Messages);
    }

    /// <summary>verificarComunicacion: <c>return = 926</c> o la lista de mensajes con el 926.</summary>
    public static SiatReply Communication(SiatSoapReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var messages = reader.Messages;
        var code = reader.Int(F.Return) ?? (messages.Any(m => m.Code == SiatCodes.CommunicationOk) ? SiatCodes.CommunicationOk : messages.FirstOrDefault()?.Code);
        var description = messages.FirstOrDefault(m => m.Code == code)?.Description;
        return new SiatReply(reader.Transaction ?? code == SiatCodes.CommunicationOk, code, description, null, messages);
    }

    public static SiatCuisReply Cuis(SiatSoapReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var code = reader.Text([.. SiatSoapContract.CodeNames]);
        return new SiatCuisReply(reader.Transaction ?? code is not null, code, SiatSoapReader.ParseInstant(reader.Text(F.ValidUntil)), reader.Messages);
    }

    public static SiatCufdReply Cufd(SiatSoapReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var code = reader.Text([.. SiatSoapContract.CodeNames]);
        return new SiatCufdReply(reader.Transaction ?? code is not null, code, reader.Text(F.ControlCode), reader.Text(F.Address),
            SiatSoapReader.ParseInstant(reader.Text(F.ValidUntil)), reader.Messages);
    }

    /// <summary>verificarNit: 986 activo, 987 inactivo, 994 inexistente (el resultado viene como mensaje).</summary>
    public static SiatNitReply Nit(SiatSoapReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var messages = reader.Messages;
        var result = messages.FirstOrDefault(m => m.Code is SiatCodes.NitActive or SiatCodes.NitInactive or SiatCodes.NitNotFound)
                     ?? messages.FirstOrDefault();
        var valid = messages.Any(m => m.Code == SiatCodes.NitActive);
        return new SiatNitReply(reader.Transaction ?? valid, valid, result?.Code, result?.Description, messages);
    }

    /// <summary>Filas de un catálogo según la forma de su respuesta (<see cref="SiatSoapContract.SyncShape"/>).</summary>
    public static SiatCatalogReply Catalog(SiatSoapReader reader, SiatSoapContract.SyncShape shape)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var rows = shape switch
        {
            SiatSoapContract.SyncShape.Activities => reader.ListItems(F.Activities, F.ActivityCaeb)
                .Select(i => Row(Child(i, F.ActivityCaeb), Child(i, F.MessageDescription), null, Child(i, F.ActivityType))),
            SiatSoapContract.SyncShape.ActivitySectors => reader.ListItems(F.ActivitySectors, F.DocumentSector)
                .Select(i => Row(Child(i, F.DocumentSector), Child(i, F.MessageDescription) ?? Child(i, F.SectorType), Child(i, F.Activity),
                    Child(i, F.SectorType))),
            SiatSoapContract.SyncShape.Legends => reader.ListItems(F.Legends, F.LegendText)
                .Select(i => Row(Child(i, F.Activity), Child(i, F.LegendText), Child(i, F.Activity), null)),
            SiatSoapContract.SyncShape.Products => reader.ListItems(F.Codes, F.Product)
                .Select(i => Row(Child(i, F.Product), Child(i, F.ProductDescription) ?? Child(i, F.MessageDescription), Child(i, F.Activity),
                    Child(i, F.Nandina))),
            SiatSoapContract.SyncShape.Parametric => reader.ListItems(F.Codes, F.Classifier)
                .Select(i => Row(Child(i, F.Classifier), Child(i, F.MessageDescription), null, null)),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "La fecha y hora no es un catálogo de filas."),
        };
        var list = rows.OfType<SiatCatalogRow>().ToList();
        return new SiatCatalogReply(reader.Transaction ?? list.Count > 0, list, reader.Messages);
    }

    /// <summary>sincronizarFechaHora: hora de Bolivia sin zona (si llega con zona, se convierte a la de Bolivia).</summary>
    public static SiatClockReply Clock(SiatSoapReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var time = SiatSoapReader.ParseBoliviaTime(reader.Text(F.DateTime));
        return new SiatClockReply(reader.Transaction ?? time is not null, time, reader.Messages);
    }

    public static SiatPointOfSaleReply PointOfSale(SiatSoapReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var code = reader.Int(F.PointOfSale);
        return new SiatPointOfSaleReply(reader.Transaction ?? code is not null, code, reader.Messages);
    }

    public static SiatPointOfSaleListReply PointsOfSale(SiatSoapReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var items = reader.ListItems(F.PointsOfSale, F.PointOfSale)
            .Select(i => SiatSoapReader.ParseInt(Child(i, F.PointOfSale)) is { } code
                ? new SiatPointOfSaleInfo(code, Child(i, F.PointOfSaleName) ?? string.Empty,
                    SiatSoapReader.ParseInt(Child(i, F.PointOfSaleTypeName)) ?? SiatSoapReader.ParseInt(Child(i, F.PointOfSaleType)))
                : null)
            .OfType<SiatPointOfSaleInfo>()
            .ToList();
        return new SiatPointOfSaleListReply(reader.Transaction ?? items.Count > 0, items, reader.Messages);
    }

    /// <summary>cierrePuntoVenta: solo transacción y mensajes.</summary>
    public static SiatReply Close(SiatSoapReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var messages = reader.Messages;
        return new SiatReply(reader.Transaction ?? false, reader.Int(F.Status), reader.Text(F.StatusDescription), null, messages);
    }

    public static SiatEventReply Event(SiatSoapReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var code = reader.Text(F.EventReceptionCode, F.ReceptionCode);
        return new SiatEventReply(reader.Transaction ?? code is not null, code, reader.Messages);
    }

    /// <summary>Código SIAT que resume una respuesta para la bitácora técnica (estado, <c>return</c> o primer mensaje).</summary>
    public static int? SummaryCode(SiatSoapReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return reader.Int(F.Status) ?? reader.Int(F.Return) ?? reader.Messages.FirstOrDefault()?.Code;
    }

    private static string? Child(System.Xml.Linq.XElement item, string name) => SiatSoapReader.Child(item, name);

    private static SiatCatalogRow? Row(string? code, string? description, string? activity, string? extra) =>
        string.IsNullOrWhiteSpace(code) ? null : new SiatCatalogRow(code.Trim(), (description ?? string.Empty).Trim(), activity?.Trim(), extra?.Trim());
}
