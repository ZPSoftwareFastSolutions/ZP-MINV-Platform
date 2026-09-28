# ZP-MINV-Platform · Contexto para agentes

M-INV es el sistema de inventarios B2B de Z&P Software Fast Solutions: libros de Excel arquitectados como aplicación
transaccional inmutable (CQRS, append-only), preparados para migrar a SQL/.NET.

Versión en desarrollo: **6.0.0-alpha.1** en la rama `Inventario-V6` (sobre `Inventario-V5`): **tienda web conectada**. El
catálogo web de la V5 (`src/3. Presentation/MINV.WebCatalog`, Vite + React) deja el mock y consume la **API pública de tienda**
del API Gateway (`/storefront/v1`, sin API Key: principal técnico `tienda-web` del rol `TIENDA_WEB` de la empresa
`Minv:Storefront:TenantCode`, sucursal `BranchCode`, CORS `AllowedOrigins`, 300 lecturas y 10 reservas por minuto por IP)
sobre la MISMA base en la nube del escritorio: instantánea del catálogo (categorías, marcas, productos con ficha, precio,
disponibilidad = existencias − reservado, imágenes, armados publicados), **reservas de armados** (`PcBuild` canal `Web`,
estado `Reserved`, contacto, `ARM-WEB-000001`, 48 h, una `StockReservation` por línea —tercer origen del arco—, idempotentes
por `Idempotency-Key`, 409 `storefront.insufficient_stock`), consulta y cancelación con el teléfono, vencimiento en segundo
plano (`StorefrontReservationExpiryService`), venta en caja que **consume** la reserva y bitácora append-only
`sales.pc_build_events`. Migración `V6Storefront`: **153 tablas en 10 esquemas**. Reglas: `.claude/v6-storefront-rules.md`
(S-01…S-10); diseño: `docs/architecture/tienda-web-conectada-v6.md`; contrato: `docs/integration/storefront-api-v1.md`;
tablas: `docs/database/ERD-MINV-V3.md` §10. Fase B (fusionada y verificada de punta a punta en la fase C): la web consume la API solo desde
`3-infrastructure/http` (`VITE_API_URL`, por defecto `http://localhost:5090`; `VITE_API_URL=mock` usa los datos de la V5;
`.env.example`; `deploy/Dockerfile.webcatalog` con `ARG VITE_API_URL`) con estados de carga y error, disponibilidad
Disponible / Últimas / Reservado / Agotado, «Reservar armado» y la página «Mi reserva»; el escritorio muestra canal,
contacto y vencimiento en Armador de PC › Cotizaciones con Reservar / Liberar / Vender en caja / Publicar, «Reservado: n» en
stock, catálogo y caja y la tarjeta «Reservas web activas» (`docs/product/escritorio-v6.md`). Paso a paso:
`docs/deployment/inicio-rapido-v6.md`; guía para todos: `GUIA-DE-INICIO.md` §7.

Versión anterior: **5.0.0-alpha.1** en la rama `Inventario-V5` (sobre `Inventario-V4.2`): **catálogo web**
`src/3. Presentation/MINV.WebCatalog` (Vite 8 + React 19 + TypeScript + Tailwind 4, arquitectura limpia con
`1-domain`/`2-application`/`3-infrastructure`/`4-presentation`, `src/architecture.test.ts` vigila las capas y que nadie use
red ni storage): inicio, catálogo con filtros, ficha del producto y «Armá tu PC» sobre datos de muestra generados con
`tools/generar_catalogo_web.py` (`npm run generar-catalogo`). Guía: `docs/product/catalogo-web-v5.md`.

