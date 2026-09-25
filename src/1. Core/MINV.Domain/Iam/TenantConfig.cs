using MINV.Domain.Common;

namespace MINV.Domain.Iam;

/// <summary>Parámetros de la empresa (una fila por tenant).</summary>
/// <remarks>Origen en la V2.1: 01_CONFIG: cfgMargenAlerta, cfgDiasSinRotacion, cfgFechaMin, cfgMoneda, cfgBodega.</remarks>
public sealed class TenantConfig : BaseEntity, IConcurrencyAware
{
    private TenantConfig()
    {
    }

    public TenantConfig(Guid tenantId, Guid defaultCurrencyId, Guid? defaultWarehouseId, decimal alertMargin, int daysWithoutRotation, DateOnly minBusinessDate, string timeZoneId)
        : base(tenantId)
    {
        DefaultCurrencyId = Guard.NotEmpty(defaultCurrencyId, nameof(defaultCurrencyId));
        DefaultWarehouseId = Guard.NotEmptyIfPresent(defaultWarehouseId, nameof(defaultWarehouseId));
        AlertMargin = Guard.Fraction(alertMargin, "El margen de alerta");
        Guard.That(daysWithoutRotation > 0, "guard.positive", "Los días sin rotación debe ser mayor que 0.");
        DaysWithoutRotation = daysWithoutRotation;
        MinBusinessDate = minBusinessDate;
        TimeZoneId = Guard.Text(timeZoneId, "La zona horaria", 64);
    }

    public Guid DefaultCurrencyId { get; private set; }

    public Guid? DefaultWarehouseId { get; private set; }

    public decimal AlertMargin { get; private set; }

    public int DaysWithoutRotation { get; private set; }

    public DateOnly MinBusinessDate { get; private set; }

    public string TimeZoneId { get; private set; } = string.Empty;

    /// <summary>Token de concurrencia optimista (xmin de PostgreSQL).</summary>
    public uint RowVersion { get; private set; }

    /// <summary>Actualiza los parámetros operativos (en la V2.1: 01_CONFIG).</summary>
    public void Update(decimal alertMargin, int daysWithoutRotation, DateOnly minBusinessDate, string timeZoneId)
    {
        AlertMargin = Guard.Fraction(alertMargin, "El margen de alerta");
        Guard.That(daysWithoutRotation > 0, "guard.positive", "Los días sin rotación debe ser mayor que 0.");
        DaysWithoutRotation = daysWithoutRotation;
        MinBusinessDate = minBusinessDate;
        TimeZoneId = Guard.Text(timeZoneId, "La zona horaria", 64);
    }

    public void SetDefaultWarehouse(Guid? warehouseId) => DefaultWarehouseId = Guard.NotEmptyIfPresent(warehouseId, nameof(warehouseId));
}
