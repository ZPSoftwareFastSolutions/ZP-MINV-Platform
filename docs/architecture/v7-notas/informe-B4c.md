# Informe del paquete B4c · comprobación de normalización de la base de datos (V7)

## Resumen

Paquete B4c terminado sobre la rama Inventario-V7, sin commits propios. Al empezar no había avance de B4c; a mitad del trabajo
el coordinador guardó el avance en el commit wip `5563b40` (con la migración temporal `20260929112801_B4cTemporal`, que ya
estaba fusionada en `V7WebPlatform`): en el árbol quedó BORRADA y el próximo commit debe registrar ese borrado (ver
«Pendientes»). Solo se usó un PostgreSQL 16.15 temporal y aislado (localhost:55439, datos en el scratchpad, contraseña aleatoria
fuera del repositorio), ya detenido y borrado; no se tocó la base `minv`, localhost:5432, Docker ni la web.

1. `scripts/verificar_normalizacion.sql`: 32 consultas de solo lectura sobre `pg_catalog` y los datos, cada una con su comentario
   de regla: E01-E19 de estructura (las 14 reglas pedidas + FK entre sucursales, grupos repetidos, copias del padre y
   booleanas derivables), D01-D10 de coherencia de los datos con las redundancias controladas e I01-I03 informativas (lo dice su
   título). Las «problema» devuelven filas solo cuando algo no cumple. Tarda < 0,5 s.
2. Ejecución sobre una base temporal migrada con `minv migrate` y cargada con `minv datos-prueba --codigo TECHZONE --dias 20
   --dias-facturacion 10` (facturación contra el simulador en proceso; estado, credenciales y claves en el scratchpad y borrados).
   ANTES: 57 filas en 11 consultas (entre ellas E06 1, E13 14 y E14 14; más 1 que halló la prueba de modelo). DESPUÉS: 32 filas
   en 9 consultas, todas documentadas; D01-D10 en 0 las dos veces.
3. `docs/database/normalizacion-v7.md`: formas normales con ejemplos de esta base, método y comando, resultado de cada consulta,
   clasificación de cada hallazgo (redundancia controlada ya documentada / excepción de diseño ya documentada / incumplimiento
   real), lo corregido, lo pendiente con su riesgo y la revisión de las tablas y columnas nuevas de la V7 (contacto y datos de
   factura de `pc_builds` explicados como instantánea del visitante).
4. Correcciones seguras en `V7WebPlatform` (id sin cambios): 25 CHECK de dominio en tablas anteriores a la V7 (14 estados y 11
   listas cerradas, desde los enums con `BillingChecks.In<TEnum>`) y el índice `ix_warranty_claims_tenant_id_serial_number_id`.
   Probadas además sobre la base ya cargada con la migración anterior (en una transacción deshecha): se aplican sin error.
   `has-pending-model-changes` «No changes», `db_init.sql` regenerado (+182 líneas, 0 borradas), ERD §11.7, guía de migraciones
   §11 y diseño §10 al día.
5. Prueba automática `NormalizationTests` (5 pruebas; 3 con `MINV_TEST_PG`): lee las consultas del script y la lista de
   excepciones del informe, falla ante una fila nueva o una excepción que ya no aparece, comprueba que cada consulta detecta
   una tabla mal diseñada y dos incoherencias de datos, y que todo enum del modelo tiene su CHECK (esta halló
   `movement_types.domain`).
6. Quedan 7 incumplimientos menores documentados y sin corregir (ninguno mezcla empresas ni pierde datos): ver «Pendientes».

## Contratos (nombres exactos)

- SCRIPT `scripts/verificar_normalizacion.sql` (UTF-8, LF). Preámbulo psql `\set ON_ERROR_STOP on`, `\pset pager off`,
  `\pset null '—'`. Cada bloque: línea `-- @comprobacion <id> · <problema|informativa> · <título>`, comentario de la regla,
  línea `\echo '<id> · <clase> · <título>'` y UNA sentencia `SELECT` que devuelve `tabla | columna | detalle` (en I01, indicador |
  — | valor). Ids: `E01`…`E19`, `I01`…`I03`, `D01`…`D10` (en ese orden). Ejecutar con el dueño o un superusuario (con un rol sujeto a
  RLS las D salen vacías). Requiere PostgreSQL 15+ (`indnullsnotdistinct`). Esquemas revisados: los 10 de M-INV; se excluye
  `iam.__ef_migrations_history`.
