# Facturación SIAT de M-INV (V4.1)

M-INV 4.1 emite **documentos fiscales digitales** del **SIAT** (Servicio de Impuestos Nacionales de Bolivia, «el SIN»)
en la modalidad **Facturación Computarizada en Línea** (`codigoModalidad = 2`, sin firma digital):

| Documento | Cuándo lo emite M-INV | Sector · tipo |
|---|---|---|
| **Factura Compra Venta** (con derecho a crédito fiscal) | cada venta de caja, cada pedido de la tienda en línea (API) y cada factura manual de contingencia que se transcribe | 1 · 1 |
| **Nota Crédito-Débito** | cada devolución (total o parcial) de una venta facturada | 24 · 3 |

Además lleva el **libro de ventas IVA**, el **libro de compras** (con las facturas de los proveedores registradas sobre
las recepciones de mercadería) y el **resumen IVA / IT** del mes.

## Qué hace M-INV por la empresa

- **Emite dentro de la venta**: la venta, su stock, su pago, su asiento y su documento fiscal (con su **CUF**, su XML
  validado contra el XSD oficial y el detalle «congelado») se guardan juntos o no se guarda nada.
- **Envía al SIN después de cobrar**: la caja lo manda en el momento y, además, un trabajo automático (el servidor en la
  nube o el escritorio en modo local) envía cada pocos segundos lo que haya quedado pendiente.
- **Sigue facturando sin internet**: si el SIN no responde dos veces seguidas, el punto de venta pasa **fuera de línea**
  con un evento significativo; la caja **no se bloquea** (factura con tipo de emisión 2 y el último CUFD). Cuando vuelve
  la comunicación, M-INV se pone al día **solo**: CUFD nuevo → registro del evento → paquetes de hasta 500 facturas →
  validación.
- **Contingencia manual (CAFC)**: ante un corte de energía o una falla de equipo, la caja factura a mano con el
  talonario autorizado; después se **transcriben** las facturas y se envían en paquete (plazo de 72 h).
- **Anula y revierte** dentro del plazo del SIN (hasta el día 9 del mes siguiente), con el motivo del catálogo; la
  reversión una sola vez. La anulación NO devuelve mercadería por sí sola: «anular y devolver» es una opción explícita.
- **Mantiene los códigos del SIN**: CUIS (un año) por punto de venta, CUFD de cada día, hora del SIN y los 18 catálogos
  sincronizados a diario; avisa con anticipación lo que está por vencer (CUIS, token, plazos de eventos y paquetes).
- **Homologación**: cada producto, unidad y medio de pago tiene su código del SIN (con sugerencias automáticas); no se
  factura nada sin homologar.
- **Entrega al comprador** el PDF (media carta) y el XML por correo, o el ticket de rollo con QR; el ambiente de pruebas
  lleva siempre «SIN VALOR LEGAL».
- **Protege los secretos**: el token delegado y la contraseña del correo se guardan cifrados (clave maestra fuera de la
  base) y no aparecen en pantallas, registros ni en la bitácora de llamadas al SIN; la tarjeta se guarda solo
  enmascarada.

## Dónde está cada cosa

| Para | Leer |
|---|---|
| Probarlo todo en este equipo con el simulador del SIN (el paso a paso) | [`docs/deployment/inicio-rapido-v4.1.md`](../deployment/inicio-rapido-v4.1.md) |
| Pasar del simulador al SIN real (autorización, piloto, inspección, producción) | [`puesta-en-produccion-siat.md`](puesta-en-produccion-siat.md) |
| Diseño: modelo de datos (esquema `billing`), estados, algoritmos, puertos, dónde corre el envío | [`docs/architecture/facturacion-siat-v4.1.md`](../architecture/facturacion-siat-v4.1.md) |
| Reglas obligatorias para quien programa (F-01 … F-17) | [`.claude/v41-billing-rules.md`](../../.claude/v41-billing-rules.md) |
| Pantallas del escritorio (Documentos fiscales, Estado SIAT, Homologación, Libros fiscales, Facturación SIAT) | `docs/product/escritorio-v4.1.md` |
| Servidores en la nube y variables (trabajo automático de la facturación, SIN real) | [`docs/deployment/despliegue-nube-v4.md`](../deployment/despliegue-nube-v4.md) §12 |

Piezas del código:

