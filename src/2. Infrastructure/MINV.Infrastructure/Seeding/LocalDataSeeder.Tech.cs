using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Billing;
using MINV.Application.Inventory.PhysicalCounts;
using MINV.Application.Inventory.Queries;
using MINV.Application.Sales;
using MINV.Application.Storefront;
using MINV.Application.Tech;
using MINV.Domain.Catalog;
using MINV.Domain.Common;
using MINV.Domain.Inventory;
using MINV.Domain.Sales;
using MINV.Domain.Service;
using MINV.Infrastructure.Persistence;
using MINV.Infrastructure.Seeding.Tecnologia;

namespace MINV.Infrastructure.Seeding;

public sealed partial class LocalDataSeeder
{
    /// <summary>Unidad serializada vendida (candidata a un caso de garantía o a una devolución por falla).</summary>
    private sealed record SoldUnit(DateOnly Day, string Branch, string Invoice, string Customer, string Sku, string Serial);

    /// <summary>Piezas de un armado cotizado que la casa matriz envía a la sucursal que lo va a cobrar.</summary>
    private sealed record PartsShipment(string Branch, string Warehouse, IReadOnlyList<(TechProduct Product, int Quantity)> Parts, string Note);

    /// <summary>Paso de un caso RMA (regla T-05: solo por las transiciones del agregado).</summary>
    private enum ClaimStep
    {
        Diagnose,
        Supplier,
        Repair,
        Replace,
        Reject,
        Deliver,
        Note,
        Dispose,
    }

    /// <summary>Caso RMA planificado: sucursal, fracción del período en que se abre (o días antes de hoy) y pasos con su
    /// desfase en días hábiles desde la apertura.</summary>
    private sealed record ClaimPlan(string Branch, double Fraction, int DaysBeforeToday, IReadOnlyList<(ClaimStep Step, int Offset)> Steps, string Title)
    {
        public DateOnly OpenDay { get; set; }

        public string? Number { get; set; }

        public SoldUnit? Unit { get; set; }
    }

    /// <summary>Contactos ficticios de las reservas web (dominios .example, teléfonos bolivianos de prueba).</summary>
    private static readonly (string Name, string Phone, string Email, string Notes)[] WebContacts =
    [
        ("Valentina Aguirre", "+591 71234567", "valentina.aguirre@correo.example", "Paso a recoger el sábado por la mañana; ¿pueden dejarla armada y probada?"),
        ("Mateo Condori", "76543210", "mateo.condori@correo.example", "Consulta: ¿aceptan pago con QR al retirar?"),
    ];

