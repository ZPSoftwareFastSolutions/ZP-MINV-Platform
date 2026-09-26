using MINV.Domain.Billing;
using MINV.Domain.Common;
using MINV.Domain.Events;
using MINV.Domain.Integration;

namespace MINV.Domain.Tests.Billing;

public sealed class FiscalDocumentTests
{
    private readonly BillingTestData _t = new();

    // ------------------------------------------------------------------------------------------------ emisión de facturas
    [Fact]
    public void La_factura_deriva_sus_totales_de_las_lineas_con_las_formulas_del_SIN()
    {
        // Ejemplo de la investigación 05 §3.4: 2 × 75.50 − 5.00 = 146.00; 1 × 104.00 = 104.00; descuento adicional 10;
        // gift card 40 → montoTotal 240.00 y montoTotalSujetoIva 200.00.
        var doc = _t.Invoice(additionalDiscount: 10m, giftCard: 40m);
        Assert.Equal([146.00m, 104.00m], doc.Lines.OrderBy(l => l.LineNumber).Select(l => l.Subtotal));
        Assert.Equal(250.00m, doc.LinesSubtotal);
        Assert.Equal(240.00m, doc.TotalAmount);
        Assert.Equal(200.00m, doc.TotalSubjectToVat);
        Assert.Equal(26.00m, doc.VatAmount);
        Assert.Equal([1, 2], doc.Lines.Select(l => l.LineNumber).Order());
        Assert.Equal(FiscalDocumentKind.Invoice, doc.Kind);
        Assert.Equal((SiatCodes.SectorPurchaseSale, SiatCodes.InvoiceWithTaxCredit), (doc.DocumentSector, doc.DocumentType));
        Assert.Equal(FiscalDocumentStatus.Pending, doc.Status);
        Assert.True(doc.IsActive);
        Assert.Equal(1, doc.CurrencyCode);
        Assert.Equal(1m, doc.ExchangeRate);
    }

    [Fact]
    public void El_CUF_usa_la_misma_marca_de_tiempo_que_la_fecha_de_emision_y_la_modalidad_2()
    {
        var doc = _t.Invoice(emission: _t.Emission(number: 137, branchCode: 3, pointOfSaleCode: 1));
        var parts = Cuf.DecodeFull(doc.Cuf, BillingTestData.ControlCode);
        Assert.NotNull(parts);
        Assert.Equal(new Cuf.Parts(BillingTestData.Nit, _t.IssuedAt, 3, SiatCodes.ModalityComputerized, SiatCodes.EmissionOnline,
            SiatCodes.InvoiceWithTaxCredit, SiatCodes.SectorPurchaseSale, 137, 1), parts);
        Assert.EndsWith(BillingTestData.ControlCode, doc.Cuf);
        Assert.Equal(DateTimeKind.Unspecified, doc.IssuedAt.Kind);
    }

    [Fact]
    public void Codigo_de_excepcion_con_NIT_especial_pedido_o_fuera_de_linea_y_0_con_CI()
    {
        Assert.Equal(1, _t.Invoice(buyer: BillingTestData.Buyer(SiatCodes.DocumentNit, "99002", name: "Control Tributario")).ExceptionCode);
        Assert.Equal(1, _t.Invoice(buyer: BillingTestData.Buyer(SiatCodes.DocumentNit, "99003")).ExceptionCode);
        var nit = BillingTestData.Buyer(SiatCodes.DocumentNit, "1020703023");
        Assert.Equal(0, _t.Invoice(buyer: nit).ExceptionCode);
        Assert.Equal(1, _t.Invoice(buyer: nit, exceptionRequested: true).ExceptionCode);
        var offline = _t.Invoice(buyer: nit, emission: _t.Emission(emissionType: SiatCodes.EmissionOffline));
        Assert.Equal(1, offline.ExceptionCode);
        Assert.Equal(FiscalDocumentStatus.Offline, offline.Status);
        Assert.Equal(0, _t.Invoice(buyer: BillingTestData.Buyer(SiatCodes.DocumentCi, "5115889"), exceptionRequested: true).ExceptionCode);
        Assert.Equal(0, _t.Invoice(buyer: BillingTestData.Buyer(SiatCodes.DocumentCi, "5115889"),
            emission: _t.Emission(emissionType: SiatCodes.EmissionOffline)).ExceptionCode);
        Assert.True(BillingTestData.Buyer(SiatCodes.DocumentNit, "99001").IsSpecial);
        Assert.False(BillingTestData.Buyer(SiatCodes.DocumentCi, "99001").IsSpecial);
    }

