using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MINV.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// V7 · SQL propio de PostgreSQL del arrendamiento del correo (lo llama <see cref="V7MailLease"/>, regla B-15): reemplaza la
    /// función SECURITY DEFINER <c>integration.claim_outgoing_mails(integer, integer)</c> de la <see cref="V7WebPlatform"/> por la
    /// que además fija y devuelve <c>leased_until</c>. Como cambia el tipo que devuelve, <c>CREATE OR REPLACE</c> no sirve: se
    /// borra y se vuelve a crear con <c>search_path</c> fijo, sin EXECUTE para PUBLIC y con EXECUTE solo para <c>minv_server</c>
    /// si existe (reglas B-12 y B-13). La reversa deja la función de la V7WebPlatform, con los mismos privilegios.
    /// </summary>
    public partial class V7MailLease
    {
        /// <summary>
        /// Reclama los pendientes vencidos de TODAS las empresas antes de fijar <c>minv.tenant_id</c> (FOR UPDATE SKIP LOCKED) y
        /// devuelve solo (empresa, correo, arrendamiento). B6: el arrendamiento es la marca de dueño de ESTE reclamo: el
        /// despachador lo renueva justo antes de enviar solo si sigue siendo el suyo (otra réplica que lo reclamó al vencer tiene
        /// otro valor).
        /// </summary>
        private static void V7MailLeaseClaimFunction(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql($"""
                DROP FUNCTION IF EXISTS integration.claim_outgoing_mails(integer, integer);
                CREATE FUNCTION integration.claim_outgoing_mails(p_limit integer, p_lease_seconds integer)
                RETURNS TABLE (tenant_id uuid, outgoing_mail_id uuid, leased_until timestamptz)
                LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, integration AS $$
                #variable_conflict use_column
                BEGIN
                    RETURN QUERY
                    WITH due AS (
                        SELECT d.outgoing_mail_id AS id
                        FROM integration.outgoing_mail_dispatch d
                        WHERE d.status = 'Pending' AND d.next_attempt_at <= now()
                        ORDER BY d.next_attempt_at
                        LIMIT greatest(1, least(p_limit, 500))
                        FOR UPDATE SKIP LOCKED)
                    UPDATE integration.outgoing_mail_dispatch d
                       SET next_attempt_at = now() + make_interval(secs => greatest(30, least(p_lease_seconds, 3600))),
                           leased_until = now() + make_interval(secs => greatest(30, least(p_lease_seconds, 3600)))
                    FROM due
                    WHERE d.outgoing_mail_id = due.id
                    RETURNING d.tenant_id, d.outgoing_mail_id, d.leased_until;
                END;
                $$;
                {Privileges}
                """);

        /// <summary>Reversa: la función de la <see cref="V7WebPlatform"/>, que devuelve solo (empresa, correo) y no toca
        /// <c>leased_until</c> (la parte generada borra la columna después).</summary>
        private static void V7MailLeaseDropClaimFunction(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql($"""
                DROP FUNCTION IF EXISTS integration.claim_outgoing_mails(integer, integer);
                CREATE FUNCTION integration.claim_outgoing_mails(p_limit integer, p_lease_seconds integer)
                RETURNS TABLE (tenant_id uuid, outgoing_mail_id uuid)
                LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, integration AS $$
                #variable_conflict use_column
                BEGIN
                    RETURN QUERY
                    WITH due AS (
                        SELECT d.outgoing_mail_id AS id
                        FROM integration.outgoing_mail_dispatch d
                        WHERE d.status = 'Pending' AND d.next_attempt_at <= now()
                        ORDER BY d.next_attempt_at
                        LIMIT greatest(1, least(p_limit, 500))
                        FOR UPDATE SKIP LOCKED)
                    UPDATE integration.outgoing_mail_dispatch d
                       SET next_attempt_at = now() + make_interval(secs => greatest(30, least(p_lease_seconds, 3600)))
                    FROM due
                    WHERE d.outgoing_mail_id = due.id
                    RETURNING d.tenant_id, d.outgoing_mail_id;
                END;
                $$;
                {Privileges}
                """);

        /// <summary>Privilegios de la función recreada (el DROP se lleva los anteriores): nadie por PUBLIC y EXECUTE solo para
        /// <c>minv_server</c>, si existe (regla B-12: los roles se crean antes de migrar).</summary>
        private const string Privileges = """
            REVOKE ALL ON FUNCTION integration.claim_outgoing_mails(integer, integer) FROM PUBLIC;
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'minv_server') THEN
                    GRANT EXECUTE ON FUNCTION integration.claim_outgoing_mails(integer, integer) TO minv_server;
                END IF;
            END;
            $$;
            """;
    }
}