- TÍTULOS: E01 Tablas sin clave primaria · E02 Tablas de negocio sin tenant_id · E03 Tablas con tenant_id sin la política RLS de
  la empresa · E04 Tablas de sucursal sin branch_isolation · E05 Tablas append-only sin trigger (o con privilegios de más) · E06
  FK sin índice que la cubra · E07 FK a tablas de negocio que no son compuestas con tenant_id · E08 FK entre tablas de sucursal
  que no incluyen la sucursal · E09 Columnas de texto sin límite · E10 Columnas cuyo nombre sugiere un total o un contador
  guardado · E11 Columnas JSON · E12 Claves naturales sin restricción de unicidad · E13 Columnas de estado sin CHECK · E14
  Columnas enumeradas sin CHECK · E15 Restricciones CHECK duplicadas · E16 Columnas anulables que forman parte de una unicidad ·
  E17 Grupos repetidos en columnas · E18 Columnas que copian un dato de la tabla que ya referencian · E19 Columnas booleanas que
  un CHECK iguala a otra columna · I01 Resumen del esquema · I02 Sucursal anulable: dato de contexto · I03 Instantáneas
  deliberadas · D01 existencias = movimientos (`v_conservation_breaches`) · D02 reservado = reservas activas · D03
  `v_transfer_breaches` · D04 `v_serial_breaches` · D05/D06/D07 estado de armados y carritos, transferencias y casos RMA = el de
  alguna fila del último instante de su bitácora · D08 `outgoing_mail_dispatch.attempts` = filas de intentos y «Sent» con un
  intento exitoso · D09 ninguna entrega del outbox de una ronda posterior a `rounds` · D10 sin ranura solo en carritos.
- Reglas de detección que otros deben conocer: tabla de sucursal = `branch_id` (o `from_branch_id`/`to_branch_id`) NOT NULL; libro
  = tiene `created_at` y no `updated_at` (convención ERD §2); índice que cubre una FK = empieza por sus columnas en cualquier orden
  (parcial solo con filtro `<col> IS NOT NULL`); CHECK de dominio = `col IN (…)` (o igualdad a un literal, o `BETWEEN` en E14); E14
  mira códigos cortos (varchar ≤ 40 o int2/int4) con nombre `kind|type|direction|outcome|channel|action|environment|scope|method|mode|domain`.
- INFORME `docs/database/normalizacion-v7.md`, tabla «Excepciones documentadas» entre `<!-- excepciones:inicio -->` y
  `<!-- excepciones:fin -->`: filas `| <id> | `esquema.tabla` | `columna` o — | <clasificación> | <motivo> |`; clasificación exactamente
  `Redundancia controlada ya documentada`, `Excepción de diseño ya documentada` o `Incumplimiento real…`; sin «|» dentro de las
  celdas. 32 filas (E02 2, E04 3, E09 3, E10 7, E11 5, E12 4, E14 4, E18 1, E19 3).
- PRUEBAS `MINV.Infrastructure.Tests.NormalizationTests` (`IClassFixture<PostgresFixture>`):
  `El_script_tiene_una_consulta_comentada_de_solo_lectura_por_regla_y_excepciones_documentadas` (sin PostgreSQL),
  `Toda_columna_de_un_enum_tiene_un_CHECK_con_sus_valores` (sin PostgreSQL, sobre el modelo de EF),
  `La_base_migrada_cumple_las_reglas_de_normalizacion_salvo_las_excepciones_documentadas`,
  `Las_consultas_de_estructura_detectan_una_tabla_mal_disenada` y `Las_redundancias_controladas_cuadran_con_los_datos_de_prueba`
  (con `MINV_TEST_PG`; la última siembra `SeedOptions("NORMAL", Days: 8, Seed: 7, Billing: false)`). Ayudas internas:
  `NormalizationTests.Checks()`, `DocumentedExceptions()`, records `Check` y `Finding`.
