# Inicio rápido · M-INV V4.2 (edición Tecnología) · paso a paso

La **V4.2** (4.2.0-alpha.1, rama `Inventario-V4.2`) convierte M-INV en el sistema de inventarios de una **tienda de
tecnología y gaming**: componentes de PC, computadoras, monitores, periféricos, consolas (PS4 y PS5, Xbox Series X y
Series S, Nintendo Switch y Switch 2), videojuegos, accesorios, redes y software. Cada equipo se recibe, se vende, se
transfiere y vuelve por garantía con su **número de serie o IMEI**; el catálogo tiene **fichas técnicas** y filtros por
especificación; un **armador de PC** revisa la compatibilidad y cotiza; y los casos de **garantía (RMA)** tienen su propia
pantalla. Todo lo de la V4.1 sigue igual: sucursales, nube, API y **facturación del SIN** (la factura ahora lleva la serie
o el IMEI de cada unidad). El tema visual es **gaming** (oscuro por defecto, con acentos violeta y cian).

Esta guía lo prueba TODO en su propio equipo, con la empresa de prueba **Tech Zone Gaming S.R.L.** y el **simulador del
SIN** (sin valor legal): no hace falta contratar nada ni internet. Diseño:
[`docs/architecture/edicion-tecnologia-v4.2.md`](../architecture/edicion-tecnologia-v4.2.md) · pantallas:
[`docs/product/escritorio-v4.2.md`](../product/escritorio-v4.2.md) · facturación paso a paso (igual que en la V4.1):
[`inicio-rapido-v4.1.md`](inicio-rapido-v4.1.md).

> **Importante: una sola base local por equipo.** `tools\bd_local.ps1 -Accion recrear` **borra la base `minv`** de este
> equipo (si hoy tiene la de la V4.1, con la ferretería de ejemplo) y la reemplaza por Tech Zone Gaming. También cambia
> las contraseñas de prueba de `usuarios-prueba.txt`. Si más adelante quiere volver a la V4.1, cambie a su rama y vuelva
> a recrear la base (§10).

```text
EL ALGORITMO V4.2 · TODO EN ESTE EQUIPO (PowerShell, dentro de la carpeta del repositorio)
 0. Esté en la rama de la V4.2:                       git switch Inventario-V4.2
 1. Detenga los servidores de la versión anterior (si estaban encendidos):
        powershell -ExecutionPolicy Bypass -File tools\servidores_locales.ps1 -Accion detener
 2. Recree la base con Tech Zone Gaming (152 tablas, 60 días de operación, series, RMA, armados y facturas; unos minutos):
        powershell -ExecutionPolicy Bypass -File tools\bd_local.ps1 -Accion recrear
 3. Publique el programa:  powershell -ExecutionPolicy Bypass -File tools\publicar_escritorio.ps1
 4. Encienda la «nube» local: simulador del SIN :5095 + servidor en la nube :5080 + API Gateway :5090
        powershell -ExecutionPolicy Bypass -File tools\servidores_locales.ps1 -Accion iniciar
 5. Abra dist\M-INV-4.2.0-alpha.1-win-x64\M-INV.exe
      → «Nube» → servidor http://localhost:5080 → «Probar» → empresa TECHZONE → correo y contraseña de
        %LOCALAPPDATA%\M-INV\usuarios-prueba.txt
      («Base local» también funciona, sin el servidor en la nube; «Explorar la demostración» no necesita nada)
 6. Bodega CM:   Órdenes de compra › Recibir → escanee o pegue las series (el IMEI inválido y las repetidas se marcan)
 7. Cajero CM:   Punto de venta → una tarjeta de video → elija su serie → CI o NIT → Cobrar → factura VÁLIDA con la serie
 8. Cajero CM:   un router 4G (lleva IMEI) → la factura lo lleva en «numeroImei»
 9. Ventas CM:   Armador de PC → arme, vea lo incompatible atenuado → Guardar cotización → Proforma → Vender en caja
10. Cajero y Bodega: Garantías y RMA → abrir un caso con la serie → diagnóstico → reemplazar con otra unidad → entregar
11. Cualquiera:  Series e IMEI → busque una serie → su línea de tiempo (compra, transferencias, venta, garantía…)
12. Catálogo → categoría y plataforma (PS5, Switch 2…) → facetas por especificación (socket, tipo de RAM…)
 Al terminar:  powershell -ExecutionPolicy Bypass -File tools\servidores_locales.ps1 -Accion detener
 Quédese en la V4.2 mientras la prueba: no cambie de rama hasta decidir pasar a otra versión.
```

