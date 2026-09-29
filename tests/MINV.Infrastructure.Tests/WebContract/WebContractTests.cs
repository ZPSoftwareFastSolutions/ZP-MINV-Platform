using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using MINV.Application.Accounts;
using MINV.Application.Remote;
using MINV.Cli.WebContract;
using MINV.Domain.Iam;

namespace MINV.Infrastructure.Tests.WebContract;

/// <summary>
/// V7 · El contrato TypeScript de la web (<c>contract.generated.ts</c>, regla P-07): el archivo del repositorio es el que
/// genera el código actual, trae todas las operaciones de RpcCatalog con sus datos, y cada tipo coincide con el JSON que
/// de verdad escriben y leen <see cref="RpcJson.Options"/>.
/// </summary>
public sealed class WebContractTests
{
    private static readonly Lazy<GeneratedWebContract> Contract = new(WebContractGenerator.Generate);

    [Fact]
    public void El_contrato_de_la_web_del_repositorio_esta_al_dia()
    {
        var path = Path.Combine(V21MigrationTests.RepoRoot(), WebContractGenerator.RelativePath);
        var current = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        var expected = Contract.Value.Text;
        if (current == expected)
        {
            return;
        }
        var have = current.Split('\n');
        var want = expected.Split('\n');
        var line = Enumerable.Range(0, Math.Max(have.Length, want.Length)).First(i => i >= have.Length || i >= want.Length || have[i] != want[i]);
        Assert.Fail($"El contrato de la web ({WebContractGenerator.RelativePath}) no coincide con el código: ejecute minv contrato-web " +
                    $"({WebContractGenerator.Command}). Primera diferencia en la línea {line + 1}: " +
                    $"«{(line < have.Length ? have[line] : "(fin del archivo)")}» → «{(line < want.Length ? want[line] : "(fin del archivo)")}».");
    }

