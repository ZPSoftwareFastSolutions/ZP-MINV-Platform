# Guía de migraciones de la base de datos · M-INV V3, V4, V4.1 y V4.2

Dos tipos de migración: **esquema** (EF Core Code-First, cambios del modelo) y **datos** (llevar un libro de la V2.1 a
la V3, una base V3 a la V4, una V4 a la V4.1 o una V4.1 a la V4.2). Reglas: `.claude/v3-architecture-rules.md` (A-06,
A-07, A-08), `.claude/v4-architecture-rules.md` (B-12, B-13, B-15), `.claude/v41-billing-rules.md` (F-11, F-14) y
`.claude/v42-tech-rules.md` (T-01, T-02, T-05).

## 0. Dos contextos desde la V4

| Contexto | Archivo | Migraciones | Para qué |
|---|---|---|---|
| **`MinvWriteDbContext`** (antes `MINVDbContext`) | `Persistence/MinvWriteDbContext.cs` | **sí**: todas las de `Persistence/Migrations` | modelo transaccional (OLTP): las 152 tablas de la V4.2 (140 de la V4.1, 110 de la V4), outbox, guardas |
| `MinvReadDbContext` | `Persistence/MinvReadDbContext.cs` | **no** | modelo de lectura (OLAP): lee las vistas `reporting.v_*`; sus vistas las crea una migración del contexto de escritura |

- `MINVDbContext` se renombró a `MinvWriteDbContext` en la V4 (clase, archivo, `IDesignTimeDbContextFactory` →
  `MinvWriteDbContextDesignTimeFactory`). Las migraciones anteriores siguen válidas: sus `.Designer.cs` y la instantánea
  apuntan a `[DbContext(typeof(MinvWriteDbContext))]`. La instantánea conserva su nombre de archivo
  (`MINVDbContextModelSnapshot.cs`): EF Core la identifica por el atributo, no por el nombre; no hace falta renombrarla.
- Como el ensamblado de infraestructura tiene dos `DbContext`, **toda orden `dotnet ef` DEBE llevar
  `--context MinvWriteDbContext`**; sin ella falla con «More than one DbContext was found».

## 1. Requisitos

- .NET SDK 8 o posterior (la solución apunta a `net8.0`; con solo el SDK 10 instalado funciona gracias a
  `RollForward=Major`: defina `$env:DOTNET_ROLL_FORWARD = 'Major'` para `dotnet ef`).
- PostgreSQL 15 o superior (se recomienda 16). Usa `security_invoker` y `security_barrier` en vistas,
  `NULLS NOT DISTINCT` en índices y políticas RLS RESTRICTIVAS.
- Herramienta local `dotnet-ef` 8.0.31 (`dotnet-tools.json`): `dotnet tool restore`.

## 2. Cambiar el esquema (Code-First)

1. Modifique la entidad en `src/1. Core/MINV.Domain` y su configuración en
   `src/2. Infrastructure/MINV.Infrastructure/Persistence/Configurations/<Contexto>/`.
   - ¿La tabla es de una sucursal? La entidad implementa `IBranchScoped` (o `IInterBranch` si es entre dos sucursales),
     su FK hacia un padre de la misma sucursal incluye `BranchId` y el padre expone la clave alterna
     `(TenantId, BranchId, Id)` (regla B-02).
   - ¿Es un libro? `IAppendOnly` (regla B-06).
2. Cree la migración (desde la raíz del repositorio):

   ```powershell
   $env:DOTNET_ROLL_FORWARD = 'Major'
   dotnet ef migrations add <NombreDescriptivo> --context MinvWriteDbContext `
       --project "src/2. Infrastructure/MINV.Infrastructure" `
       --startup-project "src/2. Infrastructure/MINV.Infrastructure" --output-dir Persistence/Migrations
   ```

3. Revise el `.cs` generado: nada de `DropColumn`/`DropTable` sobre datos de negocio sin plan de migración de datos;
   en las tablas append-only solo se agregan columnas.
