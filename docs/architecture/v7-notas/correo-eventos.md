# Mapa V7 · Correo electrónico, outbox y trabajos en segundo plano

> Subsistema leído en la rama `Inventario-V7` (idéntica a la V6 al momento de la lectura). Solo lectura: no se compiló ni se
> ejecutó nada. Rutas relativas a `D:\Proyectos Claude 2\Sistema Inventario\ZP-MINV-Platform`.
> No se abrió `deploy/.env` ni ningún archivo de `%LOCALAPPDATA%\M-INV`.

## 0. Lo esencial en 12 líneas

1. Ya existe un puerto de correo (`IMailSender`) y una implementación SMTP (`SmtpMailSender`, `System.Net.Mail`), pero SOLO lo usa la
   facturación SIAT (entrega de XML + PDF y avisos de anulación/reversión).
2. La configuración SMTP es POR EMPRESA en la tabla `billing.mail_settings` (contraseña cifrada con AES-256-GCM). No existe ninguna
   configuración SMTP por variables de entorno.
3. Cada envío fiscal queda en `billing.fiscal_deliveries` (append-only). Reintentos: máximo 3 por documento, sin espera progresiva.
4. El outbox (`integration.outbox_events` + `integration.outbox_dispatch`) se escribe dentro de `MinvWriteDbContext.SaveChangesAsync`
   y hoy tiene UN solo consumidor: los webhooks (`WebhookDispatcher`).
5. La reserva web ya guarda el correo del cliente (`sales.pc_builds.contact_email`) y ya publica `pcbuild.reserved`, pero NO envía
   ningún correo, y el evento no lleva datos de contacto (a propósito).
6. No hay ningún emisor de correo falso en las pruebas: ningún archivo de `tests/` referencia `IMailSender`.
7. Hay 4 servicios en segundo plano: 3 en el gateway y 1 en el servidor en la nube (más un temporizador en el escritorio).
8. Propuesta V7: cola de correos propia (3 tablas nuevas en `integration`), encolada en el MISMO `SaveChanges` que la reserva, y un
   trabajo `MailDispatcherService` en el gateway con `FOR UPDATE SKIP LOCKED`, reintentos y bitácora append-only.

---

## 1. Puerto de correo e implementación SMTP

### 1.1 Tipos del puerto

Archivo: `src/1. Core/MINV.Application/Abstractions/SiatPorts.cs` (líneas 198 a 208). El puerto vive en el archivo de los puertos del
SIAT aunque no es específico de la facturación.

| Tipo | Definición exacta |
|---|---|
| `MailServer` | `record MailServer(string Host, int Port, bool UseSsl, string? UserName, string? Password, string FromAddress, string FromName)` |
| `MailAttachment` | `record MailAttachment(string FileName, string ContentType, byte[] Content)` |
| `MailMessageSpec` | `record MailMessageSpec(MailServer Server, string To, string Subject, string HtmlBody, IReadOnlyList<MailAttachment> Attachments)` |
| `IMailSender` | `Task SendAsync(MailMessageSpec message, CancellationToken cancellationToken = default)` |

Limitaciones del contrato actual:

- Un solo destinatario (`To` es un `string`); sin CC, CCO ni `Reply-To`.
- Solo cuerpo HTML (`HtmlBody`); no hay alternativa de texto plano.
- No admite cabeceras propias (no se puede fijar `Message-ID` ni una cabecera de idempotencia).
- El servidor viaja DENTRO del mensaje (`MailMessageSpec.Server`), con la contraseña en claro en memoria: quien arma el mensaje
  decide a qué servidor se conecta.
- `MailServer` es un `record`: su `ToString()` generado incluiría `Password`. Hoy nadie lo imprime, pero no está protegido como
  `SiatConnection`, que sí redefine `ToString()`.

### 1.2 Implementación `SmtpMailSender`

Archivo: `src/2. Infrastructure/MINV.Infrastructure/Billing/Mail/SmtpMailSender.cs`.

| Aspecto | Valor en el código |
|---|---|
| Biblioteca | `System.Net.Mail` (`SmtpClient`, `MailMessage`, `Attachment`). No hay MailKit ni MimeKit en ningún `.cs`/`.csproj`. |
| TLS | `EnableSsl = server.UseSsl`. En `SmtpClient` eso es STARTTLS (TLS explícito); no soporta TLS implícito (puerto 465). |
| Autenticación | `UseDefaultCredentials = false`; si `UserName` no está vacío: `new NetworkCredential(server.UserName, server.Password)`. Sin OAuth2. |
| Tiempo de espera | `public static readonly TimeSpan Timeout = 30 s`, asignado a `client.Timeout`. El envío es `await client.SendMailAsync(mail, cancellationToken)`. |
| Codificación | `SubjectEncoding` y `BodyEncoding` en UTF-8; `IsBodyHtml = true`. |
| Remitente | `new MailAddress(server.FromAddress, server.FromName, Encoding.UTF8)`. |
| Destinatario | `mail.To.Add(new MailAddress(message.To))` (una dirección). |
| Adjuntos | `new Attachment(new MemoryStream(content, writable: false), fileName, contentType)`. |
| Conexión | Un `SmtpClient` nuevo por mensaje (`using`); sin reutilización de conexiones. |
| Registro | No registra nada (ni éxito ni error): la excepción sube al que llama. |

### 1.3 Registro en el contenedor de dependencias

| Dónde | Qué registra |
|---|---|
| `src/2. Infrastructure/MINV.Infrastructure/DependencyInjection.cs`, `AddMinvInfrastructure` (línea 46) | `services.TryAddSingleton<IMailSender, Billing.Mail.SmtpMailSender>()` |
| `AddMinvDemoInfrastructure` (memoria) | NO registra `IMailSender`. Los manejadores lo reciben como `null` (parámetro opcional `IMailSender? mailSender = null`). |
| `ServerHosting.AddMinvServerStorage` | `--Minv:Storage=memoria` usa la infraestructura de demostración: gateway y servidor en memoria NO tienen emisor de correo. |

Consecuencia: en las pruebas de integración (`tests/MINV.Integration.Tests/ServerFixtures.cs`, que arrancan con
`--Minv:Storage memoria`) no existe `IMailSender`; hay que registrarlo de forma explícita para probar el correo de la reserva.

### 1.4 De dónde sale la configuración

Por empresa, en la base de datos. No hay variables de entorno de correo en `deploy/docker-compose.yml`, `deploy/.env.example` ni en
`tools/docker_local.ps1`.

Entidad `MailSettings` (`src/1. Core/MINV.Domain/Billing/SiatSettings.cs`, línea 231), tabla `billing.mail_settings`
(`src/2. Infrastructure/MINV.Infrastructure/Persistence/Configurations/Billing/SiatSettingsConfigurations.cs`, línea 79):

