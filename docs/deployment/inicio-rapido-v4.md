# Inicio rápido · M-INV V4 (multi-sucursal, nube y API) · paso a paso

La **V4** (4.0.0-alpha.1, rama `Inventario-V4.-BaseDeDatosNube`) agrega a M-INV **varias sucursales** (cada una ve lo
suyo), **transferencias** con mercadería en tránsito, un **servidor en la nube** para que el escritorio trabaje por
internet y un **API** para la tienda en línea. Esta guía lo prueba TODO en su propio equipo, sin contratar nada; al
final explica cómo pasar a una nube real. Si todavía no instaló la base local, empiece por la guía de la V3:
[`docs/deployment/inicio-rapido-v3.md`](inicio-rapido-v3.md) (sección 0).

```text
ALGORITMO V4 · TODO EN ESTE EQUIPO
 A. Base local con datos de prueba (3 sucursales, 12 usuarios, transferencias, pedidos web):
        powershell -ExecutionPolicy Bypass -File tools\bd_local.ps1 -Accion recrear
 B. Abrir dist\M-INV-4.0.0-alpha.1-win-x64\M-INV.exe  →  «Base local»  →  empresa MINV  →  correo y contraseña de
        %LOCALAPPDATA%\M-INV\usuarios-prueba.txt  →  elegir la sucursal  →  probar «Transferencias»
 C. Simular la nube en este equipo:
        powershell -ExecutionPolicy Bypass -File tools\servidores_locales.ps1 -Accion iniciar
    y en M-INV.exe elegir «Nube» con el servidor  http://localhost:5080
 D. Probar el API de la tienda en línea con curl contra  http://localhost:5090
    usando la API Key de  %LOCALAPPDATA%\M-INV\claves-integracion.txt
 E. Nube real (DigitalOcean, AWS o Supabase):  docs\deployment\despliegue-nube-v4.md
 Al terminar:  tools\servidores_locales.ps1 -Accion detener
```

## Requisitos

- Windows 10 u 11 y el **SDK de .NET 8 o superior** (`dotnet --list-sdks`; con el SDK 10 funciona).
- PostgreSQL portátil de M-INV ya instalado con `tools\bd_local.ps1 -Accion instalar -Zip <binarios.zip>` (una sola
  vez; ver la guía de la V3). No hace falta ser administrador del equipo.
- Todas las órdenes se escriben en **PowerShell**, dentro de la carpeta del repositorio.

## Paso A · Preparar la base local con datos de prueba

```powershell
powershell -ExecutionPolicy Bypass -File tools\bd_local.ps1 -Accion recrear
```

Tarda unos minutos. Borra la base `minv`, la vuelve a crear con las **110 tablas** de la V4 y carga la empresa de prueba
**MINV · Ferretería El Constructor S.R.L.** usando los mismos casos de uso que el sistema real:

- **3 sucursales**: **CM** Casa matriz (almacén `ALM01`), **EA** El Alto (almacén `ALMEA`, caja `EA-CAJA1`) y **SC**
  Santa Cruz (almacén `ALMSC`, caja `SC-CAJA1`).
- **12 usuarios**, cada uno asignado a su sucursal (tabla de abajo).
- 60 días de operación: ventas en las cajas de las tres sucursales, compras, **reposición semanal** de la casa matriz a
  El Alto y a Santa Cruz (a veces con un faltante), y **pedidos de la tienda en línea** que entran por el API.
- Al final quedan, para explorar, una transferencia **en tránsito** hacia Santa Cruz y una **pendiente** hacia El Alto.

Además, en la V4 el script crea el rol `minv_server` (el que usan los servidores), genera las claves maestras de
integraciones y guarda **solo en este equipo** (nunca en el repositorio):

