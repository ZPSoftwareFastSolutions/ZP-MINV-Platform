# ZP-MINV-Platform · Contexto para agentes

M-INV es el sistema de inventarios B2B de Z&P Software Fast Solutions: libros de Excel arquitectados como aplicación
transaccional inmutable (CQRS, append-only), preparados para migrar a SQL/.NET.

Versión en desarrollo: **4.0.0-alpha.1** en la rama `Inventario-V4.-BaseDeDatosNube` (sobre
`Inventario-V3.-BaseDeDatosLocal`): M-INV **multi-sucursal en la nube**. Sucursales aisladas (`IBranchScoped` /
`IInterBranch`, filtros de EF Core, guardas, FK compuestas con la sucursal y RLS RESTRICTIVA con `minv.branch_ids`),
transferencias con mercadería en tránsito (manifiesto por lote, faltantes, asientos 1.1.06 / 2.1.04), servidor en la
nube `src/3. Presentation/MINV.CloudServer` (el escritorio envía sus comandos de MediatR por HTTPS: login, RPC
idempotente), API Gateway B2B `src/3. Presentation/MINV.ApiGateway` (API Keys con alcances, pedidos idempotentes,
webhooks firmados desde un outbox transaccional, OpenAPI), modelo de lectura (`MinvReadDbContext`, esquema
`reporting`), PostgreSQL de **110 tablas en 8 esquemas** con los roles `minv_owner`, `minv_server` (NOBYPASSRLS) y
`minv_app`. `MINVDbContext` se llama ahora `MinvWriteDbContext` (hay dos contextos: `dotnet ef … --context
MinvWriteDbContext`). Reglas: `.claude/v4-architecture-rules.md`; arquitectura: `docs/architecture/arquitectura-v4.md`.

Versión anterior: **3.1.0-alpha.1** en la rama `Inventario-V3.-BaseDeDatosLocal` (sobre `Inventario-V3.1`): solución
.NET 8 (`MINV.sln`, Clean Architecture) con PostgreSQL (97 tablas, 5FN, multi-tenant; local con `tools\bd_local.ps1` y
datos de prueba `minv datos-prueba`), hardware ESC/POS y el cliente de escritorio completo `M-INV.exe` (WPF/MVVM:
pantalla de carga, login con demostración en memoria, tablero, stock y catálogo en galería con imágenes, punto de venta,
ventas, clientes, compras, proveedores, reportes, contabilidad, usuarios y roles, registro con poka-yoke, toma física,
alertas, pedido, ficha con kardex, tema claro/oscuro). Se construyó sobre el modelo de la V2.1 (importador con
verificación de paridad). Reglas: `.claude/v3-architecture-rules.md`; interfaz: `docs/product/escritorio-v3.1.md`.

Versión estable anterior: **2.1.0** en la rama `Inventario-V2.1`: libro **colaborativo** para Microsoft 365 (SharePoint/OneDrive,
Excel para la web) con Office Scripts (`src/office-scripts/`): portadas por rol (Bodega, Ventas, Gerencia), captura por
usuario, consulta por usuario, toma física colaborativa, pedido sugerido, registro de actividad y resumen diario para
Power Automate. La edición local V1.2 (Estándar `.xlsx` y Plus `.xlsm`) sigue en el repositorio (`tools/build_minv.py`,
rama `Inventario-V1.2`). Idioma del producto y la documentación: español.

## Reglas obligatorias

@.claude/excel-architecture-rules.md
@.claude/v2-concurrency-rules.md
@.claude/v3-architecture-rules.md
@.claude/v4-architecture-rules.md

## Comandos

