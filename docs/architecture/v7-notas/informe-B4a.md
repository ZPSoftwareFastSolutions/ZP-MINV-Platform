# Informe del paquete B4a · versión 7.0.0-alpha.1 y migración V7WebPlatform (V7)

## Resumen

Paquete B4a terminado sobre la rama Inventario-V7, sin commits. Partí del avance confirmado en `3ab5bea`: la migración generada
`20260929025923_V7WebPlatform.cs` (+ `.Designer.cs`) y la instantánea ya contenían TODO el modelo actual (carrito, cuentas y
correo): `has-pending-model-changes` respondió «No changes» antes de tocar nada, así que NO la quité ni la regeneré; la completé.

1. VERSIÓN 6.0.0-alpha.1 → **7.0.0-alpha.1**: `Directory.Build.props`, las 4 imágenes de `deploy/docker-compose.yml` y los 2 textos de
   `tools/docker_local.ps1`. `appsettings.json` (gateway, nube, escritorio) y `ApiGatewayApp.cs` no citan la versión: sale del
   ensamblado (`ServerHosting.Version`). Ninguna prueba citaba 6.0.0-alpha.1 (usan `ServerHosting.Version`); el «6.0.0» que las
   pruebas de la V6 pasan a `LoginCommand` es la versión del cliente de ESA prueba y no se cambió. `package.json` de la web, CHANGELOG
   y documentos de versiones anteriores sin tocar. Con la versión mayor 7, la web (que envía `CLIENT_VERSION = '7.0.0'`) ya pasa la
   comprobación de versión mayor del servidor.
2. MIGRACIÓN: resumen de la clase y llamadas en `Up` (`V7Guard` primero, `V7Backfill` antes de los CHECK del armado, `V7Guards` al
   final) y en `Down` (`V7DropGuards` primero, `V7DropSlotDefault` al final). Parcial nuevo `20260929025923_V7WebPlatform.Sql.cs`:
   guardia del rol CLIENTE, relleno `kind = 'Build'` con verificación y retiro del valor provisional, append-only en el correo y
   sus intentos, RLS `tenant_isolation` (por descubrimiento) y `branch_isolation` RESTRICTIVA en `integration.outgoing_mails`,
   trigger «ranura nula solo en un carrito», trigger «el tipo no cambia», `integration.claim_outgoing_mails` con el SQL exacto de B3b,
   permisos `account.*` + rol CLIENTE + matriz en las empresas existentes y privilegios de `minv_app` / `minv_server`. El CHECK del
   canal `web` ya venía en la parte generada.
3. PRUEBAS Y DOCUMENTOS: `ModelTests` y `PostgresIntegrationTests` al día (157 tablas en 10 esquemas, listas V7, canal `web`);
   3 pruebas nuevas de modelo y la clase nueva `V7WebPlatformPostgresTests` (3 pruebas contra PostgreSQL). ERD §11 nuevo (cada tabla y
   columna nueva). `scripts/db_init.sql` regenerado con el comando exacto de `tools/build_v3.ps1` (solo +480 líneas: las migraciones
   publicadas no cambiaron). Guía de migraciones §6 y §11 y diseño §10 (dos filas) al día. `minv verify` exige los mínimos de la V7.
4. POSTGRESQL: el sistema de permisos NEGÓ leer `%LOCALAPPDATA%\M-INV\credenciales-bd-local.txt` (ni siquiera los nombres de las
   claves), así que NO usé el PostgreSQL de localhost:5432 ni la base `minv`. Para probar de verdad levanté un clúster PostgreSQL 16
   TEMPORAL y aislado (mismos binarios portátiles de `%LOCALAPPDATA%\M-INV\postgresql-16\bin`, `initdb` en la carpeta temporal de la
   sesión, puerto 55439 solo en localhost, contraseña aleatoria propia, roles `minv_app` y `minv_server` NOLOGIN creados para que se
   ejecuten los bloques de privilegios). Lo detuve y lo borré al terminar. Hay que repetir las pruebas contra la instancia local
   (comando en «Pendientes»).
