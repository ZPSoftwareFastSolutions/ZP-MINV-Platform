# Reglas de arquitectura · M-INV V4 (multi-sucursal en la nube, integraciones B2B)

> **Documento normativo** para personas y agentes que trabajen en la V4 (rama `Inventario-V4.-BaseDeDatosNube`:
> `MINV.sln`, `src/1. Core` a `src/4. Tools`, `MINV.CloudServer`, `MINV.ApiGateway`, `tests/MINV.*`).
> **DEBE** = obligatorio · **NO DEBE** = prohibido · **PUEDE** = permitido.
> Las reglas A-01 a A-13 de la V3 (`.claude/v3-architecture-rules.md`) siguen vigentes; esta las amplía y, donde hablen
> de lo mismo (sucursales, transferencias, servidores, integraciones), **prevalece**. Arquitectura explicada:
> `docs/architecture/arquitectura-v4.md`.

## 0. Principio rector

El inventario sigue cambiando SOLO por hechos inmutables (CQRS y append-only), ahora en varias sucursales, por la red y
desde terceros. Cada operación ocurre en una sucursal; lo que viaja entre sucursales está en tránsito y se cuenta una
sola vez; todo lo que sale hacia afuera (webhooks) nace en la misma transacción que el hecho que lo produjo.

```text
 3. Presentation   MINV.DesktopClient (local · nube · demo)   MINV.CloudServer (RPC del escritorio)   MINV.ApiGateway (SOLO B2B)
        │ mismos comandos y consultas (MediatR)                      │ token de sesión mses_…              │ API Key minv_… + alcances
 1. Core           MINV.Application: validación → permisos/licencia → [alcance por sucursal] → caso de uso → auditoría (canal)
                   MINV.Domain: IBranchScoped · IInterBranch · StockTransfer · eventos de dominio · ApiKey · Webhooks
        ▲
 2. Infrastructure MinvWriteDbContext (filtros empresa+sucursal, guardas, outbox, xmin) · MinvReadDbContext (reporting)
                   PostgreSQL: RLS tenant_isolation + branch_isolation (RESTRICTIVA) · SECURITY DEFINER mínimas · roles sin BYPASSRLS
```

## 1. Reglas

### B-01 · Alcance por sucursal: lo calcula el servidor
- El alcance de una sesión (`BranchScope`: todas, las asignadas y la activa) DEBE calcularse en el servidor a partir de
  los roles (`corporate.branches.all` = gerencia global) y de `warehouse.branch_users` (`UserAccess`). NO DEBE aceptarse
  un alcance ni una sucursal enviados por el cliente sin validarlos contra ese cálculo.
- El servidor en la nube DEBE recalcular permisos y alcance en CADA petición; el gateway, en cada autenticación de llave.
- Un usuario sin sucursales asignadas y sin `corporate.branches.all` NO DEBE poder iniciar sesión.
- La sucursal activa decide dónde se registran las operaciones que no indican otra; cambiarla es un comando auditado
  (`SelectBranchCommand`).

### B-02 · Filas de sucursal, directorio corporativo y filtros
- Toda entidad que representa algo que ocurre EN una sucursal (existencias, movimientos, documentos, cajas, asientos,
  costos) DEBE implementar `IBranchScoped`; todo documento ENTRE dos sucursales, `IInterBranch`.
- El directorio corporativo (`Branch`, `Warehouse`, `BranchUser`), el catálogo, clientes, proveedores, usuarios y plan
  de cuentas NO DEBEN filtrarse por sucursal.
- Los filtros globales de EF Core (`ModelConventions`), las guardas de escritura (`MinvSaveChangesInterceptor`), las FK
  compuestas `(tenant_id, branch_id, padre_id)` y la RLS DEBEN cubrir cada tabla de sucursal. Toda FK de un hijo de
  sucursal DEBE incluir `branch_id` (un hijo no puede tener otra sucursal que su padre).