```powershell
powershell -ExecutionPolicy Bypass -File tools\bd_local.ps1 -Accion recrear     # V4: base local con 3 sucursales + datos de prueba + claves
powershell -ExecutionPolicy Bypass -File tools\servidores_locales.ps1 -Accion iniciar   # V4: CloudServer :5080 y ApiGateway :5090 (también detener, estado)
powershell -ExecutionPolicy Bypass -File tools\bd_nube.ps1 -Accion preparar -Conexion "<cadena del rol dueño>" [-DatosPrueba]   # V4: PostgreSQL gestionado (también estado)
dotnet run --project "src/3. Presentation/MINV.CloudServer" -- --urls http://localhost:5080   # V4: servidor en la nube (MINV_DB con minv_server)
dotnet run --project "src/3. Presentation/MINV.ApiGateway" -- --urls http://localhost:5090    # V4: API Gateway B2B (MINV_INTEGRATION_KEYS)
dotnet test tests/MINV.Integration.Tests                                       # V4: pruebas de extremo a extremo de ambos servidores
dotnet ef migrations add <Nombre> --context MinvWriteDbContext --project "src/2. Infrastructure/MINV.Infrastructure" --startup-project "src/2. Infrastructure/MINV.Infrastructure" --output-dir Persistence/Migrations   # V4: dos contextos
docker compose -f deploy/docker-compose.yml up -d --build                      # V4: servidores en contenedores
powershell -ExecutionPolicy Bypass -File tools\build_v3.ps1                  # V3: compilar, probar, migraciones, db_init.sql
powershell -ExecutionPolicy Bypass -File tools\build_v3.ps1 -Capturas -Publicar # V3.1: + capturas del cliente + M-INV.exe (dist)
powershell -ExecutionPolicy Bypass -File tools\publicar_escritorio.ps1         # V3.1: solo publicar M-INV.exe
powershell -ExecutionPolicy Bypass -File tools\bd_local.ps1 -Accion recrear     # PostgreSQL local + datos de prueba
dotnet run --project "src/4. Tools/MINV.Cli" -- datos-prueba --conexion "…"      # empresa de prueba en otra base
$env:MINV_TEST_PG = '<cadena postgres>'                                        # V3: activa las pruebas contra PostgreSQL
dotnet run --project "src/4. Tools/MINV.Cli" -- import-v21 --archivo …          # V3: migrar un libro de la V2.1
powershell -ExecutionPolicy Bypass -File tools\build_v2.ps1 -Capturas        # V2: ciclo completo (DoD, regla C-12)
.venv\Scripts\python tools\build_minv_v2.py                                   # V2: solo generar
.venv\Scripts\python tools\office_scripts.py sync                             # copiar lib/comun.ts en cada script
node tests\office-scripts\pruebas.mts                                         # pruebas de los Office Scripts
$env:MINV_TSC = '<ruta de tsc>'                                                # opcional: build_v2 verifica tipos
powershell -ExecutionPolicy Bypass -File tools\build_all.ps1 -Capturas        # V1.2 local: ciclo completo
```

Los libros de `src/` y `releases/` son artefactos generados: los cambios se hacen en `tools/minv2/` (V2), `tools/minv/`
(V1.2 y componentes compartidos) o `src/office-scripts/lib/comun.ts`, y se verifican antes de dar una tarea por
terminada. Los scripts `.ps1` deben ser ASCII (PowerShell 5.1, que además no distingue mayúsculas en nombres de
variables); los Office Scripts, TypeScript sin `any` ni sintaxis no borrable.

## Documentación

- V4: arquitectura `docs/architecture/arquitectura-v4.md` · reglas `.claude/v4-architecture-rules.md` · paso a paso
  `docs/deployment/inicio-rapido-v4.md` · despliegue en la nube (DigitalOcean, AWS RDS, Supabase)
  `docs/deployment/despliegue-nube-v4.md` · guía del integrador B2B `docs/integration/api-gateway-v1.md` · ERD V3 y V4
  `docs/database/ERD-MINV-V3.md` · migraciones (dos contextos, relleno V3 → V4) `.claude/database-migration-guide.md` ·
  interfaz del escritorio `docs/product/escritorio-v4.md` (capturas en `docs/product/capturas/v4`)
- V3.1: interfaz del cliente de escritorio `docs/product/escritorio-v3.1.md` (capturas en `docs/product/capturas/v3.1`)
- V3: reglas `.claude/v3-architecture-rules.md` · migraciones `.claude/database-migration-guide.md` · ERD
  `docs/database/ERD-MINV-V3.md` · paso a paso `docs/deployment/inicio-rapido-v3.md`
- Coautoría y fragmentación: `.claude/v2-concurrency-rules.md`
- Acceso paso a paso (demo, producción, uso diario): `docs/deployment/inicio-rapido.md`
- Despliegue y roles: `docs/deployment/sharepoint-rbac-policies.md`
- Modelo de datos: `docs/architecture/data-dictionary-v2.md` (V2) y `docs/architecture/data-dictionary.md` (V1.2)
- Sistema visual y UX: `docs/product/ux-ui-guidelines.md`
- Office Scripts: `src/office-scripts/README.md` · VBA V1.2: `src/macros/README.md`
- Historial: `CHANGELOG.md`
