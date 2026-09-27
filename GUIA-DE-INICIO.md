# Guía de inicio de M-INV · las cinco ediciones, paso a paso

M-INV es el sistema de inventarios de **Z&P Software Fast Solutions**. Nació como un libro de Excel y hoy es una
aplicación de escritorio con base de datos en la nube, facturación del SIN y, en su quinta edición, especializada en
**tiendas de tecnología y gaming**. Las cinco ediciones siguen en el repositorio y funcionan con la **misma lógica de
negocio**:

- El stock solo cambia registrando **movimientos** (entradas, salidas y ajustes).
- Nada se borra: los errores se corrigen con un **ajuste**.
- Todo queda **auditado**.

Esta guía explica, para cada edición:

1. **qué es y para quién**,
2. el **algoritmo**: los pasos, en orden, para ponerla en marcha,
3. **qué funciones** tiene y **qué puede hacer cada rol**.

| # | Edición | Versión y rama | Para quién | Qué necesita |
|---|---|---|---|---|
| 1 | **Excel local** | V1.2 · `Inventario-V1.2` | Una persona o un equipo pequeño en una sola PC | Excel 2016 o superior en Windows |
| 2 | **Excel compartido** | V2.1 · `Inventario-V2.1` | Varias personas a la vez, cada una con su rol | Microsoft 365 de trabajo, SharePoint u OneDrive, Office Scripts |
| 3 | **App de escritorio con base de datos local** | V3.1 · `Inventario-V3.-BaseDeDatosLocal` | Empresa con una sede, cajas y punto de venta | Windows 10/11, .NET 8 o superior, PostgreSQL (se instala solo) |
| 4 | **App de escritorio con base de datos en la nube y facturación** | V4 · `Inventario-V4.-BaseDeDatosNube` y **V4.1 · `Inventario-V4.1`** | Empresa con sucursales, tienda en línea y facturación del SIN | Lo de la 3, más un servidor en la nube (se prueba entero en este equipo) |
| 5 | **Tecnología: PC, componentes, consolas y videojuegos** | **V4.2 · `Inventario-V4.2`** | Tiendas de computadoras, componentes, periféricos, consolas y videojuegos, con sucursales | Lo de la 4 (se prueba entero en este equipo) |
| 6 | **Catálogo Web M-INV** | **V5.0 · `Inventario-V5`** | Clientes finales que navegan el catálogo, arman su PC y ven ofertas | Lo de la 5, más la interfaz web responsiva |

> **Una edición a la vez.** Cada edición vive en su **rama** de Git. Para usar una, se cambia a su rama
> (`git switch <rama>`) y se siguen sus pasos. Quédese en una edición hasta que decida pasar a otra. La **base de datos
> local es una sola por equipo**: al recrearla desde otra rama, se reemplaza por los datos de prueba de esa edición.

> **Contraseñas.** Nunca están en el repositorio ni en esta guía. Las contraseñas de la base local y de los usuarios de
> prueba se generan al azar en cada carga y quedan solo en este equipo, en la carpeta `%LOCALAPPDATA%\M-INV\`:
>
> - `usuarios-prueba.txt`: los usuarios de prueba;
> - `credenciales-bd-local.txt`: las contraseñas de PostgreSQL;
> - `claves-integracion.txt`: las claves de integración y el token de simulación del SIN.
>
> Para verlos: `notepad $env:LOCALAPPDATA\M-INV\usuarios-prueba.txt`.

Todas las órdenes de esta guía se escriben en **PowerShell**, dentro de la carpeta del repositorio (en el Explorador de
Windows: clic derecho sobre la carpeta › «Abrir en Terminal»).

---

## 1. Edición Excel local (V1.2)

Un libro de Excel que funciona como una aplicación. Tiene dos presentaciones:

- **Estándar** (`.xlsx`): sin macros.
- **Plus** (`.xlsm`): con un formulario de registro y automatizaciones en VBA. Sin macros se comporta igual que la
  Estándar.

### Archivos

| Archivo | Para qué |
|---|---|
| `src/M-INV_V1_Core.xlsx` / `.xlsm` | Libro de **demostración** con datos de ejemplo |
| `releases/M-INV_V1_Produccion_Bloqueado.xlsx` / `.xlsm` | Libro **limpio y protegido** para la empresa |

(Los libros están en la rama `Inventario-V1.2` y también en las ramas posteriores.)

### Algoritmo

```text
 1. Copie el libro a su PC: releases\M-INV_V1_Produccion_Bloqueado.xlsx (o el .xlsm si quiere el formulario).
 2. Ábralo con Excel. Si es el .xlsm, pulse «Habilitar contenido».
 3. Portada (00_PORTADA) → siga el recuadro «Próximo paso»:
      a. Cargue los proveedores (04_PROVEEDORES) y los productos (05_PRODUCTOS) en las celdas marcadas con ✎.
      b. Saldo inicial: cuente todo en 13_CONTEO. Los ajustes se generan con el botón (Plus) o se registran en la
         bitácora (Estándar).
 4. Uso diario: registre cada entrada, salida o ajuste en 12_REGISTRO (Plus) o en la fila libre de 10_MOVIMIENTOS.
 5. Mire 15_STOCK, 16_ALERTAS y 18_PEDIDO: se calculan solos.
 6. Para generar el libro desde el código:
      powershell -ExecutionPolicy Bypass -File tools\build_all.ps1 -Capturas
