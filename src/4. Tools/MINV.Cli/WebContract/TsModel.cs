namespace MINV.Cli.WebContract;

/// <summary>
/// V7 · Tipo de TypeScript de una posición del contrato de la web (una propiedad, un elemento o un argumento). Es un
/// modelo y no texto: <see cref="TsCatalog.Render"/> lo escribe y las pruebas comparan con él el JSON que produce
/// <c>RpcJson.Options</c> (regla P-07).
/// </summary>
internal abstract record TsType;

/// <summary><c>string</c>, <c>number</c>, <c>boolean</c> o <c>unknown</c> (cualquier JSON).</summary>
internal sealed record TsPrimitive(string Name) : TsType
{
    public static readonly TsPrimitive String = new("string");
    public static readonly TsPrimitive Number = new("number");
    public static readonly TsPrimitive Boolean = new("boolean");
    public static readonly TsPrimitive Unknown = new("unknown");
}

/// <summary><c>T | null</c>.</summary>
internal sealed record TsNullable(TsType Inner) : TsType;

/// <summary><c>T[]</c>: listas, colecciones y arreglos (salvo <c>byte[]</c>, que viaja como texto en base64).</summary>
internal sealed record TsArray(TsType Element) : TsType;

/// <summary><c>Record&lt;string, T&gt;</c>: diccionario con clave de texto.</summary>
internal sealed record TsDictionary(TsType Value) : TsType;

/// <summary>Tupla (<c>IncludeFields</c>): <c>{ item1: …; item2: … }</c>.</summary>
internal sealed record TsTuple(IReadOnlyList<TsProperty> Items) : TsType;

/// <summary>Un tipo declarado en el contrato (interfaz o unión de una enumeración), por su tipo de .NET.</summary>
internal sealed record TsReference(Type Target) : TsType;

/// <summary>Propiedad: nombre en el JSON, tipo, si puede faltar (solo en una petición) y el tipo de .NET que la produce.</summary>
internal sealed record TsProperty(string Name, TsType Type, bool Optional, Type ClrType);

/// <summary>Lado del contrato: lo que la web ENVÍA (petición) o lo que RECIBE (respuesta).</summary>
internal enum TsSide
{
    /// <summary>La web lo envía y el servidor lo LEE: solo cuentan el constructor y lo que tiene «set»; un parámetro
    /// con valor por defecto es opcional.</summary>
    Input,

    /// <summary>El servidor lo ESCRIBE: todas las propiedades públicas, también las calculadas; ninguna falta nunca.</summary>
    Output,
}

/// <summary>Un tipo con nombre del contrato.</summary>
internal abstract record TsDeclaration(Type Source, string Name);

/// <summary>Interfaz de un record o clase (sin propiedades: <c>Record&lt;string, never&gt;</c>).</summary>
internal sealed record TsInterface(Type Source, string Name, TsSide Side, IReadOnlyList<TsProperty> Properties) : TsDeclaration(Source, Name);

/// <summary>Enumeración: unión de los textos con que viaja cada valor.</summary>
internal sealed record TsEnum(Type Source, string Name, IReadOnlyList<string> Members) : TsDeclaration(Source, Name);
