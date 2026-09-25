using MINV.Domain.Common;

namespace MINV.Domain.Accounting;

/// <summary>Período contable mensual.</summary>
public sealed class FiscalPeriod : Entity, IConcurrencyAware
{
    private FiscalPeriod()
    {
    }

    public FiscalPeriod(Guid tenantId, short year, short month)
        : base(tenantId)
    {
        Year = year;
        Month = month;
        Status = FiscalPeriodStatus.Open;
    }

    public short Year { get; private set; }

    public short Month { get; private set; }

    public FiscalPeriodStatus Status { get; private set; }

    /// <summary>Token de concurrencia optimista (xmin de PostgreSQL).</summary>
    public uint RowVersion { get; private set; }
}
