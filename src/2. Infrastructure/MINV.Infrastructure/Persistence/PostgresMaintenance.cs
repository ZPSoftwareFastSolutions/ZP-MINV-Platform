namespace MINV.Infrastructure.Persistence;

/// <summary>
/// Mantenimiento de PostgreSQL. Después de una carga masiva (datos de prueba, relleno de una migración) el planificador no
/// tiene estadísticas hasta que pasa el autovacuum: una consulta con muchas uniones puede elegir un plan pésimo (medido:
/// 61 s contra 0,17 s). <see cref="AnalyzeSql"/> actualiza las estadísticas de las tablas de M-INV del rol actual.
/// </summary>
public static class PostgresMaintenance
{
    public const string AnalyzeSql = """
        DO $$
        DECLARE r record;
        BEGIN
            FOR r IN SELECT schemaname, tablename FROM pg_tables
                     WHERE schemaname IN ('iam', 'catalog', 'warehouse', 'inventory', 'purchasing', 'sales', 'accounting', 'integration')
                       AND tableowner = current_user
            LOOP
                EXECUTE format('ANALYZE %I.%I', r.schemaname, r.tablename);
            END LOOP;
        END;
        $$;
        """;
}
