# Reglas de arquitectura · M-INV V3 (.NET 8 + PostgreSQL, Clean Architecture y DDD)

> **Documento normativo** para personas y agentes que trabajen en la V3 (`MINV.sln`, `src/1. Core` a `src/4. Tools`,
> `tests/MINV.*`). **DEBE** = obligatorio · **NO DEBE** = prohibido · **PUEDE** = permitido.
> Las reglas de la V1.2 (`excel-architecture-rules.md`) y de la V2.1 (`v2-concurrency-rules.md`) siguen vigentes para
> los libros de Excel; esta prevalece en la V3.

## 0. Principio rector

M-INV sigue siendo **CQRS y append-only**: el inventario solo cambia registrando hechos (movimientos inmutables) y todo
lo demás (stock, alertas, pedido, tablero) es una proyección. La V3 lleva ese principio a una base de datos
transaccional multi-empresa con cliente de escritorio y punto de venta.

```text
 3. Presentation   MINV.DesktopClient (WPF/MVVM) · 4. Tools: MINV.Cli (minv)
        │ envía comandos y consultas (MediatR)
 1. Core           MINV.Application (casos de uso, puertos, tubería: validación → RBAC/licencia → auditoría)
        │ usa
                   MINV.Domain (entidades ricas, reglas de stock; CERO dependencias)
        ▲ implementan los puertos
 2. Infrastructure MINV.Infrastructure (EF Core + Npgsql, multi-tenant, importador V2.1) · MINV.Hardware (ESC/POS)
```

## 1. Reglas

### A-01 · Dependencias hacia adentro
- `MINV.Domain` NO DEBE referenciar ningún paquete ni proyecto.
- `MINV.Application` solo referencia el dominio, MediatR, FluentValidation y `Microsoft.EntityFrameworkCore` (para
  `DbSet`/LINQ asíncrono a través de `IMinvDbContext`); NO DEBE referenciar Npgsql, WPF ni hardware.
- Infraestructura, hardware y presentación implementan los puertos de la aplicación (`IMinvDbContext`, `IClock`,
  `IPasswordHasher`, `ILicenseService`, `IAuditTrail`, `IReceiptPrinter`, `IBarcodeScanner`).

### A-02 · Cero anemia
- Las reglas de negocio viven en las entidades: `StockLevel` (movimientos, poka-yoke, reservas, ajuste por conteo),
  `Product`/`ProductVariant` (variantes, códigos de barras, empaques, impuestos, semáforo), `PhysicalCount`,
  `PosSession`, `JournalEntry`, `UserCredential`, `ProductStockPolicy`.
- Las propiedades tienen `private set`; el estado cambia solo por métodos con nombre de negocio que validan con
  `Guard` y lanzan `DomainException` con un código estable.
- Los casos de uso orquestan (cargan, llaman al dominio, guardan); NO DEBEN reimplementar reglas del dominio.

### A-03 · Concurrencia optimista (OCC)
- Toda tabla transaccional implementa `IConcurrencyAware` (`uint RowVersion` → `xmin` de PostgreSQL).
- Un conflicto se traduce a `ConcurrencyConflictException` (también la violación de unicidad concurrente). Los casos de
  uso que modifican existencias DEBEN reintentar (máximo 3) releyendo el estado: dos cajas que venden la última unidad
  no pueden dejar el stock en negativo.
- NO DEBEN usarse bloqueos pesimistas ni niveles de aislamiento serializables como mecanismo general.

### A-04 · Multi-tenant
- Toda entidad hereda de `BaseEntity` (o `Entity`) y tiene `TenantId`. Únicas excepciones documentadas:
  `Tenant` y `LicenseModule` (`PlatformEntity`).
- EF Core aplica filtros globales por `TenantId`; toda FK entre entidades de negocio es compuesta con el tenant; el
  interceptor rechaza escribir filas de otro tenant; PostgreSQL aplica Row Level Security (`minv.tenant_id`).
- NO DEBE usarse `IgnoreQueryFilters()` fuera de la plataforma (aprovisionamiento, soporte) y siempre con revisión.

### A-05 · Auditoría inmutable (append-only)
- `StockMovement`, `AuditLog`, `AccessLog`, `CashMovement`, `Payment`, `ExchangeRate` y `AverageCostHistory`
  implementan `IAppendOnly`: nunca se actualizan ni se borran (interceptor + triggers + privilegios).
- Todo error de inventario se corrige con un movimiento compensatorio (AJUSTE), como en la V1 y la V2.1.
- Todo comando que escribe implementa `IAuditableRequest` y queda en `AuditLogs` con su resultado (Succeeded,
  Rejected, Failed), también cuando se rechaza (la auditoría usa un contexto propio y nunca interrumpe la operación).

### A-06 · Base de datos 5FN
- Una tabla por concepto; sin columnas derivables de otras (salvo las redundancias controladas documentadas en
  `docs/database/ERD-MINV-V3.md`: `tenant_id` y el estado materializado de `stock_levels`).
- Relaciones N:M en tablas propias; reglas multi-tabla con triggers; arcos exclusivos con `CHECK num_nonnulls`.
- Nombres `snake_case`, un esquema por contexto, restricciones con prefijo y ≤ 63 caracteres.

### A-07 · Migraciones
- El modelo es Code-First: los cambios se hacen en el dominio y en `Persistence/Configurations`, y se crea una migración
  con `dotnet ef migrations add` (guía: `.claude/database-migration-guide.md`).
- NO DEBE editarse una migración ya publicada; el SQL propio de PostgreSQL va en migraciones con `migrationBuilder.Sql`.
- `scripts/db_init.sql` es un artefacto generado por `tools/build_v3.ps1` y DEBE regenerarse con cada migración.

