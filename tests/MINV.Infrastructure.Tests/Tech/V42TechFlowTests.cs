using Microsoft.EntityFrameworkCore;
using MINV.Application.Billing;
using MINV.Application.Catalog;
using MINV.Application.Common;
using MINV.Application.Corporate;
using MINV.Application.Inventory.Movements;
using MINV.Application.Inventory.PhysicalCounts;
using MINV.Application.Inventory.Transfers;
using MINV.Application.Sales;
using MINV.Application.Tech;
using MINV.Domain.Accounting;
using MINV.Domain.Billing;
using MINV.Domain.Catalog;
using MINV.Domain.Common;
using MINV.Domain.Inventory;
using MINV.Domain.Sales;
using MINV.Domain.Service;
using MINV.Infrastructure.Tests.Billing;

namespace MINV.Infrastructure.Tests.Tech;

/// <summary>
/// V4.2 · Casos de uso de la edición Tecnología de punta a punta sobre la base en memoria, con la tubería completa de
/// MediatR y el simulador del SIN en proceso (la misma empresa que factura del agente E): ciclo de vida de una serie
/// (recepción → venta con factura válida y numeroSerie → devolución con nota → RMA → reposición → trazabilidad), rechazos de
/// series, transferencias con faltantes, armador de PC (candidatos, cotización, incompatible sin confirmar, venta de la
/// cotización), fichas técnicas y facetas, y la división de la línea fiscal cuando las series no caben.
/// </summary>
[Collection(E_BillingCollection.Name)]
public sealed class V42TechFlowTests
{
    [Fact]
    public async Task Ciclo_de_vida_de_una_serie_de_la_recepcion_a_la_reposicion_por_garantia()
    {
        await using var host = await E_BillingTestHost.CreateAsync();
        await RunLifecycleAsync(host);
    }

