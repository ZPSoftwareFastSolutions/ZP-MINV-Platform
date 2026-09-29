using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using MINV.Application;
using MINV.Application.Abstractions;
using MINV.Infrastructure.Persistence;
using MINV.Infrastructure.Seeding;
using MINV.Infrastructure.Services;
using Npgsql;

namespace MINV.Infrastructure.Tests;

/// <summary>
/// V7 · Comprobación de normalización (paquete B4c): ejecuta las consultas de <c>scripts/verificar_normalizacion.sql</c> y falla
/// si una consulta «problema» devuelve una fila que no está en la tabla «Excepciones documentadas» de
/// <c>docs/database/normalizacion-v7.md</c> (o si una excepción ya no aparece: la lista se mantiene al día). Sin PostgreSQL solo
/// revisa la forma del script, la lista de excepciones y que todo enum guardado como texto tenga su CHECK en el modelo; con
/// <c>MINV_TEST_PG</c> ejecuta las consultas sobre una base migrada, comprueba que detectan una tabla mal diseñada y datos
/// incoherentes, y que las redundancias controladas cuadran con los datos de prueba.
/// </summary>
public sealed partial class NormalizationTests(PostgresFixture pg) : IClassFixture<PostgresFixture>
{
    /// <summary>Una consulta del script: id (E01…, I01…, D01…), clase («problema» o «informativa»), título y SQL sin las órdenes de psql.</summary>
    internal sealed record Check(string Id, string Kind, string Title, string Sql, IReadOnlyList<string> Comments)
    {
        public bool IsProblem => Kind == "problema";
    }

    /// <summary>Una fila devuelta por una consulta (todas devuelven tabla | columna | detalle).</summary>
    internal sealed record Finding(string Check, string Table, string? Column, string Detail)
    {
        public string Key => Keys(Check, Table, Column);

        public override string ToString() => $"{Check} · {Table}{(Column is null ? string.Empty : "." + Column)}: {Detail}";
    }

    private static readonly string[] RequiredChecks =
    [
        "E01", "E02", "E03", "E04", "E05", "E06", "E07", "E08", "E09", "E10", "E11", "E12", "E13", "E14", "E15", "E16", "E17",
        "E18", "E19", "I01", "I02", "I03", "D01", "D02", "D03", "D04", "D05", "D06", "D07", "D08", "D09", "D10",
    ];

    private static string ScriptPath => Path.Combine(V21MigrationTests.RepoRoot(), "scripts", "verificar_normalizacion.sql");

    private static string ReportPath => Path.Combine(V21MigrationTests.RepoRoot(), "docs", "database", "normalizacion-v7.md");

    private static string Keys(string check, string table, string? column) => $"{check}|{table}|{column ?? string.Empty}";

    [GeneratedRegex(@"^-- @comprobacion (?<id>[EID]\d{2}) · (?<kind>problema|informativa) · (?<title>.+)$")]
    private static partial Regex HeaderPattern();

    [GeneratedRegex(@"^\|\s*(?<id>[EID]\d{2})\s*\|\s*`(?<table>[a-z_]+\.[a-z_]+)`\s*\|\s*(?:`(?<column>[^`]+)`|—)\s*\|\s*(?<kind>[^|]+?)\s*\|\s*(?<reason>[^|]+?)\s*\|\s*$")]
    private static partial Regex ExceptionRow();

    /// <summary>Las consultas del script en orden. El preámbulo (órdenes de psql) queda fuera; las líneas «\…» son de psql.</summary>
    internal static IReadOnlyList<Check> Checks()
    {
        var checks = new List<Check>();
        string? id = null, kind = null, title = null;
        var sql = new List<string>();
        var comments = new List<string>();
        void Flush()
        {
            if (id is not null)
            {
                checks.Add(new Check(id, kind!, title!, string.Join('\n', sql).Trim(), comments.ToList()));
            }
            sql.Clear();
            comments.Clear();
        }
        foreach (var line in File.ReadAllLines(ScriptPath))
        {
            var header = HeaderPattern().Match(line);
            if (header.Success)
            {
                Flush();
                (id, kind, title) = (header.Groups["id"].Value, header.Groups["kind"].Value, header.Groups["title"].Value);
                continue;
            }
            if (id is null || line.StartsWith('\\'))
            {
                continue;
            }
            if (line.StartsWith("--", StringComparison.Ordinal))
            {
                if (line.Trim('-', ' ').Length > 0)
                {
                    comments.Add(line);
                }
                continue;
            }
            sql.Add(line);
        }
        Flush();
        return checks;
    }

