using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MINV.Application.Abstractions;
using MINV.Application.Billing;
using MINV.Application.Integration;
using MINV.Application.Partners;
using MINV.Application.Purchasing;
using MINV.Application.Sales;
using MINV.Domain.Billing;
using MINV.Domain.Common;
using MINV.Infrastructure.Billing.Simulator;
using MINV.Infrastructure.Persistence;

namespace MINV.Infrastructure.Seeding;

/// <summary>
/// V4.1 · Resumen de la facturación SIAT de la empresa de prueba. <see cref="SiatToken"/> es el token delegado de
/// SIMULACIÓN (aleatorio en cada carga): va cifrado en la base y se guarda en claro SOLO en el archivo local de claves
/// (<c>MINV_SIAT_TOKEN</c>) para arrancar el simulador HTTP del SIN con ese mismo token.
/// </summary>
public sealed record SeedBilling(long Nit, string BusinessName, string SystemCode, int Environment, string SiatToken, string SimulatorUrl,
    DateOnly From, int Documents, int ValidInvoices, int CreditNotes, int OfflineRecovered, int Voided, int Reverted, int CafcInvoices,
    int SupplierInvoices, int WebInvoices, int PointsOfSale, string? SimulatorStateFile)
{
    public override string ToString() => $"SeedBilling NIT {Nit} · {Documents} documentos desde {From:dd/MM/yyyy}";
}

public sealed partial class LocalDataSeeder
{
    /// <summary>Cliente habitual generado (a quien se factura con su documento: NIT las empresas, CI las personas).</summary>
    private sealed record SeedCustomer(string Code, string Name, string TaxId, string? Email, bool IsCompany);

    /// <summary>Venta facturada y VÁLIDA en el SIN (candidata para anular, revertir o devolver).</summary>
    private sealed record BilledSale(DateOnly Day, string InvoiceNumber, Guid DocumentId, FiscalBuyerInput? Buyer, IReadOnlyList<SaleLineInput> Lines);

    /// <summary>Venta extra de un escenario (hora del día en minutos, sucursal y etiqueta).</summary>
    private sealed record SaleSlot(int Minute, string Branch, string Tag);

    /// <summary>
    /// V4.1 · La empresa de prueba FACTURA desde la mitad del período (<see cref="SeedOptions.BillingDays"/>: «la empresa
    /// empezó a facturar con M-INV»), SIEMPRE contra el simulador del SIN EN PROCESO (nunca el SIN real) y con los mismos
    /// casos de uso de la caja, el trabajo automático y las pantallas de facturación. Escenarios: comprador en cada venta
    /// (clientes habituales con NIT o CI y compradores eventuales con CI), un corte de internet de 3 horas en El Alto
    /// (simulador apagado → facturas fuera de línea → recuperación con evento y paquete validado), una contingencia manual
    /// por corte de energía en Santa Cruz con 3 facturas CAFC transcritas, 3 anulaciones (una con devolución de mercadería,
    /// otra re-emitida con el comprador corregido y otra revertida), 2 devoluciones parciales con nota crédito-débito, un
    /// rechazo por NIT inválido re-emitido con excepción, pedidos web facturados y 4 facturas de proveedores.
    /// </summary>
    private sealed class BillingScenario
    {
        private const string ElAltoRegister = BranchElAlto + "-CAJA1";
        private const string SantaCruzRegister = BranchSantaCruz + "-CAJA1";

        /// <summary>NIT del comprador rechazado: termina en 999 (el Padrón simulado lo da por inactivo → 1037).</summary>
        private const string InvalidBuyerNit = "3456789999";

