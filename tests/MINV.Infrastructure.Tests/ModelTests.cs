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
    public void El_modelo_tiene_153_tablas_en_10_esquemas()
    {
        var tables = Entities.Select(e => (e.GetSchema(), e.GetTableName())).Distinct().ToList();
        Assert.True(tables.Count >= 80, $"solo {tables.Count} tablas");
        // 96 de la V3 + product_images (V3.1) + 13 de la V4 (sucursales, integración, idempotencia) + 30 de la V4.1 (facturación)
        // + 12 de la V4.2 (edición Tecnología) + 1 de la V6 (bitácora de los armados)
        Assert.Equal(153, tables.Count);
        Assert.Equal(10, Schemas.All.Count);
        Assert.Equal(Schemas.All.OrderBy(s => s), tables.Select(t => t.Item1!).Distinct().OrderBy(s => s));
        Assert.Equal(27, tables.Count(t => t.Item1 == Schemas.Billing));
        Assert.Equal(2, tables.Count(t => t.Item1 == Schemas.Service));
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
        var sinceV6 = Persistence.Migrations.V6Storefront.NewTablesV6;
        Assert.Equal(Entities.Select(Name).Where(t => !before.Contains(t) && !sinceV6.Contains(t)).Order(), v42.Order());
        Assert.All(Persistence.Migrations.V42TechRetail.BranchTablesV42.Concat(Persistence.Migrations.V42TechRetail.InterBranchTablesV42)
            .Concat(Persistence.Migrations.V42TechRetail.AppendOnlyTablesV42), t => Assert.Contains(t, v42));
        // V6 · la tabla nueva es exactamente la que el modelo tiene y la migración V4.2 no
        var v6 = Persistence.Migrations.V6Storefront.NewTablesV6;
        var beforeV6 = new Persistence.Migrations.V42TechRetail().TargetModel.GetEntityTypes()
            .Select(e => $"{e.GetSchema()}.{e.GetTableName()}").ToHashSet(StringComparer.Ordinal);
        Assert.Equal(Entities.Select(Name).Where(t => !beforeV6.Contains(t)).Order(), v6.Order());
        Assert.All(Persistence.Migrations.V6Storefront.BranchTablesV6.Concat(Persistence.Migrations.V6Storefront.AppendOnlyTablesV6),
            t => Assert.Contains(t, v6));
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

    /// <summary>Tablas de sucursal de todas las migraciones (V4 + V4.1 + V4.2 + V6).</summary>
    internal static IEnumerable<string> AllBranchTables =>
        Persistence.Migrations.V4MultiBranchCloud.BranchTables.Concat(Persistence.Migrations.V41SiatBilling.BranchTablesV41)
            .Concat(Persistence.Migrations.V42TechRetail.BranchTablesV42).Concat(Persistence.Migrations.V6Storefront.BranchTablesV6);

    /// <summary>Tablas entre sucursales de todas las migraciones (V4 + V4.2).</summary>
    internal static IEnumerable<string> AllInterBranchTables =>
        Persistence.Migrations.V4MultiBranchCloud.InterBranchTables.Concat(Persistence.Migrations.V42TechRetail.InterBranchTablesV42);

    /// <summary>Libros append-only de todas las migraciones (V3 + V4 + V4.1 + V4.2 + V6).</summary>
    internal static IEnumerable<string> AllAppendOnlyTables =>
        Persistence.Migrations.GuardsRlsAndViews.AppendOnlyTables.Concat(Persistence.Migrations.V4MultiBranchCloud.AppendOnlyTablesV4)
            .Concat(Persistence.Migrations.V41SiatBilling.AppendOnlyTablesV41).Concat(Persistence.Migrations.V42TechRetail.AppendOnlyTablesV42)
            .Concat(Persistence.Migrations.V6Storefront.AppendOnlyTablesV6);

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
    [InlineData("CONSTRAINT ck_audit_logs_canal CHECK (channel IS NULL OR channel IN ('desktop', 'cloud', 'api', 'storefront'))")]
    [InlineData("REFERENCES sales.pc_build_lines (tenant_id, branch_id, id)")]
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
