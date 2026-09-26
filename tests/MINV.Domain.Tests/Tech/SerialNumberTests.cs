using MINV.Domain.Catalog;
using MINV.Domain.Common;
using MINV.Domain.Inventory;

namespace MINV.Domain.Tests.Tech;

/// <summary>V4.2 · IMEI (15 dígitos + Luhn) y ciclo de vida de una serie (regla T-02): transiciones válidas e inválidas,
/// sucursal y posición esperadas, y una fila de bitácora por cada cambio.</summary>
public sealed class SerialNumberTests
{
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _branch = Guid.NewGuid();
    private readonly Guid _otherBranch = Guid.NewGuid();
    private readonly Guid _user = Guid.NewGuid();
    private readonly Guid _variant = Guid.NewGuid();
    private readonly DateTimeOffset _now = new(2026, 9, 25, 16, 0, 0, TimeSpan.Zero);
    private readonly Batch _batch;
    private readonly StockLevel _level;

    public SerialNumberTests()
    {
        _batch = Batch.CreateDefault(_tenant, _variant);
        _level = StockLevel.Open(_tenant, _branch, Guid.NewGuid(), _batch.Id);
    }

    private SerialContext At(Guid? branch = null, string? doc = null) => new(branch ?? _branch, _user, _now, doc);

    private SerialNumber InStock(string serial = "sn-0001") => SerialNumber.Receive(SerialKind.Serial, serial, _batch, _level, At(doc: "RC-CM-000001"));

    [Theory]
    [InlineData("490154203237518")]
    [InlineData("352099001761481")]
    [InlineData("35-209900-176148-1")]
    [InlineData(" 35 209900 176148 1 ")]
    public void Un_IMEI_valido_tiene_15_digitos_y_digito_de_Luhn(string imei)
    {
        Assert.True(Imei.IsValid(imei));
        Assert.Equal(15, SerialNumber.Normalize(SerialKind.Imei, imei).Length);
    }

    [Theory]
    [InlineData("490154203237517")]      // dígito verificador incorrecto
    [InlineData("49015420323751")]       // 14 dígitos
    [InlineData("4901542032375180")]     // 16 dígitos
    [InlineData("49015420323751A")]      // letra
    [InlineData("")]
    [InlineData(null)]
    public void Un_IMEI_invalido_se_rechaza_con_serial_imei_invalid(string? imei)
    {
        Assert.False(Imei.IsValid(imei));
        var error = Assert.Throws<DomainException>(() => SerialNumber.Normalize(SerialKind.Imei, imei));
        Assert.Equal("serial.imei_invalid", error.Code);
    }

    [Fact]
    public void El_digito_de_Luhn_se_calcula_sobre_los_14_primeros()
    {
        Assert.Equal(8, Imei.CheckDigit("49015420323751"));
        Assert.Equal(1, Imei.CheckDigit("35209900176148"));
        Assert.Equal("352099001761481", Imei.Normalize("35-209900-176148-1"));
    }

    [Fact]
    public void La_serie_se_normaliza_y_no_admite_espacios_ni_comas()
    {
        Assert.Equal("SN-ABC/123", SerialNumber.Normalize(SerialKind.Serial, "  sn-abc/123 "));
        Assert.Equal("serial.format", Assert.Throws<DomainException>(() => SerialNumber.Normalize(SerialKind.Serial, "SN 1")).Code);
        Assert.Equal("serial.format", Assert.Throws<DomainException>(() => SerialNumber.Normalize(SerialKind.Serial, "SN1,SN2")).Code);
        Assert.Throws<DomainException>(() => SerialNumber.Normalize(SerialKind.Serial, new string('X', 81)));
        Assert.Throws<DomainException>(() => SerialNumber.Normalize(SerialKind.Serial, "  "));
    }

    [Fact]
    public void Recibir_deja_la_serie_en_stock_con_su_variante_lote_existencia_y_bitacora()
    {
        var unit = InStock();
        Assert.Equal("SN-0001", unit.Serial);
        Assert.Equal(_variant, unit.VariantId);
        Assert.Equal(_batch.Id, unit.BatchId);
        Assert.Equal(_level.Id, unit.StockLevelId);
        Assert.Equal(SerialNumberStatus.InStock, unit.Status);
        Assert.True(unit.IsOnHand && unit.IsAvailable);
        Assert.Equal(_now, unit.ReceivedAt);
        var received = Assert.Single(unit.History);
        Assert.Equal((SerialEventAction.Received, unit.Id, (Guid?)_branch, "RC-CM-000001", (Guid?)_user),
            (received.Action, received.SerialNumberId, received.BranchId, received.DocumentNumber, received.UserId));
    }