| Archivo en `%LOCALAPPDATA%\M-INV\` | Qué tiene |
|---|---|
| `usuarios-prueba.txt` | empresa, rol, sucursales, nombre, correo y contraseña de cada usuario de prueba |
| `credenciales-bd-local.txt` | contraseñas de PostgreSQL (`postgres`, `minv_owner`, `minv_app`, `minv_server`) |
| `claves-integracion.txt` | la API Key de la «Tienda en línea» y la clave maestra de integraciones de este equipo |

Para verlos: `notepad $env:LOCALAPPDATA\M-INV\usuarios-prueba.txt` (o `Get-Content …`). Las contraseñas son
aleatorias y cambian cada vez que se recrea la base.

| Rol | Sucursales | Qué conviene probar con él |
|---|---|---|
| Administrador | todas | Sucursales (crear una, asignar usuarios), Integraciones (API Keys, webhooks, entregas), Usuarios |
| Gerencia | todas (gerencia global) | «Todas las sucursales» en la barra superior, stock consolidado con lo que está en tránsito, tablero por sucursal |
| Bodega (tres: CM, EA y SC) | la suya | CM crea y despacha transferencias; EA y SC las reciben |
| Ventas (CM y SC) | la suya | punto de venta, ventas, clientes |
| Cajero (dos en CM, uno en EA, uno en SC) | la suya | caja de su sucursal |
| Consulta | CM, EA y SC | stock y reportes de las tres (sin modificar nada) |

## Paso B · Abrir el escritorio con la base local

1. Abra **`dist\M-INV-4.0.0-alpha.1-win-x64\M-INV.exe`** (si publica el ejecutable usted mismo con
   `powershell -ExecutionPolicy Bypass -File tools\publicar_escritorio.ps1`, use la carpeta que el script indica al
   terminar).
2. En el inicio de sesión elija **Base local**. La pantalla debe decir que PostgreSQL está conectado (si no:
   `tools\bd_local.ps1 -Accion iniciar` y pulse «Reintentar»).
3. Empresa **MINV**; correo y contraseña de un usuario de `usuarios-prueba.txt`. Empiece por **Bodega de CM**.
4. Arriba, en la barra, está la **sucursal activa** (CM). Todo lo que registre (ventas, movimientos, transferencias)
   queda en esa sucursal, y usted solo ve el stock y los documentos de sus sucursales.

**Probar una transferencia completa** (unos 5 minutos):

1. Como **Bodega CM**, abra **Transferencias** y cree una nueva: destino el almacén **ALMEA** (El Alto), agregue
   `FER-004` (Martillo carpintero 16 oz) con cantidad **5** y guárdela. Queda **pendiente**: todavía no movió stock.
2. **Despáchela**. Queda **despachada (en tránsito)**: salió del almacén de CM, todavía no entró a El Alto, y la
   contabilidad de CM registró el envío (cuenta 1.1.06).
3. Cierre la sesión (menú de la cuenta) y entre como **Bodega EA**. En Transferencias verá la misma transferencia
   «en tránsito»: **recíbala** indicando que llegaron **4** y el motivo del faltante (por ejemplo «Caja dañada en el
   camión»). Queda **recibida con faltante**: entraron 4 a El Alto y 1 quedó como merma en tránsito.
4. Abra el detalle: líneas, **manifiesto por lote** (qué lote viajó) y la **bitácora** (quién hizo cada paso y cuándo).
5. Entre como **Gerencia**: en la barra elija **Todas las sucursales**; en **Sucursales** vea el stock consolidado por
   sucursal más la columna **en tránsito** (ahí está la transferencia a Santa Cruz que dejó la carga de datos). En
   Contabilidad › Libro diario verá los asientos de CM (1.1.06 / 1.1.05) y de EA (1.1.05 + 5.1.09 / 2.1.04).

Qué **no** se puede (y está bien que no se pueda): que El Alto despache una transferencia de la casa matriz, recibir más
de lo que se despachó, anular una transferencia ya despachada, o que un cajero de Santa Cruz vea las ventas de El Alto.

## Paso C · Simular la nube en este equipo

El modo **Nube** es el que usarán las sucursales por internet: el escritorio no tiene la contraseña de la base; habla
con el **servidor M-INV**, y el servidor con la base.

```powershell
powershell -ExecutionPolicy Bypass -File tools\servidores_locales.ps1 -Accion iniciar
```

Arranca, contra la base local y con el rol `minv_server`:

| Servidor | Dirección | Para quién |
|---|---|---|
| Servidor en la nube (`MINV.CloudServer`) | `http://localhost:5080` | el escritorio en modo Nube |
| API Gateway (`MINV.ApiGateway`) | `http://localhost:5090` (documentación en `http://localhost:5090/docs`) | la tienda en línea / el ERP |

`tools\servidores_locales.ps1 -Accion estado` dice si ambos responden (también puede abrir
`http://localhost:5080/api/v1/health` y `http://localhost:5090/health` en el navegador).

En el escritorio:

1. Cierre sesión (o abra otro `M-INV.exe`).
2. Elija **Nube**; en **Servidor** escriba `http://localhost:5080` y pulse **Probar**: debe decir
   «Servidor M-INV … disponible».
3. Entre con cualquier usuario de `usuarios-prueba.txt`. Todo funciona igual que en el paso B, pero cada acción viaja
   por HTTP al servidor, que vuelve a comprobar los permisos y las sucursales del usuario en cada pedido.

(Con una dirección que no sea de este equipo, el escritorio exige `https://`.)

## Paso D · Probar el API de la tienda en línea

La carga de datos creó la API Key **«Tienda en línea»** (puede leer catálogo y stock y registrar pedidos, solo en la
casa matriz). Cópiela de `claves-integracion.txt` y, en PowerShell (use `curl.exe`, no `curl`):