5. MIGRACIÓN SOBRE UNA COPIA CON DATOS DE LA V6 (verificada dos veces): (a) la prueba `V7_la_migracion_rellena_el_tipo_...` (base
   temporal hasta `V6Storefront`, armados de la V6 por SQL, migrar, comprobar, revertir, volver a migrar); (b) a mano con el CLI: base
   temporal `minv_v7_copia` con `minv datos-prueba --dias 20 --sin-facturacion` (TECHZONE: 11 armados, 100 líneas, 10 reservas de stock,
   6 publicados, 1552 movimientos, 17 transferencias, 1728 series) + un carrito con una pieza sin ranura → reversa a la V6 (153 tablas,
   151/63/30, la pieza pasó a `Peripheral`) → `minv migrate` → 157 tablas, 155/64/32, los 11 armados `Build`, sin valor por defecto,
   CLIENTE + `account.*` + matriz, reservas y publicaciones intactas, `minv verify --codigo TECHZONE` «base de datos correcta». Base
   borrada.
6. RESULTADOS: `dotnet build MINV.sln` 0 advertencias, 0 errores; sin cambios de modelo pendientes; `MINV.Infrastructure.Tests`
   completo SIN `MINV_TEST_PG` 254 superadas / 2 con error / 23 omitidas (279) y CON `MINV_TEST_PG` 277 / 2 / 0 (279). Las 2 fallas son
   anteriores a este paquete (rol CLIENTE sin usuario de prueba, regla P-13). Detalle en «Pruebas».

## Contratos (nombres exactos)

- MIGRACIÓN: id `20260929025923_V7WebPlatform`, clase `MINV.Infrastructure.Persistence.Migrations.V7WebPlatform`; archivos
  `Persistence/Migrations/20260929025923_V7WebPlatform.cs` (generado + llamadas), `.Designer.cs` (sin cambios) y `.Sql.cs` (nuevo).
  Migración anterior: `20260927173304_V6Storefront`.
- LISTAS (`internal static readonly`, en `V7WebPlatform`): `NewTablesV7` = `sales.customer_accounts`, `integration.outgoing_mails`,
  `integration.outgoing_mail_dispatch`, `integration.outgoing_mail_attempts`; `BranchTablesV7` = `integration.outgoing_mails`;
  `AppendOnlyTablesV7` = `integration.outgoing_mails`, `integration.outgoing_mail_attempts`; `SchemasV7` (los 10 esquemas).
- DATOS (copias fijas, una prueba las compara con el dominio): `AccountPermissions` = (`account.manage`, `account.reserve` con las
  descripciones de `PermissionCodes.All`); `AccountRolePermissions` = (ADMIN, account.manage), (ADMIN, account.reserve), (CLIENTE,
  account.manage), (CLIENTE, account.reserve); `CustomerRoleCode = "CLIENTE"`, `CustomerRoleName = "Cliente web"` (rol `is_system =
  true`); `DowngradeSlot = "Peripheral"`.
- MÉTODOS del parcial: `V7Guard`, `V7Backfill`, `V7Guards`, `V7DropGuards`, `V7DropSlotDefault` (privados, estáticos).
- FUNCIÓN `integration.claim_outgoing_mails(p_limit integer, p_lease_seconds integer) RETURNS TABLE (tenant_id uuid,
  outgoing_mail_id uuid)`: el SQL EXACTO del contrato de B3b (plpgsql, SECURITY DEFINER, `SET search_path = pg_catalog, integration`,
  `#variable_conflict use_column`, FOR UPDATE SKIP LOCKED, límite 1..500, arrendamiento 30..3600 s); `REVOKE ALL ... FROM PUBLIC`;
  `GRANT EXECUTE ... TO minv_server` (solo si el rol existe; `minv_app` NO la ejecuta). Llamada del despachador sin cambios.
- TRIGGER `trg_pc_build_line_slot`: `BEFORE INSERT OR UPDATE OF slot, pc_build_id ON sales.pc_build_lines FOR EACH ROW WHEN (NEW.slot
  IS NULL) EXECUTE FUNCTION sales.minv_pc_build_line_slot()`; si el armado padre (visible) no es `Cart`: P0001 «M-INV: cada pieza del
  armado <número> debe tener su ranura (solo un carrito admite productos sin ranura)». Sin armado visible deja que la FK o la RLS
  rechacen la fila.