## Requisitos

- Windows 10 u 11 y el **SDK de .NET 8 o superior** (`dotnet --list-sdks`; con el 10 funciona). No hace falta ser
  administrador del equipo.
- PostgreSQL portátil de M-INV instalado **una sola vez** con
  `powershell -ExecutionPolicy Bypass -File tools\bd_local.ps1 -Accion instalar -Zip <postgresql-16.x-windows-x64-binaries.zip>`
  (guía de la V3: [`inicio-rapido-v3.md`](inicio-rapido-v3.md), sección 0). Si ya lo instaló para la V3, la V4 o la V4.1,
  sirve el mismo: solo hay que **recrear** la base (paso 2).
- Para la demostración basta con el programa publicado (paso 3): no necesita PostgreSQL.

## 1. Pasar a la rama de la V4.2

```powershell
git switch Inventario-V4.2
powershell -ExecutionPolicy Bypass -File tools\servidores_locales.ps1 -Accion detener   # los de la versión anterior, si corrían
```

Los servidores locales se compilan desde la rama activa: si quedaron encendidos los de la V4.1, deténgalos antes de
seguir (el paso 2 también lo hace solo).

## 2. Recrear la base con Tech Zone Gaming

```powershell
powershell -ExecutionPolicy Bypass -File tools\bd_local.ps1 -Accion recrear
```

Compila, borra la base `minv`, la crea con las **152 tablas en 10 esquemas** de la V4.2 (el esquema nuevo `service` es el
de las garantías) y carga la empresa de prueba con los **mismos casos de uso** que el sistema real (la carga de datos
tarda unos **2 minutos**; el total, unos minutos más si es la primera compilación). Al final ejecuta `minv verify`, que
comprueba tablas, seguridad por filas, conservación del stock, transferencias, facturas y que en cada sucursal las **series
en stock coincidan con el stock**.

**La empresa de prueba: Tech Zone Gaming S.R.L.** (código de empresa **`TECHZONE`**)

| Qué | Detalle |
|---|---|
| Sucursales | **CM** Casa matriz La Paz (almacén central y 3 cajas: CAJA01, CAJA02 y CAJA03 de servicio técnico), **CB** Cochabamba y **SC** Santa Cruz (una caja cada una: CB-CAJA1 y SC-CAJA1) |
| Catálogo | 159 productos con ficha técnica e imagen propia en 35 categorías (Componentes, Computadoras, Monitores, Periféricos, Consolas, Videojuegos, Accesorios de consola, Redes, Cables y adaptadores, Software y servicios, con sus subcategorías), 176 especificaciones, 40 marcas; 105 productos se venden por unidad con serie (2 de ellos, los routers 4G, con IMEI); 4 servicios de la tienda (ensamblado, instalación de Windows y mantenimiento); precios con IVA y costos netos de IVA (el 87 % del costo con IVA): margen del 15 al 35 % sobre el precio neto (22 % en promedio) |
| Proveedores y clientes | 8 mayoristas ficticios y 30 clientes (gamers con CI, empresas e instituciones educativas con NIT), todos con correos `.example` |
| Facturación | NIT de simulación **1023456029**, ambiente 2 (pruebas) contra el simulador del SIN de este equipo; factura los **últimos 25 días**; 8 puntos de venta (uno por caja y el punto 0 de cada sucursal); productos, unidades y medios de pago homologados (actividades 4741100, 4741200 y 4742100) |
| Volumen | 60 días de operación: unas **2.700 series e IMEI**, 7 casos RMA, 8 armados de PC y unos **520 documentos fiscales** |

