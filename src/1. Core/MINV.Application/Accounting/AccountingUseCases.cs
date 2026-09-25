using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Application.Inventory;
using MINV.Domain.Accounting;
using MINV.Domain.Common;
using MINV.Domain.Iam;

namespace MINV.Application.Accounting;

/// <summary>Cuenta con sus saldos del período (los grupos suman a sus subcuentas).</summary>
public sealed record AccountRow(string Code, string Name, AccountType Type, int Level, bool IsPostable, string? ParentCode, decimal Debit,
    decimal Credit, decimal Balance);

/// <summary>Plan de cuentas con saldos (movimientos contabilizados entre las fechas; sin fechas, todo).</summary>
[RequiresPermission(PermissionCodes.AccountingManage)]
public sealed record GetChartOfAccountsQuery(DateOnly? From = null, DateOnly? To = null) : IRequest<IReadOnlyList<AccountRow>>;

public sealed class GetChartOfAccountsHandler(IMinvDbContext db) : IRequestHandler<GetChartOfAccountsQuery, IReadOnlyList<AccountRow>>
{
    public async Task<IReadOnlyList<AccountRow>> Handle(GetChartOfAccountsQuery request, CancellationToken ct) =>
        await Ledger.ChartAsync(db, request.From, request.To, ct);
}

public sealed record JournalLineRow(string AccountCode, string AccountName, decimal Debit, decimal Credit, string? Memo);

public sealed record JournalEntryRow(string Number, DateOnly Date, string Description, JournalEntryStatus Status, decimal Total,
    IReadOnlyList<JournalLineRow> Lines);

/// <summary>Libro diario: asientos del período con sus líneas.</summary>
[RequiresPermission(PermissionCodes.AccountingManage)]
public sealed record GetJournalQuery(DateOnly From, DateOnly To) : IRequest<IReadOnlyList<JournalEntryRow>>;

public sealed class GetJournalHandler(IMinvDbContext db) : IRequestHandler<GetJournalQuery, IReadOnlyList<JournalEntryRow>>
{
    public async Task<IReadOnlyList<JournalEntryRow>> Handle(GetJournalQuery request, CancellationToken ct)
    {
        var start = request.From;
        var end = request.To;
        var entries = await db.Set<JournalEntry>().Include(e => e.Lines).Where(e => e.EntryDate >= start && e.EntryDate <= end)
            .OrderByDescending(e => e.EntryDate).ThenByDescending(e => e.Number).Take(5000).ToListAsync(ct);
        var accounts = await db.Set<Account>().ToDictionaryAsync(a => a.Id, ct);
        return entries.Select(e => new JournalEntryRow(e.Number, e.EntryDate, e.Description, e.Status, e.TotalDebit,
            e.Lines.OrderByDescending(l => l.Debit).Select(l => new JournalLineRow(accounts[l.AccountId].Code, accounts[l.AccountId].Name, l.Debit,
                l.Credit, l.Memo)).ToList())).ToList();
    }
}

/// <summary>Estado de resultados: ventas netas, costo de ventas, utilidad bruta, gastos y utilidad neta del período.</summary>
public sealed record IncomeStatement(DateOnly From, DateOnly To, IReadOnlyList<AccountRow> Revenue, IReadOnlyList<AccountRow> Costs,
    IReadOnlyList<AccountRow> Expenses, decimal TotalRevenue, decimal TotalCosts, decimal GrossProfit, decimal TotalExpenses, decimal NetIncome);

[RequiresPermission(PermissionCodes.AccountingManage)]
public sealed record GetIncomeStatementQuery(DateOnly From, DateOnly To) : IRequest<IncomeStatement>;

public sealed class GetIncomeStatementHandler(IMinvDbContext db) : IRequestHandler<GetIncomeStatementQuery, IncomeStatement>
{
    public async Task<IncomeStatement> Handle(GetIncomeStatementQuery request, CancellationToken ct)
    {
        var chart = await Ledger.ChartAsync(db, request.From, request.To, ct);
        var postable = chart.Where(a => a.IsPostable && a.Balance != 0).ToList();
        var revenue = postable.Where(a => a.Type == AccountType.Revenue).ToList();
        var costs = postable.Where(a => a.Type == AccountType.Expense && a.Code.StartsWith('5')).ToList();
        var expenses = postable.Where(a => a.Type == AccountType.Expense && !a.Code.StartsWith('5')).ToList();
        var totalRevenue = revenue.Sum(a => a.Balance);
        var totalCosts = costs.Sum(a => a.Balance);
        var totalExpenses = expenses.Sum(a => a.Balance);
        return new IncomeStatement(request.From, request.To, revenue, costs, expenses, totalRevenue, totalCosts, totalRevenue - totalCosts,
            totalExpenses, totalRevenue - totalCosts - totalExpenses);
    }
}

/// <summary>Asiento manual (gastos, aportes, pagos a proveedores…): debe cuadrar y usar cuentas imputables.</summary>
[RequiresPermission(PermissionCodes.AccountingManage)]
public sealed record CreateJournalEntryCommand(DateOnly Date, string Description, IReadOnlyList<JournalLineSpec> Lines) : IRequest<string>, IAuditableRequest
{
    public object AuditDetails => new { Date, Description, Lines };
}