    /// <summary>Ciclo de vida completo (también lo corre la prueba contra PostgreSQL).</summary>
    internal static async Task RunLifecycleAsync(E_BillingTestHost host)
    {
        var tech = await V42TechScenario.CreateAsync(host);
        var gpus = V42TechScenario.Serials("GPU-4060", 1, 4);
        var receipt = await tech.ReceiveAsync(("GPU-4060", gpus));
        Assert.Equal(4m, await host.OnHandAsync("GPU-4060"));
        Assert.Equal(0, await tech.BreachesAsync());
        using var cashier = await host.CashierAsync();
        using var admin = await host.AdminAsync();
        Assert.Equal(gpus, (await cashier.Send(new GetAvailableSerialsQuery("GPU-4060"))).Select(s => s.Serial).Order(StringComparer.Ordinal));

        // Venta de dos unidades con sus series (en minúsculas: se normalizan): la factura del SIN lleva numeroSerie
        var sale = await E_BillingTestHost.SellAsync(cashier, E_BillingTestHost.Ci(),
            new SaleLineInput("GPU-4060", 2, 0, [gpus[0], gpus[1].ToLowerInvariant()]));
        Assert.Equal(6398m, sale.Total);
        var xml = await tech.XmlAsync(sale.FiscalDocumentId!.Value);
        tech.Validate(xml, SiatCodes.SectorPurchaseSale);
        Assert.Contains($"<numeroSerie>{gpus[0]}, {gpus[1]}</numeroSerie>", xml, StringComparison.Ordinal);
        Assert.Equal(gpus[..2], Assert.Single(await cashier.Send(new GetSaleLinesQuery(sale.InvoiceNumber))).Serials);
        Assert.Equal(1, (await cashier.Send(new DispatchFiscalDocumentsCommand(sale.FiscalDocumentId))).Valid);
        Assert.Equal(FiscalDocumentStatus.Valid, (await host.DocumentAsync(sale.FiscalDocumentId.Value)).Status);
        Assert.Equal(SerialNumberStatus.Sold, (await tech.SerialAsync(gpus[0])).Status);
        Assert.Equal(2m, await host.OnHandAsync("GPU-4060"));
        Assert.Equal(0, await tech.BreachesAsync());

        // Garantía derivada (T-04): fecha de la venta + 36 meses, calculada al consultar
        var today = DateOnly.FromDateTime(host.FiscalNow);
        var warranty = await cashier.Send(new GetWarrantyStatusQuery(gpus[0]));
        Assert.Equal(today, warranty.SoldOn);
        Assert.Equal(today.AddMonths(36), warranty.WarrantyUntil);
        Assert.True(warranty.InWarranty);
        Assert.Equal(sale.InvoiceNumber, warranty.InvoiceNumber);
        Assert.Equal("CF", warranty.CustomerCode);

        // Devolución de una unidad por su serie: vuelve al stock y la nota crédito-débito la lleva en su línea «devuelto»
        var returned = await admin.Send(new CreateSalesReturnCommand(sale.InvoiceNumber, "El cliente se arrepintió", "EFECTIVO",
            [new ReturnLineInput("GPU-4060", 1, [gpus[1]])]));
        Assert.Equal(3199m, returned.Refund);
        Assert.Equal(SerialNumberStatus.InStock, (await tech.SerialAsync(gpus[1])).Status);
        Assert.Equal(3m, await host.OnHandAsync("GPU-4060"));
        var note = await host.DocumentAsync(returned.CreditNoteId!.Value);
        Assert.Equal(gpus[1], Assert.Single(note.Lines, l => l.TransactionCode == 2).SerialNumber);
        tech.Validate(await tech.XmlAsync(note.Id), SiatCodes.SectorCreditDebitNote);
        Assert.Equal(1, (await admin.Send(new DispatchFiscalDocumentsCommand(note.Id))).Valid);
        var again = await Assert.ThrowsAsync<DomainException>(() => admin.Send(new CreateSalesReturnCommand(sale.InvoiceNumber, "Otra vez", "EFECTIVO",
            [new ReturnLineInput("GPU-4060", 1, [gpus[1]])])));
        Assert.Equal(SerialErrorCodes.NotAvailable, again.Code);

        // RMA de la otra unidad: sin entrada de stock; un segundo caso de la misma serie se rechaza
        var claim = await cashier.Send(new OpenWarrantyClaimCommand(gpus[0], "No da video al encender"));
        Assert.Equal("RMA-CM-000001", claim.Number);
        Assert.True(claim.IsInWarranty);
        Assert.Equal(today.AddMonths(36), claim.WarrantyUntil);
        Assert.Equal(SerialNumberStatus.InRma, (await tech.SerialAsync(gpus[0])).Status);
        Assert.Equal(3m, await host.OnHandAsync("GPU-4060"));
        Assert.Equal(SerialErrorCodes.NotAvailable, (await Assert.ThrowsAsync<DomainException>(() =>
            cashier.Send(new OpenWarrantyClaimCommand(gpus[0], "Otra falla distinta")))).Code);
        await admin.Send(new MoveWarrantyClaimCommand(claim.Number, WarrantyClaimStatus.Diagnosing, Note: "Revisión en el taller"));
        Assert.Equal("rma.transition", (await Assert.ThrowsAsync<DomainException>(() =>
            admin.Send(new MoveWarrantyClaimCommand(claim.Number, WarrantyClaimStatus.Delivered)))).Code);

        // Reposición: otra unidad sale del stock (REPOSICIÓN POR GARANTÍA) con su asiento 5.1.10 / 1.1.05 al costo promedio
        var replaced = await admin.Send(new IssueWarrantyReplacementCommand(claim.Number, gpus[2], "Falla de fábrica: se entrega otra unidad"));
        Assert.Equal(WarrantyClaimStatus.Replaced, replaced.Status);
        Assert.Equal(gpus[2], replaced.ReplacementSerial);
        Assert.Equal(SerialNumberStatus.Sold, (await tech.SerialAsync(gpus[2])).Status);
        Assert.Equal(SerialNumberStatus.InRma, (await tech.SerialAsync(gpus[0])).Status);
        Assert.Equal(2m, await host.OnHandAsync("GPU-4060"));
        var (debit, credit) = await tech.DbAsync(async db =>
        {
            var entry = await db.Set<JournalEntry>().AsNoTracking().Include(e => e.Lines)
                .FirstAsync(e => e.Description.StartsWith("Reposición por garantía " + claim.Number));
            var accounts = await db.Set<Account>().ToDictionaryAsync(a => a.Id, a => a.Code);
            return (entry.Lines.Where(l => accounts[l.AccountId] == AccountCodes.WarrantyCost).Sum(l => l.Debit),
                entry.Lines.Where(l => accounts[l.AccountId] == AccountCodes.Inventory).Sum(l => l.Credit));
        });
        Assert.Equal(2700m, debit);
        Assert.Equal(2700m, credit);
        Assert.Equal(1m, await tech.DbAsync(db => (from m in db.Set<StockMovement>()
                                                   join t in db.Set<MovementType>() on m.MovementTypeId equals t.Id
                                                   where t.Code == MovementTypeCodes.WarrantyReplacement && m.DocumentReference == claim.Number
                                                   select m.Quantity).SumAsync()));

        // Entrega, destino de la defectuosa y trazabilidad de punta a punta
        var closed = await admin.Send(new MoveWarrantyClaimCommand(claim.Number, WarrantyClaimStatus.Delivered));
        Assert.NotNull(closed.ClosedAt);
        await admin.Send(new DisposeSerialCommand(gpus[0], SerialDisposal.ReturnToSupplier, "Devuelta al mayorista por la garantía"));
        Assert.Equal(SerialNumberStatus.ReturnedToSupplier, (await tech.SerialAsync(gpus[0])).Status);
        var trace = await cashier.Send(new GetSerialTraceQuery(gpus[0]));
        Assert.Equal([SerialEventAction.Received, SerialEventAction.Sold, SerialEventAction.RmaReceived, SerialEventAction.Replaced,
            SerialEventAction.ReturnedToSupplier], trace.Events.Select(e => e.Action));
        Assert.Equal(receipt.ReceiptNumber, trace.ReceiptNumber);
        Assert.Equal("Mayorista Tecnológico S.R.L.", trace.Supplier);
        Assert.All(trace.Events, e => Assert.Equal("CM", e.Branch));
        Assert.All(trace.Events, e => Assert.False(string.IsNullOrEmpty(e.User)));
        Assert.Equal(sale.InvoiceNumber, trace.Events[1].DocumentNumber);
        Assert.Equal(claim.Number, Assert.Single(trace.Claims).Number);
        var replacement = await cashier.Send(new GetSerialTraceQuery(gpus[2]));
        Assert.Equal([SerialEventAction.Received, SerialEventAction.ReplacementIssued], replacement.Events.Select(e => e.Action));
        var replacementWarranty = await cashier.Send(new GetWarrantyStatusQuery(gpus[2]));
        Assert.True(replacementWarranty.InWarranty);
        Assert.Equal("CF", replacementWarranty.CustomerCode);
        Assert.Equal(0, await tech.BreachesAsync());

        var dashboard = await admin.Send(new GetTechDashboardQuery());
        Assert.Equal(0, dashboard.SerializedWithoutSerials);
        Assert.Equal(2, dashboard.SerialsInStock);
        Assert.Equal(2m, Assert.Single(dashboard.TopGpus).Quantity);
        Assert.Equal(0, dashboard.OpenClaims);
    }