4. SQL propio de PostgreSQL (triggers, vistas, RLS, funciones, rellenos): en un **archivo parcial** de la misma
   migración, `<fecha>_<Nombre>.Sql.cs` (`public partial class <Nombre>` con métodos estáticos que reciben el
   `MigrationBuilder`), llamado desde `Up` y `Down`, siempre con su reversa. Así el archivo generado por EF se puede
   regenerar sin perder el SQL a mano.
5. Si la tabla nueva es de sucursal, entre sucursales o append-only, agréguela a las listas **de la migración nueva**
   (política `branch_isolation`, trigger `trg_append_only`, `REVOKE` a los roles) — nunca a las de una migración
   publicada — y mantenga la prueba que compara las listas con el modelo (§6).
6. Compruebe que el modelo no tiene cambios pendientes y regenere el script:

   ```powershell
   dotnet ef migrations has-pending-model-changes --context MinvWriteDbContext `
       --project "src/2. Infrastructure/MINV.Infrastructure" --startup-project "src/2. Infrastructure/MINV.Infrastructure"
   dotnet ef migrations script --idempotent --context MinvWriteDbContext `
       --project "src/2. Infrastructure/MINV.Infrastructure" --startup-project "src/2. Infrastructure/MINV.Infrastructure" `
       --output <archivo temporal>
   ```

   `tools/build_v3.ps1` hace ambas cosas (y compila y prueba) y arma `scripts/db_init.sql` = `scripts/db_init.header.sql`
   + el script idempotente; con dos contextos también debe pasar `--context MinvWriteDbContext`.
7. Actualice `docs/database/ERD-MINV-V3.md` (cada tabla como `` `esquema.tabla` ``: lo verifica
   `ModelTests.El_ERD_documenta_todas_las_tablas_del_modelo`).

## 3. Aplicar el esquema

| Escenario | Comando |
|---|---|
| Base nueva o actualización (rol dueño) | `minv migrate --conexion "Host=…;Database=minv;Username=minv_owner;Password=…"` |
| Servidor sin .NET (DBA) | `psql -U minv_owner -d minv -v ON_ERROR_STOP=1 -f scripts/db_init.sql` (idempotente) |
| Desde el SDK | `dotnet ef database update --context MinvWriteDbContext --project … --startup-project …` (con `MINV_DB` del dueño) |
| Base en la nube (V4) | `tools\bd_nube.ps1 -Accion preparar -Conexion "<cadena del rol dueño>"` (crea roles y aplica `db_init.sql`) |
| Base local (V4) | `tools\bd_local.ps1 -Accion recrear` |
| Pruebas | `MINV_TEST_PG` → cada ejecución crea y borra una base temporal |

Las migraciones se aplican SIEMPRE con el dueño (`minv_owner`). Las aplicaciones se conectan con roles sujetos a Row
Level Security y sin UPDATE/DELETE sobre los libros mayores: `minv_app` (escritorio con conexión directa) y
`minv_server` (servidor en la nube y API Gateway). **Cree esos roles ANTES de migrar**: los permisos se conceden
dentro de las migraciones solo a los roles que existen en ese momento (si no, ver
`docs/deployment/despliegue-nube-v4.md` §4.3).

## 4. Migrar los datos de la V2.1 a la V3

1. Descargue el libro colaborativo de SharePoint/OneDrive como `.xlsx` (Archivo › Crear una copia › Descargar). Antes,
   en la V2.1, pulse «Recalcular stock»: la instantánea se usa para verificar la paridad.
2. Ejecute (una transacción: todo o nada):

   ```powershell
   minv import-v21 --archivo "C:\ruta\M-INV_V2_Colaborativo.xlsx" --codigo EMPRESA --admin admin@empresa.com `
       --nombre "Administrador" --zona America/La_Paz --moneda BOB --pais BO --pais-nombre Bolivia
   ```

3. Lea el informe: productos, proveedores, posiciones, usuarios, movimientos, rechazos, actividad, conteos en curso y la
   **paridad** (stock, semáforo, cobertura, ranking, alertas y pedido idénticos a la V2.1). Si la paridad falla, el
   comando termina con código 2 y lista las diferencias.
4. Asigne contraseña a cada usuario migrado: `minv user password --codigo EMPRESA --correo persona@empresa.com`.
5. Verifique: `minv verify --codigo EMPRESA` (tablas, triggers, RLS y conservación).

