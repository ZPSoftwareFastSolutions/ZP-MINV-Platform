# Informe del paquete B1-carrito (V7)

> Escrito por quien construyo el paquete. Sirve de contexto a los paquetes siguientes.

## Resumen

Paquete B1 (carrito) terminado sobre la rama Inventario-V7, sin commits. El arbol estaba limpio al empezar: no habia trabajo previo que continuar.
1. DOMINIO: `enum PcBuildKind { Build, Cart }` y `PcBuild.Kind` (por defecto Build, sin setter publico). Fabricas `CreateWebCart` y `CreateDesktopCart`; `CreateWeb` y el constructor publico siguen igual. `AddLine(PcSlot? ...)`: en un carrito la ranura es opcional y no hay ranura unica; en un armado todo igual (ranura obligatoria, `pcbuild.slot`). `Quote` de un carrito ignora el informe (mas `QuoteCart` y `PcCompatibilityReport.Empty`). `Publish` rechaza carritos (`pcbuild.publish_kind`). `SetBuyer` valida con `FiscalRules.EnsureBuyerDocument`. `Guard.PlainText`/`OptionalPlainText`/`HasControlCharacters` nuevos (`guard.control_chars`); `Guard.Text` y `OptionalText` no cambian. Numeracion RES-WEB / RES-<sucursal> con `PcBuild.NumberPrefixOf`.
2. PERSISTENCIA: solo configuracion de EF (columnas kind y buyer_*, 6 CHECK nuevos, slot anulable con su CHECK). No se creo migracion ni se toco Migrations/.
3. APLICACION: `CreateStorefrontReservationCommand` con Kind, HoldDays y Buyer; respuesta con `kind` y `mailQueued` (siempre false); catalogo con `reservationHours` y `maxHoldDays`; `StorefrontOptions.MaxReservationHours` (72). Punto unico `ReservationWriter` (tienda y mostrador). Hash de idempotencia con los campos nuevos, conservando el hash de la V6 cuando no vienen. `SavePcBuildCommand`/`GetPcBuildsQuery`/`PcBuildRow` con el tipo; datos de factura solo con `sales.pcbuild.manage`. Comando nuevo `ReserveCartCommand`. `SellPcBuildCommand` vende carritos y usa los datos de factura de la reserva si el cajero no capturo comprador (comprobado contra el documento fiscal emitido).
4. AUDITORIA: los comandos enmascaran documento, complemento y razon social. Hallazgo: la auditoria guardaba la RESPUESTA completa, asi que `PcBuildRow` dejaba telefono, correo y datos de factura en claro; se agrego `IAuditableResponse` para enmascararla.
5. GATEWAY: pasan los campos nuevos, `MaxReservationHours` en configuracion, OpenAPI con esquemas de respuesta. Documentos `storefront-api-v1.md` (con ejemplos reales) y `api-gateway-v1.md` §7.2 actualizados.
6. PRUEBAS: 33 pruebas nuevas en dominio, aplicacion, flujo en memoria e integracion del gateway. Todas las existentes de la V6 pasan sin modificar lo que comprueban.
AVISO: durante mi sesion aparecieron cambios en `src/3. Presentation/MINV.WebCatalog` (sesion, panel, carrito, contract.generated.ts) que NO son mios: otro proceso esta escribiendo ahi en paralelo. No los toque.

## Contratos (nombres exactos)