    [Fact]
    public void La_tarjeta_se_guarda_enmascarada()
    {
        var doc = _t.Invoice(paymentMethod: 2, card: "4797123412347896");
        Assert.Equal("4797000000007896", doc.CardNumberMasked);
        Assert.Null(_t.Invoice().CardNumberMasked);
    }

    [Theory]
    [InlineData("1.005", "10")]
    [InlineData("1", "10.001")]
    public void La_factura_rechaza_mas_de_2_decimales_en_cantidad_o_precio(string quantity, string price)
    {
        var line = BillingTestData.Line(decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture),
            decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("fiscal.decimals", Assert.Throws<DomainException>(() => _t.Invoice([line])).Code);
    }

    [Fact]
    public void La_factura_rechaza_descuento_con_mas_de_2_decimales()
    {
        Assert.Equal("fiscal.decimals", Assert.Throws<DomainException>(() => _t.Invoice([BillingTestData.Line(1, 10m, 0.125m)])).Code);
    }

    [Fact]
    public void Total_cero_gift_card_excesiva_y_cantidad_de_lineas_se_rechazan()
    {
        Assert.Equal("fiscal.total", Assert.Throws<DomainException>(() => _t.Invoice([BillingTestData.Line(1, 50m)], additionalDiscount: 50m)).Code);
        Assert.Equal("fiscal.gift_card", Assert.Throws<DomainException>(() => _t.Invoice([BillingTestData.Line(1, 50m)], giftCard: 50.01m)).Code);
        Assert.Equal("fiscal.lines", Assert.Throws<DomainException>(() => _t.Invoice([])).Code);
        var tooMany = Enumerable.Range(1, SiatCodes.MaxLinesPerDocument + 1).Select(_ => BillingTestData.Line(1, 1m)).ToList();
        Assert.Equal("fiscal.lines", Assert.Throws<DomainException>(() => _t.Invoice(tooMany)).Code);
        Assert.Equal("fiscal.line_tx", Assert.Throws<DomainException>(() => _t.Invoice([BillingTestData.Line(1, 10m, tx: 1)])).Code);
        Assert.Equal("fiscal.subtotal", Assert.Throws<DomainException>(() => _t.Invoice([BillingTestData.Line(1, 10m, 10m)])).Code);
        Assert.Equal("fiscal.payment", Assert.Throws<DomainException>(() => _t.Invoice(paymentMethod: 0)).Code);
    }

    [Fact]
    public void La_factura_con_gift_card_total_es_valida_con_base_cero()
    {
        var doc = _t.Invoice([BillingTestData.Line(1, 50m)], giftCard: 50m);
        Assert.Equal(50m, doc.TotalAmount);
        Assert.Equal(0m, doc.TotalSubjectToVat);
    }

    [Fact]
    public void Numero_y_cafc_se_validan()
    {
        Assert.Equal("fiscal.number", Assert.Throws<DomainException>(() => _t.Invoice(emission: _t.Emission(number: 0))).Code);
        Assert.Equal("fiscal.cafc", Assert.Throws<DomainException>(() => _t.Invoice(emission: _t.Emission(cafc: "1011917833B0D"))).Code);
        var manual = _t.Invoice(emission: _t.Emission(emissionType: SiatCodes.EmissionOffline, cafc: "1011917833B0D"));
        Assert.Equal("1011917833B0D", manual.Cafc);
        Assert.Equal(FiscalDocumentStatus.Offline, manual.Status);
    }

    // ------------------------------------------------------------------------------------------------ notas crédito-débito
    [Fact]
    public void Nota_con_el_ejemplo_oficial_775_75_da_efectivo_9_75()
    {
        var note = _t.CreditNote();
        Assert.Equal(FiscalDocumentKind.CreditDebitNote, note.Kind);
        Assert.Equal((SiatCodes.SectorCreditDebitNote, SiatCodes.AdjustmentDocument), (note.DocumentSector, note.DocumentType));
        Assert.Equal(775.00m, note.OriginalTotal);
        Assert.Equal(75.00m, note.ReturnedTotal);
        Assert.Equal(9.75m, note.VatAmount);
        Assert.Equal(75.00m, note.TotalAmount);
        Assert.NotNull(note.NoteReference);
        Assert.Equal(1, note.NoteReference.OriginalNumber);
        var parts = Cuf.DecodeFull(note.Cuf, BillingTestData.ControlCode);
        Assert.NotNull(parts);
        Assert.Equal((SiatCodes.AdjustmentDocument, SiatCodes.SectorCreditDebitNote), (parts.DocumentType, parts.DocumentSector));
    }