    /// <summary>
    /// V6 · Reservas web de la empresa de prueba (regla S-09) con el MISMO caso de uso que usa la tienda
    /// (<see cref="CreateStorefrontReservationCommand"/>, sesión del administrador en la casa matriz): una reserva vencida hace
    /// unos días (el trabajo de vencimiento la libera: queda anulada con motivo «Vencida» y el stock volvió) y otra activa de hace
    /// unas horas (el escritorio la ve reservada y puede venderla en caja o liberarla). Las piezas salen del primer armado
    /// publicado que tenga stock disponible en la casa matriz; si no alcanza, se informa y se sigue.
    /// </summary>
    private async Task<(int Active, int Expired)> WebReservationsAsync(SignedIn admin, DateOnly today, DateTimeOffset realNow, Action<DateOnly, int, int> at,
        Action<string> log, CancellationToken ct)
    {
        var restore = clock.UtcNow;
        var presets = await admin.Send(new GetStorefrontPresetsQuery(), ct);
        var active = 0;
        var expired = 0;
        try
        {
            // Se intenta con cada armado publicado (del más barato al más caro) hasta conseguir UNA reserva vencida y UNA
            // activa: las ventas del período pueden dejar sin stock a un armado, y entonces se pasa al siguiente.
            var candidates = presets.Where(p => p.Available).OrderBy(p => p.Total).ToList();
            var next = 0;
            for (var i = 0; i < WebContacts.Length && next < candidates.Count; i++)
            {
                var contact = WebContacts[i];
                var isExpired = i == 0;
                // La vencida se reservó hace 4 días a las 11:20 (48 h de vigencia: venció hace 2); la activa, hoy a las 09:40 o,
                // si la carga corre antes de las 10:00, ayer a las 18:30 (nunca en el futuro respecto de la hora real).
                var earlyToday = realNow.TimeOfDay < new TimeSpan(10, 0, 0);
                var day = isExpired ? today.AddDays(-4) : earlyToday ? today.AddDays(-1) : today;
                at(day, isExpired ? 11 : earlyToday ? 18 : 9, isExpired ? 20 : earlyToday ? 30 : 40);
                var done = false;
                while (!done && next < candidates.Count)
                {
                    var preset = candidates[next++];
                    var lines = preset.Lines.Select(l => new StorefrontReservationLineInput(l.Sku, l.Quantity, l.Slot)).ToList();
                    try
                    {
                        var result = await admin.Send(new CreateStorefrontReservationCommand(lines, new StorefrontContactInput(contact.Name, contact.Phone, contact.Email),
                            contact.Notes, $"datos-prueba-{preset.Number}-{i}", preset.Name), ct);
                        log($"… {day:dd/MM/yyyy}: reserva web {result.Reservation.Number} de {contact.Name} ({preset.Name}) por Bs {result.Reservation.Total:N2}, " +
                            $"vence el {result.Reservation.ReservedUntil:dd/MM/yyyy HH:mm}.");
                        if (isExpired)
                        {
                            expired++;
                        }
                        else
                        {
                            active++;
                        }
                        done = true;
                    }
                    catch (DomainException ex)
                    {
                        log($"… {day:dd/MM/yyyy}: la reserva web de {contact.Name} con «{preset.Name}» no se registró ({ex.Message}); se prueba con el siguiente armado.");
                    }
                }
            }
        }
        finally
        {
            clock.StartAt(restore);
        }
        if (expired > 0)
        {
            // El trabajo en segundo plano del gateway (cada 5 min) cierra las reservas vencidas: aquí lo hace la carga
            var closed = await admin.Send(new ExpirePcBuildReservationsCommand(), ct);
            log($"… hoy: el vencimiento de reservas cerró {closed} reserva(s) web vencida(s) (el stock volvió a estar disponible).");
        }
        return (active, expired);
    }

    /// <summary>
    /// V4.2 · Lo propio de la tienda de tecnología en la operación simulada, con los casos de uso de la edición (reglas T-02 a
    /// T-06): casos RMA en todos sus estados (reemplazo, proveedor, rechazo por mal uso, reparado y entregado, en diagnóstico
    /// y recién recibido), los 8 armados del catálogo (cotizados con la vigencia del JSON, dos cobrados en la caja de su
    /// sucursal y los incompatibles marcados: uno en borrador y otro cotizado con la confirmación explícita), una devolución
    /// por falla con la unidad devuelta al proveedor y dos tomas físicas (una contabilizada, otra en curso). Los
    /// serializados se cuentan sin diferencias (una diferencia en un serializado se rechaza, <c>serial.count_adjustment</c>).
    /// </summary>
    private sealed class TechAgenda
    {
        private const string BuildTagPrefix = "build:";

        private readonly LocalDataSeeder _seeder;
        private readonly DateOnly _today;
        private readonly Action<DateOnly, int, int> _at;
        private readonly Action<string> _log;
        private readonly SignedIn _admin;
        private readonly IReadOnlyDictionary<string, BranchTeam> _teams;
        private readonly MinvWriteDbContext _db;
        private readonly SeededCatalog _catalog;
        private readonly BillingScenario? _billing;
        private readonly CancellationToken _ct;
        private readonly List<SoldUnit> _sold = [];
        private readonly HashSet<string> _usedSerials = new(StringComparer.Ordinal);
        private readonly List<DateOnly> _days = [];
        private readonly List<ClaimPlan> _claims;
        private readonly List<(TechBuild Build, DateOnly QuoteDay, DateOnly? SaleDay)> _builds = [];
        private readonly Dictionary<string, string> _buildNumbers = new(StringComparer.Ordinal);
        private readonly DateOnly _defectiveDay;
        private readonly DateOnly _countDay;
        private SoldUnit? _defective;

