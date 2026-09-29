using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using MINV.Application.Remote;
using MINV.Cli.WebContract;

namespace MINV.Infrastructure.Tests.WebContract;

/// <summary>
/// V7 · Utilidades de las pruebas del contrato de la web: comparan lo que dice el TypeScript generado con lo que de verdad
/// escribe y lee <see cref="RpcJson.Options"/> («no adivinar», regla P-07).
/// </summary>
internal static class ContractJson
{
    public const string Text = "texto";
    public static readonly Guid SampleGuid = new("5d0c1f6e-0a51-4d0e-9d11-000000000001");

    /// <summary>¿El JSON cumple el tipo de TypeScript? Devuelve las diferencias (vacío = cumple): tipos primitivos, null solo
    /// donde se admite, valores de enumeración conocidos y EXACTAMENTE las propiedades declaradas (ni falta ni sobra).</summary>
    public static List<string> Check(JsonElement json, TsType type, TsCatalog catalog, string path)
    {
        var errors = new List<string>();
        Check(json, type, catalog, path, errors);
        return errors;
    }

    private static void Check(JsonElement json, TsType type, TsCatalog catalog, string path, List<string> errors)
    {
        if (type is TsNullable nullable)
        {
            if (json.ValueKind != JsonValueKind.Null)
            {
                Check(json, nullable.Inner, catalog, path, errors);
            }
            return;
        }
        if (type is TsPrimitive { Name: "unknown" })
        {
            return;
        }
        if (json.ValueKind == JsonValueKind.Null)
        {
            errors.Add($"{path}: llegó null y el tipo {catalog.Render(type)} no lo admite");
            return;
        }
        switch (type)
        {
            case TsPrimitive { Name: "string" }:
                Expect(json, path, errors, JsonValueKind.String);
                break;
            case TsPrimitive { Name: "number" }:
                Expect(json, path, errors, JsonValueKind.Number);
                break;
            case TsPrimitive { Name: "boolean" }:
                Expect(json, path, errors, JsonValueKind.True, JsonValueKind.False);
                break;
            case TsArray array when Expect(json, path, errors, JsonValueKind.Array):
                var index = 0;
                foreach (var item in json.EnumerateArray())
                {
                    Check(item, array.Element, catalog, $"{path}[{index++}]", errors);
                }
                break;
            case TsDictionary dictionary when Expect(json, path, errors, JsonValueKind.Object):
                foreach (var entry in json.EnumerateObject())
                {
                    Check(entry.Value, dictionary.Value, catalog, $"{path}.{entry.Name}", errors);
                }
                break;
            case TsTuple tuple:
                CheckObject(json, tuple.Items, catalog, path, errors);
                break;
            case TsReference reference:
                switch (catalog.Declaration(reference.Target))
                {
                    case TsEnum enumeration when Expect(json, path, errors, JsonValueKind.String):
                        if (!enumeration.Members.Contains(json.GetString()))
                        {
                            errors.Add($"{path}: «{json.GetString()}» no está en {enumeration.Name} ({string.Join(" | ", enumeration.Members)})");
                        }
                        break;
                    case TsInterface model:
                        CheckObject(json, model.Properties, catalog, path, errors);
                        break;
                }
                break;
        }
    }