    [Fact]
    public async Task Las_series_se_rechazan_con_codigos_estables()
    {
        await using var host = await E_BillingTestHost.CreateAsync();
        var tech = await V42TechScenario.CreateAsync(host);
        var gpus = V42TechScenario.Serials("GPU-4060", 1, 3);
        await tech.ReceiveAsync(("GPU-4060", gpus), ("TAB-LTE", [V42TechScenario.ImeiFor(1)]));
        using var cashier = await host.CashierAsync();
        using var admin = await host.AdminAsync();
        async Task<string> Rejected(IReadOnlyList<SaleLineInput> lines) =>
            (await Assert.ThrowsAsync<DomainException>(() => cashier.Send(new CheckoutCommand("CF", "EFECTIVO", lines, 100_000m, null,
                E_BillingTestHost.Ci())))).Code;

        Assert.Equal(SerialErrorCodes.Required, await Rejected([new SaleLineInput("GPU-4060", 1)]));
        Assert.Equal(SerialErrorCodes.Count, await Rejected([new SaleLineInput("GPU-4060", 2, 0, [gpus[0]])]));
        Assert.Equal(SerialErrorCodes.Duplicate, await Rejected([new SaleLineInput("GPU-4060", 2, 0, [gpus[0], gpus[0]])]));
        Assert.Equal(SerialErrorCodes.Duplicate, await Rejected([new SaleLineInput("GPU-4060", 1, 0, [gpus[0]]), new SaleLineInput("GPU-4060", 1, 0, [gpus[0]])]));
        Assert.Equal(SerialErrorCodes.NotFound, await Rejected([new SaleLineInput("GPU-4060", 1, 0, ["NO-EXISTE-1"])]));
        Assert.Equal(SerialErrorCodes.NotTracked, await Rejected([new SaleLineInput("FER-001", 1, 0, ["X1"])]));
        Assert.Equal(SerialErrorCodes.ImeiInvalid, await Rejected([new SaleLineInput("TAB-LTE", 1, 0, ["352099001761482"])]));
        Assert.Equal(0, await tech.BreachesAsync());

        // Una serie vendida ya no está disponible; los productos sin serie se venden igual que siempre
        var sale = await E_BillingTestHost.SellAsync(cashier, E_BillingTestHost.Ci(), new SaleLineInput("GPU-4060", 1, 0, [gpus[0]]),
            new SaleLineInput("FER-001", 2));
        Assert.Equal(3199m + 97m, sale.Total);
        Assert.Equal(SerialErrorCodes.NotAvailable, await Rejected([new SaleLineInput("GPU-4060", 1, 0, [gpus[0]])]));
        // Recibir una serie que ya existe
        Assert.Equal(SerialErrorCodes.Duplicate, (await Assert.ThrowsAsync<DomainException>(() => tech.ReceiveAsync(("GPU-4060", [gpus[1]])))).Code);

        // Movimientos: entrada y ajuste con sus series (misma transacción), venta por movimiento no, ajuste sin series no
        Assert.Equal(SerialErrorCodes.Required, (await Assert.ThrowsAsync<DomainException>(() => admin.Send(new RegisterMovementCommand("GPU-4060",
            E_BillingTestHost.Bin, MovementTypeCodes.AdjustmentIn, 1, null, "AJ-1", "Sobrante del conteo")))).Code);
        await admin.Send(new RegisterMovementCommand("GPU-4060", E_BillingTestHost.Bin, MovementTypeCodes.AdjustmentIn, 1, null, "AJ-1", "Sobrante del conteo",
            Serials: ["GPU-4060-S0100"]));
        Assert.Equal(SerialNumberStatus.InStock, (await tech.SerialAsync("GPU-4060-S0100")).Status);
        Assert.Equal(SerialErrorCodes.UseDocument, (await Assert.ThrowsAsync<DomainException>(() => admin.Send(new RegisterMovementCommand("GPU-4060",
            E_BillingTestHost.Bin, MovementTypeCodes.Sale, 1, null, "X", "Venta", Serials: [gpus[1]])))).Code);
        await admin.Send(new RegisterMovementCommand("GPU-4060", E_BillingTestHost.Bin, MovementTypeCodes.AdjustmentOut, 1, null, "AJ-2", "Llegó rota",
            Serials: ["GPU-4060-S0100"]));
        Assert.Equal(SerialNumberStatus.Scrapped, (await tech.SerialAsync("GPU-4060-S0100")).Status);
        Assert.Equal(0, await tech.BreachesAsync());

        // Otra sucursal: una serie que está allá no se vende aquí
        await admin.Send(new CreateBranchCommand("SB", "Sucursal B", "ALMSB", "Almacén B"));
        await admin.Send(new RegisterMovementCommand("GPU-4060", "ALMSB-GENERAL", MovementTypeCodes.InitialBalance, 1, null, "INV-SB", "Inventario inicial",
            Serials: ["GPU-4060-S0200"]));
        Assert.Equal(SerialErrorCodes.WrongBranch, await Rejected([new SaleLineInput("GPU-4060", 1, 0, ["GPU-4060-S0200"])]));
        Assert.Equal(0, await tech.BreachesAsync());

        // Toma física: un producto con serie no se ajusta por cantidad
        var count = await admin.Send(new OpenPhysicalCountCommand("ALM01"));
        await admin.Send(new RecordCountCommand(count.PhysicalCountId, "GPU-4060", E_BillingTestHost.Bin, 5));
        Assert.Equal("serial.count_adjustment", (await Assert.ThrowsAsync<DomainException>(() =>
            admin.Send(new PostPhysicalCountCommand(count.PhysicalCountId, true)))).Code);

        // Control por serie: no se deja con unidades serializadas en stock; no se activa con unidades sin serie hasta registrarlas
        Assert.Equal("tech.serials_on_hand", (await Assert.ThrowsAsync<DomainException>(() =>
            admin.Send(new SaveProductTechCommand("GPU-4060", false, SerialKind.Serial, 36, [new ProductSpecInput("condicion", ["Nuevo"]),
                new ProductSpecInput("vram", ["8"]), new ProductSpecInput("largo", ["240"]), new ProductSpecInput("consumo", ["115"])])))).Code);
        Assert.Equal("tech.serials_pending", (await Assert.ThrowsAsync<DomainException>(() =>
            admin.Send(new SaveProductTechCommand("AMO-001", true, SerialKind.Serial, 12, [])))).Code);
        var amo = Enumerable.Range(1, 20).Select(i => $"AMO-{i:000}").ToList();
        Assert.Equal(SerialErrorCodes.Count, (await Assert.ThrowsAsync<DomainException>(() =>
            admin.Send(new RegisterStockSerialsCommand("AMO-001", Enumerable.Range(1, 21).Select(i => $"AMO-{i:000}").ToList())))).Code);
        Assert.Contains("quedan 0", await admin.Send(new RegisterStockSerialsCommand("AMO-001", amo)), StringComparison.Ordinal);
        await admin.Send(new SaveProductTechCommand("AMO-001", true, SerialKind.Serial, 12, []));
        Assert.Equal(0, await tech.BreachesAsync());
        var amoSale = await E_BillingTestHost.SellAsync(cashier, E_BillingTestHost.Ci(), new SaleLineInput("AMO-001", 1, 0, ["amo-001"]));
        Assert.Contains("<numeroSerie>AMO-001</numeroSerie>", await tech.XmlAsync(amoSale.FiscalDocumentId!.Value), StringComparison.Ordinal);
        Assert.Equal(SerialErrorCodes.Required, await Rejected([new SaleLineInput("AMO-001", 1)]));
    }