| Columna (propiedad) | Tipo / límite | Nota |
|---|---|---|
| `tenant_id` (`TenantId`) | uuid | Clave primaria: UNA fila por empresa |
| `host` (`Host`) | texto ≤ 200 | `Guard.Text` |
| `port` (`Port`) | entero | `CHECK ck_mail_settings_puerto: port BETWEEN 1 AND 65535` |
| `use_ssl` (`UseSsl`) | booleano | |
| `user_name` (`UserName`) | texto ≤ 200, opcional | |
| `password_ciphertext` (`PasswordCiphertext`) | texto ≤ 4000, opcional | Cifrada |
| `password_key_id` (`PasswordKeyId`) | texto ≤ 40, opcional | `CHECK ck_mail_settings_clave: (password_ciphertext IS NULL) = (password_key_id IS NULL)` |
| `from_address` (`FromAddress`) | texto ≤ 254 | `Guard.Email` |
| `from_name` (`FromName`) | texto ≤ 100 | |
| `is_enabled` (`IsEnabled`) | booleano | `Enable()` / `Disable()` |
| `xmin` (`RowVersion`) | | `IConcurrencyAware` |

Casos de uso (`src/1. Core/MINV.Application/Billing/BillingContracts.cs` y `SiatAdminUseCases.cs`):

| Tipo | Detalle |
|---|---|
| `SaveMailSettingsCommand(string Host, int Port, bool UseSsl, string? UserName, string? NewPassword, string FromAddress, string FromName, bool Enabled)` | `[RequiresPermission(PermissionCodes.BillingConfigure)]` y `[RequiresModule(LicenseModuleCodes.FiscalSiat)]`. Auditoría sin la contraseña (`PasswordChanged`); `ToString()` redefinido. |
| `SaveMailSettingsValidator` / `SaveMailSettingsHandler` | Cifra con `SiatAdminSupport.Protect(protector, r.NewPassword, out var keyId)` y `mail.SetPassword(ciphertext, keyId)`. |
| `MailSettingsView(Host, Port, UseSsl, UserName, HasPassword, FromAddress, FromName, IsEnabled)` | Va dentro de `SiatSettingsView.Mail` (`GetSiatSettingsQuery`, permiso `BillingView`). Nunca devuelve la contraseña. |
| `BuyerMail.ServerAsync(IMinvDbContext db, ISecretProtector? protector, CancellationToken ct)` | `internal static` en `FiscalLifecycleUseCases.cs`. Devuelve `MailServer?`: `null` si no hay fila o `IsEnabled` es falso; descifra con `protector.Unprotect(cipher, keyId)`; sin protector lanza `DomainException("mail.no_keys")`. |

Cifrado (`src/2. Infrastructure/MINV.Infrastructure/Services/IntegrationServices.cs`):

- `ISecretProtector` con `CurrentKeyId`, `Protect`, `Unprotect`.
- `AesGcmSecretProtector`: AES-256-GCM, formato `base64(nonce 12 ‖ texto ‖ etiqueta 16)`, el id de la clave como dato asociado.
  Claves en la variable `MINV_INTEGRATION_KEYS` (`id1:base64(32 bytes);id2:...`, la primera es la vigente).
- `UnconfiguredSecretProtector` (cuando la variable no está): todo lanza `AccessDeniedException`.
- `AesGcmSecretProtector.Ephemeral()` en la demostración en memoria.

Estado de la empresa de prueba TECHZONE (`src/2. Infrastructure/MINV.Infrastructure/Seeding/SiatSeeding.cs`, línea 448): el sembrado
guarda el correo DESACTIVADO, con servidor `smtp.<dominio>` de dominio `.example`, puerto 587, sin contraseña. Es decir, hoy TECHZONE
no puede enviar ningún correo.

La pantalla de escritorio que ya edita esta configuración: `src/3. Presentation/MINV.DesktopClient/ViewModels/BillingSettingsViewModel.cs`.

### 1.5 Cómo se prueba hoy

| Pregunta | Respuesta verificada |
|---|---|
| ¿Hay un emisor falso (`IMailSender` de prueba)? | NO. La búsqueda de `IMailSender`, `MailSender` y `MailMessageSpec` en `tests/` no devuelve nada. |
| ¿Se prueba `SmtpMailSender`? | NO hay ninguna prueba que lo ejercite. |
| ¿Qué sí se prueba? | `tests/MINV.Infrastructure.Tests/Billing/D_SiatAdminTests.cs` (línea 65): `SaveMailSettingsCommand` guarda la contraseña cifrada (el texto cifrado no contiene la clave) y `view.Mail is { HasPassword: true, IsEnabled: true }`. |
| | `tests/MINV.Infrastructure.Tests/Billing/D_FiscalDocumentTests.cs` (líneas 88 a 92): `RecordFiscalDeliveryCommand` con canal `Print` y `Email`, y rechazo de un correo inválido. |
| | `tests/MINV.Domain.Tests/Billing/FiscalDocumentTests.cs` (línea 343): constructor de `FiscalDelivery`. |
| | `tests/MINV.Infrastructure.Tests/ModelTests.cs`: `FiscalDelivery` está en las listas append-only y de sucursal. |

---

## 2. Cómo se registra cada envío y sus reintentos

### 2.1 Tabla `billing.fiscal_deliveries`

Entidad `FiscalDelivery : Entity, IBranchScoped, IAppendOnly` (`src/1. Core/MINV.Domain/Billing/FiscalDocument.cs`, línea 600).
Configuración: `FiscalDeliveryConfiguration` en `Persistence/Configurations/Billing/FiscalDocumentConfigurations.cs` (línea 228).

| Columna (propiedad) | Detalle |
|---|---|
| `id` | Clave primaria |
| `tenant_id`, `branch_id` | FK compuesta `(tenant_id, branch_id, document_id)` → `billing.fiscal_documents` |
| `document_id` (`DocumentId`) | Documento fiscal entregado |
| `channel` (`Channel`) | `FiscalDeliveryChannel` como texto ≤ 20: `Email`, `Print`, `Pdf` (`CHECK ck_fiscal_deliveries_canal`) |
| `recipient` (`Recipient`) | ≤ 254, opcional |
| `succeeded` (`Succeeded`) | Resultado del intento |
| `error` (`Error`) | ≤ 500 (se recorta en el constructor) |
| `occurred_at` (`OccurredAt`) | Momento del intento |
| `user_id` (`UserId`) | FK `(tenant_id, user_id)` → `iam.users`, opcional |
| Índice | `(document_id, occurred_at)` |

Está en `V41SiatBilling.AppendOnlyTablesV41` y `BranchTablesV41` (trigger `trg_append_only`, política `branch_isolation`).
Cada reintento es una fila nueva; no existe ninguna tabla de cola ni de estado para el correo fiscal.

### 2.2 Quién escribe en `fiscal_deliveries`