    [Fact]
    public void Un_IMEI_se_recibe_solo_si_es_valido()
    {
        var phone = SerialNumber.Receive(SerialKind.Imei, "35-209900-176148-1", _batch, _level, At());
        Assert.Equal("352099001761481", phone.Serial);
        Assert.Equal(SerialKind.Imei, phone.Kind);
        Assert.Throws<DomainException>(() => SerialNumber.Receive(SerialKind.Imei, "352099001761482", _batch, _level, At()));
    }

    [Fact]
    public void Recibir_exige_existencia_del_lote_de_la_variante_y_de_la_sucursal_del_hecho()
    {
        var otherBatch = Batch.CreateDefault(_tenant, _variant);
        Assert.Equal("serial.batch", Assert.Throws<DomainException>(() =>
            SerialNumber.Receive(SerialKind.Serial, "A1", otherBatch, _level, At())).Code);
        Assert.Equal("serial.branch", Assert.Throws<DomainException>(() =>
            SerialNumber.Receive(SerialKind.Serial, "A1", _batch, _level, At(_otherBranch))).Code);
        var otherTenantLevel = StockLevel.Open(Guid.NewGuid(), _branch, Guid.NewGuid(), _batch.Id);
        Assert.Equal("tenant.mismatch", Assert.Throws<DomainException>(() =>
            SerialNumber.Receive(SerialKind.Serial, "A1", _batch, otherTenantLevel, At())).Code);
    }

    [Fact]
    public void Ciclo_completo_recepcion_venta_devolucion_RMA_reposicion_y_proveedor()
    {
        var unit = InStock();
        unit.Sell(_level, At(doc: "F-CM-000010"));
        Assert.Equal(SerialNumberStatus.Sold, unit.Status);
        Assert.Null(unit.StockLevelId);
        Assert.False(unit.IsOnHand);

        unit.Return(null, null, At(doc: "DV-CM-000001"));           // devuelta a revisión (sin reingreso)
        Assert.Equal(SerialNumberStatus.Returned, unit.Status);
        unit.SendToRma(At(doc: "RMA-CM-000001"));
        Assert.Equal(SerialNumberStatus.InRma, unit.Status);
        unit.SendToSupplier(At(doc: "RMA-CM-000001"));
        unit.MarkReplaced(At(doc: "RMA-CM-000001"));
        Assert.Equal(SerialNumberStatus.InRma, unit.Status);        // sigue en RMA hasta devolverla al proveedor
        unit.ReturnToSupplier(null, At(doc: "RMA-CM-000001"));
        Assert.Equal(SerialNumberStatus.ReturnedToSupplier, unit.Status);
        unit.Restock(_level, _batch, At(doc: "RC-CM-000002"));      // el proveedor la manda de vuelta reparada
        Assert.Equal((SerialNumberStatus.InStock, (Guid?)_level.Id), (unit.Status, unit.StockLevelId));

        Assert.Equal(new[]
        {
            SerialEventAction.Received, SerialEventAction.Sold, SerialEventAction.Returned, SerialEventAction.RmaReceived,
            SerialEventAction.SentToSupplier, SerialEventAction.Replaced, SerialEventAction.ReturnedToSupplier, SerialEventAction.Restocked,
        }, unit.History.Select(e => e.Action));
        Assert.All(unit.History, e => Assert.Equal(unit.Id, e.SerialNumberId));
    }

    [Fact]
    public void Devolucion_con_reingreso_vuelve_a_stock_y_RMA_reparado_vuelve_a_su_dueno()
    {
        var unit = InStock();
        unit.Sell(_level, At());
        unit.Return(_level, _batch, At());
        Assert.Equal((SerialNumberStatus.InStock, (Guid?)_level.Id), (unit.Status, unit.StockLevelId));

        unit.Sell(_level, At());
        unit.SendToRma(At());
        unit.MarkRepaired(At());
        unit.ReturnFromRma(At());
        Assert.Equal(SerialNumberStatus.Sold, unit.Status);
        Assert.Equal(SerialEventAction.ReturnedToCustomer, unit.History.Last().Action);
    }