- TRIGGER `trg_pc_build_kind_immutable`: `BEFORE UPDATE OF kind ON sales.pc_builds FOR EACH ROW WHEN (OLD.kind IS DISTINCT FROM
  NEW.kind) EXECUTE FUNCTION sales.minv_pc_build_kind_immutable()`; P0001 «M-INV: el tipo de <número> (armado o carrito) no cambia
  después de crearlo». Asignar el mismo valor no dispara. Por EF, P0001 llega como `DomainException("db.rule", mensaje)`.
- GUARDIA DE `Up`: aborta (P0001) con «M-INV V7: % empresas ya tienen un rol CLIENTE que no es el del sistema o que tiene permisos del
  personal …» si existe un rol `CLIENTE` con `is_system = false` o con algún permiso que no empiece con `account.`.
- RELLENO: `kind` se agrega con el valor provisional `''`, `UPDATE ... SET kind = 'Build' WHERE kind = ''`, `ALTER COLUMN kind DROP
  DEFAULT` y verificación («M-INV V7: % armados quedaron sin tipo»). `buyer_*` quedan NULL. No hay triggers que pausar.
- REVERSA (`Down`): guardia «M-INV V7: no se puede volver a la V6: % registros de la auditoría son del canal web …» (la auditoría es
  append-only); quita función y triggers; `UPDATE sales.pc_build_lines SET slot = 'Peripheral' WHERE slot IS NULL`; borra matriz,
  rol CLIENTE y permisos `account.*`; desactiva (`is_active = false`) a los usuarios cuyo ÚNICO rol era CLIENTE; la parte generada
  borra las 4 tablas y las columnas; `V7DropSlotDefault` quita el `DEFAULT ''` que deja el `AlterColumn` generado. Los carritos quedan
  como armados de la V6 (con su número RES-…).
- PRIVILEGIOS (si existen los roles): `minv_app` y `minv_server` con SELECT, INSERT, UPDATE, DELETE en las 4 tablas nuevas, salvo
  UPDATE, DELETE y TRUNCATE revocados en `integration.outgoing_mails` e `integration.outgoing_mail_attempts`. No hizo falta USAGE
  nuevo (`sales` e `integration` ya lo tenían desde la V4).
- CIFRAS de una base migrada: **157 tablas en 10 esquemas** (`sales` 28, `integration` 10, `billing` 27, `service` 2); 155
  `tenant_isolation`; 64 `branch_isolation` RESTRICTIVAS (58 por sucursal + 6 entre sucursales); 32 `trg_append_only` y 32
  `trg_append_only_truncate`; 6 funciones SECURITY DEFINER (`resolve_api_key`, `resolve_session`, `claim_deliveries`, `refresh_all`,
  `siat_active_tenants`, `claim_outgoing_mails`). `scripts/db_init.sql`: 158 sentencias CREATE TABLE (157 + historial).
- VERSIÓN: `7.0.0-alpha.1`; imágenes `minv-cloudserver:7.0.0-alpha.1`, `minv-apigateway:7.0.0-alpha.1`,
  `minv-siatsimulator:7.0.0-alpha.1`, `minv-webcatalog:7.0.0-alpha.1`. `ServerHosting.Major` = 7.