### Qué hace el importador con cada tabla de la V2.1

| V2.1 | V3 |
|---|---|
| 01_CONFIG (cfgEmpresa, cfgNIT, cfgMargenAlerta, cfgDiasSinRotacion, cfgFechaMin, cfgMoneda, cfgBodega) | `iam.tenants`, `iam.tenant_configs`, `accounting.currencies`, `warehouse.branches`/`warehouses` |
| tblUsuarios | `iam.users` + `iam.user_roles` (mismos roles) + `warehouse.branch_users`; sin contraseña hasta asignarla |
| tblCategorias | `catalog.categories` + clausura en `catalog.category_hierarchies` |
| tblUnidades | `catalog.units_of_measure` (las que falten) |
| tblProveedores | `purchasing.suppliers` (código P001…) + `purchasing.supplier_contacts` |
| tblProductos | `catalog.products` + variante por defecto (SKU) + lote `SIN-LOTE` + `product_stock_policies` (mín/máx) + `product_suppliers` (preferido) + `accounting.average_cost_history` (costo) |
| Ubicación (A-01-01) | `warehouse.zones` (A) › `aisles` (01) › `racks` (01) › `shelves` (00) › `bins` (ALM01-A-01-01) + `bin_assignments` |
| tblEntradas / tblSalidas «✔» | `inventory.stock_movements` reproducidos en orden a través del dominio (poka-yoke incluido), con `legacy_reference` = ID de la V2.1 y el Timestamp convertido a UTC con la zona horaria indicada |
| tblEntradas / tblSalidas «✖ Rechazado» | `iam.audit_logs` (resultado Rejected) |
| tblActividad | `iam.audit_logs` (acción = script, resultado ✔/✖, detalle) |
| tblConteo con cantidades | `inventory.physical_counts` abierta + líneas |
| 15_STOCK, 16_ALERTAS, 18_PEDIDO | No se importan (son derivadas): se usan para verificar la paridad |

Si la historia de la V2.1 viola una regla de la V3 (p. ej. un stock negativo cargado a mano), la migración se cancela
entera y el informe dice qué registro corregir en la V2.1 (con un AJUSTE) antes de volver a intentarlo. En la V4 el
importador deja todo en la sucursal principal (`CM`) de la empresa nueva.

## 5. Migrar una base V3 a la V4 (`V4MultiBranchCloud`)

La migración `20260925214056_V4MultiBranchCloud` (archivo generado + parcial `…V4MultiBranchCloud.Sql.cs`) lleva una
base V3.1 (97 tablas, una sucursal por empresa) a la V4 (110 tablas, 8 esquemas) sin perder datos.

**Qué hace, en orden** (`Up`):

1. **Guardia** (`V4Guard`): aborta si `inventory.stock_transfers` tiene filas (la V3 no tenía caso de uso de
   transferencias; el diseño cambió por completo).
2. Cambios de esquema generados por EF: columnas `branch_id` con el valor provisional `00000000-…` en las tablas de
   sucursal, `from_branch_id`/`to_branch_id` y el rediseño de las transferencias, columnas nuevas de `sessions`,
   `audit_logs` y `average_cost_history`, esquema `integration` y las 13 tablas nuevas.
3. **Relleno** (`V4Backfill`), antes de crear las claves y FK compuestas con sucursal:
   - pausa los triggers de usuario (`ALTER TABLE … DISABLE TRIGGER USER`) SOLO en las tablas que el relleno actualiza
     y que los tienen (`GuardedDuringBackfill`: movimientos, caja, pagos, costo promedio, asientos y sus líneas);
   - baja la sucursal por la jerarquía: almacén → zona → pasillo → estantería → nivel → posición → asignación;
     posición → existencia → movimiento y reserva; almacén → ajuste, toma física, orden de compra, recepción, caja
     → turno → movimiento de caja; turno o almacén → venta → línea → factura → línea y pago; almacén → costo promedio;
   - numera `average_cost_history.sequence` por (empresa, variante, almacén) en orden de `effective_at`;
   - lo que no tiene camino (asientos, facturas de proveedor sin recepción, devoluciones sin líneas) recibe la
     sucursal principal de la empresa (la de menor código: la V3 tenía una sola); las líneas heredan de su cabecera;
   - **verifica** que ninguna tabla de `BranchTables` quedó con la sucursal `00000000-…` (si no, aborta) y reactiva los
     triggers.