| Origen | Archivo | Comportamiento |
|---|---|---|
| `SendFiscalDocumentEmailHandler` (`SendFiscalDocumentEmailCommand(Guid DocumentId, string? Email = null)`, permiso `BillingIssue`, módulo `FiscalSiat`) | `FiscalLifecycleUseCases.cs` línea 398 | Envío manual. Envía y después registra la entrega; si falla lanza `DomainException("mail.failed")`. Errores previos: `mail.document_state`, `mail.no_recipient`, `mail.not_configured`, `mail.unavailable`. |
| `BuyerMail.NotifyAsync` | `FiscalLifecycleUseCases.cs` línea 496 | Avisos de anulación (`VoidFiscalDocumentHandler`) y reversión (`RevertFiscalVoidHandler`), regla F-10. Nunca lanza: registra la entrega lograda o fallida y devuelve el texto para el usuario. |
| `BuyerMail.SendDocumentAsync` | línea 530 | Arma el correo con XML + PDF. Nunca lanza: devuelve `(bool Ok, string? Error)`. |
| `SiatWorker.SendPendingEmailsAsync` | `SiatWorker.cs` línea 938 | Envío automático en el mantenimiento. |
| `RecordFiscalDeliveryHandler` (`RecordFiscalDeliveryCommand`) | `FiscalDocumentQueries.cs` línea 215 | Solo registra evidencia (impreso, PDF o correo entregado por otro medio); no envía nada. |

Cuando el envío sale bien se agrega además una fila en `billing.fiscal_document_events` con la acción `FiscalDocumentAction.Delivered`.

### 2.3 Reintentos del correo fiscal automático

`SiatWorker.SendPendingEmailsAsync` (se ejecuta al final de `MaintainAsync`):

| Regla | Valor |
|---|---|
| Documentos candidatos | `Status == Valid`, `BuyerEmail != null`, del ambiente activo, creados en los últimos 30 días |
| Condición de reintento | Ninguna entrega `Email` lograda Y menos de `MaxEmailAttempts` (constante privada = 3) entregas `Email` registradas |
| Lote | 20 documentos por pasada (`Take(20)`) |
| Frecuencia | La del mantenimiento: `SiatBackgroundOptions.MaintenanceInterval` = 60 s en el servidor; en el escritorio local, cada tercera ronda de 20 s |
| Espera progresiva | NO existe: los 3 intentos pueden ocurrir en 3 minutos seguidos |
| Aislamiento de fallas | `EachAsync`: la falla de un documento hace `db.ClearTracking()` y sigue con el siguiente |

El conteo de intentos mezcla envíos manuales y automáticos: tres envíos manuales fallidos agotan también el envío automático.

### 2.4 Relación con la regla B-08 / F-03

El correo fiscal se envía DENTRO de los manejadores (`VoidFiscalDocumentHandler`, `RevertFiscalVoidHandler`,
`SendFiscalDocumentEmailHandler`) y dentro del trabajador. La regla F-03 lo admite porque son casos de uso cuyo propósito es la
llamada externa y guardan el resultado después. Ese patrón NO sirve para la reserva: la reserva es una transacción de negocio
(stock + armado + idempotencia) y la regla B-08 prohíbe llamar a un sistema externo antes del COMMIT.

---

## 3. El outbox de integración

### 3.1 Cómo llega un evento a `outbox_events` en el mismo SaveChanges

Archivo: `src/2. Infrastructure/MINV.Infrastructure/Persistence/MinvWriteDbContext.cs`.

| Paso | Código |
|---|---|
| 1a. Un agregado acumula el evento | Interfaz `IHasDomainEvents` (`DomainEvents`, `ClearDomainEvents()`) en `src/1. Core/MINV.Domain/Common/Entity.cs`. La implementan `StockTransfer`, `FiscalDocument` y `PcBuild`. |
| 1b. O un caso de uso lo publica | `IMinvDbContext.Publish(IDomainEvent)` → lista privada `_published`. Lo usan `SalesUseCases.cs` (líneas 351 y 539), `SalesReturnUseCases.cs` (235) y `PurchasingUseCases.cs` (297). |
| 2. Antes de guardar | `SaveChangesAsync` y `SaveChanges` llaman a `WriteOutbox()` y luego a `BumpInMemoryVersions()`. |
| 3. `WriteOutbox()` | Junta `_published` + los `DomainEvents` de `ChangeTracker.Entries<IHasDomainEvents>()`, los limpia y, por cada uno, agrega `new OutboxEvent(tenantId, e.EventType, e.BranchId, JSON, e.OccurredAt)` y `new OutboxDispatch(tenantId, row.Id, e.OccurredAt)`. |
| 4. Serialización | `JsonSerializer.Serialize(e, e.GetType(), EventJson)` con `JsonSerializerDefaults.Web` (camelCase) y `JsonStringEnumConverter`. |
| 5. Confirmación | Las filas viajan en el mismo `base.SaveChangesAsync`: se confirman o se deshacen junto con el cambio que las produjo. |
| 6. Reintento optimista | `ClearTracking()` también vacía `_published`: un reintento no duplica eventos. |

Dos detalles que importan al diseñar:

- Si `events.Count == 0 || !_tenant.IsSet`, `WriteOutbox()` retorna sin escribir, pero los eventos YA se limpiaron: un guardado sin
  empresa fijada descarta los eventos en silencio.
- La interfaz `IDomainEvent` solo tiene `EventType`, `OccurredAt` y `BranchId`.

### 3.2 Tablas del esquema `integration` que participan

Dominio: `src/1. Core/MINV.Domain/Integration/Webhooks.cs`. Configuración:
`Persistence/Configurations/Integration/IntegrationConfigurations.cs`.

| Tabla | Entidad | Mutabilidad | Columnas clave | Restricciones |
|---|---|---|---|---|
| `integration.outbox_events` | `OutboxEvent : Entity, IAppendOnly` | Append-only | `event_type` (≤ 60), `branch_id` (opcional), `payload` (`jsonb`, ≤ 100 000), `occurred_at` | FK `(tenant_id, branch_id)` → sucursales; índice `(tenant_id, occurred_at)` |
| `integration.outbox_dispatch` | `OutboxDispatch : BaseEntity, IConcurrencyAware` | MUTABLE (la única de la integración) | PK `outbox_event_id` (1:1), `status`, `rounds`, `next_attempt_at`, `completed_at`, `last_error` (≤ 500) | `ck_outbox_dispatch_estado` (`Pending`, `Completed`, `Exhausted`), `ck_outbox_dispatch_fin`, `ck_outbox_dispatch_rondas` (0 a 8); índice parcial sobre `next_attempt_at` con `status = 'Pending'` |
| `integration.webhook_deliveries` | `WebhookDelivery : Entity, IAppendOnly` | Append-only | `outbox_event_id`, `endpoint_id`, `attempt`, `status_code`, `succeeded`, `error` (≤ 500), `attempted_at`, `duration_ms` | `ck_webhook_deliveries_intento` (1 a 8), `ck_webhook_deliveries_duracion`; índice único `(outbox_event_id, endpoint_id, attempt)` |
| `integration.webhook_endpoints` | `WebhookEndpoint` | Mutable | `url`, `secret_ciphertext`, `secret_key_id`, `secret_version`, secreto anterior con gracia, `branch_id`, `is_active` | Solo https (http solo loopback), sin credenciales en la URL |
| `integration.webhook_endpoint_events` | `WebhookEndpointEvent` | | `endpoint_id`, `event_type` | Una fila por evento suscrito |