- DOMINIO MINV.Domain.Sales: enum PcBuildKind { Build, Cart }; PcBuild.Kind; PcBuild.IsCart; PcBuild.HasBuyer; PcBuild.BuyerDocumentType (int?), BuyerDocumentNumber, BuyerComplement, BuyerName
- PcBuild.CreateWebCart(tenantId, branchId, number, name, contactName, contactPhone, contactEmail, notes, validUntil, userId, createdAt, Guid? customerId = null)
- PcBuild.CreateDesktopCart(tenantId, branchId, number, name, Guid? customerId, validUntil, userId, createdAt)
- PcBuild.AddLine(PcSlot? slot, Guid variantId, int quantity, decimal quotedUnitPrice); PcBuildLine.Slot es PcSlot?
- PcBuild.QuoteCart(validUntil, today, now, userId); PcBuild.SetBuyer(int? documentType, string? documentNumber, string? complement, string? name)
- PcBuild.NumberPrefixOf(PcBuildKind, PcBuildChannel); constantes PcBuild.NumberPrefix = "ARM", WebNumberPrefix = "ARM-WEB", CartNumberPrefix = "RES", WebCartNumberPrefix = "RES-WEB"
- PcCompatibilityReport.Empty
- Guard.HasControlCharacters(string?), Guard.PlainText(value, name, maxLength, minLength = 1), Guard.OptionalPlainText(value, name, maxLength)
- Codigos de error nuevos: guard.control_chars, pcbuild.publish_kind, pcbuild.kind, pcbuild.buyer, storefront.hold_days; reutilizados: buyer.doc_type, buyer.doc_number, buyer.doc_numeric, buyer.complement, pcbuild.slot, pcbuild.state
- Eventos: PcBuildReservedEvent / PcBuildReleasedEvent / PcBuildSoldEvent con Kind (string, ultimo parametro, por defecto "Build"); PcBuildEventKinds.Build / .Cart; PcBuildEventLine.Slot es string?
- BASE sales.pc_builds: kind varchar(10) NOT NULL; buyer_document_type smallint NULL; buyer_document_number varchar(20) NULL; buyer_complement varchar(5) NULL; buyer_name varchar(150) NULL
- CHECK ck_pc_builds_tipo: kind IN ('Build', 'Cart')
- CHECK ck_pc_builds_tipo_publicado: NOT published_to_web OR kind = 'Build'
- CHECK ck_pc_builds_factura_tipo: buyer_document_type IS NULL OR buyer_document_type BETWEEN 1 AND 5
- CHECK ck_pc_builds_factura_documento: (buyer_document_type IS NULL) = (buyer_document_number IS NULL)
- CHECK ck_pc_builds_factura_complemento: buyer_complement IS NULL OR buyer_document_type = 1
- CHECK ck_pc_builds_factura_nombre: buyer_name IS NULL OR buyer_document_type IS NOT NULL
- BASE sales.pc_build_lines: slot varchar(20) NULL; CHECK ck_pc_build_lines_ranura: slot IS NULL OR slot IN (las 12 ranuras)
- REGLA PARA EL TRIGGER DE B4: pc_build_lines.slot nulo SOLO si el pc_builds padre tiene kind = 'Cart' (hoy solo en el dominio)
- APLICACION MINV.Application.Storefront: StorefrontOptions(int ReservationHours = 48, int MaxReservationHours = 72) con EffectiveHours, EffectiveMaxHours, MaxHoldDays, HoursFor(int? holdDays); constante StorefrontOptions.HoldDaysLimit = 3
- StorefrontKinds.Build = "build", StorefrontKinds.Cart = "cart", StorefrontKinds.Parse(string?), StorefrontKinds.Text(PcBuildKind)
- record ReservationBuyerInput(int DocumentType, string DocumentNumber, string? Complement = null, string? Name = null); ReservationBuyerValidator; ReservationRules.IsPlain
- CreateStorefrontReservationCommand(Lines, Contact, Notes, IdempotencyKey, Name = null, PcBuildKind Kind = Build, int? HoldDays = null, ReservationBuyerInput? Buyer = null)
- StorefrontReservationView: campos nuevos al final string Kind = "build" y bool MailQueued = false; StorefrontReservationLine.Slot es string?
- StorefrontCatalogView: campos nuevos al final int ReservationHours = 48 e int MaxHoldDays = 3
- internal ReservationWriter.CreateAsync(db, clock, StorefrontOptions? options, ReservationSpec spec, userId, now, ct) devuelve PcBuild SIN guardar
- internal record ReservationSpec(Kind, Channel, Lines, ContactName, ContactPhone, ContactEmail, Notes, Name, HoldDays = null, Buyer = null, CustomerId = null)
- StorefrontReservationViews.ViewAsync(db, build, now, ct, bool mailQueued = false)
- APLICACION MINV.Application.Tech: ReserveCartCommand(IReadOnlyList<CartItemInput> Items, string ContactName, string ContactPhone, string? ContactEmail = null, string? Notes = null, int? HoldDays = null, Storefront.ReservationBuyerInput? Buyer = null, string? CustomerCode = null, string? Name = null) devuelve PcBuildRow; permiso sales.pcbuild.manage; nombre RPC MINV.Application.Tech.ReserveCartCommand
- record CartItemInput(string Sku, int Quantity = 1)
- SavePcBuildCommand(..., bool AcceptIncompatible = false, PcBuildKind Kind = Build)
- GetPcBuildsQuery(PcBuildStatus? Status = null, PcBuildChannel? Channel = null, PcBuildKind? Kind = null)
- PcBuildRow: campos nuevos al final Kind, BuyerDocumentType, BuyerDocumentNumber, BuyerComplement, BuyerName (los Buyer* solo con sales.pcbuild.manage)
- PcBuildItemInput.Slot y PcBuildItemView.Slot son PcSlot?
- MINV.Application.Common.IAuditableResponse { object AuditResult } (implementacion explicita; AuditBehavior la usa para el campo result)
- GATEWAY POST /storefront/v1/reservations: campos opcionales kind ("build" | "cart"), holdDays (1 a 3), buyer { documentType, documentNumber, complement, name }
- StorefrontReservationRequest(Lines, Contact, Notes, Name, IdempotencyKey, string? Kind, int? HoldDays, ReservationBuyerInput? Buyer)
- CONFIGURACION Minv:Storefront:MaxReservationHours (72); StorefrontSettings.MaxReservationHours
- NUMERACION: carrito web RES-WEB-000001; carrito de mostrador RES-<sucursal>-000001; armados ARM-WEB-… y ARM-<sucursal>-… sin cambios