- `branch_id`, `from_branch_id` y `to_branch_id` NO DEBEN cambiar nunca después de insertar.
- NO DEBE usarse `IgnoreQueryFilters()` fuera de procesos de plataforma documentados (despachador, autenticadores en la
  base en memoria), igual que en la regla A-04.
- Los números de documento DEBEN llevar el código de la sucursal (`F-CM-000001`).

### B-03 · Lados de una operación entre sucursales
- En una transferencia, el **origen** crea, despacha y anula; el **destino** recibe. El caso de uso DEBE verificar que
  el lado que actúa está en el alcance (`TransferRules.EnsureAllowed`); la guarda de escritura solo exige que la fila sea
  de alguna de las dos sucursales.
- Un lado NO DEBE leer ni escribir movimientos, existencias o asientos del otro: lo compartido viaja en la propia
  transferencia (líneas, manifiesto por lote, faltantes, bitácora).

### B-04 · Transferencias: máquina de estados y conservación
- Estados `Pending → Dispatched → Received` y `Pending → Cancelled`; las transiciones DEBEN hacerse solo con los métodos
  del agregado `StockTransfer` y cada una DEBE dejar una fila en `stock_transfer_events` y su evento de dominio.
- El despacho DEBE ser completo por línea, con el manifiesto por lote y el costo promedio del origen; la recepción DEBE
  entrar con los lotes del manifiesto y NUNCA más de lo despachado (ni por lote); la diferencia DEBE tener motivo y
  quedar como faltante (`StockTransferDiscrepancy`).
- Despacho y recepción DEBEN correr en UNA transacción explícita con reintento optimista (máximo 3).
- Invariante: Σ salidas = Σ entradas + Σ faltantes + en tránsito. La vista `inventory.v_transfer_breaches` DEBE estar
  vacía. Los reportes consolidados DEBEN contar lo que está en tránsito UNA sola vez.

### B-05 · Contabilidad de las transferencias
- Despacho (asiento del ORIGEN): Debe 1.1.06 Mercadería enviada a sucursales / Haber 1.1.05 Inventario, al costo
  promedio del origen.
- Recepción (asiento del DESTINO): Debe 1.1.05 (valor recibido) + Debe 5.1.09 Mermas (valor del faltante) / Haber 2.1.04
  Mercadería recibida de sucursales (valor despachado).
- Cada sucursal DEBE registrar solo sus propios asientos; en el consolidado 1.1.06 − 2.1.04 DEBE ser igual al valor en
  tránsito. NO DEBE existir un asiento que mezcle cuentas de dos sucursales.

### B-06 · Append-only y deltas compensatorios
- Los 15 libros (`stock_movements`, `audit_logs`, `access_logs`, `cash_movements`, `payments`, `exchange_rates`,
  `average_cost_history`, `stock_transfer_movements`, `stock_transfer_discrepancies`, `stock_transfer_events`,
  `stock_transfer_line_batches`, `outbox_events`, `webhook_deliveries`, `external_orders`, `processed_requests`) DEBEN
  implementar `IAppendOnly` y tener `trg_append_only` y privilegios revocados.
- Toda corrección DEBE ser un registro compensatorio (ajuste, faltante, nuevo intento). NO DEBE guardarse un valor
  derivable (p. ej. «recibido» = despachado − faltantes).
- Un estado que cambia (cola de despacho, estado de la transferencia) DEBE vivir en una tabla mutable separada del hecho
  inmutable, con su bitácora append-only.

### B-07 · Concurrencia optimista
- Toda tabla mutable con disputas posibles DEBE implementar `IConcurrencyAware` (`xmin`). Los casos de uso que cambian
  existencias, transferencias, costos o documentos numerados DEBEN reintentar (máximo 3) releyendo el estado.
- El costo promedio vigente es la fila de mayor `sequence` por (variante, almacén); toda fila nueva DEBE tomar la
  secuencia siguiente con `AverageCosts.RecordAsync`, protegida por el índice único
  `(tenant_id, variant_id, warehouse_id, sequence)`. NO DEBE elegirse el costo vigente por fecha.
