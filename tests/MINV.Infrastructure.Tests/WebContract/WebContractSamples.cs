using System.Text.Json.Serialization;

// V7 · Tipos de muestra para las pruebas del traductor del contrato de la web (minv contrato-web). Viven en espacios de
// nombres MINV.* como los del servidor; los de Ventas y Compras (y el de MINV.Otra) chocan a propósito por el nombre corto.

namespace MINV.Infrastructure.Tests.WebContract.Muestras
{
    public enum Color
    {
        Verde = 3,
        Azul = 1,
        Rojo = 2,
    }

    [Flags]
    public enum Acceso
    {
        Ninguno = 0,
        Leer = 1,
        Escribir = 2,
    }

    /// <summary>Todos los tipos simples y compuestos, tal como los ESCRIBE el servidor (una respuesta).</summary>
    public sealed record Ficha(string Texto, string? TextoOpcional, int Entero, long Largo, short Corto, decimal Precio, double Doble, bool Activo,
        Guid Id, DateTimeOffset Momento, DateTime Fecha, DateOnly Dia, TimeOnly Hora, TimeSpan Duracion, byte[] Imagen, int? Cantidad, char Letra,
        Color Color, Color? ColorOpcional, IReadOnlyList<string?> Etiquetas, (string Codigo, int Cantidad) Par, Dictionary<string, decimal> Montos,
        object Valor, IReadOnlyList<Ficha>? Hijas)
    {
        /// <summary>Calculada: también viaja en una respuesta.</summary>
        public string Resumen => Texto + " " + Entero;
    }

    /// <summary>Una petición: parámetros con valor por defecto y una propiedad calculada (como las de auditoría).</summary>
    public sealed record Pedido(string Codigo, IReadOnlyList<Linea> Lineas, int Cantidad = 1, string? Nota = null, Color Color = Color.Azul,
        DateOnly? Desde = null)
    {
        public object AuditDetails => new { Codigo, Cantidad };
    }

    /// <summary>Viaja en una petición y en una respuesta: se declara como respuesta (todo obligatorio).</summary>
    public sealed record Linea(string Sku, int Cantidad = 1);

    public sealed record Recibo(string Numero, IReadOnlyList<Linea> Lineas);

    public sealed record Pagina<T>(int Numero, IReadOnlyList<T> Items, T? Primero);

    public sealed record Producto(string Sku);

    public sealed record ConPaginas(Pagina<Producto> Productos, Pagina<int> Numeros, Pagina<Color> Colores);

    public sealed record ConPaginaAnulable(Pagina<Producto?> Productos);

    public sealed record ConAcceso(Acceso Acceso);

    public sealed record ConInterfaz(IDisposable Recurso);

    public sealed record ConClaveNumerica(Dictionary<int, string> Mapa);

    public sealed record ConConvertidor([property: JsonConverter(typeof(JsonStringEnumConverter))] Color Color);

    public sealed record ConNombres([property: JsonPropertyName("codigo-externo")] string Codigo, [property: JsonIgnore] string Secreto = "");

    /// <summary>Se llama como un nombre reservado del archivo generado.</summary>
    public sealed record Record(string Valor);
}

namespace MINV.Infrastructure.Tests.WebContract.Ventas
{
    public sealed record Fila(string Numero);

    public sealed record Consultar(int Pagina);
}

namespace MINV.Infrastructure.Tests.WebContract.Compras
{
    public sealed record Fila(int Numero);

    public sealed record Consultar(int Pagina);
}

namespace MINV.Otra.Ventas
{
    public sealed record Fila(bool Numero);
}