        private readonly SeedOptions _o;
        private readonly DateOnly _today;
        private readonly Action<DateOnly, int, int> _at;
        private readonly Action<string> _log;
        private readonly SignedIn _admin;
        private readonly SignedIn _gerencia;
        private readonly SignedIn _bodega;
        private readonly MinvWriteDbContext _db;
        private readonly IReadOnlyDictionary<string, SeedCustomer> _customers;
        private readonly IReadOnlyList<(string Sku, string Unit, int Pop)> _weighted;
        private readonly SiatSimulatorEngine _engine;
        private readonly CancellationToken _ct;
        private readonly Random _rng;
        private readonly SiatSeedProfile _profile;
        private readonly List<FiscalBuyerInput> _buyers = [];
        private readonly List<BilledSale> _billed = [];
        private readonly HashSet<string> _used = new(StringComparer.Ordinal);
        private readonly List<Guid> _deferred = [];
        private readonly List<(int Minute, Func<DateOnly, Task> Action)> _transitions = [];
        private readonly string _cafcCode;
        private readonly DateOnly _nitDay;
        private readonly DateOnly _outageDay;
        private readonly DateOnly _contingencyDay;
        private readonly DateOnly _voidGoodsDay;
        private readonly DateOnly _voidReissueDay;
        private readonly DateOnly _voidRevertDay;
        private readonly DateOnly _return1Day;
        private readonly DateOnly _return2Day;
        private IReadOnlyList<SiatPointOfSaleStatus> _points = [];
        private IReadOnlyList<SiatCatalogItemView>? _voidReasons;
        private DateOnly? _setupDay;
        private bool _outage;
        private bool _contingency;
        private long _nextCafcNumber = 1001;
        private int _webInvoices;
        private int _supplierInvoices;

        private BillingScenario(SeedOptions o, DateOnly from, DateOnly today, Action<DateOnly, int, int> at, Action<string> log, SignedIn admin,
            SignedIn gerencia, SignedIn bodega, MinvWriteDbContext db, IReadOnlyList<SeedCustomer> customers,
            IReadOnlyList<(string Sku, string Unit, int Pop)> weighted, SiatSimulatorEngine engine, CancellationToken ct)
        {
            _o = o;
            From = from;
            _today = today;
            _at = at;
            _log = log;
            _admin = admin;
            _gerencia = gerencia;
            _bodega = bodega;
            _db = db;
            _customers = customers.ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);
            _weighted = weighted;
            _engine = engine;
            _ct = ct;
            _rng = new Random(o.Seed + 41);
            var nit = long.TryParse(o.TaxId, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : 1023456028L;
            // Token delegado de SIMULACIÓN: aleatorio en cada carga (nunca versionado); el simulador HTTP lo acepta después
            var token = o.SiatToken is { Length: >= 10 } given ? given : "SIM-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            _profile = new SiatSeedProfile(nit, o.CompanyName.Trim().ToUpperInvariant(), "MINV-SIM-0041", token, o.SiatSimulatorUrl, today.AddYears(1));
            _cafcCode = "1" + Convert.ToHexString(Bytes(6));

            // Compradores eventuales (personas con CI generado; algunos con correo .example y complemento)
            var taken = customers.Select(c => c.TaxId).ToHashSet(StringComparer.Ordinal);
            for (var i = 0; _buyers.Count < 40; i++)
            {
                var ci = _rng.Next(2_000_000, 9_999_999).ToString(CultureInfo.InvariantCulture);
                if (!taken.Add(ci))
                {
                    continue;
                }
                var name = $"{FirstNames[_rng.Next(FirstNames.Length)]} {LastNames[_rng.Next(LastNames.Length)]} {LastNames[_rng.Next(LastNames.Length)]}";
                _buyers.Add(new FiscalBuyerInput(SiatCodes.DocumentCi, ci, i % 13 == 7 ? "1A" : null, name,
                    i % 3 == 0 ? $"{Slug(name.Split(' ')[0] + " " + name.Split(' ')[1])}{i}@correo.example" : null));
            }

            // Días de los escenarios, repartidos en los días hábiles facturados (sin contar hoy)
            var days = new List<DateOnly>();
            for (var d = from; d < today; d = d.AddDays(1))
            {
                if (d.DayOfWeek != DayOfWeek.Sunday)
                {
                    days.Add(d);
                }
            }
            DateOnly Day(double fraction, int minIndex) => days.Count == 0
                ? today
                : days[Math.Clamp((int)Math.Round((days.Count - 1) * fraction, MidpointRounding.AwayFromZero), Math.Min(minIndex, days.Count - 1),
                    days.Count - 1)];
            _nitDay = Day(0.15, 0);
            _outageDay = Day(0.3, 1);
            _voidGoodsDay = Day(0.4, 1);
            _return1Day = Day(0.5, 1);
            _contingencyDay = Day(0.55, 1);
            _voidReissueDay = Day(0.65, 1);
            _return2Day = Day(0.8, 1);
            _voidRevertDay = Day(0.9, 1);
        }

        /// <summary>Primer día facturado (desde ahí cada venta de caja lleva comprador y se envía al SIN).</summary>
        public DateOnly From { get; }