    /// <summary>La tabla «Excepciones documentadas» del informe (entre sus marcas): clave → (clasificación, motivo).</summary>
    internal static IReadOnlyDictionary<string, (string Kind, string Reason)> DocumentedExceptions()
    {
        var text = File.ReadAllLines(ReportPath);
        var start = Array.FindIndex(text, l => l.Contains("<!-- excepciones:inicio -->", StringComparison.Ordinal));
        var end = Array.FindIndex(text, l => l.Contains("<!-- excepciones:fin -->", StringComparison.Ordinal));
        Assert.True(start >= 0 && end > start, "Falta la tabla de excepciones (<!-- excepciones:inicio --> … <!-- excepciones:fin -->) en normalizacion-v7.md");
        var exceptions = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        foreach (var line in text[(start + 1)..end].Where(l => Regex.IsMatch(l, @"^\|\s*[EID]\d{2}\s*\|")))
        {
            var row = ExceptionRow().Match(line);
            Assert.True(row.Success, $"Fila de excepción mal formada: {line}");
            var key = Keys(row.Groups["id"].Value, row.Groups["table"].Value, row.Groups["column"].Success ? row.Groups["column"].Value : null);
            Assert.True(exceptions.TryAdd(key, (row.Groups["kind"].Value, row.Groups["reason"].Value)), $"Excepción repetida: {key}");
        }
        return exceptions;
    }