    [Fact]
    public void Transferencia_saca_la_serie_del_origen_la_deja_en_transito_y_la_recibe_en_el_destino()
    {
        var unit = InStock();
        var destination = StockLevel.Open(_tenant, _otherBranch, Guid.NewGuid(), _batch.Id);
        Assert.Equal("serial.branch", Assert.Throws<DomainException>(() => unit.TransferOut(_level, At(_otherBranch))).Code);
        unit.TransferOut(_level, At(doc: "TR-CM-000001"));
        Assert.Equal(SerialNumberStatus.InTransit, unit.Status);
        Assert.False(unit.IsOnHand);
        Assert.Equal("serial.state", Assert.Throws<DomainException>(() => unit.Sell(_level, At())).Code);
        Assert.Equal("serial.branch", Assert.Throws<DomainException>(() => unit.TransferIn(destination, _batch, At())).Code);
        unit.TransferIn(destination, _batch, At(_otherBranch, "TR-CM-000001"));
        Assert.Equal((SerialNumberStatus.InStock, (Guid?)destination.Id), (unit.Status, unit.StockLevelId));
        Assert.Equal((Guid?)_otherBranch, unit.History.Last().BranchId);
    }

    [Fact]
    public void Una_serie_solo_se_vende_desde_su_posicion_y_su_sucursal()
    {
        var unit = InStock();
        var elsewhere = StockLevel.Open(_tenant, _branch, Guid.NewGuid(), _batch.Id);
        Assert.Equal("serial.location", Assert.Throws<DomainException>(() => unit.Sell(elsewhere, At())).Code);
        Assert.Equal("serial.branch", Assert.Throws<DomainException>(() => unit.Sell(_level, At(_otherBranch))).Code);
        Assert.Equal(SerialNumberStatus.InStock, unit.Status);
        Assert.Single(unit.History);   // un intento rechazado no deja bitácora
    }

    [Fact]
    public void Una_serie_vendida_en_RMA_devuelta_al_proveedor_o_de_baja_no_se_vende()
    {
        var sold = InStock("S1");
        sold.Sell(_level, At());
        var rma = InStock("S2");
        rma.Sell(_level, At());
        rma.SendToRma(At());
        var supplier = InStock("S3");
        supplier.ReturnToSupplier(_level, At());
        var scrapped = InStock("S4");
        scrapped.Scrap(_level, At());
        foreach (var unit in new[] { sold, rma, supplier, scrapped })
        {
            Assert.False(unit.IsAvailable);
            Assert.False(unit.IsOnHand);
            Assert.Null(unit.StockLevelId);
            Assert.Equal("serial.state", Assert.Throws<DomainException>(() => unit.Sell(_level, At())).Code);
            Assert.Equal("serial.state", Assert.Throws<DomainException>(() => unit.TransferOut(_level, At())).Code);
            Assert.Equal("serial.state", Assert.Throws<DomainException>(() => unit.IssueAsReplacement(_level, At())).Code);
        }
    }

    [Fact]
    public void Transiciones_invalidas_se_rechazan_con_serial_state()
    {
        var unit = InStock();
        void Invalid(Action action) => Assert.Equal("serial.state", Assert.Throws<DomainException>(action).Code);
        Invalid(() => unit.Return(null, null, At()));          // no vendida
        Invalid(() => unit.SendToRma(At()));                   // en stock: no es de un cliente
        Invalid(() => unit.ReturnFromRma(At()));
        Invalid(() => unit.MarkRepaired(At()));
        Invalid(() => unit.Restock(_level, _batch, At()));
        Invalid(() => unit.TransferIn(_level, _batch, At()));
        unit.Scrap(_level, At());
        Invalid(() => unit.Restock(_level, _batch, At()));      // la baja es definitiva
        Invalid(() => unit.ReturnToSupplier(null, At()));
        Invalid(() => unit.Scrap(null, At()));
        Assert.Equal(new[] { SerialEventAction.Received, SerialEventAction.Scrapped }, unit.History.Select(e => e.Action));
    }

    [Fact]
    public void Salir_del_stock_exige_indicar_la_existencia()
    {
        var unit = InStock();
        Assert.Equal("serial.location", Assert.Throws<DomainException>(() => unit.ReturnToSupplier(null, At())).Code);
        Assert.Equal("serial.location", Assert.Throws<DomainException>(() => unit.Scrap(null, At())).Code);
        unit.IssueAsReplacement(_level, At(doc: "RMA-CM-000002"));
        Assert.Equal(SerialNumberStatus.Sold, unit.Status);
        Assert.Equal(SerialEventAction.ReplacementIssued, unit.History.Last().Action);
    }

    [Fact]
    public void Una_perdida_en_transito_se_da_de_baja_sin_existencia()
    {
        var unit = InStock();
        unit.TransferOut(_level, At());
        unit.Scrap(null, At(doc: "TR-CM-000001"));
        Assert.Equal(SerialNumberStatus.Scrapped, unit.Status);
    }
}
