using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MINV.Cli.WebContract;

/// <summary>
/// V7 · Traductor de tipos de .NET a TypeScript con la serialización de <c>RpcJson.Options</c> (regla P-07):
/// <list type="bullet">
/// <item>Nombres de propiedad con la política de las opciones (camelCase) o <c>[JsonPropertyName]</c>.</item>
/// <item>Enumeraciones como texto → unión de los textos que produce el propio serializador (no se adivinan).</item>
/// <item><c>IncludeFields</c> → tuplas como <c>{ item1, item2 }</c>.</item>
/// <item>Guid, fechas, horas, duraciones, char y <c>byte[]</c> (base64) → <c>string</c>; números → <c>number</c>;
/// bool → <c>boolean</c>; <c>object</c> y <c>JsonElement</c> → <c>unknown</c>; listas → <c>T[]</c>; diccionarios con
/// clave de texto → <c>Record&lt;string, T&gt;</c>; <c>T?</c> → <c>T | null</c>.</item>
/// <item>Respuesta (el servidor escribe): todas las propiedades públicas, también las calculadas, y ninguna falta.
/// Petición (el servidor lee): los parámetros del constructor que usa System.Text.Json (opcionales si tienen valor por
/// defecto) y las propiedades con «set»; una propiedad calculada de solo lectura no viaja. Un tipo que está en los dos
/// lados se declara como respuesta.</item>
/// </list>
/// Lo que no sabe traducir lo rechaza con un mensaje claro (<see cref="NotSupportedException"/>) en vez de adivinar.
/// Uso: <see cref="AddRoot"/> por cada petición y respuesta, y luego <see cref="Build"/>.
/// </summary>
internal sealed class TypeScriptTypes
{
    /// <summary>Nombres que usa el propio archivo generado: ningún tipo puede llamarse así.</summary>
    public static readonly IReadOnlySet<string> Reserved = new HashSet<string>(StringComparer.Ordinal) { "Record", "RpcOperations", "RpcOperationMeta" };

    private static readonly Dictionary<Type, TsPrimitive> Simple = new()
    {
        [typeof(string)] = TsPrimitive.String,
        [typeof(char)] = TsPrimitive.String,
        [typeof(Guid)] = TsPrimitive.String,
        [typeof(DateTime)] = TsPrimitive.String,
        [typeof(DateTimeOffset)] = TsPrimitive.String,
        [typeof(DateOnly)] = TsPrimitive.String,
        [typeof(TimeOnly)] = TsPrimitive.String,
        [typeof(TimeSpan)] = TsPrimitive.String,
        [typeof(byte[])] = TsPrimitive.String,
        [typeof(bool)] = TsPrimitive.Boolean,
        [typeof(byte)] = TsPrimitive.Number,
        [typeof(sbyte)] = TsPrimitive.Number,
        [typeof(short)] = TsPrimitive.Number,
        [typeof(ushort)] = TsPrimitive.Number,
        [typeof(int)] = TsPrimitive.Number,
        [typeof(uint)] = TsPrimitive.Number,
        [typeof(long)] = TsPrimitive.Number,
        [typeof(ulong)] = TsPrimitive.Number,
        [typeof(float)] = TsPrimitive.Number,
        [typeof(double)] = TsPrimitive.Number,
        [typeof(decimal)] = TsPrimitive.Number,
        [typeof(object)] = TsPrimitive.Unknown,
        [typeof(JsonElement)] = TsPrimitive.Unknown,
    };

    private static readonly HashSet<Type> Lists =
    [
        typeof(IEnumerable<>), typeof(IReadOnlyList<>), typeof(IReadOnlyCollection<>), typeof(IList<>), typeof(ICollection<>),
        typeof(List<>), typeof(HashSet<>), typeof(ISet<>), typeof(IReadOnlySet<>),
    ];

    private static readonly HashSet<Type> Dictionaries = [typeof(IDictionary<,>), typeof(IReadOnlyDictionary<,>), typeof(Dictionary<,>)];

    private static readonly HashSet<Type> Tuples =
    [
        typeof(ValueTuple<>), typeof(ValueTuple<,>), typeof(ValueTuple<,,>), typeof(ValueTuple<,,,>), typeof(ValueTuple<,,,,>),
        typeof(ValueTuple<,,,,,>), typeof(ValueTuple<,,,,,,>),
    ];