**Escenarios cargados para explorar** (todo ocurrió con los casos de uso, en su fecha y hora):

| Escenario | Qué pasó | Dónde verlo |
|---|---|---|
| Compras con series | Bodega de la casa matriz arma las órdenes con el pedido sugerido los lunes y jueves, Gerencia las aprueba y llegan en su fecha con la serie (o el IMEI) de cada unidad; alguna compra urgente para las piezas de un armado. Queda un **borrador** de reposición de tarjetas de video | Órdenes de compra · Series e IMEI (primer hecho «ingresó al stock» con el proveedor) |
| Transferencias con series | Primera distribución de la casa matriz a Cochabamba y Santa Cruz y reposiciones (lunes y jueves a CB, martes y viernes a SC); algunas llegan con un **faltante** (la serie que no llegó queda dada de baja con su motivo). Al final: una a Santa Cruz **en tránsito** y una a Cochabamba **pendiente** de despacho | Transferencias · Series e IMEI (hechos «transferencia despachada / recibida») |
| Ventas con serie y factura | Las cuatro cajas venden cada día; cada equipo sale con su serie escaneada y la factura la lleva (`numeroSerie` / `numeroImei`) | Punto de venta · Ventas · Documentos fiscales |
| Pedidos de la tienda en línea | Martes, jueves y sábados llegan por el API (despachados desde la casa matriz, con sus series y su factura) | Ventas (canal api) · Integraciones |
| 7 casos de garantía (RMA) | **CM**: uno con **reemplazo** (se entregó otra unidad y la defectuosa volvió al proveedor), uno **en el proveedor** y uno **en diagnóstico** (con una nota del técnico); **SC**: uno **reparado y entregado** y uno **recién recibido**; **CB**: uno **reparado por el proveedor** (listo para entregar) y uno **rechazado** por mal uso | Garantías y RMA (chips por estado) |
| Devolución por falla | Un cliente de la casa matriz devolvió un equipo con falla de fábrica: se le reembolsó, la unidad **no volvió al stock** y al día siguiente se devolvió al proveedor | Ventas › Devoluciones · Series e IMEI (estado «devuelta al proveedor») |
| 8 armados de PC | Cotizados en los últimos días hábiles: **dos cobrados** en la caja de Cochabamba y de Santa Cruz (las piezas llegaron desde la casa matriz), **uno incompatible en borrador** (procesador AM4 en placa AM5 y RAM DDR4) y **otro cotizado con errores confirmados** (RTX 5090 en un gabinete Mini-ITX, fuente corta…); el más antiguo, de 7 días, puede aparecer **Vencido** | Armador de PC › Cotizaciones |
| Tomas físicas | Una **contabilizada** (videojuegos, cables, licencias y un mouse con serie) y otra **en curso** (periféricos y consolas) | Toma física |
| Facturación (como en la V4.1) | Venta menor con el NIT 99003, un **NIT rechazado** (1037) re-emitido con código de excepción, un **corte de internet en Cochabamba** (10:00 a 13:00, facturas fuera de línea recuperadas con evento y paquete), un **corte de energía en Santa Cruz** (15:00 a 17:00, 3 facturas manuales **CAFC** transcritas a las 17:30), 3 anulaciones (con devolución, re-emitida y revertida), 2 devoluciones parciales con **nota crédito-débito** (los equipos, por su serie) y 4 facturas de proveedores | Facturación › Estado SIAT, Documentos fiscales y Libros fiscales ([`inicio-rapido-v4.1.md`](inicio-rapido-v4.1.md)) |
| Contabilidad | Asiento de apertura, depósitos semanales, gastos del mes, pagos a proveedores y el asiento de cada reposición por garantía (**5.1.10 Costo de garantías**) | Contabilidad |

Al terminar, todos los puntos de venta quedan **en línea**, con el CUFD vigente, y nada queda pendiente de envío.

## 3. Publicar el programa

```powershell
powershell -ExecutionPolicy Bypass -File tools\publicar_escritorio.ps1
```

