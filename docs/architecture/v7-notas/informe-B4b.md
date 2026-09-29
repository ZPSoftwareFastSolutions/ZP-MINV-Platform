# Informe del paquete B4b · datos de prueba de la V7 (reglas P-13 y A-13)

## Resumen

Paquete B4b terminado sobre la rama Inventario-V7, sin commits. Al empezar, el último commit era `65644f3` (B4a, migración V7WebPlatform)
y `git status` solo mostraba cambios de la web (otro equipo, no los toqué): no había avance previo de B4b. Durante la sesión apareció el
commit `7c38afe` (panel web, W3a): solo archivos de la web y su informe; nada de este paquete. Lo de B4b queda SIN confirmar en el árbol.

1. SEMBRADOR (`LocalDataSeeder`, archivo nuevo `Seeding/LocalDataSeeder.Web.cs`): después de las reservas web de la V6, la carga crea
   con los casos de uso (regla A-13) la tienda web de la V7: 2 cuentas de cliente registradas con `RegisterCustomerAccountCommand`
   (canal web, sucursal de la tienda CM, contraseña aleatoria como la del personal) que reservan desde su cuenta con
   `CreateMyReservationCommand` un carrito y un armado cada una; un carrito de la tienda sin cuenta de UN solo monitor, vigente y con el
   CI para la factura; otro carrito de la tienda que venció hace tres días (lo cierra `ExpirePcBuildReservationsCommand`); y un carrito
   de mostrador (`ReserveCartCommand`, vendedor de CM) para un cliente habitual. Todas tienen correo: quedan 9 correos pendientes en la
   cola en una empresa nueva (7 de la V7 + 2 de la V6). En memoria cuesta unos 0,3 s (la demostración sigue en unos 17-21 s).
2. RESULTADO: `SeedResult.Users` trae, después del personal, las 2 cuentas (rol CLIENTE): así quedan cubiertas las pruebas que exigen un
   usuario por rol (`DemoWorkspaceTests`, `LocalDataSeederTests`). `SeedResult.Web` (record nuevo `SeedWeb`) resume la tienda web.
   `SeedTech.PcBuilds` cuenta ahora solo armados (sin carritos).
3. `usuarios-prueba.txt`: el texto lo arma ahora `SeedUsersFile.Text` (Seeding) y el CLI lo usa; gana la sección «Clientes de la tienda
   web» con Rol (Cliente), Nombre, Correo y Contraseña en las mismas columnas que el personal, y una línea de resumen «Tienda web (V7)».
   Su único lector (`ScreenshotRunner.LocalUsers`, capturas del escritorio) no cambia: una prueba repite su expresión regular.
4. `minv verify`: sigue pasando con los datos nuevos (comprobado en una base PostgreSQL temporal) y gana la comprobación de reservas
   (lo reservado de cada existencia = la suma de sus reservas activas, de armados Y de carritos) y un informe de cuentas y correos.
5. PRUEBAS QUE DEPENDÍAN DE LA HORA: StorefrontApiTests, StorefrontCartApiTests, StorefrontShortHoldTests y StorefrontFlowTests miden
   ahora contra el reloj del servidor (`IClock`/`DemoClock`), sin cambiar lo que comprueban. Pasaron entre las 04:00 y las 06:00 hora
   local, dentro de la ventana «antes de las 10:00» en que fallaban (el cálculo ya no depende de la hora del equipo).
6. Pruebas existentes puestas al día con los datos nuevos (numeración relativa, recuentos, filtros por tipo) y prueba nueva
   `LocalDataSeederTests.V7_la_tienda_web_trae_cuentas_de_cliente_con_sus_reservas_carritos_y_correos_en_cola`. Con PostgreSQL
   (clúster temporal propio en localhost:55439, ya borrado): `Los_datos_de_prueba_respetan_todas_las_restricciones_de_PostgreSQL` y las
   23 pruebas de PostgreSQL pasan.
7. QUEDA EN ROJO una comprobación anterior (V4.2) que depende del DÍA DE LA SEMANA, no de este paquete:
   `DemoWorkspaceTests.La_demostracion_es_Tech_Zone_Gaming_y_permite_ingresar_con_cada_rol` falla hoy (martes) en «Solo 943 series»
   (umbral fijo de 1000 series). Con ese umbral salteado a mano pasa entera, incluido lo de la V7. Detalle en «Pendientes».

## Contratos (nombres exactos)