    [Fact]
    public async Task Las_transferencias_llevan_sus_series_y_el_faltante_dice_cuales_no_llegaron()
    {
        await using var host = await E_BillingTestHost.CreateAsync();
        await RunTransferAsync(host);
    }

    /// <summary>Transferencia con series y faltante (también la corre la prueba contra PostgreSQL).</summary>
    internal static async Task RunTransferAsync(E_BillingTestHost host)
    {
        var tech = await V42TechScenario.CreateAsync(host);
        var gpus = V42TechScenario.Serials("GPU-4060", 1, 4);
        await tech.ReceiveAsync(("GPU-4060", gpus), ("SSD-1TB", V42TechScenario.Serials("SSD-1TB", 1, 2)));
        using var admin = await host.AdminAsync();
        await admin.Send(new CreateBranchCommand("SB", "Sucursal B", "ALMSB", "Almacén B"));
        Assert.Equal(SerialErrorCodes.Required, (await Assert.ThrowsAsync<DomainException>(() => admin.Send(new CreateTransferCommand("ALMSB",
            [new TransferLineInput("GPU-4060", 3)], null, "ALM01")))).Code);
        var transfer = await admin.Send(new CreateTransferCommand("ALMSB",
            [new TransferLineInput("GPU-4060", 3, [gpus[0], gpus[1], gpus[2]]), new TransferLineInput("FER-001", 5)], "Reposición de la sucursal B", "ALM01"));
        await admin.Send(new DispatchTransferCommand(transfer.Id));
        Assert.All(gpus[..3], s => Assert.Equal(SerialNumberStatus.InTransit, tech.SerialAsync(s).Result.Status));
        Assert.Equal(1m, await host.OnHandAsync("GPU-4060"));
        Assert.Equal(0, await tech.BreachesAsync());
        var detail = await admin.Send(new GetTransferQuery(transfer.Id));
        Assert.Equal(gpus[..3], detail.Lines.Single(l => l.Sku == "GPU-4060").Serials);

        // Faltante de un producto serializado: DEBE decir qué series no llegaron
        Assert.Equal(SerialErrorCodes.Required, (await Assert.ThrowsAsync<DomainException>(() => admin.Send(new ReceiveTransferCommand(transfer.Id,
            [new TransferReceiptInput("GPU-4060", 2, "Una caja no llegó")])))).Code);
        Assert.Equal(SerialErrorCodes.NotInDocument, (await Assert.ThrowsAsync<DomainException>(() => admin.Send(new ReceiveTransferCommand(transfer.Id,
            [new TransferReceiptInput("GPU-4060", 2, "Una caja no llegó", [gpus[3]])])))).Code);
        await admin.Send(new ReceiveTransferCommand(transfer.Id, [new TransferReceiptInput("GPU-4060", 2, "Una caja no llegó", [gpus[1]])]));
        Assert.Equal(SerialNumberStatus.Scrapped, (await tech.SerialAsync(gpus[1])).Status);
        foreach (var serial in new[] { gpus[0], gpus[2] })
        {
            var unit = await tech.SerialAsync(serial);
            Assert.Equal(SerialNumberStatus.InStock, unit.Status);
            Assert.Equal("SB", await tech.DbAsync(db => (from l in db.Set<StockLevel>()
                                                         join b in db.Set<MINV.Domain.Warehousing.Branch>() on l.BranchId equals b.Id
                                                         where l.Id == unit.StockLevelId
                                                         select b.Code).FirstAsync()));
        }
        Assert.Equal(3m, await host.OnHandAsync("GPU-4060"));
        Assert.Equal(0, await tech.BreachesAsync());
        var trace = await admin.Send(new GetSerialTraceQuery(gpus[0]));
        Assert.Equal([SerialEventAction.Received, SerialEventAction.TransferDispatched, SerialEventAction.TransferReceived],
            trace.Events.Select(e => e.Action));
        Assert.Equal(["CM", "CM", "SB"], trace.Events.Select(e => e.Branch));
    }