4. Claves alternas y FK compuestas con sucursal, índices (generado por EF).
5. **Defensas** (`V4Guards`): append-only en `AppendOnlyTablesV4`; `iam.branch_visible(uuid)`; `tenant_isolation` en
   todas las tablas con `tenant_id` (108); `branch_isolation` RESTRICTIVA generada desde `BranchTables` e
   `InterBranchTables` (40); funciones SECURITY DEFINER (`integration.resolve_api_key`, `iam.resolve_session`,
   `integration.claim_deliveries`, `reporting.refresh_all`) con `search_path` fijo y EXECUTE solo para `minv_server`;
   esquema `reporting` (vistas materializadas + vistas `security_barrier`); `inventory.v_transfer_breaches`; permisos
   y cuentas V4 de las empresas existentes; privilegios de `minv_app` y `minv_server` (si existen).

`Down` (`V4DropGuards` + lo generado) quita políticas, funciones, el esquema `reporting` y la vista, y borra las cuentas
1.1.06 y 2.1.04 solo si no tienen movimientos.

**Paso a paso**:

1. **Respaldo** completo de la base V3 (`pg_dump -Fc`) y, si es posible, pruebe primero sobre una copia.
2. Cree el rol `minv_server` (y `minv_app` si no existe) **antes** de migrar.
3. Aplique la migración con el dueño: `minv migrate --conexion "<cadena de minv_owner>"` (o `scripts/db_init.sql`).
4. Verifique (como dueño):

   ```sql
   SELECT count(*) FROM pg_policies WHERE policyname = 'tenant_isolation';   -- 108
   SELECT count(*) FROM pg_policies WHERE policyname = 'branch_isolation';   -- 40
   SELECT count(*) FROM pg_trigger  WHERE tgname = 'trg_append_only';        -- 15
   SELECT count(*) FROM inventory.v_conservation_breaches;                   -- 0
   SELECT count(*) FROM inventory.v_transfer_breaches;                       -- 0
   ```

   (Resultado sobre una copia de la base local de la V3: 110 tablas, 108, 40, 15 y 0 descuadres.)
5. Las empresas existentes reciben los permisos y las cuentas nuevas, pero **no** los módulos comerciales de la V4: se
   venden aparte. Para habilitarlos (como dueño):

   ```sql
   INSERT INTO iam.tenant_modules (tenant_id, module_id, activated_at, expires_at, is_active)
   SELECT t.id, m.id, now(), NULL, true
   FROM iam.tenants t CROSS JOIN iam.modules m
   WHERE t.code = '<CODIGO>' AND m.code IN ('CLOUD_HA', 'MULTI_BRANCH', 'API_INTEGRATIONS', 'GLOBAL_AUDIT')
     AND NOT EXISTS (SELECT 1 FROM iam.tenant_modules x WHERE x.tenant_id = t.id AND x.module_id = m.id);
   ```

6. Cree las sucursales nuevas y asigne los usuarios desde el escritorio (Sucursales) o con `CreateBranchCommand` /
   `AssignUserBranchesCommand`. Los usuarios sin `corporate.branches.all` necesitan al menos una fila en
   `warehouse.branch_users` para iniciar sesión (los importados de la V2.1 ya están asignados a la principal; revise
   los demás antes de abrir la V4 a los usuarios:
   `SELECT u.email FROM iam.users u WHERE NOT EXISTS (SELECT 1 FROM warehouse.branch_users b WHERE b.user_id = u.id);`).

## 6. Listas explícitas de tablas (mantenerlas sincronizadas)

`V4MultiBranchCloud.Sql.cs` define tres listas que generan políticas, triggers y privilegios:

| Lista | Debe coincidir con | Genera |
|---|---|---|
| `BranchTables` (35) | entidades `IBranchScoped` | política RESTRICTIVA `branch_isolation` sobre `branch_id`; verificación del relleno |
| `InterBranchTables` (5) | entidades `IInterBranch` | `branch_isolation` sobre `from_branch_id OR to_branch_id` |
| `AppendOnlyTablesV4` (8) | entidades `IAppendOnly` nuevas de la V4 (las 7 de la V3 están en `GuardsRlsAndViews.AppendOnlyTables`) | `trg_append_only`, `REVOKE UPDATE, DELETE, TRUNCATE` |
| `V41SiatBilling.BranchTablesV41` (15) | entidades `IBranchScoped` nuevas de la V4.1 (junto con `BranchTables`: 50) | `branch_isolation` RESTRICTIVA sobre `branch_id` |
| `V41SiatBilling.AppendOnlyTablesV41` (9) | entidades `IAppendOnly` nuevas de la V4.1 (24 libros en total) | `trg_append_only`, `REVOKE UPDATE, DELETE, TRUNCATE` |
| `V41SiatBilling.NewTablesV41` (30) | tablas creadas por la V4.1 | `GRANT` a `minv_app` y `minv_server` |
| `V42TechRetail.BranchTablesV42` (6) | entidades `IBranchScoped` nuevas de la V4.2 (56 en total) | `branch_isolation` RESTRICTIVA sobre `branch_id` |
| `V42TechRetail.InterBranchTablesV42` (1) | entidades `IInterBranch` nuevas de la V4.2 (6 en total) | `branch_isolation` sobre `from_branch_id OR to_branch_id` |
| `V42TechRetail.AppendOnlyTablesV42` (5) | entidades `IAppendOnly` nuevas de la V4.2 (29 libros en total) | `trg_append_only`, `REVOKE UPDATE, DELETE, TRUNCATE` |
| `V42TechRetail.NewTablesV42` (12) | tablas creadas por la V4.2 (la prueba las compara con las que el modelo tiene y `V41CafcNumbering` no) | `GRANT` a `minv_app` y `minv_server` |

- Son listas **explícitas** a propósito: una migración publicada es inmutable y su resultado no debe cambiar si mañana
  aparece otra tabla con esas columnas. La política de empresa sí se genera por descubrimiento (`tenant_id`), porque
  toda tabla la necesita.
- Una tabla nueva de sucursal, entre sucursales o append-only DEBE agregarse en una lista de la **migración nueva** que
  la crea (con su política, trigger y `REVOKE`), y la prueba de modelo DEBE comparar la unión de todas las listas con
  las entidades del modelo (regla B-15). `ModelTests.Los_libros_mayores_son_append_only` fija los 29 libros y
  `ModelTests.Las_listas_de_las_migraciones_coinciden_con_el_modelo` compara la unión de las listas V4 + V4.1 + V4.2
  con el modelo.

## 7. Revertir

- Esquema: `dotnet ef database update <MigraciónAnterior> --context MinvWriteDbContext` (usa los `Down`); en
  producción, restaure el respaldo (o use la restauración a un punto en el tiempo del proveedor).
- Datos importados: la importación crea una empresa nueva; si hay que repetirla, cree la base de nuevo o use otro código
  de empresa (los movimientos son append-only y no se borran).
- V4 → V3: el `Down` de `V4MultiBranchCloud` borra las tablas nuevas (transferencias, integraciones, idempotencia) y las
  columnas de sucursal: solo tiene sentido antes de operar con varias sucursales. Con datos V4 reales, restaure el
  respaldo previo a la migración.

## 8. Migrar una base V4 a la V4.1 (`V41SiatBilling`)

La migración `20260926003559_V41SiatBilling` (archivo generado + parcial `…V41SiatBilling.Sql.cs`) agrega la facturación
SIAT: 110 → **140 tablas en 9 esquemas**. No tiene relleno: todas las tablas son nuevas y las columnas nuevas de
`sales.customers` (`document_type`, `complement`) son opcionales, así que los datos V4 no cambian.

**Qué hace, en orden** (`Up`):

1. Generado por EF: esquema `billing` y sus 27 tablas (configuración, conexión por ambiente, sucursales del Padrón,
   puntos de venta, CUIS, CUFD, catálogos sincronizados, homologación, verificaciones de NIT, documentos fiscales con su
   subtipo de nota, líneas, XML, bitácora y entregas, eventos significativos, paquetes, CAFC, bitácora SOAP y correo);
   `sales.sales_returns` y `sales.sales_return_lines`; `purchasing.supplier_invoice_fiscal`; las columnas del cliente
   con sus CHECK; claves alternas y FK compuestas con empresa y sucursal; el módulo `FISCAL_SIAT` en `iam.modules`
   (`HasData`).