## Desviaciones del diseno

- NO se modifico docs/architecture/plataforma-web-v7.md: no hubo desviacion del diseño, solo precisiones que van en 'contratos'.
- AuditBehavior + IAuditableResponse (no estaba en el diseño): la auditoria serializaba la respuesta completa del comando, de modo que PcBuildRow dejaba telefono, correo y datos de factura en claro en iam.audit_logs. Ahora PcBuildRow implementa IAuditableResponse de forma explicita (no viaja en el JSON) y se audita enmascarada. Esto tambien corrige el mismo problema que ya tenian en la V6 ReservePcBuild, ReleasePcBuildReservation, PublishPcBuild y SavePcBuild con el telefono de las reservas web.
- StorefrontOptions.EffectiveHours ahora se acota por MaxReservationHours (72). Un despliegue de la V6 que tuviera ReservationHours mayor que 72 pasara a reservar 72 h salvo que suba Minv:Storefront:MaxReservationHours. Interprete asi 'tope MaxReservationHours' del diseño.
- Cambio de firma con ajuste minimo en el escritorio: PcBuildItemInput.Slot y PcBuildItemView.Slot pasan a PcSlot?. Para que compile: sobrecarga TechText.Slot(PcSlot?) en TechShared.cs (devuelve 'Producto' sin ranura) y en PcBuilderViewModel.OpenBuildAsync la linea sin ranura se coloca en Perifericos. No se toco ninguna vista ni XAML.
- StorefrontReservationLine.Slot y PcBuildEventLine.Slot pasan a string? (null solo en lineas de carrito sin ranura).
- Los eventos pcbuild.reserved / released / sold ganan el campo Kind ('Build' o 'Cart'), parametro opcional al final. Cambio aditivo (regla B-08), documentado en api-gateway-v1.md.
- Los caracteres de control se validan en dos niveles: validador (400 validation, mensaje por campo) y dominio (422 guard.control_chars). Los espacios y saltos de los EXTREMOS se recortan y no son error.
- SetBuyer es todo o nada: sin tipo de documento no se acepta numero, complemento ni razon social sueltos (pcbuild.buyer), y se rechaza en reservas vendidas o anuladas (pcbuild.state). El diseño solo decia 'opcionales'.
- No se modifico el CHECK existente ck_pc_builds_publicado: la regla 'un carrito no se publica' va en un CHECK nuevo aparte (ck_pc_builds_tipo_publicado), para que la migracion solo agregue restricciones.
- SavePcBuildCommand rechaza guardar un borrador existente con otro tipo (pcbuild.kind) en vez de ignorar el tipo enviado.
- OpenAPI: se agregaron .Produces<> a catalog, products/{slug} y POST reservations, porque devolvian IResult y sus esquemas de respuesta no salian en /docs/v1/openapi.json.
- Se actualizo docs/architecture/v7-notas/casos-de-uso.md (total 186, firmas cambiadas y comando nuevo) aunque no estaba en la lista del paquete.

## Pendientes

