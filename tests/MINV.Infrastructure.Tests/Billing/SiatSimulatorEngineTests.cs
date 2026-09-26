using System.Xml.Linq;
using MINV.Application.Abstractions;
using MINV.Domain.Billing;
using MINV.Infrastructure.Billing.Simulator;
using MINV.Tests.Siat;

namespace MINV.Infrastructure.Tests.Billing;

/// <summary>V4.1 · Motor del simulador del SIN: códigos, catálogos, recepción, paquetes, anulación y reversión.</summary>
public sealed class SiatSimulatorEngineTests
{
    private const string Token = SiatTestKit.Token;
    private static readonly SiatPlace Main = new(0, 0);

    private readonly FakeTime _time = new(SiatTestKit.Start);
    private readonly SiatSimulatorEngine _engine;

    public SiatSimulatorEngineTests()
    {
        _engine = new SiatSimulatorEngine(new SiatSimulatorOptions(), _time);
    }

    private SiatSimulatorCaller Caller => SiatTestKit.Caller;

    private (string Cuis, SiatCufdReply Cufd) Codes(SiatPlace? place = null)
    {
        var p = place ?? Main;
        var cuis = _engine.RequestCuis(Token, Caller, p);
        Assert.True(cuis.Transaction, string.Join(" · ", cuis.Messages.Select(m => $"{m.Code} {m.Description}")));
        var cufd = _engine.RequestCufd(Token, Caller, p, cuis.Code);
        Assert.True(cufd.Transaction, string.Join(" · ", cufd.Messages.Select(m => $"{m.Code} {m.Description}")));
        return (cuis.Code!, cufd);
    }

    private SiatSimulatorDocumentCall Call(string cuis, string cufd, SiatDocumentRef? document = null, int emission = SiatCodes.EmissionOnline,
        SiatPlace? place = null)
    {
        var d = document ?? SiatDocumentRef.PurchaseSale;
        return new(Caller, place ?? Main, d.Resource, d.DocumentSector, d.DocumentType, emission, cuis, cufd);
    }

    private SiatReply Send(string cuis, SiatCufdReply cufd, string xml, SiatDocumentRef? document = null)
    {
        var gzip = SiatTestKit.Gzip(xml);
        return _engine.ReceiveDocument(Token, Call(cuis, cufd.Code!, document), gzip, SiatTestKit.Sha256(gzip), SiatTestKit.Bolivia(_time.Now));
    }

    private DateTime Now => SiatTestKit.Bolivia(_time.Now);

    // ------------------------------------------------------------------------------------------------ códigos
    [Fact]
    public void El_CUIS_dura_365_dias_y_se_devuelve_el_vigente()
    {
        var first = _engine.RequestCuis(Token, Caller, Main);
        var second = _engine.RequestCuis(Token, Caller, Main);
        Assert.True(first.Transaction);
        Assert.Equal(first.Code, second.Code);
        Assert.Equal(TimeSpan.FromHours(-4), first.ValidUntil!.Value.Offset);
        Assert.Equal(SiatTestKit.Start.AddDays(365), first.ValidUntil.Value.ToUniversalTime());
        // Otra sucursal tiene su propio CUIS
        Assert.NotEqual(first.Code, _engine.RequestCuis(Token, Caller, new SiatPlace(1, 0)).Code);
        // A 5 días del vencimiento se entrega uno nuevo
        _time.Advance(TimeSpan.FromDays(361));
        Assert.NotEqual(first.Code, _engine.RequestCuis(Token, Caller, Main).Code);
    }