        public TechAgenda(LocalDataSeeder seeder, DateOnly start, DateOnly today, Action<DateOnly, int, int> at, Action<string> log, SignedIn admin,
            IReadOnlyDictionary<string, BranchTeam> teams, MinvWriteDbContext db, SeededCatalog catalog, BillingScenario? billing, CancellationToken ct)
        {
            _seeder = seeder;
            _today = today;
            _at = at;
            _log = log;
            _admin = admin;
            _teams = teams;
            _db = db;
            _catalog = catalog;
            _billing = billing;
            _ct = ct;
            for (var d = start.AddDays(1); d <= today; d = d.AddDays(1))
            {
                if (d.DayOfWeek != DayOfWeek.Sunday)
                {
                    _days.Add(d);
                }
            }
            _claims =
            [
                new(BranchMain, 0.35, -1, [(ClaimStep.Diagnose, 1), (ClaimStep.Replace, 2), (ClaimStep.Deliver, 2), (ClaimStep.Dispose, 4)], "reemplazo"),
                new(BranchSantaCruz, 0.45, -1, [(ClaimStep.Diagnose, 1), (ClaimStep.Repair, 3), (ClaimStep.Deliver, 4)], "reparado y entregado"),
                new(BranchCochabamba, 0.5, -1, [(ClaimStep.Diagnose, 1), (ClaimStep.Supplier, 2), (ClaimStep.Repair, 8)], "reparado por el proveedor"),
                new(BranchCochabamba, 0.62, -1, [(ClaimStep.Diagnose, 1), (ClaimStep.Reject, 2)], "rechazado (mal uso, fuera de la cobertura)"),
                new(BranchMain, 0.75, -1, [(ClaimStep.Diagnose, 1), (ClaimStep.Supplier, 2)], "enviado al proveedor"),
                new(BranchMain, 1, 1, [(ClaimStep.Diagnose, 1), (ClaimStep.Note, 1)], "en diagnóstico"),
                new(BranchSantaCruz, 1, 0, [], "recién recibido"),
            ];
            foreach (var claim in _claims)
            {
                claim.OpenDay = claim.DaysBeforeToday >= 0 ? DayBefore(claim.DaysBeforeToday) : DayAt(claim.Fraction);
            }
            // Armados: uno por día hábil en los últimos 8 (el primero ya vencido si su vigencia es de 7 días); los que el JSON
            // da por aprobados o convertidos en venta se cobran el día hábil siguiente en la caja de su sucursal
            var builds = TechSeedCatalog.Current.Builds;
            for (var i = 0; i < builds.Count; i++)
            {
                var index = Math.Max(0, _days.Count - builds.Count + i);
                var quote = _days[Math.Min(index, _days.Count - 1)];
                var sells = builds[i].Status is "Aprobada" or "Convertida en venta";
                DateOnly? sale = sells && index + 1 < _days.Count ? _days[index + 1] : null;
                _builds.Add((builds[i], quote, sale));
            }
            _defectiveDay = DayAt(0.8);
            _countDay = DayAt(0.55);
        }

        /// <summary>Armados cobrados en la caja.</summary>
        public int BuildsSold { get; private set; }

        /// <summary>V6 · Armados publicados como sugeridos en la tienda web.</summary>
        public int BuildsPublished { get; private set; }

        public static bool IsBuildTag(string tag) => tag.StartsWith(BuildTagPrefix, StringComparison.Ordinal);

        /// <summary>Recuerda las unidades serializadas de una venta (candidatas a garantía o devolución por falla).</summary>
        public void NoteSale(DateOnly day, string branch, string invoice, string customer, IEnumerable<SaleLineInput> lines)
        {
            foreach (var line in lines)
            {
                foreach (var serial in line.Serials ?? [])
                {
                    _sold.Add(new SoldUnit(day, branch, invoice, customer, line.Sku, serial));
                }
            }
        }

        /// <summary>Ventas de armados del día (la primera venta de la mañana en la caja de su sucursal: las piezas llegaron
        /// temprano y el cliente pasa a recoger su equipo).</summary>
        public IEnumerable<SaleSlot> BuildSales(DateOnly day) =>
            _builds.Where(b => b.SaleDay == day && _buildNumbers.ContainsKey(b.Build.Number))
                .Select(b => new SaleSlot(8 * 60 + 55, b.Build.Branch, BuildTagPrefix + _buildNumbers[b.Build.Number]));