- `MINV.Infrastructure.Seeding.SeedWeb(int Accounts, int AccountReservations, int AccountBuilds, int ActiveCarts, int ExpiredCarts,
  int CounterCarts, int QueuedMails)`: cuentas de cliente; reservas hechas desde ellas (un carrito y un armado por cuenta); de ellas,
  armados; carritos de la tienda sin cuenta vigentes; carritos de la tienda vencidos (cerrados); carritos de mostrador; correos
  PENDIENTES en la cola al terminar (todos, también los de la V6). Valores en una empresa nueva: `(2, 4, 2, 1, 1, 1, 9)`.
- `SeedResult(..., string? StorefrontUser = null, SeedWeb? Web = null)` (parámetro nuevo al final). `SeedResult.Users`: el personal
  (administrador + 11) y DESPUÉS las 2 cuentas: `SeedUser(RoleCodes.Customer, "Cliente", nombre, correo, contraseña, "CM")`.
- `LocalDataSeeder.CustomerRoleName = "Cliente"` (columna «Rol» de las cuentas; el nombre del rol en `RoleCodes.All` sigue siendo
  «Cliente web»).
- `SeedTech.PcBuilds` y `SeedTech.PcBuildsSold`: solo `PcBuildKind.Build`. `SeedTech.WebReservationsActive`/`Expired` siguen siendo las
  2 reservas web de armados de la V6. En una empresa nueva `PcBuilds` = 12 (8 del catálogo + 2 de la V6 + 2 de las cuentas).
- `MINV.Infrastructure.Seeding.SeedUsersFile.Text(SeedResult)` y `SeedUsersFile.CustomersTitle = "Clientes de la tienda web"`.
  Formato de la sección (después de la tabla del personal y antes de «Son contraseñas de PRUEBA…»): línea
  `Clientes de la tienda web: ingresan en la web con «Ingresar» (Mi cuenta: sus datos y sus reservas); no sirven para el escritorio.`,
  cabecera `{"Rol",-15} {"Nombre",-26} {"Correo",-44} Contraseña`, 104 guiones y una fila por cuenta
  `{RoleName,-15} {Name,-26} {Email,-44} {Password}` (las del personal: lo mismo con `{Password,-16} {Branches}`). Al final:
  `Tienda web (V7): 2 cuentas de cliente con 4 reservas propias, 1 carrito vigente de un solo monitor, 1 vencido y 1 de mostrador; 9 correos de confirmación en la cola (los envía el API Gateway con Minv:Mail).`
  `Program.Credentials` del CLI se quitó (lo reemplaza `SeedUsersFile.Text`).
- CUENTAS DE CLIENTE (fijas): «Rocío Villca Choque» `rocio.villca@correo.example` `+591 71550321` (carrito: un mouse y un mousepad,
  notas «Paso a recogerlo el sábado por la mañana.»; armado «Mi PC gamer por partes»: procesador, refrigeración, placa, memoria y SSD de
  `ARM-CM-000002`) y «Marcelo Quisbert Loza» `marcelo.quisbert@correo.example` `76019482` (carrito: audífonos y teclado; armado
  «Actualización de mi PC de oficina»: piezas base de `ARM-SC-000002`). Contraseña: `SeedOptions.SharedPassword ?? NewPassword()`
  (formato «Palabra-1234»; en la demostración, la compartida). Clientes `WEB-000001` y `WEB-000002`.
- REGISTRO como la web (`POST /api/v1/web/account/register`): ámbito nuevo con `ITenantContext.SetBranches(new BranchScope(false,
  [Guid.Empty], null))` e `IRequestOrigin.Set(RequestChannels.Web, null)`, `RegisterCustomerAccountCommand(tenant, nombre, correo,
  teléfono, contraseña, BranchCode: "CM", MachineName: "web datos-prueba", ClientVersion: "datos-prueba")`; el mismo ámbito queda con la
  sesión del cliente y envía `CreateMyReservationCommand(líneas, Kind, HoldDays: null, Notes, Name)` (48 h: la vigencia configurada).
- CARRITOS DE LA TIENDA (sesión del administrador en CM, como las reservas web de la V6), `CreateStorefrontReservationCommand(...,
  Kind: Cart)`: VENCIDO a las 17:30 de hace 3 días, `HoldDays: 1`, contacto «Jorge Salazar Mendoza» `70654213`
  `jorge.salazar@correo.example`, un mando y un cargador, llave `datos-prueba-carrito-vencido`; VIGENTE de un solo monitor,
  `HoldDays: null` (48 h), contacto «Paola Rivera Gutiérrez» `+591 76120934` `paola.rivera@correo.example`, datos para la factura
  `ReservationBuyerInput(1, "6043317", null, "Paola Rivera Gutiérrez")`, llave `datos-prueba-carrito-activo`.