    [Fact]
    public void El_CUIS_de_un_punto_de_venta_exige_registrarlo_antes()
    {
        var pointOfSale = new SiatPlace(0, 1);
        Assert.Equal(933, Assert.Single(_engine.RequestCuis(Token, Caller, pointOfSale).Messages).Code);
        var cuis = _engine.RequestCuis(Token, Caller, Main).Code;
        var registered = _engine.RegisterPointOfSale(Token, Caller, 0, cuis, SiatCodes.PointOfSaleCashier, "CAJA 1", "Caja principal");
        Assert.True(registered.Transaction);
        Assert.Equal(1, registered.Code);
        Assert.Equal(2, _engine.RegisterPointOfSale(Token, Caller, 0, cuis, SiatCodes.PointOfSaleCashier, "CAJA 2", "Caja secundaria").Code);
        var own = _engine.RequestCuis(Token, Caller, pointOfSale);
        Assert.True(own.Transaction);
        Assert.NotEqual(cuis, own.Code);
        var list = _engine.ListPointsOfSale(Token, Caller, 0, cuis);
        Assert.Equal([1, 2], list.Items.Select(i => i.Code));
        Assert.Equal(SiatCodes.PointOfSaleCashier, list.Items[0].TypeCode);
        // Validaciones del registro
        Assert.Equal(947, _engine.RegisterPointOfSale(Token, Caller, 0, cuis, 9, "X", "Y").Messages[0].Code);
        Assert.Equal(948, _engine.RegisterPointOfSale(Token, Caller, 0, cuis, 5, " ", "Y").Messages[0].Code);
        Assert.Equal(949, _engine.RegisterPointOfSale(Token, Caller, 0, cuis, 5, "X", "").Messages[0].Code);
        // Cierre: el punto deja de existir y su CUIS deja de valer
        Assert.True(_engine.ClosePointOfSale(Token, Caller, pointOfSale, cuis).Transaction);
        Assert.Equal(933, _engine.RequestCufd(Token, Caller, pointOfSale, own.Code).Messages[0].Code);
        Assert.Equal([2], _engine.ListPointsOfSale(Token, Caller, 0, cuis).Items.Select(i => i.Code));
        Assert.Equal(933, _engine.ClosePointOfSale(Token, Caller, Main, cuis).Messages[0].Code);
    }

    [Fact]
    public void El_CUFD_es_nuevo_en_cada_solicitud_con_codigo_de_control_direccion_y_24_horas()
    {
        var (cuis, first) = Codes();
        var second = _engine.RequestCufd(Token, Caller, Main, cuis);
        Assert.NotEqual(first.Code, second.Code);
        Assert.Matches("^[0-9A-F]{15}$", first.ControlCode!);
        Assert.Equal("AV. SIMULADA N° 0", first.Address);
        Assert.Equal(SiatTestKit.Start.AddHours(24), first.ValidUntil!.Value.ToUniversalTime());
        Assert.Equal(TimeSpan.FromHours(-4), first.ValidUntil.Value.Offset);
        Assert.Empty(first.Messages);
        // CUIS inexistente, de otra sucursal o vencido
        Assert.Equal(913, _engine.RequestCufd(Token, Caller, Main, "NOEXISTE").Messages[0].Code);
        Assert.Equal(930, _engine.RequestCufd(Token, Caller, new SiatPlace(1, 0), cuis).Messages[0].Code);
        // A menos de 5 días del vencimiento del CUIS, el CUFD trae la advertencia 3008
        _time.Advance(TimeSpan.FromDays(361));
        Assert.Contains(_engine.RequestCufd(Token, Caller, Main, cuis).Messages, m => m.Code == SiatCodes.CuisAboutToExpire && m.IsWarning);
        _time.Advance(TimeSpan.FromDays(5));
        Assert.Equal(929, _engine.RequestCufd(Token, Caller, Main, cuis).Messages[0].Code);
    }

    [Fact]
    public void El_token_y_los_parametros_se_validan_como_en_el_SIN()
    {
        Assert.Equal(SiatCodes.InvalidToken, _engine.RequestCuis("corto", Caller, Main).Messages[0].Code);
        Assert.Equal(SiatCodes.InvalidToken, _engine.RequestCuis(null, Caller, Main).Messages[0].Code);
        Assert.Equal(910, _engine.RequestCuis(Token, Caller with { Environment = 3 }, Main).Messages[0].Code);
        Assert.Equal(917, _engine.RequestCuis(Token, Caller with { Modality = 1 }, Main).Messages[0].Code);
        Assert.Equal(911, _engine.RequestCuis(Token, Caller with { SystemCode = " " }, Main).Messages[0].Code);
        Assert.Equal(918, _engine.RequestCuis(Token, Caller, new SiatPlace(-1, 0)).Messages[0].Code);
        var restricted = new SiatSimulatorEngine(new SiatSimulatorOptions { AcceptedTokens = ["UNICO-TOKEN-ACEPTADO"] }, _time);
        Assert.Equal(SiatCodes.InvalidToken, restricted.RequestCuis(Token, Caller, Main).Messages[0].Code);
        Assert.True(restricted.RequestCuis("UNICO-TOKEN-ACEPTADO", Caller, Main).Transaction);
        Assert.Equal(SiatCodes.CommunicationOk, _engine.CheckCommunication(Token).StatusCode);
        Assert.False(_engine.CheckCommunication("x").Transaction);
    }