- MIGRACIÓN `20260929025923_V7WebPlatform` (mismo id): en `Up`, después de `ck_audit_logs_canal`, 25 `AddCheckConstraint` y, después
  del último índice del correo, `CreateIndex ix_warranty_claims_tenant_id_serial_number_id`; en `Down`, justo después de
  `V7DropGuards`, `DropIndex` y los 25 `DropCheckConstraint`. `.Designer.cs` y `MINVDbContextModelSnapshot.cs` con el modelo nuevo.
  CHECK nuevos: `ck_fiscal_periods_estado`, `ck_journal_entries_estado`, `ck_accounts_tipo`, `ck_physical_counts_estado`,
  `ck_stock_adjustments_estado`, `ck_stock_reservations_estado`, `ck_stock_transfer_events_estado`, `ck_movement_types_dominio`,
  `ck_goods_receipts_estado`, `ck_purchase_orders_estado`, `ck_purchase_returns_estado`, `ck_supplier_invoices_estado`,
  `ck_supplier_addresses_tipo`, `ck_invoices_estado`, `ck_pos_sessions_estado`, `ck_sales_orders_estado`,
  `ck_customer_addresses_tipo`, `ck_cash_movements_sentido`, `ck_products_trazabilidad`, `ck_audit_logs_resultado`,
  `ck_hardware_tokens_tipo`, `ck_fiscal_packages_tipo` (`document_type IN (1, 3)`), `ck_siat_service_calls_ambiente` y
  `ck_siat_sync_runs_ambiente` (`environment IN (1, 2)`) y `ck_warranty_claim_events_estado`. Valores exactos en el ERD §11.7.
- CONFIGURACIONES tocadas (solo `HasCheckConstraint`/`HasIndex`): Accounting (`Account`, `FiscalPeriod`, `JournalEntry`), Billing
  (`FiscalPackage` en `ContingencyConfigurations.cs`, `SiatSyncRun` y `SiatServiceCall` en `SiatCatalogConfigurations.cs`), Catalog
  (`Product`), Iam (`AuditLog`, `HardwareToken`), Inventory (`MovementType`, `PhysicalCount`, `StockAdjustment`, `StockReservation`,
  `StockTransferEvent`), Purchasing (`GoodsReceipt`, `PurchaseOrder`, `PurchaseReturn`, `SupplierInvoice`, `SupplierAddress`), Sales
  (`Invoice`, `PosSession`, `SalesOrder`, `CustomerAddress`, `CashMovement`), Service (`WarrantyClaim`: índice con nombre
  `HasIndex(…, "ix_warranty_claims_tenant_id_serial_number_id")`; `WarrantyClaimEvent`). `BillingChecks` solo cambió su resumen.
- CIFRAS de una base V7 migrada: 157 tablas, 432 FK, **244 CHECK** (antes 219), 480 índices únicos, 155 `tenant_isolation`, 64
  `branch_isolation`, 32 libros, 6 SECURITY DEFINER. `scripts/db_init.sql`: 158 `CREATE TABLE`.
- DOCUMENTOS: ERD §11.7 (nuevo), `.claude/database-migration-guide.md` §11 (punto 4, recuento de CHECK, consulta previa de 25 filas
  y cómo correr la comprobación), `docs/architecture/plataforma-web-v7.md` §10 (fila «Normalización»).

## Desviaciones del diseño

- Más comprobaciones que las 14 pedidas: E08 (FK de sucursal, regla B-02), E17 (1FN), E18 y E19 (3FN), I01-I03 y las 10 de datos
  D01-D10, que demuestran que cada redundancia controlada cuadra.
- Se corrigieron también 11 listas cerradas sin CHECK (E14), no solo los estados: mismo riesgo nulo y mismo mecanismo. Una la
  encontró la prueba de modelo (`inventory.movement_types.domain`), no el script; se agregó `domain` al patrón de E14.
- Tercera clasificación «Excepción de diseño ya documentada» para lo que no es redundancia ni incumplimiento (la regla no
  aplica: tablas de plataforma, directorio de sucursales, datos de contacto que no son clave, catálogos del SIN, textos legales).
