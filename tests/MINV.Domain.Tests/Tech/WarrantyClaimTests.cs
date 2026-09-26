using MINV.Domain.Common;
using MINV.Domain.Service;

namespace MINV.Domain.Tests.Tech;

/// <summary>V4.2 · Casos de garantía (RMA, regla T-05): la tabla de transiciones completa (cada par estado → estado),
/// requisitos de cada paso y una fila de bitácora por cada cambio.</summary>
public sealed class WarrantyClaimTests
{
    /// <summary>La tabla esperada, escrita aparte del agregado para que un cambio en él no pase inadvertido.</summary>
    private static readonly Dictionary<WarrantyClaimStatus, WarrantyClaimStatus[]> Expected = new()
    {
        [WarrantyClaimStatus.Received] = [WarrantyClaimStatus.Diagnosing, WarrantyClaimStatus.Rejected],
        [WarrantyClaimStatus.Diagnosing] = [WarrantyClaimStatus.SentToSupplier, WarrantyClaimStatus.Repaired, WarrantyClaimStatus.Replaced,
            WarrantyClaimStatus.Rejected],
        [WarrantyClaimStatus.SentToSupplier] = [WarrantyClaimStatus.Repaired, WarrantyClaimStatus.Replaced, WarrantyClaimStatus.Rejected],
        [WarrantyClaimStatus.Repaired] = [WarrantyClaimStatus.Delivered],
        [WarrantyClaimStatus.Replaced] = [WarrantyClaimStatus.Delivered],
        [WarrantyClaimStatus.Rejected] = [WarrantyClaimStatus.Delivered],
        [WarrantyClaimStatus.Delivered] = [],
    };

    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Branch = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();
    private static readonly Guid Supplier = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 16, 0, 0, TimeSpan.Zero);

    private static WarrantyClaim Open(bool inWarranty = true) =>
        new(Tenant, Branch, "RMA-CM-000001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "No enciende después de 3 días", inWarranty, null,
            User, Now);

    /// <summary>Un caso en el estado pedido, al que se llegó por un camino válido.</summary>
    private static WarrantyClaim In(WarrantyClaimStatus status)
    {
        var claim = Open();
        switch (status)
        {
            case WarrantyClaimStatus.Received:
                break;
            case WarrantyClaimStatus.Rejected:
                claim.MoveTo(WarrantyClaimStatus.Rejected, "Daño físico: sellos rotos", null, User, Now);
                break;
            case WarrantyClaimStatus.Delivered:
                claim.MoveTo(WarrantyClaimStatus.Rejected, "Daño físico: sellos rotos", null, User, Now);
                claim.MoveTo(WarrantyClaimStatus.Delivered, null, null, User, Now);
                break;
            default:
                claim.MoveTo(WarrantyClaimStatus.Diagnosing, null, null, User, Now);
                if (status == WarrantyClaimStatus.SentToSupplier)
                {
                    claim.MoveTo(WarrantyClaimStatus.SentToSupplier, null, Supplier, User, Now);
                }
                else if (status == WarrantyClaimStatus.Repaired)
                {
                    claim.MoveTo(WarrantyClaimStatus.Repaired, "Se cambió el conector de energía", null, User, Now);
                }
                else if (status == WarrantyClaimStatus.Replaced)
                {
                    claim.SetReplacement(Guid.NewGuid(), User, Now);
                    claim.MoveTo(WarrantyClaimStatus.Replaced, "Se entregó una unidad nueva", null, User, Now);
                }
                break;
        }
        Assert.Equal(status, claim.Status);
        return claim;
    }

    public static IEnumerable<object[]> AllPairs() =>
        from origin in Enum.GetValues<WarrantyClaimStatus>()
        from target in Enum.GetValues<WarrantyClaimStatus>()
        select new object[] { origin, target };

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void La_tabla_de_transiciones_se_cumple_para_cada_par_de_estados(WarrantyClaimStatus from, WarrantyClaimStatus to)
    {
        var claim = In(from);
        var allowed = Expected[from].Contains(to);
        Assert.Equal(allowed, claim.CanMoveTo(to));
        Assert.Equal(Expected[from], WarrantyClaim.Transitions[from]);
        var before = claim.History.Count;
        if (!allowed)
        {
            Assert.Equal("rma.transition", Assert.Throws<DomainException>(() =>
                claim.MoveTo(to, "Resolución de prueba", Supplier, User, Now)).Code);
            Assert.Equal(from, claim.Status);
            Assert.Equal(before, claim.History.Count);
            return;
        }
        if (to == WarrantyClaimStatus.Replaced)
        {
            claim.SetReplacement(Guid.NewGuid(), User, Now);
            before++;
        }
        var row = claim.MoveTo(to, "Resolución de prueba", Supplier, User, Now);
        Assert.Equal(to, claim.Status);
        Assert.Equal(before + 1, claim.History.Count);
        Assert.Same(row, claim.History.Last());
        Assert.Equal((to, claim.Id, Branch, User), (row.Status, row.ClaimId, row.BranchId, row.UserId));
        Assert.Equal(to == WarrantyClaimStatus.Delivered ? WarrantyClaimAction.Closed : WarrantyClaimAction.StatusChanged, row.Action);
    }

    [Fact]
    public void Abrir_deja_la_primera_fila_y_marca_si_es_servicio_con_cargo()
    {
        var covered = Open();
        var paid = Open(inWarranty: false);
        Assert.Equal(WarrantyClaimStatus.Received, covered.Status);
        Assert.True(covered.IsOpen);
        Assert.Equal(WarrantyClaimAction.Opened, Assert.Single(covered.History).Action);
        Assert.False(paid.IsInWarranty);
        Assert.Contains("FUERA de garantía", Assert.Single(paid.History).Note, StringComparison.Ordinal);
        Assert.Throws<DomainException>(() => new WarrantyClaim(Tenant, Branch, "RMA-CM-000002", Guid.NewGuid(), Guid.NewGuid(), null, "mal",
            true, null, User, Now));   // la falla necesita al menos 5 caracteres
    }

    [Fact]
    public void Reparar_reemplazar_o_rechazar_exige_la_resolucion()
    {
        var claim = In(WarrantyClaimStatus.Diagnosing);
        Assert.Throws<DomainException>(() => claim.MoveTo(WarrantyClaimStatus.Repaired, "  ", null, User, Now));
        Assert.Throws<DomainException>(() => claim.MoveTo(WarrantyClaimStatus.Rejected, null, null, User, Now));
        Assert.Equal(WarrantyClaimStatus.Diagnosing, claim.Status);
    }

    [Fact]
    public void Enviar_al_proveedor_exige_el_proveedor()
    {
        var claim = In(WarrantyClaimStatus.Diagnosing);
        Assert.Equal("rma.supplier", Assert.Throws<DomainException>(() =>
            claim.MoveTo(WarrantyClaimStatus.SentToSupplier, null, null, User, Now)).Code);
        claim.MoveTo(WarrantyClaimStatus.SentToSupplier, null, Supplier, User, Now);
        Assert.Equal(Supplier, claim.SupplierId);
    }

    [Fact]
    public void Reemplazar_exige_registrar_antes_otra_unidad_una_sola_vez()
    {
        var claim = In(WarrantyClaimStatus.Diagnosing);
        Assert.Equal("rma.replacement", Assert.Throws<DomainException>(() =>
            claim.MoveTo(WarrantyClaimStatus.Replaced, "Unidad nueva", null, User, Now)).Code);
        Assert.Equal("rma.replacement_same", Assert.Throws<DomainException>(() =>
            claim.SetReplacement(claim.SerialNumberId, User, Now)).Code);
        var replacement = Guid.NewGuid();
        var row = claim.SetReplacement(replacement, User, Now);
        Assert.Equal(WarrantyClaimAction.ReplacementIssued, row.Action);
        Assert.Equal("rma.replacement_twice", Assert.Throws<DomainException>(() => claim.SetReplacement(Guid.NewGuid(), User, Now)).Code);
        claim.MoveTo(WarrantyClaimStatus.Replaced, "Unidad nueva", null, User, Now);
        Assert.Equal(replacement, claim.ReplacementSerialId);
        Assert.Equal("rma.replacement_state", Assert.Throws<DomainException>(() => In(WarrantyClaimStatus.Received)
            .SetReplacement(Guid.NewGuid(), User, Now)).Code);
    }

    [Fact]
    public void Entregar_cierra_el_caso_y_despues_no_admite_notas()
    {
        var claim = In(WarrantyClaimStatus.Repaired);
        claim.AddNote("Cliente avisado por teléfono", User, Now);
        claim.MoveTo(WarrantyClaimStatus.Delivered, null, null, User, Now.AddDays(1));
        Assert.False(claim.IsOpen);
        Assert.Equal(Now.AddDays(1), claim.ClosedAt);
        Assert.Equal("rma.closed", Assert.Throws<DomainException>(() => claim.AddNote("otra", User, Now)).Code);
        Assert.Equal(new[]
        {
            WarrantyClaimAction.Opened, WarrantyClaimAction.StatusChanged, WarrantyClaimAction.StatusChanged, WarrantyClaimAction.NoteAdded,
            WarrantyClaimAction.Closed,
        }, claim.History.Select(e => e.Action));
        Assert.Equal(new[]
        {
            WarrantyClaimStatus.Received, WarrantyClaimStatus.Diagnosing, WarrantyClaimStatus.Repaired, WarrantyClaimStatus.Repaired,
            WarrantyClaimStatus.Delivered,
        }, claim.History.Select(e => e.Status));
    }
}