    [Theory]
    [InlineData(1003579028L, SiatCodes.NitActive, true)]
    [InlineData(12345L, SiatCodes.NitActive, true)]
    [InlineData(1003579999L, SiatCodes.NitInactive, false)]
    [InlineData(1234L, SiatCodes.NitNotFound, false)]
    public void VerificarNit_responde_986_987_o_994(long nit, int code, bool valid)
    {
        var (cuis, _) = Codes();
        var reply = _engine.VerifyNit(Token, Caller, 0, cuis, nit);
        Assert.Equal(code, reply.Code);
        Assert.Equal(valid, reply.IsValid);
        Assert.Equal(code, Assert.Single(reply.Messages).Code);
    }

    // ------------------------------------------------------------------------------------------------ sincronización
    [Fact]
    public void Los_catalogos_de_simulacion_tienen_los_datos_de_la_investigacion()
    {
        var (cuis, _) = Codes();
        SiatCatalogReply Sync(string catalog) => _engine.SyncCatalog(Token, Caller, Main, cuis, catalog);
        var products = Sync(SiatCatalogNames.Products);
        Assert.True(products.Transaction);
        Assert.Equal(276, products.Rows.Count);   // todas las filas del CSV de ferretería y construcción
        Assert.Contains(products.Rows, r => r.Code == "1001658" && r.ActivityCode == "4752300" && r.Description.Contains("; desperdicios", StringComparison.Ordinal));
        var activities = Sync(SiatCatalogNames.Activities);
        Assert.Contains(activities.Rows, r => r.Code == "4752100" && r.Extra == "P"
                                              && r.Description == "VENTA AL POR MENOR DE ARTÍCULOS DE FERRETERÍA, FONTANERÍA Y CALEFACCIÓN");
        Assert.Contains(activities.Rows, r => r.Extra == "S");
        var sectors = Sync(SiatCatalogNames.ActivitySectors);
        Assert.Contains(sectors.Rows, r => r.ActivityCode == "4752100" && r.Code == "1");
        Assert.Contains(sectors.Rows, r => r.ActivityCode == "4752100" && r.Code == "24");
        Assert.True(sectors.Rows.All(r => (r.Extra ?? string.Empty).Length <= 20));
        var legends = Sync(SiatCatalogNames.Legends).Rows.Where(r => r.ActivityCode == "4752100").ToList();
        Assert.True(legends.Count >= 6);
        Assert.All(legends, l => Assert.StartsWith("Ley N° 453:", l.Description, StringComparison.Ordinal));
        Assert.Equal(["1", "2", "3", "4", "5"], Sync(SiatCatalogNames.IdentityDocumentTypes).Rows.Select(r => r.Code));
        Assert.Equal(7, Sync(SiatCatalogNames.SignificantEvents).Rows.Count);
        Assert.Contains(Sync(SiatCatalogNames.SignificantEvents).Rows, r => r.Code == "5" && r.Description.Contains("ENERGIA", StringComparison.Ordinal));
        Assert.Contains(Sync(SiatCatalogNames.PaymentMethods).Rows, r => r.Code == "33" && r.Description == "PAGO ONLINE (QR)");
        Assert.Contains(Sync(SiatCatalogNames.UnitsOfMeasure).Rows, r => r.Code == "58" && r.Description == "UNIDAD (SERVICIOS)");
        Assert.Contains(Sync(SiatCatalogNames.UnitsOfMeasure).Rows, r => r.Code == "57" && r.Description == "UNIDAD (BIENES)");
        Assert.Equal(4, Sync(SiatCatalogNames.VoidReasons).Rows.Count);
        Assert.Contains(Sync(SiatCatalogNames.ServiceMessages).Rows, r => r.Code == "908" && r.Description == "Recepción Validada");
        foreach (var catalog in SiatCatalogNames.All.Where(c => c != SiatCatalogNames.DateTime))
        {
            var reply = Sync(catalog);
            Assert.True(reply.Transaction && reply.Rows.Count > 0, catalog);
        }
        Assert.Equal(913, _engine.SyncCatalog(Token, Caller, Main, "OTRO", SiatCatalogNames.Currencies).Messages[0].Code);
    }

