using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MINV.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// V7 · Arrendamiento del despachador del correo (B6): columna <c>integration.outgoing_mail_dispatch.leased_until</c> (la
    /// marca de dueño de cada reclamo; solo un correo pendiente puede estar tomado, CHECK
    /// <c>ck_outgoing_mail_dispatch_arrendamiento</c>) y la función <c>integration.claim_outgoing_mails</c> que la fija y la
    /// devuelve. Sin tablas nuevas (157 tablas en 10 esquemas). La V7WebPlatform ya publicada NO se edita (reglas A-07 y B-15):
    /// el SQL propio de PostgreSQL está en <c>V7MailLease.Sql.cs</c>.
    /// </summary>
    public partial class V7MailLease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Tolerante: una base de desarrollo creada con la edición provisional de V7WebPlatform ya puede tener la columna
            migrationBuilder.Sql("""
                ALTER TABLE integration.outgoing_mail_dispatch ADD COLUMN IF NOT EXISTS leased_until timestamp with time zone NULL;
                ALTER TABLE integration.outgoing_mail_dispatch DROP CONSTRAINT IF EXISTS ck_outgoing_mail_dispatch_arrendamiento;
                UPDATE integration.outgoing_mail_dispatch SET leased_until = NULL WHERE status <> 'Pending' AND leased_until IS NOT NULL;
                ALTER TABLE integration.outgoing_mail_dispatch ADD CONSTRAINT ck_outgoing_mail_dispatch_arrendamiento
                    CHECK (leased_until IS NULL OR status = 'Pending');
                """);

            V7MailLeaseClaimFunction(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            V7MailLeaseDropClaimFunction(migrationBuilder);

            migrationBuilder.DropCheckConstraint(
                name: "ck_outgoing_mail_dispatch_arrendamiento",
                schema: "integration",
                table: "outgoing_mail_dispatch");

            migrationBuilder.DropColumn(
                name: "leased_until",
                schema: "integration",
                table: "outgoing_mail_dispatch");
        }
    }
}
