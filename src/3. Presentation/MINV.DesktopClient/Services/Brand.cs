namespace MINV.DesktopClient.Services;

/// <summary>
/// V4.2 · Textos de marca de la edición Tecnología (pantalla de carga, inicio de sesión, barra lateral, acerca de y ayuda).
/// Un solo lugar para cambiarlos; las vistas los usan con <c>{x:Static s:Brand.…}</c>.
/// </summary>
public static class Brand
{
    /// <summary>Nombre corto de la edición (barra lateral).</summary>
    public const string EditionName = "Edición Tecnología";

    /// <summary>Subtítulo completo de la edición (carga, inicio de sesión, acerca de y ayuda).</summary>
    public const string Edition = EditionName + " · PC, componentes y consolas";

    /// <summary>Titular del panel de marca del inicio de sesión.</summary>
    public const string Headline = "Tecnología y gaming, bajo control.";

    /// <summary>Texto de apoyo del panel de marca del inicio de sesión.</summary>
    public const string Pitch =
        "Componentes de PC, computadoras, consolas, periféricos y videojuegos: stock por sucursal, ventas con factura del SIN y compras en un solo lugar.";
}
