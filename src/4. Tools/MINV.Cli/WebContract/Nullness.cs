using System.Collections.ObjectModel;
using System.Reflection;

namespace MINV.Cli.WebContract;

/// <summary>
/// V7 · Nulabilidad de una posición de un tipo y de sus elementos y argumentos genéricos (forma simple de
/// <see cref="NullabilityInfo"/>): <see cref="Nullable"/> = el JSON puede traer <c>null</c> ahí.
/// </summary>
internal sealed record Nullness(bool Nullable, Nullness? Element, IReadOnlyList<Nullness> Arguments)
{
    public static readonly Nullness NotNull = new(false, null, []);

    /// <summary>Lo que dice el compilador según <see cref="NullabilityInfoContext"/>; «desconocido» cuenta como anulable.</summary>
    public static Nullness From(NullabilityInfo info) =>
        new(info.ReadState != NullabilityState.NotNull, info.ElementType is { } element ? From(element) : null,
            info.GenericTypeArguments.Select(From).ToList());

    public Nullness ElementOrNotNull => Element ?? NotNull;

    public Nullness Argument(int index) => index < Arguments.Count ? Arguments[index] : NotNull;

    /// <summary>Forma legible y comparable: <c>?</c> anulable, <c>!</c> no; elemento en <c>[]</c>, argumentos en <c>&lt;&gt;</c>.</summary>
    public override string ToString() =>
        (Nullable ? "?" : "!") + (Element is null ? string.Empty : "[" + Element + "]") +
        (Arguments.Count == 0 ? string.Empty : "<" + string.Join(",", Arguments) + ">");
}

/// <summary>
/// V7 · Nulabilidad de un miembro de un genérico CERRADO (<c>Page&lt;ApiProduct&gt;</c>). <see cref="NullabilityInfoContext"/>
/// da «anulable» en toda posición declarada con un parámetro de tipo sin restricciones: no distingue <c>T</c> de <c>T?</c>
/// (mira la anotación del propio parámetro, que para un <c>T</c> libre es «puede ser nulo»). Aquí se leen las marcas que
/// deja el compilador en el miembro de la DEFINICIÓN (<c>NullableAttribute</c>, o <c>NullableContextAttribute</c> del
/// miembro y de los tipos que lo contienen), recorridas en preorden igual que lo hace .NET: <c>T</c> toma la nulabilidad
/// del argumento (el contrato exige argumentos no anulables) y <c>T?</c> es anulable si el argumento es por referencia.
/// </summary>
internal static class CompilerNullability
{
    private const string CompilerServices = "System.Runtime.CompilerServices";
    private const byte NotAnnotated = 1;

    public static Nullness Of(PropertyInfo definition, IReadOnlyList<Type> arguments) =>
        Decode(definition.PropertyType, definition.GetCustomAttributesData(), definition, arguments);

    public static Nullness Of(FieldInfo definition, IReadOnlyList<Type> arguments) =>
        Decode(definition.FieldType, definition.GetCustomAttributesData(), definition, arguments);

    public static Nullness Of(ParameterInfo definition, IReadOnlyList<Type> arguments) =>
        Decode(definition.ParameterType, definition.GetCustomAttributesData(), definition.Member, arguments);

    private static Nullness Decode(Type type, IList<CustomAttributeData> attributes, MemberInfo context, IReadOnlyList<Type> arguments)
    {
        var flags = FlagsOf(attributes, ContextOf(context));
        var index = 0;
        return Decode(type, flags, ref index, arguments);
    }

    /// <summary>Mismo recorrido que NullabilityInfoContext: una marca por tipo por referencia (y por parámetro de tipo),
    /// ninguna por un tipo por valor salvo si es genérico; luego el elemento de un arreglo y los argumentos genéricos.</summary>
    private static Nullness Decode(Type type, Flags flags, ref int index, IReadOnlyList<Type> arguments)
    {
        var underlying = type;
        bool nullable;
        Nullness? element = null;
        if (underlying.IsValueType)
        {
            var inner = Nullable.GetUnderlyingType(underlying);
            nullable = inner is not null;
            underlying = inner ?? underlying;
            if (underlying.IsGenericType)
            {
                index++;
            }
        }
        else
        {
            nullable = flags.At(index++) != NotAnnotated;
            if (underlying.IsGenericParameter && arguments[underlying.GenericParameterPosition].IsValueType)
            {
                // T? con un argumento por valor es el mismo valor (no Nullable<T>): nunca viaja null
                nullable = false;
            }
            if (underlying.IsArray)
            {
                element = Decode(underlying.GetElementType()!, flags, ref index, arguments);
            }
        }
        var list = new List<Nullness>();
        if (underlying.IsGenericType)
        {
            foreach (var argument in underlying.GetGenericArguments())
            {
                list.Add(Decode(argument, flags, ref index, arguments));
            }
        }
        return new Nullness(nullable, element, list);
    }

    private static Flags FlagsOf(IEnumerable<CustomAttributeData> attributes, byte context)
    {
        foreach (var attribute in attributes)
        {
            if (Is(attribute, "NullableAttribute"))
            {
                return attribute.ConstructorArguments[0].Value switch
                {
                    byte all => new Flags(all, null, context),
                    ReadOnlyCollection<CustomAttributeTypedArgument> each => new Flags(null, each.Select(a => (byte)a.Value!).ToArray(), context),
                    _ => new Flags(null, null, context),
                };
            }
        }
        return new Flags(null, null, context);
    }

    /// <summary>Anotación por defecto: la del miembro o la del primer tipo que lo contiene (0 = sin anotaciones).</summary>
    private static byte ContextOf(MemberInfo? member)
    {
        for (; member is not null; member = member.DeclaringType)
        {
            foreach (var attribute in member.GetCustomAttributesData())
            {
                if (Is(attribute, "NullableContextAttribute") && attribute.ConstructorArguments[0].Value is byte context)
                {
                    return context;
                }
            }
        }
        return 0;
    }

    private static bool Is(CustomAttributeData attribute, string name) =>
        attribute.AttributeType.Name == name && attribute.AttributeType.Namespace == CompilerServices && attribute.ConstructorArguments.Count == 1;

    /// <summary>Un byte para todo el tipo, o uno por posición (si faltan, vale el del contexto).</summary>
    private sealed record Flags(byte? All, byte[]? Each, byte Context)
    {
        public byte At(int index) => All ?? (Each is not null && index < Each.Length ? Each[index] : Context);
    }
}