Versión anterior: **4.2.0-alpha.1** en la rama `Inventario-V4.2` (sobre `Inventario-V4.1`): **edición
Tecnología**, M-INV EXCLUSIVO para tiendas de computadoras, componentes, periféricos, consolas (PS4 y PS5, Xbox Series X
y Series S, Nintendo Switch y Switch 2), videojuegos, accesorios, redes y software, **con datos y no con código a
medida**: fichas técnicas tipadas por categoría (herencia, facetas, plataformas y condición como especificaciones de
opción), **productos serializados** con número de serie o IMEI (Luhn) en la recepción, la venta, las transferencias y las
devoluciones, con bitácora append-only por serie y `inventory.v_serial_breaches` (series en stock = stock), series en la
factura del SIN (`numeroSerie`/`numeroImei`, línea dividida si pasa de 1500 caracteres), **garantía derivada** (venta +
meses), **garantías y RMA** (`service.warranty_claims`, reposición con REPOSICIÓN POR GARANTÍA y asiento 5.1.10),
**armador de PC** (`PcCompatibility`, cotización con precios congelados que se cobra en la caja), tablero Tecnología y
**tema gaming** (oscuro por defecto). Migración `V42TechRetail`: PostgreSQL de **152 tablas en 10 esquemas** (esquema
`service`). Empresa de prueba **Tech Zone Gaming S.R.L.** (`TECHZONE`, sucursales CM La Paz, CB Cochabamba y SC Santa
Cruz) desde `Seeding/Tecnologia/catalogo-tecnologia.json`; la demostración es la misma empresa en memoria (8 días,
~17 s en frío; T-09 prevalece sobre A-12). Reglas: `.claude/v42-tech-rules.md`; diseño:
`docs/architecture/edicion-tecnologia-v4.2.md`; paso a paso: `docs/deployment/inicio-rapido-v4.2.md`.

Versión anterior: **4.1.0-alpha.1** en la rama `Inventario-V4.1` (sobre `Inventario-V4.-BaseDeDatosNube`):
**facturación SIAT** de Bolivia, modalidad **Facturación Computarizada en Línea**: factura Compra Venta (sector 1) al
vender (caja, API y transcripción CAFC) y nota Crédito-Débito (sector 24) en las devoluciones, dentro de la misma
transacción que la venta; envío al SIN después del COMMIT (`DispatchFiscalDocumentsCommand`, despachador en
`MINV.CloudServer`), fuera de línea automático con recuperación (CUFD → evento significativo → paquetes → validación),
contingencia manual CAFC, anulación (día 9) y reversión, homologación, libros de ventas y compras. Esquema `billing`
(27 tablas): PostgreSQL de **140 tablas en 9 esquemas**. Cliente SOAP con el contrato en `SiatSoapContract` (confirmar
con el WSDL del piloto) y **simulador del SIN** (en proceso y HTTP `src/4. Tools/MINV.SiatSimulator`, puerto 5095). Los
datos de prueba facturan los últimos 25 días contra el simulador en proceso (estado en
`%LOCALAPPDATA%\M-INV\siat-simulador.json`, token de simulación `MINV_SIAT_TOKEN` en `claves-integracion.txt`); la
demostración factura en memoria. Reglas: `.claude/v41-billing-rules.md`; diseño: `docs/architecture/facturacion-siat-v4.1.md`;
qué falta confirmar con el SIN: `docs/billing/README.md`.

Versión previa: **4.0.0-alpha.1** en la rama `Inventario-V4.-BaseDeDatosNube` (sobre
`Inventario-V3.-BaseDeDatosLocal`): M-INV **multi-sucursal en la nube**. Sucursales aisladas (`IBranchScoped` /
`IInterBranch`, filtros de EF Core, guardas, FK compuestas con la sucursal y RLS RESTRICTIVA con `minv.branch_ids`),
transferencias con mercadería en tránsito (manifiesto por lote, faltantes, asientos 1.1.06 / 2.1.04), servidor en la
nube `src/3. Presentation/MINV.CloudServer` (el escritorio envía sus comandos de MediatR por HTTPS: login, RPC
idempotente), API Gateway B2B `src/3. Presentation/MINV.ApiGateway` (API Keys con alcances, pedidos idempotentes,
webhooks firmados desde un outbox transaccional, OpenAPI), modelo de lectura (`MinvReadDbContext`, esquema
`reporting`), PostgreSQL de **110 tablas en 8 esquemas** (V4.1: 140 en 9; V4.2: 152 en 10) con los roles `minv_owner`, `minv_server` (NOBYPASSRLS) y
`minv_app`. `MINVDbContext` se llama ahora `MinvWriteDbContext` (hay dos contextos: `dotnet ef … --context
MinvWriteDbContext`). Reglas: `.claude/v4-architecture-rules.md`; arquitectura: `docs/architecture/arquitectura-v4.md`.