### 3.3 Despacho, reintentos y espera

Despachador: `WebhookDispatcher(IServiceScopeFactory scopes, HttpClient http, IClock clock)` en
`src/2. Infrastructure/MINV.Infrastructure/Integration/WebhookDispatcher.cs`. Registro: `AddMinvWebhookDispatcher(bool allowPrivateTargets)`
(singleton) en `DependencyInjection.cs`.

| Etapa | Detalle |
|---|---|
| Reclamo de la cola | `integration.claim_deliveries(p_limit integer, p_lease_seconds integer)`, `SECURITY DEFINER`, `search_path = pg_catalog, integration`, con `FOR UPDATE SKIP LOCKED`. Definida en `Migrations/20260925214056_V4MultiBranchCloud.Sql.cs` (línea 242). `EXECUTE` solo para `minv_server`. |
| Arrendamiento | El reclamo adelanta `next_attempt_at = now() + p_lease_seconds` (acotado entre 30 y 3600 s). El despachador pasa `{batch}` y `120`. Límite del lote en SQL: entre 1 y 500. |
| En memoria | Sin función: consulta `OutboxDispatches.IgnoreQueryFilters()` con `Status == Pending && NextAttemptAt <= now`. |
| Identidad por evento | Un scope DI nuevo por evento con `tenant.Set(item.TenantId)`; no se inicia sesión de usuario; alcance de sucursal por defecto (`BranchScope.Unrestricted`). |
| Destinos | `WebhookEndpoint.Wants(eventType, eventBranchId, occurredAt)`: activo, suscrito, de su sucursal (o sin filtro) y posterior a su registro. |
| Cuerpo | JSON con `id`, `type`, `occurredAt`, `tenantId`, `branchId`, `data`. |
| Cabeceras | `X-MINV-Signature` (`ApiKeyTokens.Sign`, admite doble firma en la rotación), `X-MINV-Event`, `X-MINV-Delivery` (`<id>-<intento>`), agente `M-INV-Webhooks/4.0`. |
| Cliente HTTP | `SafeWebhookHttp.Create`: solo IP públicas (anti SSRF), sin redirecciones, conexión 5 s, total 10 s, respuesta ≤ 64 KB. |
| Registro | Una fila `WebhookDelivery` por intento y destino; luego `dispatch.RecordRound(failed == 0, lastError, now)` y `SaveChangesAsync`. |
| Sin suscriptores | La ronda queda `Completed` de inmediato (no hay nada que entregar). |

Espera progresiva, `WebhookDelivery.BackoffBefore(int attempt)` con `MaxAttempts = 8`:

| Intento | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 |
|---|---|---|---|---|---|---|---|---|
| Espera previa | inmediato | 1 min | 5 min | 30 min | 2 h | 6 h | 12 h | 24 h |

Tras 8 rondas sin éxito el estado pasa a `Exhausted`.

### 3.4 Códigos de evento (`IntegrationEvents`, 12 en total)

Constantes en `src/1. Core/MINV.Domain/Integration/Webhooks.cs` (línea 287); tipos en `src/1. Core/MINV.Domain/Events/DomainEvents.cs`.

| Constante | Código | Tipo del evento | Cómo se emite |
|---|---|---|---|
| `SaleCompleted` | `sale.completed` | `SaleCompletedEvent` | `db.Publish` en `SalesUseCases.cs` |
| `SaleVoided` | `sale.voided` | `SaleVoidedEvent` | `db.Publish` en `SalesUseCases.cs` |
| `PurchaseReceived` | `purchase.received` | `PurchaseReceivedEvent` | `db.Publish` en `PurchasingUseCases.cs` |
| `TransferDispatched` | `transfer.dispatched` | `TransferDispatchedEvent` | Agregado `StockTransfer` |
| `TransferReceived` | `transfer.received` | `TransferReceivedEvent` | Agregado `StockTransfer` |
| `TransferDiscrepancy` | `transfer.discrepancy` | `TransferDiscrepancyRecordedEvent` | Agregado `StockTransfer` |
| `SaleReturned` | `sale.returned` | `SaleReturnedEvent` | `db.Publish` en `SalesReturnUseCases.cs` |
| `FiscalDocumentValidated` | `fiscal.document.validated` | `FiscalDocumentValidatedEvent` | Agregado `FiscalDocument` |
| `FiscalDocumentVoided` | `fiscal.document.voided` | `FiscalDocumentVoidedEvent` | Agregado `FiscalDocument` |
| `PcBuildReserved` | `pcbuild.reserved` | `PcBuildReservedEvent` | `PcBuild.Reserve(...)` |
| `PcBuildReleased` | `pcbuild.released` | `PcBuildReleasedEvent` | `PcBuild.ReleaseReservation(...)` |
| `PcBuildSold` | `pcbuild.sold` | `PcBuildSoldEvent` | `PcBuild.MarkSold(...)` |

Campos de los eventos de armados:

| Evento | Campos |
|---|---|
| `PcBuildReservedEvent` | `PcBuildId`, `Number`, `BranchIdOfBuild`, `Channel`, `Total`, `ReservedUntil`, `Lines`, `OccurredAt` |
| `PcBuildReleasedEvent` | `PcBuildId`, `Number`, `BranchIdOfBuild`, `Channel`, `Total`, `Reason`, `Expired`, `Lines`, `OccurredAt` |
| `PcBuildSoldEvent` | `PcBuildId`, `Number`, `BranchIdOfBuild`, `Channel`, `Total`, `InvoiceId`, `WasReserved`, `Lines`, `OccurredAt` |
| `PcBuildEventLine` | `VariantId`, `Slot`, `Quantity`, `QuotedUnitPrice` (el comentario del código dice: nunca datos del contacto) |

Documentación de los eventos: `docs/integration/api-gateway-v1.md` §7.2 (los `pcbuild.*` están desde la línea 832).

---

## 4. Servicios en segundo plano existentes