        /// <summary>Cobra un armado cotizado en la caja (series de las piezas escaneadas del stock de la sucursal).</summary>
        public async Task SellBuildAsync(SignedIn cashier, string branch, string tag, DateOnly day)
        {
            var number = tag[BuildTagPrefix.Length..];
            var build = _builds.First(b => _buildNumbers.GetValueOrDefault(b.Build.Number) == number).Build;
            var warehouse = _teams[branch].Warehouse;
            var taken = new List<SaleLineInput>();
            foreach (var group in build.Lines.GroupBy(l => l.Sku))
            {
                var product = TechSeedCatalog.Current.Product(group.Key);
                var quantity = group.Sum(l => l.Quantity);
                if (!_seeder._stock.TryTake(warehouse, product, quantity, _seeder._rng, out var serials))
                {
                    _seeder._stock.GiveBack(warehouse, taken);
                    _log($"… {day:dd/MM/yyyy}: el armado {number} no se cobró: falta {product.Sku} en {branch}.");
                    return;
                }
                taken.Add(new SaleLineInput(product.Sku, quantity, 0, serials));
            }
            var customer = _catalog.CustomerCodes[build.Customer];
            var method = branch == BranchSantaCruz ? "TARJETA" : "TRANSFERENCIA";
            var (buyer, card) = _billing?.ForSale(customer, method, null) ?? (null, null);
            try
            {
                var result = await cashier.Send(new SellPcBuildCommand(number, method,
                    taken.Where(t => t.Serials is { Count: > 0 }).Select(t => new SkuSerials(t.Sku, t.Serials!)).ToList(), null,
                    $"{method[..2]}-{_seeder._rng.Next(100000, 999999)}", buyer, card), _ct);
                BuildsSold++;
                NoteSale(day, branch, result.InvoiceNumber, customer, taken);
                if (_billing is not null)
                {
                    await _billing.AfterSaleAsync(cashier, branch, result, taken, day, buyer, tag);
                }
                _log($"… {day:dd/MM/yyyy}: el armado {number} ({build.Name}) se cobró en la caja de {branch}: venta {result.InvoiceNumber} por Bs {result.Total:N2}.");
            }
            catch (DomainException ex)
            {
                _seeder._stock.GiveBack(warehouse, taken);
                _log($"… {day:dd/MM/yyyy}: el armado {number} no se cobró: {ex.Message}");
            }
        }

        /// <summary>
        /// Al cerrar el día: pasos de los casos RMA, cotización de los armados (y las piezas que la casa matriz envía a la
        /// sucursal que los va a cobrar), devolución por falla y toma física contabilizada, desde <paramref name="startMinute"/>
        /// (18:00; hoy, enseguida de la última venta: nada queda después de la hora final de la carga). Devuelve los envíos de
        /// piezas.
        /// </summary>
        public async Task<IReadOnlyList<PartsShipment>> EndOfDayAsync(DateOnly day, bool isToday, int startMinute)
        {
            var minute = startMinute;
            void Next()
            {
                _at(day, minute / 60, minute % 60);
                minute += 3;
            }

            // Garantías: primero los pasos de los casos abiertos, después los casos nuevos del día
            foreach (var claim in _claims.Where(c => c.Number is not null))
            {
                foreach (var (step, offset) in claim.Steps.Where(s => StepDay(claim.OpenDay, s.Offset) == day))
                {
                    Next();
                    await StepAsync(claim, step, day);
                }
            }
            foreach (var claim in _claims.Where(c => c.Number is null && c.OpenDay == day))
            {
                Next();
                await OpenClaimAsync(claim, day);
                foreach (var (step, _) in claim.Steps.Where(s => claim.Number is not null && StepDay(claim.OpenDay, s.Offset) == day))
                {
                    Next();
                    await StepAsync(claim, step, day);
                }
            }

            if (day == _defectiveDay)
            {
                Next();
                await DefectiveReturnAsync(day);
            }
            if (_defective is not null && day > _defectiveDay && day == StepDay(_defectiveDay, 1))
            {
                Next();
                await _teams[BranchMain].Keeper.Send(new DisposeSerialCommand(_defective.Serial, SerialDisposal.ReturnToSupplier,
                    "Unidad con falla de fábrica devuelta al proveedor (espera su nota de crédito)", _defective.Sku), _ct);
            }
            if (day == _countDay && !isToday)
            {
                Next();
                await CycleCountAsync(day);
            }

            // Armados de PC del día (en el orden del catálogo: así sus números coinciden con los del JSON)
            var shipments = new List<PartsShipment>();
            foreach (var (build, quoteDay, saleDay) in _builds.Where(b => b.QuoteDay == day))
            {
                Next();
                var number = await QuoteAsync(build, day);
                if (number is not null && saleDay is not null && build.Branch != BranchMain)
                {
                    // Todas las piezas salen del almacén central (el ensamblado se hace con piezas selladas del proveedor); un
                    // armado de la casa matriz ya tiene sus piezas en su almacén (no hay transferencia a sí misma)
                    var parts = build.Lines.GroupBy(l => l.Sku).Select(g => (Product: TechSeedCatalog.Current.Product(g.Key), Quantity: g.Sum(l => l.Quantity)))
                        .ToList();
                    shipments.Add(new PartsShipment(build.Branch, _teams[build.Branch].Warehouse, parts, $"Piezas para el armado {number} ({build.Name})"));
                }
            }
            if (shipments.Count > 0)
            {
                var ship = Math.Max(minute, startMinute + 50);   // 18:50 (el despacho de las piezas, después de las cotizaciones)
                _at(day, ship / 60, ship % 60);
            }
            return shipments;
        }