Versión de la V3: **3.1.0-alpha.1** en la rama `Inventario-V3.-BaseDeDatosLocal` (sobre `Inventario-V3.1`): solución
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
@.claude/v41-billing-rules.md
@.claude/v42-tech-rules.md
@.claude/v6-storefront-rules.md

## Comandos

```powershell
powershell -ExecutionPolicy Bypass -File tools\servidores_locales.ps1 -Accion iniciar   # V6: + tienda web http://localhost:5090/storefront/v1/catalog (-EmpresaTienda TECHZONE -SucursalTienda CM -OrigenTienda http://localhost:5173)
dotnet run --project "src/3. Presentation/MINV.ApiGateway" -- --urls http://localhost:5090 --Minv:Storefront:TenantCode TECHZONE   # V6: gateway con la tienda web (también Minv__Storefront__* por variables de entorno)
dotnet test tests/MINV.Integration.Tests --filter Storefront                    # V6: API pública de tienda de punta a punta (Kestrel + base en memoria)
cd "src\3. Presentation\MINV.WebCatalog"; npm install; npm run dev              # V5/V6: la web en http://localhost:5173 (V6: VITE_API_URL, por defecto http://localhost:5090; =mock usa los datos de la V5; npm test · typecheck · lint · build)
dotnet run --project "src/4. Tools/MINV.Cli" -- migrate --conexion "…"           # V6: base existente de la V4.2/V5 → migración V6Storefront sin recrear (rol minv_owner)
docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d --build   # V6: servidores + webcatalog (MINV_STOREFRONT_TENANT/BRANCH/ORIGIN/HOURS, MINV_WEB_API_URL)
powershell -ExecutionPolicy Bypass -File tools\docker_local.ps1 -Accion subir   # V6: tienda pública en Docker Desktop + túnel trycloudflare (reanudar, enlace, estado, arranque, bajar); docs/deployment/tienda-publica-docker-v6.md
powershell -ExecutionPolicy Bypass -File tools\bd_local.ps1 -Accion recrear     # V4.2: base local con Tech Zone Gaming (TECHZONE): 60 días, series, RMA, armados y facturación (~2 min de carga)
dotnet run --project "src/4. Tools/MINV.Cli" -- datos-prueba --conexion "…"      # V4.2: empresa TECHZONE por defecto (--codigo, --dias 60, --semilla 2026)
dotnet run --project "src/4. Tools/MINV.Cli" -- verify --codigo TECHZONE --conexion "…"   # V4.2: además, series en stock = stock (v_serial_breaches)
& "src\3. Presentation\MINV.DesktopClient\bin\Release\net8.0-windows\M-INV.exe" --capturas "<carpeta temporal>" [--tema oscuro]   # V4.2: capturas 01 a 98 con la demostración (MINV_CAPTURAS_USUARIOS vacía)
.venv\Scripts\python tools\tecnologia\generar_catalogo.py                     # V4.2: regenerar y validar catalogo-tecnologia.json (armados y compatibilidad)
.venv\Scripts\python tools\tecnologia\generar_imagenes_tecnologia.py          # V4.2: ilustraciones propias de los productos (sin logotipos)
.venv\Scripts\python tools\tecnologia\generar_icono_gaming.py [--vista-previa] # V4.2: ícono y logotipo de la edición
.venv\Scripts\python tools\tecnologia\verificar_paleta.py <Theme original> <Theme nueva>   # V4.2: mismas claves y contraste WCAG AA de las paletas
powershell -ExecutionPolicy Bypass -File tools\bd_local.ps1 -Accion recrear     # V4.1: base local + datos de prueba QUE FACTURAN (-SinFacturacion) + claves y token de simulación
powershell -ExecutionPolicy Bypass -File tools\servidores_locales.ps1 -Accion iniciar   # V4.1: simulador del SIN :5095 + CloudServer :5080 + ApiGateway :5090 (detener, estado; -SinSimulador)
dotnet run --project "src/4. Tools/MINV.Cli" -- siat estado|preparar|sincronizar|procesar [--codigo MINV] [--conexion …]   # V4.1: facturación de una empresa
dotnet run --project "src/4. Tools/MINV.Cli" -- siat simulador-estado|simulador-apagar|simulador-encender   # V4.1: corte de internet simulado
dotnet run --project "src/4. Tools/MINV.SiatSimulator" -- --urls http://localhost:5095 --Siat:StateFile <archivo.json>   # V4.1: simulador HTTP del SIN
docker compose -f deploy/docker-compose.yml --profile siat-simulador up -d --build   # V4.1: servidores + simulador del SIN (solo ensayos)
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
dotnet run --project "src/4. Tools/MINV.Cli" -- datos-prueba --conexion "…"      # empresa de prueba en otra base (V4.1: --sin-facturacion, --dias-facturacion, --siat-estado, --simulador)
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

- V6: tienda web conectada: el algoritmo paso a paso `docs/deployment/inicio-rapido-v6.md` · guía para todos
  `GUIA-DE-INICIO.md` §7 · diseño `docs/architecture/tienda-web-conectada-v6.md` · reglas S-01 a S-10
  `.claude/v6-storefront-rules.md` · contrato de la API pública `docs/integration/storefront-api-v1.md` · eventos
  `pcbuild.*` `docs/integration/api-gateway-v1.md` §7.2 · tablas `docs/database/ERD-MINV-V3.md` §10 · nube (gateway con la
  tienda, `webcatalog`, CORS, TLS) `docs/deployment/despliegue-nube-v4.md` §14 · escritorio `docs/product/escritorio-v6.md`
  (capturas 99 a 102 en `docs/product/capturas/v6`, generadas con `tools\build_v3.ps1 -Capturas`) · historial `CHANGELOG.md`
- V5: catálogo web: `docs/product/catalogo-web-v5.md` (páginas, disponibilidad, arquitectura, nota de la conexión V6) ·
  `src/3. Presentation/MINV.WebCatalog/README.md` (variables, modo mock, pruebas)
- V4.2: edición Tecnología: el algoritmo paso a paso `docs/deployment/inicio-rapido-v4.2.md` · diseño (modelo,
  ciclo de vida de la serie, RMA, armador, factura, decisiones y límites) `docs/architecture/edicion-tecnologia-v4.2.md` ·
  reglas T-01 a T-10 `.claude/v42-tech-rules.md` · interfaz `docs/product/escritorio-v4.2.md` (capturas en
  `docs/product/capturas/v4.2`) · tema gaming `docs/product/ux-ui-guidelines.md` §13 · tablas `docs/database/ERD-MINV-V3.md`
  §9 · migración V4.1 → V4.2 `.claude/database-migration-guide.md` §9 · guía para todos `GUIA-DE-INICIO.md` §5
- V4.1: facturación SIAT: el algoritmo paso a paso `docs/deployment/inicio-rapido-v4.1.md` · qué es, investigación del
  SIN y huecos por confirmar `docs/billing/README.md` · del simulador al SIN real (autorización, Fases I-III, producción)
  `docs/billing/puesta-en-produccion-siat.md` · diseño `docs/architecture/facturacion-siat-v4.1.md` · reglas F-01 a
  F-17 `.claude/v41-billing-rules.md` · normativa citada `docs/billing/investigacion-siat/` (00 a 08) · interfaz
  `docs/product/escritorio-v4.1.md` (capturas en `docs/product/capturas/v4.1`) · despachador fiscal en la nube
  `docs/deployment/despliegue-nube-v4.md` §12
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