- La lista de excepciones vive en el informe (tabla marcada) y la prueba la lee de ahí: una sola lista para el documento y la
  prueba.
- Sin guardia propia para los CHECK nuevos: si una fila editada a mano no cumpliera, el CHECK detiene la migración entera
  (transacción) y la guía trae la consulta previa; así la migración no suma otra copia fija de las listas.
- Documentos fuera de la lista del paquete: ERD §11.7 y una fila del diseño §10.
- Operación: las operaciones de EF se generaron con migraciones temporales (`B4cTemporal`, `B4cTemporal2`), se copiaron a
  `V7WebPlatform` (y su `.Designer.cs`) y se borraron sus archivos; nunca se usó `migrations remove` (se conecta a la base).

## Pendientes

- COMMIT: el wip `5563b40` tiene `Persistence/Migrations/20260929112801_B4cTemporal.cs` y `.Designer.cs`; en el árbol están
  borrados y el próximo commit DEBE incluir ese borrado (con esos archivos, las restricciones se crearían dos veces).
- Incumplimientos menores sin corregir (detalle y riesgo en `normalizacion-v7.md` §6): `billing.fiscal_packages.messages` (texto
  sin límite con JSON del SIN); `integration.api_key_scopes.scope` (lista cerrada `ApiScopes.All` sin CHECK); booleanas
  derivables `fiscal_documents.is_reverted`, `outgoing_mail_attempts.succeeded` y `webhook_endpoints.is_active`; estados de dos
  valores derivables `pos_sessions.status` e `invoices.status`. Todos protegidos por CHECK o por el caso de uso; quitarlos cambia
  entidades y contratos de otros paquetes.
- Base `minv` de la tienda EN LÍNEA (sigue en la V6): antes de `minv migrate`, correr la consulta previa de la guía §11 (25 filas
  en 0) y, después, `scripts/verificar_normalizacion.sql` (solo las 32 excepciones). No se ejecutó nada contra localhost:5432.
- Repetir `NormalizationTests` y las pruebas de PostgreSQL contra la instancia local cuando haya credenciales (como indicó B4a).
- CHANGELOG y guías de la versión: mencionar la comprobación de normalización (paquete de documentos).

## Pruebas

Todo desde `D:\Proyectos Claude 2\Sistema Inventario\ZP-MINV-V7`, `dotnet ef` con `DOTNET_ROLL_FORWARD=Major` y `MINV_DB` apuntando al
clúster temporal (ninguna orden se conectó a otra base), pruebas con `--no-build` después de compilar. «Clúster temporal» =
PostgreSQL 16.15 portátil (`%LOCALAPPDATA%\M-INV\postgresql-16\bin`), `initdb` en el scratchpad, localhost:55439, roles `minv_app`
y `minv_server` NOLOGIN, contraseña aleatoria solo en el scratchpad; detenido (`pg_ctl stop -m fast`) y borrado al final.

- `dotnet build MINV.sln -nologo -v q` (inicial): «Compilación correcta. 0 Advertencia(s) 0 Errores».
- Base «antes» (`minv migrate` → «✔ Base de datos al día: … 20260929025923_V7WebPlatform»; `minv datos-prueba --codigo TECHZONE
  --dias 20 --dias-facturacion 10 --siat-estado/--credenciales/--integracion <scratchpad>` con una `MINV_INTEGRATION_KEYS` aleatoria
  → código 0, «✔ Datos de prueba listos en 99 s»; salida filtrada y enmascarada, archivos borrados):
  `psql -X -v ON_ERROR_STOP=1 -d minv_b4c -f scripts/verificar_normalizacion.sql` → código 0 (327 ms): E02 2, E04 3, E06 1, E09 3,
  E10 7, E11 5, E12 4, E13 14, E14 14, E18 1, E19 3; el resto y D01-D10 en 0.
- `dotnet ef migrations add B4cTemporal …` y `B4cTemporal2` → «Done.»; sus operaciones (26) y su `.Designer.cs` se copiaron a
  `V7WebPlatform` y sus archivos se borraron.