- CARRITO DE MOSTRADOR: el usuario de Ventas de CM, `ReserveCartCommand([router, cable], cliente, teléfono, correo, "Pasa a recogerlo el
  lunes; factura a su nombre.", null, ReservationBuyerInput(1, CI del cliente, null, nombre), código del cliente)` para el primer cliente
  persona de CM del catálogo con teléfono y correo (`CLI-001`, «Diego Alejandro Mamani Rojas»).
- ELECCIÓN DE PRODUCTOS: por categoría, el de más disponible en el almacén de CM (dejando al menos 1 unidad libre); categorías MOU, PAD,
  AUD, KEY, MON, MAND, CARG, RED y CAB. Nunca juegos (JUE) ni gabinetes. Si no alcanza, se informa en el registro y se sigue.
- HORARIO SIMULADO: lo «de hoy» desde las 09:00 (registros +02 y +17 min, carritos +05 y +20, armados +11 y +26, monitor +33, mostrador
  +52) o, si la carga corre antes de las 10:00, AYER desde las 18:00 (igual que la V6). Todas las vigentes son de 48 h: ninguna vence
  antes de 25 h y todas vencen a las 49 h (las pruebas de vencimiento cuentan con eso).
- NUMERACIÓN en una empresa nueva: `RES-WEB-000001` (vencido), `RES-WEB-000002`/`ARM-WEB-000003` (Rocío), `RES-WEB-000003`/`ARM-WEB-000004`
  (Marcelo), `RES-WEB-000004` (monitor), `RES-CM-000001` (mostrador); `ARM-WEB-000001/2` son las de la V6.
- REGISTRO DE LA CARGA: líneas «… dd/MM/yyyy: <nombre> se registró en la tienda web (<correo>) y reservó desde su cuenta el carrito … y
  el armado …», «… carrito de la tienda de un solo monitor … y carrito de mostrador … para …» y «Tienda web (V7): …».
- `minv verify --codigo X` (con empresa) agrega: `✔|✖ Reservas: N carritos y M armados reservados (K reservas de stock activas), B
  existencias cuyo reservado no es la suma de sus reservas` (B > 0 hace fallar el resultado) y `· Tienda web: N cuentas de cliente y Q
  correos de confirmación pendientes en la cola` (informativo).
- `tools/bd_local.ps1`: solo dos mensajes finales (el archivo de usuarios incluye los clientes; esas cuentas son de la tienda web). ASCII.

## Desviaciones del diseño

- El diseño no fija la composición de los datos: lo de «Contratos» es decisión de este paquete. El «armado» de cada cuenta es un kit de
  piezas base de un armado compatible del catálogo (procesador, refrigeración, placa, memoria y SSD), no una PC completa: no gasta
  tarjetas de video ni gabinetes y no toca el gabinete `CASE-COR-4000D` que usan las pruebas de vencimiento. Sale sin errores de
  compatibilidad (solo avisos de piezas faltantes).
- Los carritos de la tienda sin cuenta se crean con la sesión del administrador (como las reservas web de la V6), no con el principal
  técnico `tienda-web` (vive en el gateway).
- `SeedTech.PcBuilds`/`PcBuildsSold` cambian de significado (solo armados); el CLI ya los describía como «armados de PC».
- `DemoSession.Users` (los accesos de la demostración del escritorio) incluye ahora un acceso «Cliente» (lo exige
  `DemoWorkspaceTests`). No toqué el escritorio: ver «Pendientes».
- Fuera de lo pedido: `minv verify` compara lo reservado con las reservas activas (el pedido decía «si verifica reservas, que cuente las
  de carrito»: no las verificaba; ahora sí, y cuenta carritos y armados).
- Pruebas existentes puestas al día, sin cambiar lo que comprueban (además de las de la hora):
  - `LocalDataSeederTests.Genera_una_empresa…`: en vez de 12 usuarios en total se exigen 12 del personal (primero) y 2 cuentas; el
    dominio `@techzone.example` se exige al personal y a los clientes un correo `.example` fuera de ese dominio; `GetUsersQuery` 13 → 15.
  - `CartFlowTests`: números `RES-WEB-…`/`RES-CM-…` y recuentos relativos a lo que trae la carga (helper `LastNumberAsync`); la lista de
    carritos = los de la carga + los 3 de la prueba (el canal Web se exige a los de la prueba: la carga trae uno de mostrador); al
    adelantar 73 h vencen `1 + las reservadas de la carga` (antes `InRange(1, 2)`).
  - `StorefrontFlowTests`: el tablero cuenta `1 + carritos vigentes + reservas de las cuentas` y lo mismo que la lista; a las 49 h vencen
    `1 + las reservadas de la carga`; «un armado web no se publica» toma un ARMADO (`Kind: Build`: un carrito falla antes por su tipo).
  - `DemoWorkspaceTests` y `V42TechSeedTests`: `GetPcBuildsQuery(Channel: Desktop, Kind: Build)` (el carrito de mostrador es del canal
    del escritorio); `V42TechSeedTests` 10 → 12 armados.
  - Escritorio (`tests/MINV.DesktopClient.Tests`, no las pantallas): `StorefrontScreenTests` identifica la reserva web de la V6 (armado sin
    cliente) y los indicadores cuentan todas las reservas web vigentes; `BusinessScreenTests` 7 → 8 roles (el rol CLIENTE de B2).
