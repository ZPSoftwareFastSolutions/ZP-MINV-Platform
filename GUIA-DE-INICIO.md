# Guía de inicio de M-INV · las cuatro ediciones, paso a paso

M-INV es el sistema de inventarios de **Z&P Software Fast Solutions**. Nació como un libro de Excel y hoy es una
aplicación de escritorio con base de datos en la nube y facturación del SIN. Las cuatro ediciones siguen en el
repositorio y funcionan con la **misma lógica de negocio**:

- El stock solo cambia registrando **movimientos** (entradas, salidas y ajustes).
- Nada se borra: los errores se corrigen con un **ajuste**.
- Todo queda **auditado**.

Esta guía explica, para cada edición:

1. **qué es y para quién**,
2. el **algoritmo** para ponerla en marcha,
3. **qué funciones** tiene y **qué puede hacer cada rol**.

| # | Edición | Versión y rama | Para quién | Qué necesita |
|---|---|---|---|---|
| 1 | **Excel local** | V1.2 · `Inventario-V1.2` | Una persona o un equipo pequeño en una sola PC | Excel 2016 o superior en Windows |
| 2 | **Excel compartido** | V2.1 · `Inventario-V2.1` | Varias personas a la vez, cada una con su rol | Microsoft 365 de trabajo, SharePoint u OneDrive, Office Scripts |
| 3 | **App de escritorio con base de datos local** | V3.1 · `Inventario-V3.-BaseDeDatosLocal` | Empresa con una sede, cajas y punto de venta | Windows 10/11, .NET 8 o superior, PostgreSQL (se instala solo) |
| 4 | **App de escritorio con base de datos en la nube y facturación** | V4 · `Inventario-V4.-BaseDeDatosNube` y **V4.1 · `Inventario-V4.1`** | Empresa con sucursales, tienda en línea y facturación del SIN | Lo de la 3, más un servidor en la nube (se prueba entero en este equipo) |

> **Contraseñas.** Nunca están en el repositorio. Las contraseñas de la base local y de los usuarios de prueba se
> generan al azar en cada carga y quedan solo en este equipo, en la carpeta `%LOCALAPPDATA%\M-INV\`:
>
> - `usuarios-prueba.txt`: los usuarios de prueba;
> - `credenciales-bd-local.txt`: las contraseñas de PostgreSQL;
> - `claves-integracion.txt`: las claves de integración y el token.
>
> Para verlos: `notepad $env:LOCALAPPDATA\M-INV\usuarios-prueba.txt`.

Todas las órdenes de esta guía se escriben en **PowerShell**, dentro de la carpeta del repositorio.

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

> **Estado de la V4.1 al publicar esta guía:**
>
> - **Listo** (probado con más de 400 pruebas automáticas, incluidas las de PostgreSQL):
>   - la base de datos de la facturación: 27 tablas, 140 en total;
>   - el cliente de los servicios del SIN y el simulador del SIN;
>   - el XML validado contra los esquemas oficiales, el código CUF y el QR;
>   - la representación gráfica en PDF y en rollo;
>   - el registro del servidor en la nube.
> - **En construcción** (se incorporan en los próximos commits de esta rama, y esta guía se actualizará):
>   - la emisión automática al cobrar y el envío al SIN;
>   - la contingencia fuera de línea;
>   - la anulación, la reversión y las notas crédito-débito;
>   - los libros de compras y ventas;
>   - las pantallas de facturación del escritorio.

### Algoritmo · todo en este equipo

La guía detallada es [`docs/deployment/inicio-rapido-v4.md`](docs/deployment/inicio-rapido-v4.md).

```text
 A. Requisitos: los de la edición 3 (PostgreSQL portátil instalado con tools\bd_local.ps1 -Accion instalar).
 B. Base local con datos de prueba de 3 sucursales, 12 usuarios, transferencias y pedidos web:
        powershell -ExecutionPolicy Bypass -File tools\bd_local.ps1 -Accion recrear
    Una base existente de la V4 se actualiza a la V4.1 sin perder datos:
        dotnet run --project "src/4. Tools/MINV.Cli" -- migrate --conexion "<cadena del rol minv_owner>"
 C. Publique el programa:  powershell -ExecutionPolicy Bypass -File tools\publicar_escritorio.ps1
 D. Encienda la «nube» local (servidor en la nube :5080 y API Gateway :5090):
        powershell -ExecutionPolicy Bypass -File tools\servidores_locales.ps1 -Accion iniciar
 E. Abra dist\M-INV-4.1.0-alpha.1-win-x64\M-INV.exe
      → «Nube» → servidor http://localhost:5080 → «Probar» → empresa MINV → correo y contraseña
      («Base local» también funciona, sin servidor)
 F. Arriba elija la SUCURSAL ACTIVA: todo lo que registre queda en esa sucursal.
 G. Tienda en línea: API en http://localhost:5090 (documentación en /docs), con la API Key de claves-integracion.txt.
 H. Al terminar:  tools\servidores_locales.ps1 -Accion detener   (estado: -Accion estado)
 I. Nube real (DigitalOcean, AWS RDS o Supabase):  docs\deployment\despliegue-nube-v4.md
