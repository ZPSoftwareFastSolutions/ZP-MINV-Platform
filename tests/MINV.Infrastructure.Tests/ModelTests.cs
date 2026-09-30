using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using MINV.Domain.Accounting;
using MINV.Domain.Billing;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;
using MINV.Domain.Sales;
using MINV.Domain.Service;
using MINV.Infrastructure;
using MINV.Infrastructure.Persistence;

namespace MINV.Infrastructure.Tests;

/// <summary>Pruebas del modelo relacional (no necesitan PostgreSQL: EF Core arma el modelo y el DDL en memoria).</summary>
public sealed class ModelTests
{
    private static readonly MinvWriteDbContext Db = new MinvWriteDbContextDesignTimeFactory().CreateDbContext([]);
    private static readonly IModel Model = Db.GetService<IDesignTimeModel>().Model;
    private static readonly string Ddl = Db.Database.GenerateCreateScript();

    private static IEnumerable<IEntityType> Entities => Model.GetEntityTypes().Where(e => !e.IsOwned());

    private static string Name(IEntityType e) => $"{e.GetSchema()}.{e.GetTableName()}";

    [Fact]
    public void El_modelo_tiene_157_tablas_en_10_esquemas()
    {
        var tables = Entities.Select(e => (e.GetSchema(), e.GetTableName())).Distinct().ToList();
        Assert.True(tables.Count >= 80, $"solo {tables.Count} tablas");
        // 96 de la V3 + product_images (V3.1) + 13 de la V4 (sucursales, integración, idempotencia) + 30 de la V4.1 (facturación)
        // + 12 de la V4.2 (edición Tecnología) + 1 de la V6 (bitácora de los armados) + 4 de la V7 (cuentas de cliente y correo)
        Assert.Equal(157, tables.Count);
        Assert.Equal(10, Schemas.All.Count);
        Assert.Equal(Schemas.All.OrderBy(s => s), tables.Select(t => t.Item1!).Distinct().OrderBy(s => s));
        Assert.Equal(27, tables.Count(t => t.Item1 == Schemas.Billing));
        Assert.Equal(2, tables.Count(t => t.Item1 == Schemas.Service));
        Assert.Equal(10, tables.Count(t => t.Item1 == Schemas.Integration));   // V7: + outgoing_mails, outgoing_mail_dispatch y outgoing_mail_attempts
    }

    /// <summary>
    /// V4.1 · Las listas explícitas de las migraciones (regla B-15) coinciden con el modelo: toda tabla de sucursal tiene
    /// su política <c>branch_isolation</c> y todo libro su trigger append-only, y las tablas nuevas de la V4.1 son
    /// exactamente las que reciben los privilegios de los roles de aplicación.
    /// </summary>
    [Fact]
    public void Las_listas_de_las_migraciones_coinciden_con_el_modelo()
    {
        Assert.Equal(Entities.Where(e => typeof(IBranchScoped).IsAssignableFrom(e.ClrType)).Select(Name).Order(), AllBranchTables.Order());
        Assert.Equal(Entities.Where(e => typeof(IInterBranch).IsAssignableFrom(e.ClrType)).Select(Name).Order(), AllInterBranchTables.Order());
        Assert.Equal(Entities.Where(e => typeof(IAppendOnly).IsAssignableFrom(e.ClrType)).Select(Name).Order(), AllAppendOnlyTables.Order());
        var v41 = Persistence.Migrations.V41SiatBilling.NewTablesV41;
        Assert.Equal(v41.Length, v41.Distinct().Count());
        Assert.Equal(Entities.Where(e => e.GetSchema() == Schemas.Billing).Select(Name)
                .Concat(new[] { "sales.sales_returns", "sales.sales_return_lines", "purchasing.supplier_invoice_fiscal" }).Order(),
            v41.Order());
        Assert.All(Persistence.Migrations.V41SiatBilling.BranchTablesV41.Concat(Persistence.Migrations.V41SiatBilling.AppendOnlyTablesV41),
            t => Assert.Contains(t, v41));
        // V4.2 · las tablas nuevas son exactamente las que el modelo tiene y la migración anterior no
        var v42 = Persistence.Migrations.V42TechRetail.NewTablesV42;
        var before = new Persistence.Migrations.V41CafcNumbering().TargetModel.GetEntityTypes()
            .Select(e => $"{e.GetSchema()}.{e.GetTableName()}").ToHashSet(StringComparer.Ordinal);
        Assert.Equal(v42.Length, v42.Distinct().Count());
        var sinceV6 = Persistence.Migrations.V6Storefront.NewTablesV6.Concat(Persistence.Migrations.V7WebPlatform.NewTablesV7).ToList();
        Assert.Equal(Entities.Select(Name).Where(t => !before.Contains(t) && !sinceV6.Contains(t)).Order(), v42.Order());
        Assert.All(Persistence.Migrations.V42TechRetail.BranchTablesV42.Concat(Persistence.Migrations.V42TechRetail.InterBranchTablesV42)
            .Concat(Persistence.Migrations.V42TechRetail.AppendOnlyTablesV42), t => Assert.Contains(t, v42));
        // V6 · la tabla nueva es exactamente la que el modelo tiene y la migración V4.2 no (sin contar las de la V7)
        var v6 = Persistence.Migrations.V6Storefront.NewTablesV6;
        var v7 = Persistence.Migrations.V7WebPlatform.NewTablesV7;
        var beforeV6 = new Persistence.Migrations.V42TechRetail().TargetModel.GetEntityTypes()
            .Select(e => $"{e.GetSchema()}.{e.GetTableName()}").ToHashSet(StringComparer.Ordinal);
        Assert.Equal(Entities.Select(Name).Where(t => !beforeV6.Contains(t) && !v7.Contains(t)).Order(), v6.Order());
        Assert.All(Persistence.Migrations.V6Storefront.BranchTablesV6.Concat(Persistence.Migrations.V6Storefront.AppendOnlyTablesV6),
            t => Assert.Contains(t, v6));
        // V7 · las tablas nuevas son exactamente las que el modelo tiene y la migración V6 no
        var beforeV7 = new Persistence.Migrations.V6Storefront().TargetModel.GetEntityTypes()
            .Select(e => $"{e.GetSchema()}.{e.GetTableName()}").ToHashSet(StringComparer.Ordinal);
        Assert.Equal(v7.Length, v7.Distinct().Count());
        Assert.Equal(Entities.Select(Name).Where(t => !beforeV7.Contains(t)).Order(), v7.Order());
        Assert.All(Persistence.Migrations.V7WebPlatform.BranchTablesV7.Concat(Persistence.Migrations.V7WebPlatform.AppendOnlyTablesV7),
            t => Assert.Contains(t, v7));
    }