- Las contraseñas de las cuentas no se imprimieron en esta sesión: la salida del CLI se filtró y se enmascaró.

## Pendientes

- `DemoWorkspaceTests.La_demostracion_es_Tech_Zone_Gaming_y_permite_ingresar_con_cada_rol` (línea 89, V4.2): «Solo 943 series» con el
  umbral fijo `totals.Total > 1000`. No lo causa este paquete (la V7 no crea ni mueve series: solo reserva) sino el calendario: la
  demostración opera 8 días y las compras se piden los lunes y jueves y llegan en 2 días; con hoy MARTES solo entra la del jueves (la del
  lunes llega mañana) y las series quedan en 943; el lunes 28/09 entraban dos y pasaba. No lo cambié (regla: no modificar lo que
  comprueban las pruebas). Arreglo sugerido: comparar con el tope de la lista (`new SearchSerialsQuery().Max` = 500), que es lo que el
  comentario quiere probar, o hacer que la demostración reciba compras sin depender del día de la semana.
- Escritorio (otro paquete): la lista de accesos de la demostración muestra «Cliente» con la descripción «Sin permisos asignados»
  (`LoginViewModel.Describe` no conoce CLIENTE) y entrar al escritorio con esa cuenta no sirve (`GetWorkspaceQuery` exige
  `inventory.stock.view`; lo comprueba `DemoWorkspaceTests`). Conviene ocultar ese acceso o explicar que es de la tienda web. El
  armador y la caja listan también los carritos y las reservas de las cuentas (`GetPcBuildsQuery()` sin tipo, ya lo decía B1): al
  filtrarlos habrá que volver a ajustar `StorefrontScreenTests`.
- Web (otro equipo): hay 2 cuentas listas para probar «Mi cuenta» (correos arriba; contraseñas en `usuarios-prueba.txt` después de
  `tools\bd_local.ps1 -Accion recrear`). La base `minv` de la tienda EN LÍNEA no se recargó (no se tocó).
- Las reservas vigentes de la carga vencen a las 48 h: en una base local recreada hace más de dos días aparecen «Vencida» hasta que el
  trabajo de vencimiento del gateway las cierre. Los correos en cola salen solo si un gateway con `Minv:Mail` apunta a esa base; el del
  carrito vencido lo cancelará el despachador (la reserva ya no está vigente).
- Documentación (paquete de documentos): `GUIA-DE-INICIO.md`, `docs/deployment/inicio-rapido-*.md` y `CHANGELOG.md` deben mencionar la
  sección «Clientes de la tienda web» de `usuarios-prueba.txt` y las líneas nuevas de `minv verify`. `DemoWorkspace` sigue diciendo
  «unos 17 s» (sigue siendo cierto).
- No usé el PostgreSQL de localhost:5432 ni leí credenciales: todo contra un clúster temporal (localhost:55439) que ya detuve y borré.
  Si se quiere, repetir con la instancia local como indicó B4a.

## Pruebas

Todo desde `D:\Proyectos Claude 2\Sistema Inventario\ZP-MINV-V7`, pruebas con `--no-build` después de compilar, ejecutadas el
29/09/2026 entre las 04:00 y las 06:00 hora local (ventana «antes de las 10:00» en que fallaban las pruebas de la tienda).
«Clúster temporal» = PostgreSQL 16 propio (binarios de `%LOCALAPPDATA%\M-INV\postgresql-16\bin`, `initdb` en la carpeta temporal de la
sesión, localhost:55439, contraseña aleatoria fuera del repositorio, roles `minv_app` y `minv_server` NOLOGIN), detenido y borrado al final.

- Línea base antes de tocar nada: `dotnet test tests/MINV.Infrastructure.Tests --no-build --filter "FullyQualifiedName~DemoWorkspaceTests.La_demostracion_es_Tech_Zone_Gaming"`
  → «Con error … [17 s] Assert.Equal() Failure: Collections differ» (el rol CLIENTE sin usuario).
