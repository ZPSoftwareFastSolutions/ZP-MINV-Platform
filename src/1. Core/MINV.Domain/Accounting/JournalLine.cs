using MINV.Domain.Common;

namespace MINV.Domain.Accounting;

/// <summary>Línea de un asiento (debe o haber).</summary>
public sealed class JournalLine : Entity
{
    private JournalLine()
    {
    }

    public JournalLine(Guid tenantId, Guid journalEntryId, Guid accountId, Guid? costCenterId, decimal debit, decimal credit, string? memo)
        : base(tenantId)
    {
        JournalEntryId = Guard.NotEmpty(journalEntryId, nameof(journalEntryId));
        AccountId = Guard.NotEmpty(accountId, nameof(accountId));
        CostCenterId = Guard.NotEmptyIfPresent(costCenterId, nameof(costCenterId));
        Debit = Guard.NonNegative(debit, "El debe");
        Credit = Guard.NonNegative(credit, "El haber");
        Memo = Guard.OptionalText(memo, "El detalle", 200);
    }

    public Guid JournalEntryId { get; private set; }

    public Guid AccountId { get; private set; }

    public Guid? CostCenterId { get; private set; }

    public decimal Debit { get; private set; }

    public decimal Credit { get; private set; }

    public string? Memo { get; private set; }
}