| Servicio | Archivo | Proceso | Qué hace | Cada cuánto | Identidad / empresa | Interruptor |
|---|---|---|---|---|---|---|
| `WebhookDispatcherService` | `src/3. Presentation/MINV.ApiGateway/Background/BackgroundServices.cs` | `MINV.ApiGateway` | `dispatcher.RunOnceAsync(50, ...)`; si hubo eventos repite sin esperar | `Minv:Webhooks:IntervalSeconds` (5 s) cuando la cola está vacía | Proceso de plataforma: por evento fija `tenant.Set(TenantId)`; sin usuario; todas las sucursales | `Minv:Webhooks:Enabled` (verdadero por defecto) |
| `ReportingRefreshService` | mismo archivo | `MINV.ApiGateway` | `SELECT reporting.refresh_all()` (candado consultivo entre réplicas) | `Minv:Reporting:RefreshMinutes` (5 min) | Sin empresa ni usuario; función `SECURITY DEFINER` | `Minv:Reporting:Enabled`; solo con almacenamiento PostgreSQL |
| `StorefrontReservationExpiryService` | mismo archivo | `MINV.ApiGateway` | Envía `ExpirePcBuildReservationsCommand` por MediatR (permisos y auditoría); método público `RunOnceAsync` (devuelve -1 si la tienda no está configurada) | `StorefrontSettings.ExpiryMinutes` (5 min, acotado entre 1 y 60) | Usuario técnico `tienda-web` (rol `TIENDA_WEB`) de `Minv:Storefront:TenantCode`, sucursal de la tienda, canal `storefront`, vía `StorefrontAuthenticator.AuthenticateAsync` | Solo si `storefront.IsConfigured` |
| `SiatBackgroundService` | `src/2. Infrastructure/MINV.Infrastructure/Billing/Hosting/SiatBackgroundService.cs` | `MINV.CloudServer` (`AddMinvSiatBackground()`) | Por cada empresa con facturación activa: `ISiatWorker.DispatchAsync(null, 50, ...)` y, cuando toca, `ISiatWorker.MaintainAsync(false, ...)` (que incluye los correos fiscales) | `DispatchInterval` 10 s; `MaintenanceInterval` 60 s; `StartupDelay` 5 s | Proceso de plataforma: scope nuevo por empresa, `tenant.Set(tenantId)` + `SetBranches(BranchScope.Unrestricted)`; sin usuario. Empresas desde `ISiatTenantSource` (`billing.siat_active_tenants()`, `SECURITY DEFINER`) | `Minv:Siat:Background` (verdadero por defecto) |
| `BillingWorkService` (no es `BackgroundService`) | `src/3. Presentation/MINV.DesktopClient/Services/Billing.cs` | `M-INV.exe`, modos Base local y Demostración | `RunSiatWorkCommand(Maintain: _ticks % 3 == 0)` con un `DispatcherTimer` | 20 s | La sesión del usuario del escritorio | En modo Nube no corre (lo hace el servidor) |

Observaciones:

- El gateway NO registra `SiatBackgroundService`; el servidor en la nube NO registra ninguno de los tres servicios del gateway.
- Las pruebas de integración apagan los dos primeros (`--Minv:Webhooks:Enabled false`, `--Minv:Siat:Background false`) y ejecutan las
  pasadas a mano: `WebhookDispatcher.RunOnceAsync(5000, default)` en `ApiGatewayTests.cs` (línea 129) y
  `StorefrontReservationExpiryService.RunOnceAsync` en `StorefrontApiTests.cs` (línea 228). El mismo patrón sirve para el correo.
- En Docker (`deploy/docker-compose.yml`): `cloudserver` (puerto 5080), `apigateway` (5090), `webcatalog` (5173), `siat-simulador`
  (perfil), `tunel` (perfil `publico`), `postgres` (perfil `local-db`). Ningún servicio de correo.

---

## 5. Estado actual de la reserva respecto del correo

| Hecho | Dónde |
|---|---|
| La API pública recibe el correo | `StorefrontContactInput(string Name, string Phone, string? Email = null)` en `StorefrontContracts.cs` |
| Validación de entrada | `CreateStorefrontReservationValidator`: `Contact.Email` solo `MaximumLength(254)`; nombre ≤ 120; notas ≤ 500 |
| Validación de dominio | `PcBuild.SetContact` → `Guard.OptionalEmail` → `Guard.Email`: recorta, pasa a minúsculas y exige el patrón `^[^@\s]+@[^@\s]+\.[^@\s]+$` |
| Dónde se guarda | `sales.pc_builds.contact_email` (≤ 254), `contact_name`, `contact_phone`, `notes` |
| Auditoría | `CreateStorefrontReservationCommand.AuditDetails` enmascara el teléfono (`Mask`) y reemplaza el correo por `***` |
| Privacidad (S-06) | `PcBuildUseCases.cs` línea 143: el contacto solo se muestra con `showContact` (permiso `sales.pcbuild.manage`); `StorefrontReservationView` nunca lleva teléfono ni correo |
| Transacción | `CreateStorefrontReservationHandler`: armado + reservas de stock + `ProcessedRequest` en UN `SaveChangesAsync`, con hasta 3 intentos |
| Idempotencia | Una repetición con la misma llave retorna ANTES de `CreateAsync`: no vuelve a crear nada |
| Reserva desde el escritorio | `ReservePcBuildHandler` (`ReservePcBuildCommand(string Number, int Hours = 48)`); el armado puede tener `ContactEmail` o no |
| Envío de correo | NO existe |
| La web ya pide el correo | Hay referencias a `email` en `src/3. Presentation/MINV.WebCatalog/src/4-presentation/pages/builder/components/ReserveDialog.tsx` y en `1-domain/storefront/contact.ts` |

---

## 6. PROPUESTA PARA LA V7 · correo de la reserva cumpliendo B-08

### 6.1 Decisión de diseño: cola propia, no reutilizar `outbox_dispatch`

| Opción | Veredicto | Motivo |
|---|---|---|
| A. Que el trabajo de correo consuma `outbox_events` (`pcbuild.reserved`) | Descartada | `outbox_dispatch` es 1:1 con el evento y su estado pertenece a los webhooks (`RecordRound` lo cierra como `Completed` cuando no hay suscriptores). Un segundo consumidor necesitaría su propio cursor. Además el evento no lleva contacto y no debe llevarlo (saldría a terceros por webhook). |
| B. Enviar el correo dentro del manejador de la reserva | Prohibida | Viola B-08; además un SMTP lento bloquearía la transacción que tiene stock tomado. |
| C. Cola de correos propia, encolada en el mismo `SaveChanges` | RECOMENDADA | Mismo patrón que el outbox (hecho inmutable + cola mutable + bitácora de intentos), sin tocar lo publicado. |

### 6.2 Tablas nuevas (3), esquema `integration`

Nombres propuestos con el prefijo `outgoing_` para no chocar con `System.Net.Mail.MailMessage`.

**`integration.outgoing_mails`** · el hecho «se pidió enviar este correo». Append-only, de sucursal.
Entidad `OutgoingMail : Entity, IBranchScoped, IAppendOnly`.

| Columna | Tipo | Regla |
|---|---|---|
| `id` | uuid | PK |
| `tenant_id`, `branch_id` | uuid | Sucursal del armado; políticas `tenant_isolation` y `branch_isolation` |
| `kind` | texto ≤ 40 | `CHECK` con los valores del enum `OutgoingMailKind` (inicial: `ReservationConfirmed`) |
| `pc_build_id` | uuid | FK compuesta `(tenant_id, branch_id, pc_build_id)` → `sales.pc_builds` |
| `recipient` | texto ≤ 254 | Dirección a la que se pidió el envío (puede diferir del contacto en un reenvío) |
| `requested_at` | timestamptz | |
| `requested_by_user_id` | uuid | FK `(tenant_id, user_id)` → `iam.users` |

No se guarda el asunto ni el cuerpo: se derivan al enviar desde `pc_builds` + `pc_build_lines` + catálogo (regla A-06). Los precios
del armado están congelados desde `Quote` y un armado reservado no es editable (`EnsureEditable` exige `Draft`).
Si en el futuro hay correos de otros documentos: arco exclusivo con `CHECK num_nonnulls(pc_build_id, ...) = 1`.