```

### Funciones

| Hoja | Qué hace |
|---|---|
| `00_PORTADA` | Tablero: indicadores, próximo paso y navegación |
| `04_PROVEEDORES` · `05_PRODUCTOS` | Maestros: proveedores con días de entrega; productos con categoría, unidad, mínimo y máximo |
| `10_MOVIMIENTOS` | Bitácora inmutable. Cada fila se valida (✔ correcto, ⚠ revisar, ✖ error) |
| `12_REGISTRO` (Plus) | Formulario guiado con búsqueda de producto y control de stock negativo; sella las filas registradas |
| `13_CONTEO` | Toma física: las diferencias se convierten en ajustes |
| `15_STOCK` · `16_ALERTAS` | Stock por producto y alertas priorizadas con semáforo |
| `17_KARDEX` | Ficha de un producto: historial y gráfico |
| `18_PEDIDO` | Pedido sugerido agrupado por proveedor |
| `99_AYUDA` | Guía por perfil y primeros pasos |

**Roles.** No hay usuarios con contraseña. La protección de las hojas evita errores; el responsable de cada
movimiento se elige de una lista.

---

## 2. Edición Excel compartido (V2.1, Microsoft 365)

El libro se abre por internet en Excel para la web y **varias personas trabajan a la vez sin pisarse**:

- cada persona escribe en **su propia fila**;
- un **Office Script** consolida su registro en la bitácora oficial;
- los scripts verifican el **correo de Microsoft 365** y el rol de cada persona.

### Archivos

| Archivo | Para qué |
|---|---|
| `src/M-INV_V2_Colaborativo.xlsx` | Libro de **demostración** |
| `releases/M-INV_V2_Colaborativo_Produccion.xlsx` | Libro **limpio y protegido** para la empresa |
| `src/office-scripts/` | Los 6 scripts |

(Rama `Inventario-V2.1`; los libros y los scripts también están en las ramas posteriores.)

### Algoritmo

La guía detallada es [`docs/deployment/inicio-rapido.md`](docs/deployment/inicio-rapido.md).

```text
 A. Solo mirar (5 min):  abra src\M-INV_V2_Colaborativo.xlsx y recorra las portadas (Bodega, Ventas, Gerencia).
 B. Probar con su cuenta (30 min):
      1. Genere los scripts de la demo:  python tools\office_scripts.py deploy --edicion core  (quedan en build\office-scripts\core)
      2. Suba el libro a su OneDrive o SharePoint y ábralo en Excel para la web.
      3. Regístrese como ADMIN en 02_USUARIOS (Revisar › Desproteger hoja, con la contraseña de desarrollo de la demo
         que indica la guía detallada): su correo de Microsoft 365, su nombre, ADMIN y SI; vuelva a proteger la hoja.
      4. Automatizar › Nuevo script: pegue los 6 scripts y ejecute DiagnosticoInstalacion (debe quedar sin ✖).
      5. Agregue los botones sobre los recuadros amarillos ⚙.
 C. Empresa (1-2 h):
      1. Genere el Release con SU contraseña:  $env:MINV_RELEASE_PASSWORD='…'; tools\build_v2.ps1
      2. Súbalo a una biblioteca de SharePoint con permisos por grupo.
      3. Registre a cada persona en 02_USUARIOS con su correo y su rol.
      4. Instale los scripts y los botones. Haga el saldo inicial con una toma física en 13_CONTEO.
 D. Cada día: abra el enlace → portada de su rol → escriba en SU fila → pulse el botón → lea «Resultado».