- Los errores de PostgreSQL DEBEN traducirse con `PostgresErrors` (unicidad/serialización → conflicto; FK/CHECK/trigger →
  dominio; RLS → acceso denegado). En memoria, `MinvWriteDbContext` incrementa la versión: las pruebas de OCC DEBEN
  poder correr sin PostgreSQL.
- NO DEBEN usarse bloqueos pesimistas ni aislamiento serializable como mecanismo general (A-03); la única excepción es
  `FOR UPDATE SKIP LOCKED` de la cola de webhooks.

### B-08 · Outbox en la misma transacción
- Todo hecho que interese fuera de M-INV DEBE publicarse como evento de dominio (`IHasDomainEvents` o
  `IMinvDbContext.Publish`) y guardarse en `integration.outbox_events` + `outbox_dispatch` dentro del MISMO
  `SaveChanges`. NO DEBE llamarse a un sistema externo desde un caso de uso ni antes del COMMIT.
- Un evento nuevo DEBE tener un código estable en `IntegrationEvents` (`<dominio>.<hecho en pasado>`), documentarse en
  `docs/integration/api-gateway-v1.md` y NO DEBE cambiar el significado de uno publicado (solo agregar campos).
- Las entregas DEBEN tomar la cola con `integration.claim_deliveries` (SKIP LOCKED), registrar cada intento en
  `webhook_deliveries` y respetar 8 intentos con la espera de `WebhookDelivery.BackoffBefore`.

### B-09 · Idempotencia
- Toda operación que llega por la red y cambia datos DEBE ser idempotente: pedidos externos por (API Key, `externalId`)
  con hash del contenido normalizado; comandos del servidor en la nube por `requestId` con hash de tipo + payload.
- El registro de idempotencia DEBE escribirse en la MISMA transacción que la operación. Repetir igual DEBE devolver la
  respuesta original; mismo id con otro contenido DEBE rechazarse (`IdempotencyConflictException`, 422).
- El canal de un pedido externo DEBE salir de la credencial (la llave), nunca del cuerpo de la petición.

### B-10 · Servidor en la nube y API Gateway: cada uno para lo suyo
- El escritorio en modo nube DEBE hablar SOLO con `MINV.CloudServer` (`/api/v1/*`) y NUNCA con el gateway; el gateway
  DEBE servir SOLO a integraciones B2B con API Keys.
- El servidor en la nube DEBE ejecutar únicamente `IRequest<>` públicos de `MINV.Application` (`RpcCatalog`), nunca
  `LoginCommand` por RPC ni tipos de otros ensamblados; DEBE exigir la misma versión mayor del cliente
  (`X-MINV-Client-Version`).
- Toda ruta del gateway DEBE exigir un alcance (`RequireAuthorization(ApiScopes.…)`) y enviar el caso de uso por MediatR
  (la tubería vuelve a comprobar permisos, módulo y sucursal). NO DEBE haber lógica de negocio en los endpoints.
- Los errores DEBEN salir con el mapeo único de `RpcCatalog` (400/401/403/404/409/422/500) y sin detalles internos en
  las fallas técnicas; los límites de tasa DEBEN mantenerse (login por IP, cubeta por sesión, límite por IP y por llave).
- Una API nueva DEBE versionarse en la ruta (`/v1`, `/v2`); un cambio incompatible NO DEBE hacerse en una versión
  publicada.

### B-11 · Secretos y salidas a internet
- Contraseñas: PBKDF2 (A-02). API Keys y tokens de sesión: 256 bits de azar, guardados SOLO como SHA-256 y comparados en
  tiempo constante; se muestran UNA vez. NO DEBEN aparecer en logs, auditoría, excepciones ni documentación.
- Los secretos que M-INV necesita en claro (firma de webhooks) DEBEN cifrarse con `ISecretProtector` (AES-256-GCM, clave
  maestra `MINV_INTEGRATION_KEYS` fuera de la base, id de la clave como dato asociado). NO DEBE versionarse ni guardarse
  en la base una clave maestra.
- Toda llamada saliente a una URL de un tercero DEBE usar `SafeWebhookHttp` (solo IP públicas resueltas al conectar, sin
  redirecciones, con tiempo máximo). `AllowPrivateTargets` solo en pruebas.