**`integration.outgoing_mail_dispatch`** · la cola. MUTABLE, 1:1 con el correo.
Entidad `OutgoingMailDispatch : BaseEntity, IConcurrencyAware`.

| Columna | Tipo | Regla |
|---|---|---|
| `outgoing_mail_id` | uuid | PK y FK `(tenant_id, outgoing_mail_id)` |
| `tenant_id` | uuid | |
| `status` | texto ≤ 20 | `CHECK IN ('Pending', 'Sent', 'Exhausted', 'Cancelled')` |
| `attempts` | entero | `CHECK BETWEEN 0 AND 5` |
| `next_attempt_at` | timestamptz | Índice parcial con `status = 'Pending'` |
| `completed_at` | timestamptz | `CHECK (status = 'Pending') = (completed_at IS NULL)` |
| `last_error` | texto ≤ 500 | Sin datos del servidor ni credenciales |
| `xmin` | | `RowVersion` |

**`integration.outgoing_mail_attempts`** · bitácora de intentos. Append-only.
Entidad `OutgoingMailAttempt : Entity, IAppendOnly`.

| Columna | Tipo | Regla |
|---|---|---|
| `id` | uuid | PK |
| `tenant_id`, `outgoing_mail_id` | uuid | FK compuesta |
| `attempt` | entero | `CHECK BETWEEN 1 AND 5`; índice único `(outgoing_mail_id, attempt)` |
| `succeeded` | booleano | |
| `smtp_status` | entero, opcional | Código SMTP si lo hubo |
| `error` | texto ≤ 500 | |
| `attempted_at` | timestamptz | |
| `duration_ms` | entero | `CHECK >= 0` |

**Función nueva** `integration.claim_outgoing_mails(p_limit integer, p_lease_seconds integer)`: copia de `claim_deliveries`
(`SECURITY DEFINER`, `SET search_path = pg_catalog, integration`, `FOR UPDATE SKIP LOCKED`, `REVOKE ALL ... FROM PUBLIC`,
`GRANT EXECUTE` solo a `minv_server`, regla B-13). Devuelve `(tenant_id, outgoing_mail_id)`.

Comprobación de normalización de estas tablas: cada una describe un solo concepto; ningún atributo depende de otro no clave; el estado
mutable está separado del hecho inmutable (B-06); no hay totales ni textos derivables guardados.

### 6.3 Migración y listas obligatorias (B-15)

| Elemento | Acción |
|---|---|
| Migración nueva | `dotnet ef migrations add V7... --context MinvWriteDbContext` + archivo parcial `.Sql.cs` |
| Listas | `NewTablesV7` (las 3), `BranchTablesV7` (`integration.outgoing_mails`), `AppendOnlyTablesV7` (`outgoing_mails`, `outgoing_mail_attempts`) |
| Prueba | `tests/MINV.Infrastructure.Tests/ModelTests.cs` concatena las listas por versión (líneas 52 a 124): hay que sumar las de la V7 |
| Seguridad | `trg_append_only` + `trg_append_only_truncate`, RLS por descubrimiento, `branch_isolation` RESTRICTIVA, privilegios de `minv_app` y `minv_server` |
| Documentación | `docs/database/ERD-MINV-V3.md` debe nombrar las 3 tablas (`El_ERD_documenta_todas_las_tablas_del_modelo`); total: 153 → 156 tablas |
| Artefacto | Regenerar `scripts/db_init.sql` |
| `VerifyDatabaseAsync` | Hoy solo comprueba `iam.resolve_session`; conviene comprobar también `EXECUTE` sobre la función nueva en el proceso que hospede el trabajo |

### 6.4 Flujo propuesto

```text
POST /storefront/v1/reservations  (o el comando del carrito / ReservePcBuildCommand)
  └─ manejador, UNA transacción (un SaveChangesAsync):
       PcBuild + StockReservation por línea + pc_build_events + outbox (pcbuild.reserved)
       + ProcessedRequest (idempotencia)
       + SI build.ContactEmail != null → OutgoingMail + OutgoingMailDispatch(Pending, next_attempt_at = now)
  └─ COMMIT  →  respuesta 201 al cliente (el correo todavía no salió)

MailDispatcherService (segundo plano, después del COMMIT)
  └─ claim_outgoing_mails(20, 120)   [SKIP LOCKED + arrendamiento]
  └─ por cada correo, scope nuevo: tenant.Set(tenantId) + SetBranches(Unrestricted)
       1. leer OutgoingMail + PcBuild + líneas + nombres
       2. si el armado ya no está Reserved → dispatch Cancelled (no se envía)
       3. resolver el servidor SMTP (6.6) y armar el mensaje (6.8)
       4. IMailSender.SendAsync con límite de tiempo propio
       5. OutgoingMailAttempt (siempre) + dispatch.RecordAttempt(...) + SaveChangesAsync
```

Punto único de encolado: un auxiliar de aplicación (por ejemplo `ReservationMail.Enqueue(db, build, userId, now)`) llamado desde
`CreateStorefrontReservationHandler.CreateAsync`, desde `ReservePcBuildHandler` y desde el manejador nuevo del carrito, siempre antes
del `SaveChangesAsync`. La repetición idempotente no encola de nuevo porque retorna antes.

Reintentos propuestos (5 intentos, reutilizando el criterio de `WebhookDelivery.BackoffBefore`):

| Intento | 1 | 2 | 3 | 4 | 5 |
|---|---|---|---|---|---|
| Espera previa | inmediato | 1 min | 5 min | 30 min | 2 h |

Clasificación de fallas:

| Falla | Tratamiento |
|---|---|
| Red, tiempo agotado, 4xx transitorio del servidor | Reintento con espera |
| Buzón inexistente o rechazo permanente (5xx del destinatario) | `Exhausted` de inmediato |
| Autenticación rechazada o servidor mal configurado | NO gastar los intentos de toda la cola: pausar el trabajo, registrar el error y avisar |
| Correo no configurado en la empresa | Dejar `Pending` y reprogramar; no es culpa del mensaje |

Entrega «al menos una vez»: si el proceso cae después de que el servidor aceptó el mensaje y antes de guardar el intento, el correo
puede salir dos veces. Mitigación: arrendamiento del reclamo y una cabecera `Message-ID` determinista por `outgoing_mail_id`.

### 6.5 Dónde corre el trabajo

Recomendación: en `MINV.ApiGateway`, junto a `WebhookDispatcherService` y `StorefrontReservationExpiryService`.

| Criterio | Gateway | Servidor en la nube |
|---|---|---|
| Es el proceso de la tienda pública | Sí | No |
| Ya hospeda el patrón cola + `SKIP LOCKED` | Sí | No |
| Ya tiene `IMailSender` registrado (PostgreSQL) | Sí | Sí |
| Tiene `MINV_INTEGRATION_KEYS` | Sí | Sí |
| Salida a internet desde el contenedor | Sí | Sí |