```

### Funciones por rol

| Rol | Qué puede hacer |
|---|---|
| **Bodega** | Registrar entradas, saldo inicial y ajustes (10A); toma física colaborativa (13_CONTEO); ver alertas y pedido |
| **Ventas** | Registrar salidas (10B). La cantidad se pone roja y tachada si no hay stock. Consultar un producto en su fila de 17_CONSULTA |
| **Gerencia / ADMIN** | Recalcular stock; indicadores, alertas, pedido sugerido y 10 más vendidos; auditoría en 14_ACTIVIDAD; usuarios en 02_USUARIOS |
| **Consulta** | Solo lectura: portada de gerencia, stock, alertas, pedido y actividad |

**Extras:**

- **Resumen diario por correo** con Power Automate (script `ResumenDiario`).
- **Registro de actividad**: cada ejecución de un script queda registrada, también los intentos bloqueados.

---

## 3. Edición app de escritorio con base de datos local (V3.1)

Programa de Windows (`M-INV.exe`) con base de datos **PostgreSQL** instalada en el mismo equipo. Incluye:

- punto de venta, impresora de tickets y lector de códigos de barras;
- contabilidad y compras;
- usuarios con contraseña.

También tiene un **modo demostración** que no necesita base de datos.

### Algoritmo

La guía detallada es [`docs/deployment/inicio-rapido-v3.md`](docs/deployment/inicio-rapido-v3.md).

```text
 0. Cambie a la rama de la edición:  git switch Inventario-V3.-BaseDeDatosLocal
 A. Requisitos: Windows 10/11 y el SDK de .NET 8 o superior (con el 10 funciona). No hace falta ser administrador.
 B. Base de datos local con datos de prueba (una sola vez, unos minutos):
        powershell -ExecutionPolicy Bypass -File tools\bd_local.ps1 -Accion instalar -Zip <postgresql-16.x-windows-x64-binaries.zip>
    Si ya está instalada y quiere datos nuevos (cambian las contraseñas):
        powershell -ExecutionPolicy Bypass -File tools\bd_local.ps1 -Accion recrear
 C. Publique el programa:  powershell -ExecutionPolicy Bypass -File tools\publicar_escritorio.ps1
 D. Abra dist\M-INV-<versión>-win-x64\M-INV.exe
      → «Base local» → empresa MINV → correo y contraseña de %LOCALAPPDATA%\M-INV\usuarios-prueba.txt
 E. Cada vez que encienda el equipo:  tools\bd_local.ps1 -Accion iniciar   (o una vez: -Accion iniciar -Autoiniciar)
 Sin base de datos: abra M-INV.exe → «Explorar la demostración» → elija un rol.
```

### Funciones (menú del escritorio)

| Área | Pantallas y qué hacen |
|---|---|
| **General** | **Inicio**: tablero con indicadores, gráfico de entradas y salidas, estado del inventario, alertas, más vendidos y últimos movimientos |
| **Ventas** | **Punto de venta**: abrir y cerrar caja, cobrar en efectivo, QR, tarjeta o transferencia, imprimir el ticket, arqueo. **Ventas**: historial y anulación con devolución del stock. **Clientes**: altas y cambios |
| **Inventario** | **Stock**: galería con imágenes y semáforo. **Catálogo**: productos, variantes, códigos de barras, precios e imágenes. **Registrar movimiento**: entradas, salidas y ajustes; no deja vender más de lo que hay. **Toma física**: conteo y ajustes. **Ficha de producto** con kardex |
| **Compras y reposición** | **Alertas** · **Pedido sugerido** por proveedor · **Órdenes de compra**: aprobación y recepción · **Proveedores** |
| **Análisis** | **Reportes**: ventas, compras, inventario y rentabilidad. **Contabilidad**: asientos automáticos, libro diario y costos |
| **Administración** | **Usuarios y roles** · **Actividad**: auditoría · **Configuración**: tema claro u oscuro, impresora y lector · **Ayuda** |

---

## 4. Edición app de escritorio con base de datos en la nube y facturación (V4 · V4.1)

El mismo programa trabaja con **varias sucursales**, cada una aislada con sus cajas, stock y documentos. Se conecta a
un **servidor en la nube**: el equipo de la caja nunca tiene la contraseña de la base. La edición suma:

- **transferencias** entre sucursales con mercadería en tránsito;
- un **API** para la tienda en línea y el ERP;
- en la **V4.1**, la **facturación del SIN (SIAT)** en la modalidad *Facturación Computarizada en Línea*.

Todo se prueba en este equipo **sin contratar nada**: el servidor en la nube, el API Gateway y un **simulador del
SIN** corren en su PC.

> **Estado de la V4.1: completa** (más de 460 pruebas automáticas, incluidas las de PostgreSQL). Emisión al cobrar y
> envío al SIN, contingencia fuera de línea con recuperación automática, facturas manuales CAFC, anulación, reversión,
> notas crédito-débito, libros de compras y ventas, y las cinco pantallas de facturación del escritorio. Lo único que
> falta es propio del SIN real (token, NIT y confirmar el contrato con el WSDL del piloto): ver
> [`docs/billing/puesta-en-produccion-siat.md`](docs/billing/puesta-en-produccion-siat.md).

### Algoritmo · todo en este equipo

Guías detalladas: [`docs/deployment/inicio-rapido-v4.1.md`](docs/deployment/inicio-rapido-v4.1.md) (facturación, paso a
paso) y [`docs/deployment/inicio-rapido-v4.md`](docs/deployment/inicio-rapido-v4.md) (sucursales, nube y API).

```text
 0. Cambie a la rama de la edición:  git switch Inventario-V4.1     (la V4 sin facturación: Inventario-V4.-BaseDeDatosNube)
 A. Requisitos: los de la edición 3 (PostgreSQL portátil instalado con tools\bd_local.ps1 -Accion instalar).
 B. Base local con 3 sucursales, 12 usuarios, transferencias, pedidos web y 25 días de FACTURAS del SIN simulado:
        powershell -ExecutionPolicy Bypass -File tools\bd_local.ps1 -Accion recrear
    (sin facturación: agregue -SinFacturacion). Una base existente de la V4 se actualiza sin perder datos:
        dotnet run --project "src/4. Tools/MINV.Cli" -- migrate --conexion "<cadena del rol minv_owner>"
 C. Publique el programa:  powershell -ExecutionPolicy Bypass -File tools\publicar_escritorio.ps1
 D. Encienda la «nube» local: simulador del SIN :5095 + servidor en la nube :5080 + API Gateway :5090
        powershell -ExecutionPolicy Bypass -File tools\servidores_locales.ps1 -Accion iniciar
 E. Abra dist\M-INV-4.1.0-alpha.1-win-x64\M-INV.exe
      → «Nube» → servidor http://localhost:5080 → «Probar» → empresa MINV → correo y contraseña
      («Base local» también funciona, sin servidor; «Demostración» factura en memoria, sin base)
 F. Arriba elija la SUCURSAL ACTIVA: todo lo que registre queda en esa sucursal.
 G. Facturación › Estado SIAT: 8 puntos de venta EN LÍNEA con el CUFD del día.
 H. Punto de venta: CI o NIT del comprador → Cobrar → la factura sale VÁLIDA, con QR y «SIN VALOR LEGAL» (pruebas).
 I. Corte de internet simulado:  dotnet run --project "src/4. Tools/MINV.Cli" -- siat simulador-apagar
      → la caja sigue facturando FUERA DE LÍNEA →  … siat simulador-encender  → en 1 o 2 minutos todo VÁLIDO.
 J. Tienda en línea: API en http://localhost:5090 (documentación en /docs), con la API Key de claves-integracion.txt;
      cada pedido web también se factura.
 K. Al terminar:  tools\servidores_locales.ps1 -Accion detener   (estado: -Accion estado)
 L. Nube real (DigitalOcean, AWS RDS o Supabase):  docs\deployment\despliegue-nube-v4.md
    SIN real (Fases I a III y producción):        docs\billing\puesta-en-produccion-siat.md