    [Fact]
    public void Es_determinista_con_fin_de_linea_LF_sin_BOM_y_sin_fecha()
    {
        var text = WebContractGenerator.Generate().Text;

        Assert.Equal(Contract.Value.Text, text);
        Assert.StartsWith("// " + WebContractGenerator.Warning + "\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', text);
        Assert.NotEqual((char)0xFEFF, text[0]);
        Assert.DoesNotContain(DateTime.Now.ToString("yyyy-MM-dd"), text, StringComparison.Ordinal);
        Assert.EndsWith("] as const;\n", text, StringComparison.Ordinal);
        // Tipos y operaciones en orden alfabético (ordinal)
        var names = Contract.Value.Catalog.Declarations.Select(d => d.Name).ToList();
        Assert.Equal(names.Order(StringComparer.Ordinal), names);
        var operations = Contract.Value.Operations.Select(o => o.Name).ToList();
        Assert.Equal(operations.Order(StringComparer.Ordinal), operations);
    }

    [Fact]
    public void Trae_cada_operacion_de_RpcCatalog_con_su_nombre_completo_permisos_modulos_y_si_la_usa_un_cliente()
    {
        var contract = Contract.Value;
        var text = contract.Text;
        Assert.Equal(RpcCatalog.Names.Count, contract.Operations.Count);
        foreach (var operation in contract.Operations)
        {
            Assert.True(RpcCatalog.TryResolve(operation.FullName, out var request, out var response));
            Assert.Equal((request, response), (operation.Request, operation.Response));
            Assert.Contains($"  {operation.Name}: {{ type: '{operation.FullName}', command: {Bool(RpcCatalog.IsCommand(request))}, " +
                            $"permissions: [{Codes(RpcCatalog.PermissionsOf(request))}], modules: [{Codes(RpcCatalog.ModulesOf(request))}], " +
                            $"customer: {Bool(RpcCatalog.IsAllowedForCustomer(request))} }},\n", text, StringComparison.Ordinal);
        }

        // Las operaciones de la cuenta del cliente que la web ya usa (contract.ts, ACCOUNT_OPERATIONS)
        Assert.Contains("  GetMyAccountQuery: { type: 'MINV.Application.Accounts.GetMyAccountQuery', command: false, permissions: ['account.manage'], " +
                        "modules: [], customer: true },\n", text, StringComparison.Ordinal);
        Assert.Contains("  CreateMyReservationCommand: { type: 'MINV.Application.Accounts.CreateMyReservationCommand', command: true, " +
                        "permissions: ['account.reserve'], modules: [], customer: true },\n", text, StringComparison.Ordinal);
        Assert.Contains("  ChangePasswordCommand: { type: 'MINV.Application.Iam.ChangePasswordCommand', command: true, permissions: [], modules: [], " +
                        "customer: true },\n", text, StringComparison.Ordinal);
        Assert.Contains("  SelectBranchCommand: { type: 'MINV.Application.Iam.SelectBranchCommand', command: true, permissions: [], modules: [], " +
                        "customer: false },\n", text, StringComparison.Ordinal);
        Assert.Contains("  GetMyReservationsQuery: { request: GetMyReservationsQuery; response: StorefrontReservationView[] };\n", text, StringComparison.Ordinal);
        Assert.Contains("  CreateMyReservationCommand: { request: CreateMyReservationCommand; response: StorefrontReservationView };\n", text, StringComparison.Ordinal);
        // La única respuesta anulable: la declara el manejador (Task<PhysicalCountSheet?>)
        Assert.Contains("  GetOpenPhysicalCountQuery: { request: GetOpenPhysicalCountQuery; response: PhysicalCountSheet | null };\n", text, StringComparison.Ordinal);
        Assert.Single(contract.Operations, o => o.ResponseNullness.Nullable);
        // Un genérico cerrado con nombre estable
        Assert.Contains("  GetApiCatalogQuery: { request: GetApiCatalogQuery; response: PageOfApiProduct };\n", text, StringComparison.Ordinal);
        // Una operación con módulo comercial
        Assert.Contains(contract.Operations, o => o.Modules.Count > 0 && text.Contains($"  {o.Name}: {{ type: '{o.FullName}', command: {Bool(o.Command)}, " +
                                                                                       $"permissions: [{Codes(o.Permissions)}], modules: [{Codes(o.Modules)}]", StringComparison.Ordinal));
    }

    [Fact]
    public void Tiene_la_forma_que_espera_el_adaptador_de_la_web()
    {
        var text = Contract.Value.Text;

        Assert.Contains("export interface WebSession {\n  access: BranchAccess;\n  company: string;\n  displayName: string;\n  email: string;\n" +
                        "  expiresAt: string;\n  kind: string;\n  mustChangePassword: boolean;\n  permissions: string[];\n  roles: string[];\n" +
                        "  serverVersion: string;\n}\n", text, StringComparison.Ordinal);
        // BranchAccess.Active es calculada: también viaja
        Assert.Contains("export interface BranchAccess {\n  active: BranchInfo | null;\n  activeBranchId: string | null;\n  allBranches: boolean;\n" +
                        "  branches: BranchInfo[];\n}\n", text, StringComparison.Ordinal);
        Assert.Contains("export interface RpcRequest {\n  payload: unknown;\n  requestId: string;\n  type: string;\n}\n", text, StringComparison.Ordinal);
        Assert.Contains("export interface RpcResponse {\n  error: RpcError | null;\n  ok: boolean;\n  replayed: boolean;\n  result: unknown;\n}\n", text,
            StringComparison.Ordinal);
        Assert.Contains("export interface RpcError {\n  code: string | null;\n  errors: string[] | null;\n  kind: string;\n  message: string;\n}\n", text,
            StringComparison.Ordinal);
        // Petición: los parámetros con valor por defecto son opcionales y AuditDetails (calculada) no viaja
        Assert.Contains("export interface CreateMyReservationCommand {\n  holdDays?: number | null;\n  kind?: PcBuildKind;\n" +
                        "  lines: StorefrontReservationLineInput[];\n  name?: string | null;\n  notes?: string | null;\n}\n", text, StringComparison.Ordinal);
        Assert.Contains("export type PcBuildKind = 'Build' | 'Cart';\n", text, StringComparison.Ordinal);
        Assert.Contains("export type GetMyAccountQuery = Record<string, never>;\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("auditDetails", text, StringComparison.Ordinal);
        Assert.Contains("export interface RpcOperations {\n", text, StringComparison.Ordinal);
        Assert.Contains("export interface RpcOperationMeta {\n", text, StringComparison.Ordinal);
        Assert.Contains("export const RPC_META = {\n", text, StringComparison.Ordinal);
        Assert.Contains("} as const satisfies Record<keyof RpcOperations, RpcOperationMeta>;\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Las_listas_de_permisos_y_roles_son_las_del_dominio_por_codigo()
    {
        var text = Contract.Value.Text;

        Assert.Contains("export const PERMISSIONS = [\n" +
                        string.Concat(PermissionCodes.All.OrderBy(p => p.Code, StringComparer.Ordinal).Select(p => $"  {{ code: '{p.Code}', name: '{p.Description}' }},\n")) +
                        "] as const;\n", text, StringComparison.Ordinal);
        Assert.Contains("export const ROLES = [\n" +
                        string.Concat(RoleCodes.All.OrderBy(r => r.Code, StringComparer.Ordinal).Select(r => $"  {{ code: '{r.Code}', name: '{r.Name}' }},\n")) +
                        "] as const;\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Cada_respuesta_serializada_con_RpcJson_cumple_su_tipo_TypeScript()
    {
        var contract = Contract.Value;
        var errors = new List<string>();
        var outputs = contract.Catalog.Declarations.OfType<TsInterface>().Where(d => d.Side == TsSide.Output).ToList();
        foreach (var full in new[] { true, false })
        {
            var samples = new ContractSamples(full);
            var label = full ? string.Empty : " (mínima)";
            foreach (var operation in contract.Operations)
            {
                var json = JsonSerializer.SerializeToElement(samples.Create(operation.Response, operation.ResponseNullness), operation.Response, RpcJson.Options);
                errors.AddRange(ContractJson.Check(json, contract.Types.Reference(operation.Response, operation.ResponseNullness, operation.FullName),
                    contract.Catalog, operation.Name + label));
            }
            foreach (var model in outputs)
            {
                var json = JsonSerializer.SerializeToElement(samples.Create(model.Source, Nullness.NotNull), model.Source, RpcJson.Options);
                errors.AddRange(ContractJson.Check(json, new TsReference(model.Source), contract.Catalog, model.Name + label));
            }
        }

        Assert.True(errors.Count == 0, $"{errors.Count} diferencias:\n{string.Join("\n", errors.Take(40))}");
        Assert.True(outputs.Count > 150, $"Solo {outputs.Count} tipos de respuesta.");
    }

    [Fact]
    public void Cada_peticion_armada_solo_con_su_tipo_TypeScript_la_lee_RpcJson_sin_perder_nada()
    {
        var contract = Contract.Value;
        var errors = new List<string>();
        var inputs = contract.Catalog.Declarations.OfType<TsInterface>().Where(d => d.Side == TsSide.Input).ToList();
        foreach (var full in new[] { true, false })
        {
            foreach (var model in inputs)
            {
                var sent = ContractJson.Build(new TsReference(model.Source), model.Source, contract.Catalog, full)!.AsObject();
                object? read;
                try
                {
                    read = JsonSerializer.Deserialize(sent.ToJsonString(), model.Source, RpcJson.Options);
                }
                catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or ArgumentException)
                {
                    errors.Add($"{model.Name}: RpcJson no lee {sent.ToJsonString()}: {ex.Message}");
                    continue;
                }
                foreach (var property in model.Properties.Where(p => sent.ContainsKey(p.Name)))
                {
                    var member = model.Source.GetProperties(BindingFlags.Public | BindingFlags.Instance).Single(p => JsonName(p) == property.Name);
                    var back = JsonSerializer.SerializeToNode(member.GetValue(read), member.PropertyType, RpcJson.Options);
                    if (!ContractJson.IsSubset(sent[property.Name], back))
                    {
                        errors.Add($"{model.Name}.{property.Name}: se envió {sent[property.Name]?.ToJsonString() ?? "null"} y se leyó {back?.ToJsonString() ?? "null"}");
                    }
                }
            }
        }

        Assert.True(errors.Count == 0, $"{errors.Count} diferencias:\n{string.Join("\n", errors.Take(40))}");
        Assert.Contains(inputs, m => m.Source == typeof(CreateMyReservationCommand));
        Assert.True(inputs.Count > 150, $"Solo {inputs.Count} tipos de petición.");
    }

    [Fact]
    public void Las_marcas_del_compilador_dan_la_misma_nulabilidad_que_NullabilityInfoContext()
    {
        // El generador las lee él mismo solo en los genéricos cerrados; aquí se comprueba el lector con TODO lo demás
        var context = new NullabilityInfoContext();
        var differences = new List<string>();
        var compared = 0;
        foreach (var type in Contract.Value.Catalog.Declarations.OfType<TsInterface>().Select(d => d.Source).Where(t => !t.IsGenericType))
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                Compare(CompilerNullability.Of(property, []), Nullness.From(context.Create(property)), $"{type.Name}.{property.Name}");
            }
            foreach (var parameter in type.GetConstructors().SelectMany(c => c.GetParameters()))
            {
                Compare(CompilerNullability.Of(parameter, []), Nullness.From(context.Create(parameter)), $"{type.Name}({parameter.Name})");
            }
        }

        Assert.True(differences.Count == 0, string.Join("\n", differences.Take(40)));
        Assert.True(compared > 1000, $"Solo {compared} comparaciones.");

        void Compare(Nullness decoded, Nullness expected, string where)
        {
            compared++;
            if (decoded.ToString() != expected.ToString())
            {
                differences.Add($"{where}: marcas {decoded} ≠ NullabilityInfoContext {expected}");
            }
        }
    }

    private static string Bool(bool value) => value ? "true" : "false";

    private static string Codes(IEnumerable<string> codes) => string.Join(", ", codes.Order(StringComparer.Ordinal).Select(c => $"'{c}'"));

    private static string JsonName(PropertyInfo property) =>
        property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? RpcJson.Options.PropertyNamingPolicy!.ConvertName(property.Name);
}