    [Fact]
    public async Task El_armador_marca_candidatos_exige_confirmar_lo_incompatible_y_vende_la_cotizacion_a_su_precio()
    {
        await using var host = await E_BillingTestHost.CreateAsync();
        await RunBuilderAsync(host);
    }

    /// <summary>Armador de punta a punta (también lo corre la prueba contra PostgreSQL).</summary>
    internal static async Task RunBuilderAsync(E_BillingTestHost host)
    {
        var tech = await V42TechScenario.CreateAsync(host);
        string[] parts = ["CPU-7600", "MB-B650", "RAM-D5-32", "GPU-4060", "SSD-1TB", "PSU-650", "CASE-ATX"];
        await tech.ReceiveAsync(parts.Concat(["RAM-D4-16", "GPU-4090", "CPU-14400F"]).Select(s => (s, V42TechScenario.Serials(s, 1, 2))).ToArray());
        using var cashier = await host.CashierAsync();
        List<PcBuildItemInput> Build(string ram = "RAM-D5-32") =>
        [
            new(PcSlot.Cpu, "CPU-7600"), new(PcSlot.Motherboard, "MB-B650"), new(PcSlot.Ram, ram), new(PcSlot.Gpu, "GPU-4060"),
            new(PcSlot.Storage, "SSD-1TB"), new(PcSlot.Psu, "PSU-650"), new(PcSlot.Case, "CASE-ATX"),
        ];

        var check = await cashier.Send(new CheckPcBuildQuery(Build()));
        Assert.True(check.IsCompatible);
        Assert.Empty(check.Issues);
        Assert.Equal(65 + 115 + 75, check.EstimatedDrawW);
        Assert.Equal(350, check.RecommendedPsuW);
        Assert.Equal(650, check.PsuW);
        Assert.Equal(10023m, check.Total);

        // Candidatos: la RAM DDR4 no entra en la placa DDR5, la RTX 4090 no cabe en el gabinete y el i5 no es del socket
        var rams = await cashier.Send(new GetPcBuildCandidatesQuery(PcSlot.Ram, Build().Where(i => i.Slot != PcSlot.Ram).ToList()));
        Assert.True(rams.Single(c => c.Sku == "RAM-D5-32").IsCompatible);
        var ddr4 = rams.Single(c => c.Sku == "RAM-D4-16");
        Assert.False(ddr4.IsCompatible);
        Assert.Contains("DDR4", ddr4.Reason, StringComparison.Ordinal);
        var gpusCandidates = await cashier.Send(new GetPcBuildCandidatesQuery(PcSlot.Gpu, Build()));
        Assert.Contains("357 mm", gpusCandidates.Single(c => c.Sku == "GPU-4090").Reason, StringComparison.Ordinal);
        Assert.True(gpusCandidates.Single(c => c.Sku == "GPU-4060").IsCompatible);
        Assert.Equal(["CPU-7600", "CPU-14400F"], (await cashier.Send(new GetPcBuildCandidatesQuery(PcSlot.Cpu, Build()))).Select(c => c.Sku));
        Assert.False((await cashier.Send(new GetPcBuildCandidatesQuery(PcSlot.Cpu, Build()))).Single(c => c.Sku == "CPU-14400F").IsCompatible);
        Assert.Equal("pcbuild.category", (await Assert.ThrowsAsync<DomainException>(() =>
            cashier.Send(new GetPcBuildCandidatesQuery(PcSlot.Monitor, Build())))).Code);

        // Incompatible: no se cotiza sin confirmarlo; confirmado queda marcado
        Assert.Equal("pcbuild.incompatible", (await Assert.ThrowsAsync<DomainException>(() =>
            cashier.Send(new SavePcBuildCommand(null, "PC con RAM equivocada", null, Build("RAM-D4-16"), Quote: true)))).Code);
        var marked = await cashier.Send(new SavePcBuildCommand(null, "PC con RAM equivocada", null, Build("RAM-D4-16"), Quote: true, AcceptIncompatible: true));
        Assert.True(marked.QuotedWithErrors);
        Assert.False(marked.IsCompatible);
        Assert.Equal(PcBuildStatus.Quoted, marked.Status);

        // Borrador, luego cotización (precios congelados), el precio de lista cambia y la venta usa el cotizado
        var draft = await cashier.Send(new SavePcBuildCommand(null, "PC Gamer 1080p", "CF", Build().Take(3).ToList()));
        Assert.Equal(PcBuildStatus.Draft, draft.Status);
        Assert.Equal("pcbuild.not_quoted", (await Assert.ThrowsAsync<DomainException>(() =>
            cashier.Send(new SellPcBuildCommand(draft.Number, "EFECTIVO", CashReceived: 20_000m, Buyer: E_BillingTestHost.Ci())))).Code);
        var quote = await cashier.Send(new SavePcBuildCommand(draft.Id, "PC Gamer 1080p", "CF", Build(), Quote: true, ValidDays: 7));
        Assert.Equal(draft.Number, quote.Number);
        Assert.Equal(PcBuildStatus.Quoted, quote.Status);
        Assert.True(quote.IsCompatible);
        Assert.Equal(10023m, quote.Total);
        using (var admin = await host.AdminAsync())
        {
            await admin.Send(new SaveProductCommand("GPU-4060", "GPU-4060", "Tarjeta de video RTX 4060 8 GB", null, "GPU", "UND", V42TechScenario.Supplier,
                1, 50, 2700m, 3499m, null, true));
        }
        var detail = await cashier.Send(new GetPcBuildQuery(quote.Number));
        Assert.Equal(10023m, detail.QuotedItems.Sum(i => i.Subtotal));
        Assert.Equal(10323m, detail.Check.Total);

        var serials = parts.Select(p => new SkuSerials(p, [$"{p}-S0001"])).ToList();
        Assert.Equal(SerialErrorCodes.Required, (await Assert.ThrowsAsync<DomainException>(() => cashier.Send(new SellPcBuildCommand(quote.Number, "EFECTIVO",
            serials.Where(s => s.Sku != "GPU-4060").ToList(), 20_000m, Buyer: E_BillingTestHost.Ci())))).Code);
        var sold = await cashier.Send(new SellPcBuildCommand(quote.Number, "EFECTIVO", serials, 20_000m, Buyer: E_BillingTestHost.Ci()));
        Assert.Equal(10023m, sold.Total);
        Assert.Equal(20_000m - 10023m, sold.Change);
        var xml = await tech.XmlAsync(sold.FiscalDocumentId!.Value);
        tech.Validate(xml, SiatCodes.SectorPurchaseSale);
        Assert.All(parts, p => Assert.Contains($"<numeroSerie>{p}-S0001</numeroSerie>", xml, StringComparison.Ordinal));
        Assert.Equal(1, (await cashier.Send(new DispatchFiscalDocumentsCommand(sold.FiscalDocumentId))).Valid);
        var builds = await cashier.Send(new GetPcBuildsQuery(PcBuildStatus.Sold));
        Assert.Equal(sold.InvoiceNumber, Assert.Single(builds).InvoiceNumber);
        Assert.All(parts, p => Assert.Equal(SerialNumberStatus.Sold, tech.SerialAsync($"{p}-S0001").Result.Status));
        Assert.Equal("pcbuild.state", (await Assert.ThrowsAsync<DomainException>(() =>
            cashier.Send(new SellPcBuildCommand(quote.Number, "EFECTIVO", serials, 20_000m)))).Code);
        Assert.Equal("pcbuild.state", (await Assert.ThrowsAsync<DomainException>(() => cashier.Send(new CancelPcBuildCommand(quote.Number)))).Code);
        Assert.Contains("anulado", await cashier.Send(new CancelPcBuildCommand(marked.Number)), StringComparison.Ordinal);
        Assert.Equal(0, await tech.BreachesAsync());

        using var manager = await host.AdminAsync();
        var dashboard = await manager.Send(new GetTechDashboardQuery());
        Assert.Equal(1, dashboard.BuildsSold);
        Assert.Equal(10023m, dashboard.BuildsSoldValue);
        Assert.Equal(0, dashboard.QuotesOpen);
        Assert.Contains(dashboard.SalesByCategory, c => c.Name == "Componentes" && c.Amount == 10023m);
    }