        /// <summary>¿La empresa ya factura? (configuración hecha y activada)</summary>
        public bool Active => _setupDay is not null;

        /// <summary>Ventas registradas al transcribir las facturas manuales del talonario CAFC (también son ventas de caja).</summary>
        public int ManualSales { get; private set; }

        /// <summary>
        /// Escenario de facturación si corresponde: la opción está activa, el SIN es el simulador EN PROCESO y hay una clave
        /// maestra para cifrar el token. Si no, se explica por qué se omite (la carga sigue sin facturación).
        /// </summary>
        public static BillingScenario? TryCreate(IServiceProvider services, SeedOptions o, DateOnly from, DateOnly today, Action<DateOnly, int, int> at,
            Action<string> log, SignedIn admin, SignedIn gerencia, SignedIn bodega, MinvWriteDbContext db, IReadOnlyList<SeedCustomer> customers,
            IReadOnlyList<(string Sku, string Unit, int Pop)> weighted, CancellationToken ct)
        {
            if (!o.Billing)
            {
                log("Facturación SIAT omitida (se pidió sin facturación).");
                return null;
            }
            if (services.GetService<SiatSimulatorEngine>() is not { } engine || admin.Scope.ServiceProvider.GetService<ISiatGateway>() is not InProcessSiatGateway)
            {
                log("Facturación SIAT omitida: los datos de prueba facturan SOLO contra el simulador del SIN en proceso (nunca contra el SIN real).");
                return null;
            }
            try
            {
                _ = services.GetRequiredService<ISecretProtector>().Protect("prueba");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log("Facturación SIAT omitida: falta la clave maestra de integraciones (MINV_INTEGRATION_KEYS) para cifrar el token del SIN.");
                return null;
            }
            engine.Available = true;
            return new BillingScenario(o, from, today, at, log, admin, gerencia, bodega, db, customers, weighted, engine, ct);
        }

        // ============================================================================================ día a día
        /// <summary>Al empezar el día: el primer día facturado configura la facturación (07:45); cada día facturado corre el
        /// trabajo automático a las 08:15 (CUFD del día, hora del SIN, catálogos y lo pendiente), como el servidor en la nube.</summary>
        public async Task StartDayAsync(DateOnly day)
        {
            _transitions.Clear();
            if (day < From)
            {
                return;
            }
            if (!Active)
            {
                await SetupAsync(day);
            }
            _at(day, 8, 15);
            await _admin.Send(new RunSiatWorkCommand(Maintain: true), _ct);
            if (day == _outageDay)
            {
                _transitions.Add((10 * 60, OutageStartAsync));
                _transitions.Add((13 * 60, OutageEndAsync));
            }
            if (day == _contingencyDay)
            {
                _transitions.Add((15 * 60, ContingencyStartAsync));
                _transitions.Add((17 * 60, ContingencyEndAsync));
                _transitions.Add((17 * 60 + 30, TranscribeAsync));
            }
            _transitions.Sort((a, b) => a.Minute.CompareTo(b.Minute));
        }

        /// <summary>Ventas extra de los escenarios del día (hasta <paramref name="maxMinute"/>).</summary>
        public IEnumerable<SaleSlot> ExtraSales(DateOnly day, int maxMinute)
        {
            if (!Active)
            {
                yield break;
            }
            var slots = new List<SaleSlot>();
            if (day == _setupDay)
            {
                slots.Add(new SaleSlot(9 * 60 + 5, BranchMain, "cf"));   // venta menor: el consumidor final factura con el NIT 99003
            }
            if (day == _nitDay)
            {
                slots.Add(new SaleSlot(11 * 60 + 30, BranchMain, "nit"));
            }
            if (day == _outageDay)
            {
                slots.AddRange([new SaleSlot(10 * 60 + 10, BranchElAlto, "offline"), new SaleSlot(11 * 60 + 5, BranchElAlto, "offline"),
                    new SaleSlot(12 * 60 + 25, BranchElAlto, "offline")]);
            }
            foreach (var slot in slots.Where(s => s.Minute <= maxMinute))
            {
                yield return slot;
            }
        }