- CLI: `minv verify` exige `MinTables = 157`, `MinLedgers = 32`, `MinRls = 155`, `MinBranch = 64` (antes 152/29/150/62).
- PRUEBAS (nombres): `ModelTests.El_modelo_tiene_157_tablas_en_10_esquemas` (renombrada, + 10 tablas en `integration`);
  `ModelTests.Las_listas_de_las_migraciones_coinciden_con_el_modelo` (+ V7); nuevas `ModelTests.Los_datos_de_la_plataforma_web_de_la_migracion_coinciden_con_el_dominio`,
  `ModelTests.El_correo_y_las_cuentas_de_cliente_respetan_sucursal_empresa_y_unicidad` y
  `ModelTests.La_migracion_V7_rellena_el_tipo_protege_el_carrito_y_crea_la_funcion_del_correo` (sin PostgreSQL: revisa el script que
  genera EF, el orden de guardia, relleno, CHECK y triggers, la función y la reversa); `ModelTests.AllBranchTables` y
  `AllAppendOnlyTables` incluyen la V7; `PostgresIntegrationTests.Las_migraciones_crean_157_tablas_triggers_RLS_por_sucursal_y_vistas`
  (renombrada); clase nueva `MINV.Infrastructure.Tests.V7WebPlatformPostgresTests` con
  `V7_las_tablas_nuevas_tienen_RLS_append_only_y_el_carrito_sus_reglas_en_la_base`,
  `V7_el_correo_solo_se_ve_desde_su_sucursal_y_la_cola_se_reclama_solo_con_la_funcion` y
  `V7_la_migracion_rellena_el_tipo_de_una_base_V6_con_datos_y_su_guardia_protege_el_rol_CLIENTE`.
- DOCUMENTOS: `docs/database/ERD-MINV-V3.md` §11 (11.1 carrito, 11.2 cuentas, 11.3 correo y función, 11.4 sucursal/append-only/
  normalización, 11.5 datos de la migración, 11.6 roles); `.claude/database-migration-guide.md` §6 (listas V6 y V7) y §11 (V6 → V7).

## Desviaciones del diseño

- No se quitó ni se regeneró la migración del intento anterior: ya tenía todo el modelo (sin cambios pendientes). Solo se agregaron
  el resumen y las llamadas al parcial; el `.Designer.cs` y la instantánea no cambiaron.
- `kind` se rellena con el patrón de `channel` de la V6 (valor provisional `''` + UPDATE + verificación + DROP DEFAULT) en vez de
  `defaultValue: "Build"` en el `AddColumn` (sugerencia de B1). Mismo resultado y la columna queda sin valor por defecto, como el modelo.
- Guardia nueva (no estaba en el diseño): un rol CLIENTE previo que no sea de sistema o tenga permisos del personal detiene la
  migración (P-03: el registro asigna ese rol a cualquier visitante). En una base de la V6 no existe ese rol: no se dispara.
- Reversa más completa que la generada: guardia de la auditoría `web`, piezas sin ranura a `Peripheral` (como ya las muestra el
  escritorio, ajuste de B1) y retiro del `DEFAULT ''` que el `AlterColumn` generado dejaba en `slot`.
- Privilegios según la convención de las migraciones anteriores (SELECT/INSERT/UPDATE/DELETE en las tablas nuevas y revocados en los
  libros): `minv_app` y `minv_server` también tienen DELETE en `outgoing_mail_dispatch` y `customer_accounts` (B3b pedía solo
  SELECT/INSERT/UPDATE para la cola).
- Fuera de la lista del paquete, por coherencia: umbrales de `minv verify` (V7), el mensaje de `tools/build_v3.ps1` («V7: 157
  tablas»), la guía de migraciones (§6 y §11) y dos filas del diseño §10 (triggers y relleno/guardia).
- Pruebas de PostgreSQL contra un clúster temporal propio (puerto 55439) y no contra localhost:5432: se negó el permiso para leer el
  archivo de credenciales. Las 23 pruebas de PostgreSQL (las 20 que ya existían y las 3 nuevas) pasaron allí.
- Pruebas existentes cambiadas solo para ponerlas al día con el modelo, sin debilitarlas: recuentos 153→157, 30→32, 151→155, 63→64,
  5→6 funciones SECURITY DEFINER (con `claim_outgoing_mails`), el InlineData de `ck_audit_logs_canal` con `'web'` (texto que dio B2) y
  la comparación de listas de la V4.2 y la V6, que ahora excluye las tablas de la V7 y compara la V7 aparte.

## Pendientes

- REPETIR contra el PostgreSQL local (no pude leer las credenciales). En PowerShell, armando la cadena sin imprimirla:
  `$env:MINV_TEST_PG = '<cadena de un rol que puede crear bases en localhost:5432>'; dotnet test tests/MINV.Infrastructure.Tests`
  (o con `--filter "FullyQualifiedName~V7WebPlatformPostgresTests|FullyQualifiedName~PostgresIntegrationTests"`). Solo crea y borra
  bases temporales `minv_test_*` / `minv_v7mig_*`. Si `minv_server` y `minv_app` existen en esa instancia, la primera prueba V7
  comprueba además que solo `minv_server` puede ejecutar la función.