- Las URL de webhooks DEBEN ser https (http solo loopback) y sin credenciales; las firmas DEBEN llevar marca de tiempo y
  admitir doble firma durante la rotación.

### B-12 · Roles de base de datos y RLS
- Las aplicaciones DEBEN conectarse con roles `NOSUPERUSER NOBYPASSRLS` que NO son dueños de tablas: `minv_server`
  (servidores) y `minv_app` (escritorio directo). `minv_owner` solo migra y administra.
- Los servidores DEBEN verificar al arrancar que su rol no puede saltarse RLS y negarse a arrancar si puede
  (`ServerHosting.VerifyDatabaseAsync`); `MINV_ALLOW_PRIVILEGED_ROLE=1` NO DEBE usarse fuera de desarrollo.
- Toda tabla con `tenant_id` DEBE tener `tenant_isolation`; toda tabla de sucursal, además, `branch_isolation`
  RESTRICTIVA con `iam.branch_visible(…)`.
- `minv.tenant_id` y `minv.branch_ids` son variables de SESIÓN fijadas al abrir la conexión: NO DEBE usarse un pool en
  modo transacción (PgBouncer, Supavisor 6543, RDS Proxy) delante de M-INV.
- Los permisos de un rol nuevo se conceden en la migración solo si el rol existe: los roles DEBEN crearse antes de migrar.

### B-13 · Funciones SECURITY DEFINER
- Solo PUEDEN existir para lo que no se puede hacer antes de conocer la empresa o sin ser dueño: resolver una API Key o
  un token, reclamar la cola de webhooks, refrescar vistas materializadas.
- DEBEN tener `SET search_path` fijo (`pg_catalog` + su esquema), `REVOKE ALL … FROM PUBLIC` y `GRANT EXECUTE` solo a
  `minv_server`; DEBEN recibir parámetros tipados, hacer UNA cosa y NO DEBEN ejecutar SQL dinámico con datos del llamador.
- Cualquier búsqueda por credencial DEBE devolver lo mínimo (id, empresa, hash, vigencia); la comparación del hash se hace
  en la aplicación en tiempo constante.

### B-14 · Modelo de lectura
- Las consultas pesadas de gerencia DEBEN ir por `IReportingReader` / `MinvReadDbContext` (vistas `reporting.v_*`,
  réplica `MINV_DB_READ` si existe), no por la base transaccional de las cajas.
- `MinvReadDbContext` NO DEBE guardar nada. Las vistas materializadas NO DEBEN concederse a los roles de aplicación: solo
  las vistas `security_barrier` filtradas por empresa y sucursal.
- Una vista materializada nueva DEBE tener índice único (para `REFRESH … CONCURRENTLY`) y agregarse a
  `reporting.refresh_all()`; la pantalla DEBE mostrar de cuándo son los datos (`RefreshedAt`).

### B-15 · Migraciones con relleno y listas explícitas
- Hay dos contextos: toda orden `dotnet ef` DEBE llevar `--context MinvWriteDbContext`. `MinvReadDbContext` no tiene
  migraciones: sus vistas las crea el contexto de escritura.
- El SQL propio de una migración DEBE ir en su archivo parcial `<migración>.Sql.cs`. Un relleno DEBE: vigilar la
  precondición (guardia), agregar la columna con valor provisional, rellenar siguiendo la jerarquía, pausar triggers SOLO
  en las tablas que lo necesitan y SOLO durante el relleno, verificar que no quedó ninguna fila sin valor y reactivar los
  triggers.
- Las listas `BranchTables`, `InterBranchTables` y `AppendOnlyTablesV4` DEBEN coincidir con el modelo (`IBranchScoped`,
  `IInterBranch`, `IAppendOnly`) y una prueba DEBE verificarlo. Una tabla nueva de sucursal o append-only DEBE agregarse
  en la lista de una migración NUEVA con su política o trigger; NO DEBE editarse una migración publicada (A-07).