        /// <summary>Ejecuta los cambios de estado del día (corte, contingencia…) hasta la hora dada, en orden y con su hora.</summary>
        public async Task AdvanceAsync(DateOnly day, int minute)
        {
            while (_transitions.Count > 0 && _transitions[0].Minute <= minute)
            {
                var (at, action) = _transitions[0];
                _transitions.RemoveAt(0);
                _at(day, at / 60, at % 60);
                await action(day);
            }
        }

        /// <summary>Durante la contingencia manual la caja de Santa Cruz no tiene energía: vende con el talonario CAFC.</summary>
        public bool Skips(string branch) => _contingency && branch == BranchSantaCruz;

        /// <summary>Datos de facturación que captura la caja: cliente habitual con su documento, comprador eventual con CI, NIT
        /// especial 99003 (ventas menores) o, en el escenario de rechazo, un NIT mal escrito. Tarjeta: número de prueba.</summary>
        public (FiscalBuyerInput? Buyer, string? Card) ForSale(string customerCode, string method, string? tag)
        {
            if (!Active)
            {
                return (null, null);
            }
            var card = method == "TARJETA" ? "4" + string.Concat(Enumerable.Range(0, 15).Select(_ => _rng.Next(10).ToString(CultureInfo.InvariantCulture))) : null;
            FiscalBuyerInput? buyer = tag switch
            {
                "cf" => new FiscalBuyerInput(SiatCodes.DocumentNit, SiatCodes.SpecialMinorSales, null, null, null),
                "nit" => new FiscalBuyerInput(SiatCodes.DocumentNit, InvalidBuyerNit, null, "IMPORTADORA CHUQUIAGO LTDA.", "compras@chuquiago.example"),
                _ when _customers.TryGetValue(customerCode, out var customer) => BuyerOf(customer),
                // Consumidor final: casi siempre da su CI; a veces no da datos y sale con el NIT especial 99003
                _ => _rng.NextDouble() < 0.82 ? _buyers[_rng.Next(_buyers.Count)] : null,
            };
            return (buyer, card);
        }

        /// <summary>Datos de facturación de un pedido web (el cliente de la tienda con su NIT o CI).</summary>
        public FiscalBuyerInput? ForWebOrder(string customerCode) =>
            Active && _customers.TryGetValue(customerCode, out var customer) ? BuyerOf(customer) : null;

        public void NoteWebOrder(ExternalOrderResult result)
        {
            if (result.FiscalNumber is not null)
            {
                _webInvoices++;
            }
        }

        /// <summary>Los pedidos web los envía el trabajo automático (como el servidor en la nube cada pocos segundos).</summary>
        public async Task DispatchPendingAsync()
        {
            if (Active && !_outage)
            {
                await _admin.Send(new DispatchFiscalDocumentsCommand(null, 100), _ct);
            }
        }

        /// <summary>La caja, al terminar de cobrar, envía el documento al SIN (durante el corte de El Alto, las otras sucursales
        /// lo envían cuando el simulador vuelve: su internet no se cortó).</summary>
        public async Task AfterSaleAsync(SignedIn cashier, string branch, CheckoutResult result, IReadOnlyList<SaleLineInput> lines, DateOnly day,
            FiscalBuyerInput? buyer, string? tag)
        {
            if (!Active || result.FiscalDocumentId is not { } id || result.FiscalStatus != FiscalDocumentStatus.Pending)
            {
                return;
            }
            if (_outage && branch != BranchElAlto)
            {
                _deferred.Add(id);
                return;
            }
            var dispatch = await cashier.Send(new DispatchFiscalDocumentsCommand(id), _ct);
            var final = dispatch.Documents.LastOrDefault();
            if (tag == "nit")
            {
                _log($"… {day:dd/MM/yyyy}: el SIN rechazó la factura de {InvalidBuyerNit} (NIT inválido, 1037) y se re-emitió con código de excepción: " +
                     $"N° {final?.Number} {Status(final?.Status)}.");
                return;
            }
            if (tag is null && branch == BranchMain && final is { Status: FiscalDocumentStatus.Valid, Kind: FiscalDocumentKind.Invoice })
            {
                _billed.Add(new BilledSale(day, result.InvoiceNumber, final.Id, buyer, lines));
            }
        }