### A-08 · Paridad con la V2.1
- `StockRules` y `StockProjection` son la tercera implementación de las reglas de la V2.1 (Python y Office Scripts):
  semáforo, alertas, cobertura, ranking y pedido. Si cambian, DEBE cambiar también la documentación y la prueba
  `V21MigrationTests.Paridad_V21_V3_...` DEBE seguir pasando con el libro real de la V2.1.
- La aritmética de cantidades es `Quantities.Round6` (= `r6` de la V2.1).

### A-09 · Presentación y hardware
- WPF con MVVM: las vistas no contienen lógica de negocio; las tablas grandes usan virtualización (`Recycling`).
- Las contraseñas no se enlazan a propiedades (se leen del `PasswordBox` solo al ingresar o guardar y se borran).
- El hardware recibe documentos ya codificados (ESC/POS) a través de `IReceiptPrinter`; el escáner entrega códigos
  que la aplicación resuelve por SKU o por código de barras.
- Sistema visual (V3.1): los colores salen SIEMPRE de la paleta (`Theme/Palette.*.xaml`) con `DynamicResource` (o
  `Ui.BrushKey` desde código); NO DEBEN escribirse colores fijos en las vistas salvo sobre el degradado de marca. Los
  estilos viven en `Theme/Controls.xaml` y los textos visibles están en español (`Services/Formats.cs`).
- Las pantallas envían los casos de uso por `AppServices.SendAsync` (de a uno, con el rastreo limpio) y muestran los
  errores como avisos, nunca como cuadros técnicos. Un color o mensaje de estado en la vista (p. ej. el poka-yoke
  rojo sangre) es solo guía: la validación que manda es la del dominio.

### A-10 · Licencias comerciales
- Los módulos vendibles (`iam.modules`: DATA_ENGINE, DESKTOP_CLIENT, POS_HARDWARE, RBAC, SLA_SUPPORT) se habilitan por
  empresa en `iam.tenant_modules`; los casos de uso de un módulo se marcan con `[RequiresModule]` y la tubería los
  bloquea si el módulo no está activo y vigente.

### A-11 · Definición de terminado (DoD)
Un cambio en la V3 está terminado solo si:
1. `tools/build_v3.ps1` termina sin fallas: compila la solución con advertencias como errores, ejecuta todas las
   pruebas (y las de PostgreSQL si `MINV_TEST_PG` está definida), verifica que no falten migraciones y regenera
   `scripts/db_init.sql`.
2. La documentación refleja el cambio (este documento, el ERD, la guía de migración, `CHANGELOG.md`).
3. Si cambió el cliente de escritorio: `tools/build_v3.ps1 -Capturas` regenera `docs/product/capturas/v3.1`, las
   capturas se revisaron (tema claro y oscuro) y `docs/product/escritorio-v3.1.md` está al día.

### A-12 · Modo demostración
- La demostración vive solo en memoria (EF Core InMemory) y se llena con el importador de la V2.1: NO DEBE escribir en
  disco ni en PostgreSQL, y su contraseña es aleatoria en cada ejecución (nunca versionada ni mostrada).
- Usa los mismos casos de uso, la misma tubería y las mismas guardas que producción; lo que no existe en memoria (Row
  Level Security, triggers, transacciones reales) se prueba contra PostgreSQL con `MINV_TEST_PG`.
- La interfaz DEBE mostrar siempre que se está en la demostración (distintivo en la barra superior).

### A-13 · Base local y datos de prueba
- `tools/bd_local.ps1` instala PostgreSQL portátil en `%LOCALAPPDATA%\M-INV` y `minv datos-prueba` (`LocalDataSeeder`)
  genera la empresa de prueba. Los datos DEBEN crearse con los casos de uso (tubería completa: validación, permisos,
  poka-yoke, auditoría y contabilidad), nunca insertando filas a mano: así son coherentes por construcción.
- Las contraseñas de prueba y de PostgreSQL son aleatorias en cada carga y viven solo en el equipo
  (`usuarios-prueba.txt`, `credenciales-bd-local.txt`): NO DEBEN versionarse ni escribirse en la documentación.
- Cada cambio en un caso de uso de escritura DEBE seguir pasando `LocalDataSeederTests` (memoria) y
  `Los_datos_de_prueba_respetan_todas_las_restricciones_de_PostgreSQL` (con `MINV_TEST_PG`): PostgreSQL valida CHECK,
  arcos exclusivos y triggers que la memoria no conoce.
- Las imágenes de producto se guardan en `catalog.product_images` (PNG/JPEG ≤ 1 MB); el cliente las reduce antes de
  enviarlas y las muestra como miniaturas en caché (`ImageCache`).

## 2. Checklist para agentes

- [ ] ¿El dominio ganó una dependencia? → rechazar (A-01).
- [ ] ¿Una regla de negocio quedó en un handler o en la vista? → moverla a la entidad (A-02).
- [ ] ¿Una tabla nueva sin `TenantId`, sin FK compuesta o sin filtro? → corregir (A-04).
- [ ] ¿Se actualiza o borra un movimiento, pago o log? → rechazar: compensatorio (A-05).
- [ ] ¿Una columna nueva se puede derivar de otras? → quitarla o documentar la redundancia (A-06).
- [ ] ¿Cambió el modelo sin migración o sin regenerar `db_init.sql`? → `tools/build_v3.ps1` (A-07).
- [ ] ¿Cambió el semáforo, la cobertura o el pedido? → paridad con la V2.1 (A-08).
- [ ] ¿Una vista con un color fijo o un texto técnico? → paleta y `Formats` (A-09); ¿cambió la interfaz? → capturas (A-11).
