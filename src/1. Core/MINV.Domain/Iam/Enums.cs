namespace MINV.Domain.Iam;

/// <summary>Tipo de equipo autorizado. Se guarda como texto.</summary>
public enum HardwareTokenKind
{
    Workstation,
    PosTerminal,
    Scanner,
    Printer,
}

/// <summary>Resultado de una operación auditada (✔ correcto, ✖ bloqueado o fallido). Se guarda como texto.</summary>
public enum AuditOutcome
{
    Succeeded,
    Rejected,
    Failed,
}