        /// <summary>Al cerrar el día (19:50, después del cierre de caja): anulaciones, reversión y devoluciones programadas.</summary>
        public async Task EndOfDayAsync(DateOnly day, int startMinute)
        {
            if (!Active)
            {
                return;
            }
            await AdvanceAsync(day, 24 * 60);
            var minute = startMinute;
            async Task StepAsync(Func<DateOnly, Task> action)
            {
                _at(day, minute / 60, minute % 60);
                minute += 2;
                await action(day);
            }
            if (day == _voidGoodsDay)
            {
                await StepAsync(VoidWithGoodsAsync);
            }
            if (day == _return1Day)
            {
                await StepAsync(d => ReturnAsync(d, "Le sobró material de la obra"));
            }
            if (day == _voidReissueDay)
            {
                await StepAsync(VoidAndReissueAsync);
            }
            if (day == _return2Day)
            {
                await StepAsync(d => ReturnAsync(d, "Producto con falla de fábrica"));
            }
            if (day == _voidRevertDay)
            {
                await StepAsync(VoidAndRevertAsync);
            }
        }

        /// <summary>Cierre de la carga: todo en línea, facturas de proveedores, trabajo automático final (CUFD vigentes para
        /// hoy) y resumen.</summary>
        public async Task<SeedBilling?> FinishAsync()
        {
            if (!Active)
            {
                return null;
            }
            if (_outage)
            {
                await OutageEndAsync(_today);
            }
            if (_contingency)
            {
                await ContingencyEndAsync(_today);
            }
            _engine.Available = true;
            var receipts = await _bodega.Send(new GetReceiptsWithoutInvoiceQuery(), _ct);
            foreach (var receipt in receipts.OrderByDescending(r => r.ReceivedOn).ThenBy(r => r.ReceiptNumber, StringComparer.Ordinal).Take(4))
            {
                var number = _rng.Next(1_000, 99_999).ToString(CultureInfo.InvariantCulture);
                await _bodega.Send(new RegisterSupplierInvoiceCommand(receipt.ReceiptNumber, number, Convert.ToHexString(Bytes(28)), receipt.ReceivedOn,
                    receipt.Total), _ct);
                _supplierInvoices++;
            }
            await _admin.Send(new RunSiatWorkCommand(Maintain: true), _ct);
            var work = await _admin.Send(new RunSiatWorkCommand(Maintain: true), _ct);
            var rows = await _admin.Send(new GetFiscalDocumentsQuery(From, _today.AddDays(1)), _ct);
            var events = await _admin.Send(new GetSignificantEventsQuery(From, _today.AddDays(1)), _ct);
            var points = (await _admin.Send(new GetSiatStatusQuery(), _ct)).Points;
            var result = new SeedBilling(_profile.Nit, _profile.BusinessName, _profile.SystemCode, SiatCodes.EnvironmentTest, _profile.Token,
                _profile.SimulatorBaseUrl, From, rows.Count,
                rows.Count(r => r.Kind == FiscalDocumentKind.Invoice && r.Status == FiscalDocumentStatus.Valid),
                rows.Count(r => r.Kind == FiscalDocumentKind.CreditDebitNote),
                events.Where(e => e.Kind == SignificantEventKind.Offline).Sum(e => e.Documents),
                rows.Count(r => r.Status == FiscalDocumentStatus.Voided), rows.Count(r => r.IsReverted),
                events.Where(e => e.Kind == SignificantEventKind.ManualCafc).Sum(e => e.Documents), _supplierInvoices, _webInvoices, points.Count,
                _engine.Options.StateFile);
            _log($"Facturación SIAT: {result.Documents} documentos desde el {From:dd/MM/yyyy} ({result.ValidInvoices} facturas válidas, " +
                 $"{result.CreditNotes} notas crédito-débito, {result.OfflineRecovered} fuera de línea recuperadas, {result.CafcInvoices} CAFC, " +
                 $"{result.Voided} anuladas, {result.Reverted} revertida, {result.WebInvoices} de pedidos web) y {result.SupplierInvoices} facturas de " +
                 $"proveedores. Puntos de venta en línea: {points.Count(p => p.Mode == SiatConnectionMode.Online)}/{points.Count}; " +
                 $"pendientes: {work.Dispatch.Documents.Count(d => d.Status == FiscalDocumentStatus.Pending)}.");
            return result;
        }