    [Fact]
    public void La_fecha_y_hora_es_la_de_Bolivia()
    {
        var (cuis, _) = Codes();
        var clock = _engine.SyncClock(Token, Caller, Main, cuis);
        Assert.True(clock.Transaction);
        Assert.Equal(new DateTime(2026, 9, 25, 10, 0, 0), clock.SiatTime);
        Assert.Equal(DateTimeKind.Unspecified, clock.SiatTime!.Value.Kind);
    }

    [Fact]
    public void El_CSV_admite_comillas_separadores_y_saltos_de_linea_dentro_de_un_campo()
    {
        var rows = SiatSimulatorCatalogs.ParseCsv("﻿a;b;c\r\n1;\"uno; dos\";x\r\n2;\"dice \"\"hola\"\"\ny chau\";y\n\n3;tres;z");
        Assert.Equal(3, rows.Count);
        Assert.Equal("uno; dos", rows[0][1]);
        Assert.Equal("dice \"hola\"\ny chau", rows[1][1]);
        Assert.Equal(["3", "tres", "z"], rows[2]);
    }

    // ------------------------------------------------------------------------------------------------ recepción individual
    [Fact]
    public void Una_factura_valida_se_recibe_con_908_y_codigo_de_recepcion()
    {
        var (cuis, cufd) = Codes();
        var xml = SiatTestKit.Invoice(cufd.Code!, cufd.ControlCode!, Now, 1);
        var reply = Send(cuis, cufd, xml);
        Assert.True(reply.Transaction, reply.Describe());
        Assert.Equal(SiatCodes.ReceptionValidated, reply.StatusCode);
        Assert.True(Guid.TryParse(reply.ReceptionCode, out _));
        var cuf = XDocument.Parse(xml).Root!.Element("cabecera")!.Element("cuf")!.Value;
        var status = _engine.CheckDocumentStatus(Token, Call(cuis, cufd.Code!), cuf);
        Assert.Equal(SiatCodes.ReceptionValidated, status.StatusCode);
        Assert.Equal("VALIDA", status.StatusDescription);
        Assert.Equal(reply.ReceptionCode, status.ReceptionCode);
        // CUF duplicado → rechazo
        Assert.True(Send(cuis, cufd, xml).Has(952));
    }

    [Fact]
    public void Una_nota_credito_debito_se_recibe_por_Documentos_de_Ajuste()
    {
        var (cuis, cufd) = Codes();
        var reply = Send(cuis, cufd, SiatTestKit.Note(cufd.Code!, cufd.ControlCode!, Now, 1), SiatDocumentRef.CreditDebitNote);
        Assert.Equal(SiatCodes.ReceptionValidated, reply.StatusCode);
        // N3: montoEfectivoCreditoDebito = round2(devuelto × 0,13)
        var bad = SiatTestKit.Note(cufd.Code!, cufd.ControlCode!, Now, 2, tamper:
            root => SiatTestKit.Set(root.Element("cabecera")!, "montoEfectivoCreditoDebito", "9.90"));
        Assert.True(Send(cuis, cufd, bad, SiatDocumentRef.CreditDebitNote).Has(1031));
        // N1: montoTotalOriginal = Σ subTotal de las líneas tx 1
        var original = SiatTestKit.Note(cufd.Code!, cufd.ControlCode!, Now, 3, tamper:
            root => SiatTestKit.Set(root.Element("cabecera")!, "montoTotalOriginal", "800.00"));
        Assert.True(Send(cuis, cufd, original, SiatDocumentRef.CreditDebitNote).Has(1030));
        // Una nota enviada al servicio de Compra Venta no corresponde al servicio (932); con otro tipo de documento, 915
        var note = SiatTestKit.Gzip(SiatTestKit.Note(cufd.Code!, cufd.ControlCode!, Now, 4));
        var noteCall = Call(cuis, cufd.Code!, SiatDocumentRef.CreditDebitNote);
        Assert.True(_engine.ReceiveDocument(Token, noteCall with { Resource = SiatResource.PurchaseSale }, note, SiatTestKit.Sha256(note), Now).Has(932));
        Assert.True(_engine.ReceiveDocument(Token, noteCall with { DocumentType = 1 }, note, SiatTestKit.Sha256(note), Now).Has(915));
    }