    [Fact]
    public void Nota_con_descuento_prorrateado_775_75_menos_4_41()
    {
        var note = _t.CreditNote(discountShare: 4.41m);
        Assert.Equal(70.59m, note.ReturnedTotal);
        Assert.Equal(9.18m, note.VatAmount);
    }

    [Fact]
    public void Nota_hasta_18_meses_despues_de_la_factura_original()
    {
        var original = new DateTime(2025, 1, 10, 9, 0, 0);
        Assert.NotNull(_t.CreditNote(issuedAt: new DateTime(2026, 7, 10, 9, 0, 0), originalIssuedAt: original));
        Assert.Equal("fiscal.note_deadline",
            Assert.Throws<DomainException>(() => _t.CreditNote(issuedAt: new DateTime(2026, 7, 10, 9, 0, 1), originalIssuedAt: original)).Code);
        Assert.Equal("fiscal.note_date",
            Assert.Throws<DomainException>(() => _t.CreditNote(issuedAt: new DateTime(2025, 1, 9), originalIssuedAt: original)).Code);
    }

    [Fact]
    public void Nota_sin_lineas_originales_o_sin_devueltas_se_rechaza()
    {
        Assert.Equal("fiscal.note_lines", Assert.Throws<DomainException>(() =>
            _t.CreditNote([BillingTestData.Line(1, 775m, tx: 1), BillingTestData.Line(1, 75m, tx: 1)])).Code);
        Assert.Equal("fiscal.note_lines", Assert.Throws<DomainException>(() =>
            _t.CreditNote([BillingTestData.Line(1, 775m, tx: 2), BillingTestData.Line(1, 75m, tx: 2)])).Code);
        Assert.Equal("fiscal.note_lines", Assert.Throws<DomainException>(() => _t.CreditNote([BillingTestData.Line(1, 775m, tx: 1)])).Code);
        Assert.Equal("fiscal.note_tx", Assert.Throws<DomainException>(() =>
            _t.CreditNote([BillingTestData.Line(1, 775m, tx: 1), BillingTestData.Line(1, 75m, tx: 2), BillingTestData.Line(1, 5m)])).Code);
        Assert.Equal("fiscal.note_exceeds", Assert.Throws<DomainException>(() =>
            _t.CreditNote([BillingTestData.Line(1, 75m, tx: 1), BillingTestData.Line(1, 775m, tx: 2)])).Code);
    }

    [Fact]
    public void Nota_fuera_de_linea_no_se_emite()
    {
        var ex = Assert.Throws<DomainException>(() => FiscalDocument.IssueCreditNote(_t.Tenant, _t.Emission(emissionType: SiatCodes.EmissionOffline),
            BillingTestData.Buyer(), null, new FiscalOriginalInvoice(null, 1, "ABC", new DateTime(2026, 9, 1), null),
            [BillingTestData.Line(1, 775m, tx: 1), BillingTestData.Line(1, 75m, tx: 2)], _t.Now));
        Assert.Equal("fiscal.note_offline", ex.Code);
    }

    [Fact]
    public void Nota_admite_cantidades_con_mas_de_2_decimales_en_el_detalle()
    {
        var note = _t.CreditNote([BillingTestData.Line(2.5m, 62m, tx: 1), BillingTestData.Line(0.75m, 62m, tx: 2)]);
        Assert.Equal(155m, note.OriginalTotal);
        Assert.Equal(46.50m, note.ReturnedTotal);
    }

    // ------------------------------------------------------------------------------------------------ transiciones
    [Fact]
    public void Accept_valida_una_sola_vez_y_publica_el_evento()
    {
        var doc = _t.Invoice();
        doc.Accept("REC-1", SiatCodes.ReceptionValidated, _t.Now);
        Assert.Equal(FiscalDocumentStatus.Valid, doc.Status);
        Assert.Equal("REC-1", doc.ReceptionCode);
        Assert.Equal(SiatCodes.ReceptionValidated, doc.LastSiatCode);
        var e = Assert.IsType<FiscalDocumentValidatedEvent>(Assert.Single(doc.DomainEvents));
        Assert.Equal(IntegrationEvents.FiscalDocumentValidated, e.EventType);
        Assert.Equal((doc.Id, doc.Cuf, doc.TotalAmount, _t.Branch), (e.DocumentId, e.Cuf, e.Total, e.BranchId!.Value));
        Assert.Equal("fiscal.state", Assert.Throws<DomainException>(() => doc.Accept(null, 908, _t.Now)).Code);
        doc.ClearDomainEvents();
        Assert.Empty(doc.DomainEvents);
    }

