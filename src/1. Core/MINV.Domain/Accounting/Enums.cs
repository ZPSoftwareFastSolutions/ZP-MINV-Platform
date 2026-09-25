namespace MINV.Domain.Accounting;

/// <summary>Naturaleza de una cuenta contable. Se guarda como texto.</summary>
public enum AccountType
{
    Asset,
    Liability,
    Equity,
    Revenue,
    Expense,
}

/// <summary>Estado de un período contable. Se guarda como texto.</summary>
public enum FiscalPeriodStatus
{
    Open,
    Closed,
}

/// <summary>Estado de un asiento. Se guarda como texto.</summary>
public enum JournalEntryStatus
{
    Draft,
    Posted,
}
