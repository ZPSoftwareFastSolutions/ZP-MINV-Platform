# Guía de migraciones de la base de datos · M-INV V3

Dos tipos de migración: **esquema** (EF Core Code-First, cambios del modelo) y **datos** (llevar un libro de la V2.1 a
la V3). Reglas: `.claude/v3-architecture-rules.md` (A-06, A-07, A-08).

## 1. Requisitos

- .NET SDK 8 o posterior (la solución apunta a `net8.0`; con solo el SDK 10 instalado funciona gracias a
  `RollForward=Major`).
- PostgreSQL 15 o superior (se recomienda 16). Usa `security_invoker` en vistas y `NULLS NOT DISTINCT` en índices.
- Herramienta local `dotnet-ef` 8.0.31 (`dotnet-tools.json`): `dotnet tool restore`.

## 2. Cambiar el esquema (Code-First)

1. Modifique la entidad en `src/1. Core/MINV.Domain` y su configuración en
   `src/2. Infrastructure/MINV.Infrastructure/Persistence/Configurations/<Contexto>/`.
2. Cree la migración (desde la raíz del repositorio):

   ```powershell
   $env:DOTNET_ROLL_FORWARD = 'Major'
   dotnet ef migrations add <NombreDescriptivo> --project "src/2. Infrastructure/MINV.Infrastructure" `
       --startup-project "src/2. Infrastructure/MINV.Infrastructure" --output-dir Persistence/Migrations
   ```

3. Revise el `.cs` generado: nada de `DropColumn`/`DropTable` sobre datos de negocio sin plan de migración de datos;
   en las tablas append-only solo se agregan columnas.
4. SQL propio de PostgreSQL (triggers, vistas, RLS): en la misma migración o en una nueva con `migrationBuilder.Sql`,
   siempre con su `Down`.
5. Ejecute `tools/build_v3.ps1`: compila, prueba, verifica que el modelo no tenga cambios pendientes
   (`dotnet ef migrations has-pending-model-changes`) y regenera `scripts/db_init.sql`.

## 3. Aplicar el esquema

| Escenario | Comando |
|---|---|
| Base nueva o actualización (rol dueño) | `minv migrate --conexion "Host=…;Database=minv;Username=minv_owner;Password=…"` |
| Servidor sin .NET (DBA) | `psql -U minv_owner -d minv -v ON_ERROR_STOP=1 -f scripts/db_init.sql` (idempotente) |
| Pruebas | `MINV_TEST_PG` → cada ejecución crea y borra una base temporal |

La aplicación se conecta con el rol `minv_app` (Row Level Security activa, sin UPDATE/DELETE sobre los libros
mayores). Las migraciones se aplican con el dueño (`minv_owner`).

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
entera y el informe dice qué registro corregir en la V2.1 (con un AJUSTE) antes de volver a intentarlo.

## 5. Revertir

- Esquema: `dotnet ef database update <MigraciónAnterior>` (usa los `Down`); en producción, restaure el respaldo.
- Datos importados: la importación crea una empresa nueva; si hay que repetirla, cree la base de nuevo o use otro código
  de empresa (los movimientos son append-only y no se borran).