        // ============================================================================================ configuración
        private async Task SetupAsync(DateOnly day)
        {
            _at(day, 7, 45);
            // El consumidor final factura con el NIT especial 99003 (ventas menores del día) cuando el comprador no da datos
            await _admin.Send(new SaveCustomerCommand("CF", "Consumidor final", SiatCodes.SpecialMinorSales, null, null, "GENERAL", true), _ct);
            _points = await SiatSeedSetup.ConfigureAsync(_admin, _profile,
            [
                new SiatSeedBranch(BranchMain, 0, "La Paz", "2-2441122"), new SiatSeedBranch(BranchElAlto, 1, "El Alto", "2-2825566"),
                new SiatSeedBranch(BranchSantaCruz, 2, "Santa Cruz", "3-3345678"),
            ],
            [
                new SiatSeedRegister(BranchMain, "CAJA01", "Caja 1"), new SiatSeedRegister(BranchMain, "CAJA02", "Caja 2"),
                new SiatSeedRegister(BranchMain, "CAJA03", "Caja 3 (mostrador)"), new SiatSeedRegister(BranchElAlto, ElAltoRegister, "Caja El Alto"),
                new SiatSeedRegister(BranchSantaCruz, SantaCruzRegister, "Caja Santa Cruz"),
            ], _log, _ct);
            // Correo de la empresa DESACTIVADO (dominio .example): la pantalla lo muestra; los avisos quedan «por otro medio»
            await _admin.Send(new SaveMailSettingsCommand($"smtp.{_o.Domain}", 587, true, $"facturacion@{_o.Domain}", null, $"facturacion@{_o.Domain}",
                _profile.BusinessName, false), _ct);
            // Talonario de contingencia (CAFC) de Santa Cruz para facturas Compra Venta
            await _admin.Send(new RegisterContingencyCodeCommand(BranchSantaCruz, SiatCodes.SectorPurchaseSale, _cafcCode, 1001, 1100, day.AddYears(1)),
                _ct);
            _setupDay = day;
            _log($"{day:dd/MM/yyyy}: la empresa empezó a facturar con M-INV (facturación computarizada en línea, ambiente de pruebas).");
        }

        // ============================================================================================ corte de internet (El Alto)
        private Task OutageStartAsync(DateOnly day)
        {
            _engine.Available = false;
            _outage = true;
            _log($"… {day:dd/MM/yyyy} 10:00: corte de internet en El Alto (simulador del SIN apagado): la caja no se bloquea, factura fuera de línea.");
            return Task.CompletedTask;
        }

        private async Task OutageEndAsync(DateOnly day)
        {
            _engine.Available = true;
            _outage = false;
            foreach (var id in _deferred)
            {
                await _admin.Send(new DispatchFiscalDocumentsCommand(id), _ct);
            }
            _deferred.Clear();
            // Recuperación automática: CUFD nuevo → evento significativo → verificación → paquete → validación
            await _admin.Send(new RunSiatWorkCommand(Maintain: true), _ct);
            var point = await PointAsync(ElAltoRegister);
            if (point.Mode != SiatConnectionMode.Online)
            {
                await _admin.Send(new RecoverPointOfSaleCommand(point.Id), _ct);
            }
            await _admin.Send(new RunSiatWorkCommand(Maintain: true), _ct);
            var events = await _admin.Send(new GetSignificantEventsQuery(day, day), _ct);
            var evt = events.FirstOrDefault(e => e.BranchCode == BranchElAlto && e.Kind == SignificantEventKind.Offline);
            _log($"… {day:dd/MM/yyyy} 13:00: volvió internet en El Alto: {evt?.Documents ?? 0} facturas fuera de línea enviadas en paquete " +
                 $"(evento «{evt?.Description}», {Status(evt?.Status)}).");
        }

        // ============================================================================================ contingencia manual (Santa Cruz)
        private async Task ContingencyStartAsync(DateOnly day)
        {
            var events = await _admin.Send(new GetSiatCatalogQuery(SiatCatalogNames.SignificantEvents), _ct);
            var code = SiatSeedSetup.CodeContaining(events, "ENERGIA") ?? throw new DomainException("seed.event", "Falta el evento de corte de energía.");
            var point = await PointAsync(SantaCruzRegister);
            await _gerencia.Send(new StartManualContingencyCommand(point.Id, code,
                "Corte de suministro de energía eléctrica en la zona: la caja factura con el talonario CAFC", null, _cafcCode), _ct);
            _contingency = true;
            _log($"… {day:dd/MM/yyyy} 15:00: corte de energía en Santa Cruz: contingencia manual con el talonario CAFC {_cafcCode}.");
        }