2. **Defensas** (`V41Guards`): `trg_append_only` y `trg_append_only_truncate` en `AppendOnlyTablesV41` (9);
   `tenant_isolation` por descubrimiento en toda tabla con `tenant_id` que aún no la tenga (las 30 nuevas: 138 en
   total); `branch_isolation` RESTRICTIVA con `iam.branch_visible(branch_id)` en `BranchTablesV41` (15: 55 en total);
   función SECURITY DEFINER `billing.siat_active_tenants()` (`SETOF uuid`, `search_path` fijo, sin EXECUTE para
   PUBLIC); vista `billing.v_fiscal_document_totals` (`security_barrier` + `security_invoker`: totales derivados de las
   líneas); permisos `billing.*` y matriz rol-permiso de las empresas existentes; privilegios de `minv_app` y
   `minv_server` (solo si existen: SELECT/INSERT/UPDATE/DELETE en las 30 tablas nuevas salvo UPDATE/DELETE/TRUNCATE en
   los libros, SELECT en la vista y EXECUTE de la función solo para `minv_server`).

`Down` (`V41DropGuards` + lo generado + `V41DropSchema`) borra la vista, la función y los permisos `billing.*` (con sus
filas de `role_permissions`), las tablas nuevas, las columnas del cliente y el módulo, y por último el esquema vacío.
Solo tiene sentido antes de emitir documentos fiscales: con documentos reales, restaure el respaldo.

**Paso a paso**:

1. **Respaldo** (`pg_dump -Fc`) y, si es posible, pruebe primero sobre una copia (`CREATE DATABASE minv_v41_prueba
   TEMPLATE minv`, sin conexiones abiertas a `minv`; o `pg_dump`/`pg_restore`).
2. Los roles `minv_server` y `minv_app` ya existen desde la V4 (si no, créelos con `minv roles` antes de migrar).
3. Aplique con el dueño: `minv migrate --conexion "<cadena de minv_owner>"` (o `scripts/db_init.sql`).
4. Verifique (como dueño) o con `minv verify --codigo <EMPRESA>`:

   ```sql
   SELECT count(*) FROM information_schema.tables WHERE table_type = 'BASE TABLE'
     AND table_schema IN ('iam','catalog','warehouse','inventory','purchasing','sales','accounting','integration','billing')
     AND table_name <> '__ef_migrations_history';                            -- 140
   SELECT count(*) FROM pg_policies WHERE policyname = 'tenant_isolation';   -- 138
   SELECT count(*) FROM pg_policies WHERE policyname = 'branch_isolation';   -- 55
   SELECT count(*) FROM pg_trigger  WHERE tgname = 'trg_append_only';        -- 24
   SELECT count(*) FROM inventory.v_conservation_breaches;                   -- 0
   SELECT count(*) FROM inventory.v_transfer_breaches;                       -- 0
   ```

5. Las empresas existentes reciben los permisos `billing.*`, pero **no** el módulo `FISCAL_SIAT` (se vende aparte).
   Para habilitarlo, el mismo `INSERT INTO iam.tenant_modules …` del §5 con `m.code = 'FISCAL_SIAT'`. Después se
   configura la facturación desde el escritorio (Configuración › Facturación SIAT): NIT, código de sistema, ambiente,
   token, sucursales del Padrón y puntos de venta.

**Notas de la V4.1 (fase 3):**

- `20260926033017_V41CafcNumbering` (sin SQL propio): las facturas manuales de contingencia transcritas llevan la
  numeración de SU talonario. El índice único de la numeración se divide en dos parciales: `(tenant_id, environment,
  point_of_sale_id, document_sector, number) WHERE cafc IS NULL` para la serie del punto de venta y el mismo más `cafc`
  `WHERE cafc IS NOT NULL` para cada talonario (así el N° 1001 del CAFC no choca con el N° 1001 de la serie normal).