    [Fact]
    public async Task Fichas_tecnicas_con_herencia_busqueda_facetas_y_filtro_del_catalogo()
    {
        await using var host = await E_BillingTestHost.CreateAsync();
        await RunSpecsAsync(host);
    }

    internal static async Task RunSpecsAsync(E_BillingTestHost host)
    {
        var tech = await V42TechScenario.CreateAsync(host);
        await tech.ReceiveAsync(("GPU-4060", V42TechScenario.Serials("GPU-4060", 1, 2)), ("PS5-SLIM", V42TechScenario.Serials("PS5-SLIM", 1, 1)));
        using var admin = await host.AdminAsync();

        // Herencia: «condición» se define en Componentes y la heredan las tarjetas de video
        var gpuSpecs = await admin.Send(new GetSpecDefinitionsQuery("GPU"));
        Assert.Equal(["condicion", "vram", "consumo", "largo"], gpuSpecs.Select(s => s.Code));   // de la raíz a la hoja, por orden y nombre
        Assert.True(gpuSpecs[0].IsInherited);
        Assert.False(gpuSpecs[1].IsInherited);
        Assert.Equal(["Nuevo", "Reacondicionado", "Usado"], gpuSpecs[0].Options);
        Assert.Equal("spec.duplicate", (await Assert.ThrowsAsync<DomainException>(() => admin.Send(new SaveSpecDefinitionCommand("GPU", "condicion",
            "Condición", null, SpecDataType.Option, false, true, false, null, 1, ["Nuevo"])))).Code);
        Assert.Equal("spec.option_in_use", (await Assert.ThrowsAsync<DomainException>(() => admin.Send(new SaveSpecDefinitionCommand("COMP", "condicion",
            "Condición", null, SpecDataType.Option, false, true, true, null, 1, ["Reacondicionado", "Usado"])))).Code);
        Assert.Equal("spec.type_change", (await Assert.ThrowsAsync<DomainException>(() => admin.Send(new SaveSpecDefinitionCommand("GPU", "vram",
            "Memoria de video", "GB", SpecDataType.Text, false, true, false, null, 1, [])))).Code);

        // Ficha técnica tipada
        var ficha = await admin.Send(new GetProductTechQuery("GPU-4060"));
        Assert.True(ficha.TrackSerials);
        Assert.Equal(36, ficha.WarrantyMonths);
        Assert.Equal(2, ficha.SerialsInStock);
        Assert.Equal("8 GB", ficha.Specs.Single(s => s.Code == "vram").Display);
        Assert.Equal(["8"], ficha.Specs.Single(s => s.Code == "vram").Values);
        Assert.Equal("spec.option", (await Assert.ThrowsAsync<DomainException>(() => admin.Send(new SaveProductTechCommand("GPU-4060", true,
            SerialKind.Serial, 36, [new("condicion", ["Seminuevo"]), new("vram", ["8"])])))).Code);
        Assert.Equal("spec.number", (await Assert.ThrowsAsync<DomainException>(() => admin.Send(new SaveProductTechCommand("GPU-4060", true,
            SerialKind.Serial, 36, [new("condicion", ["Nuevo"]), new("vram", ["ocho"])])))).Code);
        Assert.Equal("spec.required", (await Assert.ThrowsAsync<DomainException>(() => admin.Send(new SaveProductTechCommand("GPU-4060", true,
            SerialKind.Serial, 36, [new("vram", ["8"])])))).Code);
        Assert.Equal("spec.multi", (await Assert.ThrowsAsync<DomainException>(() => admin.Send(new SaveProductTechCommand("GPU-4060", true,
            SerialKind.Serial, 36, [new("condicion", ["Nuevo", "Usado"])])))).Code);
        Assert.Equal("spec.unknown", (await Assert.ThrowsAsync<DomainException>(() => admin.Send(new SaveProductTechCommand("GPU-4060", true,
            SerialKind.Serial, 36, [new("condicion", ["Nuevo"]), new("socket", ["AM5"])])))).Code);
        Assert.Equal("tech.serial_kind", (await Assert.ThrowsAsync<DomainException>(() => admin.Send(new SaveProductTechCommand("GPU-4060", true,
            SerialKind.Imei, 36, [new("condicion", ["Nuevo"])])))).Code);
        // Cambiar valores (número con coma decimal y opción sin distinguir mayúsculas) reemplaza la ficha
        await admin.Send(new SaveProductTechCommand("GPU-4090", true, SerialKind.Serial, 24,
            [new("condicion", ["reacondicionado"]), new("vram", ["24"]), new("largo", ["357,5"]), new("consumo", ["450"])]));
        var updated = await admin.Send(new GetProductTechQuery("GPU-4090"));
        Assert.Equal("Reacondicionado", updated.Specs.Single(s => s.Code == "condicion").Display);
        Assert.Equal(["357.5"], updated.Specs.Single(s => s.Code == "largo").Values);
        Assert.Equal(24, updated.WarrantyMonths);

        // Búsqueda con filtros (rango y opción), plataforma y stock de la sucursal
        var big = await admin.Send(new SearchTechProductsQuery(CategoryCode: "COMP", Filters: [new SpecFilter("vram", Min: 12)]));
        Assert.Equal("GPU-4090", Assert.Single(big).Sku);
        var fresh = await admin.Send(new SearchTechProductsQuery(CategoryCode: "GPU", Filters: [new SpecFilter("condicion", ["Nuevo"])]));
        Assert.Equal("GPU-4060", Assert.Single(fresh).Sku);
        Assert.Equal(2m, fresh[0].Stock);
        Assert.Contains("Memoria de video: 8 GB", fresh[0].KeySpecs, StringComparison.Ordinal);
        var ps5 = Assert.Single(await admin.Send(new SearchTechProductsQuery(Platform: "PS5")));
        Assert.Equal("PS5-SLIM", ps5.Sku);
        Assert.Equal(["PS5"], ps5.Platforms);
        Assert.Equal("GPU-4060", Assert.Single(await admin.Send(new SearchTechProductsQuery(Text: "4060", OnlyInStock: true))).Sku);
        Assert.Empty(await admin.Send(new SearchTechProductsQuery(Text: "4090", OnlyInStock: true)));

        // Facetas de Componentes (condición heredada) y de tarjetas de video (rango de VRAM)
        var facets = await admin.Send(new GetSpecFacetsQuery("GPU"));
        var condition = facets.Single(f => f.Code == "condicion");
        Assert.Equal([("Nuevo", 1), ("Reacondicionado", 1)], condition.Values.Select(v => (v.Value, v.Count)));
        var vram = facets.Single(f => f.Code == "vram");
        Assert.Equal(8m, vram.Min);
        Assert.Equal(24m, vram.Max);
        var comp = await admin.Send(new GetSpecFacetsQuery("COMP"));
        Assert.Equal(10, comp.Single(f => f.Code == "condicion").Values.Sum(v => v.Count));

        // El catálogo de siempre también se filtra por especificaciones
        var catalog = await admin.Send(new GetCatalogQuery([new SpecFilter("socket", ["AM5"])]));
        Assert.Equal(["CPU-7600", "MB-B650"], catalog.Select(c => c.Sku).Order(StringComparer.Ordinal));
        Assert.Equal(["GPU-4060", "GPU-4090"], (await admin.Send(new GetCatalogQuery(null, "GPU"))).Select(c => c.Sku).Order(StringComparer.Ordinal));
        Assert.True((await admin.Send(new GetCatalogQuery())).Count >= V42TechScenario.Products.Length + E_BillingTestHost.Products.Length);
    }