Deja **`dist\M-INV-4.2.0-alpha.1-win-x64\M-INV.exe`** (el de la V4.1 queda en su propia carpeta y no se toca). Necesita el
runtime de escritorio de .NET 8 o superior; con `-Autocontenido` lo incluye (no hace falta .NET en la estación).

## 4. Encender los servidores locales

```powershell
powershell -ExecutionPolicy Bypass -File tools\servidores_locales.ps1 -Accion iniciar
```

| Servidor | Dirección | Para qué |
|---|---|---|
| Simulador del SIN (`MINV.SiatSimulator`) | `http://localhost:5095` | responde como el SIN; conoce los CUIS, CUFD y facturas de la carga (`siat-simulador.json`) |
| Servidor en la nube (`MINV.CloudServer`) | `http://localhost:5080` | el escritorio en modo **Nube**; además envía las facturas y recupera los cortes solo |
| API Gateway (`MINV.ApiGateway`) | `http://localhost:5090` | la tienda en línea (documentación en `/docs`) |

`-Accion estado` dice si los tres responden; `-Accion detener` los apaga.

## 5. Abrir el escritorio (tres formas)

Abra **`dist\M-INV-4.2.0-alpha.1-win-x64\M-INV.exe`**. La primera vez se ve en el **tema oscuro** (gaming); el ícono de
luna o sol de la barra superior (o `Ctrl + Shift + L`) cambia al claro.

| Modo | Cómo | Cuándo |
|---|---|---|
| **Nube** | «Nube» → servidor `http://localhost:5080` → **Probar** (debe decir «disponible») → empresa **TECHZONE**, correo y contraseña | Como trabajaría una tienda real: el equipo de la caja no tiene la contraseña de la base; todo pasa por el servidor |
| **Base local** | «Base local» → empresa **TECHZONE**, correo y contraseña (la base debe estar encendida: `tools\bd_local.ps1 -Accion iniciar`) | Sin servidor en la nube. Las facturas las envía el propio escritorio al simulador del SIN: déjelo encendido con `servidores_locales.ps1` o saldrán fuera de línea |
| **Demostración** | «Explorar la demostración · sin instalar nada» → elija un rol | Sin base de datos ni servidores. Genera Tech Zone Gaming **en la memoria** del equipo (8 días de operación, con facturas del simulador en memoria) en unos 17 segundos; todo se pierde al cerrar. No hay contraseñas que copiar. Lo más completo está en la casa matriz (CM): Cochabamba y Santa Cruz recibieron una primera distribución de unos 90 productos con pocas unidades, así que parte de su catálogo aparece agotado |

Las contraseñas de **Nube** y **Base local** están en `%LOCALAPPDATA%\M-INV\usuarios-prueba.txt`
(`notepad $env:LOCALAPPDATA\M-INV\usuarios-prueba.txt`). Arriba, **Administrador**, **Gerencia** y **Consulta** eligen la
**sucursal activa**; los demás trabajan en la suya. En el menú aparece la sección nueva **Tecnología**: **Armador de PC**,
**Series e IMEI** y **Garantías y RMA**.

## 6. Qué probar con cada rol

### 6.1 Bodega · recibir una compra con series (Mariana Suárez, CM)

1. **Órdenes de compra**: elija una orden **aprobada** y pulse **Recibir**. Si no hay ninguna, cree una (por ejemplo, 3
   tarjetas de video y 2 routers 4G) y pida a **Gerencia** que la apruebe (o apruebe como Gerencia el borrador de
   reposición de tarjetas de video que dejó la carga).
2. Después del documento del proveedor se pide **una línea por producto serializado** con el contador «0 de 3». Las
   series se **escanean** (el lector en modo teclado termina cada una con Enter), se **escriben** o se **pegan como
   lista** («Pegar una lista»: una por renglón o separadas por comas).
3. Pruebe los avisos: un IMEI de 14 dígitos o con el dígito de control mal sale como **IMEI inválido**; una serie
   repetida se marca; si la serie ya existe en la empresa, el formulario sigue abierto con el motivo.
4. En **Series e IMEI** las unidades nuevas aparecen **en stock** con la fecha de ingreso y el proveedor.

