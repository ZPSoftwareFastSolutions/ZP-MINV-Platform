# ZP-MINV-Platform · Contexto para agentes

M-INV es el sistema de inventarios B2B de Z&P Software Fast Solutions: libros de Excel arquitectados como aplicación
transaccional inmutable (CQRS, append-only), preparados para migrar a SQL/.NET.

Versión en desarrollo: **3.0.0-alpha.1** en la rama `Inventario-V3`: solución .NET 8 (`MINV.sln`, Clean
Architecture) con PostgreSQL (96 tablas, 5FN, multi-tenant), cliente WPF y hardware ESC/POS; se construyó sobre el
modelo de la V2.1 (importador con verificación de paridad). Reglas: `.claude/v3-architecture-rules.md`.

Versión estable anterior: **2.1.0** en la rama `Inventario-V2.1`: libro **colaborativo** para Microsoft 365 (SharePoint/OneDrive,
Excel para la web) con Office Scripts (`src/office-scripts/`): portadas por rol (Bodega, Ventas, Gerencia), captura por
usuario, consulta por usuario, toma física colaborativa, pedido sugerido, registro de actividad y resumen diario para
Power Automate. La edición local V1.2 (Estándar `.xlsx` y Plus `.xlsm`) sigue en el repositorio (`tools/build_minv.py`,
rama `Inventario-V1.2`). Idioma del producto y la documentación: español.

## Reglas obligatorias

@.claude/excel-architecture-rules.md
@.claude/v2-concurrency-rules.md
@.claude/v3-architecture-rules.md

## Comandos

```powershell
powershell -ExecutionPolicy Bypass -File tools\build_v3.ps1                  # V3: compilar, probar, migraciones, db_init.sql
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

- V3: reglas `.claude/v3-architecture-rules.md` · migraciones `.claude/database-migration-guide.md` · ERD
  `docs/database/ERD-MINV-V3.md` · paso a paso `docs/deployment/inicio-rapido-v3.md`
- Coautoría y fragmentación: `.claude/v2-concurrency-rules.md`
- Acceso paso a paso (demo, producción, uso diario): `docs/deployment/inicio-rapido.md`
- Despliegue y roles: `docs/deployment/sharepoint-rbac-policies.md`
- Modelo de datos: `docs/architecture/data-dictionary-v2.md` (V2) y `docs/architecture/data-dictionary.md` (V1.2)
- Sistema visual y UX: `docs/product/ux-ui-guidelines.md`
- Office Scripts: `src/office-scripts/README.md` · VBA V1.2: `src/macros/README.md`
- Historial: `CHANGELOG.md`