    private readonly JsonSerializerOptions options;
    private readonly IReadOnlySet<Type> pinned;
    private readonly NullabilityInfoContext nullability = new();
    private readonly Dictionary<Type, TsSide> models = new();
    private readonly HashSet<Type> enums = new();

    /// <param name="options">Las opciones con que viaja el JSON (<c>RpcJson.Options</c>).</param>
    /// <param name="pinned">Tipos que conservan su nombre si chocan con otro (el sobre del RPC: la web los pide por nombre).</param>
    public TypeScriptTypes(JsonSerializerOptions options, IEnumerable<Type>? pinned = null)
    {
        this.options = options;
        this.pinned = new HashSet<Type>(pinned ?? []);
    }

    /// <summary>Registra un tipo raíz del contrato (una petición, una respuesta o el sobre del RPC) y todo lo que alcanza.</summary>
    public void AddRoot(Type type, Nullness nullness, TsSide side, string where) => Visit(Reference(type, nullness, where), side);

    /// <summary>Tipo de TypeScript de una posición: <paramref name="where"/> solo sirve para el mensaje de error.</summary>
    public TsType Reference(Type type, Nullness nullness, string where)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return Nullify(Reference(underlying, nullness with { Nullable = false }, where));
        }
        var core = ReferenceCore(type, nullness, where);
        return nullness.Nullable && !type.IsValueType ? Nullify(core) : core;
    }

    /// <summary>Declaraciones con su nombre final (ordenadas por nombre) y el nombre de cada tipo de .NET.</summary>
    public TsCatalog Build()
    {
        var names = ResolveNames([.. models.Keys, .. enums]);
        var declarations = new List<TsDeclaration>();
        foreach (var (type, side) in models)
        {
            declarations.Add(new TsInterface(type, names[type], side, Members(type, side)));
        }
        foreach (var type in enums)
        {
            declarations.Add(new TsEnum(type, names[type], EnumMembers(type)));
        }
        return new TsCatalog(declarations.OrderBy(d => d.Name, StringComparer.Ordinal).ToList(), names);
    }

    /// <summary>Nombre legible de un tipo de .NET para los comentarios y los mensajes (<c>MINV.X.Page&lt;MINV.X.Item&gt;</c>).</summary>
    public static string Display(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return Display(underlying) + "?";
        }
        if (type.IsArray)
        {
            return Display(type.GetElementType()!) + "[]";
        }
        if (type.IsGenericType && !type.IsGenericTypeDefinition)
        {
            var definition = type.GetGenericTypeDefinition().FullName!;
            return definition[..definition.IndexOf('`')] + "<" + string.Join(", ", type.GetGenericArguments().Select(Display)) + ">";
        }
        return type.FullName ?? type.Name;
    }

    private static TsType Nullify(TsType type) => type is TsNullable ? type : new TsNullable(type);

    private TsType ReferenceCore(Type type, Nullness nullness, string where)
    {
        if (Simple.TryGetValue(type, out var simple))
        {
            return simple;
        }
        if (type.IsEnum)
        {
            return new TsReference(type);
        }
        if (type.IsArray && type.GetArrayRank() == 1)
        {
            return new TsArray(Reference(type.GetElementType()!, nullness.ElementOrNotNull, where));
        }
        if (type.IsConstructedGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            var arguments = type.GetGenericArguments();
            if (Lists.Contains(definition))
            {
                return new TsArray(Reference(arguments[0], nullness.Argument(0), where));
            }
            if (Dictionaries.Contains(definition))
            {
                if (arguments[0] != typeof(string))
                {
                    throw Unsupported(type, where, "solo se traducen diccionarios con clave de texto");
                }
                return new TsDictionary(Reference(arguments[1], nullness.Argument(1), where));
            }
            if (Tuples.Contains(definition))
            {
                return new TsTuple(arguments.Select((argument, i) =>
                    new TsProperty(Name("Item" + (i + 1)), Reference(argument, nullness.Argument(i), where), false, argument)).ToList());
            }
            if (IsMinv(type) && IsModelShape(type))
            {
                CheckGenericArguments(type, nullness, where);
                return new TsReference(type);
            }
            throw Unsupported(type, where);
        }
        if (IsMinv(type) && IsModelShape(type))
        {
            return new TsReference(type);
        }
        throw Unsupported(type, where);
    }

    /// <summary>Un genérico cerrado se declara con nombre propio (<c>PageOfApiProduct</c>): sus argumentos deben ser tipos
    /// simples o modelos no genéricos, y no anulables (el nombre no distingue <c>Page&lt;X?&gt;</c> de <c>Page&lt;X&gt;</c>).</summary>
    private static void CheckGenericArguments(Type type, Nullness nullness, string where)
    {
        var arguments = type.GetGenericArguments();
        for (var i = 0; i < arguments.Length; i++)
        {
            var argument = arguments[i];
            var plain = Nullable.GetUnderlyingType(argument) ?? argument;
            if (!Simple.ContainsKey(plain) && !plain.IsEnum && !(IsMinv(plain) && !plain.IsGenericType && IsModelShape(plain)))
            {
                throw Unsupported(type, where, $"el argumento genérico {Display(argument)} no es un tipo simple ni un modelo no genérico");
            }
            if (!argument.IsValueType && nullness.Argument(i).Nullable)
            {
                throw Unsupported(type, where, $"el argumento genérico {Display(argument)} es anulable");
            }
        }
    }

    private static bool IsMinv(Type type) => type.Namespace is "MINV" || type.Namespace?.StartsWith("MINV.", StringComparison.Ordinal) == true;

    private static bool IsModelShape(Type type) => !type.IsInterface && !type.IsAbstract && !type.IsPointer && !type.IsByRef && !type.IsGenericTypeDefinition;

    private static NotSupportedException Unsupported(Type type, string where, string? reason = null) =>
        new($"El contrato de la web no sabe traducir {Display(type)} ({where}){(reason is null ? string.Empty : ": " + reason)}. " +
            "Agregue la traducción en src/4. Tools/MINV.Cli/WebContract o use un tipo que ya sepa traducir.");

    private void Visit(TsType type, TsSide side)
    {
        switch (type)
        {
            case TsNullable nullable:
                Visit(nullable.Inner, side);
                break;
            case TsArray array:
                Visit(array.Element, side);
                break;
            case TsDictionary dictionary:
                Visit(dictionary.Value, side);
                break;
            case TsTuple tuple:
                foreach (var item in tuple.Items)
                {
                    Visit(item.Type, side);
                }
                break;
            case TsReference reference:
                Mark(reference.Target, side);
                break;
        }
    }

    /// <summary>Un tipo que ya es respuesta no cambia; uno que era solo petición y aparece como respuesta se vuelve a recorrer
    /// con las reglas de la respuesta (sus propiedades calculadas también viajan).</summary>
    private void Mark(Type type, TsSide side)
    {
        if (type.IsEnum)
        {
            enums.Add(type);
            return;
        }
        if (models.TryGetValue(type, out var current) && (current == TsSide.Output || current == side))
        {
            return;
        }
        models[type] = side;
        foreach (var member in Members(type, side))
        {
            Visit(member.Type, side);
        }
    }

    private IReadOnlyList<TsProperty> Members(Type type, TsSide side)
    {
        CheckAttributes(type);
        var owner = Display(type);
        var result = new List<TsProperty>();
        const BindingFlags Instance = BindingFlags.Public | BindingFlags.Instance;
        if (side == TsSide.Output)
        {
            foreach (var property in type.GetProperties(Instance))
            {
                if (property.GetIndexParameters().Length > 0 || property.GetMethod is not { IsPublic: true } || Ignored(property))
                {
                    continue;
                }
                result.Add(new TsProperty(Name(property), Reference(property.PropertyType, NullnessOf(property), $"{owner}.{property.Name}"), false,
                    property.PropertyType));
            }
        }
        else
        {
            var bound = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var parameter in DeserializationConstructor(type, owner)?.GetParameters() ?? [])
            {
                // System.Text.Json une cada parámetro con la propiedad del mismo nombre (sin distinguir mayúsculas)
                var property = type.GetProperties(Instance).SingleOrDefault(p => string.Equals(p.Name, parameter.Name, StringComparison.OrdinalIgnoreCase));
                if (property is null || Ignored(property))
                {
                    throw new NotSupportedException($"El parámetro «{parameter.Name}» del constructor de {owner} no corresponde a una propiedad: " +
                                                    "System.Text.Json no puede leer esa petición.");
                }
                bound.Add(property.Name);
                result.Add(new TsProperty(Name(property), Reference(parameter.ParameterType, NullnessOf(parameter), $"{owner}({parameter.Name})"),
                    parameter.HasDefaultValue, parameter.ParameterType));
            }
            foreach (var property in type.GetProperties(Instance))
            {
                // Una propiedad calculada (solo lectura y fuera del constructor) no se lee al deserializar: no viaja en la petición
                if (bound.Contains(property.Name) || property.GetIndexParameters().Length > 0 || property.SetMethod is not { IsPublic: true } ||
                    Ignored(property))
                {
                    continue;
                }
                result.Add(new TsProperty(Name(property), Reference(property.PropertyType, NullnessOf(property), $"{owner}.{property.Name}"),
                    !property.IsDefined(typeof(RequiredMemberAttribute), false), property.PropertyType));
            }
        }
        foreach (var field in type.GetFields(Instance))
        {
            if (Ignored(field) || (side == TsSide.Input && field.IsInitOnly))
            {
                continue;
            }
            result.Add(new TsProperty(Name(field), Reference(field.FieldType, NullnessOf(field), $"{owner}.{field.Name}"), false, field.FieldType));
        }
        if (result.GroupBy(p => p.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1) is { } repeated)
        {
            throw new NotSupportedException($"{owner} tiene dos propiedades que viajan con el mismo nombre «{repeated.Key}».");
        }
        return result.OrderBy(p => p.Name, StringComparer.Ordinal).ToList();
    }

    /// <summary>El constructor que usa System.Text.Json para LEER el tipo (null = sin parámetros: solo propiedades con «set»).</summary>
    private static ConstructorInfo? DeserializationConstructor(Type type, string owner)
    {
        var all = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (all.Where(c => c.IsDefined(typeof(JsonConstructorAttribute), false)).ToList() is [var marked])
        {
            return marked;
        }
        if (type.IsValueType)
        {
            return null;
        }
        var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        if (constructors.Any(c => c.GetParameters().Length == 0))
        {
            return null;
        }
        return constructors.Length == 1
            ? constructors[0]
            : throw new NotSupportedException($"{owner} no tiene un constructor que System.Text.Json pueda usar para leer la petición.");
    }

    /// <summary>Solo se aceptan los atributos de System.Text.Json que el generador sabe traducir: el nombre, el orden y
    /// <c>[JsonIgnore]</c> incondicional. Cualquier otro (convertidores, polimorfismo, <c>[JsonInclude]</c>…) cambia el JSON.</summary>
    private static void CheckAttributes(Type type)
    {
        const string SerializationNamespace = "System.Text.Json.Serialization";
        var owner = Display(type);
        if (type.GetCustomAttributes(true).FirstOrDefault(a => a.GetType().Namespace == SerializationNamespace) is { } typeAttribute)
        {
            throw new NotSupportedException($"{owner} usa [{typeAttribute.GetType().Name}], que el generador del contrato de la web no sabe traducir.");
        }
        foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (member is ConstructorInfo)
            {
                continue;
            }
            foreach (var attribute in member.GetCustomAttributes(true))
            {
                if (attribute.GetType().Namespace != SerializationNamespace ||
                    attribute is JsonPropertyNameAttribute or JsonPropertyOrderAttribute or JsonIgnoreAttribute { Condition: JsonIgnoreCondition.Always })
                {
                    continue;
                }
                throw new NotSupportedException($"{owner}.{member.Name} usa [{attribute.GetType().Name}], que el generador del contrato de la web no sabe traducir.");
            }
        }
    }

    private static bool Ignored(MemberInfo member) => member.GetCustomAttribute<JsonIgnoreAttribute>() is { Condition: JsonIgnoreCondition.Always };

    private string Name(MemberInfo member) => member.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? Name(member.Name);

    private string Name(string clrName) => options.PropertyNamingPolicy?.ConvertName(clrName) ?? clrName;

    private Nullness NullnessOf(PropertyInfo property) =>
        property.DeclaringType is { IsConstructedGenericType: true } owner
            ? CompilerNullability.Of(owner.GetGenericTypeDefinition().GetProperty(property.Name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!,
                owner.GetGenericArguments())
            : Nullness.From(nullability.Create(property));

    private Nullness NullnessOf(FieldInfo field) =>
        field.DeclaringType is { IsConstructedGenericType: true } owner
            ? CompilerNullability.Of(owner.GetGenericTypeDefinition().GetField(field.Name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!,
                owner.GetGenericArguments())
            : Nullness.From(nullability.Create(field));

    private Nullness NullnessOf(ParameterInfo parameter)
    {
        if (parameter.Member.DeclaringType is not { IsConstructedGenericType: true } owner)
        {
            return Nullness.From(nullability.Create(parameter));
        }
        var definition = owner.GetGenericTypeDefinition()
            .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(c => c.MetadataToken == parameter.Member.MetadataToken);
        return CompilerNullability.Of(definition.GetParameters()[parameter.Position], owner.GetGenericArguments());
    }

    /// <summary>Los textos con que viaja cada valor, tal como los escribe el serializador (orden alfabético).</summary>
    private IReadOnlyList<string> EnumMembers(Type type)
    {
        if (type.IsDefined(typeof(FlagsAttribute), false))
        {
            throw new NotSupportedException($"La enumeración {Display(type)} es [Flags]: una combinación viaja como «A, B» y el contrato de la web no la sabe traducir.");
        }
        var members = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var value in Enum.GetValues(type))
        {
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(value, type, options));
            if (json.RootElement.ValueKind != JsonValueKind.String)
            {
                throw new NotSupportedException($"La enumeración {Display(type)} no viaja como texto con estas opciones de JSON.");
            }
            members.Add(json.RootElement.GetString()!);
        }
        return [.. members];
    }

    /// <summary>
    /// Nombre corto de cada tipo; un genérico cerrado, <c>Nombre</c> + <c>Of</c> + sus argumentos (<c>PageOfApiProduct</c>).
    /// Si dos tipos chocan (o uno choca con un nombre reservado), a cada uno se le antepone el último segmento de su espacio
    /// de nombres, y otro más mientras sigan chocando. Los tipos fijos (el sobre del RPC) conservan su nombre.
    /// </summary>
    private IReadOnlyDictionary<Type, string> ResolveNames(IReadOnlyCollection<Type> types)
    {
        var level = types.ToDictionary(t => t, _ => 0);
        while (true)
        {
            var names = new Dictionary<Type, string>();
            foreach (var type in types.Where(t => !t.IsGenericType))
            {
                names[type] = Prefix(type, level[type]) + type.Name;
            }
            foreach (var type in types.Where(t => t.IsGenericType))
            {
                names[type] = Prefix(type, level[type]) + GenericName(type, names);
            }
            var bump = new List<Type>();
            foreach (var group in names.GroupBy(n => n.Value, StringComparer.Ordinal))
            {
                var clashing = group.Select(n => n.Key).ToList();
                if (clashing.Count == 1 && !Reserved.Contains(group.Key))
                {
                    continue;
                }
                var fixedOnes = clashing.Where(pinned.Contains).ToList();
                if (fixedOnes.Count > 1 || (fixedOnes.Count == 1 && Reserved.Contains(group.Key)))
                {
                    throw Clash(group.Key, clashing);
                }
                bump.AddRange(fixedOnes.Count == 1 ? clashing.Where(t => !pinned.Contains(t)) : clashing);
            }
            if (bump.Count == 0)
            {
                return names;
            }
            foreach (var type in bump)
            {
                if (level[type] >= Segments(type).Length)
                {
                    throw Clash(names[type], names.Where(n => n.Value == names[type]).Select(n => n.Key).ToList());
                }
                level[type]++;
            }
        }
    }

    private static InvalidOperationException Clash(string name, IEnumerable<Type> types) =>
        new($"El contrato de la web no puede dar un nombre único a «{name}»: lo reclaman " +
            $"{string.Join(" y ", types.Select(Display).Order(StringComparer.Ordinal))}" +
            (Reserved.Contains(name) ? " (y es un nombre reservado del archivo generado)" : string.Empty) + ". Cambie el nombre de uno de los tipos.");

    private static string[] Segments(Type type) => type.Namespace?.Split('.') ?? [];

    private static string Prefix(Type type, int level) => level == 0 ? string.Empty : string.Concat(Segments(type)[^level..]);

    private static string GenericName(Type type, IReadOnlyDictionary<Type, string> names) =>
        type.Name[..type.Name.IndexOf('`')] + "Of" + string.Join("And", type.GetGenericArguments().Select(a => ArgumentName(a, names)));

    private static string ArgumentName(Type argument, IReadOnlyDictionary<Type, string> names) =>
        Nullable.GetUnderlyingType(argument) is { } underlying ? "Nullable" + ArgumentName(underlying, names)
        : argument == typeof(byte[]) ? "Bytes"
        : names.TryGetValue(argument, out var name) ? name
        : argument.Name;
}