- EN ROJO, anteriores a este paquete (dependen de los datos de prueba P-13 / del rol CLIENTE de B2, no los toqué):
  `DemoWorkspaceTests.La_demostracion_es_Tech_Zone_Gaming_y_permite_ingresar_con_cada_rol` («Collections differ … "CLIENTE"»),
  `LocalDataSeederTests.Genera_una_empresa_completa_y_coherente_con_usuarios_de_cada_rol` («[6]: Item: Tuple ("CLIENTE", "Cliente
  web") … Filter not matched») y, en el escritorio, `BusinessScreenTests.Catalogo_reportes_contabilidad_y_usuarios_cargan_con_sus_datos`
  («Expected: 7 Actual: 8» en `users.Roles.Count`, línea 190).
- INESTABLE (B3b): `SmtpMailSenderTests.El_tiempo_maximo_rige_tambien_el_envio_asincrono` falló 1 vez en una corrida completa
  («Assert.Equal() Failure: Strings differ»); sola pasó 3 de 3 y en las dos corridas completas finales pasó.
- La base `minv` de la tienda EN LÍNEA sigue en la V6. Para llevarla a la V7: respaldo (`pg_dump -Fc`), roles `minv_server` y
  `minv_app` ya creados, `minv migrate --conexion "<cadena de minv_owner>"` (guía §11) y después configurar `Minv:Web:*` en el
  servidor en la nube y, si se quiere correo, `Minv:Mail:*` en el gateway. Las imágenes 7.0.0-alpha.1 no se construyeron (no se tocó
  Docker).
- La reversa a la V6 se niega si la auditoría ya tiene filas del canal `web`: en ese caso, restaurar el respaldo previo.
- `docs/database/normalizacion-v7.md` y `scripts/verificar_normalizacion.sql` (citados en el diseño §10) NO existen; no estaban en la
  lista de B4a.
- `CHANGELOG.md`, `CLAUDE.md`, `README.md`, `GUIA-DE-INICIO.md` y `docs/deployment/*` siguen diciendo 6.0.0-alpha.1 (documentación de
  la versión en curso, para el paquete de documentos). `package.json` de la web: lo actualiza el equipo de la web.
- Opcional (B3b): comprobar en `ServerHosting.VerifyDatabaseAsync` del gateway `has_function_privilege(...'integration.claim_outgoing_mails(integer, integer)', 'EXECUTE')`.
- `tools/docker_local.ps1`: solo cambió el texto de la versión; sigue pendiente el riesgo R-05 de B3b (reescribe `deploy/.env`).

## Pruebas

Todo desde `D:\Proyectos Claude 2\Sistema Inventario\ZP-MINV-V7`, `dotnet ef` con `DOTNET_ROLL_FORWARD=Major`, pruebas con
`--no-build` después de compilar. «Clúster temporal» = PostgreSQL 16 propio en localhost:55439 (ver Resumen 4), ya borrado.

- `dotnet build MINV.sln -nologo -v q` (final): «Compilación correcta. 0 Advertencia(s) 0 Errores».
- `dotnet ef migrations has-pending-model-changes --context MinvWriteDbContext --project "src/2. Infrastructure/MINV.Infrastructure"
  --startup-project "src/2. Infrastructure/MINV.Infrastructure" --no-build`: «No changes have been made to the model since the last
  migration.» (antes y después de completar la migración).
- `db_init.sql` como `tools/build_v3.ps1`: `dotnet build <infra> -c Release` + `dotnet ef migrations script --idempotent --context
  MinvWriteDbContext --project <infra> --startup-project <infra> --configuration Release --no-build --output <tmp>` + cabecera, CRLF→LF,
  UTF-8 sin BOM: «158 sentencias CREATE TABLE»; `git diff --stat scripts/db_init.sql`: 480 inserciones, 0 borrados.
- `dotnet test tests/MINV.Infrastructure.Tests --no-build --filter "FullyQualifiedName~ModelTests"`: «Correctas! - Con error: 0,
  Superado: 67, Omitido: 0, Total: 67».
- Con `MINV_TEST_PG` (clúster temporal): `dotnet test tests/MINV.Infrastructure.Tests --no-build --filter
  "FullyQualifiedName~V7WebPlatformPostgresTests"`: «Correctas! - Con error: 0, Superado: 3, Omitido: 0, Total: 3» (18 s).
- `dotnet test tests/MINV.Infrastructure.Tests --no-build` SIN `MINV_TEST_PG` (final): «Con error: 2, Superado: 254, Omitido: 23,
  Total: 279» (4 m 30 s). Las 2: `DemoWorkspaceTests.La_demostracion_es_Tech_Zone_Gaming_y_permite_ingresar_con_cada_rol` y
  `LocalDataSeederTests.Genera_una_empresa_completa_y_coherente_con_usuarios_de_cada_rol` (ver «Pendientes»). Una corrida anterior
  igual dio «Con error: 3, Superado: 253, Omitido: 23» por la prueba inestable de `SmtpMailSenderTests`.
- `dotnet test tests/MINV.Infrastructure.Tests --no-build` CON `MINV_TEST_PG` (clúster temporal, final): «Con error: 2, Superado: 277,
  Omitido: 0, Total: 279» (5 m 8 s). Las mismas 2. Pasan todas las de PostgreSQL (V4, V4.1, V4.2, V6, datos de prueba con facturación
  y las 3 de la V7). Otra corrida previa dio lo mismo (2 / 277 / 0).
- Versión (otras baterías): `dotnet test tests/MINV.Integration.Tests --no-build`: «Correctas! - Con error: 0, Superado: 45, Total:
  45» (a las 02:38 hora local). `MINV.Domain.Tests` 290/290, `MINV.Application.Tests` 100/100, `MINV.Hardware.Tests` 19/19,
  `MINV.DesktopClient.Tests` «Con error: 1, Superado: 46, Total: 47» (la de `users.Roles.Count`, anterior a este paquete).
- Copia de la V6 con datos (clúster temporal, a mano, base `minv_v7_copia` borrada al final): `minv migrate` → `minv datos-prueba
  --dias 20 --sin-facturacion` («Datos de prueba listos en 52 s») → carrito RES-CM-000001 con una pieza sin ranura (SQL) →
  `dotnet ef database update 20260927173304_V6Storefront --connection …` («Reverting migration '20260929025923_V7WebPlatform'. Done.»):
  153 tablas, 151 `tenant_isolation`, 63 `branch_isolation`, 30 append-only, sin columna `kind`, pieza en `Peripheral`, `slot` NOT
  NULL sin valor por defecto, sin rol CLIENTE ni `account.*` → `minv migrate`: 157 tablas, 155 / 64 / 32, los 11 armados `kind =
  'Build'`, `kind` sin valor por defecto, 0 con datos de factura, `slot` admite nulo sin valor por defecto, rol CLIENTE y 2 permisos
  con la matriz (ADMIN 2, CLIENTE 2), 3 funciones y 2 triggers de la V7, `ck_audit_logs_canal` con `'web'`, 10 reservas activas y 6
  publicados como antes, EXECUTE de la función: `minv_server` sí, `minv_app` no, PUBLIC no → `minv verify --codigo TECHZONE`: «157
  tablas … 32 libros … RLS en 155 … sucursal en 64 … Conservación: 1552 movimientos, 0 … Transferencias: 17, 0 … Series: 1728, 0 …
  RESULTADO: base de datos correcta» (corrido antes de subir los mínimos de `minv verify`; los valores medidos son justo los mínimos
  nuevos de la V7).
- Final: `dotnet build MINV.sln --no-incremental -nologo -v q`: «0 Advertencia(s) 0 Errores»; después, `ModelTests` 67/67 y
  `has-pending-model-changes` «No changes …» otra vez.
- No ejecutado: nada contra localhost:5432 ni la base `minv`; Docker; `tools\build_v3.ps1` completo; recorrido en navegador.