- `scripts/db_init.sql` DEBE regenerarse con cada migración y `docs/database/ERD-MINV-V3.md` DEBE nombrar cada tabla
  (`ModelTests.El_ERD_documenta_todas_las_tablas_del_modelo`).

### B-16 · Módulos comerciales de la V4
- `CLOUD_HA`, `MULTI_BRANCH`, `API_INTEGRATIONS` y `GLOBAL_AUDIT` se habilitan por empresa en `iam.tenant_modules`; los
  casos de uso de escritura de sucursales y transferencias DEBEN llevar `[RequiresModule(MULTI_BRANCH)]`; llaves,
  webhooks y pedidos externos `[RequiresModule(API_INTEGRATIONS)]`; el tablero por sucursal `[RequiresModule(GLOBAL_AUDIT)]`.

### B-17 · Definición de terminado (DoD) de la V4
Un cambio en la V4 está terminado solo si:
1. La solución compila con advertencias como errores y pasan TODAS las pruebas: dominio, aplicación, infraestructura
   (incluidas `ModelTests` y los datos de prueba en memoria), escritorio y **`tests/MINV.Integration.Tests`** (servidor en
   la nube y gateway reales); con `MINV_TEST_PG`, también las de PostgreSQL.
2. `dotnet ef migrations has-pending-model-changes --context MinvWriteDbContext` no encuentra cambios y
   `scripts/db_init.sql` está regenerado.
3. Si cambió el modelo o la seguridad: sobre una copia de una base real se verificó 110+ tablas, políticas
   `tenant_isolation` y `branch_isolation`, triggers append-only, `v_conservation_breaches` y `v_transfer_breaches` vacías.
4. La documentación refleja el cambio: este documento, `docs/architecture/arquitectura-v4.md`, el ERD, la guía de
   migraciones, `docs/integration/api-gateway-v1.md` (si cambió el API o un evento), `docs/deployment/` y `CHANGELOG.md`.
5. Si cambió el escritorio: capturas y guía de la interfaz al día (A-11).

## 2. Checklist para agentes

- [ ] ¿Una entidad nueva ocurre en una sucursal y no implementa `IBranchScoped` (o `IInterBranch`)? → corregir (B-02).
- [ ] ¿Una FK de un hijo de sucursal sin `branch_id`? → FK compuesta `(tenant_id, branch_id, padre_id)` (B-02).
- [ ] ¿El alcance o la sucursal vienen del cliente sin validar? → calcularlo en el servidor (B-01).
- [ ] ¿El destino despacha o el origen recibe? → revisar `TransferRules.EnsureAllowed` (B-03).
- [ ] ¿Se «edita» una cantidad recibida, un intento o un evento? → registro compensatorio (B-06).
- [ ] ¿Un caso de uso que toca stock, costos o números sin reintento optimista? → reintentar ×3 (B-07).
- [ ] ¿Se llama a un webhook o a un tercero desde un caso de uso? → evento de dominio + outbox (B-08).
- [ ] ¿Una operación de red que escribe sin id de idempotencia? → `externalId` / `requestId` (B-09).
- [ ] ¿El escritorio llama al gateway, o una ruta del gateway no exige alcance? → rechazar (B-10).
- [ ] ¿Un secreto, token o llave en un log, en la base sin cifrar o en la documentación? → rechazar (B-11).
- [ ] ¿Un servidor conectado con el dueño, con BYPASSRLS o detrás de un pool en modo transacción? → rechazar (B-12).
- [ ] ¿Una función SECURITY DEFINER nueva sin `search_path` fijo o con EXECUTE para PUBLIC? → rechazar (B-13).
- [ ] ¿Un reporte pesado sobre el contexto de escritura? → modelo de lectura (B-14).
- [ ] ¿Una orden `dotnet ef` sin `--context MinvWriteDbContext`, una tabla nueva fuera de las listas o una migración
      publicada editada? → corregir (B-15).
- [ ] ¿Pasó `tests/MINV.Integration.Tests` y se actualizaron ERD, guías y `CHANGELOG.md`? → DoD (B-17).