    [Fact]
    public void Cada_rechazo_clave_de_la_recepcion_tiene_su_codigo()
    {
        var (cuis, cufd) = Codes();
        var call = Call(cuis, cufd.Code!);
        var good = SiatTestKit.Gzip(SiatTestKit.Invoice(cufd.Code!, cufd.ControlCode!, Now, 1));

        // hash de otros bytes (969), archivo que no es GZIP (920), sin archivo (920)
        Assert.True(_engine.ReceiveDocument(Token, call, good, SiatTestKit.Sha256([1, 2, 3]), Now).Has(969));
        var plain = System.Text.Encoding.UTF8.GetBytes("<no-es-gzip/>");
        Assert.True(_engine.ReceiveDocument(Token, call, plain, SiatTestKit.Sha256(plain), Now).Has(920));
        Assert.True(_engine.ReceiveDocument(Token, call, null, null, Now).Has(920));
        // documento sector del recurso equivocado (932) y tipo de emisión equivocado (916)
        Assert.True(_engine.ReceiveDocument(Token, call with { Resource = SiatResource.Computerized }, good, SiatTestKit.Sha256(good), Now).Has(932));
        Assert.True(_engine.ReceiveDocument(Token, call with { Emission = 2 }, good, SiatTestKit.Sha256(good), Now).Has(916));
        // CUFD que el simulador no emitió (914) y CUFD vencido (953)
        Assert.True(_engine.ReceiveDocument(Token, call with { Cufd = "BQNOEMITIDO" }, good, SiatTestKit.Sha256(good), Now).Has(914));

        // XML que no cumple el XSD (falta <telefono>): 902 con 939
        var noPhone = Send(cuis, cufd, SiatTestKit.Invoice(cufd.Code!, cufd.ControlCode!, Now, 2,
            tamper: root => root.Element("cabecera")!.Element("telefono")!.Remove()));
        Assert.Equal(SiatCodes.ReceptionRejected, noPhone.StatusCode);
        Assert.False(noPhone.Transaction);
        Assert.Contains(noPhone.Messages, m => m.Code == 939 && m.Description.Contains("telefono", StringComparison.Ordinal));

        // NIT del XML distinto del de la solicitud (1001; el CUF sigue siendo coherente con el XML)
        Assert.True(Send(cuis, cufd, SiatTestKit.Invoice(cufd.Code!, cufd.ControlCode!, Now, 3, nit: 123456789)).Has(1001));
        // CUF que no termina en el código de control de ESE CUFD
        Assert.True(Send(cuis, cufd, SiatTestKit.Invoice(cufd.Code!, "A19E23EF34124CD", Now, 4)).Has(1002));
        // CUF incoherente con el XML (número distinto)
        Assert.True(Send(cuis, cufd, SiatTestKit.Invoice(cufd.Code!, cufd.ControlCode!, Now, 5,
            tamper: root => SiatTestKit.Set(root.Element("cabecera")!, "numeroFactura", "6"))).Has(1002));
        // CUF con tipo de emisión 2 en una recepción en línea
        Assert.True(Send(cuis, cufd, SiatTestKit.Invoice(cufd.Code!, cufd.ControlCode!, Now, 7, emission: 2)).Has(1002));
        // V2: montoTotal = Σ subTotal − descuentoAdicional
        Assert.True(Send(cuis, cufd, SiatTestKit.Invoice(cufd.Code!, cufd.ControlCode!, Now, 8,
            tamper: root => SiatTestKit.Set(root.Element("cabecera")!, "montoTotal", "98"))).Has(1013));
        // V1: subTotal = cantidad × precio − descuento
        Assert.True(Send(cuis, cufd, SiatTestKit.Invoice(cufd.Code!, cufd.ControlCode!, Now, 9,
            tamper: root => SiatTestKit.Set(root.Element("detalle")!, "subTotal", "90"))).Has(1018));
        // V4/V5: montoTotalSujetoIva = montoTotal − montoGiftCard
        Assert.True(Send(cuis, cufd, SiatTestKit.Invoice(cufd.Code!, cufd.ControlCode!, Now, 10,
            tamper: root => SiatTestKit.Set(root.Element("cabecera")!, "montoTotalSujetoIva", "90"))).Has(1058));
        // Fecha de emisión en el futuro
        Assert.True(Send(cuis, cufd, SiatTestKit.Invoice(cufd.Code!, cufd.ControlCode!, Now.AddHours(2), 11)).Has(1009));
        // Sucursal del XML distinta de la solicitud
        Assert.True(Send(cuis, cufd, SiatTestKit.Invoice(cufd.Code!, cufd.ControlCode!, Now, 12, branch: 3)).Has(1004));

        _time.Advance(TimeSpan.FromHours(25));
        Assert.True(Send(cuis, cufd, SiatTestKit.Invoice(cufd.Code!, cufd.ControlCode!, Now, 13)).Has(953));
    }