```

### Funciones que se suman a la edición 3

| Área | Qué hace |
|---|---|
| **Sucursales** | Tablero por sucursal: ventas, stock valorizado y lo que está en tránsito. Stock consolidado. Alta de sucursales y asignación de usuarios |
| **Transferencias** | La sucursal de origen solicita y despacha; la de destino recibe, contando lo que llegó. Los faltantes quedan registrados como merma en tránsito, con sus asientos contables |
| **Integraciones** | API Keys con permisos por sucursal. Webhooks firmados para avisar de ventas, anulaciones y transferencias. Historial de entregas |
| **Modo Nube** | Sesión con token. Cada acción se verifica en el servidor con los permisos y las sucursales del usuario |
| **Facturación SIAT (V4.1)** | Pantallas **Documentos fiscales**, **Estado SIAT**, **Homologación**, **Libros fiscales** y **Facturación SIAT** (configuración). Facturas Compra Venta y notas crédito-débito, con CUF, QR, leyendas de la Ley 453 y PDF o rollo. Envío al SIN al cobrar. **Contingencia automática**: sin internet la caja no se bloquea; factura fuera de línea y, al volver la conexión, registra el evento y envía los paquetes solo. Anulación hasta el día 9 del mes siguiente; reversión una sola vez. Devoluciones parciales con nota crédito-débito. Facturas manuales CAFC. Homologación de productos. Verificación de NIT. Libros de ventas y compras y resumen de IVA e IT |

### Pasos para facturar con el SIN (V4.1)

```text
 1. Configuración › Facturación SIAT (Administrador), una sola vez:
      · NIT y razón social del Padrón;
      · código de sistema;
      · ambiente de pruebas;
      · URL del SIN (o del simulador local http://localhost:5095);
      · token delegado del Portal SIAT;
      · código del Padrón de cada sucursal (0 = casa matriz).
 2. Estado SIAT › Registrar punto de venta por cada caja → Preparar SIAT (CUIS, CUFD, hora y catálogos).
 3. Homologación: cada producto, unidad y medio de pago con su código del SIN. «Sugerir» propone los productos.
 4. En la caja: datos de facturación del comprador (CI o NIT; «Verificar NIT») → Cobrar → se imprime la factura con QR.
 5. Cada día: M-INV renueva el CUFD y sincroniza los catálogos solo, y revisa las alertas de Estado SIAT.
 6. Paso a producción con el SIN real: registro del sistema en el Portal SIAT, pruebas de la Fase I, inspección de la
    Fase II, piloto de la Fase III y token de producción. Guía paso a paso:
    docs\billing\puesta-en-produccion-siat.md (normativa: docs\billing\investigacion-siat\08-autorizacion-inspeccion-versionamiento.md).
```

### Funciones por rol (V4 · V4.1)

| Rol | Qué puede hacer |
|---|---|
| **Administrador** | Todo: catálogo, usuarios y roles, movimientos, compras, caja, contabilidad, auditoría, sucursales, transferencias, integraciones y toda la facturación, incluida la configuración del SIAT y las devoluciones con nota crédito-débito |
| **Gerencia** | Todas las sucursales. Stock, reportes, ventas, compras (aprobar), contabilidad, auditoría y transferencias. Facturación: consultar, anular, revertir y contingencia |
| **Bodega** | Su sucursal. Entradas, saldo inicial y ajustes; toma física; compras y recepciones; reportes; transferencias (despachar y recibir) |
| **Ventas** | Su sucursal. Caja y salidas, clientes, historial de ventas, reportes. Facturación: consultar y emitir |
| **Cajero** | Su sucursal. Caja (abrir, cobrar y cerrar), salidas, clientes e historial de ventas. Facturación: consultar y emitir |
| **Consulta** | Solo lectura: stock, reportes y, en la V4.1, los documentos fiscales y los libros |

---

## 5. Edición Tecnología: PC, componentes, consolas y videojuegos (V4.2 · `Inventario-V4.2`)

La edición 4 **especializada para tiendas de tecnología y gaming**: componentes de PC (procesadores, tarjetas de video,
placas, memorias, almacenamiento, fuentes, gabinetes, refrigeración), computadoras, monitores, periféricos, consolas (PS4
y PS5, Xbox Series X y Series S, Nintendo Switch y Switch 2), videojuegos, accesorios, redes, cables y software. Todo lo
de la edición 4 sigue igual (sucursales, nube, API y facturación del SIN) y se suma:

- cada equipo se recibe, se vende, viaja entre sucursales y vuelve por garantía con su **número de serie o IMEI**, y la
  **factura del SIN lo lleva**;
- **fichas técnicas** (socket, VRAM, tipo de RAM, Hz, plataforma, condición…) con filtros por especificación;
- un **armador de PC** que revisa la compatibilidad de las piezas y deja una **cotización** que se cobra en la caja;
- **garantías y RMA**: recibir el equipo, diagnosticar, enviarlo al proveedor, reparar o **reemplazar con otra unidad** y
  entregar;
- un **tablero Tecnología** y un **tema gaming** (oscuro por defecto, con acentos violeta y cian).

Se prueba entero en este equipo con la empresa **Tech Zone Gaming S.R.L.** (código `TECHZONE`, sucursales **CM** La Paz,
**CB** Cochabamba y **SC** Santa Cruz) y el simulador del SIN. Estado: **4.2.0-alpha.1, en desarrollo**.

### Archivos

| Archivo o carpeta | Para qué |
|---|---|
| `dist\M-INV-4.2.0-alpha.1-win-x64\M-INV.exe` | El programa de la edición (lo crea el paso 6 del algoritmo) |
| `tools\bd_local.ps1` | Instala o recrea la base local con Tech Zone Gaming (también `iniciar`, `detener`, `estado`) |
| `tools\servidores_locales.ps1` | Enciende o apaga la «nube» de este equipo: simulador del SIN, servidor en la nube y API Gateway |
| `tools\publicar_escritorio.ps1` | Publica `M-INV.exe` en `dist\` |
| [`docs/deployment/inicio-rapido-v4.2.md`](docs/deployment/inicio-rapido-v4.2.md) | La guía detallada: escenarios cargados, qué probar con cada rol, archivos locales y problemas frecuentes |
| [`docs/product/escritorio-v4.2.md`](docs/product/escritorio-v4.2.md) | Cada pantalla nueva, explicada |
| [`docs/architecture/edicion-tecnologia-v4.2.md`](docs/architecture/edicion-tecnologia-v4.2.md) | El diseño (para técnicos) |
| `%LOCALAPPDATA%\M-INV\usuarios-prueba.txt` | Los correos y contraseñas de prueba (solo en este equipo) |

### Algoritmo · todo en este equipo

```text
 PRIMERA VEZ (una sola vez)
  1. Abra PowerShell en la carpeta del repositorio.
  2. Cambie a la edición 5:        git switch Inventario-V4.2
  3. Si tenía encendidos los servidores de otra edición, apáguelos:
        powershell -ExecutionPolicy Bypass -File tools\servidores_locales.ps1 -Accion detener
  4. ¿Nunca instaló PostgreSQL en este equipo? Hágalo una vez (edición 3, paso B, con -Accion instalar). Si ya lo tiene, siga.
  5. Recree la base con la tienda de prueba Tech Zone Gaming (BORRA la base anterior de este equipo; tarda unos minutos):
        powershell -ExecutionPolicy Bypass -File tools\bd_local.ps1 -Accion recrear
     Al final debe decir «RESULTADO: base de datos correcta».
  6. Publique el programa:
        powershell -ExecutionPolicy Bypass -File tools\publicar_escritorio.ps1
     Queda en dist\M-INV-4.2.0-alpha.1-win-x64\M-INV.exe

 CADA VEZ QUE LO USE
  7. Si reinició el equipo, encienda la base:   powershell -ExecutionPolicy Bypass -File tools\bd_local.ps1 -Accion iniciar
  8. Encienda la «nube» de este equipo (simulador del SIN :5095, servidor en la nube :5080 y API :5090):
        powershell -ExecutionPolicy Bypass -File tools\servidores_locales.ps1 -Accion iniciar
  9. Abra dist\M-INV-4.2.0-alpha.1-win-x64\M-INV.exe y elija UNA de estas tres formas:
       a. Nube:          «Nube» → servidor http://localhost:5080 → «Probar» (debe decir «disponible»)
                         → empresa TECHZONE → correo y contraseña
       b. Base local:    «Base local» → empresa TECHZONE → correo y contraseña
                         (no usa el servidor en la nube; para que las facturas salgan VÁLIDAS, deje encendido el paso 8)
       c. Demostración:  «Explorar la demostración» → elija un rol
                         (no necesita la base ni los servidores: solo el paso 6; se prepara en unos 17 segundos y todo
                          se pierde al cerrar el programa)
     Correos y contraseñas:   notepad $env:LOCALAPPDATA\M-INV\usuarios-prueba.txt
 10. Arriba, elija la SUCURSAL ACTIVA (CM, CB o SC) si su rol ve varias: lo que registre queda en esa sucursal.
 11. Pruebe lo nuevo (lista de abajo) con el rol que corresponda.
 12. Al terminar:  powershell -ExecutionPolicy Bypass -File tools\servidores_locales.ps1 -Accion detener

 QUÉ PROBAR (detalle en docs\deployment\inicio-rapido-v4.2.md, sección 6)
  · Bodega CM:   Órdenes de compra › Recibir → escanee, escriba o pegue las series (el IMEI inválido y las repetidas se marcan).
  · Cajero CM:   Punto de venta → una tarjeta de video → elija su serie → CI o NIT → Cobrar → factura VÁLIDA con la serie.
  · Cajero CM:   un router 4G (lleva IMEI) → el ticket dice «IMEI:» y la factura lo lleva. Las consolas llevan número de serie.
  · Ventas CM:   Armador de PC → arme una PC (lo incompatible se ve atenuado con el motivo) → Guardar cotización →
                 Proforma → Vender en caja.
  · Cajero CM:   Series e IMEI → una unidad vendida → Abrir RMA (el equipo queda en garantía).
  · Bodega CM:   Garantías y RMA → Pasar a diagnóstico → Reemplazar con otra unidad → Entregar al cliente;
                 la unidad defectuosa: Series e IMEI › Dar destino → devolver al proveedor.
  · Todos:       Series e IMEI → busque una serie → su historia completa (compra, transferencias, venta, garantía).
  · Todos:       Catálogo → una categoría → filtros por especificación (socket, tipo de RAM…) y por plataforma (PS5, Switch 2…).
  · Gerencia:    Inicio › Tecnología (ventas por categoría y plataforma, tarjetas de video y consolas más vendidas, RMA y armados).
```

### Funciones que se suman a la edición 4

| Área | Qué hace |
|---|---|
| **Tecnología › Armador de PC** | Ranuras (procesador, placa, RAM, tarjeta de video, almacenamiento, fuente, gabinete, refrigeración y extras). Muestra las piezas con su stock en la sucursal; las incompatibles aparecen atenuadas con el motivo. Consumo estimado y fuente recomendada. Cotización con precios congelados y vigencia, proforma (impresa o en PDF) y cobro en la caja. Lista de cotizaciones: borrador, cotizado, vencido, vendido y anulado |
| **Tecnología › Series e IMEI** | Buscar cualquier serie, IMEI o SKU (también con el lector). Dónde está cada unidad, a quién se vendió, su garantía vigente y su historia completa. Registrar las series de unidades que ya estaban en stock. Dar destino a una unidad devuelta o en garantía (al proveedor o de baja) |
| **Tecnología › Garantías y RMA** | Abrir un caso con la serie (en garantía o como reparación con cargo), diagnóstico, envío al proveedor, reparado, reemplazo con otra unidad (sale del stock con su asiento), rechazo con la resolución y entrega; bitácora de cada caso |
| **Catálogo técnico** | Pestaña **Ficha técnica** (lleva serie o IMEI, meses de garantía y especificaciones), insignias Serie / IMEI / Garantía y plataformas en la galería, filtros por plataforma y por especificación, administración de **Especificaciones** por categoría y subcategorías |
| **Punto de venta** | Chips de categoría y de plataforma; al agregar un equipo se elige o escanea su unidad; el ticket y la factura llevan la serie o el IMEI y «Garantía hasta…»; **Desde armado** cobra una cotización |
| **Compras, movimientos, transferencias y devoluciones** | Piden las series de los productos serializados (escáner, escritas o pegadas como lista); la **devolución por falla** reembolsa sin devolver la unidad al stock vendible |
| **Inicio › Tecnología** | Unidades con serie en stock, casos RMA abiertos, armados cotizados y vendidos, ventas por categoría y por plataforma, tarjetas de video y consolas más vendidas |
| **Facturación** | Igual que la V4.1; la factura lleva `numeroSerie` o `numeroImei` en cada línea de un equipo serializado |
| **Tema gaming** | Oscuro por defecto; el claro, a un clic (ícono de luna o sol de la barra superior o `Ctrl + Shift + L`) |

### Funciones por rol (V4.2)

| Rol | Qué puede hacer |
|---|---|
| **Administrador** | Todo lo de la edición 4 y todo lo de la edición Tecnología: fichas técnicas y especificaciones, series (consultar, registrar las de stock y dar destino), garantías y RMA completas, armador (armar, cotizar, anular y cobrar) y las **devoluciones**, también por falla |
| **Gerencia** | Todas las sucursales. Lo de la edición 4 (reportes, compras, contabilidad, auditoría, transferencias, anular y revertir facturas) más el **tablero Tecnología**, fichas técnicas, series (consultar, registrar y dar destino), garantías y RMA completas y el armador (arma, cotiza y anula; no cobra: no tiene caja) |
| **Bodega** | Su sucursal. Recibir compras **con series** (escáner o lista pegada), saldo inicial y ajustes con series, toma física, transferencias con series, fichas técnicas y especificaciones, series (registrar y dar destino) y garantías y RMA completas (diagnóstico, proveedor, reemplazo y entrega); reportes |
| **Ventas** | Su sucursal. Caja con series e IMEI y factura, clientes, historial de ventas, reportes, **armador de PC** (arma, cotiza, anula y cobra), consulta de series y abrir casos RMA (y agregarles notas) |
| **Cajero** | Su sucursal. Caja (abrir, cobrar y cerrar) con series e IMEI y factura, clientes, historial de ventas, **armador de PC** (arma, cotiza, anula y cobra), consulta de series y abrir casos RMA (y agregarles notas) |
| **Consulta** | Solo lectura: stock, catálogo con fichas técnicas, series e IMEI y su historia, casos RMA, reportes, documentos fiscales y libros |

---

## 6. Usuarios de prueba

**Edición 5 (esta rama, `Inventario-V4.2`):** empresa **TECHZONE**, Tech Zone Gaming S.R.L. Las **contraseñas** están en
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

Sucursales: **CM** = casa matriz La Paz, **CB** = Cochabamba, **SC** = Santa Cruz.

**Ediciones 3 y 4 (ramas `Inventario-V3.-BaseDeDatosLocal`, `Inventario-V4.-BaseDeDatosNube` e `Inventario-V4.1`):** la
empresa de prueba es **MINV**, la ferretería de ejemplo **Ferretería El Constructor S.R.L.**, con las mismas personas y
correos `@elconstructor.example` (por ejemplo, `admin@elconstructor.example`); en la V4 y la V4.1 sus sucursales son
**CM** casa matriz, **EA** El Alto y **SC** Santa Cruz (Sergio Mamani y Camila Fernández trabajan en EA).

En la **demostración** no hay contraseñas: se elige el rol en la pantalla de inicio.

---

## 7. Si algo no funciona

| Síntoma | Solución |
|---|---|
| El programa dice «Sin conexión con la base de datos» | `tools\bd_local.ps1 -Accion iniciar` y pulse «Reintentar» |
| Modo Nube: «Sin conexión con el servidor» | `tools\servidores_locales.ps1 -Accion iniciar`, luego «Probar» |
| Modo Nube: «actualice el escritorio» | El escritorio y el servidor deben ser de la misma versión: detenga los servidores, confirme la rama (`git branch --show-current`), vuelva a iniciarlos y publique el escritorio con `tools\publicar_escritorio.ps1` |
| La empresa TECHZONE no existe (o la base todavía es la ferretería MINV) | La base es de otra edición: desde la rama `Inventario-V4.2`, `tools\bd_local.ps1 -Accion recrear` |
| «Su usuario no tiene sucursales asignadas» | Como Administrador: Sucursales › Asignar usuarios |
| Facturas «fuera de línea» que no pasan a válidas | ¿Está encendido el simulador? `tools\servidores_locales.ps1 -Accion estado`; luego Estado SIAT › «Procesar ahora» |
| La caja dice «producto sin homologar» | Facturación › Homologación: asigne su código del SIN (o «Sugerir») |
| V4.2 · La caja no deja agregar un equipo: la serie «no está disponible» o «es de otra sucursal» | Esa unidad ya se vendió, está en tránsito, en garantía o en otra sucursal: elija otra de la lista de disponibles de la sucursal activa |
| V4.2 · «Faltan series» o «sobran series» | Un equipo serializado lleva exactamente una serie por unidad y la cantidad debe ser entera: complete el contador («3 de 3») |
| V4.2 · «IMEI inválido» | Un IMEI tiene 15 dígitos y el último es el dígito de control: revise que no falte ni sobre un número (los espacios y guiones se quitan solos) |
| V4.2 · «La serie ya existe» al recibir | Las series son únicas por producto en toda la empresa. Si la unidad vuelve (reparada o del proveedor), regístrela con un ajuste positivo y M-INV la reingresa |
| V4.2 · No se puede abrir el RMA: «fuera de garantía» | La garantía venció (fecha de la venta + meses del producto): ábralo como **reparación con cargo** |
| V4.2 · El armador no deja guardar la cotización | Tiene piezas incompatibles: corríjalas o confírmelo expresamente (la cotización queda marcada) |
| V4.2 · La devolución responde «Su rol no tiene el permiso…» | Con los roles de fábrica, las devoluciones las hace el Administrador |
| V4.2 · El tablero avisa «existencias con serie sin todas sus series» | Series e IMEI › **Registrar series de stock** |
| Olvidé la contraseña de prueba | Está en `%LOCALAPPDATA%\M-INV\usuarios-prueba.txt` |
| Excel compartido: «su cuenta no está autorizada» | El ADMIN agrega el correo al final de `02_USUARIOS` |
| Excel: los botones no hacen nada | En la edición Plus, habilite las macros. En la compartida, agregue el script sobre el recuadro ⚙ |

Más casos de la edición 5: [`docs/deployment/inicio-rapido-v4.2.md`](docs/deployment/inicio-rapido-v4.2.md) §9.

## 8. Documentación relacionada

| Tema | Documento |
|---|---|
| Excel compartido (V2.1) | [`docs/deployment/inicio-rapido.md`](docs/deployment/inicio-rapido.md) · [`docs/deployment/sharepoint-rbac-policies.md`](docs/deployment/sharepoint-rbac-policies.md) |
| Escritorio y base local (V3.1) | [`docs/deployment/inicio-rapido-v3.md`](docs/deployment/inicio-rapido-v3.md) · [`docs/product/escritorio-v3.1.md`](docs/product/escritorio-v3.1.md) |
| Nube y sucursales (V4) | [`docs/deployment/inicio-rapido-v4.md`](docs/deployment/inicio-rapido-v4.md) · [`docs/deployment/despliegue-nube-v4.md`](docs/deployment/despliegue-nube-v4.md) · [`docs/product/escritorio-v4.md`](docs/product/escritorio-v4.md) · [`docs/integration/api-gateway-v1.md`](docs/integration/api-gateway-v1.md) |
| Facturación SIAT (V4.1) | [`docs/deployment/inicio-rapido-v4.1.md`](docs/deployment/inicio-rapido-v4.1.md) · [`docs/product/escritorio-v4.1.md`](docs/product/escritorio-v4.1.md) · [`docs/billing/puesta-en-produccion-siat.md`](docs/billing/puesta-en-produccion-siat.md) · [`docs/billing/README.md`](docs/billing/README.md) · [`docs/architecture/facturacion-siat-v4.1.md`](docs/architecture/facturacion-siat-v4.1.md) · [`.claude/v41-billing-rules.md`](.claude/v41-billing-rules.md) · investigación de la normativa del SIN en [`docs/billing/investigacion-siat/`](docs/billing/investigacion-siat/) |
| Edición Tecnología (V4.2) | [`docs/deployment/inicio-rapido-v4.2.md`](docs/deployment/inicio-rapido-v4.2.md) · [`docs/product/escritorio-v4.2.md`](docs/product/escritorio-v4.2.md) · [`docs/architecture/edicion-tecnologia-v4.2.md`](docs/architecture/edicion-tecnologia-v4.2.md) · [`.claude/v42-tech-rules.md`](.claude/v42-tech-rules.md) · tema gaming en [`docs/product/ux-ui-guidelines.md`](docs/product/ux-ui-guidelines.md) §13 · tablas en [`docs/database/ERD-MINV-V3.md`](docs/database/ERD-MINV-V3.md) §9 |
| Historial de cambios | [`CHANGELOG.md`](CHANGELOG.md) |