```

### Funciones que se suman a la edición 3

| Área | Qué hace |
|---|---|
| **Sucursales** | Tablero por sucursal: ventas, stock valorizado y lo que está en tránsito. Stock consolidado. Alta de sucursales y asignación de usuarios |
| **Transferencias** | La sucursal de origen solicita y despacha; la de destino recibe, contando lo que llegó. Los faltantes quedan registrados como merma en tránsito, con sus asientos contables |
| **Integraciones** | API Keys con permisos por sucursal. Webhooks firmados para avisar de ventas, anulaciones y transferencias. Historial de entregas |
| **Modo Nube** | Sesión con token. Cada acción se verifica en el servidor con los permisos y las sucursales del usuario |
| **Facturación SIAT (V4.1)** | Facturas Compra Venta y notas crédito-débito, con CUF, QR, leyendas de la Ley 453 y PDF o rollo. Envío al SIN al cobrar. **Contingencia automática**: sin internet la caja no se bloquea; factura fuera de línea y, al volver la conexión, registra el evento y envía los paquetes solo. Anulación hasta el día 9 del mes siguiente; reversión una sola vez. Devoluciones parciales con nota crédito-débito. Facturas manuales CAFC. Homologación de productos. Verificación de NIT. Libros de ventas y compras y resumen de IVA e IT |

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
    Fase II, piloto de la Fase III y token de producción. El detalle está en
    docs\billing\investigacion-siat\08-autorizacion-inspeccion-versionamiento.md (la guía paso a paso
    docs\billing\puesta-en-produccion-siat.md llega con la V4.1 completa).
```

---

## 5. Usuarios de prueba y qué puede hacer cada rol (ediciones 3 y 4)

Empresa **MINV**, Ferretería El Constructor S.R.L. Las **contraseñas** están en
`%LOCALAPPDATA%\M-INV\usuarios-prueba.txt` y cambian cada vez que se recrea la base.

| Rol | Nombre | Correo | Sucursales |
|---|---|---|---|
| Administrador | Administrador General | admin@elconstructor.example | Todas |
| Gerencia | Luis Gutiérrez | luis.gutierrez@elconstructor.example | Todas (gerencia global) |
| Bodega | Mariana Suárez | mariana.suarez@elconstructor.example | CM |
| Bodega | Sergio Mamani | sergio.mamani@elconstructor.example | EA |
| Bodega | Daniela Céspedes | daniela.cespedes@elconstructor.example | SC |
| Ventas | Fernando Choque | fernando.choque@elconstructor.example | CM |
| Ventas | Sergio Rojas | sergio.rojas@elconstructor.example | SC |
| Cajero | Miguel Ortiz | miguel.ortiz@elconstructor.example | CM |
| Cajero | Luis Flores | luis.flores@elconstructor.example | CM |
| Cajero | Camila Fernández | camila.fernandez@elconstructor.example | EA |
| Cajero | Camila Morales | camila.morales@elconstructor.example | SC |
| Consulta | María Villarroel | maria.villarroel@elconstructor.example | CM, EA, SC |