    // ------------------------------------------------------------------------------------------------ paquetes
    [Fact]
    public void Un_paquete_con_un_archivo_malo_se_valida_con_904_y_el_numero_de_archivo()
    {
        var (cuis, eventCufd) = Codes();
        var start = Now;
        var docs = new List<string>
        {
            SiatTestKit.Invoice(eventCufd.Code!, eventCufd.ControlCode!, start.AddMinutes(5), 1, emission: SiatCodes.EmissionOffline),
            SiatTestKit.Invoice(eventCufd.Code!, eventCufd.ControlCode!, start.AddMinutes(10), 2, emission: SiatCodes.EmissionOffline),
            // fuera del rango del evento → 1040 en el archivo 3
            SiatTestKit.Invoice(eventCufd.Code!, eventCufd.ControlCode!, start.AddHours(3), 3, emission: SiatCodes.EmissionOffline),
        };
        _time.Advance(TimeSpan.FromHours(4));
        var newCufd = _engine.RequestCufd(Token, Caller, Main, cuis);
        var evt = _engine.RegisterEvent(Token, Caller, Main, cuis, newCufd.Code, 1, "CORTE DEL SERVICIO DE INTERNET", start, start.AddHours(1),
            eventCufd.Code);
        Assert.True(evt.Transaction, string.Join(" · ", evt.Messages.Select(m => $"{m.Code} {m.Description}")));
        Assert.Matches("^[0-9]+$", evt.ReceptionCode!);

        var call = Call(cuis, newCufd.Code!, emission: SiatCodes.EmissionOffline);
        var package = SiatTestKit.Package(docs);
        var sent = _engine.ReceivePackage(Token, call, package, SiatTestKit.Sha256(package), Now, 3, evt.ReceptionCode, null);
        Assert.True(sent.Transaction, sent.Describe());
        Assert.Equal(SiatCodes.ReceptionPending, sent.StatusCode);

        var validation = _engine.ValidatePackage(Token, call, sent.ReceptionCode);
        Assert.Equal(SiatCodes.ReceptionObserved, validation.StatusCode);
        var error = Assert.Single(validation.Messages);
        Assert.Equal(1040, error.Code);
        Assert.Equal(3, error.FileNumber);

        // Los correctos quedaron registrados; el malo no
        var cufs = docs.Select(x => XDocument.Parse(x).Root!.Element("cabecera")!.Element("cuf")!.Value).ToList();
        var status = Call(cuis, newCufd.Code!);
        Assert.Equal(SiatCodes.ReceptionValidated, _engine.CheckDocumentStatus(Token, status, cufs[0]).StatusCode);
        Assert.True(_engine.CheckDocumentStatus(Token, status, cufs[2]).Has(924));

        // Cantidad declarada distinta (985), evento inexistente (942), código de recepción desconocido (944)
        var one = SiatTestKit.Package([SiatTestKit.Invoice(eventCufd.Code!, eventCufd.ControlCode!, start.AddMinutes(20), 4,
            emission: SiatCodes.EmissionOffline)]);
        Assert.True(_engine.ReceivePackage(Token, call, one, SiatTestKit.Sha256(one), Now, 2, evt.ReceptionCode, null).Has(985));
        Assert.True(_engine.ReceivePackage(Token, call, one, SiatTestKit.Sha256(one), Now, 1, "999", null).Has(942));
        Assert.True(_engine.ValidatePackage(Token, call, Guid.NewGuid().ToString()).Has(944));

        // Un paquete sin errores valida con 908; uno con todos malos, 902
        var ok = _engine.ReceivePackage(Token, call, one, SiatTestKit.Sha256(one), Now, 1, evt.ReceptionCode, null);
        Assert.Equal(SiatCodes.ReceptionValidated, _engine.ValidatePackage(Token, call, ok.ReceptionCode).StatusCode);
        var again = _engine.ReceivePackage(Token, call, one, SiatTestKit.Sha256(one), Now, 1, evt.ReceptionCode, null);
        var rejected = _engine.ValidatePackage(Token, call, again.ReceptionCode);
        Assert.Equal(SiatCodes.ReceptionRejected, rejected.StatusCode);
        Assert.Equal(952, Assert.Single(rejected.Messages).Code);
    }

