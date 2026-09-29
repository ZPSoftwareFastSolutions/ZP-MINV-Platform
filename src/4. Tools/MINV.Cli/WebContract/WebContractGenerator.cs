using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using MediatR;
using MINV.Application.Remote;
using MINV.Domain.Iam;

namespace MINV.Cli.WebContract;

/// <summary>V7 · Una operación del RPC tal como la ve la web.</summary>
internal sealed record WebOperation(Type Request, Type Response, Nullness ResponseNullness, bool Command, IReadOnlyList<string> Permissions,
    IReadOnlyList<string> Modules, bool Customer)
{
    /// <summary>Nombre corto: la clave en <c>RpcOperations</c> y <c>RPC_META</c>.</summary>
    public string Name => Request.Name;

    /// <summary>Nombre completo: lo que viaja en <c>RpcRequest.type</c>.</summary>
    public string FullName => RpcCatalog.NameOf(Request);
}

/// <summary>V7 · El contrato generado: el texto del archivo y el modelo con que se escribió (lo usan las pruebas).</summary>
internal sealed record GeneratedWebContract(string Text, IReadOnlyList<WebOperation> Operations, TsCatalog Catalog, TypeScriptTypes Types);

/// <summary>
/// V7 · Generador del contrato TypeScript de la web (regla P-07; diseño §7): <c>minv contrato-web</c> escribe
/// <c>contract.generated.ts</c> por reflexión sobre <see cref="RpcCatalog"/> con la serialización de
/// <see cref="RpcJson.Options"/> (<see cref="TypeScriptTypes"/>). La forma es la que espera el único importador de la web
/// (<c>3-infrastructure/http/contract.ts</c>): <c>RpcOperations</c> (nombre corto → petición y respuesta), <c>RPC_META</c>
/// (nombre completo, si es comando, permisos, módulos y si la puede usar un cliente), <c>WebSession</c>,
/// <c>RpcRequest</c>, <c>RpcResponse</c>, <c>RpcError</c> y las listas <c>PERMISSIONS</c> y <c>ROLES</c>.
/// Salida determinista: orden alfabético (ordinal), LF, sin fecha. Una prueba falla si el archivo quedó desactualizado.
/// </summary>
internal static class WebContractGenerator
{
    /// <summary>Primera línea del archivo generado.</summary>
    public const string Warning = "archivo generado: no editar; regenerar con minv contrato-web";

    /// <summary>La orden para regenerarlo desde la raíz del repositorio.</summary>
    public const string Command = "dotnet run --project \"src/4. Tools/MINV.Cli\" -- contrato-web";

    /// <summary>Dónde vive el archivo dentro del repositorio.</summary>
    public static readonly string RelativePath =
        Path.Combine("src", "3. Presentation", "MINV.WebCatalog", "src", "3-infrastructure", "http", "contract.generated.ts");

    /// <summary>El sobre del RPC y la sesión web: la web los pide por su nombre (conservan el nombre si otro tipo choca).</summary>
    private static readonly (Type Type, TsSide Side)[] Envelope =
    [
        (typeof(RpcRequest), TsSide.Input), (typeof(RpcResponse), TsSide.Output), (typeof(RpcError), TsSide.Output), (typeof(WebSession), TsSide.Output),
    ];

    private const string Rule = "// ----------------------------------------------------------------------------------------------------";

    /// <summary>El contrato de TODAS las operaciones de <see cref="RpcCatalog"/>.</summary>
    public static GeneratedWebContract Generate() => Generate(CatalogOperations());

    public static GeneratedWebContract Generate(IReadOnlyList<WebOperation> operations)
    {
        var options = RpcJson.Options;
        CheckOptions(options);
        if (operations.GroupBy(o => o.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1) is { } repeated)
        {
            throw new InvalidOperationException(
                $"Dos operaciones del RPC tienen el mismo nombre corto «{repeated.Key}»: " +
                $"{string.Join(" y ", repeated.Select(o => o.FullName).Order(StringComparer.Ordinal))}. La web nombra cada operación por su " +
                "nombre corto (RpcOperations y RPC_META del contrato generado): cambie el nombre de una de ellas.");
        }
        var ordered = operations.OrderBy(o => o.Name, StringComparer.Ordinal).ToList();
        var types = new TypeScriptTypes(options, Envelope.Select(e => e.Type));
        // Primero lo que escribe el servidor: un tipo que la web también envía se declara como respuesta (completo)
        foreach (var (type, _) in Envelope.Where(e => e.Side == TsSide.Output))
        {
            types.AddRoot(type, Nullness.NotNull, TsSide.Output, "sobre del RPC");
        }
        foreach (var operation in ordered)
        {
            types.AddRoot(operation.Response, operation.ResponseNullness, TsSide.Output, "respuesta de " + operation.FullName);
        }
        foreach (var (type, _) in Envelope.Where(e => e.Side == TsSide.Input))
        {
            types.AddRoot(type, Nullness.NotNull, TsSide.Input, "sobre del RPC");
        }
        foreach (var operation in ordered)
        {
            types.AddRoot(operation.Request, Nullness.NotNull, TsSide.Input, "petición " + operation.FullName);
        }
        var catalog = types.Build();
        return new GeneratedWebContract(Render(ordered, types, catalog), ordered, catalog, types);
    }

