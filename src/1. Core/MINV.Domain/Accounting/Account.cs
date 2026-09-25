using MINV.Domain.Common;

namespace MINV.Domain.Accounting;

/// <summary>Cuenta del plan contable.</summary>
public sealed class Account : Entity
{
    private Account()
    {
    }

    public Account(Guid tenantId, string code, string name, AccountType accountType, Guid? parentAccountId, bool isPostable)
        : base(tenantId)
    {
        Code = Guard.Text(code, "El código de la cuenta", 20);
        Name = Guard.Text(name, "El nombre", 120);
        AccountType = Guard.Defined(accountType, "El tipo");
        ParentAccountId = Guard.NotEmptyIfPresent(parentAccountId, nameof(parentAccountId));
        IsPostable = isPostable;
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public AccountType AccountType { get; private set; }

    public Guid? ParentAccountId { get; private set; }

    public bool IsPostable { get; private set; }
}