Otras tareas de Bodega con series: **Registrar movimiento** (saldo inicial y ajustes positivos piden las series que entran;
las salidas, elegir las que salen), **Transferencias** (elegir o escanear las unidades que viajan; al recibir con faltante,
marcar las que no llegaron) y **Catálogo › Especificaciones / Ficha técnica**.

### 6.2 Cajero · vender una tarjeta de video con serie y factura (Miguel Ortiz, CM, CAJA01)

1. **Punto de venta** → abra la caja si lo pide.
2. Filtre con los chips de **categoría** (Tarjetas de video) o busque, por ejemplo, `GPU-MSI-4060-V2XB`. Al agregarla se
   abre **N° de serie de…**: escanee la unidad o márquela en la lista de **disponibles de la sucursal**. La caja no deja
   agregar una serie que no está en la sucursal.
3. En **Datos de facturación**: CI o NIT del comprador (o sin datos: 99003) → **Cobrar**.
4. En uno o dos segundos la factura sale **VÁLIDA**. El ticket muestra `S/N:` y «Garantía hasta dd/mm/aaaa» (fecha de la
   venta + meses del producto); en **Facturación › Documentos fiscales**, el XML de la factura lleva la serie en
   `numeroSerie`.

**Con IMEI:** repita con un router 4G (`RED-TPL-MR600` o `RED-TPL-M7450`, categoría Redes): el ticket dice `IMEI:` y la
factura lo lleva en `numeroImei` (si la sucursal se quedó sin unidades, recíbalas primero como en el §6.1). Las **consolas** (PS5, Xbox Series, Switch 2…) se venden igual, con su **número de
serie**: filtre con los chips de **plataforma**.

### 6.3 Ventas · armar una PC, cotizarla y venderla (Fernando Choque, CM)

1. **Armador de PC** → pestaña **Armar**. Elija una pieza por ranura: procesador, placa madre, memoria RAM, tarjeta de
   video, almacenamiento, fuente, gabinete y refrigeración (y extras: monitor, periféricos, software, servicios).
2. Los **candidatos** muestran su stock en la sucursal y sus especificaciones; los **incompatibles aparecen atenuados con
   el motivo** («El procesador es LGA1700 y la placa AM5: el socket no coincide»). La **revisión en vivo** calcula el
   consumo estimado y la fuente recomendada.
3. Ponga nombre, cliente y vigencia → **Guardar cotización** (congela los precios). Si fuerza una pieza incompatible, le
   pide confirmarlo y la cotización queda marcada.
4. **Proforma** → imprímala en la caja o guárdela en PDF («Documento sin valor fiscal»).
5. **Vender en caja** (o, en el Punto de venta, **Desde armado**): cada pieza sale a su precio cotizado, se eligen las
   series de las piezas serializadas y se cobra con factura. El armado queda **Vendido** con la venta vinculada.

En **Cotizaciones** están los 8 armados de la carga (vigentes, vendidos, el borrador incompatible y el cotizado con errores).

### 6.4 Garantía (RMA) · abrir, resolver y entregar (Cajero CM y Bodega CM)

1. **Cajero**: **Series e IMEI** → estado «vendida», sucursal CM → elija una unidad → **Abrir RMA** (o **Garantías y RMA ›
   Abrir caso** y escriba o escanee la serie). **Buscar** muestra el equipo, la venta y si está **en garantía**; describa la
   falla y abra el caso: la unidad queda **en garantía (RMA)**, fuera del stock vendible. Fuera de garantía solo se abre
   como **reparación con cargo**.
2. **Bodega** (Mariana Suárez): **Garantías y RMA** → el caso → **Pasar a diagnóstico** → **Reemplazar con otra unidad**
   (escanee o elija la serie que se entrega y pulse **Entregar reemplazo**: sale del stock con su asiento de Costo de
   garantías) → **Entregar al cliente**. También puede probar **Enviar al proveedor** → **Marcar reparado** → **Entregar
   al cliente**, o **Rechazar la garantía** con la resolución.