    [Fact]
    public async Task La_linea_fiscal_se_divide_si_las_series_no_caben_y_el_IMEI_va_en_numeroImei()
    {
        await using var host = await E_BillingTestHost.CreateAsync();
        var tech = await V42TechScenario.CreateAsync(host);
        var ssd = Enumerable.Range(1, 30).Select(i => $"SSD{i:0000}".PadRight(60, 'X')).ToArray();
        var imei = new[] { V42TechScenario.ImeiFor(1), V42TechScenario.ImeiFor(2) };
        await tech.ReceiveAsync(("SSD-1TB", ssd), ("TAB-LTE", imei));
        using var cashier = await host.CashierAsync();
        using var admin = await host.AdminAsync();
        var sale = await E_BillingTestHost.SellAsync(cashier, E_BillingTestHost.Ci(), new SaleLineInput("SSD-1TB", 30, 10, ssd),
            new SaleLineInput("TAB-LTE", 2, 0, [imei[0][..2] + "-" + imei[0][2..], imei[1]]));
        Assert.Equal(23733m + 5998m, sale.Total);
        var document = await host.DocumentAsync(sale.FiscalDocumentId!.Value);
        var lines = document.Lines.OrderBy(l => l.LineNumber).ToList();
        Assert.Equal(3, lines.Count);
        Assert.Equal([24m, 6m, 2m], lines.Select(l => l.Quantity));
        Assert.Equal(sale.Total, document.TotalAmount);
        Assert.Equal(23733m, lines[0].Subtotal + lines[1].Subtotal);
        Assert.All(lines.Take(2), l => Assert.True(l.SerialNumber!.Length <= FiscalIssuer.MaxSerialText));
        Assert.Equal(ssd, lines.Take(2).SelectMany(l => FiscalIssuer.SplitSerials(l.SerialNumber)));
        Assert.Equal(string.Join(", ", imei), lines[2].Imei);
        Assert.Null(lines[2].SerialNumber);
        var xml = await tech.XmlAsync(document.Id);
        tech.Validate(xml, SiatCodes.SectorPurchaseSale);
        Assert.Contains($"<numeroImei>{string.Join(", ", imei)}</numeroImei>", xml, StringComparison.Ordinal);
        Assert.Equal(1, (await cashier.Send(new DispatchFiscalDocumentsCommand(sale.FiscalDocumentId))).Valid);

        // Anular la factura y devolver la mercadería: las series vuelven al stock
        var reason = await host.DbAsync(db => db.Set<SiatCatalogItem>()
            .Where(i => i.Catalog == SiatCatalogNames.VoidReasons && i.Description == "FACTURA MAL EMITIDA").Select(i => i.Code).FirstAsync());
        await admin.Send(new VoidFiscalDocumentCommand(document.Id, reason, true, "Venta registrada por error"));
        Assert.All(ssd.Concat(imei), s => Assert.Equal(SerialNumberStatus.InStock, tech.SerialAsync(s).Result.Status));
        Assert.Equal(30m, await host.OnHandAsync("SSD-1TB"));
        Assert.Equal(0, await tech.BreachesAsync());
        var trace = await admin.Send(new GetSerialTraceQuery(imei[0]));
        Assert.Equal([SerialEventAction.Received, SerialEventAction.Sold, SerialEventAction.Returned], trace.Events.Select(e => e.Action));
    }