    private static void CheckObject(JsonElement json, IReadOnlyList<TsProperty> properties, TsCatalog catalog, string path, List<string> errors)
    {
        if (!Expect(json, path, errors, JsonValueKind.Object))
        {
            return;
        }
        var names = json.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var property in properties)
        {
            if (json.TryGetProperty(property.Name, out var value))
            {
                Check(value, property.Type, catalog, $"{path}.{property.Name}", errors);
            }
            else if (!property.Optional)
            {
                errors.Add($"{path}.{property.Name}: el tipo la declara obligatoria y el JSON no la trae");
            }
        }
        foreach (var extra in names.Except(properties.Select(p => p.Name)))
        {
            errors.Add($"{path}.{extra}: el JSON la trae y el tipo no la declara");
        }
    }

    private static bool Expect(JsonElement json, string path, List<string> errors, params JsonValueKind[] kinds)
    {
        if (kinds.Contains(json.ValueKind))
        {
            return true;
        }
        errors.Add($"{path}: se esperaba {string.Join(" o ", kinds)} y llegó {json.ValueKind} ({json.GetRawText()})");
        return false;
    }

    /// <summary>
    /// JSON armado SOLO con lo que dice el tipo de TypeScript (lo que enviaría la web): <paramref name="full"/> con todas las
    /// propiedades y sin nulos; si no, solo las obligatorias y null donde se admite. Los textos llevan un valor válido para
    /// su tipo de .NET (fecha, Guid, base64…).
    /// </summary>
    public static JsonNode? Build(TsType type, Type clr, TsCatalog catalog, bool full, int depth = 0)
    {
        if (type is TsNullable nullable)
        {
            return full && depth < 4 ? Build(nullable.Inner, Nullable.GetUnderlyingType(clr) ?? clr, catalog, full, depth) : null;
        }
        switch (type)
        {
            case TsPrimitive { Name: "string" }:
                return JsonValue.Create(TextFor(clr));
            case TsPrimitive { Name: "number" }:
                return clr == typeof(decimal) || clr == typeof(double) || clr == typeof(float) ? JsonValue.Create(1.5m) : JsonValue.Create(1);
            case TsPrimitive { Name: "boolean" }:
                return JsonValue.Create(true);
            case TsPrimitive:
                return new JsonObject();
            case TsArray array:
                return depth < 4 ? new JsonArray(Build(array.Element, ElementOf(clr), catalog, full, depth + 1)) : new JsonArray();
            case TsDictionary dictionary:
                return new JsonObject { ["clave"] = Build(dictionary.Value, clr.GetGenericArguments()[1], catalog, full, depth + 1) };
            case TsTuple tuple:
                var items = new JsonObject();
                foreach (var item in tuple.Items)
                {
                    items[item.Name] = Build(item.Type, item.ClrType, catalog, full, depth + 1);
                }
                return items;
            case TsReference reference:
                switch (catalog.Declaration(reference.Target))
                {
                    case TsEnum enumeration:
                        return JsonValue.Create(enumeration.Members[0]);
                    case TsInterface model:
                        var result = new JsonObject();
                        foreach (var property in model.Properties.Where(p => full || !p.Optional))
                        {
                            result[property.Name] = Build(property.Type, property.ClrType, catalog, full, depth + 1);
                        }
                        return result;
                }
                break;
        }
        throw new InvalidOperationException($"No sé armar JSON para {type}.");
    }

    /// <summary>¿Todo lo que se envió volvió igual? (el valor leído puede traer además propiedades calculadas).</summary>
    public static bool IsSubset(JsonNode? sent, JsonNode? actual) => sent switch
    {
        null => actual is null,
        JsonObject objectSent => actual is JsonObject objectActual &&
                                 objectSent.All(p => objectActual.ContainsKey(p.Key) && IsSubset(p.Value, objectActual[p.Key])),
        JsonArray arraySent => actual is JsonArray arrayActual && arraySent.Count == arrayActual.Count &&
                               arraySent.Select((item, i) => IsSubset(item, arrayActual[i])).All(ok => ok),
        _ => JsonNode.DeepEquals(sent, actual),
    };

    private static Type ElementOf(Type clr) => clr.IsArray ? clr.GetElementType()! : clr.GetGenericArguments()[0];

    private static string TextFor(Type clr) => clr switch
    {
        _ when clr == typeof(Guid) => SampleGuid.ToString(),
        _ when clr == typeof(DateTime) => "2026-09-29T10:30:00",
        _ when clr == typeof(DateTimeOffset) => "2026-09-29T10:30:00-04:00",
        _ when clr == typeof(DateOnly) => "2026-09-29",
        _ when clr == typeof(TimeOnly) => "10:30:00",
        _ when clr == typeof(TimeSpan) => "01:30:00",
        _ when clr == typeof(byte[]) => Convert.ToBase64String([1, 2, 3]),
        _ when clr == typeof(char) => "x",
        _ => Text,
    };
}

/// <summary>
/// V7 · Valores de muestra de cualquier tipo del contrato para serializarlos con <see cref="RpcJson.Options"/>: «completo»
/// (ningún nulo, una fila en cada lista) o «mínimo» (null en cada posición anulable). La nulabilidad sale de lo que declara
/// el código (NullabilityInfoContext, o las marcas del compilador en un genérico cerrado), igual que en el generador.
/// </summary>
internal sealed class ContractSamples(bool full)
{
    private const int MaxDepth = 4;
    private readonly NullabilityInfoContext context = new();

