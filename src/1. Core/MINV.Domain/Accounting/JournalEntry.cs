using MINV.Domain.Common;

namespace MINV.Domain.Accounting;

/// <summary>Asiento contable de partida doble. Solo se contabiliza si cuadra (Σ debe = Σ haber); contabilizado es
/// inmutable (las correcciones son asientos de reversión).</summary>
public sealed class JournalEntry : Entity, IConcurrencyAware, IAggregateRoot
{
    private readonly List<JournalLine> _lines = new();

    private JournalEntry()
    {
    }

    public JournalEntry(Guid tenantId, string number, Guid fiscalPeriodId, DateOnly entryDate, string description,
        Guid currencyId, Guid? sourceCorrelationId = null)
        : base(tenantId)
    {
        Number = Guard.Text(number, "El número", 30);
        FiscalPeriodId = Guard.NotEmpty(fiscalPeriodId, nameof(fiscalPeriodId));
        EntryDate = entryDate;
        Description = Guard.Text(description, "La glosa", 250);
        CurrencyId = Guard.NotEmpty(currencyId, nameof(currencyId));
        SourceCorrelationId = Guard.NotEmptyIfPresent(sourceCorrelationId, nameof(sourceCorrelationId));
        Status = JournalEntryStatus.Draft;
    }

    public string Number { get; private set; } = string.Empty;

    public Guid FiscalPeriodId { get; private set; }

    public DateOnly EntryDate { get; private set; }

    public string Description { get; private set; } = string.Empty;

    public Guid CurrencyId { get; private set; }

    public JournalEntryStatus Status { get; private set; }

    public DateTimeOffset? PostedAt { get; private set; }

    public Guid? PostedByUserId { get; private set; }

    /// <summary>Operación de origen (p. ej. la correlación de los movimientos de una toma física).</summary>
    public Guid? SourceCorrelationId { get; private set; }

    public uint RowVersion { get; private set; }

    public IReadOnlyCollection<JournalLine> Lines => _lines;

    public decimal TotalDebit => _lines.Sum(l => l.Debit);

    public decimal TotalCredit => _lines.Sum(l => l.Credit);

    public JournalLine Debit(Guid accountId, decimal amount, Guid? costCenterId = null, string? memo = null) =>
        AddLine(accountId, costCenterId, Guard.Positive(amount, "El debe"), 0m, memo);

    public JournalLine Credit(Guid accountId, decimal amount, Guid? costCenterId = null, string? memo = null) =>
        AddLine(accountId, costCenterId, 0m, Guard.Positive(amount, "El haber"), memo);

    public void Post(Guid userId, DateTimeOffset now)
    {
        EnsureDraft();
        Guard.That(_lines.Count >= 2, "journal.lines", "Un asiento necesita al menos dos líneas.");
        Guard.That(TotalDebit == TotalCredit, "journal.unbalanced",
            $"El asiento {Number} no cuadra: debe {TotalDebit:0.####} ≠ haber {TotalCredit:0.####}.");
        PostedByUserId = Guard.NotEmpty(userId, nameof(userId));
        PostedAt = now.ToUniversalTime();
        Status = JournalEntryStatus.Posted;
    }

    private JournalLine AddLine(Guid accountId, Guid? costCenterId, decimal debit, decimal credit, string? memo)
    {
        EnsureDraft();
        var line = new JournalLine(TenantId, Id, accountId, costCenterId, debit, credit, memo);
        _lines.Add(line);
        return line;
    }

    private void EnsureDraft() =>
        Guard.That(Status == JournalEntryStatus.Draft, "journal.posted", $"El asiento {Number} ya está contabilizado.");
}