    [Fact]
    public void Un_evento_manual_exige_el_CAFC_en_cada_factura_del_paquete()
    {
        var (cuis, eventCufd) = Codes();
        var start = Now;
        var withCafc = SiatTestKit.Invoice(eventCufd.Code!, eventCufd.ControlCode!, start.AddMinutes(5), 1, emission: 2, cafc: "CAFC-101");
        var withoutCafc = SiatTestKit.Invoice(eventCufd.Code!, eventCufd.ControlCode!, start.AddMinutes(6), 2, emission: 2);
        _time.Advance(TimeSpan.FromHours(2));
        var newCufd = _engine.RequestCufd(Token, Caller, Main, cuis);
        var evt = _engine.RegisterEvent(Token, Caller, Main, cuis, newCufd.Code, 5, "CORTE DE SUMINISTRO DE ENERGIA ELECTRICA", start,
            start.AddHours(1), eventCufd.Code);
        var call = Call(cuis, newCufd.Code!, emission: SiatCodes.EmissionOffline);
        var package = SiatTestKit.Package([withCafc, withoutCafc]);
        var sent = _engine.ReceivePackage(Token, call, package, SiatTestKit.Sha256(package), Now, 2, evt.ReceptionCode, "CAFC-101");
        var validation = _engine.ValidatePackage(Token, call, sent.ReceptionCode);
        Assert.Equal(SiatCodes.ReceptionObserved, validation.StatusCode);
        Assert.Equal((1045, 2), (validation.Messages[0].Code, validation.Messages[0].FileNumber!.Value));
    }

    [Fact]
    public void El_registro_del_evento_valida_fechas_catalogo_y_CUFD_del_evento()
    {
        var (cuis, cufd) = Codes();
        var start = Now.AddHours(-2);
        SiatEventReply Register(int code, DateTime from, DateTime to, string? eventCufd) =>
            _engine.RegisterEvent(Token, Caller, Main, cuis, cufd.Code, code, "EVENTO", from, to, eventCufd);
        Assert.True(Register(1, start, start.AddHours(1), cufd.Code).Transaction);
        Assert.Equal(974, Register(1, start, start, cufd.Code).Messages[0].Code);                 // fin no posterior al inicio
        Assert.Equal(974, Register(1, start, Now.AddHours(1), cufd.Code).Messages[0].Code);       // fin en el futuro
        Assert.Equal(976, Register(99, start, start.AddHours(1), cufd.Code).Messages[0].Code);    // evento fuera del catálogo
        Assert.Equal(984, Register(1, start, start.AddHours(1), "BQDESCONOCIDO").Messages[0].Code);
        Assert.Equal(974, Register(1, Now.AddHours(-60), Now.AddHours(-50), cufd.Code).Messages[0].Code);   // pasado el plazo de 48 h
        Assert.Equal(914, _engine.RegisterEvent(Token, Caller, Main, cuis, "BQX", 1, "E", start, start.AddHours(1), cufd.Code).Messages[0].Code);
    }

    // ------------------------------------------------------------------------------------------------ anulación y reversión
    [Fact]
    public void Anulacion_y_reversion_una_sola_vez_dentro_del_plazo()
    {
        var (cuis, cufd) = Codes();
        var xml = SiatTestKit.Invoice(cufd.Code!, cufd.ControlCode!, Now, 1);
        Assert.True(Send(cuis, cufd, xml).Transaction);
        var cuf = XDocument.Parse(xml).Root!.Element("cabecera")!.Element("cuf")!.Value;
        var call = Call(cuis, cufd.Code!);

        Assert.True(_engine.VoidDocument(Token, call, cuf, 99).Has(925));
        Assert.True(_engine.VoidDocument(Token, call, "NOEXISTE", 1).Has(924));
        var voided = _engine.VoidDocument(Token, call, cuf, 1);
        Assert.True(voided.Transaction);
        Assert.Equal(SiatCodes.VoidConfirmed, voided.StatusCode);
        Assert.Equal("ANULADA", _engine.CheckDocumentStatus(Token, call, cuf).StatusDescription);
        var twice = _engine.VoidDocument(Token, call, cuf, 1);
        Assert.False(twice.Transaction);
        Assert.True(twice.Has(SiatCodes.AlreadyVoided));

        var reverted = _engine.RevertVoid(Token, call, cuf);
        Assert.True(reverted.Transaction);
        Assert.Equal(SiatCodes.RevertConfirmed, reverted.StatusCode);
        Assert.Equal(SiatCodes.ReceptionValidated, _engine.CheckDocumentStatus(Token, call, cuf).StatusCode);
        Assert.True(_engine.RevertVoid(Token, call, cuf).Has(968));      // una sola vez
        Assert.True(_engine.VoidDocument(Token, call, cuf, 1).Has(941)); // lo revertido no se vuelve a anular

        var status = _engine.CheckDocumentStatus(Token, call, "NOEXISTE");
        Assert.False(status.Transaction);
        Assert.True(status.Has(924));
    }