    /// <summary>
    /// Las operaciones de <see cref="RpcCatalog"/>. La nulabilidad de la respuesta sale del manejador
    /// (<c>Task&lt;T?&gt; Handle(…)</c>): la del <c>IRequest&lt;T?&gt;</c> no se puede leer por reflexión. Cada petición debe
    /// tener un único manejador en MINV.Application.
    /// </summary>
    public static IReadOnlyList<WebOperation> CatalogOperations()
    {
        var application = typeof(RpcCatalog).Assembly;
        var context = new NullabilityInfoContext();
        var handlers = application.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false })
            .SelectMany(t => t.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>))
                .Select(i => (Request: i.GetGenericArguments()[0], Handle: t.GetInterfaceMap(i).TargetMethods[0])))
            .ToLookup(h => h.Request, h => h.Handle);
        var result = new List<WebOperation>();
        foreach (var name in RpcCatalog.Names.Order(StringComparer.Ordinal))
        {
            RpcCatalog.TryResolve(name, out var request, out var response);
            if (handlers[request].ToList() is not [var handle])
            {
                throw new InvalidOperationException($"{name} tiene {handlers[request].Count()} manejadores en {application.GetName().Name}: el contrato " +
                                                    "de la web necesita exactamente uno para saber si la respuesta puede ser nula.");
            }
            var nullness = Nullness.From(context.Create(handle.ReturnParameter)).Argument(0);
            result.Add(new WebOperation(request, response, nullness, RpcCatalog.IsCommand(request), RpcCatalog.PermissionsOf(request),
                RpcCatalog.ModulesOf(request), RpcCatalog.IsAllowedForCustomer(request)));
        }
        return result;
    }

    /// <summary>Raíz del repositorio (donde está MINV.sln): desde la carpeta actual o desde la de la herramienta.</summary>
    public static string RepositoryRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "MINV.sln")))
                {
                    return directory.FullName;
                }
            }
        }
        throw new InvalidOperationException("No se encontró la raíz del repositorio (MINV.sln): ejecute minv contrato-web dentro del repositorio " +
                                            "o indique --salida <archivo>.");
    }

    /// <summary>El generador traduce la serialización de RpcJson.Options: si esas opciones cambian en algo que altera el JSON
    /// y que no sabe traducir, se niega en vez de escribir un contrato equivocado.</summary>
    private static void CheckOptions(JsonSerializerOptions options)
    {
        var problems = new List<string>();
        if (!options.IncludeFields)
        {
            problems.Add("IncludeFields = false (las tuplas viajarían vacías)");
        }
        if (options.DefaultIgnoreCondition != JsonIgnoreCondition.Never || options.IgnoreReadOnlyProperties || options.IgnoreReadOnlyFields)
        {
            problems.Add("se omiten propiedades al escribir (DefaultIgnoreCondition, IgnoreReadOnlyProperties o IgnoreReadOnlyFields)");
        }
        if ((options.NumberHandling & JsonNumberHandling.WriteAsString) != 0)
        {
            problems.Add("los números se escriben como texto");
        }
        if (options.ReferenceHandler is not null)
        {
            problems.Add("ReferenceHandler ($id y $ref)");
        }
        if (options.Converters.Any(c => c is not JsonStringEnumConverter))
        {
            problems.Add("hay convertidores además del de las enumeraciones");
        }
        if (options.TypeInfoResolver?.GetType() != typeof(DefaultJsonTypeInfoResolver) ||
            ((DefaultJsonTypeInfoResolver)options.TypeInfoResolver).Modifiers.Count > 0)
        {
            problems.Add("el resolvedor de tipos no es el predeterminado");
        }
        if (problems.Count > 0)
        {
            throw new NotSupportedException("RpcJson.Options cambió y el generador del contrato de la web no sabe traducirlo: " + string.Join("; ", problems) +
                                            ". Actualice src/4. Tools/MINV.Cli/WebContract.");
        }
    }

    private static string Render(IReadOnlyList<WebOperation> operations, TypeScriptTypes types, TsCatalog catalog)
    {
        var text = new StringBuilder();
        text.Append("// ").Append(Warning).Append('\n')
            .Append("//\n")
            .Append("// Contrato del RPC del servidor en la nube para la web (regla P-07). Lo escribe `minv contrato-web` por reflexión\n")
            .Append("// sobre RpcCatalog, con la serialización de RpcJson.Options:\n")
            .Append("//   ").Append(Command).Append('\n')
            .Append("// Nombres en camelCase; enumeraciones como texto; tuplas como { item1, item2 }; Guid, fechas, horas y byte[] (base64)\n")
            .Append("// como texto; T? como T | null. En una PETICIÓN, un parámetro con valor por defecto es opcional y las propiedades\n")
            .Append("// calculadas no viajan; en una RESPUESTA viajan todas las propiedades, también las calculadas. Una prueba del servidor\n")
            .Append("// falla si este archivo no coincide con el código. Su ÚNICO importador es ./contract.ts: el resto de la web usa los\n")
            .Append("// nombres de ese adaptador.\n");

        text.Append('\n').Append(Rule).Append(" tipos de las peticiones y las respuestas\n");
        foreach (var declaration in catalog.Declarations)
        {
            text.Append('\n');
            catalog.Write(text, declaration);
        }

        text.Append('\n').Append(Rule).Append(" operaciones del RPC\n\n")
            .Append("/** Nombre corto de cada operación → su petición y su respuesta. */\n")
            .Append("export interface RpcOperations {\n");
        foreach (var operation in operations)
        {
            var request = catalog.Render(types.Reference(operation.Request, Nullness.NotNull, operation.FullName));
            var response = catalog.Render(types.Reference(operation.Response, operation.ResponseNullness, operation.FullName));
            text.Append("  ").Append(operation.Name).Append(": { request: ").Append(request).Append("; response: ").Append(response).Append(" };\n");
        }
        text.Append("}\n\n")
            .Append("/** Datos de una operación del RPC. */\n")
            .Append("export interface RpcOperationMeta {\n")
            .Append("  /** Nombre completo del caso de uso: es lo que viaja en `RpcRequest.type`. */\n")
            .Append("  type: string;\n")
            .Append("  /** Comando que modifica datos (auditado e idempotente por `requestId`). */\n")
            .Append("  command: boolean;\n")
            .Append("  /** Permisos que exige: todos. */\n")
            .Append("  permissions: readonly string[];\n")
            .Append("  /** Módulos comerciales que exige: todos. */\n")
            .Append("  modules: readonly string[];\n")
            .Append("  /** Lo puede ejecutar una sesión de cliente (regla P-04). */\n")
            .Append("  customer: boolean;\n")
            .Append("}\n\n")
            .Append("/** Operaciones del RPC por nombre corto. El servidor vuelve a comprobar todo en cada petición (regla P-01). */\n")
            .Append("export const RPC_META = {\n");
        foreach (var operation in operations)
        {
            text.Append("  ").Append(operation.Name).Append(": { type: ").Append(TsCatalog.Quote(operation.FullName))
                .Append(", command: ").Append(operation.Command ? "true" : "false")
                .Append(", permissions: ").Append(List(operation.Permissions))
                .Append(", modules: ").Append(List(operation.Modules))
                .Append(", customer: ").Append(operation.Customer ? "true" : "false").Append(" },\n");
        }
        text.Append("} as const satisfies Record<keyof RpcOperations, RpcOperationMeta>;\n");

        text.Append('\n').Append(Rule).Append(" permisos y roles\n\n")
            .Append("/** Permisos del sistema con su nombre en español (PermissionCodes.All), por código. */\n")
            .Append("export const PERMISSIONS = [\n");
        foreach (var (code, description) in PermissionCodes.All.OrderBy(p => p.Code, StringComparer.Ordinal))
        {
            text.Append("  { code: ").Append(TsCatalog.Quote(code)).Append(", name: ").Append(TsCatalog.Quote(description)).Append(" },\n");
        }
        text.Append("] as const;\n\n")
            .Append("/** Roles del sistema con su nombre en español (RoleCodes.All), por código. */\n")
            .Append("export const ROLES = [\n");
        foreach (var (code, name) in RoleCodes.All.OrderBy(r => r.Code, StringComparer.Ordinal))
        {
            text.Append("  { code: ").Append(TsCatalog.Quote(code)).Append(", name: ").Append(TsCatalog.Quote(name)).Append(" },\n");
        }
        text.Append("] as const;\n");
        return text.ToString();
    }

    private static string List(IEnumerable<string> values) => "[" + string.Join(", ", values.Order(StringComparer.Ordinal).Select(TsCatalog.Quote)) + "]";
}