        /// <summary>Toma física en curso al final del período (periféricos sin serie con diferencias y serializados exactos).</summary>
        public async Task FinalCountAsync(SignedIn bodega)
        {
            var open = await bodega.Send(new OpenPhysicalCountCommand(MainWarehouse, _today, "Conteo cíclico de periféricos y consolas (en curso)"), _ct);
            var products = TechSeedCatalog.Current.Products.Where(p => !p.IsService && TechSeedCatalog.Current.Lineage(p.Category).Contains("PER")).Take(5)
                .Concat(TechSeedCatalog.Current.Products.Where(p => p.TracksSerials && TechSeedCatalog.Current.Lineage(p.Category).Contains("CON")).Take(2));
            foreach (var p in products)
            {
                var bin = (await _seeder._stock.BinsAsync(MainWarehouse, p.Sku, _ct)).OrderByDescending(b => b.OnHand).FirstOrDefault();
                if (bin.OnHand > 0)
                {
                    var delta = p.TracksSerials ? 0 : _seeder._rng.Next(-1, 2);
                    await bodega.Send(new RecordCountCommand(open.PhysicalCountId, p.Sku, bin.BinCode, Math.Max(0, bin.OnHand + delta)), _ct);
                }
            }
        }

        /// <summary>Resumen de la edición Tecnología (desde la base).</summary>
        public async Task<SeedTech> SummaryAsync()
        {
            _db.ChangeTracker.Clear();
            var claims = await _db.WarrantyClaims.AsNoTracking().Select(c => c.Status).ToListAsync(_ct);
            var builds = await _db.PcBuilds.AsNoTracking().Select(b => new { b.Status, b.QuotedWithErrors }).ToListAsync(_ct);
            var tech = TechSeedCatalog.Current;
            return new SeedTech(await _db.Categories.CountAsync(_ct), await _db.SpecDefinitions.CountAsync(_ct), await _db.ProductSpecValues.CountAsync(_ct),
                await _db.Brands.CountAsync(_ct), tech.Products.Count(p => p.TracksSerials), await _db.SerialNumbers.CountAsync(_ct),
                await _db.SerialNumbers.CountAsync(s => s.Status == SerialNumberStatus.InStock, _ct), claims.Count,
                claims.GroupBy(c => c).OrderBy(g => g.Key).Select(g => $"{g.Key}: {g.Count()}").ToList(), builds.Count,
                builds.Count(b => b.Status == PcBuildStatus.Sold), tech.Builds.Count(b => b.MarkedIncompatible),
                await _db.Set<SalesReturn>().CountAsync(_ct), await _db.PcBuilds.CountAsync(b => b.PublishedToWeb, _ct));
        }

        // ============================================================================================ garantías
        private async Task OpenClaimAsync(ClaimPlan claim, DateOnly day)
        {
            var team = _teams[claim.Branch];
            var tech = TechSeedCatalog.Current;
            var candidates = _sold.Where(u => u.Branch == claim.Branch && u.Day < day && !_usedSerials.Contains(u.Serial))
                .OrderBy(u => u.Customer == "CF" ? 1 : 0).ThenBy(_ => _seeder._rng.Next()).ToList();
            if (claim.Steps.Any(s => s.Step == ClaimStep.Replace))
            {
                // El reemplazo sale del stock de la sucursal: un producto del que haya unidades
                candidates = candidates.Where(u => _seeder._stock.SerialCount(team.Warehouse, u.Sku) >= 2).ToList();
            }
            foreach (var unit in candidates.Take(12))
            {
                var status = await team.Cashier.Send(new GetWarrantyStatusQuery(unit.Serial, unit.Sku), _ct);
                if (status.Status != SerialNumberStatus.Sold || !status.InWarranty || status.OpenClaim is not null)
                {
                    continue;
                }
                var category = tech.Product(unit.Sku).Category;
                var issue = IssueFor(category, tech.Root(category));
                var row = await team.Cashier.Send(new OpenWarrantyClaimCommand(unit.Serial, issue, Sku: unit.Sku), _ct);
                _usedSerials.Add(unit.Serial);
                claim.Number = row.Number;
                claim.Unit = unit;
                _log($"… {day:dd/MM/yyyy}: caso {row.Number} abierto en {claim.Branch} por la serie {unit.Serial} ({unit.Sku}, venta {unit.Invoice}): {issue} " +
                     $"[{claim.Title}].");
                return;
            }
            _log($"… {day:dd/MM/yyyy}: no hubo una unidad vendida en {claim.Branch} para el caso RMA «{claim.Title}».");
        }