- `dotnet build MINV.sln -nologo -v q --no-incremental` (final): «Compilación correcta. 0 Advertencia(s) 0 Errores».
- `dotnet test tests/MINV.Infrastructure.Tests --no-build --filter "…LocalDataSeederTests|DemoWorkspaceTests|V42TechSeedTests|StorefrontFlowTests|CartFlowTests|AccountFlowTests|ReservationMail|MailDispatcherTests"`
  → «Con error: 1, Superado: 44, Omitido: 0, Total: 45» (4 m 40 s). La única: `DemoWorkspaceTests.La_demostracion_es_Tech_Zone_Gaming…`
  «Solo 943 series» (ver «Pendientes»).
- La misma prueba de la demostración con la línea del umbral de 1000 series comentada A MANO y restaurada enseguida:
  «Correctas! - Con error: 0, Superado: 1» (21 s): pasan el rol CLIENTE, los recuentos V7 y el acceso de cliente.
- Tiempo de la demostración: `LocalDataSeederTests.La_demostracion_trae_imagenes_y_precios_para_el_punto_de_venta` (prepara la demostración)
  18, 21, 21 s con la V7 (una corrida fría de 45 s) y 27, 28 s con el paso V7 desactivado temporalmente (medición ruidosa); en el
  registro de la carga el paso V7 dura unos 0,3 s.
- `dotnet test tests/MINV.Integration.Tests --no-build` → «Correctas! - Con error: 0, Superado: 45, Omitido: 0, Total: 45» (1 m 19 s).
- `dotnet test tests/MINV.DesktopClient.Tests --no-build` (primera corrida) → «Con error: 1, Superado: 46, Total: 47»:
  `StorefrontScreenTests.V6_una_cotizacion_propia…` «Expected: "1" Actual: "6"» (un indicador que no había puesto al día); corregido,
  `--filter "FullyQualifiedName~StorefrontScreenTests"` → «Correctas! - Con error: 0, Superado: 4, Total: 4».
- `dotnet test tests/MINV.DesktopClient.Tests --no-build` (final, después de la compilación completa) → «Correctas! - Con error: 0,
  Superado: 47, Omitido: 0, Total: 47» (4 m 52 s). Incluye `BusinessScreenTests.Catalogo_reportes_contabilidad_y_usuarios…`, que B4a
  dejó en rojo por el rol CLIENTE.
- Con `MINV_TEST_PG` (clúster temporal), `dotnet test tests/MINV.Infrastructure.Tests --no-build --filter "FullyQualifiedName~Los_datos_de_prueba_respetan_todas_las_restricciones_de_PostgreSQL"`
  → «Correctas! - Con error: 0, Superado: 1, Omitido: 0, Total: 1» (54 s).
- Con `MINV_TEST_PG` (clúster temporal), `dotnet test tests/MINV.Infrastructure.Tests --no-build` completo → «Con error: 1, Superado: 279,
  Omitido: 0, Total: 280» (5 m 54 s). La única: la de las 943 series. Pasan las 23 de PostgreSQL (también
  `H_SeedBillingPostgresTests` y `Con_un_rol_sin_privilegios…`, que cargan la empresa de prueba con la V7).
- CLI contra el clúster temporal (base `minv_b4b`): `minv migrate` → «✔ Base de datos al día: … 20260929025923_V7WebPlatform»;
  `minv datos-prueba --codigo TECHZONE --dias 20 --sin-facturacion --credenciales <temporal>` → código 0, «✔ Datos de prueba listos en
  85 s», con las líneas de la V7 y el archivo con la sección nueva; `minv verify --codigo TECHZONE` → «✔ 157 tablas … ✔ Conservación:
  1552 movimientos, 0 … ✔ Transferencias: 17 en total, 0 … ✔ Series e IMEI: 1728 en total, 0 … ✔ Reservas: 4 carritos y 3 armados
  reservados (26 reservas de stock activas), 0 existencias cuyo reservado no es la suma de sus reservas · Tienda web: 2 cuentas de
  cliente y 9 correos de confirmación pendientes en la cola … RESULTADO: base de datos correcta» (código 0).
- `tools/bd_local.ps1`: sigue siendo ASCII (búsqueda de caracteres fuera de ASCII: ninguno). No lo ejecuté (prohibido).
- No ejecutado: `tools\build_v3.ps1`, `MINV.Domain.Tests`, `MINV.Application.Tests` y `MINV.Hardware.Tests` (no cambió nada de su área),
  nada contra localhost:5432 ni la base `minv`, Docker, la web.