La clase despachadora debe vivir en `MINV.Infrastructure/Integration` (como `WebhookDispatcher`) con un método público
`RunOnceAsync(int batch, CancellationToken ct)`; el `BackgroundService` del gateway solo marca el ritmo. Así las pruebas ejecutan una
pasada a mano y cualquier otro proceso puede hospedarla. Interruptor: `Minv:Mail:Enabled`; intervalo: `Minv:Mail:IntervalSeconds`.

Las reservas hechas desde el escritorio en modo Base local quedan encoladas en la base local: solo saldrán si algún proceso con el
trabajo de correo apunta a esa base. Hay que decidirlo de forma explícita.

### 6.6 Configuración para Gmail

Parámetros: servidor `smtp.gmail.com`, puerto `587`, `UseSsl = true` (en `SmtpClient` eso negocia STARTTLS), usuario = la cuenta
completa de Gmail de la empresa, contraseña = una CONTRASEÑA DE APLICACIÓN de Google (exige verificación en dos pasos en la cuenta),
remitente = la misma cuenta.

Dos fuentes posibles, en este orden de precedencia:

| Fuente | Estado | Uso recomendado |
|---|---|---|
| 1. `billing.mail_settings` de la empresa (`IsEnabled`) | YA EXISTE, cifrada con `ISecretProtector` | Configuración real. Se carga con `SaveMailSettingsCommand`; la pantalla del escritorio ya existe. Sobrevive a la regeneración de `deploy/.env`. |
| 2. Sección nueva `Minv:Mail` por variables de entorno | NO EXISTE, hay que crearla | Remitente por defecto del servidor y redirección a un buzón de prueba |

Variables propuestas para `deploy/docker-compose.yml` (servicio `apigateway`) y `deploy/.env.example`:

| Variable en `.env` | Clave de configuración | Ejemplo |
|---|---|---|
| `MINV_MAIL_ENABLED` | `Minv__Mail__Enabled` | `true` |
| `MINV_MAIL_HOST` | `Minv__Mail__Host` | `smtp.gmail.com` |
| `MINV_MAIL_PORT` | `Minv__Mail__Port` | `587` |
| `MINV_MAIL_STARTTLS` | `Minv__Mail__UseSsl` | `true` |
| `MINV_MAIL_USER` | `Minv__Mail__UserName` | la cuenta de Gmail de la empresa |
| `MINV_MAIL_PASSWORD` | `Minv__Mail__Password` | contraseña de aplicación; solo en `deploy/.env` |
| `MINV_MAIL_FROM` | `Minv__Mail__FromAddress` | la misma cuenta |
| `MINV_MAIL_FROM_NAME` | `Minv__Mail__FromName` | nombre comercial |
| `MINV_MAIL_OVERRIDE_HOST` / `_PORT` | `Minv__Mail__Override__Host` / `Port` | `mailpit` / `1025` (solo pruebas) |

Reglas:

- La contraseña de aplicación la genera el dueño de la cuenta en su cuenta de Google; nadie más puede hacerlo por él.
- Nunca en el repositorio, en la documentación, en los registros ni en la auditoría (B-11, F-12).
- `deploy/.env.example` solo lleva marcadores (`<contraseña de aplicación>`).
- Restricción actual: `SaveMailSettingsCommand` exige el módulo `FISCAL_SIAT` y el permiso `billing.configure`. Una empresa sin ese
  módulo no podría configurar el correo de reservas por esa vía.

**Problema concreto con `tools/docker_local.ps1`:** la acción `subir` REESCRIBE `deploy\.env` completo (líneas 192 a 209) con una lista
fija de variables. Cualquier variable de correo agregada a mano se perdería en cada `subir`. El script debe leer las variables de
correo de un archivo local no versionado (como ya hace con `Leer $claves 'MINV_INTEGRATION_KEYS'`) y escribirlas en `deploy\.env`.

### 6.7 Prueba en Docker con un buzón de prueba

Servicio nuevo en `deploy/docker-compose.yml`, con perfil propio para que nunca se levante en producción:

| Campo | Valor propuesto |
|---|---|
| Servicio | `mailpit` |
| Perfil | `correo-prueba` |
| Imagen | `axllent/mailpit` |
| SMTP (red interna) | `mailpit:1025`, sin TLS ni credenciales |
| Interfaz web | `127.0.0.1:8025` (solo este equipo; NO pasa por el túnel público) |

Recorrido de verificación:

1. Levantar con el perfil y con `MINV_MAIL_OVERRIDE_HOST=mailpit`.
2. Reservar desde la web indicando un correo.
3. Comprobar en `http://localhost:8025` que llegó un mensaje con el número `ARM-WEB-...`, las líneas y el vencimiento.
4. Comprobar en la base: una fila en `outgoing_mails`, `outgoing_mail_dispatch.status = 'Sent'`, una fila en `outgoing_mail_attempts`.
5. Apagar `mailpit`, reservar otra vez: la reserva responde 201 igual, y la cola queda `Pending` con intentos fallidos registrados.
6. Encender `mailpit`: el correo sale en el siguiente reintento.

Agregar la comprobación a `Estado` de `tools/docker_local.ps1` (hoy comprueba 5 direcciones y el enlace público).

### 6.8 Inyección de cabeceras y de HTML

Datos que controla el cliente y que podrían llegar al correo: `Contact.Name` (≤ 120), `Contact.Email`, `Contact.Phone`, `Notes`
(≤ 500) y `Name` del armado (≤ 150).

| Riesgo | Situación actual | Medida para la V7 |
|---|---|---|
| Salto de línea en el destinatario | `Guard.Email` recorta y el patrón excluye todo espacio en blanco (`\s`), incluidos CR y LF | Mantener; además construir `MailAddress` al validar y comparar `Address` con el texto; rechazar `<`, `>`, `"`, `,`, `;` |
| Salto de línea en el nombre | `Guard.OptionalText` solo recorta los extremos y limita la longitud: admite CR, LF y caracteres de control en el medio | Rechazar caracteres de control en nombre y notas al validar la entrada |
| Asunto con datos del cliente | No aplica todavía | El asunto se arma SOLO con datos del servidor: número de la reserva y nombre de la empresa |
| Nombre visible del destinatario | `SmtpMailSender` usa solo la dirección | Mantener: nunca poner el nombre del cliente en la cabecera `To` |
| HTML en el cuerpo | `BuyerMail` ya usa `WebUtility.HtmlEncode` en cada valor | Mismo criterio para TODO valor variable: nombre, nombres de producto, sucursal |
| Texto libre del cliente | | No incluir `Notes` ni el nombre libre del armado en el correo |
| Enlaces | | Solo enlaces construidos por el servidor con un origen configurado; nunca con datos del cliente |
| Teléfono | El estado de la reserva se consulta con número + teléfono (S-06) | No escribir el teléfono completo en el correo: usar el enmascarado de `CreateStorefrontReservationCommand.Mask` |
| Plantilla | | Texto fijo en el código; los valores entran ya codificados; agregar alternativa de texto plano |