    private static async Task<List<Finding>> RunAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Check check)
    {
        await using var command = new NpgsqlCommand(check.Sql, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.Equal(new[] { "tabla", "columna", "detalle" }, Enumerable.Range(0, reader.FieldCount).Select(reader.GetName));
        var rows = new List<Finding>();
        while (await reader.ReadAsync())
        {
            rows.Add(new Finding(check.Id, reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetString(2)));
        }
        return rows;
    }

    /// <summary>Sin PostgreSQL: cada regla pedida tiene su consulta comentada, de solo lectura y con su título para psql; la lista de
    /// excepciones del informe solo nombra consultas «problema» de estructura y cada excepción dice su clasificación y su motivo.</summary>
    [Fact]
    public void El_script_tiene_una_consulta_comentada_de_solo_lectura_por_regla_y_excepciones_documentadas()
    {
        var checks = Checks();
        Assert.Equal(RequiredChecks, checks.Select(c => c.Id));
        var text = File.ReadAllText(ScriptPath);
        foreach (var check in checks)
        {
            Assert.True(check.Comments.Count >= 1, $"{check.Id}: falta el comentario que explica la regla");
            Assert.Contains($"\\echo '{check.Id} · {check.Kind} · {check.Title}'", text, StringComparison.Ordinal);
            Assert.Equal(check.Id.StartsWith('I') ? "informativa" : "problema", check.Kind);
            if (check.Kind == "informativa")
            {
                Assert.Contains("revisión informativa", check.Title, StringComparison.Ordinal);
            }
            // Una sola sentencia, de solo lectura (sin contar los textos entre comillas, como 'UPDATE' en E05)
            var code = Regex.Replace(check.Sql, "'[^']*'", "''");
            Assert.Equal(1, code.Count(c => c == ';'));
            Assert.EndsWith(";", code, StringComparison.Ordinal);
            Assert.DoesNotMatch(new Regex(@"\b(INSERT|UPDATE|DELETE|MERGE|CREATE|ALTER|DROP|TRUNCATE|GRANT|REVOKE|COPY|CALL|DO)\b",
                RegexOptions.IgnoreCase), code);
        }
        var exceptions = DocumentedExceptions();
        Assert.NotEmpty(exceptions);
        var structural = checks.Where(c => c.IsProblem && c.Id.StartsWith('E')).Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var (key, (kind, reason)) in exceptions)
        {
            Assert.Contains(key.Split('|')[0], structural);
            Assert.Matches("^(Redundancia controlada ya documentada|Excepción de diseño ya documentada|Incumplimiento real.*)$", kind);
            Assert.True(reason.Length >= 20, $"{key}: el motivo es demasiado corto");
        }
    }

    /// <summary>Sin PostgreSQL (regla del dominio de los atributos, E13 y E14): toda columna que guarda un enum como texto tiene en el
    /// modelo un CHECK que la limita a sus valores (IN (…), o igualdades como en la clase del documento fiscal), y todo enum guardado
    /// como número, un CHECK con su rango. Así una columna nueva de un enum no puede quedar sin dominio.</summary>
    [Fact]
    public void Toda_columna_de_un_enum_tiene_un_CHECK_con_sus_valores()
    {
        var db = new MinvWriteDbContextDesignTimeFactory().CreateDbContext([]);
        var model = db.GetService<IDesignTimeModel>().Model;
        var missing = new List<string>();
        foreach (var entity in model.GetEntityTypes().Where(e => !e.IsOwned()))
        {
            var checks = entity.GetCheckConstraints().Select(c => c.Sql).ToList();
            foreach (var property in entity.GetProperties())
            {
                var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                if (!type.IsEnum)
                {
                    continue;
                }
                var column = property.GetColumnName();
                var provider = property.GetValueConverter()?.ProviderClrType ?? property.GetProviderClrType() ?? type;
                var pattern = provider == typeof(string)
                    ? $@"\b{column} IN \(|\b{column} = '"
                    : $@"\b{column} IN \(|\b{column} BETWEEN ";
                if (!checks.Any(sql => Regex.IsMatch(sql, pattern)))
                {
                    missing.Add($"{entity.GetSchema()}.{entity.GetTableName()}.{column} ({type.Name})");
                }
            }
        }
        Assert.True(missing.Count == 0, "Columnas de un enum sin CHECK de dominio: " + string.Join(", ", missing));
    }

    /// <summary>Con PostgreSQL: sobre la base migrada, cada consulta corre sin errores; las de «problema» solo devuelven las excepciones
    /// documentadas y cada excepción documentada sigue apareciendo (si se corrige, se quita de la lista del informe).</summary>
    [PostgresFact]
    public async Task La_base_migrada_cumple_las_reglas_de_normalizacion_salvo_las_excepciones_documentadas()
    {
        var exceptions = DocumentedExceptions();
        await using var connection = new NpgsqlConnection(pg.ConnectionString);
        await connection.OpenAsync();
        var found = new List<Finding>();
        foreach (var check in Checks())
        {
            var rows = await RunAsync(connection, null, check);
            if (check.Id == "I01")
            {
                Assert.Contains(rows, r => r.Table == "Tablas del modelo" && r.Detail == "157");
            }
            if (check.IsProblem)
            {
                found.AddRange(rows);
            }
        }
        var unexpected = found.Where(f => !exceptions.ContainsKey(f.Key)).Select(f => f.ToString()).ToList();
        Assert.True(unexpected.Count == 0,
            "Hallazgos que no están en las excepciones documentadas de docs/database/normalizacion-v7.md:\n" + string.Join('\n', unexpected));
        var stale = exceptions.Keys.Except(found.Select(f => f.Key), StringComparer.Ordinal).ToList();
        Assert.True(stale.Count == 0, "Excepciones documentadas que ya no aparecen (quítelas del informe): " + string.Join(", ", stale));
    }

    /// <summary>Con PostgreSQL: las consultas de estructura no son vacías por construcción: en una transacción que se deshace se crean
    /// tablas que rompen cada regla y cada consulta las encuentra.</summary>
    [PostgresFact]
    public async Task Las_consultas_de_estructura_detectan_una_tabla_mal_disenada()
    {
        await using var connection = new NpgsqlConnection(pg.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var ddl = new NpgsqlCommand("""
            CREATE TABLE sales.b4c_sin_empresa (id integer PRIMARY KEY);
            CREATE TABLE sales.b4c_mal_disenada (
                id integer,
                tenant_id uuid NOT NULL,
                branch_id uuid NOT NULL,
                customer_id uuid,
                customer_code varchar(20),
                otro_cliente_id uuid REFERENCES sales.customers (id),
                notas text,
                total numeric(18,2),
                datos jsonb,
                code varchar(20),
                status varchar(20),
                kind varchar(10),
                telefono1 varchar(20),
                activo boolean NOT NULL DEFAULT true,
                baja timestamptz,
                referencia varchar(20),
                created_at timestamptz NOT NULL DEFAULT now(),
                CONSTRAINT ck_b4c_uno CHECK (total >= 0),
                CONSTRAINT ck_b4c_dos CHECK (total >= 0),
                CONSTRAINT ck_b4c_baja CHECK (activo = (baja IS NULL)),
                CONSTRAINT fk_b4c_cliente FOREIGN KEY (tenant_id, customer_id) REFERENCES sales.customers (tenant_id, id));
            CREATE UNIQUE INDEX ux_b4c_referencia ON sales.b4c_mal_disenada (tenant_id, referencia);
            CREATE TABLE sales.b4c_padre (id uuid PRIMARY KEY, tenant_id uuid NOT NULL, branch_id uuid NOT NULL, UNIQUE (tenant_id, branch_id, id),
                                          UNIQUE (tenant_id, id));
            CREATE TABLE sales.b4c_hijo (id uuid PRIMARY KEY, tenant_id uuid NOT NULL, branch_id uuid NOT NULL, padre_id uuid NOT NULL,
                                         CONSTRAINT fk_b4c_hijo_padre FOREIGN KEY (tenant_id, padre_id) REFERENCES sales.b4c_padre (tenant_id, id));
            CREATE POLICY branch_isolation ON sales.b4c_padre AS RESTRICTIVE USING (iam.branch_visible(branch_id));
            CREATE POLICY branch_isolation ON sales.b4c_hijo AS RESTRICTIVE USING (iam.branch_visible(branch_id));
            """, connection, transaction))
        {
            await ddl.ExecuteNonQueryAsync();
        }
        var found = new List<Finding>();
        foreach (var check in Checks().Where(c => c.Id.StartsWith('E')))
        {
            found.AddRange(await RunAsync(connection, transaction, check));
        }
        const string bad = "sales.b4c_mal_disenada";
        (string Check, string Table, string? Column)[] expected =
        [
            ("E01", bad, null), ("E02", "sales.b4c_sin_empresa", null), ("E03", bad, null), ("E04", bad, "branch_id"), ("E05", bad, null),
            ("E06", bad, "tenant_id, customer_id"), ("E06", bad, "otro_cliente_id"), ("E07", bad, "otro_cliente_id"),
            ("E08", "sales.b4c_hijo", "tenant_id, padre_id"), ("E09", bad, "notas"), ("E10", bad, "total"), ("E11", bad, "datos"),
            ("E12", bad, "code"), ("E13", bad, "status"), ("E14", bad, "kind"), ("E15", bad, "ck_b4c_dos, ck_b4c_uno"),
            ("E16", bad, "referencia"), ("E17", bad, "telefono1"), ("E18", bad, "customer_code"), ("E19", bad, "activo"),
        ];
        var keys = found.Select(f => f.Key).ToHashSet(StringComparer.Ordinal);
        var missed = expected.Select(e => Keys(e.Check, e.Table, e.Column)).Where(k => !keys.Contains(k)).ToList();
        Assert.True(missed.Count == 0, "Consultas que no detectaron su regla rota: " + string.Join(", ", missed));
        await transaction.RollbackAsync();
    }

    /// <summary>Con PostgreSQL: con los datos de prueba (una empresa de 8 días sin facturación, cargada con los casos de uso) las
    /// redundancias controladas cuadran (D01 a D10 sin filas) y, en una transacción que se deshace, una existencia con reservado de
    /// más y una cola de correo con un intento sin fila se detectan.</summary>
    [PostgresFact]
    public async Task Las_redundancias_controladas_cuadran_con_los_datos_de_prueba()
    {
        var services = new ServiceCollection();
        services.AddSingleton<DemoClock>();
        services.AddSingleton<IClock>(sp => sp.GetRequiredService<DemoClock>());
        services.AddMinvApplication();
        services.AddMinvInfrastructure(pg.ConnectionString);
        await using (var provider = services.BuildServiceProvider())
        {
            var result = await provider.GetRequiredService<LocalDataSeeder>()
                .SeedAsync(new SeedOptions("NORMAL", Days: 8, Seed: 7, Billing: false), _ => { });
            Assert.True(result.Tickets > 0);
        }
        var data = Checks().Where(c => c.Id.StartsWith('D')).ToList();
        await using var connection = new NpgsqlConnection(pg.ConnectionString);
        await connection.OpenAsync();
        async Task<long> Scalar(string sql, NpgsqlTransaction? tx = null)
        {
            await using var command = new NpgsqlCommand(sql, connection, tx);
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }
        // Hay datos que revisar en las tablas con redundancias controladas
        Assert.True(await Scalar("SELECT count(*) FROM inventory.stock_reservations WHERE status = 'Active'") > 0);
        Assert.True(await Scalar("SELECT count(*) FROM sales.pc_build_events") > 0);
        Assert.True(await Scalar("SELECT count(*) FROM integration.outgoing_mail_dispatch") > 0);
        foreach (var check in data)
        {
            var rows = await RunAsync(connection, null, check);
            Assert.True(rows.Count == 0, $"{check.Id} · {check.Title}:\n" + string.Join('\n', rows));
        }

        await using var transaction = await connection.BeginTransactionAsync();
        Assert.Equal(1, await Scalar("""
            WITH x AS (SELECT id FROM inventory.stock_levels WHERE quantity_on_hand - quantity_reserved >= 1 ORDER BY id LIMIT 1)
            UPDATE inventory.stock_levels s SET quantity_reserved = s.quantity_reserved + 1 FROM x WHERE s.id = x.id RETURNING 1
            """, transaction));
        Assert.Equal(1, await Scalar("""
            WITH x AS (SELECT outgoing_mail_id FROM integration.outgoing_mail_dispatch WHERE status = 'Pending' AND attempts = 0
                       ORDER BY outgoing_mail_id LIMIT 1)
            UPDATE integration.outgoing_mail_dispatch d SET attempts = 1 FROM x WHERE d.outgoing_mail_id = x.outgoing_mail_id RETURNING 1
            """, transaction));
        Assert.Single(await RunAsync(connection, transaction, data.Single(c => c.Id == "D02")));
        Assert.Single(await RunAsync(connection, transaction, data.Single(c => c.Id == "D08")));
        await transaction.RollbackAsync();
    }
}