        private async Task ContingencyEndAsync(DateOnly day)
        {
            var point = await PointAsync(SantaCruzRegister);
            await _gerencia.Send(new EndContingencyCommand(point.Id), _ct);
            _contingency = false;
        }

        /// <summary>Transcripción de las 3 facturas manuales del talonario y envío del paquete del evento.</summary>
        private async Task TranscribeAsync(DateOnly day)
        {
            var events = await _gerencia.Send(new GetSignificantEventsQuery(day, day), _ct);
            var evt = events.FirstOrDefault(e => e.BranchCode == BranchSantaCruz && e.Kind == SignificantEventKind.ManualCafc);
            if (evt is null)
            {
                return;
            }
            var warehouse = await _db.Warehouses.AsNoTracking().FirstAsync(w => w.Code == "ALMSC", _ct);
            var times = new[] { new TimeOnly(15, 20, 12), new TimeOnly(15, 52, 40), new TimeOnly(16, 37, 5) };
            var transcribed = 0;
            foreach (var time in times)
            {
                var lines = new List<SaleLineInput>();
                foreach (var item in _weighted.OrderBy(_ => _rng.Next()).DistinctBy(x => x.Sku).Take(12))
                {
                    var quantity = item.Unit is "KG" or "MT" or "LT" or "GL" ? 1m : _rng.Next(1, 3);
                    if (lines.Count < 2 && await AvailableAsync(_db, item.Sku, warehouse.Id, _ct) >= quantity + 2)
                    {
                        lines.Add(new SaleLineInput(item.Sku, quantity));
                    }
                }
                if (lines.Count == 0)
                {
                    continue;
                }
                var buyer = _rng.NextDouble() < 0.5 ? _buyers[_rng.Next(_buyers.Count)] : BuyerOf(_customers.Values.ElementAt(_rng.Next(_customers.Count)));
                await _gerencia.Send(new TranscribeManualInvoiceCommand(evt.Id, _nextCafcNumber++, day.ToDateTime(time), buyer, "EFECTIVO", lines), _ct);
                transcribed++;
                ManualSales++;
            }
            await _admin.Send(new RunSiatWorkCommand(Maintain: true), _ct);
            _log($"… {day:dd/MM/yyyy} 17:30: volvió la energía en Santa Cruz: {transcribed} facturas manuales CAFC transcritas y enviadas en paquete.");
        }

        // ============================================================================================ anulaciones y devoluciones
        private async Task VoidWithGoodsAsync(DateOnly day)
        {
            if (Candidate(_ => true) is not { } sale)
            {
                return;
            }
            var reason = await VoidReasonAsync("FACTURA O NOTA DE CREDITO-DEBITO DEVUELTA", "DEVUELTA");
            await _gerencia.Send(new VoidFiscalDocumentCommand(sale.DocumentId, reason, ReturnGoods: true,
                "El cliente devolvió toda la compra: se equivocó de producto"), _ct);
            _log($"… {day:dd/MM/yyyy}: anulación con devolución de mercadería de la venta {sale.InvoiceNumber}.");
        }

        private async Task VoidAndReissueAsync(DateOnly day)
        {
            if (Candidate(s => s.Buyer is { DocumentType: SiatCodes.DocumentCi }) is not { } sale)
            {
                return;
            }
            var reason = await VoidReasonAsync("DATOS DE EMISION INCORRECTOS", "DATOS");
            await _gerencia.Send(new VoidFiscalDocumentCommand(sale.DocumentId, reason, ReturnGoods: false,
                "El número de CI del comprador estaba mal escrito"), _ct);
            var buyer = sale.Buyer!;
            var number = buyer.DocumentNumber[..^1] + ((buyer.DocumentNumber[^1] - '0' + 1) % 10).ToString(CultureInfo.InvariantCulture);
            var row = await _admin.Send(new ReissueFiscalDocumentCommand(sale.DocumentId, buyer with { DocumentNumber = number }), _ct);
            var dispatch = await _admin.Send(new DispatchFiscalDocumentsCommand(row.Id), _ct);
            _log($"… {day:dd/MM/yyyy}: factura de la venta {sale.InvoiceNumber} anulada por datos incorrectos y re-emitida con el CI corregido " +
                 $"(N° {row.Number}, {Status(dispatch.Documents.LastOrDefault()?.Status)}).");
        }