public sealed class CreateJournalEntryValidator : AbstractValidator<CreateJournalEntryCommand>
{
    public CreateJournalEntryValidator()
    {
        RuleFor(x => x.Description).NotEmpty().WithMessage("Escriba la glosa del asiento.").MaximumLength(250);
        RuleFor(x => x.Lines).Must(l => l.Count(x => x.Debit > 0 || x.Credit > 0) >= 2).WithMessage("Un asiento necesita al menos dos líneas.");
        RuleFor(x => x.Lines).Must(l => l.Sum(x => x.Debit) == l.Sum(x => x.Credit))
            .WithMessage("El asiento no cuadra: el total del debe debe ser igual al del haber.");
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.AccountCode).NotEmpty().WithMessage("Elija la cuenta de cada línea.");
            l.RuleFor(x => x).Must(x => x.Debit >= 0 && x.Credit >= 0 && (x.Debit == 0 || x.Credit == 0))
                .WithMessage("Cada línea va al debe o al haber (no a ambos) y sin negativos.");
        });
    }
}

public sealed class CreateJournalEntryHandler(IMinvDbContext db, ICurrentUser user, ITenantContext tenant, IClock clock)
    : IRequestHandler<CreateJournalEntryCommand, string>
{
    public async Task<string> Handle(CreateJournalEntryCommand request, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new AccessDeniedException("Inicie sesión para contabilizar.");
        var config = await new InventoryLookups(db).ConfigAsync(ct);
        Guard.That(request.Date <= clock.TodayIn(config.TimeZoneId), "journal.date", "La fecha del asiento no puede ser futura.");
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var entry = await JournalPoster.PostAsync(db, tenant.TenantId, userId, request.Date, request.Description.Trim(), request.Lines,
                    clock.UtcNow, null, ct);
                await db.SaveChangesAsync(ct);
                return entry.Number;
            }
            catch (ConcurrencyConflictException) when (attempt < 3)
            {
                db.ClearTracking();
            }
        }
    }
}

/// <summary>Agrega una cuenta imputable bajo un grupo del plan (hereda su tipo).</summary>
[RequiresPermission(PermissionCodes.AccountingManage)]
public sealed record CreateAccountCommand(string Code, string Name, string ParentCode) : IRequest<string>, IAuditableRequest
{
    public object AuditDetails => new { Code, Name, ParentCode };
}

public sealed class CreateAccountHandler(IMinvDbContext db, ITenantContext tenant) : IRequestHandler<CreateAccountCommand, string>
{
    public async Task<string> Handle(CreateAccountCommand request, CancellationToken ct)
    {
        var code = request.Code.Trim();
        var parent = await db.Set<Account>().FirstOrDefaultAsync(a => a.Code == request.ParentCode.Trim(), ct)
                     ?? throw new NotFoundException($"La cuenta {request.ParentCode} no existe.");
        Guard.That(!parent.IsPostable, "account.parent", "Las cuentas nuevas se crean bajo un grupo (no bajo una cuenta imputable).");
        Guard.That(code.StartsWith(parent.Code + ".", StringComparison.Ordinal), "account.code", $"El código debe empezar con {parent.Code}.");
        Guard.That(!await db.Set<Account>().AnyAsync(a => a.Code == code, ct), "account.duplicate", $"Ya existe la cuenta {code}.");
        db.Set<Account>().Add(new Account(tenant.TenantId, code, request.Name.Trim(), parent.AccountType, parent.Id, true));
        await db.SaveChangesAsync(ct);
        return code;
    }
}

internal static class Ledger
{
    public static async Task<IReadOnlyList<AccountRow>> ChartAsync(IMinvDbContext db, DateOnly? start, DateOnly? end, CancellationToken ct)
    {
        var accounts = await db.Set<Account>().OrderBy(a => a.Code).ToListAsync(ct);
        var sums = await (from l in db.Set<JournalLine>()
                          join e in db.Set<JournalEntry>() on l.JournalEntryId equals e.Id
                          where e.Status == JournalEntryStatus.Posted && (start == null || e.EntryDate >= start) && (end == null || e.EntryDate <= end)
                          group l by l.AccountId into g
                          select new { g.Key, Debit = g.Sum(x => x.Debit), Credit = g.Sum(x => x.Credit) }).ToDictionaryAsync(x => x.Key, ct);
        var byId = accounts.ToDictionary(a => a.Id);
        var totals = accounts.ToDictionary(a => a.Id, a => sums.TryGetValue(a.Id, out var s) ? (s.Debit, s.Credit) : (0m, 0m));
        // Los grupos acumulan a sus hijos (de las hojas hacia la raíz)
        foreach (var account in accounts.OrderByDescending(a => a.Code.Count(ch => ch == '.')))
        {
            if (account.ParentAccountId is { } parent && totals.ContainsKey(parent))
            {
                var (d, c) = totals[account.Id];
                var (pd, pc) = totals[parent];
                totals[parent] = (pd + d, pc + c);
            }
        }
        return accounts.Select(a =>
        {
            var (d, c) = totals[a.Id];
            return new AccountRow(a.Code, a.Name, a.AccountType, a.Code.Count(ch => ch == '.'), a.IsPostable,
                a.ParentAccountId is { } p && byId.TryGetValue(p, out var parent) ? parent.Code : null, d, c, ChartOfAccounts.NaturalBalance(a.AccountType, d, c));
        }).ToList();
    }
}