    /// <summary>V7 · La migración siembra en las empresas existentes los mismos permisos de la cuenta de cliente, la misma matriz
    /// rol-permiso y el mismo rol CLIENTE que el aprovisionamiento.</summary>
    [Fact]
    public void Los_datos_de_la_plataforma_web_de_la_migracion_coinciden_con_el_dominio()
    {
        var account = PermissionCodes.All.Where(p => p.Code.StartsWith(PermissionCodes.AccountPrefix, StringComparison.Ordinal)).ToList();
        Assert.Equal(2, account.Count);
        Assert.Equal(account.OrderBy(p => p.Code), Persistence.Migrations.V7WebPlatform.AccountPermissions.OrderBy(p => p.Code));
        var expected = RoleCodes.All.SelectMany(r => PermissionCodes.ForRole(r.Code)
            .Where(p => p.StartsWith(PermissionCodes.AccountPrefix, StringComparison.Ordinal)).Select(p => (r.Code, p))).Order();
        Assert.Equal(expected, Persistence.Migrations.V7WebPlatform.AccountRolePermissions.Order());
        Assert.Equal((RoleCodes.Customer, RoleCodes.All.Single(r => r.Code == RoleCodes.Customer).Name),
            (Persistence.Migrations.V7WebPlatform.CustomerRoleCode, Persistence.Migrations.V7WebPlatform.CustomerRoleName));
        // El rol CLIENTE solo tiene los permisos de su cuenta (lo que la guardia de la migración exige a un CLIENTE que ya existiera)
        Assert.All(PermissionCodes.ForRole(RoleCodes.Customer), p => Assert.StartsWith(PermissionCodes.AccountPrefix, p, StringComparison.Ordinal));
        // La reversa pone las piezas sin ranura en una ranura que existe
        Assert.Equal(nameof(Domain.Catalog.PcSlot.Peripheral), Persistence.Migrations.V7WebPlatform.DowngradeSlot);
    }

    /// <summary>V7 · El correo pedido es de la sucursal de su reserva y append-only, igual que sus intentos; la cola es la única tabla
    /// mutable del correo (OCC, de la empresa); la cuenta de cliente une un usuario y un cliente 1 a 1 (de la empresa).</summary>
    [Fact]
    public void El_correo_y_las_cuentas_de_cliente_respetan_sucursal_empresa_y_unicidad()
    {
        Assert.True(typeof(IBranchScoped).IsAssignableFrom(typeof(Domain.Integration.OutgoingMail))
                    && typeof(IAppendOnly).IsAssignableFrom(typeof(Domain.Integration.OutgoingMail)));
        Assert.True(typeof(IAppendOnly).IsAssignableFrom(typeof(Domain.Integration.OutgoingMailAttempt)));
        Assert.False(typeof(IBranchScoped).IsAssignableFrom(typeof(Domain.Integration.OutgoingMailAttempt)));
        Assert.True(typeof(IConcurrencyAware).IsAssignableFrom(typeof(Domain.Integration.OutgoingMailDispatch)));
        Assert.False(typeof(IBranchScoped).IsAssignableFrom(typeof(Domain.Integration.OutgoingMailDispatch))
                     || typeof(IAppendOnly).IsAssignableFrom(typeof(Domain.Integration.OutgoingMailDispatch)));
        Assert.False(typeof(IBranchScoped).IsAssignableFrom(typeof(CustomerAccount)));
        var mails = Model.FindEntityType(typeof(Domain.Integration.OutgoingMail))!;
        Assert.Contains(mails.GetForeignKeys(), fk => fk.PrincipalEntityType.ClrType == typeof(PcBuild)
                                                      && fk.Properties.Select(p => p.Name).SequenceEqual(["TenantId", "BranchId", "PcBuildId"]));
        var dispatch = Model.FindEntityType(typeof(Domain.Integration.OutgoingMailDispatch))!;
        Assert.Contains(dispatch.GetIndexes(), i => i.GetFilter() == "status = 'Pending'");
        var attempts = Model.FindEntityType(typeof(Domain.Integration.OutgoingMailAttempt))!;
        Assert.Contains(attempts.GetIndexes(), i => i.IsUnique && i.Properties.Select(p => p.Name)
            .SequenceEqual([nameof(Domain.Integration.OutgoingMailAttempt.OutgoingMailId), nameof(Domain.Integration.OutgoingMailAttempt.Attempt)]));
        var accounts = Model.FindEntityType(typeof(CustomerAccount))!;
        Assert.Equal("sales.customer_accounts", Name(accounts));
        Assert.Contains(accounts.GetIndexes(), i => i.IsUnique && i.Properties.Select(p => p.Name)
            .SequenceEqual([nameof(CustomerAccount.TenantId), nameof(CustomerAccount.UserId)]));
        Assert.Contains(accounts.GetIndexes(), i => i.IsUnique && i.Properties.Select(p => p.Name)
            .SequenceEqual([nameof(CustomerAccount.TenantId), nameof(CustomerAccount.CustomerId)]));
    }