- Datos de prueba: después de migrar una base de PRUEBAS, `tools\bd_local.ps1 -Accion recrear` carga la empresa MINV
  con la facturación de los últimos 25 días contra el simulador del SIN en proceso (o `-SinFacturacion`). El estado
  del simulador queda en `%LOCALAPPDATA%\M-INV\siat-simulador.json`: si se restaura o se copia una base de pruebas,
  copie también ese archivo (y el `MINV_SIAT_TOKEN` de `claves-integracion.txt`), o el simulador HTTP no reconocerá los
  CUIS, CUFD y documentos de esa base (bastará con «Preparar» para pedir códigos nuevos).
- `minv verify --codigo <EMPRESA>` comprueba además que el total de cada factura válida (derivado en
  `billing.v_fiscal_document_totals`) sea el cobrado y que ninguna venta tenga dos documentos fiscales vigentes.

## 9. Migrar una base V4.1 a la V4.2 (`V42TechRetail`)

La migración `20260926082719_V42TechRetail` (archivo generado + parcial `…V42TechRetail.Sql.cs`) agrega la edición
Tecnología: 140 → **152 tablas en 10 esquemas** (esquema nuevo `service`). Tiene UN relleno: las series existentes
(`inventory.serial_numbers`) ganan `variant_id`, `kind` y `received_at`, y su unicidad pasa de (lote, serie) a
(empresa, variante, serie).

**Qué hace, en orden** (`Up`):

1. **Guardia** (`V42Guard`, antes de cualquier cambio): se detiene con un mensaje «M-INV V4.2: …» si una serie se repite
   en dos lotes de la misma variante, si hay series vacías o con espacios, comas o punto y coma, si una serie en stock
   no tiene existencia (o una fuera de stock la tiene) o si su existencia es de otro lote. La transacción de la migración
   se deshace y la base queda en la V4.1: corrija esas filas y vuelva a migrar.
2. Generado por EF: quita las FK e índices viejos de `serial_numbers` y agrega sus columnas con valor provisional;
   claves alternas `(tenant_id, variant_id, id)` en `batches` y `(tenant_id, batch_id, id)` en `stock_levels` (destino de
   las FK compuestas de las series).
3. **Relleno** (`V42Backfill`): `variant_id` desde el lote, `kind = 'Serial'` y `received_at = created_at`; verificación
   de que ninguna fila quedó con el valor provisional y retiro de los DEFAULT provisionales. `serial_numbers` no tiene
   triggers: no se pausa nada.
4. Generado por EF: las 12 tablas nuevas (fichas técnicas, bitácora de series, series por línea de venta, devolución y
   transferencia, armados de PC, casos RMA y su bitácora), índices (serie única por variante, un caso RMA abierto por
   serie, números únicos por sucursal), CHECK y FK compuestas con empresa, sucursal y padre.
5. **Defensas y datos** (`V42Guards`): `trg_append_only` en `AppendOnlyTablesV42` (5: 29 en total); `tenant_isolation`
   por descubrimiento (150 en total); `branch_isolation` RESTRICTIVA en `BranchTablesV42` (6) y por origen o destino en
   `InterBranchTablesV42` (1): 62 en total; trigger `catalog.minv_spec_value_matches` (el valor usa la columna del tipo
   de su especificación y una especificación de un solo valor tiene una sola fila); vista `inventory.v_serial_breaches`;
   permisos `catalog.specs.manage`, `inventory.serials.view`, `inventory.serials.manage`, `service.rma.open`,
   `service.rma.manage`, `sales.pcbuild.manage` con su matriz rol-permiso, tipo de movimiento `REPOSICION_GARANTIA` y
   cuenta `5.1.10` Costo de garantías en las empresas existentes; privilegios de `minv_app` y `minv_server` (solo si
   existen).

`Down` (`V42DropGuards` + lo generado + `V42DropSchema`) borra la vista, el trigger, los permisos de la V4.2, el tipo de
movimiento y la cuenta (si no se usaron), las tablas nuevas y las columnas nuevas de las series, restaura la unicidad
por lote y por último borra el esquema `service` vacío. Solo tiene sentido antes de operar con series, RMA o armados.

