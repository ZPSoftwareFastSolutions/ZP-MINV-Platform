namespace MINV.Domain.Common;

/// <summary>Violación de una regla de negocio. <see cref="Code"/> es estable (se usa en pruebas, auditoría y en la
/// interfaz); <see cref="Exception.Message"/> es el texto para el usuario, en español.</summary>
public class DomainException : Exception
{
    public DomainException(string code, string message) : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

/// <summary>Poka-yoke transaccional: la operación dejaría el stock disponible en negativo.</summary>
public sealed class InsufficientStockException : DomainException
{
    public InsufficientStockException(decimal available, decimal requested)
        : base("stock.insufficient", $"Stock insuficiente: disponible {Quantities.Format(available)}, " +
                                     $"solicitado {Quantities.Format(requested)}.")
    {
        Available = available;
        Requested = requested;
    }

    public decimal Available { get; }

    public decimal Requested { get; }
}

/// <summary>Intento de modificar o borrar una fila de una tabla append-only (libro mayor).</summary>
public sealed class AppendOnlyViolationException : DomainException
{
    public AppendOnlyViolationException(string entity)
        : base("ledger.append_only", $"{entity} es inmutable: corrija con un movimiento compensatorio (AJUSTE).")
    {
    }
}
