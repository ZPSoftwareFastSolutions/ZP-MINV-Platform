namespace MINV.Application.Billing;

/// <summary>
/// V4.1 · Monto literal de la representación gráfica: «Son: Ciento veinte 50/100 Bolivianos». Implementación: equipo de
/// representación gráfica (agente C).
/// </summary>
public static class AmountInWords
{
    /// <summary>Texto en español del monto con centavos en fracción /100 y la moneda (por defecto «Bolivianos»).</summary>
    public static string Bolivianos(decimal amount, string currency = "Bolivianos") =>
        throw new NotImplementedException("AmountInWords.Bolivianos: pendiente de implementar.");
}