```powershell
$key = '<pegue aquí la API Key de claves-integracion.txt>'     # empieza con minv_
curl.exe -s "http://localhost:5090/health"
curl.exe -s "http://localhost:5090/v1/catalog?pageSize=3" -H "Authorization: Bearer $key"
curl.exe -s "http://localhost:5090/v1/stock?sku=FER-004" -H "Authorization: Bearer $key"
```

Registre un pedido como si viniera de la tienda (se guarda en un archivo para evitar problemas de comillas):

```powershell
'{ "externalId": "PRUEBA-0001", "customerCode": "CF", "paymentMethodCode": "EFECTIVO", "lines": [ { "sku": "FER-004", "quantity": 1 } ] }' |
    Set-Content -Encoding ascii pedido.json
curl.exe -s -i -X POST "http://localhost:5090/v1/orders" -H "Authorization: Bearer $key" -H "Content-Type: application/json" --data "@pedido.json"
```

1. La primera vez responde **201 Created** con el número de factura (`F-CM-…`): la venta quedó registrada en la casa
   matriz, con su salida de stock, factura, pago y asiento. Véala en el escritorio (Ventas).
2. Envíe **exactamente lo mismo** otra vez: responde **200** con `Idempotent-Replayed: true` y la MISMA factura. No se
   vendió dos veces (así se protege la tienda de los reintentos por cortes de red).
3. Cambie `"quantity": 1` por `2` sin cambiar el `externalId` y envíelo: responde **422** (`idempotency`): ese id ya
   se usó con otro contenido.
4. `curl.exe -s -i "http://localhost:5090/v1/transfers" -H "Authorization: Bearer $key"` responde **403**: esta llave
   no tiene el alcance `transfers:read`. Para más permisos, el Administrador crea otra llave en **Integraciones ›
   API Keys**.
5. Abra `http://localhost:5090/docs` en el navegador: ahí está la documentación interactiva de todo el API.

Guía completa del API (todas las rutas, errores, webhooks y cómo verificar sus firmas):
[`docs/integration/api-gateway-v1.md`](../integration/api-gateway-v1.md).

**Webhooks**: la carga de datos registra un webhook de prueba hacia `https://tienda.elconstructor.example/…`, un
dominio que no existe a propósito: en **Integraciones › Entregas** verá sus intentos fallidos y los reintentos (1 min,
5 min, 30 min…). Para recibir webhooks de verdad hace falta una URL https pública.

Cuando termine: `powershell -ExecutionPolicy Bypass -File tools\servidores_locales.ps1 -Accion detener`.

## Paso E · Pasar a una nube real

Siga [`docs/deployment/despliegue-nube-v4.md`](despliegue-nube-v4.md): crear un PostgreSQL gestionado (DigitalOcean,
AWS RDS o Supabase), prepararlo con `tools\bd_nube.ps1 -Accion preparar -Conexion "<cadena del rol dueño>"`, levantar
los dos servidores detrás de https (con `dotnet`, el ejecutable publicado o `deploy\docker-compose.yml`) y conectar los
escritorios en modo **Nube** a `https://…`.

## Si algo no funciona

| Síntoma | Solución |
|---|---|
| `bd_local.ps1` dice que PostgreSQL no está instalado | instálelo una vez: `tools\bd_local.ps1 -Accion instalar -Zip <binarios.zip>` (guía V3) |
| El escritorio: «Sin conexión con la base de datos» | `tools\bd_local.ps1 -Accion iniciar` y «Reintentar» |
| «Su usuario no tiene sucursales asignadas» | entre como Administrador › Sucursales y asígnele una, o recree la base |
| Modo Nube: «Sin conexión con el servidor» | `tools\servidores_locales.ps1 -Accion iniciar` (o `-Accion estado`) y **Probar** |
| Modo Nube: «Este servidor es M-INV …: actualice el escritorio» | el escritorio y los servidores deben ser de la misma versión mayor (4): vuelva a publicar el escritorio |
| El API responde 401 | la llave está mal copiada (debe empezar con `minv_` y no llevar espacios) o se recreó la base (la llave cambia: vuelva a copiarla) |
| El API responde 403 | la llave no tiene ese alcance (la de la tienda solo lee catálogo y stock y registra pedidos) |
| El API responde 422 `stock.insufficient` | no hay stock de ese producto en la casa matriz: pruebe con otro SKU del catálogo |
| El API responde 429 | demasiadas peticiones seguidas (más de 120 por minuto): espere un momento |
| No se pueden registrar webhooks desde el escritorio en «Base local» | los secretos de los webhooks se cifran con la clave maestra que tienen los servidores: regístrelos en modo **Nube** o por el API |
