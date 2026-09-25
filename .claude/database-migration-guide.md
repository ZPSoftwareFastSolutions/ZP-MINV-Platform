# Guía de migraciones de la base de datos · M-INV V3 y V4

Dos tipos de migración: **esquema** (EF Core Code-First, cambios del modelo) y **datos** (llevar un libro de la V2.1 a
la V3, o una base V3 a la V4). Reglas: `.claude/v3-architecture-rules.md` (A-06, A-07, A-08) y
`.claude/v4-architecture-rules.md` (B-12, B-13, B-15).

## 0. Dos contextos desde la V4

| Contexto | Archivo | Migraciones | Para qué |
|---|---|---|---|
| **`MinvWriteDbContext`** (antes `MINVDbContext`) | `Persistence/MinvWriteDbContext.cs` | **sí**: todas las de `Persistence/Migrations` | modelo transaccional (OLTP): las 110 tablas, outbox, guardas |
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

- Son listas **explícitas** a propósito: una migración publicada es inmutable y su resultado no debe cambiar si mañana
  aparece otra tabla con esas columnas. La política de empresa sí se genera por descubrimiento (`tenant_id`), porque
  toda tabla la necesita.
- Una tabla nueva de sucursal, entre sucursales o append-only DEBE agregarse en una lista de la **migración nueva** que
  la crea (con su política, trigger y `REVOKE`), y la prueba de modelo DEBE comparar la unión de todas las listas con
  las entidades del modelo (regla B-15). `ModelTests.Los_libros_mayores_son_append_only` ya fija los 15 libros.

## 7. Revertir

- Esquema: `dotnet ef database update <MigraciónAnterior> --context MinvWriteDbContext` (usa los `Down`); en
  producción, restaure el respaldo (o use la restauración a un punto en el tiempo del proveedor).
- Datos importados: la importación crea una empresa nueva; si hay que repetirla, cree la base de nuevo o use otro código
  de empresa (los movimientos son append-only y no se borran).
- V4 → V3: el `Down` de `V4MultiBranchCloud` borra las tablas nuevas (transferencias, integraciones, idempotencia) y las
  columnas de sucursal: solo tiene sentido antes de operar con varias sucursales. Con datos V4 reales, restaure el
  respaldo previo a la migración.