    public object? Create(Type type, Nullness nullness, int depth = 0)
    {
        if (depth > 3 * MaxDepth)
        {
            throw new InvalidOperationException($"La muestra de {type} es demasiado profunda.");
        }
        var underlying = Nullable.GetUnderlyingType(type);
        if ((underlying is not null || (nullness.Nullable && !type.IsValueType)) && (!full || depth >= MaxDepth))
        {
            return null;
        }
        if (underlying is not null)
        {
            return Create(underlying, nullness with { Nullable = false }, depth);
        }
        if (Simple(type) is { } simple)
        {
            return simple;
        }
        if (type.IsEnum)
        {
            return Enum.GetValues(type).GetValue(0);
        }
        if (type.IsArray)
        {
            var element = type.GetElementType()!;
            var array = Array.CreateInstance(element, depth >= MaxDepth ? 0 : 1);
            if (array.Length == 1)
            {
                array.SetValue(Create(element, nullness.ElementOrNotNull, depth + 1), 0);
            }
            return array;
        }
        if (type.IsConstructedGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            var arguments = type.GetGenericArguments();
            if (definition.Name.StartsWith("ValueTuple", StringComparison.Ordinal))
            {
                return Activator.CreateInstance(type, arguments.Select((a, i) => Create(a, nullness.Argument(i), depth + 1)).ToArray());
            }
            if (definition == typeof(Dictionary<,>) || definition == typeof(IDictionary<,>) || definition == typeof(IReadOnlyDictionary<,>))
            {
                var dictionary = (System.Collections.IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(arguments))!;
                if (depth < MaxDepth)
                {
                    dictionary["clave"] = Create(arguments[1], nullness.Argument(1), depth + 1);
                }
                return dictionary;
            }
            if (typeof(System.Collections.IEnumerable).IsAssignableFrom(type) && arguments.Length == 1 && !IsModel(type))
            {
                var set = definition == typeof(HashSet<>) || definition == typeof(ISet<>) || definition == typeof(IReadOnlySet<>);
                var collection = (set ? typeof(HashSet<>) : typeof(List<>)).MakeGenericType(arguments);
                var list = Activator.CreateInstance(collection)!;
                if (depth < MaxDepth)
                {
                    collection.GetMethod("Add")!.Invoke(list, [Create(arguments[0], nullness.Argument(0), depth + 1)]);
                }
                return list;
            }
        }
        if (!IsModel(type))
        {
            throw new InvalidOperationException($"No sé armar una muestra de {type}.");
        }
        var constructor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        var instance = constructor.Invoke(constructor.GetParameters().Select(p => Create(p.ParameterType, NullnessOf(p), depth + 1)).ToArray());
        foreach (var property in type.GetProperties().Where(p => p.SetMethod is { IsPublic: true } &&
                                                                 !constructor.GetParameters().Any(c => string.Equals(c.Name, p.Name, StringComparison.OrdinalIgnoreCase))))
        {
            property.SetValue(instance, Create(property.PropertyType, Nullness.From(context.Create(property)), depth + 1));
        }
        return instance;
    }

    /// <summary>Igual que el generador: en un genérico cerrado, las marcas del compilador de la definición.</summary>
    public Nullness NullnessOf(ParameterInfo parameter)
    {
        if (parameter.Member.DeclaringType is not { IsConstructedGenericType: true } owner)
        {
            return Nullness.From(context.Create(parameter));
        }
        var definition = owner.GetGenericTypeDefinition().GetConstructors().Single(c => c.MetadataToken == parameter.Member.MetadataToken);
        return CompilerNullability.Of(definition.GetParameters()[parameter.Position], owner.GetGenericArguments());
    }

    private static bool IsModel(Type type) => type.Namespace?.StartsWith("MINV.", StringComparison.Ordinal) == true;

    private static object? Simple(Type type) => type switch
    {
        _ when type == typeof(string) || type == typeof(object) => ContractJson.Text,
        _ when type == typeof(char) => 'x',
        _ when type == typeof(bool) => true,
        _ when type == typeof(Guid) => ContractJson.SampleGuid,
        _ when type == typeof(DateTime) => new DateTime(2026, 9, 29, 10, 30, 0),
        _ when type == typeof(DateTimeOffset) => new DateTimeOffset(2026, 9, 29, 10, 30, 0, TimeSpan.FromHours(-4)),
        _ when type == typeof(DateOnly) => new DateOnly(2026, 9, 29),
        _ when type == typeof(TimeOnly) => new TimeOnly(10, 30),
        _ when type == typeof(TimeSpan) => TimeSpan.FromMinutes(90),
        _ when type == typeof(byte[]) => new byte[] { 1, 2, 3 },
        _ when type == typeof(JsonElement) => JsonDocument.Parse("{\"a\":1}").RootElement.Clone(),
        _ when type == typeof(decimal) || type == typeof(double) || type == typeof(float) => Convert.ChangeType(1.5m, type, CultureInfo.InvariantCulture),
        _ when type.IsPrimitive => Convert.ChangeType(1, type, CultureInfo.InvariantCulture),
        _ => null,
    };
}