    [Fact]
    public async Task Una_devolucion_por_falla_no_vuelve_al_stock_y_la_unidad_queda_en_garantia()
    {
        await using var host = await E_BillingTestHost.CreateAsync();
        await RunDefectiveReturnAsync(host);
    }

    internal static async Task RunDefectiveReturnAsync(E_BillingTestHost host)
    {
        var tech = await V42TechScenario.CreateAsync(host);
        var consoles = V42TechScenario.Serials("PS5-SLIM", 1, 2);
        await tech.ReceiveAsync(("PS5-SLIM", consoles));
        using var cashier = await host.CashierAsync();
        using var admin = await host.AdminAsync();
        var sale = await E_BillingTestHost.SellAsync(cashier, E_BillingTestHost.Ci(), new SaleLineInput("PS5-SLIM", 1, 0, [consoles[0]]));
        Assert.Equal(1, (await cashier.Send(new DispatchFiscalDocumentsCommand(sale.FiscalDocumentId))).Valid);
        var result = await admin.Send(new CreateSalesReturnCommand(sale.InvoiceNumber, "No enciende al día siguiente", "EFECTIVO",
            [new ReturnLineInput("PS5-SLIM", 1, [consoles[0]])], Defective: true));
        Assert.Contains("por falla", result.Message, StringComparison.Ordinal);
        Assert.Equal(4999m, result.Refund);
        Assert.Equal(SerialNumberStatus.InRma, (await tech.SerialAsync(consoles[0])).Status);
        Assert.Equal(1m, await host.OnHandAsync("PS5-SLIM"));
        Assert.Equal(0, await tech.BreachesAsync());
        Assert.NotNull(result.CreditNoteId);
        // Reparada por la tienda: vuelve al stock con una entrada con su serie (y se puede vender otra vez)
        using (var bodega = await host.AdminAsync())
        {
            await bodega.Send(new RegisterMovementCommand("PS5-SLIM", E_BillingTestHost.Bin, MovementTypeCodes.Receipt, 1, null, "REP-1", "Reparada en el taller",
                Serials: [consoles[0]]));
        }
        Assert.Equal(SerialNumberStatus.InStock, (await tech.SerialAsync(consoles[0])).Status);
        Assert.Equal(2m, await host.OnHandAsync("PS5-SLIM"));
        Assert.Equal(0, await tech.BreachesAsync());
        Assert.Equal([SerialEventAction.Received, SerialEventAction.Sold, SerialEventAction.Returned, SerialEventAction.RmaReceived,
            SerialEventAction.Restocked], (await admin.Send(new GetSerialTraceQuery(consoles[0]))).Events.Select(e => e.Action));
        // Una unidad en stock no se da de baja por aquí (se hace con un ajuste negativo con su serie)
        Assert.Equal(SerialErrorCodes.NotAvailable, (await Assert.ThrowsAsync<DomainException>(() =>
            admin.Send(new DisposeSerialCommand(consoles[0], SerialDisposal.Scrap, "Prueba")))).Code);
    }
}