Sucursales: **CM** = casa matriz, **EA** = El Alto, **SC** = Santa Cruz.

| Rol | Funciones |
|---|---|
| **Administrador** | Todo: catálogo, usuarios y roles, movimientos, compras, caja, contabilidad, auditoría, sucursales, transferencias, integraciones y toda la facturación, incluida la configuración del SIAT |
| **Gerencia** | Todas las sucursales. Stock, reportes, ventas, compras, contabilidad, auditoría y transferencias. Facturación: consultar, anular, revertir, notas y contingencia |
| **Bodega** | Su sucursal. Entradas, saldo inicial y ajustes; toma física; compras y recepciones; reportes; transferencias (despachar y recibir) |
| **Ventas** | Su sucursal. Caja y salidas, clientes, historial de ventas, reportes. Facturación: consultar y emitir |
| **Cajero** | Su sucursal. Caja (abrir, cobrar y cerrar), salidas, clientes e historial de ventas. Facturación: consultar y emitir |
| **Consulta** | Solo lectura: stock, reportes y, en la V4.1, los documentos fiscales y los libros |

En la **demostración** no hay contraseñas: se elige el rol en la pantalla de inicio.

---

## 6. Si algo no funciona

| Síntoma | Solución |
|---|---|
| El programa dice «Sin conexión con la base de datos» | `tools\bd_local.ps1 -Accion iniciar` y pulse «Reintentar» |
| Modo Nube: «Sin conexión con el servidor» | `tools\servidores_locales.ps1 -Accion iniciar`, luego «Probar» |
| Modo Nube: «actualice el escritorio» | El escritorio y el servidor deben ser de la misma versión mayor: vuelva a publicar con `tools\publicar_escritorio.ps1` |
| «Su usuario no tiene sucursales asignadas» | Como Administrador: Sucursales › Asignar usuarios |
| Olvidé la contraseña de prueba | Está en `%LOCALAPPDATA%\M-INV\usuarios-prueba.txt` |
| Excel compartido: «su cuenta no está autorizada» | El ADMIN agrega el correo al final de `02_USUARIOS` |
| Excel: los botones no hacen nada | En la edición Plus, habilite las macros. En la compartida, agregue el script sobre el recuadro ⚙ |

## 7. Documentación relacionada

| Tema | Documento |
|---|---|
| Excel compartido (V2.1) | [`docs/deployment/inicio-rapido.md`](docs/deployment/inicio-rapido.md) · [`docs/deployment/sharepoint-rbac-policies.md`](docs/deployment/sharepoint-rbac-policies.md) |
| Escritorio y base local (V3.1) | [`docs/deployment/inicio-rapido-v3.md`](docs/deployment/inicio-rapido-v3.md) · [`docs/product/escritorio-v3.1.md`](docs/product/escritorio-v3.1.md) |
| Nube y sucursales (V4) | [`docs/deployment/inicio-rapido-v4.md`](docs/deployment/inicio-rapido-v4.md) · [`docs/deployment/despliegue-nube-v4.md`](docs/deployment/despliegue-nube-v4.md) · [`docs/product/escritorio-v4.md`](docs/product/escritorio-v4.md) · [`docs/integration/api-gateway-v1.md`](docs/integration/api-gateway-v1.md) |
| Facturación SIAT (V4.1) | [`docs/architecture/facturacion-siat-v4.1.md`](docs/architecture/facturacion-siat-v4.1.md) · [`.claude/v41-billing-rules.md`](.claude/v41-billing-rules.md) · investigación de la normativa del SIN en [`docs/billing/investigacion-siat/`](docs/billing/investigacion-siat/) |
| Historial de cambios | [`CHANGELOG.md`](CHANGELOG.md) |