        private async Task StepAsync(ClaimPlan claim, ClaimStep step, DateOnly day)
        {
            var team = _teams[claim.Branch];
            var number = claim.Number!;
            var unit = claim.Unit!;
            try
            {
                switch (step)
                {
                    case ClaimStep.Diagnose:
                        await team.Keeper.Send(new MoveWarrantyClaimCommand(number, WarrantyClaimStatus.Diagnosing,
                            Note: "Revisión técnica en el taller: se reproduce la falla reportada"), _ct);
                        break;
                    case ClaimStep.Supplier:
                        await team.Keeper.Send(new MoveWarrantyClaimCommand(number, WarrantyClaimStatus.SentToSupplier,
                            Note: $"Enviado al servicio técnico del proveedor con la guía GT-{_seeder._rng.Next(10000, 99999)}"), _ct);
                        break;
                    case ClaimStep.Repair:
                        await team.Keeper.Send(new MoveWarrantyClaimCommand(number, WarrantyClaimStatus.Repaired,
                            "Reparado: se reemplazó el componente defectuoso y se probó 24 horas", Note: "Listo para entregar al cliente"), _ct);
                        break;
                    case ClaimStep.Replace:
                        var product = TechSeedCatalog.Current.Product(unit.Sku);
                        if (_seeder._stock.TryTake(team.Warehouse, product, 1, _seeder._rng, out var replacement))
                        {
                            await team.Keeper.Send(new IssueWarrantyReplacementCommand(number, replacement![0],
                                "Falla de fábrica confirmada: se entregó una unidad nueva en reemplazo"), _ct);
                        }
                        else
                        {
                            await team.Keeper.Send(new MoveWarrantyClaimCommand(number, WarrantyClaimStatus.Repaired, "Reparado en el taller (sin unidades para reemplazo)"), _ct);
                        }
                        break;
                    case ClaimStep.Reject:
                        await team.Keeper.Send(new MoveWarrantyClaimCommand(number, WarrantyClaimStatus.Rejected,
                            "Garantía rechazada: daño físico (golpe o líquido) y sello de garantía roto: mal uso, fuera de la cobertura",
                            Note: "Se ofreció la reparación con cargo; el cliente pasará a recoger el equipo"), _ct);
                        break;
                    case ClaimStep.Deliver:
                        await team.Keeper.Send(new MoveWarrantyClaimCommand(number, WarrantyClaimStatus.Delivered, Note: "Entregado al cliente con su comprobante"), _ct);
                        break;
                    case ClaimStep.Note:
                        await team.Keeper.Send(new AddWarrantyClaimNoteCommand(number, "Se pidió el repuesto al proveedor: llega en 5 días hábiles"), _ct);
                        break;
                    case ClaimStep.Dispose:
                        await team.Keeper.Send(new DisposeSerialCommand(unit.Serial, SerialDisposal.ReturnToSupplier,
                            $"Unidad defectuosa del caso {number} devuelta al proveedor (espera su nota de crédito)", unit.Sku), _ct);
                        break;
                }
                _log($"… {day:dd/MM/yyyy}: caso {number}: {StepText(step)}.");
            }
            catch (DomainException ex)
            {
                _log($"… {day:dd/MM/yyyy}: el paso {step} del caso {number} no se registró: {ex.Message}");
            }
        }