        private async Task VoidAndRevertAsync(DateOnly day)
        {
            if (Candidate(_ => true) is not { } sale)
            {
                return;
            }
            var reason = await VoidReasonAsync("FACTURA MAL EMITIDA", "MAL EMITIDA");
            await _gerencia.Send(new VoidFiscalDocumentCommand(sale.DocumentId, reason, ReturnGoods: false, "Anulada por error: correspondía a otra venta"),
                _ct);
            await _gerencia.Send(new RevertFiscalVoidCommand(sale.DocumentId), _ct);
            _log($"… {day:dd/MM/yyyy}: la factura de la venta {sale.InvoiceNumber} se anuló por error y se revirtió la anulación.");
        }

        private async Task ReturnAsync(DateOnly day, string reason)
        {
            if (Candidate(s => s.Lines.Count >= 2 || s.Lines.Any(l => l.Quantity >= 2)) is not { } sale)
            {
                return;
            }
            var line = sale.Lines.FirstOrDefault(l => l.Quantity >= 2) ?? sale.Lines[0];
            var quantity = line.Quantity >= 2 ? 1m : line.Quantity;
            var result = await _admin.Send(new CreateSalesReturnCommand(sale.InvoiceNumber, reason, "EFECTIVO", [new ReturnLineInput(line.Sku, quantity)]),
                _ct);
            FiscalDocumentStatus? status = result.CreditNoteStatus;
            if (result.CreditNoteId is { } note)
            {
                status = (await _admin.Send(new DispatchFiscalDocumentsCommand(note), _ct)).Documents.LastOrDefault()?.Status;
            }
            _log($"… {day:dd/MM/yyyy}: devolución parcial {result.Number} de la venta {sale.InvoiceNumber} con nota crédito-débito " +
                 $"N° {result.CreditNoteNumber} ({Status(status)}).");
        }

        /// <summary>Venta facturada y válida de la casa matriz todavía no usada en otro escenario (la más reciente).</summary>
        private BilledSale? Candidate(Func<BilledSale, bool> predicate)
        {
            var sale = _billed.LastOrDefault(s => !_used.Contains(s.InvoiceNumber) && predicate(s));
            if (sale is not null)
            {
                _used.Add(sale.InvoiceNumber);
            }
            return sale;
        }

        private async Task<int> VoidReasonAsync(string description, string contains)
        {
            _voidReasons ??= await _admin.Send(new GetSiatCatalogQuery(SiatCatalogNames.VoidReasons), _ct);
            return SiatSeedSetup.CodeFor(_voidReasons, description) ?? SiatSeedSetup.CodeContaining(_voidReasons, contains)
                   ?? throw new DomainException("seed.void_reason", $"Falta el motivo de anulación «{description}» en el catálogo del SIN.");
        }

        // ============================================================================================ auxiliares
        private async Task<SiatPointOfSaleStatus> PointAsync(string registerCode)
        {
            _points = (await _admin.Send(new GetSiatStatusQuery(), _ct)).Points;
            return _points.First(p => p.RegisterCode == registerCode);
        }

        private static FiscalBuyerInput BuyerOf(SeedCustomer customer) =>
            new(customer.IsCompany ? SiatCodes.DocumentNit : SiatCodes.DocumentCi, customer.TaxId, null, customer.Name, customer.Email);

        private byte[] Bytes(int count)
        {
            var bytes = new byte[count];
            _rng.NextBytes(bytes);
            return bytes;
        }

        private static string Status(FiscalDocumentStatus? status) => status switch
        {
            FiscalDocumentStatus.Valid => "válida",
            FiscalDocumentStatus.Pending => "pendiente",
            FiscalDocumentStatus.Offline => "fuera de línea",
            FiscalDocumentStatus.InPackage => "en paquete",
            FiscalDocumentStatus.Rejected => "rechazada",
            FiscalDocumentStatus.Voided => "anulada",
            null => "sin estado",
            _ => status.ToString()!.ToLowerInvariant(),
        };

        private static string Status(SignificantEventStatus? status) => status switch
        {
            SignificantEventStatus.Reconciled => "conciliado",
            SignificantEventStatus.PackagesSent => "paquetes enviados",
            SignificantEventStatus.Registered => "registrado",
            SignificantEventStatus.WithObservations => "con observaciones",
            SignificantEventStatus.Closed => "cerrado",
            SignificantEventStatus.Open => "abierto",
            _ => "sin evento",
        };
    }
}
