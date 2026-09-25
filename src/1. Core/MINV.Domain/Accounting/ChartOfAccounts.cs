namespace MINV.Domain.Accounting;

/// <summary>Cuentas que usan los asientos automáticos (ventas, compras, ajustes).</summary>
public static class AccountCodes
{
    public const string Cash = "1.1.01";
    public const string Bank = "1.1.02";
    public const string Receivables = "1.1.03";
    public const string VatCredit = "1.1.04";
    public const string Inventory = "1.1.05";
    public const string Payables = "2.1.01";
    public const string VatDebit = "2.1.02";
    public const string Capital = "3.1.01";
    public const string RetainedEarnings = "3.1.02";
    public const string Sales = "4.1.01";
    public const string InventorySurplus = "4.1.02";
    public const string CostOfSales = "5.1.01";
    public const string InventoryShrinkage = "5.1.09";
}

/// <summary>
/// Plan de cuentas por defecto de una empresa (jerárquico: los grupos no reciben movimientos, solo las cuentas
/// imputables). Se siembra al crear la empresa; el administrador puede agregar cuentas.
/// </summary>
public static class ChartOfAccounts
{
    /// <summary>Código, nombre, tipo, código del padre (null = raíz) e imputable.</summary>
    public static readonly IReadOnlyList<(string Code, string Name, AccountType Type, string? Parent, bool Postable)> Defaults =
    [
        ("1", "ACTIVO", AccountType.Asset, null, false),
        ("1.1", "Activo corriente", AccountType.Asset, "1", false),
        (AccountCodes.Cash, "Caja", AccountType.Asset, "1.1", true),
        (AccountCodes.Bank, "Bancos", AccountType.Asset, "1.1", true),
        (AccountCodes.Receivables, "Cuentas por cobrar a clientes", AccountType.Asset, "1.1", true),
        (AccountCodes.VatCredit, "IVA crédito fiscal", AccountType.Asset, "1.1", true),
        (AccountCodes.Inventory, "Inventario de mercaderías", AccountType.Asset, "1.1", true),
        ("2", "PASIVO", AccountType.Liability, null, false),
        ("2.1", "Pasivo corriente", AccountType.Liability, "2", false),
        (AccountCodes.Payables, "Proveedores", AccountType.Liability, "2.1", true),
        (AccountCodes.VatDebit, "IVA débito fiscal", AccountType.Liability, "2.1", true),
        ("2.1.03", "Sueldos por pagar", AccountType.Liability, "2.1", true),
        ("3", "PATRIMONIO", AccountType.Equity, null, false),
        ("3.1", "Capital", AccountType.Equity, "3", false),
        (AccountCodes.Capital, "Capital social", AccountType.Equity, "3.1", true),
        (AccountCodes.RetainedEarnings, "Resultados acumulados", AccountType.Equity, "3.1", true),
        ("4", "INGRESOS", AccountType.Revenue, null, false),
        ("4.1", "Ingresos operativos", AccountType.Revenue, "4", false),
        (AccountCodes.Sales, "Ventas", AccountType.Revenue, "4.1", true),
        (AccountCodes.InventorySurplus, "Sobrantes de inventario", AccountType.Revenue, "4.1", true),
        ("5", "COSTOS", AccountType.Expense, null, false),
        ("5.1", "Costo de ventas", AccountType.Expense, "5", false),
        (AccountCodes.CostOfSales, "Costo de ventas", AccountType.Expense, "5.1", true),
        (AccountCodes.InventoryShrinkage, "Mermas y ajustes de inventario", AccountType.Expense, "5.1", true),
        ("6", "GASTOS", AccountType.Expense, null, false),
        ("6.1", "Gastos de operación", AccountType.Expense, "6", false),
        ("6.1.01", "Sueldos y salarios", AccountType.Expense, "6.1", true),
        ("6.1.02", "Alquileres", AccountType.Expense, "6.1", true),
        ("6.1.03", "Servicios básicos", AccountType.Expense, "6.1", true),
        ("6.1.04", "Gastos administrativos", AccountType.Expense, "6.1", true),
    ];

    public static IReadOnlyList<Account> CreateDefaults(Guid tenantId)
    {
        var byCode = new Dictionary<string, Account>(StringComparer.Ordinal);
        foreach (var (code, name, type, parent, postable) in Defaults)
        {
            byCode[code] = new Account(tenantId, code, name, type, parent is null ? null : byCode[parent].Id, postable);
        }
        return [.. byCode.Values];
    }

    /// <summary>Saldo con el signo natural de la cuenta (activos y gastos: debe − haber; el resto: haber − debe).</summary>
    public static decimal NaturalBalance(AccountType type, decimal debit, decimal credit) =>
        type is AccountType.Asset or AccountType.Expense ? debit - credit : credit - debit;
}