**Paso a paso**: respaldo (`pg_dump -Fc`) y prueba sobre una copia; `minv migrate --conexion "<cadena de minv_owner>"`
(o `scripts/db_init.sql`); verificación con `minv verify --codigo <EMPRESA>` o, como dueño:

```sql
SELECT count(*) FROM information_schema.tables WHERE table_type = 'BASE TABLE'
  AND table_schema IN ('iam','catalog','warehouse','inventory','purchasing','sales','accounting','integration','billing','service')
  AND table_name <> '__ef_migrations_history';                            -- 152
SELECT count(*) FROM pg_policies WHERE policyname = 'tenant_isolation';   -- 150
SELECT count(*) FROM pg_policies WHERE policyname = 'branch_isolation';   -- 62
SELECT count(*) FROM pg_trigger  WHERE tgname = 'trg_append_only';        -- 29
SELECT count(*) FROM inventory.v_serial_breaches;                         -- 0
SELECT count(*) FROM inventory.v_conservation_breaches;                   -- 0
```

La prueba `V42TechPostgresTests.V42_la_migracion_rellena_las_series_existentes_y_su_guardia_las_protege` (con
`MINV_TEST_PG`) migra una base en la V4.1 con series a la V4.2, comprueba el relleno, los datos agregados y la reversa, y
que la guardia detiene la migración con una serie repetida en dos lotes.

## 10. Migrar una base V4.2 (o V5) a la V6 (`V6Storefront`)

La V5 no tocó la base: una base V4.2 migra directo. `V6Storefront` (`20260927173304_V6Storefront.cs` + `.Sql.cs`) agrega
`sales.pc_build_events`, 9 columnas a `sales.pc_builds` (`channel`, contacto, reserva, `cancel_reason`, `published_to_web`),
`inventory.stock_reservations.pc_build_line_id` (tercer origen del arco) y el canal `storefront` en `iam.audit_logs`. Su SQL
propio sigue la regla B-15: relleno de `channel = 'Desktop'` con verificación y retiro del valor provisional, bitácora
reconstruida de los armados existentes (`Created` y, si aplica, `Quoted`, `Sold` o `Cancelled`) ANTES del trigger
append-only, RLS por descubrimiento y `branch_isolation` RESTRICTIVA en la tabla nueva, permisos `storefront.read` y
`storefront.reserve` con su matriz, rol `TIENDA_WEB` y el usuario técnico `tienda-web@<dominio>` por empresa, y los
privilegios de `minv_app` y `minv_server`. Listas nuevas: `NewTablesV6`, `BranchTablesV6`, `AppendOnlyTablesV6`
(`ModelTests.Las_listas_de_las_migraciones_coinciden_con_el_modelo` las vigila).

```powershell
dotnet run --project "src/4. Tools/MINV.Cli" -- migrate --conexion "<cadena del rol dueño>"
```

Verificación en una copia:

```sql
SELECT count(*) FROM information_schema.tables WHERE table_type = 'BASE TABLE'
  AND table_schema IN ('iam','catalog','warehouse','inventory','purchasing','sales','accounting','integration','billing','service')
  AND table_name <> '__ef_migrations_history';                            -- 153
SELECT count(*) FROM pg_policies WHERE policyname = 'tenant_isolation';   -- 151
SELECT count(*) FROM pg_policies WHERE policyname = 'branch_isolation';   -- 63
SELECT count(*) FROM pg_trigger  WHERE tgname = 'trg_append_only';        -- 30
SELECT count(*) FROM sales.pc_builds WHERE channel NOT IN ('Desktop', 'Web');   -- 0
SELECT count(*) FROM iam.users u JOIN iam.user_roles ur ON ur.user_id = u.id JOIN iam.roles r ON r.id = ur.role_id
 WHERE r.code = 'TIENDA_WEB';                                             -- una por empresa
```

Reversa (`Down`): libera las reservas de armados (el reservado vuelve a las existencias), anula los armados reservados,
despublica, quita permisos, matriz y rol, y desactiva el usuario técnico (sus filas de auditoría lo referencian). Después
de migrar configure el gateway (`Minv:Storefront:TenantCode`) y, si la tienda no atiende desde la casa matriz,
`Minv:Storefront:BranchCode`.