3. La unidad defectuosa que quedó en garantía: **Series e IMEI › Dar destino** → devolver al proveedor o dar de baja.
4. **Devolución por falla** (Administrador): **Ventas** → la venta → **Devolución** → marque las series → «Devolución por
   falla»: se reembolsa y se emite la nota crédito-débito, pero la unidad no vuelve al stock vendible.

### 6.5 Cualquier rol · rastrear una serie

**Series e IMEI** → escriba o escanee una serie, un IMEI o un SKU. El detalle muestra la **garantía vigente** (calculada al
consultar), la venta y la recepción y la **trazabilidad** como línea de tiempo: ingresó al stock, transferencias, vendida,
devuelta, en garantía, repuesta… con documento, sucursal y usuario de cada hecho. Pruebe con las series de los casos RMA
de la carga (Garantías y RMA › **Ver la serie**).

### 6.6 Cualquier rol · catálogo técnico y facetas

**Catálogo**: la galería muestra las insignias **Serie** o **IMEI**, **Garantía N meses** y las **plataformas**; la tarjeta
**Margen promedio** muestra el margen sobre el precio neto (unos 22 %; en Bolivia el neto es el 87 % del precio, porque el
IVA es el 13 % de lo facturado, y el costo es neto con la misma regla) y cuenta los de «margen bajo» (menos del 15 %:
ninguno en la empresa de prueba; filtro **Margen bajo (< 15 %)**). En la galería, el precio de cada tarjeta se ve completo
(«Bs 6.899,00»); si el SKU no cabe, se recorta con «…» y se lee entero al pasar el mouse. Elija una
categoría (por ejemplo, Procesadores o Placas madre): aparecen sus **especificaciones filtrables** con la cantidad por
valor (socket AM5, LGA1700, tipo de RAM…); los chips de **Plataforma** filtran consolas, juegos y accesorios. Con permiso
de fichas técnicas (Administrador, Gerencia, Bodega), el editor tiene la pestaña **Ficha técnica** y el botón
**Especificaciones** administra las de cada categoría.

### 6.7 Gerencia, Consulta y Administrador

- **Gerencia** (Luis Gutiérrez): **Inicio › Tecnología** (ventas por categoría y plataforma, tarjetas de video y consolas más
  vendidas, casos RMA y armados de los últimos 30 días), aprobar compras, transferencias, anular facturas, series y RMA en
  todas las sucursales. Arma y cotiza, pero no tiene caja: no cobra.
- **Consulta** (María Villarroel): solo lectura de stock, catálogo con fichas, series e IMEI, casos RMA, reportes y
  documentos fiscales.
- **Administrador** (Administrador General): todo, incluidas las devoluciones, la configuración de la facturación y los
  usuarios y roles.

### 6.8 Contabilidad · factura del proveedor, mermas y el inventario del mayor (Administrador)

En Bolivia el IVA está incluido en el precio y es el **13 % del importe facturado**: el crédito fiscal de una compra es el
13 % de la factura del proveedor y el costo contable es el **87 %**. Por eso las compras de la empresa de prueba entran al
**costo neto** (el 87 % del costo con IVA) y la factura del proveedor de una recepción es **recepción ÷ 0,87**.

1. **Facturación › Libros fiscales › Compras**: en **Recepciones sin factura** elija una y pulse **Registrar factura del
   proveedor**. El importe total ya viene propuesto (recepción ÷ 0,87) y el texto explica por qué; escriba el importe que
   dice la factura real si es otro.
2. El recuadro azul muestra la base y el crédito fiscal (13 %) y **el asiento que dejará**: con el importe propuesto,
   «crédito fiscal … al Debe de 1.1.04 · la deuda con el proveedor sube … · el inventario (1.1.05) no cambia». Si escribe
   otro importe, avisa cuánto sube o baja el inventario (el costo promedio no se recalcula con la factura).
3. **Registrar movimiento** con un **AJUSTE (−)** (una merma, con su observación) o una **toma física** con diferencias: el
   mensaje termina con «asiento AS-…». Las mermas y los faltantes van a **5.1.09 Mermas y ajustes de inventario**; los
   sobrantes, a **4.1.02 Sobrantes de inventario**, siempre al costo promedio del almacén.