    [Fact]
    public void Pasado_el_dia_9_del_mes_siguiente_no_se_anula_ni_se_revierte()
    {
        var (cuis, cufd) = Codes();
        var first = SiatTestKit.Invoice(cufd.Code!, cufd.ControlCode!, Now, 1);   // emitida el 25/09/2026
        var second = SiatTestKit.Invoice(cufd.Code!, cufd.ControlCode!, Now, 2);
        Send(cuis, cufd, first);
        Send(cuis, cufd, second);
        string Cuf(string xml) => XDocument.Parse(xml).Root!.Element("cabecera")!.Element("cuf")!.Value;

        // 09/10/2026 23:00 en Bolivia: todavía se puede
        _time.Now = new DateTimeOffset(2026, 10, 9, 23, 0, 0, TimeSpan.FromHours(-4));
        var (cuis2, cufd2) = Codes();
        var call = Call(cuis2, cufd2.Code!);
        Assert.Equal(SiatCodes.VoidConfirmed, _engine.VoidDocument(Token, call, Cuf(first), 3).StatusCode);

        // 10/10/2026 00:00:01: fuera de plazo
        _time.Now = new DateTimeOffset(2026, 10, 10, 0, 0, 1, TimeSpan.FromHours(-4));
        var late = _engine.VoidDocument(Token, call, Cuf(second), 3);
        Assert.False(late.Transaction);
        Assert.True(late.Has(SiatCodes.VoidOutOfTime));
        Assert.True(_engine.RevertVoid(Token, call, Cuf(first)).Has(3012));
    }

    // ------------------------------------------------------------------------------------------------ disponibilidad y estado
    [Fact]
    public void Con_el_interruptor_apagado_no_hay_comunicacion()
    {
        _engine.Available = false;
        Assert.Throws<SiatUnavailableException>(() => _engine.CheckCommunication(Token));
        Assert.Throws<SiatUnavailableException>(() => _engine.RequestCuis(Token, Caller, Main));
        _engine.Available = true;
        Assert.True(_engine.RequestCuis(Token, Caller, Main).Transaction);
    }

    [Fact]
    public void El_estado_se_guarda_en_un_archivo_y_sobrevive_al_reinicio()
    {
        var file = Path.Combine(Path.GetTempPath(), $"minv-siat-sim-{Guid.NewGuid():N}.json");
        try
        {
            var options = new SiatSimulatorOptions { StateFile = file };
            var engine = new SiatSimulatorEngine(options, _time);
            var cuis = engine.RequestCuis(Token, Caller, Main).Code!;
            var cufd = engine.RequestCufd(Token, Caller, Main, cuis);
            var xml = SiatTestKit.Invoice(cufd.Code!, cufd.ControlCode!, Now, 1);
            var gzip = SiatTestKit.Gzip(xml);
            var call = new SiatSimulatorDocumentCall(Caller, Main, SiatResource.PurchaseSale, 1, 1, 1, cuis, cufd.Code);
            Assert.True(engine.ReceiveDocument(Token, call, gzip, SiatTestKit.Sha256(gzip), Now).Transaction);
            Assert.True(File.Exists(file));

            var restarted = new SiatSimulatorEngine(options, _time);
            Assert.Equal(cuis, restarted.RequestCuis(Token, Caller, Main).Code);
            var cuf = XDocument.Parse(xml).Root!.Element("cabecera")!.Element("cuf")!.Value;
            Assert.Equal(SiatCodes.ReceptionValidated, restarted.CheckDocumentStatus(Token, call, cuf).StatusCode);
            Assert.Equal(1, restarted.Status().Documents);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