| Pieza | Ubicación |
|---|---|
| Dominio (documento fiscal, CUF, reglas, punto de venta, eventos, paquetes, CAFC) | `src/1. Core/MINV.Domain/Billing` |
| Casos de uso y consultas (contratos en `BillingContracts.cs`) | `src/1. Core/MINV.Application/Billing` |
| Cliente SOAP del SIN (`SiatSoapContract`: nombres de servicios, operaciones y elementos en UN solo lugar) | `src/2. Infrastructure/MINV.Infrastructure/Billing/Soap` |
| Simulador del SIN (motor, en proceso y HTTP en el puerto 5095) | `src/2. Infrastructure/MINV.Infrastructure/Billing/Simulator` · `src/4. Tools/MINV.SiatSimulator` |
| XML/XSD, PDF y rollo ESC/POS | `src/2. Infrastructure/MINV.Infrastructure/Billing` · `src/2. Infrastructure/MINV.Hardware` |
| Trabajo automático (servidor en la nube) | `src/2. Infrastructure/MINV.Infrastructure/Billing/Hosting` |
| Órdenes de línea de comandos | `minv siat estado · preparar · sincronizar · procesar · simulador-estado · simulador-apagar · simulador-encender` |
| Datos de prueba que facturan | `src/2. Infrastructure/MINV.Infrastructure/Seeding` (`minv datos-prueba`, `tools\bd_local.ps1`) |

## Investigación de la normativa del SIN

La carpeta [`investigacion-siat/`](investigacion-siat/) resume y cita (página por página de
`siatinfo.impuestos.gob.bo`) todo lo que M-INV necesita saber del SIN:

| # | Documento | De qué trata |
|---|---|---|
| 00 | [`00-indice-y-contradicciones.md`](investigacion-siat/00-indice-y-contradicciones.md) | Índice, verificación de los 6 puntos críticos, contradicciones resueltas, **huecos abiertos H-01 … H-29**, decisiones y requisitos priorizados |
| 01 | [`01-codigos-operaciones-conexion.md`](investigacion-siat/01-codigos-operaciones-conexion.md) | Token delegado, CUIS, CUFD, verificar NIT, puntos de venta, eventos significativos, despliegue |
| 02 | [`02-servicios-facturacion-computarizada.md`](investigacion-siat/02-servicios-facturacion-computarizada.md) | Servicios de envío, `archivo` (GZIP) y `hashArchivo` (SHA-256), paquetes, estados, anulación, casos de prueba |
| 03 | [`03-emision-contingencia-anulacion.md`](investigacion-siat/03-emision-contingencia-anulacion.md) | Emisión, los cinco casos de falla, eventos, paquetes, CAFC, anulación y reversión, plazos y máquinas de estado |
| 04 | [`04-catalogos-errores-homologacion.md`](investigacion-siat/04-catalogos-errores-homologacion.md) · [`04-catalogo-productos-sin-ferreteria.csv`](investigacion-siat/04-catalogo-productos-sin-ferreteria.csv) | 18 catálogos, 193 códigos de respuesta, homologación de productos (ferretería) |
| 05 | [`05-xml-xsd-validaciones.md`](investigacion-siat/05-xml-xsd-validaciones.md) | XML campo por campo (sector 1 y 24), fórmulas, decimales, validación con .NET |
| 06 | [`06-representacion-grafica-qr-redondeo.md`](investigacion-siat/06-representacion-grafica-qr-redondeo.md) | CUF (con vectores oficiales), redondeo HALF-UP, QR, representación gráfica y leyendas |
| 07 | [`07-notas-credito-debito-casos-especiales.md`](investigacion-siat/07-notas-credito-debito-casos-especiales.md) | Notas crédito-débito, casos especiales, Registro de Compras y Ventas |
| 08 | [`08-autorizacion-inspeccion-versionamiento.md`](investigacion-siat/08-autorizacion-inspeccion-versionamiento.md) | Autorización del sistema, Fases I-III, **checklist de inspección II-1 … II-15**, inicio de operaciones, versiones 2021-2026 |

## Qué está verificado

Con la documentación oficial (00 §2) y con pruebas automáticas del propio M-INV:

- **CUF**: el algoritmo reproduce los vectores oficiales del SIN (Módulo 11, Base 16, código de control).
- **XML**: el de la factura Compra Venta (30 + 11 elementos) y el de la nota sector 24 validan contra los **XSD
  oficiales** embebidos; los opcionales van con `xsi:nil`; montos con 2 decimales y redondeo HALF-UP por línea.
- **Archivo y hash**: GZIP del XML; paquete GZIP(TAR); SHA-256 hexadecimal en minúsculas sobre los bytes GZIP.
- **Cabecera del token**: `apikey: TokenApi <token>` en todas las llamadas.
- **Plazos**: anulación y reversión hasta el día 9 del mes siguiente; evento significativo y paquetes fuera de línea en
  48 h; CAFC en 72 h; CUFD de 24 h (72 h si falla su solicitud); CUIS de 365 días.
- **Flujo completo contra el simulador** (en memoria, por HTTP y con PostgreSQL real): emisión en línea, rechazo y
  re-emisión, fuera de línea con recuperación y paquete validado, contingencia manual CAFC, anulación, reversión, nota
  crédito-débito, libros; y la empresa de prueba de `minv datos-prueba` factura con todo eso.

## Qué queda por confirmar con el SIN

Los huecos de la documentación pública están en [00 §4](investigacion-siat/00-indice-y-contradicciones.md). Ninguno
bloquea el diseño porque todo lo dudoso es **configuración** o está centralizado en un solo lugar, pero hay que
confirmarlos en el **ambiente piloto** antes de producción:

| Hueco | Qué falta | Dónde se ajusta en M-INV |
|---|---|---|
| **H-01 (el más importante)** | **URL y WSDL reales** de cada servicio en piloto y producción, `targetNamespace`, versión de SOAP y mayúsculas exactas de las operaciones. Llegan en el reporte de registro del sistema (piloto) y en el inicio de operaciones (producción) | Facturación SIAT › Conexión (URL de cada servicio y namespace, por ambiente) · `SiatSoapContract` (nombres de operaciones y elementos) |
| H-02 · H-03 · H-04 · H-05 · H-06 | Estructura de las respuestas y mensajes, tipo del `archivo` (Base64), nombres dentro del TAR, nombres de las 18 sincronizaciones, `verificarComunicacion` | `SiatSoapContract` y `SiatSoapReplies` (lectura tolerante por nombre local) |
| H-07 · H-08 · H-09 | Zona horaria de las fechas (M-INV usa la hora del SIN sincronizada, sin sufijo), formato de las vigencias, si el día 9 es inclusivo | `FiscalRules`, `SiatClock` |
| H-10 · H-11 · H-12 · H-13 | Códigos de catálogos (motivos de anulación, métodos de pago, eventos…), estados de verificación, texto de la leyenda «fuera de línea», advertencias | Catálogos sincronizados (nunca fijos) · leyendas en Facturación SIAT |
| H-14 · H-15 · H-16 | Rango y vigencia del CAFC, hora de las facturas manuales, CUFD en contingencias largas | Registro del CAFC por sucursal · transcripción con fecha y hora |
| H-17 | Notas crédito-débito sin conexión (no hay paquetes de notas) | DECISIÓN M-INV: la nota espera en cola hasta que vuelva la comunicación |
| H-18 · H-19 | Descuento adicional en notas (sector 47), prorrateo del descuento, numeración de las notas | Nota sector 24 con descuento prorrateado; serie por punto de venta |
| H-20 · H-21 · H-22 · H-23 · H-24 | Sistema **propio o proveedor** (M-INV es multiempresa), autorización web/API, reparto de las pruebas, características del formulario, texto de las RND | Proceso de autorización ([`puesta-en-produccion-siat.md`](puesta-en-produccion-siat.md) §1) |
| H-25 · H-26 · H-27 · H-28 · H-29 | URL del QR en producción, monto literal con gift card, regla importado/nacional, Registro de Compras por servicio web, reintentos y tiempo de espera | URL base del QR y tiempo de espera por ambiente · el libro de compras se genera (el envío por servicio web queda fuera de la V4.1) |

**Lo que no se hace en la V4.1** (preparado, sin implementar): modalidad **electrónica** (firma digital), emisión
**masiva**, otros sectores (47, 35, 41…), pago **combinado** de varios medios en una misma venta (II-3 del checklist),
y el envío del **Registro de Compras** por servicio web.