- B4 (migracion V7WebPlatform): crear las columnas de sales.pc_builds y la anulabilidad de sales.pc_build_lines.slot. La columna kind es NOT NULL y el modelo NO declara valor por defecto (igual que channel en la V6): en el AddColumn hay que poner defaultValue 'Build' para rellenar las filas existentes.
- B4: escribir el trigger de la regla que cruza dos tablas: sales.pc_build_lines.slot IS NULL solo si el sales.pc_builds padre tiene kind = 'Cart'. Hoy la regla vive solo en el dominio (PcBuild.AddLine). Conviene tambien impedir en la base que kind cambie despues de insertar.
- B4: regenerar scripts/db_init.sql, documentar las columnas en docs/database/ERD-MINV-V3.md y la normalizacion. Hasta entonces 'has-pending-model-changes' da cambios pendientes.
- FRAGIL hasta B4: con MINV_TEST_PG definida, las pruebas de PostgreSQL que insertan en sales.pc_builds fallaran (la columna kind no existe en el esquema migrado). Los CHECK nuevos no estan probados contra una base real.
- B3 (correo): mailQueued es siempre false. Puntos de encolado marcados con comentario: CreateStorefrontReservationHandler.CreateAsync (pasar mailQueued a StorefrontReservationViews.ViewAsync), ReserveCartHandler y ReservePcBuildHandler, siempre antes del SaveChangesAsync.
- Web (otro paquete): manejar lines[].slot = null, enviar kind/holdDays/buyer y leer reservationHours y maxHoldDays del catalogo en vez de la constante de 48 h.
- RIESGO para la tienda en linea: el formulario actual de la web usa un <textarea> para las notas. Unas notas con saltos de linea en el medio ahora reciben 400. La web debe unir las lineas antes de enviar. Es el unico cambio que rechaza algo que la V6 aceptaba.
- Escritorio (otro paquete): GetPcBuildsQuery() sin Kind devuelve armados Y carritos, asi que Armador de PC > Cotizaciones y la lista de la caja mostraran los carritos hasta que se filtren con Kind. Falta la pantalla Reservas y precargar en la caja los Buyer* de PcBuildRow.
- Cuentas de cliente (otro paquete): ReservationWriter y ReservationSpec son internal de MINV.Application y ya aceptan CustomerId, pensados para CreateMyReservationCommand. No estan probados con CustomerId en canal Web mas alla del dominio.
- Datos de prueba (regla P-13): LocalDataSeeder no crea todavia una reserva de carrito activa.
- deploy/docker-compose.yml y tools/docker_local.ps1 no llevan Minv__Storefront__MaxReservationHours: vale el predeterminado 72. No los toque.
- Limite conocido sin cambios: el vencimiento corre solo en el gateway y solo ve la sucursal de la tienda. Un carrito de mostrador reservado en otra sucursal no vence solo.
- contract.generated.ts que aparecio en la web (no es mio) pudo generarse antes de estos cambios: quien lo mantenga debe regenerarlo para incluir ReserveCartCommand y los campos nuevos.
- CHANGELOG.md y CLAUDE.md no se actualizaron.

## Pruebas

Todo sobre la compilacion final, con `--no-build` despues de `dotnet build MINV.sln`.
- `dotnet build MINV.sln`: Compilacion correcta, 0 advertencias, 0 errores.
- `dotnet test tests/MINV.Domain.Tests`: 284 superadas, 0 con error, 0 omitidas (268 anteriores + 16 nuevas de PcBuildCartTests).
- `dotnet test tests/MINV.Application.Tests`: 82 superadas, 0 con error (76 anteriores + 6 nuevas de ReservationContractTests).
- `dotnet test tests/MINV.Infrastructure.Tests`: 218 superadas, 0 con error, 20 OMITIDAS (incluye 5 nuevas de CartFlowTests y 1 nueva en ModelTests). Las 20 omitidas son las de PostgreSQL: la variable MINV_TEST_PG no esta definida en este equipo.
- `dotnet test tests/MINV.Integration.Tests`: 28 superadas, 0 con error (23 anteriores + 5 nuevas: 4 de StorefrontCartApiTests y 1 de StorefrontShortHoldTests).
- `dotnet test tests/MINV.DesktopClient.Tests`: 47 superadas, 0 con error.
- `dotnet test tests/MINV.Hardware.Tests`: 19 superadas, 0 con error.
NO ejecutado: ninguna prueba contra PostgreSQL (ni las existentes ni nuevas). No abri las credenciales de la base local y, ademas, la migracion de la V7 todavia no existe, asi que el esquema real no tiene las columnas nuevas. Los CHECK nuevos solo estan comprobados contra el DDL que genera EF en memoria (ModelTests), no contra una base real.
NO ejecutado: `dotnet ef migrations has-pending-model-changes` ni `tools\build_v3.ps1`. Hoy darian cambios pendientes, que es lo esperado hasta que B4 cree la migracion.
No se desactivo ni se modifico ninguna prueba existente; en ModelTests solo se agrego un metodo nuevo.