    [Fact]
    public void Reject_deja_el_documento_inactivo()
    {
        var doc = _t.Invoice();
        doc.Reject(SiatCodes.ReceptionRejected);
        Assert.Equal(FiscalDocumentStatus.Rejected, doc.Status);
        Assert.False(doc.IsActive);
        Assert.Equal("fiscal.state", Assert.Throws<DomainException>(() => doc.Reject(904)).Code);
        Assert.Equal("fiscal.state", Assert.Throws<DomainException>(() => doc.MarkNoResponse()).Code);
        Assert.Empty(doc.DomainEvents);
    }

    [Theory]
    [InlineData(true, false, FiscalDocumentStatus.Valid, 1)]
    [InlineData(true, true, FiscalDocumentStatus.DuplicateToVoid, 0)]
    [InlineData(false, true, FiscalDocumentStatus.Discarded, 0)]
    public void Sin_respuesta_se_resuelve_tras_verificar_el_estado(bool registered, bool reissued, FiscalDocumentStatus expected, int events)
    {
        var doc = _t.Invoice();
        doc.MarkNoResponse();
        Assert.Equal(FiscalDocumentStatus.NoResponse, doc.Status);
        doc.ResolveNoResponse(registered, reissued, 908, _t.Now);
        Assert.Equal(expected, doc.Status);
        Assert.Equal(events, doc.DomainEvents.Count);
        Assert.Equal("fiscal.state", Assert.Throws<DomainException>(() => doc.ResolveNoResponse(true, false, null, _t.Now)).Code);
    }

    [Fact]
    public void Sin_respuesta_que_el_SIN_si_registro_se_acepta()
    {
        var doc = _t.Invoice();
        doc.MarkNoResponse();
        doc.Accept(null, SiatCodes.ReceptionValidated, _t.Now);
        Assert.Equal(FiscalDocumentStatus.Valid, doc.Status);
    }

    [Fact]
    public void Paquete_de_contingencia_confirma_rechaza_o_devuelve_a_la_cola()
    {
        var offline = _t.Invoice(emission: _t.Emission(emissionType: SiatCodes.EmissionOffline));
        var package = Guid.NewGuid();
        Assert.Equal("fiscal.package_position", Assert.Throws<DomainException>(() => offline.AddToPackage(package, 0)).Code);
        Assert.Equal("fiscal.package_position", Assert.Throws<DomainException>(() => offline.AddToPackage(package, 501)).Code);
        offline.AddToPackage(package, 7);
        Assert.Equal((FiscalDocumentStatus.InPackage, package, 7), (offline.Status, offline.PackageId!.Value, offline.PackagePosition!.Value));
        offline.ReturnToOfflineQueue();
        Assert.Equal(FiscalDocumentStatus.Offline, offline.Status);
        Assert.Null(offline.PackageId);
        Assert.Null(offline.PackagePosition);
        offline.AddToPackage(package, 1);
        offline.ConfirmInPackage(SiatCodes.ReceptionValidated, _t.Now);
        Assert.Equal(FiscalDocumentStatus.Valid, offline.Status);
        Assert.IsType<FiscalDocumentValidatedEvent>(Assert.Single(offline.DomainEvents));

        var observed = _t.Invoice(emission: _t.Emission(emissionType: SiatCodes.EmissionOffline));
        observed.AddToPackage(package, 2);
        observed.RejectInPackage(1013);
        Assert.Equal(FiscalDocumentStatus.PackageRejected, observed.Status);
        Assert.False(observed.IsActive);

        Assert.Equal("fiscal.state", Assert.Throws<DomainException>(() => _t.Invoice().AddToPackage(package, 1)).Code);
        Assert.Equal("fiscal.state", Assert.Throws<DomainException>(() => _t.Invoice().ConfirmInPackage(908, _t.Now)).Code);
    }

    [Fact]
    public void Anular_revertir_una_sola_vez_y_no_volver_a_anular()
    {
        var doc = Valid();
        var fiscalNow = _t.IssuedAt.AddDays(3);
        Assert.True(doc.CanVoidAt(fiscalNow));
        doc.EnsureVoidable(fiscalNow);
        doc.Void(1, SiatCodes.VoidConfirmed, _t.Now);
        Assert.Equal(FiscalDocumentStatus.Voided, doc.Status);
        Assert.Equal(1, doc.VoidReasonCode);
        Assert.False(doc.IsActive);
        var voided = Assert.IsType<FiscalDocumentVoidedEvent>(Assert.Single(doc.DomainEvents));
        Assert.Equal(IntegrationEvents.FiscalDocumentVoided, voided.EventType);
        Assert.Equal("fiscal.void_state", Assert.Throws<DomainException>(() => doc.Void(1, 905, _t.Now)).Code);

        doc.EnsureRevertible(fiscalNow);
        doc.RevertVoid(SiatCodes.RevertConfirmed, _t.Now);
        Assert.Equal(FiscalDocumentStatus.Valid, doc.Status);
        Assert.True(doc.IsReverted);
        Assert.NotNull(doc.RevertedAt);
        Assert.False(doc.CanVoidAt(fiscalNow));
        Assert.Equal("fiscal.void_reverted", Assert.Throws<DomainException>(() => doc.EnsureVoidable(fiscalNow)).Code);
        Assert.Equal("fiscal.void_reverted", Assert.Throws<DomainException>(() => doc.Void(1, 905, _t.Now)).Code);
        Assert.Equal("fiscal.revert_state", Assert.Throws<DomainException>(() => doc.RevertVoid(907, _t.Now)).Code);
        Assert.Equal("fiscal.revert_state", Assert.Throws<DomainException>(() => doc.EnsureRevertible(fiscalNow)).Code);
    }