    /// <summary>V7 · El SQL propio de la migración (sin PostgreSQL: el script que genera EF): la guardia va primero, el relleno del
    /// tipo antes de sus CHECK y los triggers del carrito después del relleno; la función del despachador es SECURITY DEFINER con
    /// search_path fijo, sin EXECUTE para PUBLIC y con EXECUTE solo para minv_server; y la reversa deja la V6 como estaba.</summary>
    [Fact]
    public void La_migracion_V7_rellena_el_tipo_protege_el_carrito_y_crea_la_funcion_del_correo()
    {
        var migrator = Db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
        static string Lf(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var up = Lf(migrator.GenerateScript("20260927173304_V6Storefront", "20260929025923_V7WebPlatform"));
        var down = Lf(migrator.GenerateScript("20260929025923_V7WebPlatform", "20260927173304_V6Storefront"));
        int At(string script, string fragment)
        {
            var index = script.IndexOf(fragment, StringComparison.Ordinal);
            Assert.True(index >= 0, $"Falta en el script: {fragment}");
            return index;
        }
        Assert.True(At(up, "M-INV V7: % empresas ya tienen un rol CLIENTE") < At(up, "ADD kind character varying(10) NOT NULL DEFAULT ''"));
        Assert.True(At(up, "UPDATE sales.pc_builds SET kind = 'Build' WHERE kind = ''") < At(up, "ADD CONSTRAINT ck_pc_builds_tipo CHECK"));
        Assert.True(At(up, "ALTER COLUMN kind DROP DEFAULT") < At(up, "CREATE TRIGGER trg_pc_build_kind_immutable BEFORE UPDATE OF kind ON sales.pc_builds"));
        At(up, "CREATE TRIGGER trg_pc_build_line_slot BEFORE INSERT OR UPDATE OF slot, pc_build_id ON sales.pc_build_lines");
        At(up, "FOR EACH ROW WHEN (NEW.slot IS NULL) EXECUTE FUNCTION sales.minv_pc_build_line_slot()");
        At(up, "FOR EACH ROW WHEN (OLD.kind IS DISTINCT FROM NEW.kind) EXECUTE FUNCTION sales.minv_pc_build_kind_immutable()");
        At(up, "CREATE OR REPLACE FUNCTION integration.claim_outgoing_mails(p_limit integer, p_lease_seconds integer)");
        At(up, "LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, integration AS $$\n#variable_conflict use_column");
        At(up, "FOR UPDATE SKIP LOCKED)");
        At(up, "REVOKE ALL ON FUNCTION integration.claim_outgoing_mails(integer, integer) FROM PUBLIC;");
        At(up, "GRANT EXECUTE ON FUNCTION integration.claim_outgoing_mails(integer, integer) TO minv_server;");
        Assert.DoesNotContain("TO minv_app", up[At(up, "GRANT EXECUTE ON FUNCTION integration.claim_outgoing_mails")..], StringComparison.Ordinal);
        foreach (var table in Persistence.Migrations.V7WebPlatform.AppendOnlyTablesV7)
        {
            At(up, $"CREATE TRIGGER trg_append_only BEFORE UPDATE OR DELETE ON {table}");
            At(up, $"CREATE TRIGGER trg_append_only_truncate BEFORE TRUNCATE ON {table}");
        }
        At(up, "CREATE POLICY branch_isolation ON integration.outgoing_mails AS RESTRICTIVE");
        At(up, "ADD CONSTRAINT ck_audit_logs_canal CHECK (channel IS NULL OR channel IN ('desktop', 'cloud', 'api', 'storefront', 'web'))");
        At(up, "SELECT gen_random_uuid(), t.id, 'CLIENTE', 'Cliente web', true");
        // Reversa: guardia de la auditoría web, triggers y función fuera, piezas sin ranura a Periféricos y sin valor por defecto
        Assert.True(At(down, "no se puede volver a la V6") < At(down, "DROP TABLE sales.customer_accounts"));
        Assert.True(At(down, "UPDATE sales.pc_build_lines SET slot = 'Peripheral' WHERE slot IS NULL") <
                    At(down, "ALTER TABLE sales.pc_build_lines ALTER COLUMN slot SET NOT NULL"));
        Assert.True(At(down, "DROP TRIGGER IF EXISTS trg_pc_build_kind_immutable ON sales.pc_builds") <
                    At(down, "ALTER TABLE sales.pc_builds DROP COLUMN kind"));
        Assert.True(At(down, "ALTER COLUMN slot SET DEFAULT ''") < At(down, "ALTER TABLE sales.pc_build_lines ALTER COLUMN slot DROP DEFAULT"));
        At(down, "DROP FUNCTION IF EXISTS integration.claim_outgoing_mails(integer, integer)");
        At(down, "DELETE FROM iam.roles WHERE code = 'CLIENTE'");
    }

    /// <summary>V7 · El arrendamiento del correo va en una migración NUEVA (la V7WebPlatform publicada no se edita, A-07): agrega la
    /// columna y su CHECK y, como la función cambia el tipo que devuelve, la borra antes de crearla de nuevo con SECURITY DEFINER,
    /// search_path fijo, sin EXECUTE para PUBLIC y con EXECUTE solo para minv_server (B-13); la reversa deja la función de la
    /// V7WebPlatform con los mismos privilegios.</summary>
    [Fact]
    public void La_migracion_del_arrendamiento_del_correo_recrea_la_funcion_con_sus_privilegios()
    {
        var migrator = Db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
        static string Lf(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var v7 = Lf(migrator.GenerateScript("20260927173304_V6Storefront", "20260929025923_V7WebPlatform"));
        var up = Lf(migrator.GenerateScript("20260929025923_V7WebPlatform", "20260930211805_V7MailLease"));
        var down = Lf(migrator.GenerateScript("20260930211805_V7MailLease", "20260929025923_V7WebPlatform"));
        int At(string script, string fragment, int from = 0)
        {
            var index = script.IndexOf(fragment, from, StringComparison.Ordinal);
            Assert.True(index >= 0, $"Falta en el script: {fragment}");
            return index;
        }
        // La V7WebPlatform sigue como se publicó: sin la columna y con la función de (empresa, correo)
        Assert.DoesNotContain("leased_until", v7, StringComparison.Ordinal);
        At(v7, "RETURNS TABLE (tenant_id uuid, outgoing_mail_id uuid)\n");
        // Subida (tolerante con una base que ya tenga la columna): columna y CHECK antes de la función; DROP antes de CREATE;
        // privilegios después de crearla
        Assert.True(At(up, "ADD COLUMN IF NOT EXISTS leased_until timestamp with time zone NULL") < At(up, "CREATE FUNCTION integration.claim_outgoing_mails"));
        At(up, "DROP CONSTRAINT IF EXISTS ck_outgoing_mail_dispatch_arrendamiento;");
        At(up, "ADD CONSTRAINT ck_outgoing_mail_dispatch_arrendamiento");
        At(up, "CHECK (leased_until IS NULL OR status = 'Pending')");
        var create = At(up, "CREATE FUNCTION integration.claim_outgoing_mails(p_limit integer, p_lease_seconds integer)");
        Assert.True(At(up, "DROP FUNCTION IF EXISTS integration.claim_outgoing_mails(integer, integer);") < create);
        At(up, "RETURNS TABLE (tenant_id uuid, outgoing_mail_id uuid, leased_until timestamptz)", create);
        At(up, "LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, integration AS $$\n#variable_conflict use_column", create);
        At(up, "FOR UPDATE SKIP LOCKED)", create);
        At(up, "RETURNING d.tenant_id, d.outgoing_mail_id, d.leased_until;", create);
        var revoke = At(up, "REVOKE ALL ON FUNCTION integration.claim_outgoing_mails(integer, integer) FROM PUBLIC;", create);
        At(up, "GRANT EXECUTE ON FUNCTION integration.claim_outgoing_mails(integer, integer) TO minv_server;", revoke);
        Assert.DoesNotContain("minv_app", up, StringComparison.Ordinal);
        // Reversa: la función de la V7WebPlatform (sin arrendamiento) con sus privilegios, y después la columna fuera
        var restore = At(down, "CREATE FUNCTION integration.claim_outgoing_mails(p_limit integer, p_lease_seconds integer)");
        Assert.True(At(down, "DROP FUNCTION IF EXISTS integration.claim_outgoing_mails(integer, integer);") < restore);
        At(down, "RETURNS TABLE (tenant_id uuid, outgoing_mail_id uuid)\n", restore);
        At(down, "RETURNING d.tenant_id, d.outgoing_mail_id;", restore);
        At(down, "GRANT EXECUTE ON FUNCTION integration.claim_outgoing_mails(integer, integer) TO minv_server;", restore);
        Assert.True(restore < At(down, "DROP COLUMN leased_until"));
    }

    /// <summary>V6 · La migración siembra en las empresas existentes los mismos permisos, la misma matriz rol-permiso, el mismo rol
    /// TIENDA_WEB y el mismo usuario técnico que el aprovisionamiento.</summary>
    [Fact]
    public void Los_datos_de_la_tienda_web_de_la_migracion_coinciden_con_el_dominio()
    {
        string[] codes = [PermissionCodes.StorefrontRead, PermissionCodes.StorefrontReserve];
        Assert.Equal(PermissionCodes.All.Where(p => codes.Contains(p.Code)).OrderBy(p => p.Code),
            Persistence.Migrations.V6Storefront.StorefrontPermissions.OrderBy(p => p.Code));
        var expected = RoleCodes.All.SelectMany(r => PermissionCodes.ForRole(r.Code)
            .Where(p => codes.Contains(p) || r.Code == RoleCodes.Storefront).Select(p => (r.Code, p))).Order();
        Assert.Equal(expected, Persistence.Migrations.V6Storefront.StorefrontRolePermissions.Order());
        Assert.Equal((RoleCodes.Storefront, RoleCodes.All.Single(r => r.Code == RoleCodes.Storefront).Name),
            (Persistence.Migrations.V6Storefront.StorefrontRoleCode, Persistence.Migrations.V6Storefront.StorefrontRoleName));
        Assert.Equal(Provisioning.TenantProvisioner.StorefrontUserLocalPart, Persistence.Migrations.V6Storefront.StorefrontUserLocalPart);
        Assert.Equal(Provisioning.TenantProvisioner.StorefrontUserName, Persistence.Migrations.V6Storefront.StorefrontUserName);
        Assert.Equal("tienda-web@techzone.example", Provisioning.TenantProvisioner.StorefrontEmail("TECHZONE", "admin@techzone.example"));
        Assert.Equal("tienda-web@minv.local", Provisioning.TenantProvisioner.StorefrontEmail("MINV", "sin-dominio"));
    }

    /// <summary>V6 · El armado y su bitácora son de la sucursal; una reserva de stock tiene a lo sumo un origen (caja, línea de pedido
    /// o línea de armado) y el estado Reserved exige su vigencia.</summary>
    [Fact]
    public void La_tienda_web_respeta_sucursales_arcos_y_estados()
    {
        Assert.True(typeof(IBranchScoped).IsAssignableFrom(typeof(PcBuildEvent)) && typeof(IAppendOnly).IsAssignableFrom(typeof(PcBuildEvent)));
        var reservations = Model.FindEntityType(typeof(StockReservation))!;
        Assert.Contains(reservations.GetForeignKeys(), fk => fk.PrincipalEntityType.ClrType == typeof(PcBuildLine)
                                                             && fk.Properties.Select(p => p.Name).SequenceEqual(["TenantId", "BranchId", "PcBuildLineId"]));
        var builds = Model.FindEntityType(typeof(PcBuild))!;
        Assert.Contains(builds.GetIndexes(), i => i.GetFilter() == "status = 'Reserved'");
        Assert.Contains(builds.GetIndexes(), i => i.GetFilter() == "published_to_web");
        Assert.Null(builds.FindProperty(nameof(PcBuild.IsReservationActive)));
    }

    /// <summary>V7 · Carrito (regla P-05): el tipo y los datos para la factura de la reserva son columnas de <c>sales.pc_builds</c>
    /// con sus CHECK (tipo; datos de factura coherentes; un carrito nunca publicado) y la ranura de la línea admite nulo. Que sea
    /// nula SOLO en un carrito cruza dos tablas: lo exige el dominio y, en la base, el trigger de la migración V7.</summary>
    [Fact]
    public void El_carrito_tiene_su_tipo_sus_datos_de_factura_y_la_ranura_opcional()
    {
        var builds = Model.FindEntityType(typeof(PcBuild))!;
        var kind = builds.FindProperty(nameof(PcBuild.Kind))!;
        Assert.Equal(("kind", "character varying(10)", false), (kind.GetColumnName(), kind.GetColumnType(), kind.IsNullable));
        var type = builds.FindProperty(nameof(PcBuild.BuyerDocumentType))!;
        Assert.Equal(("buyer_document_type", "smallint", true), (type.GetColumnName(), type.GetColumnType(), type.IsNullable));
        Assert.Equal(("buyer_document_number", 20, true), Column(builds, nameof(PcBuild.BuyerDocumentNumber)));
        Assert.Equal(("buyer_complement", 5, true), Column(builds, nameof(PcBuild.BuyerComplement)));
        Assert.Equal(("buyer_name", 150, true), Column(builds, nameof(PcBuild.BuyerName)));
        Assert.Null(builds.FindProperty(nameof(PcBuild.IsCart)));
        Assert.Null(builds.FindProperty(nameof(PcBuild.HasBuyer)));
        var slot = Model.FindEntityType(typeof(PcBuildLine))!.FindProperty(nameof(PcBuildLine.Slot))!;
        Assert.Equal(("slot", "character varying(20)", true), (slot.GetColumnName(), slot.GetColumnType(), slot.IsNullable));
        foreach (var fragment in new[]
                 {
                     "CONSTRAINT ck_pc_builds_tipo CHECK (kind IN ('Build', 'Cart'))",
                     "CONSTRAINT ck_pc_builds_tipo_publicado CHECK (NOT published_to_web OR kind = 'Build')",
                     "CONSTRAINT ck_pc_builds_factura_tipo CHECK (buyer_document_type IS NULL OR buyer_document_type BETWEEN 1 AND 5)",
                     "CONSTRAINT ck_pc_builds_factura_documento CHECK ((buyer_document_type IS NULL) = (buyer_document_number IS NULL))",
                     "CONSTRAINT ck_pc_builds_factura_complemento CHECK (buyer_complement IS NULL OR buyer_document_type = 1)",
                     "CONSTRAINT ck_pc_builds_factura_nombre CHECK (buyer_name IS NULL OR buyer_document_type IS NOT NULL)",
                     "CONSTRAINT ck_pc_build_lines_ranura CHECK (slot IS NULL OR slot IN ('Cpu', 'Motherboard', 'Ram', 'Gpu', 'Storage', 'Psu', 'Case', " +
                     "'Cooler', 'Monitor', 'Peripheral', 'Software', 'Service'))",
                     "kind character varying(10) NOT NULL",
                     "slot character varying(20),",
                 })
        {
            Assert.Contains(fragment, Ddl, StringComparison.Ordinal);
        }
        // Sin tablas nuevas: el carrito reutiliza las del armado (el recuento de tablas lo vigila El_modelo_tiene_…)
        Assert.Equal("sales.pc_builds", Name(builds));

        static (string Column, int? MaxLength, bool Nullable) Column(IEntityType entity, string property)
        {
            var p = entity.FindProperty(property)!;
            return (p.GetColumnName(), p.GetMaxLength(), p.IsNullable);
        }
    }

    /// <summary>Tablas de sucursal de todas las migraciones (V4 + V4.1 + V4.2 + V6 + V7).</summary>
    internal static IEnumerable<string> AllBranchTables =>
        Persistence.Migrations.V4MultiBranchCloud.BranchTables.Concat(Persistence.Migrations.V41SiatBilling.BranchTablesV41)
            .Concat(Persistence.Migrations.V42TechRetail.BranchTablesV42).Concat(Persistence.Migrations.V6Storefront.BranchTablesV6)
            .Concat(Persistence.Migrations.V7WebPlatform.BranchTablesV7);

    /// <summary>Tablas entre sucursales de todas las migraciones (V4 + V4.2).</summary>
    internal static IEnumerable<string> AllInterBranchTables =>
        Persistence.Migrations.V4MultiBranchCloud.InterBranchTables.Concat(Persistence.Migrations.V42TechRetail.InterBranchTablesV42);

    /// <summary>Libros append-only de todas las migraciones (V3 + V4 + V4.1 + V4.2 + V6 + V7).</summary>
    internal static IEnumerable<string> AllAppendOnlyTables =>
        Persistence.Migrations.GuardsRlsAndViews.AppendOnlyTables.Concat(Persistence.Migrations.V4MultiBranchCloud.AppendOnlyTablesV4)
            .Concat(Persistence.Migrations.V41SiatBilling.AppendOnlyTablesV41).Concat(Persistence.Migrations.V42TechRetail.AppendOnlyTablesV42)
            .Concat(Persistence.Migrations.V6Storefront.AppendOnlyTablesV6).Concat(Persistence.Migrations.V7WebPlatform.AppendOnlyTablesV7);

    /// <summary>V4.2 · La migración siembra en las empresas existentes los mismos permisos, la misma matriz rol-permiso, el
    /// mismo tipo de movimiento de reposición por garantía y la misma cuenta 5.1.10 que el aprovisionamiento.</summary>
    [Fact]
    public void Los_datos_de_la_edicion_tecnologia_de_la_migracion_coinciden_con_el_dominio()
    {
        string[] codes =
        [
            PermissionCodes.SpecsManage, PermissionCodes.SerialsView, PermissionCodes.SerialsManage, PermissionCodes.ServiceOpen,
            PermissionCodes.ServiceManage, PermissionCodes.PcBuildManage,
        ];
        Assert.Equal(PermissionCodes.All.Where(p => codes.Contains(p.Code)).OrderBy(p => p.Code),
            Persistence.Migrations.V42TechRetail.TechPermissions.OrderBy(p => p.Code));
        var expected = RoleCodes.All.SelectMany(r => PermissionCodes.ForRole(r.Code).Where(codes.Contains).Select(p => (r.Code, p))).Order();
        Assert.Equal(expected, Persistence.Migrations.V42TechRetail.TechRolePermissions.Order());
        var type = MovementType.CreateDefaults(Guid.NewGuid()).Single(t => t.Code == MovementTypeCodes.WarrantyReplacement);
        Assert.Equal((Persistence.Migrations.V42TechRetail.WarrantyMovementCode, Persistence.Migrations.V42TechRetail.WarrantyMovementName,
                Persistence.Migrations.V42TechRetail.WarrantyMovementDescription, (short)-1, MovementDomain.Warehouse),
            (type.Code, type.Name, type.Description, type.StockFactor, type.Domain));
        var account = ChartOfAccounts.Defaults.Single(a => a.Code == AccountCodes.WarrantyCost);
        Assert.Equal((Persistence.Migrations.V42TechRetail.WarrantyAccountCode, Persistence.Migrations.V42TechRetail.WarrantyAccountName,
            AccountType.Expense, (string?)"5.1", true), (account.Code, account.Name, account.Type, account.Parent, account.Postable));
    }

    /// <summary>V4.2 · Lo que ocurre en una sucursal (series vendidas o devueltas, armados, casos RMA) es de la sucursal; la
    /// serie, su bitácora y las fichas técnicas son de la empresa (la serie viaja entre sucursales). Unicidades clave y
    /// garantía derivada (ninguna columna guarda un fin de garantía, regla T-04).</summary>
    [Fact]
    public void La_edicion_tecnologia_respeta_sucursales_y_unicidades()
    {
        var tech = new[]
        {
            typeof(Domain.Catalog.SpecDefinition), typeof(Domain.Catalog.SpecOption), typeof(Domain.Catalog.ProductSpecValue),
            typeof(Domain.Catalog.ProductTechProfile), typeof(SerialNumber), typeof(SerialEvent), typeof(StockTransferLineSerial),
            typeof(SalesOrderLineSerial), typeof(SalesReturnLineSerial), typeof(PcBuild), typeof(PcBuildLine), typeof(WarrantyClaim),
            typeof(WarrantyClaimEvent),
        };
        Assert.Equal(new[] { "PcBuild", "PcBuildLine", "SalesOrderLineSerial", "SalesReturnLineSerial", "WarrantyClaim", "WarrantyClaimEvent" },
            tech.Where(t => typeof(IBranchScoped).IsAssignableFrom(t)).Select(t => t.Name).Order());
        Assert.True(typeof(IBranchScoped).IsAssignableFrom(typeof(PcBuildEvent)));   // V6
        var serials = Model.FindEntityType(typeof(SerialNumber))!;
        Assert.Contains(serials.GetIndexes(), i => i.IsUnique && i.Properties.Select(p => p.Name)
            .SequenceEqual([nameof(SerialNumber.TenantId), nameof(SerialNumber.VariantId), nameof(SerialNumber.Serial)]));
        var claims = Model.FindEntityType(typeof(WarrantyClaim))!;
        Assert.Contains(claims.GetIndexes(), i => i.IsUnique && i.Properties.Select(p => p.Name)
            .SequenceEqual([nameof(WarrantyClaim.TenantId), nameof(WarrantyClaim.BranchId), nameof(WarrantyClaim.Number)]));
        Assert.True(Assert.Single(claims.GetIndexes(), i => i.GetFilter() == "status <> 'Delivered'").IsUnique);
        Assert.Contains(Model.FindEntityType(typeof(PcBuild))!.GetIndexes(), i => i.IsUnique && i.Properties.Select(p => p.Name)
            .SequenceEqual([nameof(PcBuild.TenantId), nameof(PcBuild.BranchId), nameof(PcBuild.Number)]));
        Assert.All(new[] { typeof(SerialNumber), typeof(WarrantyClaim), typeof(PcBuild), typeof(Domain.Catalog.ProductTechProfile) },
            t => Assert.True(typeof(IConcurrencyAware).IsAssignableFrom(t), t.Name));
        Assert.DoesNotContain(tech.SelectMany(t => Model.FindEntityType(t)!.GetProperties()),
            p => p.GetColumnName().Contains("warranty_until", StringComparison.Ordinal));
    }

    /// <summary>V4.1 · La migración siembra en las empresas existentes los mismos permisos y la misma matriz rol-permiso de
    /// la facturación que el aprovisionamiento de una empresa nueva (<see cref="PermissionCodes"/>).</summary>
    [Fact]
    public void Los_permisos_de_facturacion_de_la_migracion_coinciden_con_el_dominio()
    {
        var billing = PermissionCodes.All.Where(p => p.Code.StartsWith("billing.", StringComparison.Ordinal)).ToList();
        Assert.Equal(5, billing.Count);
        Assert.Equal(billing.OrderBy(p => p.Code), Persistence.Migrations.V41SiatBilling.BillingPermissions.OrderBy(p => p.Code));
        var expected = RoleCodes.All
            .SelectMany(r => PermissionCodes.ForRole(r.Code).Where(p => p.StartsWith("billing.", StringComparison.Ordinal)).Select(p => (r.Code, p)))
            .Order();
        Assert.Equal(expected, Persistence.Migrations.V41SiatBilling.BillingRolePermissions.Order());
        Assert.Contains(LicenseModule.Catalog(), m => m.Code == LicenseModuleCodes.FiscalSiat);
    }

    /// <summary>V4 / V4.1 · Regla B-02: una FK entre dos tablas de sucursal incluye <c>branch_id</c> (un hijo, una línea o
    /// un documento que usa un CUIS, un CUFD o un punto de venta no puede ser de otra sucursal).</summary>
    [Fact]
    public void Las_FK_entre_tablas_de_sucursal_incluyen_la_sucursal()
    {
        foreach (var fk in Entities.SelectMany(e => e.GetForeignKeys()))
        {
            if (typeof(IBranchScoped).IsAssignableFrom(fk.DeclaringEntityType.ClrType)
                && typeof(IBranchScoped).IsAssignableFrom(fk.PrincipalEntityType.ClrType))
            {
                Assert.True(fk.Properties.Any(p => p.Name == nameof(IBranchScoped.BranchId))
                            && fk.PrincipalKey.Properties.Any(p => p.Name == nameof(IBranchScoped.BranchId)),
                    $"{fk.DeclaringEntityType.ClrType.Name} → {fk.PrincipalEntityType.ClrType.Name}: FK sin branch_id");
            }
        }
    }

    [Fact]
    public void Toda_entidad_salvo_la_plataforma_tiene_TenantId_filtro_global_y_FK_al_tenant()
    {
        foreach (var e in Entities)
        {
            if (typeof(PlatformEntity).IsAssignableFrom(e.ClrType))
            {
                continue;
            }
            Assert.True(typeof(ITenantScoped).IsAssignableFrom(e.ClrType), $"{e.ClrType.Name} no tiene TenantId");
            Assert.NotNull(e.GetQueryFilter());
            Assert.Contains(e.GetForeignKeys(), fk => fk.PrincipalEntityType.ClrType.Name == "Tenant");
        }
        Assert.Equal(2, Entities.Count(e => typeof(PlatformEntity).IsAssignableFrom(e.ClrType)));
    }

    [Fact]
    public void Las_FK_entre_entidades_de_negocio_incluyen_el_tenant_para_no_mezclar_empresas()
    {
        foreach (var fk in Entities.SelectMany(e => e.GetForeignKeys()))
        {
            var principal = fk.PrincipalEntityType.ClrType;
            if (typeof(PlatformEntity).IsAssignableFrom(principal))
            {
                continue;
            }
            // V3: (tenant_id, x_id). V4: (tenant_id, branch_id, x_id) entre filas de una sucursal y
            // (tenant_id, from_branch_id, to_branch_id, x_id) entre las filas de una transferencia.
            var names = fk.Properties.Select(p => p.Name).ToList();
            Assert.True(names.Contains(nameof(ITenantScoped.TenantId)) && fk.PrincipalKey.Properties.Any(p => p.Name == nameof(ITenantScoped.TenantId)),
                $"{fk.DeclaringEntityType.ClrType.Name} → {principal.Name}: FK sin tenant_id");
            // V4.1 · Clave natural del catálogo sincronizado: (tenant_id, activity_code, product_code) de billing.siat_products
            if (fk.PrincipalKey.Properties.All(p => p.Name != nameof(Entity.Id)))
            {
                Assert.Equal((nameof(ProductSiatCode), nameof(SiatProduct)), (fk.DeclaringEntityType.ClrType.Name, principal.Name));
                continue;
            }
            // (la FK a la propia sucursal, p. ej. centro de costo → sucursal, es (tenant_id, branch_id): ahí branch_id es la referencia)
            var branchColumns = principal.Name == "Branch" ? 0 : names.Count(n => n is "BranchId" or "FromBranchId" or "ToBranchId");
            // V4.2 · FK compuestas con el padre (documentadas en el ERD): la opción es de ESA especificación, el lote de la serie
            // es de SU variante y la existencia donde está es de SU lote
            var parentColumn = (fk.DeclaringEntityType.ClrType.Name, principal.Name) switch
            {
                ("ProductSpecValue", "SpecOption") => "SpecDefinitionId",
                ("SerialNumber", "Batch") => "VariantId",
                ("SerialNumber", "StockLevel") => "BatchId",
                _ => null,
            };
            var extra = parentColumn is not null && names.Contains(parentColumn)
                        && fk.PrincipalKey.Properties.Any(p => p.Name == parentColumn) ? 1 : 0;
            Assert.True(fk.Properties.Count == 2 + branchColumns + extra && branchColumns <= 2,
                $"{fk.DeclaringEntityType.ClrType.Name} → {principal.Name}: FK sin tenant_id");
        }
    }

    [Fact]
    public void Las_tablas_transaccionales_usan_xmin_como_token_de_concurrencia()
    {
        var conc = Entities.Where(e => typeof(IConcurrencyAware).IsAssignableFrom(e.ClrType)).ToList();
        Assert.Contains(conc, e => e.ClrType.Name == "StockLevel");
        Assert.Contains(conc, e => e.ClrType.Name == "PosSession");
        foreach (var e in conc)
        {
            var p = e.FindProperty(nameof(IConcurrencyAware.RowVersion))!;
            Assert.True(p.IsConcurrencyToken, e.ClrType.Name);
            Assert.Equal("xmin", p.GetColumnName());
        }
        Assert.DoesNotContain("row_version", Ddl, StringComparison.Ordinal);
    }

    [Fact]
    public void Los_libros_mayores_son_append_only()
    {
        var appendOnly = Entities.Where(e => typeof(IAppendOnly).IsAssignableFrom(e.ClrType)).Select(e => e.ClrType.Name).OrderBy(n => n);
        Assert.Equal(new[]
        {
            "AccessLog", "AuditLog", "AverageCostHistory", "CashMovement", "CustomerNitCheck", "ExchangeRate", "ExternalOrder",
            "FiscalDelivery", "FiscalDocumentEvent", "FiscalDocumentFile", "FiscalDocumentLine", "OutboxEvent", "OutgoingMail", "OutgoingMailAttempt",
            "Payment", "PcBuildEvent", "ProcessedRequest", "SalesOrderLineSerial", "SalesReturnLineSerial", "SerialEvent", "SiatCufd", "SiatCuis",
            "SiatServiceCall",
            "SiatSyncRun", "StockMovement", "StockTransferDiscrepancy", "StockTransferEvent", "StockTransferLineBatch", "StockTransferLineSerial",
            "StockTransferMovement", "WarrantyClaimEvent", "WebhookDelivery",
        }, appendOnly);
    }

    /// <summary>V4.1 · Todo lo operativo de la facturación es de una sucursal (regla F-14); la configuración de la empresa,
    /// los catálogos, la homologación y la bitácora SOAP no.</summary>
    [Fact]
    public void La_facturacion_operativa_es_por_sucursal()
    {
        var branch = Entities.Where(e => e.GetSchema() == Schemas.Billing && typeof(IBranchScoped).IsAssignableFrom(e.ClrType))
            .Select(e => e.ClrType.Name).Order();
        Assert.Equal(new[]
        {
            "ContingencyCode", "FiscalDelivery", "FiscalDocument", "FiscalDocumentEvent", "FiscalDocumentFile", "FiscalDocumentLine",
            "FiscalNoteReference", "FiscalPackage", "SiatCufd", "SiatCuis", "SiatPointOfSale", "SignificantEvent",
        }, branch);
        var documents = Model.FindEntityType(typeof(FiscalDocument))!;
        Assert.Equal("timestamp without time zone", documents.FindProperty(nameof(FiscalDocument.IssuedAt))!.GetColumnType());
        var active = Assert.Single(documents.GetIndexes(), i => i.GetFilter()?.Contains("'Pending'", StringComparison.Ordinal) == true);
        Assert.True(active.IsUnique);
        Assert.Equal(new[] { nameof(FiscalDocument.TenantId), nameof(FiscalDocument.InvoiceId) }, active.Properties.Select(p => p.Name));
        Assert.Contains("'Pending', 'Valid', 'Offline', 'InPackage'", active.GetFilter(), StringComparison.Ordinal);
        // Numeración electrónica correlativa (sin CAFC) y numeración de cada talonario CAFC por separado
        Assert.Contains(documents.GetIndexes(), i => i.IsUnique && i.GetFilter() == "cafc IS NULL" && i.Properties.Select(p => p.Name).SequenceEqual(
            [nameof(FiscalDocument.TenantId), nameof(FiscalDocument.Environment), nameof(FiscalDocument.PointOfSaleId),
             nameof(FiscalDocument.DocumentSector), nameof(FiscalDocument.Number)]));
        Assert.Contains(documents.GetIndexes(), i => i.IsUnique && i.GetFilter() == "cafc IS NOT NULL" && i.Properties.Select(p => p.Name).SequenceEqual(
            [nameof(FiscalDocument.TenantId), nameof(FiscalDocument.Environment), nameof(FiscalDocument.PointOfSaleId),
             nameof(FiscalDocument.DocumentSector), nameof(FiscalDocument.Cafc), nameof(FiscalDocument.Number)]));
        Assert.Contains(documents.GetIndexes(), i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(
            [nameof(FiscalDocument.TenantId), nameof(FiscalDocument.Cuf)]));
        var line = Model.FindEntityType(typeof(FiscalDocumentLine))!;
        Assert.Equal("numeric(20,10)", line.FindProperty(nameof(FiscalDocumentLine.Quantity))!.GetColumnType());
        Assert.Equal("numeric(20,10)", line.FindProperty(nameof(FiscalDocumentLine.UnitPrice))!.GetColumnType());
        Assert.Equal("smallint", Model.FindEntityType(typeof(MINV.Domain.Sales.Customer))!
            .FindProperty(nameof(MINV.Domain.Sales.Customer.DocumentType))!.GetColumnType());
    }

    [Fact]
    public void Los_identificadores_caben_en_PostgreSQL_y_estan_en_snake_case()
    {
        foreach (var e in Entities)
        {
            Assert.Matches("^[a-z][a-z0-9_]*$", e.GetTableName()!);
            foreach (var p in e.GetProperties())
            {
                Assert.Matches("^[a-z][a-z0-9_]*$", p.GetColumnName());
            }
            foreach (var name in e.GetKeys().Select(k => k.GetName()!)
                         .Concat(e.GetForeignKeys().Select(f => f.GetConstraintName()!))
                         .Concat(e.GetIndexes().Select(i => i.GetDatabaseName()!)))
            {
                Assert.True(name.Length <= 63, $"{name} supera 63 caracteres");
            }
        }
    }

    [Theory]
    [InlineData("CREATE SCHEMA inventory;")]
    [InlineData("CREATE TABLE inventory.stock_movements")]
    [InlineData("CONSTRAINT ck_stock_levels_existencia CHECK (quantity_on_hand >= 0)")]
    [InlineData("CONSTRAINT ck_movement_types_factor CHECK (stock_factor IN (-1, 1))")]
    [InlineData("CONSTRAINT ck_journal_lines_partida CHECK ((debit > 0 AND credit = 0) OR (credit > 0 AND debit = 0))")]
    [InlineData("WHERE status = 'Open'")]
    [InlineData("NULLS NOT DISTINCT")]
    [InlineData("quantity numeric(18,6) NOT NULL")]
    [InlineData("REFERENCES catalog.product_variants (tenant_id, id)")]
    [InlineData("CREATE SCHEMA billing;")]
    [InlineData("CREATE TABLE billing.fiscal_documents")]
    [InlineData("issued_at timestamp without time zone NOT NULL")]
    [InlineData("quantity numeric(20,10) NOT NULL")]
    [InlineData("CONSTRAINT ck_fiscal_documents_sector CHECK (document_sector IN (1, 24))")]
    [InlineData("CONSTRAINT ck_customers_complemento CHECK (complement IS NULL OR document_type = 1)")]
    [InlineData("REFERENCES billing.siat_points_of_sale (tenant_id, branch_id, id)")]
    [InlineData("REFERENCES billing.siat_products (tenant_id, activity_code, product_code)")]
    [InlineData("REFERENCES sales.pos_registers (tenant_id, branch_id, id)")]
    [InlineData("CREATE TABLE sales.sales_returns")]
    [InlineData("CREATE TABLE purchasing.supplier_invoice_fiscal")]
    [InlineData("CREATE SCHEMA service;")]
    [InlineData("CREATE TABLE service.warranty_claims")]
    [InlineData("CONSTRAINT ck_product_spec_values_arco CHECK (num_nonnulls(number_value, text_value, option_id) = 1)")]
    [InlineData("REFERENCES catalog.spec_options (tenant_id, spec_definition_id, id)")]
    [InlineData("REFERENCES inventory.batches (tenant_id, variant_id, id)")]
    [InlineData("REFERENCES inventory.stock_levels (tenant_id, batch_id, id)")]
    [InlineData("REFERENCES inventory.stock_transfer_lines (tenant_id, from_branch_id, to_branch_id, id)")]
    [InlineData("REFERENCES sales.sales_order_lines (tenant_id, branch_id, id)")]
    [InlineData("REFERENCES service.warranty_claims (tenant_id, branch_id, id)")]
    [InlineData("CONSTRAINT ck_serial_numbers_ubicacion CHECK ((status IN ('InStock', 'Reserved')) = (stock_level_id IS NOT NULL))")]
    [InlineData("CONSTRAINT ck_warranty_claims_cierre CHECK ((status = 'Delivered') = (closed_at IS NOT NULL))")]
    [InlineData("CREATE TABLE sales.pc_build_events")]
    [InlineData("CONSTRAINT ck_stock_reservations_origen CHECK (num_nonnulls(pos_session_id, sales_order_line_id, pc_build_line_id) <= 1)")]
    [InlineData("CONSTRAINT ck_pc_builds_reserva CHECK (status <> 'Reserved' OR (reserved_at IS NOT NULL AND reserved_until IS NOT NULL))")]
    [InlineData("CONSTRAINT ck_pc_builds_contacto CHECK (channel <> 'Web' OR (contact_name IS NOT NULL AND contact_phone IS NOT NULL))")]
    [InlineData("CONSTRAINT ck_audit_logs_canal CHECK (channel IS NULL OR channel IN ('desktop', 'cloud', 'api', 'storefront', 'web'))")]
    [InlineData("REFERENCES sales.pc_build_lines (tenant_id, branch_id, id)")]
    [InlineData("CREATE TABLE sales.customer_accounts")]
    [InlineData("CREATE UNIQUE INDEX ux_customer_accounts_tenant_id_user_id ON sales.customer_accounts (tenant_id, user_id)")]
    [InlineData("CREATE UNIQUE INDEX ux_customer_accounts_tenant_id_customer_id ON sales.customer_accounts (tenant_id, customer_id)")]
    [InlineData("CREATE TABLE integration.outgoing_mails")]
    [InlineData("CONSTRAINT fk_outgoing_mails_tenant_id_branch_id_pc_build_id FOREIGN KEY (tenant_id, branch_id, pc_build_id) REFERENCES sales.pc_builds (tenant_id, branch_id, id)")]
    [InlineData("CONSTRAINT ck_outgoing_mail_dispatch_fin CHECK ((status = 'Pending') = (completed_at IS NULL))")]
    [InlineData("CONSTRAINT ck_outgoing_mail_attempts_error CHECK (succeeded = (error IS NULL))")]
    [InlineData("CREATE UNIQUE INDEX ux_outgoing_mail_attempts_outgoing_mail_id_attempt ON integration.outgoing_mail_attempts (outgoing_mail_id, attempt)")]
    [InlineData("REFERENCES integration.outgoing_mails (tenant_id, id)")]
    public void El_DDL_generado_contiene_las_restricciones_clave(string fragment) =>
        Assert.Contains(fragment, Ddl, StringComparison.Ordinal);

    [Fact]
    public void El_ERD_documenta_todas_las_tablas_del_modelo()
    {
        var erd = File.ReadAllText(Path.Combine(V21MigrationTests.RepoRoot(), "docs", "database", "ERD-MINV-V3.md"));
        foreach (var e in Entities)
        {
            Assert.Contains($"`{e.GetSchema()}.{e.GetTableName()}`", erd, StringComparison.Ordinal);
        }
    }

    /// <summary>Reglas A-07 y B-15: <c>scripts/db_init.sql</c> es la cabecera más el script idempotente de TODAS las
    /// migraciones (lo regenera <c>tools\build_v3.ps1</c>). Falla si falta regenerarlo o si cambió el SQL de una migración
    /// publicada (p. ej. porque usaba una constante compartida que después creció).</summary>
    [Fact]
    public void Db_init_sql_coincide_con_el_script_de_las_migraciones()
    {
        static string Normalize(string text) => text.TrimStart('﻿').Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
        var scripts = Path.Combine(V21MigrationTests.RepoRoot(), "scripts");
        var script = Db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>()
            .GenerateScript(options: Microsoft.EntityFrameworkCore.Migrations.MigrationsSqlGenerationOptions.Idempotent);
        var expected = Normalize(File.ReadAllText(Path.Combine(scripts, "db_init.header.sql")) + script);
        var actual = Normalize(File.ReadAllText(Path.Combine(scripts, "db_init.sql")));
        Assert.True(expected == actual,
            "scripts/db_init.sql no coincide con las migraciones: regenérelo con tools\\build_v3.ps1 y, si cambió una migración " +
            "publicada, deje su SQL como se publicó (reglas A-07 y B-15).");
    }

    [Fact]
    public void Los_modulos_comerciales_se_siembran_con_la_migracion()
    {
        Assert.Contains("DATA_ENGINE", Ddl, StringComparison.Ordinal);
        Assert.Contains("SLA_SUPPORT", Ddl, StringComparison.Ordinal);
        Assert.Contains("FISCAL_SIAT", Ddl, StringComparison.Ordinal);
    }
}