- `dotnet ef migrations has-pending-model-changes --context MinvWriteDbContext --project <infra> --startup-project <infra> --no-build`
  (Debug y Release): «No changes have been made to the model since the last migration.»
- `db_init.sql` como `tools/build_v3.ps1` (build Release + `migrations script --idempotent … --configuration Release --no-build` +
  cabecera, LF, UTF-8 sin BOM): «158 sentencias CREATE TABLE»; `git diff --stat -- scripts/db_init.sql`: 182 inserciones, 0 borrados.
- `dotnet test tests/MINV.Infrastructure.Tests --no-build --filter "FullyQualifiedName~NormalizationTests.Toda_columna"` (primera vez):
  «Con error: 1» — «Columnas de un enum sin CHECK de dominio: inventory.movement_types.domain (MovementDomain)». Corregido.
- `dotnet test tests/MINV.Infrastructure.Tests --no-build --filter "FullyQualifiedName~ModelTests|FullyQualifiedName~NormalizationTests.Toda_columna"`:
  «Correctas! - Con error: 0, Superado: 68, Omitido: 0, Total: 68».
- Las 26 órdenes nuevas (extraídas de `dotnet ef migrations script 20260927173304_V6Storefront 20260929025923_V7WebPlatform`) sobre la
  base «antes» ya cargada, en `BEGIN … ROLLBACK`: 25 «ALTER TABLE» + 1 «CREATE INDEX» sin errores, «restricciones nuevas validas sobre
  los datos: 25».
- Base «después» (nueva, misma carga → «✔ Datos de prueba listos en 81 s»: 1638 movimientos, 235 documentos fiscales, 17 armados y
  carritos, 17 transferencias, 7 RMA, 1738 series, 26 reservas activas, 9 correos, 1906 filas de auditoría): el script → código 0
  (391 ms): E02 2, E04 3, E09 3, E10 7, E11 5, E12 4, E14 4, E18 1, E19 3 (32, las de la tabla de excepciones); E06, E13 y D01-D10
  en 0; I01: 157 tablas, 432 FK, 244 CHECK, 480 únicos, 155 / 64 políticas, 32 libros, 6 SECURITY DEFINER.
- Consulta previa de la guía §11 sobre la base «después»: 25 filas, todas en 0. Recuento de CHECK de la guía: 244.
- `dotnet test tests/MINV.Infrastructure.Tests --no-build --filter "FullyQualifiedName~NormalizationTests"` sin `MINV_TEST_PG`:
  «Correctas! - Con error: 0, Superado: 2, Omitido: 3, Total: 5».
- Lo mismo con `MINV_TEST_PG` (clúster temporal): «Correctas! - Con error: 0, Superado: 5, Omitido: 0, Total: 5» (55 s).
- `dotnet test tests/MINV.Infrastructure.Tests --no-build` completo con `MINV_TEST_PG`: «Con error: 1, Superado: 303, Omitido: 0,
  Total: 304» (9 m 56 s). Pasan todas las de PostgreSQL (incluidas `V7WebPlatformPostgresTests`, que migran una base de la V6 con
  datos, revierten y vuelven a migrar con los CHECK nuevos, y los datos de prueba con facturación). La única falla:
  `DemoWorkspaceTests.La_demostracion_es_Tech_Zone_Gaming_y_permite_ingresar_con_cada_rol` «La demostración tardó 115,9 s» (en
  paralelo con las pruebas de PostgreSQL). Sola, sin `MINV_TEST_PG`: «Con error: 1 … Total: 1» (25 s) con «Solo 943 series»
  (línea 89): la falla anterior que documentó B4b (umbral fijo de 1000 series que depende del día; hoy martes). Es en memoria, donde
  los CHECK no existen: no la causa este paquete.
- `dotnet build MINV.sln -nologo -v q` (final): «Compilación correcta. 0 Advertencia(s) 0 Errores».
- No ejecutado: `MINV.Integration.Tests`, `MINV.DesktopClient.Tests`, `MINV.Domain.Tests` y `MINV.Application.Tests` (sin cambios en su
  área; corren en memoria, donde los CHECK no aplican), `tools\build_v3.ps1` completo, nada contra localhost:5432 ni la base `minv`,
  Docker ni la web.