4. **Contabilidad**: el saldo de **1.1.05 Inventario** de cada sucursal es el valor del stock (existencias × costo promedio;
   lo en tránsito está en 1.1.06 − 2.1.04). En la carga de 60 días la diferencia es **Bs 0,00** en las tres sucursales. La
   ENTRADA, la SALIDA y el SALDO INICIAL que se registran a mano no generan asiento (su contrapartida la registra el
   contador); la consulta de control está en el diseño (`edicion-tecnologia-v4.2.md` §12).

Gerencia también registra facturas de proveedores y ve la contabilidad; Bodega registra mermas y tomas físicas.

### 6.9 La tienda en línea: ficha técnica por el API

Con la API Key «Tienda en línea» de `claves-integracion.txt` (en PowerShell use `curl.exe`):

```powershell
$key = '<pegue aquí la API Key de claves-integracion.txt>'     # empieza con minv_
curl.exe -s "http://localhost:5090/v1/products/GPU-MSI-4060-V2XB/specs" -H "Authorization: Bearer $key"
```

Devuelve la ficha técnica, si lleva serie o IMEI, los meses de garantía y cuántas unidades con serie hay en stock. Los
pedidos (`POST /v1/orders`) aceptan `serials` por línea. Guía completa: [`docs/integration/api-gateway-v1.md`](../integration/api-gateway-v1.md).

## 7. Usuarios de prueba

Empresa **TECHZONE** · Tech Zone Gaming S.R.L. Las **contraseñas** no están aquí: están en
`%LOCALAPPDATA%\M-INV\usuarios-prueba.txt` y cambian cada vez que se recrea la base.

| Rol | Nombre | Correo | Sucursales |
|---|---|---|---|
| Administrador | Administrador General | admin@techzone.example | Todas |
| Gerencia | Luis Gutiérrez | luis.gutierrez@techzone.example | Todas (gerencia global) |
| Bodega | Mariana Suárez | mariana.suarez@techzone.example | CM |
| Bodega | Sergio Mamani | sergio.mamani@techzone.example | CB |
| Bodega | Daniela Céspedes | daniela.cespedes@techzone.example | SC |
| Ventas | Fernando Choque | fernando.choque@techzone.example | CM |
| Ventas | Sergio Rojas | sergio.rojas@techzone.example | SC |
| Cajero | Miguel Ortiz | miguel.ortiz@techzone.example | CM |
| Cajero | Luis Flores | luis.flores@techzone.example | CM |
| Cajero | Camila Fernández | camila.fernandez@techzone.example | CB |
| Cajero | Camila Morales | camila.morales@techzone.example | SC |
| Consulta | María Villarroel | maria.villarroel@techzone.example | CM, CB, SC |

Los nombres salen de la semilla fija de la carga (siempre los mismos); las contraseñas, del azar. En la **demostración**
se entra por rol, sin contraseña: Administrador General, Luis Gutiérrez (Gerencia), Mariana Suárez (Bodega CM), Fernando
Choque (Ventas CM), Miguel Ortiz (Cajero CM) y María Villarroel (Consulta).

## 8. Qué queda en `%LOCALAPPDATA%\M-INV` (nunca en el repositorio)