### 6.9 Abuso del envío (riesgo nuevo que trae esta función)

La API de reservas es pública y sin llave: con el correo automático, cualquiera puede hacer que la cuenta de la empresa envíe
mensajes a direcciones ajenas.

| Medida | Detalle |
|---|---|
| Límite existente | 10 reservas por minuto por IP (`StorefrontSettings.ReservationsPerMinute`) |
| Un correo por reserva | Encolado solo al crear; la repetición idempotente no encola |
| Tope por destinatario | Máximo de correos por dirección en 24 h (contando `outgoing_mails`) |
| Tope diario por empresa | Por debajo del límite de envío de la cuenta de Gmail, para no bloquearla |
| Contenido no controlable | Plantilla fija, sin texto libre del cliente |
| Reserva real | El correo solo sale si hubo stock y la reserva se confirmó |

### 6.10 Cambios sugeridos al puerto

| Cambio | Motivo |
|---|---|
| Agregar `TextBody` a `MailMessageSpec` | Alternativa de texto plano |
| Agregar cabeceras controladas (`Message-ID`) | Idempotencia y trazabilidad |
| Redefinir `ToString()` en `MailServer` | Que la contraseña nunca pueda imprimirse |
| Límite de tiempo real en `SmtpMailSender` | Ver el riesgo R-01 |
| Mover `IMailSender` a un archivo propio | Hoy está en `SiatPorts.cs` |

### 6.11 Pruebas que deben existir

| Prueba | Nivel |
|---|---|
| Emisor falso `RecordingMailSender : IMailSender` que guarda los `MailMessageSpec` | Infraestructura de pruebas (no existe ninguno) |
| Reservar con correo crea `OutgoingMail` + `OutgoingMailDispatch` en la misma transacción | `tests/MINV.Infrastructure.Tests` |
| Reservar sin correo no encola nada | Igual |
| Falta de stock (409): no queda ningún correo encolado | Igual |
| Repetición idempotente: sigue habiendo un solo correo | `tests/MINV.Integration.Tests` |
| El manejador de la reserva NO llama a `IMailSender` (el falso queda vacío hasta ejecutar la pasada) | Igual; es la prueba de B-08 |
| Pasada del despachador: envía, registra el intento y deja `Sent` | Igual |
| Falla del emisor: intento fallido, `Pending`, `next_attempt_at` según la espera | Igual |
| Reserva cancelada antes del envío: `Cancelled`, sin envío | Igual |
| Nombre con CR/LF o con `<script>`: rechazado o codificado; asunto sin datos del cliente | Dominio y aplicación |
| Append-only: actualizar `outgoing_mail_attempts` falla | Con `MINV_TEST_PG` |
| Aislamiento: otra empresa no ve los correos | Con `MINV_TEST_PG` |
| Listas de la migración contra el modelo | `ModelTests` |

---

## 7. Riesgos encontrados

| Id | Riesgo | Evidencia | Gravedad |
|---|---|---|---|
| R-01 | El límite de 30 s de `SmtpMailSender` probablemente no aplica al envío asíncrono: según la documentación de .NET, `SmtpClient.Timeout` rige las llamadas síncronas `Send`. Con `SendMailAsync` el único freno es el `CancellationToken`. Un servidor que no responde podría colgar la pasada. | `SmtpMailSender.cs` líneas 39 y 46 | Media (por confirmar) |
| R-02 | `System.Net.Mail.SmtpClient` no soporta TLS implícito (465) ni OAuth2, y Microsoft no lo recomienda para desarrollos nuevos. Para Gmail en 587 con contraseña de aplicación es suficiente. | `SmtpMailSender.cs` | Baja |
| R-03 | No existe ninguna prueba del envío de correo ni un emisor falso. | Búsqueda en `tests/` sin resultados | Media |
| R-04 | TECHZONE tiene el correo desactivado y apuntando a un dominio `.example`: sin configurar, ningún correo saldrá. | `SiatSeeding.cs` línea 448 | Alta para la demostración |
| R-05 | `tools/docker_local.ps1 -Accion subir` reescribe `deploy\.env` y borraría variables de correo agregadas a mano. | Script, líneas 192 a 209 | Alta |
| R-06 | La configuración de correo exige el módulo `FISCAL_SIAT`. | `SaveMailSettingsCommand` | Media |
| R-07 | `Guard.OptionalText` admite saltos de línea y caracteres de control dentro del nombre de contacto y las notas. | `Guard.cs` líneas 32 a 40; `PcBuild.SetContact` | Media |
| R-08 | El envío automático convierte la API pública en un posible medio de envío de correo no deseado. | `/storefront/v1/reservations` sin llave | Alta |
| R-09 | `WriteOutbox()` descarta los eventos sin aviso si no hay empresa fijada. | `MinvWriteDbContext.cs` líneas 280 a 290 | Baja |
| R-10 | En `BuyerMail.NotificationTargetAsync` solo se captura `DomainException`, pero el descifrado puede lanzar `AccessDeniedException` (`UnconfiguredSecretProtector`) o `CryptographicException` (clave rotada), que no heredan de ella. En `VoidFiscalDocumentHandler` esa llamada ocurre después de que el SIN confirmó la anulación y antes de aplicarla en M-INV. | `FiscalLifecycleUseCases.cs` líneas 166 y 482 a 489; `Exceptions.cs` | Baja (caso borde, no reproducido) |
| R-11 | Los reintentos del correo fiscal no tienen espera progresiva y mezclan intentos manuales y automáticos. | `SiatWorker.cs` líneas 958 a 964 | Baja |
| R-12 | `PcBuild.SetContact` no comprueba el estado del armado: el correo de contacto podría cambiar después de reservar. | `PcBuild.cs` línea 174 | Baja |
| R-13 | `MailServer` es un `record` con `Password`: su `ToString()` generado la incluiría. | `SiatPorts.cs` línea 198 | Baja |

---

## 8. Lo que no se pudo confirmar

1. El comportamiento exacto de `SmtpClient.Timeout` con `SendMailAsync` en .NET 8 (R-01): se dedujo de la documentación, no se ejecutó.
2. Si `MailMessage.Subject` de .NET 8 rechaza por sí solo un asunto con CR/LF. La propuesta no depende de ello.
3. El límite diario de envío de la cuenta de Gmail de la empresa (depende del tipo de cuenta).
4. Si la cuenta de Gmail de la empresa ya tiene verificación en dos pasos y una contraseña de aplicación. Es un paso que solo puede
   hacer el dueño de la cuenta.
5. Si el contenedor del gateway tiene salida al puerto 587 en la red donde se despliegue (algunos proveedores lo bloquean).
6. No se leyó `BillingSettingsViewModel.cs` en detalle: se confirmó que referencia `MailSettings`, no qué campos expone la pantalla.
7. No se revisó cómo la web envía hoy el campo `email` (solo que existen referencias); corresponde al mapa de la web.
8. No se verificó el contenido de `deploy/.env` (prohibido por las reglas de la tarea).