        private static string StepText(ClaimStep step) => step switch
        {
            ClaimStep.Diagnose => "en diagnóstico",
            ClaimStep.Supplier => "enviado al proveedor",
            ClaimStep.Repair => "reparado",
            ClaimStep.Replace => "unidad de reemplazo entregada (sale del stock con su asiento de garantía)",
            ClaimStep.Reject => "garantía rechazada por mal uso",
            ClaimStep.Deliver => "entregado al cliente",
            ClaimStep.Note => "nota del técnico",
            _ => "unidad defectuosa devuelta al proveedor",
        };

        /// <summary>Falla reportada creíble para el tipo de equipo: por subcategoría y, si no hay una propia, por categoría raíz.</summary>
        private static string IssueFor(string category, string root) => category switch
        {
            "GPU" => "Artefactos en pantalla y reinicios bajo carga",
            "CPU" => "Reinicios y pantallazos azules al exigir el procesador",
            "MB" => "No da video y se apaga al encender (código de error en la placa)",
            "RAM" => "Errores de memoria y pantallazos azules en la prueba",
            "STO" => "La unidad desaparece del sistema y pierde archivos",
            "PSU" => "Se apaga bajo carga y huele a quemado",
            "CASE" => "Los puertos USB frontales no funcionan",
            "COOL" => "La bomba hace ruido y el procesador se sobrecalienta",
            "LAPG" or "LAPU" => "La batería no carga y el equipo se apaga solo",
            "DESK" => "No enciende después de un corte de luz",
            "KEY" => "Varias teclas no responden",
            "MOU" => "Doble clic involuntario y desconexiones",
            "AUD" => "El micrófono no funciona y un auricular no suena",
            "CAM" => "El equipo no reconoce la cámara",
            "CHA" => "El pistón de la silla se hunde",
            "CPS" or "CXB" or "CNS" => "La consola se apaga sola y se sobrecalienta",
            "MAND" => "Deriva en la palanca y el mando no carga",
            "CARG" => "La estación no carga los mandos",
            "ALMC" => "La consola no reconoce la unidad",
            _ => IssueForRoot(root),
        };

        private static string IssueForRoot(string root) => root switch
        {
            "COMP" => "Falla intermitente bajo carga",
            "PC" => "La batería no carga y el equipo se apaga solo",
            "MON" => "Píxeles muertos y parpadeo del panel",
            "PER" => "Deja de funcionar y se desconecta",
            "CON" => "La consola se apaga sola y se sobrecalienta",
            "ACC" => "El accesorio no funciona con la consola",
            "RED" => "Se reinicia solo y pierde la conexión",
            _ => "No enciende",
        };

        // ============================================================================================ devolución por falla
        private async Task DefectiveReturnAsync(DateOnly day)
        {
            var candidates = _sold.Where(u => u.Branch == BranchMain && u.Day < day && u.Customer != "CF" && !_usedSerials.Contains(u.Serial))
                .OrderByDescending(u => u.Day).ToList();
            foreach (var unit in candidates.Take(12))
            {
                var status = await _teams[BranchMain].Cashier.Send(new GetWarrantyStatusQuery(unit.Serial, unit.Sku), _ct);
                if (status.Status != SerialNumberStatus.Sold || status.OpenClaim is not null)
                {
                    continue;
                }
                try
                {
                    var result = await _admin.Send(new CreateSalesReturnCommand(unit.Invoice, "Falla de fábrica al primer uso: el cliente pidió su dinero",
                        "EFECTIVO", [new ReturnLineInput(unit.Sku, 1, [unit.Serial])], Defective: true), _ct);
                    if (result.CreditNoteId is { } note)
                    {
                        await _admin.Send(new DispatchFiscalDocumentsCommand(note), _ct);
                    }
                    _usedSerials.Add(unit.Serial);
                    _defective = unit;
                    _log($"… {day:dd/MM/yyyy}: devolución por falla {result.Number} de la venta {unit.Invoice}: la serie {unit.Serial} quedó en garantía " +
                         "(no vuelve al stock vendible).");
                    return;
                }
                catch (DomainException ex)
                {
                    _log($"… {day:dd/MM/yyyy}: la devolución por falla de {unit.Invoice} no se registró: {ex.Message}");
                }
            }
        }