| Archivo o carpeta | Qué tiene |
|---|---|
| `usuarios-prueba.txt` | empresa, resumen de la carga (tecnología y facturación) y, por usuario: rol, nombre, correo, **contraseña** y sucursales |
| `credenciales-bd-local.txt` | contraseñas de los roles de PostgreSQL (`postgres`, `minv_owner`, `minv_app`, `minv_server`) |
| `claves-integracion.txt` | clave maestra de integraciones de este equipo, API Key de la tienda, secreto del webhook de prueba y **token de simulación** del SIN (`MINV_SIAT_TOKEN`) |
| `siat-simulador.json` · `siat-simulador.anterior.json` | lo que «sabe» el simulador del SIN (CUIS, CUFD, facturas, eventos y paquetes) y el de la carga anterior |
| `cliente.json` | preferencias del escritorio: tema, servidor, empresa y correo recordados, impresora (sin contraseñas) |
| `postgresql-16\` · `pgdata\` · `postgresql.log` | PostgreSQL portátil, sus datos y su registro |
| `servidor-nube.log` · `api-gateway.log` · `simulador-sin.log` · `servidores.pid` | registros y procesos de los servidores locales |

Las contraseñas, las claves y el token son **aleatorios**, cambian cada vez que se recrea la base y nunca se escriben en
la documentación. El token de simulación solo lo acepta el simulador de este equipo: **nunca** lo use con el SIN real.

## 9. Si algo no funciona

| Síntoma | Solución |
|---|---|
| «La empresa TECHZONE no existe» o el programa pide la empresa MINV | la base todavía es la de la V4.1: recree la base desde la rama `Inventario-V4.2` (§2) |
| Los servidores arrancan con la versión anterior o el escritorio dice «actualice el escritorio» | detenga los servidores, confirme la rama (`git branch --show-current` debe decir `Inventario-V4.2`), vuelva a iniciarlos y publique el escritorio de la V4.2 |
| «Sin conexión con la base de datos» | `tools\bd_local.ps1 -Accion iniciar` y pulse «Reintentar» |
| La caja no deja agregar un equipo: «la serie no está disponible» o «es de otra sucursal» | la unidad ya se vendió, está en tránsito, en garantía o en otra sucursal: elija otra de la lista de disponibles de la sucursal activa (o búsquela en Series e IMEI) |
| «Faltan series» o «sobran series» al recibir o registrar | un producto serializado lleva exactamente una serie por unidad y la cantidad debe ser entera: complete el contador («3 de 3») |
| «IMEI inválido» | un IMEI tiene 15 dígitos y el último es el dígito de control (Luhn); revise que no falte ni sobre un número. Espacios y guiones se aceptan y se quitan |
| «La serie ya existe» al recibir | las series son únicas por producto en toda la empresa; si la unidad vuelve (reparada o del proveedor), regístrela con un ajuste positivo o en la recepción y M-INV la **reingresa** |
| La toma física rechaza un conteo de un producto con serie | la diferencia de un serializado se registra con un ajuste que lleva sus series (Registrar movimiento), no por conteo |
| No se puede abrir el RMA: «fuera de garantía» | la garantía venció (fecha de la venta + meses): márquelo como **reparación con cargo** |
| «La serie tiene un caso abierto» | una serie solo puede tener un caso RMA abierto: termine (entregue) el anterior |
| El armador no deja guardar la cotización | tiene errores de compatibilidad: corríjalos o confírmelos expresamente (queda marcado) |
| «Vender en caja» no está o dice «vencida» | la cotización no está vigente: guárdela de nuevo (precios y vigencia nuevos) |
| La devolución responde «Su rol no tiene el permiso …» | una devolución exige `sales.pos.operate` y `billing.void`: con los roles predeterminados la hace el Administrador (se puede ampliar en Usuarios y roles) |
| El tablero avisa que productos serializados «no tienen todas sus unidades con serie» | registre las series que faltan en **Series e IMEI › Registrar series de stock** |
| En la demostración, Cochabamba o Santa Cruz muestran unos 60 a 70 productos **agotados** | es lo esperado: para abrir rápido, la demostración (8 días) solo hace la primera distribución de la casa matriz (unos 90 productos con pocas unidades por sucursal) y pocas reposiciones. La base local (60 días) abastece las tres sucursales |
| Facturas «fuera de línea» que no pasan a válidas | ¿está encendido el simulador? `tools\servidores_locales.ps1 -Accion estado`; más casos en [`inicio-rapido-v4.1.md`](inicio-rapido-v4.1.md) |

## 10. Volver a la V4.1 (la ferretería de ejemplo)

La V4.1 y las versiones anteriores siguen en sus ramas, sin cambios, con la empresa **MINV · Ferretería El Constructor**.
Para volver: detenga los servidores, `git switch Inventario-V4.1` y recree la base desde esa rama
(`tools\bd_local.ps1 -Accion recrear`). Su programa sigue en `dist\M-INV-4.1.0-alpha.1-win-x64\M-INV.exe`.