    [Fact]
    public void Anular_fuera_de_plazo_se_rechaza_localmente()
    {
        var doc = Valid();   // emitido el 25/09/2026: se anula hasta el 09/10/2026 inclusive
        var lastMoment = new DateTime(2026, 10, 9, 23, 59, 59, 999);
        Assert.True(doc.CanVoidAt(lastMoment));
        doc.EnsureVoidable(lastMoment);
        var late = new DateTime(2026, 10, 10, 0, 0, 0);
        Assert.False(doc.CanVoidAt(late));
        Assert.Equal("fiscal.void_deadline", Assert.Throws<DomainException>(() => doc.EnsureVoidable(late)).Code);
        doc.Void(1, 905, _t.Now);
        Assert.Equal("fiscal.revert_deadline", Assert.Throws<DomainException>(() => doc.EnsureRevertible(late)).Code);
    }

    [Fact]
    public void Solo_se_anula_un_documento_valido_o_duplicado()
    {
        var pending = _t.Invoice();
        Assert.False(pending.CanVoidAt(_t.IssuedAt));
        Assert.Equal("fiscal.void_state", Assert.Throws<DomainException>(() => pending.EnsureVoidable(_t.IssuedAt)).Code);
        Assert.Equal("fiscal.void_state", Assert.Throws<DomainException>(() => pending.Void(1, 905, _t.Now)).Code);
        Assert.Equal("fiscal.revert_state", Assert.Throws<DomainException>(() => pending.EnsureRevertible(_t.IssuedAt)).Code);

        var duplicate = _t.Invoice();
        duplicate.MarkNoResponse();
        duplicate.ResolveNoResponse(registeredInSiat: true, reissued: true, 908, _t.Now);
        Assert.True(duplicate.CanVoidAt(_t.IssuedAt));
        duplicate.Void(2, SiatCodes.AlreadyVoided, _t.Now);
        Assert.Equal(FiscalDocumentStatus.Voided, duplicate.Status);
    }

    [Fact]
    public void Las_lineas_y_la_referencia_de_la_nota_quedan_en_la_sucursal_del_documento()
    {
        var note = _t.CreditNote();
        Assert.All(note.Lines, l => Assert.Equal((_t.Branch, note.Id), (l.BranchId, l.DocumentId)));
        Assert.Equal((_t.Branch, note.Id), (note.NoteReference!.BranchId, note.NoteReference.DocumentId));
    }

    [Fact]
    public void Archivo_bitacora_y_entrega_validan_sus_datos()
    {
        var doc = _t.Invoice();
        var file = new FiscalDocumentFile(_t.Tenant, _t.Branch, doc.Id, "<xml/>", new string('A', 64), _t.Now);
        Assert.Equal(new string('a', 64), file.GzipSha256);
        Assert.Equal("guard.text", Assert.Throws<DomainException>(() =>
            new FiscalDocumentFile(_t.Tenant, _t.Branch, doc.Id, "<xml/>", "abc", _t.Now)).Code);
        var e = new FiscalDocumentEvent(_t.Tenant, _t.Branch, doc.Id, FiscalDocumentAction.Sent, _t.Now, 908, new string('x', 600), "R", null, null);
        Assert.Equal(500, e.Description!.Length);
        var delivery = new FiscalDelivery(_t.Tenant, _t.Branch, doc.Id, FiscalDeliveryChannel.Email, "juan@example.com", true, null, _t.Now, null);
        Assert.True(delivery.Succeeded);
    }

    private FiscalDocument Valid()
    {
        var doc = _t.Invoice();
        doc.Accept("REC", SiatCodes.ReceptionValidated, _t.Now);
        doc.ClearDomainEvents();
        return doc;
    }
}