        // ============================================================================================ toma física
        private async Task CycleCountAsync(DateOnly day)
        {
            var bodega = _teams[BranchMain].Keeper;
            var open = await bodega.Send(new OpenPhysicalCountCommand(MainWarehouse, day, "Conteo cíclico de videojuegos, cables y licencias"), _ct);
            var tech = TechSeedCatalog.Current;
            var products = tech.Products.Where(p => !p.TracksSerials && !p.IsService && p.Category is "JUE" or "CAB" or "LIC").Take(8)
                .Concat(tech.Products.Where(p => p.TracksSerials && p.Category == "MOU").Take(1));
            var counted = 0;
            foreach (var p in products)
            {
                var bin = (await _seeder._stock.BinsAsync(MainWarehouse, p.Sku, _ct)).OrderByDescending(b => b.OnHand).FirstOrDefault();
                if (bin.OnHand > 0)
                {
                    // Los serializados se cuentan exactos (una diferencia con serie se registra con sus series, no por conteo)
                    var delta = p.TracksSerials ? 0 : _seeder._rng.NextDouble() < 0.6 ? 0 : _seeder._rng.Next(-1, 2);
                    await bodega.Send(new RecordCountCommand(open.PhysicalCountId, p.Sku, bin.BinCode, Math.Max(0, bin.OnHand + delta)), _ct);
                    counted++;
                }
            }
            var posted = await bodega.Send(new PostPhysicalCountCommand(open.PhysicalCountId, Confirmed: true), _ct);
            _log($"… {day:dd/MM/yyyy}: toma física {posted.Number} contabilizada ({counted} productos: {posted.Surpluses} sobrantes, " +
                 $"{posted.Shortages} faltantes).");
        }

        // ============================================================================================ armados de PC
        private async Task<string?> QuoteAsync(TechBuild build, DateOnly day)
        {
            var tech = TechSeedCatalog.Current;
            var items = build.Lines.Select(l => new PcBuildItemInput(tech.SlotOf(l)!.Value, l.Sku, l.Quantity)).ToList();
            // El primer armado incompatible queda en borrador (se ve marcado en la lista); el segundo se cotiza con la
            // confirmación explícita del vendedor (queda marcado como cotizado con errores, regla T-06)
            var draft = build.MarkedIncompatible && !_builds.Any(b => b.Build.MarkedIncompatible && _buildNumbers.ContainsKey(b.Build.Number));
            try
            {
                var row = await _teams[build.Branch].Seller.Send(new SavePcBuildCommand(null, build.Name, _catalog.CustomerCodes[build.Customer], items,
                    Quote: !draft, ValidDays: build.ValidDays, AcceptIncompatible: build.MarkedIncompatible), _ct);
                _buildNumbers[build.Number] = row.Number;
                if (row.Number != build.Number)
                {
                    _log($"… aviso: el armado {build.Number} del catálogo quedó con el número {row.Number}.");
                }
                _log($"… {day:dd/MM/yyyy}: armado {row.Number} «{build.Name}» {(draft ? "guardado en borrador" : "cotizado")} por Bs {row.Total:N2}" +
                     $"{(row.IsCompatible ? string.Empty : " (incompatible: " + (draft ? "pendiente de corregir)" : "cotizado con la confirmación del vendedor)"))}.");
                // V6 · Los armados sugeridos del catálogo (compatibles y cotizados; los de prueba de incompatibilidad no) se publican en
                // la tienda web (regla S-09)
                if (!draft && !build.MarkedIncompatible && !build.Name.StartsWith("PRUEBA", StringComparison.OrdinalIgnoreCase))
                {
                    await _teams[build.Branch].Seller.Send(new PublishPcBuildCommand(row.Number), _ct);
                    BuildsPublished++;
                }
                return row.Number;
            }
            catch (DomainException ex)
            {
                _log($"… {day:dd/MM/yyyy}: el armado {build.Number} no se guardó: {ex.Message}");
                return null;
            }
        }

        // ============================================================================================ calendario
        private DateOnly DayAt(double fraction) => _days.Count == 0
            ? _today
            : _days[Math.Clamp((int)Math.Round((_days.Count - 1) * fraction, MidpointRounding.AwayFromZero), 0, _days.Count - 1)];

        private DateOnly DayBefore(int businessDays) => _days.Count == 0 ? _today : _days[Math.Max(0, _days.Count - 1 - businessDays)];

        /// <summary>Día hábil a <paramref name="offset"/> días hábiles de <paramref name="from"/> (o después de hoy: nunca llega).</summary>
        private DateOnly StepDay(DateOnly from, int offset)
        {
            var index = _days.IndexOf(from);
            return index < 0 || index + offset >= _days.Count ? DateOnly.MaxValue : _days[index + offset];
        }
    }
}
