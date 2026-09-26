# 04. Catálogos (sincronización), códigos de error, documentos sector y homologación de productos

Especificación de implementación para M-INV (facturación SIAT, **modalidad 2: Computarizada en Línea**, sin firma digital).
Todo dato sale de los archivos citados. Cuando algo no aparece en la documentación, se indica **NO DOCUMENTADO**.
Lo que solo aplica a la modalidad electrónica se marca con **⚠ ELECTRÓNICA**.

## 0. Fuentes

**Bloque asignado (leído completo):**

| Clave | Archivo (carpeta `siat/txt`) | URL original |
|---|---|---|
| [SINC] | `facturacion-en-linea__implementacion-servicios-facturacion__sincronizacion-codigos-catalogos.md` | .../implementacion-servicios-facturacion/sincronizacion-codigos-catalogos |
| [ERR] | `facturacion-en-linea__implementacion-servicios-facturacion__codigos-error-siat.md` | .../implementacion-servicios-facturacion/codigos-error-siat |
| [HOM] | `facturacion-en-linea__requerimientos__homologacion-de-productos-servicios.md` | .../requerimientos/homologacion-de-productos-servicios |
| [TIPOS] | `informacion__tipos-facturas.md` | .../informacion/tipos-facturas |
| [GEN] | `informacion__generalidades-sfvl.md` | .../informacion/generalidades-sfvl |
| [XLS-SINC] | adjunto `CasosDePruebaSincronizaciónCatálogos.xlsx` (18 hojas) | /images/archivos_tecnicos/archivos_apoyo/ |
| [XLS-PROD] | adjunto `Catalogo Productos.xls` (1 hoja, 21 556 filas) | /images/archivos_tecnicos/archivos_apoyo/Catalogo Productos.xls |

**Referencias cruzadas** (fuera del bloque; se usan solo para completar valores de catálogo, y se citan con su nombre de archivo):
`facturacion-en-linea__algoritmos-utilizados__generacion-cuf.md` [CUF],
`facturacion-en-linea__requerimientos__sistema-informatico.md` [SISINF],
`facturacion-en-linea__emision-y-envio-de-facturas__emision-y-envio.md` [EMI],
`facturacion-en-linea__emision-y-envio-de-facturas__solicitud-token.md` [TOKEN],
`facturacion-en-linea__emision-y-envio-de-facturas__contingencia-y-eventos-significativos.md` [CONT],
`facturacion-en-linea__implementacion-servicios-facturacion__operaciones__registro-punto-de-venta.md` [RPV],
`facturacion-en-linea__implementacion-servicios-facturacion__facturacion-computarizada__recepcion-factura-computarizada.md` [RECEP],
`facturacion-en-linea__implementacion-servicios-facturacion__codigos__verifica-nit.md` [VNIT],
`facturacion-en-linea__archivos-xml-xsd-de-facturas-electronicas__factura-de-compra-y-venta.md` [XSD-CV],
`facturacion-en-linea__autorizacion-de-sistemas__pruebas-para-la-autorizacion-del-sistema-de-facturacion__fase-i-pruebas.md` [FASE1],
`...__fase-ii-inspeccion.md` [FASE2], `versionamiento__versionamiento-2021.md` [VER2021],
adjunto `adjuntos/xml/CompraVentaXML/facturaComputarizadaCompraVenta.xsd` y `.xml` [XSD-CV-FILE].

**Archivos que acompañan esta especificación** (misma carpeta `specs/`):
- `04-catalogo-productos-sin-completo.csv`: exportación íntegra de [XLS-PROD] en UTF-8, separador `;`, con las descripciones recortadas (trim). Tiene 21 555 filas de datos y las columnas `codigo_producto_sin;descripcion_producto;codigo_actividad;descripcion_actividad`.
- `04-catalogo-productos-sin-ferreteria.csv`: el subconjunto de ferretería y construcción de la sección 6.4 (276 filas).

---

## 1. Generalidades [GEN]

- El SIN publica el "Anexo Técnico" que documenta las modalidades de facturación. Cubre la autorización, la emisión, el registro y el envío de facturas, tanto manuales como electrónicas.
- Base legal citada: **Resolución Normativa de Directorio N° 102100000011** (el enlace apunta a `https://www.impuestos.gob.bo/ckeditor/plugins/imageuploader/uploads/491335f008.pdf`).
  - El adjunto descargado `adjuntos/rnd_491335f008.pdf` **no es la RND**: es el HTML de la portada del sitio del SIN ("Impuestos - SIN"). Por eso el texto de la RND **NO está disponible** en el material.
- Última actualización publicada, del **10/06/2026**: en la representación gráfica de los documentos sector **13** (Servicios Básicos) y **40** (Servicios Básicos Zona Franca), la leyenda "Tarifa Dignidad" se reemplaza por "Descuento Patria". No afecta a M-INV (ferretería), pero muestra que el catálogo de leyendas y las representaciones gráficas **cambian con el tiempo**.

---

## 2. Tipos de documentos fiscales y documentos sector [TIPOS]

### 2.1 Tipos de documento fiscal (paramétrica "Tipo Factura")

| Código `tipoFacturaDocumento` | Tipo | Definición [TIPOS] | Fuente del código |
|---|---|---|---|
| 1 | Factura con derecho a crédito fiscal | Genera crédito fiscal para el comprador y débito fiscal para el vendedor. | [CUF]: "1 = Factura con Derecho a Crédito Fiscal" |
| 2 | Factura sin derecho a crédito fiscal | No genera crédito fiscal para el comprador ni débito fiscal para el vendedor. | [CUF]: "2 = Factura sin Derecho a Crédito Fiscal" |
| 3 | Documento de ajuste | Nota de Crédito-Débito (por devolución parcial o total, o por rescisión de contrato), emitible **hasta 18 meses** después de la factura original. También la Nota de Conciliación, para ajustes por transacciones de periodos anteriores **no mayores a 12 meses**. | [CUF]: "3 = Documento de Ajuste" |
| 4 | Documento equivalente | No es una factura o nota propiamente dicha, pero su emisión implica una operación gravada por el IVA y da lugar al cómputo del crédito fiscal. Solo se usa para el sector 30, Boleto Aéreo. | [VER2021]: "Se agrego el tipo de documento 4. Documento Equivalente en el sector 30 Boleto Aéreo" (el código 4 **no** figura en la tabla del CUF) |

Notas:
- En el CUF, el campo "TIPO FACTURA / DOCUMENTO AJUSTE" tiene 1 dígito [CUF].
- El SIAT publica este catálogo por sincronización (servicio "Códigos de Tipo Factura") [SINC]. Los valores anteriores deben **validarse contra lo sincronizado**.

### 2.2 Lista completa de documentos sector [TIPOS]

La columna "Cód. tipo" es el `tipoFacturaDocumento` derivado del texto de [TIPOS] y de la sección 2.1.
La columna "M-INV" indica la relevancia para una ferretería o comercio de materiales de construcción (interpretación propia, no del SIN).

| Código | Documento sector | Características (texto de [TIPOS]) | Tipo factura/documento [TIPOS] | Cód. tipo | Página XSD | M-INV |
|---|---|---|---|---|---|---|
| 1 | Factura de Compra y Venta | Habilitada para transacciones por bienes o servicios en general, incluyen línea blanca, negra y cualquier actividad que involucre un intercambio de estos. | Con derecho a crédito fiscal | 1 | `factura-de-compra-y-venta` | **SÍ (principal)** |
| 2 | Factura de Alquiler de Bienes Inmuebles | Habilitado para alquiler de bienes inmuebles propios. | Con derecho a crédito fiscal | 1 | `recibo-alquiler-bienes-inmuebles` | No |
| 3 | Factura Comercial de Exportación | Habilitada para transacciones de exportación de bienes, no se incluyen minerales. | Sin derecho a crédito fiscal | 2 | `factura-comercial-exportacion` | No |
| 4 | Factura de Comercial de Exportación en Libre Consignación | Habilitada para transacciones de exportación de bienes en libre consignación. | Sin derecho a crédito fiscal | 2 | `factura-comercial-exportacion-libre-consignacion` | No |
| 5 | Factura de Venta en Zona Franca | Habilitada para transacciones en zonas francas a concesionario o usuario. | Sin derecho a crédito fiscal | 2 | `factura-de-venta-en-zona-franca` | No |
| 6 | Factura de Servicio Turístico y Hospedaje | Habilitada para la exportación de servicios turísticos y hospedaje, alcanzados por el Artículo 30 de la Ley N° 292. | Sin derecho a crédito fiscal | 2 | `factura-de-servicios-turisticos-y-hospedaje` | No |
| 7 | Factura de Seguridad Alimentaria y Abastecimiento | Habilitada para comercialización de alimentos exentos de impuestos. | Sin derecho a crédito fiscal | 2 | `factura-de-seguridad-alimentaria-y-abastecimiento` | No |
| 8 | Factura Tasa Cero Venta de Libros y Transporte Internacional de Carga por Carretera | Habilitada para los que se encuentren alcanzados por el Régimen Tasa Cero en el IVA. Para la venta de libros nacionales o importados y publicaciones oficiales. Por el transporte internacional de carga por carretera. | Sin derecho a crédito fiscal | 2 | `factura-tasa-cero` | No |
| 9 | Facturas de Compra y Venta de Moneda Extranjera | Habilitada para transacciones de compra/venta de moneda extranjera. | Sin derecho a crédito fiscal | 2 | `factura-de-compra-venta-moneda-extranjera` | No |
| 10 | Factura Dutty Free | Habilitada para los que realicen ventas en tiendas libres o Dutty Free. | Sin derecho a crédito fiscal | 2 | `factura-dutty-free` | No |
| 11 | Factura Sectores Educativos | Habilitada para la facturación de unidades educativas preescolares, primaria, secundaria, de educación superior, institutos educativos, enseñanza de adultos y otros tipos de enseñanza. | Con derecho a crédito fiscal | 1 | `factura-sector-educativo` | No |
| 12 | Factura de Comercialización de Hidrocarburos | Habilitada para la venta de combustible diésel oíl, venta de combustible gasolina especial y/o gasolina Premium, venta de combustible para automotores. | Con derecho a crédito fiscal | 1 | `factura-comercializacion-hidrocarburos` | No |
| 13 | Factura de Servicios Básicos | Habilitada para la distribución de agua, electricidad y Cooperativas Telefónicas que dentro de sus operaciones utilicen otras tasas. | Con derecho a crédito fiscal | 1 | `factura-servicios-basicos` | No |
| 14 | Factura Productos Alcanzados por el ICE | Habilitada a los productos que estén alcanzados por el ICE, por ejemplo: cigarrillos, bebidas alcohólicas y otros. | Con derecho a crédito fiscal | 1 | `factura-alcanzada-por-ice` | No |
| 15 | Factura de Entidades Financieras | Habilitada para entidades de carácter financiero, por ejemplo: bancos, cooperativas y otros. No incluyen casas de cambio. | Con derecho a crédito fiscal | 1 | `factura-entidades-financieras` | No |
| 16 | Factura de Hoteles | Habilitada para hoteles, hostales, alojamientos y otros, cuando los huéspedes sean de origen nacional o residentes en Bolivia. | Con derecho a crédito fiscal | 1 | `factura-hoteles` | No |
| 17 | Factura de Hospitales/Clínicas | Habilitada para hospitales y clínicas, deberá incluir información de los pacientes y médicos cuando sea una intervención quirúrgica. | Con derecho a crédito fiscal | 1 | `factura-hospitales-clinicas` | No |
| 18 | Factura de Juegos de Azar | Habilitada para las actividades que incluyan sorteos, concursos o juegos de azar. | Con derecho a crédito fiscal | 1 | `factura-juegos-azar` | No |
| 19 | Factura de Hidrocarburos Alcanzada IEHD | Habilitada para empresas dedicadas a la comercialización de hidrocarburos o sus derivados en primera fase | Con derecho a crédito fiscal | 1 | `factura-de-hidrocarburos` | No |
| 20 | Factura Comercial de Exportación de Minerales | Habilitada para transacciones de exportación de minerales. | Sin derecho a crédito fiscal | 2 | `factura-comercial-exportacion-minera` | No |
| 21 | Factura de Venta de Minerales | Habilitada para la venta de minerales en el territorio nacional. | Con derecho a crédito fiscal | 1 | `factura-venta-interna-minerales` | No |
| 22 | Factura de Telecomunicaciones | Habilitada para servicios de telecomunicaciones. | Con derecho a crédito fiscal | 1 | `factura-telecomunicaciones` | No |
| 23 | Factura Prevalorada | Habilitada para actividades de cobro de tasa aeroportuaria y terrestre, y para entradas a ferias. | Con derecho a crédito fiscal | 1 | `factura-prevalorada` | No |
| 24 | Nota de Crédito - Débito | Habilitada para realizar ajustes en el crédito y débito fiscal de los Sujetos Pasivos o compradores. | Documento de Ajuste | 3 | `nota-credito-debito` | **SÍ** (devoluciones) |
| 28 | Factura Comercial de Exportación de Servicios | Habilitada para Contribuyentes que Exportan Servicios | Sin derecho a crédito fiscal | 2 | `factura-comercial-de-exportacion-de-servicios` | No |
| 29 | Nota de Conciliación | Habilitada para realizar ajustes en el Crédito y en el Débito Fiscal de los Sujetos Pasivos del IVA por transacciones facturadas en periodos anteriores no mayores a doce (12) meses, por servicios de energía eléctrica, telecomunicaciones, agua potable e hidrocarburos. | Documento de Ajuste | 3 | `nota-conciliacion` | No |
| 30 | Boleto Aéreo | Habilitada para el registro de pasajes aéreos. | Documento Equivalente | 4 | `boleto-aereo` | No |
| 31 | Factura de Suministro de Energía | Habilitada para la recarga de Energía Eléctrica a Vehículos Eléctricos. | Con derecho a crédito fiscal | 1 | `factura-de-suministro-de-energia` | No |
| 33 | Factura Tasa Cero IVA Ley N° 1613 | Habilitada para la importación y comercialización de bienes de capital y plantas industriales | Sin derecho a crédito fiscal | 2 | `facturacion-tasa-cero-iva-ley-n-1613` | No |
| 34 | Factura de Seguros | Habilitada para transacciones específicas del Sector Seguros | Con derecho a crédito fiscal | 1 | `factura-de-seguros` | No |
| 35 | Factura Compra Venta Bonificaciones | Habilitada para transacciones por bienes o servicios en general, incluyen línea blanca, negra y cualquier actividad que involucre un intercambio de estos. Permite descuento total en algunos de los productos en el detalle. | Con derecho a crédito fiscal | 1 | `factura-compra-venta-bonificaciones` | Opcional |
| 36 | Factura Prevalorada Sin Derecho Crédito Fiscal | Habilitada para actividades de realización de Espectáculos Públicos Eventuales, Zona Franza e Importación y Venta de Libros | Sin derecho a crédito fiscal | 2 | `factura-prevalorada-sin-derecho-a-credito-fiscal` | No |
| 37 | Factura de Comercialización de GNV | Habilitada para la venta de Gas Natural Vehicular | Con derecho a crédito fiscal | 1 | `factura-comercializacion-de-gnv` | No |
| 38 | Factura Hidrocarburos No Alcanzada IEHD | Habilitada para todas aquellas actividades exentas del pago del IEHD | Con Derecho a crédito fiscal | 1 | `factura-de-hidrocarburos-no-alcanzada-iehd` | No |
| 39 | Factura de Comercialización De GN y GLP | Habilitada para la comercialización de Gas Natural y Gas Licuado de Petróleo | Con Derecho a Crédito Fiscal | 1 | `factura-comercializacion-de-gn-y-glp` | No |
| 40 | Factura de Servicios Básicos Zona Franca | Habilitada para la distribución de agua, electricidad o cualquier servicio que se considere básico, de acuerdo a normativa vigente en Zona Franca | Sin Derecho a Crédito Fiscal | 2 | `factura-servicios-basicos-zona-franca` | No |
| 41 | Factura de Compra Venta Tasas | Habilitada para transacciones por bienes o servicios en general, incluyen línea blanca, negra y cualquier actividad que involucre un intercambio de estos, permite incluir tasas no sujetas a Crédito Fiscal | Con Derecho a Crédito Fiscal | 1 | `factura-compra-venta-tasas` | Opcional |
| 42 | Factura Alquiler Zona Franca | Habilitada para el alquiler de Bienes Inmuebles en zona Franca | Sin Derecho a Crédito Fiscal | 2 | `alquiler-zona-franca` | No |
| 43 | Factura Comercial de Exportación Hidrocarburos | Habilitada para transacciones de exportación del sector Hidrocarburos adecuado a legislativa vigente | Sin Derecho a Crédito Fiscal | 2 | `factura-comercial-exportacion-hidrocarburos` | No |
| 44 | Factura Importación y Comercialización de Lubricantes | Habilitada para empresas que importen de forma directa lubricantes y que comercialicen los mismos al consumidor final o distribuidor (No utilizada para servicios, solo venta de productos) | Con Derecho a Crédito Fiscal | 1 | `factura-importacion-y-comercializacion-de-lubricantes` | No |
| 45 | Factura Comercial de Exportación Precio Venta | Habilitada para transacciones de exportación de bienes, no se incluye a la exportación de minerales. | Sin Derecho a Crédito Fiscal | 2 | `factura-comercial-de-exportacion-precio-venta` | No |
| 46 | Factura Sector Educativo Zona Franca | Habilitada para la facturación de unidades educativas preescolares, primaria, secundaria, de educación superior, institutos educativos, enseñanza de adultos y otros tipos de enseñanza al interior de Zona Franca | Sin Derecho a Crédito Fiscal | 2 | `factura-sector-educativo-zona-franca` | No |
| 47 | Nota Crédito Débito Descuentos | Habilitada para realizar ajustes en el crédito y débito fiscal de los Sujetos Pasivos o compradores a facturas afectadas con un Descuento Adicional | Documento de Ajuste | 3 | `nota-credito-debito-descuento` | Opcional (si usa descuento adicional) |
| 48 | Nota Crédito Débito ICE | Habilitada para realizar ajustes en el crédito y débito fiscal de los Sujetos Pasivos o compradores a facturas emitidas con ICE | Documento de Ajuste | 3 | `nota-credito-debito-ice` | No |
| 49 | Factura Telecomunicaciones Zona Franca | Habilitada para servicios de telecomunicaciones en Zona Franca | Sin Derecho a Crédito Fiscal | 2 | `factura-telecomunicaciones-zona-franca` | No |
| 50 | Factura Hospitales/ Clínicas Zona Franca | Habilitada para hospitales y clínicas en Zona Franca, deberá incluir información de los pacientes y médicos cuando sea una intervención quirúrgica. | Sin Derecho a Crédito Fiscal | 2 | `factura-hospital-clinica-zona-franca` | No |
| 51 | Factura Engarrafadoras | Habilitada para empresas dedicadas a la recarga o llenado de gas en Garrafas y Contenedores | Con Derecho a Crédito Fiscal | 1 | `factura-engarrafadoras` | No |
| 52 | Factura Venta Minerales Banco Central ⚠ ELECTRÓNICA | Habilitada para la Venta de Minerales al Banco Central y solo en la modalidad electrónica | Sin Derecho a Crédito Fiscal | 2 | `factura-venta-mineral-banco-central` | No |
| 53 | Factura Importación y Comercialización de Lubricantes IEHD | Habilitada para empresas que importen de forma directa lubricantes y que comercialicen los mismos al consumidor final o distribuidor (No utilizada para servicios, solo venta de productos) | Con Derecho a Crédito Fiscal | 1 | `factura-importacion-y-comercializacion-de-lubricantes-iehd` | No |
| 54 | Factura Compra-Venta de Insumos para la Producción de Biodiésel y/o Diésel Ecológico | Habilitada para ventas en el mercado interno de productos de soya, cusi, totaí y otras especies cultivados o silvestres, y/o sus derivados, destinadas exclusivamente a la producción de biodiésel y/o diésel ecológico para las plantas de Yacimientos Petrolíferos Fiscales Bolivianos - YPFB | Sin Derecho a Crédito Fiscal | 2 | `factura-compra-venta-de-insumos-para-la-produccion-de-biodiesel-y-o-diesel-ecologico` | No |
| 55 | Factura Comercialización de Combustible | Habilitada para la venta de combustible para automotores. | Con Derecho a Crédito Fiscal | 1 | `factura-comercializacion-de-combustible` | No |

Observaciones sobre la lista:
- **Faltan los códigos 25, 26, 27 y 32**: no figuran en [TIPOS]. Su significado es **NO DOCUMENTADO**; habrá que tomarlo del catálogo sincronizado "Tipo Documento Sector".
- **Inconsistencia:** [TIPOS] asigna el **40** a "Factura de Servicios Básicos Zona Franca". Sin embargo, la página XSD de ese sector (`...__factura-servicios-basicos-zona-franca.md`, línea 497) dice "Para este tipo de factura este valor es 13". Hay que tomar como válido el catálogo sincronizado.
- El sector **52** (Venta Minerales Banco Central) se habilita "solo en la modalidad electrónica" (**⚠ ELECTRÓNICA**).
- En el CUF, el campo "TIPO DOCUMENTO SECTOR" tiene **2 dígitos** y se rellena con ceros a la izquierda (sector 1 → `01`) [CUF].
- Según [XSD-CV], la Factura de Compra y Venta usa `codigoDocumentoSector` = **1**, y en el XSD computarizado el elemento es `fixed="1"` [XSD-CV-FILE].

---

## 3. Sincronización de catálogos [SINC]

### 3.1 Obligación, frecuencia y alcance

- "Conforme a normativa vigente la sincronización de catálogos de facturación debe realizarse **diariamente** a través de los Servicios Web correspondientes" [SINC]. [SISINF] lo repite: "La sincronización de catálogos se realizará de forma diaria".
- La **fecha y hora** se sincronizan **obligatoriamente a diario**. Puede hacerse varias veces al día, y se recomienda hacerlo **antes de obtener el CUFD**. Esa fecha y hora se usa para "realizar los controles de plazos de envíos y registros" [SISINF].
- [EMI] sobre el despliegue: si el contribuyente tiene varias sucursales o puntos de venta, la sincronización puede hacerse **una sola vez con la Casa Matriz** cuando el esquema es **centralizado**; en cualquier otro caso, hay que sincronizar **por cada sucursal y/o punto de venta**.
- El objetivo es mantener actualizadas **localmente** las tablas paramétricas: productos y servicios, países, eventos significativos, mensajes de servicios, etc. [SINC].
- **Autenticación:** el consumo requiere un **Token Delegado** [SINC], que se obtiene en el Portal SIAT: "Gestión de Autorización de Sistemas Informáticos de Facturación", opción "(Piloto)" para piloto, "Token Delegado", "Generar Nuevo Ticket", NIT y duración [TOKEN].
  - Se envía en el **header HTTP** de cada solicitud: `apikey: TokenApi <token>`. En Java: `headers.put("apikey", Arrays.asList("TokenApi " + pToken));` [TOKEN].
  - Cuando el token caduca, se inactiva (botón X) y se genera uno nuevo [TOKEN].
- **Catálogos por ampliación**: el SIN agrega ítems a los catálogos (métodos de pago, unidades de medida y otros) sin ajustar los XSD publicados. "el contribuyente [debe] realizar los mismos directamente en sus sistemas" [VER2021]. Por eso **no conviene fijar en el código enums cerrados**: los valores válidos son los sincronizados.

### 3.2 Servicio y operaciones

- **Nombre del servicio SOAP, URL del WSDL y namespace: NO DOCUMENTADO** en [SINC] ni en ningún otro archivo descargado. No aparece ninguna URL de WSDL ni la palabra "wsdl" en el material.
- **Nombres exactos de las operaciones: NO DOCUMENTADO.** [SINC] solo nombra 18 catálogos en la cabecera de su tabla. La siguiente tabla los relaciona con los nombres de operación que trae el enunciado de este trabajo. Esa relación **no sale de la documentación** y debe verificarse contra el WSDL del ambiente piloto antes de programar.

| # | Catálogo según [SINC] | Hoja de [XLS-SINC] | Resultado esperado [XLS-SINC] | Nombre de operación (del enunciado, **NO DOCUMENTADO**) |
|---|---|---|---|---|
| 1 | Códigos de Actividades | Actividades | LISTADO TOTAL DE ACTIVIDADES | `sincronizarActividades` |
| 2 | Fecha y Hora | Fecha y Hora | FECHA Y HORA ACTUAL | `sincronizarFechaHora` |
| 3 | Códigos de Actividades Documento Sector | Actividades Documento Sector | LISTADO TOTAL DE ACTIVIDADES DOCUMENTO SECTOR | `sincronizarListaActividadesDocumentoSector` |
| 4 | Códigos de Leyendas Facturas | LeyendasFactura | LISTADO TOTAL DE LEYENDAS DE FACTURAS | `sincronizarListaLeyendasFactura` |
| 5 | Códigos de Mensajes Servicios | MensajesServicios | LISTADO TOTAL DE MENSAJES DE SERVICIOS | `sincronizarListaMensajesServicios` |
| 6 | Códigos de Productos y Servicios | ProductosServicios | LISTADO TOTAL DE PRODUCTOS Y SERVICIOS | `sincronizarListaProductosServicios` |
| 7 | Códigos de Eventos Significativos | EventosSignificativos | LISTADO TOTAL DE EVENTOS SIGNIFICATIVOS | `sincronizarParametricaEventosSignificativos` |
| 8 | Códigos de Motivos Anulación | MotivoAnulación | LISTADO TOTAL DE MOTIVO DE ANULACIÓN | `sincronizarParametricaMotivoAnulacion` |
| 9 | Códigos de País Origen | PaísOrigen | LISTADO TOTAL DE PAÍSES | `sincronizarParametricaPaisOrigen` |
| 10 | Códigos de Tipo Documento Identidad | TipoDocumentoIdentidad | LISTADO TOTAL DE TIPOS DE DOCUMENTO DE IDENTIDAD | `sincronizarParametricaTipoDocumentoIdentidad` |
| 11 | Códigos de Tipo Documento Sector | TipoDocumentoSector | LISTADO TOTAL DE TIPOS DE DOCUMENTO SECTOR | `sincronizarParametricaTipoDocumentoSector` |
| 12 | Códigos de Tipo Emisión | TipoEmisión | LISTADO TOTAL DE TIPO EMISIÓN | `sincronizarParametricaTipoEmision` |
| 13 | Códigos de Tipo Habitación | TipoHabitación | LISTADO TOTAL DE TIPO HABITACIÓN | `sincronizarParametricaTipoHabitacion` |
| 14 | Códigos de Tipo Método Pago | TipoMétodoPago | LISTADO TOTAL DE MÉTODO DE PAGO | `sincronizarParametricaTipoMetodoPago` |
| 15 | Códigos de Tipo Moneda | TipoMoneda | LISTADO TOTAL DE TIPOS DE MONEDA | `sincronizarParametricaTipoMoneda` |
| 16 | Códigos de Tipo Punto de Venta | TipoPuntoVenta | LISTADO TOTAL DE TIPOS DE PUNTO DE VENTA | `sincronizarParametricaTipoPuntoVenta` |
| 17 | Códigos de Tipo Factura | TipoFactura | LISTADO TOTAL DE TIPOS DE FACTURA | `sincronizarParametricaTiposFactura` |
| 18 | Códigos de Unidad de Medida | UnidadMedida | LISTADO TOTAL DE UNIDAD DE MEDIDA | `sincronizarParametricaUnidadMedida` |

Total: **18 operaciones**, y las 18 reciben exactamente los mismos parámetros de entrada [SINC].
[FASE1] menciona además un catálogo de "**modalidad**" entre los que se validan en la Etapa II, pero **no existe** una operación de "modalidad" en [SINC] ni en [XLS-SINC]. Queda como duda en la sección 8.

### 3.3 Parámetros de entrada (iguales para las 18 operaciones) [SINC]

La **agrupación** de estos campos en un objeto de solicitud, y el nombre de ese objeto, son **NO DOCUMENTADO** en [SINC]. Por analogía, otros servicios usan objetos como `SolicitudVerificarNit` [VNIT] y `SolicitudServicioRecepcionFactura` [RECEP], pero para sincronización no se documenta.

| Parámetro | Tipo | Obligatorio | Descripción y valores [SINC] | Longitud / rango |
|---|---|---|---|---|
| `codigoAmbiente` | Numérico | Sí | Producción = **1**; Pruebas y Piloto = **2** | NO DOCUMENTADO (1 dígito según los valores) |
| `codigoSistema` | Alfanumérico | Sí | Código de sistema asignado al hacer la solicitud de autorización | NO DOCUMENTADO |
| `nit` | Numérico | Sí | NIT del emisor de la factura | NO DOCUMENTADO en [SINC]. Como referencia, el CUF usa 13 dígitos [CUF] y el XSD `nitEmisor` va de 1 a 9999999999999 [XSD-CV-FILE] |
| `cuis` | Alfanumérico | Sí | Valor único por sucursal y/o punto de venta, obtenido en el inicio de uso del sistema | NO DOCUMENTADO |
| `codigoSucursal` | Numérico | Sí | Casa Matriz = **0**; sucursales = 1, 2, …, n | NO DOCUMENTADO en [SINC]. Como referencia, en el XSD va de 0 a 9999 [XSD-CV-FILE] y en el CUF ocupa 4 dígitos [CUF] |
| `codigoPuntoVenta` | Numérico | **No** | Se envía el número de punto de venta (1, 2, …, n) solo cuando la sincronización es para ese punto; si no, se envía **0** | NO DOCUMENTADO en [SINC]. Como referencia, en el XSD va de 0 a 9999 [XSD-CV-FILE] |

Reglas de validación locales, antes de llamar al servicio:
- `codigoAmbiente` ∈ {1, 2}. Si el ambiente no es válido, el SIAT responde **910**.
- `codigoSistema` no vacío. Si no es válido, el SIAT responde **911**; si el sistema no está asociado al contribuyente, **912**.
- `cuis` vigente y asociado a la sucursal o punto de venta. Errores posibles: **913**, **929**, **930**, **959**, **973**, **979**, **988**.
- `codigoSucursal` ≥ 0. Si no es válido, el SIAT responde **918**.
- `codigoPuntoVenta` ≥ 0 (0 = sin punto de venta). Si el punto no existe, el SIAT responde **933**.
- Token en el header. Si no es válido, el SIAT responde **989**.

### 3.4 Respuesta [SINC]

| Salida | Tipo | Descripción |
|---|---|---|
| Lista de Códigos | Alfanumérico | El listado del catálogo pedido. Su **estructura interna** (nombres de campos, tipos, longitudes) es **NO DOCUMENTADO** |
| Transaccion | Boolean | Indica si la transacción se procesó bien. En los casos de prueba el resultado esperado es `TRUE` [XLS-SINC] |
| Lista de Mensajes | Alfanumérico | Mensajes de respuesta. Su estructura interna (código y descripción) es **NO DOCUMENTADO** en [SINC]. Otros servicios devuelven `codigosRespuestas DTO[codigosRespuesta]` o `mensajes: Lista` [RECEP] [VNIT] |

Para "Fecha y Hora", el resultado esperado es "FECHA Y HORA ACTUAL" [XLS-SINC]. El **formato** del valor devuelto es **NO DOCUMENTADO** en el bloque. Como referencia, las fechas del XML usan el "formato UTC Extendido", por ejemplo `2020-02-15T08:40:12.215` [XSD-CV], y [FASE1] habla de un "formato UTC extendido sin zona horaria".

Como la estructura exacta no está documentada, M-INV debe modelar cada catálogo con un esquema **genérico**:
- `catalogo` (nombre)
- `codigo` (texto, para conservar los ceros a la izquierda de las actividades)
- `descripcion`
- `campos_extra` (JSON con cualquier otro campo que devuelva el servicio)
- `fecha_sincronizacion`
- `ambiente`, `sucursal`, `punto_venta`

### 3.5 Casos de prueba de la Etapa II, Sincronización de catálogos [XLS-SINC] [FASE1]

El libro tiene **18 hojas**, una por catálogo, en el mismo orden de la tabla 3.2, y cada hoja tiene **2 casos**.
Columnas: `NRO; CÓDIGO AMBIENTE; CÓDIGO SISTEMA; NIT; CUIS; SUCURSAL; CÓDIGO PUNTO VENTA; TRANSACCIÓN; RESULTADO ESPERADO`.

| NRO | Ambiente | Código sistema | NIT | CUIS | Sucursal | Punto venta | Transacción | Resultado esperado |
|---|---|---|---|---|---|---|---|---|
| 1 | 2 | su código de sistema | su NIT | su CUIS | 0 | **1** | TRUE | LISTADO TOTAL DE <catálogo> |
| 2 | 2 | su código de sistema | su NIT | su CUIS | 0 | **0** | TRUE | LISTADO TOTAL DE <catálogo> |

- En los dos casos de cada hoja se sincroniza la casa matriz (sucursal 0): el caso 1 **con punto de venta 1** y el caso 2 **sin punto de venta (0)**. Por eso **debe existir un punto de venta 1** registrado en piloto.
- [FASE1]: "Para esta etapa son **50 pruebas por cada caso** detallado en el apartado de seguimiento". Con 18 × 2 = 36 casos, eso da 1 800 llamadas. El total real no está explicado, así que queda **NO DOCUMENTADO**.
- [FASE1]: la Etapa II valida "la actualización de los datos contenidos en los catálogos de leyendas de factura, mensajes de servicios, eventos significativos, motivos de anulación, países, tipos de factura, documento de identidad, documento sector, emisión, métodos de pago, modalidad, moneda, unidad de medida y productos y servicios".
- La Etapa I (CUIS) va antes: sin CUIS no se puede sincronizar [FASE1].

### 3.6 Valores conocidos por catálogo

El valor definitivo **siempre** es el sincronizado. Aquí se listan solo los valores que aparecen en la documentación.

| Catálogo | Valores documentados | Fuente |
|---|---|---|
| Ambiente (no se sincroniza) | 1 = Producción; 2 = Pruebas y Piloto | [SINC] |
| Modalidad (no aparece como operación de sincronización) | 1 = Electrónica en Línea (**⚠ ELECTRÓNICA**); **2 = Computarizada en Línea** (la de M-INV); 3 = Portal Web en Línea (solo aparece en la tabla del CUF) | [CUF], [RECEP], [VNIT] |
| Tipo Emisión | 1 = Online; 2 = Offline; 3 = Masiva. Existe un tipo "CONTINGENCIA" en el catálogo sincronizado que es "para uso exclusivo del SIN" y **no debe usarse**. La recepción individual solo admite `codigoEmision` = 1 | [CUF], [EMI], [RECEP] |
| Tipo Factura / Documento | 1 con crédito fiscal; 2 sin crédito fiscal; 3 documento de ajuste; 4 documento equivalente | Sección 2.1 |
| Tipo Documento Sector | Tabla 2.2 (51 códigos documentados) | [TIPOS] |
| Tipo Documento Identidad | Rango **1 a 5** en la Factura de Compra y Venta (XSD `minInclusive 1`, `maxInclusive 5`). Solo está explícito **1 = CI** ("Ejemplo 1 que representa al CI"). Que **5 = NIT** se **deduce** de los XML de ejemplo, que usan 5 con números de 10 dígitos tipo NIT (`facturaComputarizadaAlquilerBienInmueble.xml`, `facturaComputarizadaTasaCero.xml`). **2, 3 y 4** (se suponen CEX, Pasaporte y Otro Documento) son **NO DOCUMENTADO**. Los mensajes 1010/2003 ("CI/NIT/CEX") confirman que existen CI, NIT y CEX. En los documentos de ajuste y en Hidrocarburos el rango documentado es de **1 a 9** | [XSD-CV] (rango de 1 a 5), [XSD-CV-FILE], `...__alquiler-zona-franca.md` y `...__factura-alcanzada-por-ice.md` ("Ejemplo 1 que representa al CI"), `...__nota-credito-debito.md` y `...__factura-de-hidrocarburos.md` (rango de 1 a 9) |
| Tipo Método Pago | 1 = Efectivo; 2 = Tarjeta (exige `numeroTarjeta` ofuscado: los 4 primeros y 4 últimos dígitos en claro y ceros al medio, por ejemplo `4797000000007896`); 5 = Otros ("solo si el método utilizado no esta disponible"). Rango del XSD: **1 a 308**. [VER2021] dice que "se adicionaron nuevos métodos de pago y combinaciones". El resto de los valores es **NO DOCUMENTADO** | [XSD-CV], [XSD-CV-FILE], [VER2021] |
| Tipo Moneda | Rango del XSD: **1 a 154**. Los XML de ejemplo usan `codigoMoneda` = 1 con `tipoCambio` = 1 y `montoTotalMoneda` = `montoTotal`, lo que **sugiere** que 1 = Boliviano, pero no está dicho de forma explícita. Regla: "si el código de moneda es boliviano [tipoCambio] deberá ser igual a 1" y `montoTotalMoneda` = `montoTotal` | [XSD-CV], [XSD-CV-FILE] |
| Unidad de Medida | **58 = Unidad (Servicios)**, que se usa con cantidad = 1 cuando se vende un servicio. Rango del XSD: **1 a 200**. Los ejemplos usan 1, cuyo significado es **NO DOCUMENTADO**. El resto de los valores es **NO DOCUMENTADO** | [XSD-CV], [XSD-CV-FILE] |
| Motivo Anulación | Se usa en el parámetro `codigoMotivo` (Numérico, obligatorio) de la anulación. Los **valores** son **NO DOCUMENTADO** en el material | página de anulación computarizada |
| Eventos Significativos | La página de contingencia enumera 7 eventos: 1) Corte del servicio de Internet; 2) Inaccesibilidad al Servicio Web de la Administración Tributaria; 3) Ingreso a zonas sin Internet por despliegue de puntos de venta; 4) Venta en lugares sin internet; 5) Virus informático o falla de software; 6) Cambio de infraestructura de sistema o falla de hardware; 7) Corte de suministro de energía eléctrica. Que esa numeración coincida con los **códigos** del catálogo es **NO DOCUMENTADO**: hay que confirmarlo con la sincronización | [CONT] |
| País Origen | Valores **NO DOCUMENTADO**. El único ejemplo es el JSON de huéspedes del sector Hoteles, con `"codigoPais":"1"` | páginas XSD de hoteles |
| Tipo Punto de Venta | 1 = Punto Venta Comisionista; 2 = Punto Venta Ventanilla de Cobranza; 3 = Punto de Venta Móviles; 4 = Punto de Venta YPFB; 5 = Punto de Venta Cajeros; 6 = Punto de Venta Conjunta | [RPV] |
| Tipo Habitación | Valores **NO DOCUMENTADO**. Solo aplica al sector Hoteles y no le sirve a M-INV | n/a |
| Leyendas Factura | Ejemplo: "Ley N° 453: Tienes derecho a recibir información sobre las características y contenidos de los servicios que utilices." El campo `leyenda` es obligatorio y tiene entre 1 y 200 caracteres; es la "Leyenda asociada a la actividad económica". [FASE2]: la **segunda** leyenda al pie debe cambiar **aleatoriamente con cada emisión** (Ley del consumidor N° 453) y la **tercera** leyenda debe cambiar entre "en linea" y "fuera de linea" según la forma de operación | [XSD-CV], [XSD-CV-FILE], [FASE2] |
| Mensajes Servicios | Valores **NO DOCUMENTADO** en el bloque. Probablemente coinciden con la tabla de la sección 4, pero eso tampoco está documentado | [SINC] |
| Actividades | Actividades económicas del NIT (códigos CAEB). En la factura, `actividadEconomica` es **texto** de 1 a 10 caracteres. El catálogo de productos usa **códigos de 7 dígitos, con ceros a la izquierda** (337 actividades empiezan con "0"), así que hay que guardarlos como `string` | [XSD-CV-FILE], [XLS-PROD] |
| Actividades Documento Sector | Relaciona la actividad con el documento sector. Estructura **NO DOCUMENTADO** | [SINC] |
| Productos y Servicios | `codigoProductoSin` es un entero de 1 a 99 999 999. En el catálogo actual son códigos de 7 dígitos, de 1000000 a 1004803 (sección 6) | [XSD-CV-FILE], [XLS-PROD] |
| Códigos especiales de cliente | **99001** = consulados, embajadas, etc.; **99002** = Control Tributario; **99003** = Ventas Menores del Día. Se envían con tipo de documento NIT y `codigoExcepcion` = 1 | [EMI] |
| `codigoExcepcion` (no es catálogo) | 0 o nulo por defecto; **1** solo si el tipo de documento es NIT y se pide al SIN que no lo valide. En la emisión **fuera de línea** con NIT se envía **siempre 1**. El XSD admite de 0 a 1 | [XSD-CV], [EMI], [CONT] |

---

## 4. Códigos de error y estado del SIAT [ERR]

[ERR] los presenta como "Algunos de los codigos de error utilizados por el SIAT". Por lo tanto **la lista puede no estar completa**.
Tiene **193 códigos**: 123; 901 a 1061 sin huecos (161 códigos); 2000 a 2019 (20); 3000 a 3010 (11). No hay duplicados.

Las columnas "Categoría" y "Ámbito" son una **clasificación propia** para implementar, no del SIN:
- **Estado**: resultado de un proceso, que no es necesariamente un error.
- **Advertencia**: el texto empieza con "Advertencia".
- **Marca/Bloqueo**: restricciones del padrón del contribuyente.
- **Error**: rechazo.

Ámbitos: **⚠ ELECTRÓNICA** (firma digital), CONTINGENCIA/paquetes (paquetes fuera de línea, CAFC, eventos significativos), MASIVA, ANULACIÓN/REVERSIÓN, NIT, "Sector específico / notas" (cálculos de sectores especiales como ICE, IEHD, juegos o conciliación, y notas de crédito-débito) y GENERAL.

| Código | Descripción (literal [ERR]) | Categoría | Ámbito |
|---|---|---|---|
| 123 | Código Único De Facturación Diaria (Cufd) Fuera De Tolerancia | Error | GENERAL |
| 901 | Recepción Pendiente | Estado | GENERAL |
| 902 | Recepción Rechazada | Estado | GENERAL |
| 903 | Recepción Procesada | Estado | GENERAL |
| 904 | Recepción Observada | Estado | GENERAL |
| 905 | Anulación Confirmada | Estado | ANULACIÓN/REVERSIÓN |
| 906 | Anulación Rechazada | Estado | ANULACIÓN/REVERSIÓN |
| 907 | Reversión De Anulación Confirmada | Estado | ANULACIÓN/REVERSIÓN |
| 908 | Recepción Validada | Estado | GENERAL |
| 909 | Reversión De Anulación Rechazada | Estado | ANULACIÓN/REVERSIÓN |
| 910 | El Parámetro Ambiente Es Invalido | Error | GENERAL |
| 911 | El Parámetro Código De Sistema Es Invalido | Error | GENERAL |
| 912 | El Sistema No Esta Asociado Al Contribuyente | Error | GENERAL |
| 913 | Código Único De Inicio De Sistema (Cuis) Invalido | Error | GENERAL |
| 914 | Código Único De Facturación Diaria (Cufd) Invalido | Error | GENERAL |
| 915 | El Parámetro Tipo Factura Documento Es Invalido | Error | GENERAL |
| 916 | El Parámetro Tipo De Emisión Es Invalido | Error | GENERAL |
| 917 | El Parámetro Modalidad Es Invalido | Error | GENERAL |
| 918 | El Parámetro Sucursal Es Invalido | Error | GENERAL |
| 919 | El Parámetro NIT Es Invalido | Error | GENERAL |
| 920 | El Parámetro Archivo Es Invalido | Error | GENERAL |
| 921 | El Firmado Del XML Es Incorrecto | Error | ⚠ ELECTRÓNICA (firma) |
| 922 | La Firma Del XML No Corresponde Al Contribuyente | Error | ⚠ ELECTRÓNICA (firma) |
| 923 | El Parámetro Código De Recepción Es Invalido | Error | GENERAL |
| 924 | La Factura o Nota, No Existe En La Base De Datos Del Sin | Error | GENERAL |
| 925 | El Parámetro Motivo De Anulación Es Invalido | Error | ANULACIÓN/REVERSIÓN |
| 926 | Comunicación Exitosa | Estado (OK) | GENERAL |
| 927 | El Certificado De La Firma Es Invalido | Error | ⚠ ELECTRÓNICA (firma) |
| 928 | El Certificado Se Encuentra Revocado | Error | ⚠ ELECTRÓNICA (firma) |
| 929 | El Código Único De Inicio De Sistema (Cuis) No Esta Vigente | Error | GENERAL |
| 930 | El Código Único De Inicio De Sistema (Cuis) No Corresponde A La Sucursal/Punto Venta | Error | GENERAL |
| 931 | El Parámetro Código Documento Sector Es Invalido | Error | GENERAL |
| 932 | El Parámetro Código Documento Sector No Corresponde Al Servicio | Error | GENERAL |
| 933 | El Punto De Venta Es Inexistente o Invalido | Error | GENERAL |
| 934 | La Solicitud De Anulación De La Factura o Nota De Crédito-Débito Se Encuentra Fuera De Plazo | Error | ANULACIÓN/REVERSIÓN |
| 935 | El Parámetro Fecha De Envío Es Invalido | Error | GENERAL |
| 936 | La Factura o Nota De Crédito-Débito Ya Se Encuentra Anulada | Error | ANULACIÓN/REVERSIÓN |
| 937 | El NIT No Tiene Asociado La Modalidad De Facturación | Error | GENERAL |
| 938 | El NIT Presenta Marcas De Control | Marca/Bloqueo | GENERAL |
| 939 | La Factura o Nota De Crédito - Débito No Cumple Con El Formato Del Xsd Especificado | Error | GENERAL |
| 940 | El NIT No Tiene Habilitado El Documento Sector | Error | GENERAL |
| 941 | La Factura o Nota De Crédito - Débito No Se Encuentra Disponible Para Ser Anulada | Error | ANULACIÓN/REVERSIÓN |
| 942 | El Código De Recepción De Evento Significativo No Se Encuentra En La Base De Datos Del Sin | Error | CONTINGENCIA/paquetes |
| 943 | El Formato De La Fecha De Envío Es Incorrecto | Error | GENERAL |
| 944 | El Código De Recepción No Se Encuentra En La Base De Datos Del Sin | Error | GENERAL |
| 945 | El Estado De Recepción De La Anulación Es Incorrecta | Error | ANULACIÓN/REVERSIÓN |
| 946 | El Código Único De Factura (Cuf) No Existe En Base De Datos Del Sin | Error | GENERAL |
| 947 | El Parámetro Tipo De Punto De Venta Es Invalido | Error | GENERAL |
| 948 | El Parámetro Nombre De Punto De Venta No Puede Ser Vacío | Error | GENERAL |
| 949 | El Parámetro Descripción De Punto De Venta No Puede Ser Vacío | Error | GENERAL |
| 950 | El Parámetro Código De Evento Significativo No Puede Ser Vacío | Error | GENERAL |
| 951 | El Parámetro Descripción De Evento Significativo No Puede Ser Vacío | Error | GENERAL |
| 952 | El Código Único De Factura (Cuf) Ya Se Encuentra Registrado En La Base De Datos Del Sin | Error | GENERAL |
| 953 | El Código Único De Facturación Diaria (Cufd) No Se Encuentra Vigente | Error | GENERAL |
| 954 | La Cantidad De Facturas En El Paquete Emitido Por Contingencia Ha Excedido El Máximo Permitido | Error | CONTINGENCIA/paquetes |
| 955 | No Existe Registro Para Autorizar El Proceso Masivo | Error | MASIVA |
| 956 | La Cantidad De Facturas En El Paquete Emitido Masivamente Ha Excedido El Máximo Permitido | Error | MASIVA |
| 957 | No Existe Registro De Evento Significativo En La Base De Datos Del Sin | Error | CONTINGENCIA/paquetes |
| 958 | El Usuario No Se Encuentra Autorizado Para Consumir Este Servicio | Error | GENERAL |
| 959 | El Código Único De Inicio De Sistema (Cuis) No Se Encuentra Asociado Al Sistema | Error | GENERAL |
| 960 | El Parámetro Fin De Evento Es Requerido | Error | CONTINGENCIA/paquetes |
| 961 | El NIT Tiene Marca De Domicilio Inexistente | Marca/Bloqueo | GENERAL |
| 962 | El NIT Tiene Bloqueo De Dosificación Originado En Fiscalización | Marca/Bloqueo | GENERAL |
| 963 | El NIT Tiene Bloqueo De Dosificación Originado En Jurídica | Marca/Bloqueo | GENERAL |
| 964 | El NIT No Cumple Con Obligatoriedad De Presentación De DDJJ | Marca/Bloqueo | GENERAL |
| 965 | El Contribuyente No Cuenta Con Firma Vigente Registrada | Error | ⚠ ELECTRÓNICA (firma) |
| 966 | No Se Puede Recuperar Los Datos Del Contribuyente | Error | GENERAL |
| 967 | Tiempo De Espera Agotado Para Conexión A Base De Datos | Error | GENERAL |
| 968 | La Anulación De La Factura o Nota De Crédito - Débito Ya Se Encuentra Revertida | Error | ANULACIÓN/REVERSIÓN |
| 969 | El Parámetro Hash Es Invalido | Error | GENERAL |
| 970 | El Cuis En La Base De Datos Se Encuentra Vigente, No Puede Solicitar Otro | Error | GENERAL |
| 971 | El Tamaño Del Archivo Excede El Tamaño Permitido De 100 Mb | Error | GENERAL |
| 972 | La Cantidad De Facturas Enviada En El Paquete Es Mayor A La Definida En La Normativa | Error | MASIVA |
| 973 | El Código Único De Inicio De Sistema (Cuis) No Se Encuentra Vigente | Error | GENERAL |
| 974 | El Rango De Fechas Del Evento Significativo Para Registrar Es Inválido | Error | CONTINGENCIA/paquetes |
| 975 | El Sistema No Se Encuentra Autorizado O Se Encuentra Observado | Error | GENERAL |
| 976 | El Código Del Evento Es Incorrecto | Error | CONTINGENCIA/paquetes |
| 977 | No Existen Actividades Asociadas Al NIT | Error | GENERAL |
| 978 | Reversión De La Factura o Nota De Crédito/Débito Confirmada | Estado | ANULACIÓN/REVERSIÓN |
| 979 | El Cuis No Se Encuentra Asociado Al Sistema O A La Sucursal | Error | GENERAL |
| 980 | Existe Un Cuis Vigente Para La Sucursal O Punto De Venta | Error | GENERAL |
| 981 | Rango De Fechas De Evento Significativo Invalido | Error | CONTINGENCIA/paquetes |
| 982 | No Existe Puntos De Venta Asociados | Error | GENERAL |
| 983 | La Fecha De Envío Del Paquete Esta Fuera De Plazo | Error | CONTINGENCIA/paquetes |
| 984 | El Evento Significativo No Corresponde Al Cufd Del Evento Registrado | Error | CONTINGENCIA/paquetes |
| 985 | La Cantidad De Facturas Es Diferente A La Declarada | Error | MASIVA |
| 986 | NIT Activo | Estado NIT | NIT |
| 987 | NIT Inactivo | Estado NIT | NIT |
| 988 | Código Único De Inicio De Sistema (Cuis) Fuera De Tolerancia | Error | GENERAL |
| 989 | Token Invalido | Error | GENERAL |
| 990 | El Cliente No Tiene Actividades Relacionadas Al Sector Que Intenta Asociar | Error | GENERAL |
| 991 | Error En Base De Datos | Error | GENERAL |
| 992 | Error Servicio Padrón | Error | GENERAL |
| 993 | La Fecha De Envío Esta Fuera De Plazo | Error | GENERAL |
| 994 | NIT Inexistente | Error | NIT |
| 995 | Servicio No Disponible | Error | GENERAL |
| 996 | Rango De Fechas Invalido | Error | GENERAL |
| 997 | El Nombre Excede El Limite De Caracteres Permitidos | Error | GENERAL |
| 998 | La Descripción Excede El Limite De Caracteres Permitidos | Error | GENERAL |
| 999 | Error En La Ejecución Del Servicio | Error | GENERAL |
| 1000 | El Cuf Enviado Ya Existe En La Base De Datos Del Sin | Error | GENERAL |
| 1001 | El NIT Enviado En El XML Es Inexistente O No Corresponde Al Cufd | Error | GENERAL |
| 1002 | El Código Único De Factura (Cuf) Enviado En El XML Es Invalido | Error | GENERAL |
| 1003 | El Código Único De Facturación Diaria (Cufd) Enviado En El XML Es Invalido | Error | GENERAL |
| 1004 | La Sucursal Enviada En El XML No Corresponde A Los Datos Del Cufd | Error | GENERAL |
| 1005 | La Factura o Nota De Crédito-Débito No Puede Ser Emitida Al Mismo Emisor | Error | GENERAL |
| 1006 | El Cufd Enviado No Corresponde Al Evento Asociado Al Paquete Enviado | Error | CONTINGENCIA/paquetes |
| 1007 | La Dirección Enviada En El XML No Corresponde A La Registrada En Padrón | Error | GENERAL |
| 1008 | El Punto De Venta Enviado En El XML Es Inexistente O Invalido | Error | GENERAL |
| 1009 | La Fecha De Emisión Enviada En El XML No Es Valida Para Emisión En Linea | Error | GENERAL |
| 1010 | La Factura No Puede Ser Enviada Con Numero De CI/NIT/CEX o Para Montos Mayores A 3000 | Error | GENERAL |
| 1011 | El Complemento Solo Puede Ser Enviado Cuando El Tipo De Documento Es Carnet De Identidad | Error | GENERAL |
| 1012 | El Numero De Tarjeta Solo Puede Ser Enviado Cuando El Método De Pago Sea Con Tarjeta | Error | GENERAL |
| 1013 | El Calculo Del Monto Total Es Erróneo | Error | GENERAL |
| 1014 | El Calculo Del Monto Total Moneda Es Erróneo | Error | GENERAL |
| 1015 | El Calculo Del Importe Base Para Crédito Fiscal Es Erróneo | Error | GENERAL |
| 1016 | El Código De Actividad Económica No Esta Habilitada Para El Contribuyente | Error | GENERAL |
| 1017 | El Código De Producto No Esta Relacionado A Ninguna Actividad Económica Del Contribuyente | Error | GENERAL |
| 1018 | El Calculo Del Subtotal Es Erróneo | Error | GENERAL |
| 1019 | El Calculo De Ice Especifico Es Erróneo | Error | Sector específico / notas |
| 1020 | El Calculo De Ice Porcentual Es Erróneo | Error | Sector específico / notas |
| 1021 | El Monto Ice Especifico Es Erróneo | Error | Sector específico / notas |
| 1022 | El Monto Ice Porcentual Es Erróneo | Error | Sector específico / notas |
| 1023 | El Código Nandina Enviado En La Factura Es Erróneo | Error | Sector específico / notas |
| 1024 | La Sumatoria De Lo Detalles Es Errónea | Error | GENERAL |
| 1025 | El Monto Sujeto A Crédito Fiscal Ley 317 Es Erróneo | Error | Sector específico / notas |
| 1026 | El Monto Total Sujeto Al Impuesto Del Juego (IJ) Es Erróneo | Error | Sector específico / notas |
| 1027 | El Monto De Diferencia De Cambios Es Erróneo | Error | Sector específico / notas |
| 1028 | El Monto De IVA Enviado Es Erróneo | Error | GENERAL |
| 1029 | El Monto Total Devuelto Enviado Es Erróneo | Error | Sector específico / notas |
| 1030 | El Monto Total Original Enviado Es Erróneo | Error | Sector específico / notas |
| 1031 | El Monto Efectivo De Crédito O Débito Devuelto Enviado Es Erróneo | Error | Sector específico / notas |
| 1032 | El Monto Total De Impuesto A La Participación En Juego (IPJ) Es Erróneo | Error | Sector específico / notas |
| 1033 | El Monto Devuelto Es Mayor Al Monto Original | Error | Sector específico / notas |
| 1034 | La Fecha De Emisión Es Menor Al Periodo Anterior | Error | GENERAL |
| 1035 | Formato De Fecha Incorrecta | Error | GENERAL |
| 1036 | Nominatividad Incorrecta Para Nombre/Razón Social | Error | GENERAL |
| 1037 | El Numero Documento De Tipo NIT No Es Valido | Error | NIT |
| 1038 | NIT Conjunto No Valido | Error | NIT |
| 1039 | Fecha Emisión Para Envío Masivo Incorrecto | Error | MASIVA |
| 1040 | Fecha De Emisión No Se Encuentra En El Rango De Contingencia | Error | CONTINGENCIA/paquetes |
| 1041 | La Fecha De Emisión No Se Encuentra Dentro Del Plazo Establecido En Norma | Error | GENERAL |
| 1042 | El NIT Del Medico Enviado No Es Valido | Error | Sector específico / notas |
| 1043 | El Monto Conciliado Enviado Es Erróneo | Error | Sector específico / notas |
| 1044 | El Monto Total Conciliado Enviado Es Erróneo | Error | Sector específico / notas |
| 1045 | Valor De Cafc No Valido Para La Factura | Error | CONTINGENCIA/paquetes |
| 1046 | Fecha Emisión Para El Cafc Enviado Incorrecto | Error | CONTINGENCIA/paquetes |
| 1047 | Numero Factura Para El Cafc Enviado Incorrecto | Error | CONTINGENCIA/paquetes |
| 1048 | Factura De La Nota Crédito Débito No Encontrada | Error | Sector específico / notas |
| 1049 | Detalle De La Nota Diferente Al Detalle De La Factura Original | Error | Sector específico / notas |
| 1050 | Monto Gift Card No Corresponde Al Método De Pago | Error | GENERAL |
| 1051 | Fecha De Factura Incorrecta | Error | GENERAL |
| 1052 | El Calculo Del Monto IEHD Es Erróneo | Error | Sector específico / notas |
| 1053 | La Actividad De La Nota De Crédito Débito No Se Encuentra Autorizada Para Este Plazo | Error | Sector específico / notas |
| 1054 | El Monto Descuento Crédito Débito Es Erróneo | Error | Sector específico / notas |
| 1055 | El Monto Tarifa Es Erróneo | Error | Sector específico / notas |
| 1056 | El Tipo De Cambio Es Erróneo | Error | GENERAL |
| 1057 | El Monto Total Moneda Es Erróneo | Error | GENERAL |
| 1058 | El Monto Total Sujeto Iva Es Erróneo | Error | GENERAL |
| 1059 | La Razón Social Es Errónea | Error | GENERAL |
| 1060 | El Monto Detalle Es Erróneo | Error | GENERAL |
| 1061 | Factura De La Nota Crédito Débito No Es Valida Para Realizar La Devolución | Error | Sector específico / notas |
| 2000 | Advertencia: El Numero Factura Enviado Tiene Error De Correlatividad | Advertencia | GENERAL |
| 2001 | Advertencia: La Fecha De Emisión Enviada No Se Encuentra Dentro Del Rango Del Evento De Contingencia Asociado | Advertencia | CONTINGENCIA/paquetes |
| 2002 | Advertencia: La Fecha De Emisión Enviada No Es Valida Para La Emisión Masiva | Advertencia | MASIVA |
| 2003 | Advertencia: La Factura No Puede Ser Enviada Con Numero De CI/NIT/CEX o Para Montos Mayores A 3000 | Advertencia | GENERAL |
| 2004 | Advertencia: El Complemento Solo Puede Ser Enviado Cuando El Tipo De Documento Es Carnet De Identidad | Advertencia | GENERAL |
| 2005 | Advertencia: El NIT Del Cliente Enviado En El Campo Numero De Documento No Es Valido | Advertencia | NIT |
| 2006 | Advertencia: El Numero De Tarjeta Solo Puede Ser Enviado Cuando El Método De Pago Sea Con Tarjeta | Advertencia | GENERAL |
| 2007 | Advertencia: El Calculo Del Monto Total Es Erróneo | Advertencia | GENERAL |
| 2008 | Advertencia: El Calculo Del Monto Total Moneda Es Erróneo | Advertencia | GENERAL |
| 2009 | Advertencia: El Calculo Del Importe Base Para Crédito Fiscal Es Erróneo | Advertencia | GENERAL |
| 2010 | Advertencia: El Código De Actividad Económica No Esta Habilitada Para El Contribuyente | Advertencia | GENERAL |
| 2011 | Advertencia: El Código De Producto No Esta Relacionado A Ningún Actividad Económica Del Contribuyente | Advertencia | GENERAL |
| 2012 | Advertencia: El Calculo Del Subtotal Es Erróneo | Advertencia | GENERAL |
| 2013 | Advertencia: La Factura o Nota De Crédito-Débito No Puede Ser Emitida Al Mismo Emisor | Advertencia | GENERAL |
| 2014 | Advertencia: La Dirección Enviada En El XML No Corresponde A La Registrada En Padrón | Advertencia | GENERAL |
| 2015 | Advertencia: El Calculo De Ice Especifico Es Erróneo | Advertencia | Sector específico / notas |
| 2016 | Advertencia: El Calculo De Ice Porcentual Es Erróneo | Advertencia | Sector específico / notas |
| 2017 | Advertencia: El Monto Ice Especifico Es Erróneo | Advertencia | Sector específico / notas |
| 2018 | Advertencia: El Monto Ice Porcentual Es Erróneo | Advertencia | Sector específico / notas |
| 2019 | Advertencia: El Código Nandina Enviado En La Factura Es Erróneo | Advertencia | Sector específico / notas |
| 3000 | El NIT No Tiene Contrato Vigente | Marca/Bloqueo | GENERAL |
| 3001 | La Categoría De Contrato No Corresponde Al Sector | Marca/Bloqueo | GENERAL |
| 3002 | La Solicitud Excede El Limite De Cufd Masivo Permitido | Marca/Bloqueo | MASIVA |
| 3003 | Marca: No Tiene Formularios 200 Y 210 Vigentes | Marca/Bloqueo | GENERAL |
| 3004 | Marca: Domicilio Inexistente | Marca/Bloqueo | GENERAL |
| 3005 | Marca: Bloqueo De Dosificación Originados En Fiscalización | Marca/Bloqueo | GENERAL |
| 3006 | Marca: Bloqueo De Dosificación Originados En Jurídica | Marca/Bloqueo | GENERAL |
| 3007 | Marca: No Cumple Con Obligatoriedad De Presentación De DDJJ | Marca/Bloqueo | GENERAL |
| 3008 | Advertencia: El Cuis Esta A Punto De Caducar, Genere Un Nuevo Cuis Por Favor | Advertencia | GENERAL |
| 3009 | El Tamaño Del Archivo Es Mayor A La Definida En Norma | Error | GENERAL |
| 3010 | La Factura Ya Se Encuentra Utilizada o Consolidada | Error | GENERAL |

Estados que devuelven los servicios de recepción y validación, según [EMI]:
- La recepción de un paquete devuelve **901** (pendiente) con `transaccion` = true y un código de recepción.
- La validación devuelve **901** (pendiente), **904** (observada) o **908** (validada).
- Si hay observaciones, la respuesta trae una lista de mensajes con código, descripción, número de archivo y número de detalle.
- **902** = Rechazada.
- Anulación: **905** confirmada, **906** rechazada.
- Reversión de anulación: **907** confirmada, **909** rechazada.

---

## 5. Homologación de productos y servicios [HOM]

### 5.1 Proceso

1. El sistema de facturación del contribuyente **descarga el listado** de productos y servicios mediante el servicio web ("Códigos de Productos y Servicios").
2. Un "**Equipo de homologación**", personal que define el sujeto pasivo, busca para **cada producto propio** su equivalente entre los códigos genéricos descargados y los **relaciona**.

Ejemplo de [HOM]. El ejemplo usa códigos antiguos de 4 dígitos y CAEB de 6 dígitos; el catálogo vigente usa 7 dígitos (ver la sección 6).

| Listado SIN: Código | Descripción | CAEB |
|---|---|---|
| 1111 | Semillas de trigo, para siembra | 106110 |
| 1377 | Nueces del Brasil con cáscara | 463010 |

| Homologación: Código interno | Descripción | Código SIN |
|---|---|---|
| 12384E1 | Nuez embolsada de 50gr | 1377 |
| 4654D3 | Nuez procesada con sal de 100gr | 1377 |

Reglas:
- **Varios productos internos** pueden apuntar al **mismo** código SIN (relación N a 1).
- **Nota 1:** en el **XML** de la factura va el **código de producto del SIN** (`codigoProductoSin`). En la **representación gráfica** se muestra el **código interno** de la empresa (`codigoProducto`: texto de 1 a 50 caracteres [XSD-CV-FILE]).
- Al homologar hay que considerar si el producto es **importado o nacional**, "considerando que el código de producto en el SIN diferencia ambos conceptos". En el catálogo actual ([XLS-PROD]) **ninguna** descripción de producto distingue entre importado y nacional. Solo hay 2 actividades de "Importación" (4730150 y 4730210, ambas de combustibles y lubricantes). Además, [EMI] dice: "No se puede realizar emisión de facturas utilizando para ello actividades económicas de Importación". Ver la sección 8.
- La factura también exige que el producto esté ligado a una actividad del contribuyente. Errores relacionados: **1016** (actividad no habilitada), **1017** (producto no relacionado con ninguna actividad del contribuyente) y las advertencias **2010** y **2011** [ERR].

---

## 6. Análisis del catálogo de productos del SIN [XLS-PROD]

### 6.1 Estructura

- Archivo `Catalogo Productos.xls` (formato BIFF de Excel 97-2003, última edición el 23/10/2025), con una hoja, "Catalogo Productos", en el rango `A1:D21556`.
- Tiene **21 555 filas de datos** más la cabecera. Todas las filas tienen 4 columnas.

| Col | Cabecera exacta | Tipo observado | Notas |
|---|---|---|---|
| A | `CÓDIGO PRODUCTO` | 7 dígitos numéricos, de 1000000 a 1004803 | 4 798 códigos distintos |
| B | `DESCRIPCIÓN PRODUCTO` | Texto de hasta 657 caracteres | 7 941 filas empiezan con espacios, así que hay que aplicar **Trim**. El uso de mayúsculas y el punto final no son uniformes |
| C | `CÓDIGO ACTIVIDAD` | 7 dígitos guardados como **texto** | 2 840 actividades distintas; 337 empiezan con "0" (por ejemplo 0111100) |
| D | `DESCRIPCIÓN ACTIVIDAD` | Texto de hasta 183 caracteres | Algunas actividades tienen dos variantes de guion ("–" y "-") en la descripción |

- El catálogo es una **relación N a N** entre producto y actividad: la misma fila de producto se repite para cada actividad donde es válida. Un producto llega a estar en 22 actividades, y una actividad llega a tener 207 productos (2011000).
- Hay 21 550 pares distintos (producto, actividad): **5 pares aparecen dos veces con descripciones distintas**, todos en la actividad 1079305: 1004799 a 1004803, por ejemplo 1004801 "ají." y "Pimentón". Por eso **no se puede imponer unicidad estricta** sobre (actividad, producto, descripción).
- 35 códigos de producto tienen más de una descripción, en general por diferencias de mayúsculas o del punto final.
- Muestra (primeras filas):

```
CÓDIGO PRODUCTO;DESCRIPCIÓN PRODUCTO;CÓDIGO ACTIVIDAD;DESCRIPCIÓN ACTIVIDAD
1000011;  amaranto.;0111100;Cultivo de cereales
1000010;  arroz con cascara.;0111100;Cultivo de cereales
1000006;  avena.;0111100;Cultivo de cereales
...
1004661;Actividades de misiones diplomáticas y consulares ...;9900200;Actividades de misiones diplomáticas y consulados
```

### 6.2 Patrón de los dos últimos dígitos de la actividad

Esto se **observó en los datos**: el SIN **no lo documenta**, y hay algunas excepciones.

| Sufijo | Variante | Cantidad |
|---|---|---|
| x0 (00, 10, 20, …) | Actividad base | 889 con sufijo 00 |
| x2 | "... para Exportación" | 307 con sufijo 02 |
| x3 | "... en Zona Franca" | 768 con sufijo 03 |
| x4 | "... – Entidad Pública" | 66 con sufijo 04, y 9 excepciones que no lo son |
| x5 | "... - Empresa Pública" | 542 con sufijo 05 |

Para una ferretería privada que no está en zona franca, la actividad es la **base (…00)**. De todos modos, **la que vale es la actividad registrada en el padrón del NIT**, que se obtiene con la sincronización de "Actividades".

### 6.3 Actividades de ferretería y construcción encontradas

| Código | Descripción de la actividad | N° productos | Comentario |
|---|---|---|---|
| **4752100** | Venta al por menor de artículos de ferretería, fontanería y calefacción | 24 | **Principal para una ferretería minorista** |
| **4752400** | Venta al por menor de ladrillo, madera, cemento y otros materiales de construcción | 27 | **Principal para materiales de construcción** |
| **4752200** | Venta al por menor de pinturas, barnices y lacas | 29 | Pinturas |
| **4752300** | Venta al por menor de vidrio plano, espejos y sus productos | 20 | Vidriería |
| 4759200 | Venta al por menor de artículos de iluminación | 14 | Material eléctrico e iluminación |
| 4753300 | Venta al por menor de cubrimientos para paredes y pisos | 1 | |
| 4759100 | Venta al por menor de enseres eléctricos y línea blanca | 44 | Si también vende electrodomésticos |
| 4663100 | Venta al por mayor de materiales de construcción | 25 | Mayorista |
| 4663200 | Venta al por mayor de artículos sanitarios | 5 | Mayorista |
| 4663300 | Venta al por mayor de artículos prefabricados de cemento y estuco | 20 | Mayorista |
| 4663400 | Venta al por mayor de productos para la construcción de acero y aluminio | 22 | Mayorista |
| 4663500 | Venta al por mayor de madera sin desbastar, puertas, ventanas, marcos y productos primarios | 17 | Mayorista |
| 4663600 | Venta al por mayor de artículos de ferretería, fontanería y calefacción | 24 | Mayorista |
| 4663700 | Venta al por mayor de pintura, barnices y productos conexos | 29 | Mayorista |
| 4663800 | Venta al por mayor de vidrio, espejos y sus productos | 19 | Mayorista |
| 4610500 | Venta al por mayor a cambio de una retribución o por contrato de madera, muebles y materiales de construcción | 3 | Intermediación |
| 4659300 | Venta al por mayor de maquinaria, equipo de minería y construcción | 24 | |
| 7730800 | Alquiler de motores, máquinas y herramientas en general | 1 | Si alquila herramientas |
| 2593200 | Fabricación de herramientas de mano y artículos de ferretería | 12 | Fabricación, no aplica a la venta |

Cada actividad tiene además sus variantes 03 (Zona Franca) y 05 (Empresa Pública), y algunas la 04 (Entidad Pública). Todas están en el CSV completo.

### 6.4 Productos del SIN para ferretería minorista

La lista completa de las 14 actividades clave (276 filas) está en `04-catalogo-productos-sin-ferreteria.csv`.

**4752100: Venta al por menor de artículos de ferretería, fontanería y calefacción (24).** Los mismos 24 códigos están en la actividad mayorista 4663600.

| Código SIN | Descripción |
|---|---|
| 1001903 | herramientas de mano tales como alicates, destornilladores. |
| 1001904 | herramientas de mano de uso agrícola no motorizadas. |
| 1001905 | sierras y hojas para sierras, incluidas sierras circulares y de cadena. |
| 1001906 | accesorios intercambiables para herramientas de mano motorizadas o no y para máquinas herramientas: brocas, punzones y fresas. |
| 1001907 | estampas y troqueles de prensa. |
| 1001908 | herramientas de herrería: machos de forja, yunques. |
| 1001909 | moldes y cajas de moldeo (excepto lingoteras). |
| 1001910 | tornos de banco, abrazaderas. |
| 1001911 | candados, cerraduras, pasadores, llaves, bisagras y artículos similares. |
| 1001912 | accesorios de ferretería para edificios, muebles, vehículos, etcétera. |
| 1001913 | machetes, espadas y bayonetas. |
| 1001914 | cuchillas y cizallas para máquinas y para aparatos mecánicos. |
| 1003449 | Herramientas Eléctricas |
| 1003450 | Accesorios para Herramientas |
| 1003451 | Artículos para Fijación (Tornillos, tuercas, arandelas) |
| 1003452 | Artículos de Seguridad (Cascos, guantes, gafas de protección) |
| 1003453 | Tuberías y Accesorios |
| 1003454 | Accesorios de Conexión (Grifos, llaves de paso, etc) |
| 1003455 | Sistemas de Drenaje y Desagüe |
| 1003456 | Equipos de Fontanería |
| 1003457 | Radiadores |
| 1003458 | Sistemas de Calefacción |
| 1003459 | Accesorios de Calefacción |
| 1003460 | Aire Acondicionado y Ventiladores |

**4752400: Venta al por menor de ladrillo, madera, cemento y otros materiales de construcción (27).** Los códigos 1003362 a 1003386 también están en la actividad mayorista 4663100; los códigos 1003636 y 1003637 son exclusivos de la minorista.

| Código SIN | Descripción |
|---|---|
| 1003362 | Piedra de construcción o para tallado |
| 1003363 | Pizarra |
| 1003364 | Mármol y otras piedras calizas para tallado o construcción |
| 1003365 | Granito, arenisca y otras piedras para tallado o construcción |
| 1003366 | fundente calizo |
| 1003367 | otras piedras calcáreas del tipo habitualmente utilizado para la fabricación de cal o cemento |
| 1003368 | Yeso |
| 1003369 | Anhidrita |
| 1003370 | Roca o piedra caliza en bruto (para cal o cemento) |
| 1003371 | Caliza triturada o molida (para cal o cemento) |
| 1003372 | Arenas, piedras, gravilla, piedras machacadas, bitumen natural y asfalto |
| 1003373 | Arenas naturales |
| 1003374 | Piedras, gravilla, piedra triturada |
| 1003375 | Gránulos, grava y polvo de piedra |
| 1003376 | Betún y asfalto natural; asfaltita y rocas asfálticas |
| 1003377 | Arcillas |
| 1003378 | Caolín (arcilla blanca) |
| 1003379 | Bentonita (tierras decolorantes) |
| 1003380 | Cemento y derivados |
| 1003381 | Ladrillos, Bloques y Mampostería |
| 1003382 | Cemento Asfáltico y Materiales Bituminosos |
| 1003383 | Acero y Hierro |
| 1003384 | Tejas y Materiales para Cubiertas |
| 1003385 | Aislantes y Materiales de Impermeabilización |
| 1003386 | Pinturas y Recubrimientos |
| 1003636 | puertas, ventanas, contraventanas y sus marcos, escaleras, pórticos o barandales. |
| 1003637 | fierro de construcción y artículos sanitarios. |

**4752200: Venta al por menor de pinturas, barnices y lacas (29).**
- 1001384 Pinturas y barnices
- 1001385 Secativos
- 1001386 Lacas
- 1001387 Pigmentos preparados
- 1001388 Colores vitrificables
- 1001389 Brilladores
- 1001390 Pinturas aditivas
- 1001391 Colores para cerámica
- 1001392 Removedores de barniz
- 1001393 Removedores de pinturas
- 1001394 Removedores de tintes
- 1001395 Desmanchadores
- 1001396 Masillas
- 1001397 Pintura al óleo
- 1001398 Témperas
- 1001399 Acuarelas
- 1001400 Tinta de impresión
- 1001401 Tintas para escribir o dibujar y otras tintas
- 1001402 compuestos para calafatear y preparados similares no refractarios para relleno y enlucido
- 1001403 disolventes y diluyentes orgánicos compuestos
- 1001404 decapantes para pintura y barniz preparados
- 1003461 Imprimantes y Selladores
- 1003462 Adhesivos
- 1003463 Aceites y Ceras
- 1003464 Rodillos de Pintura
- 1003465 Cinta de Pintor
- 1003466 Cubetas, Bandejas y Otros Accesorios
- 1003467 Protección para Superficies
- 1003468 Productos para Reparación de Paredes y Superficies

**4752300: Venta al por menor de vidrio plano, espejos y sus productos (20).** Códigos 1001658 a 1001666, 1001697, 1003469 a 1003477 y 1003635. Por ejemplo, 1001662 "Vidrio de seguridad", 1001663 "Espejos de vidrio; unidades de vidrio aislantes de paredes múltiples", 1003476 "vidrios planos y de seguridad, espejos; artículos de vidrio utilizados en la construcción…" y 1003477 "La colocación de los vidrios o espejos cuando lo realiza el mismo vendedor."

**4759200: Artículos de iluminación (14).** Códigos 1002126 a 1002139. Por ejemplo, 1002126 "lámparas, tubos y bombillas de descarga (foco) incandescentes, fluorescentes…" y 1002127 "lámparas de techo".

**Actividades mayoristas con productos propios:**
- 4663400, acero y aluminio: códigos 1003410 a 1003431. Por ejemplo, 1003411 "Varilla de acero", 1003413 "Alambres de acero" y 1003419 "Perfiles de aluminio".
- 4663500, madera: códigos 1003432 a 1003448.
- 4663300, prefabricados de cemento: 1001747, 1001756 y 1003392 a 1003409.
- 4663200, sanitarios: 1003387 a 1003391.

Consecuencias para la homologación en M-INV:
- Un mismo código SIN puede estar en **varias actividades**. Por ejemplo, 1003451 está en 4663600/03/05 y 4752100/03/05, y 1001911 además en 2593200/02/03/05.
- Por eso la homologación debe guardar el **par (actividad, código SIN)**: en cada línea del XML van tanto `actividadEconomica` como `codigoProductoSin` [XSD-CV].
- Faltan equivalentes directos para varios productos típicos de ferretería. Material eléctrico como cables, interruptores y tomacorrientes no aparece bajo 4752100: la búsqueda de "cable", "conductor" e "interruptor" en actividades 47xx no dio resultados útiles. Esos productos habrá que homologarlos con el código genérico más cercano, por ejemplo 1001912 "accesorios de ferretería para edificios…" o los de 4759200 (iluminación). Esa decisión le corresponde al "Equipo de homologación" [HOM].

---

## 7. Qué debe implementar M-INV

1. **Configuración SIAT por empresa (tenant):**
   - `codigoAmbiente` (1 o 2), `codigoSistema`, NIT, `codigoModalidad` = 2 (fijo en esta versión).
   - Token delegado, cifrado en reposo, con fecha de vencimiento y alerta de renovación. Se envía en el header `apikey: TokenApi <token>`.
   - Esquema de despliegue: centralizado o por sucursal. Define si se sincroniza una vez con la Casa Matriz o por cada sucursal y punto de venta [EMI].
2. **Mapeo de sucursales y puntos de venta:** relacionar cada sucursal de M-INV con `codigoSucursal` SIN (0 = Casa Matriz) y cada `PosRegister` con `codigoPuntoVenta` SIN (0 = ninguno). También guardar el `codigoTipoPuntoVenta` (1 a 6) de cada punto.
3. **Cliente SOAP de sincronización:** un método genérico `SincronizarAsync(catalogo, contexto)` que cubra las **18 operaciones** de la tabla 3.2, con los 6 parámetros de la sección 3.3.
   - Los nombres de operación, el WSDL y los namespaces deben ir en **configuración**, no fijos en el código, porque la documentación no los publica.
   - Hay que confirmarlos contra el WSDL del ambiente piloto.
4. **Almacenamiento local de catálogos:**
   - Tabla genérica `siat_catalogo_item` con los campos `(tenant, ambiente, catalogo, codigo TEXT, descripcion, extra JSONB, sucursal, punto_venta, vigente, sincronizado_en, hash_lote)`.
   - Tablas específicas para lo que más se consulta: `siat_actividad` (código TEXT de 7 caracteres, con ceros a la izquierda), `siat_producto_servicio` (código de actividad + código de producto, **sin** unicidad estricta), `siat_leyenda` (actividad + texto) y `siat_actividad_documento_sector`.
   - Las filas que el SIN retire no se borran: se marcan como no vigentes para conservar la trazabilidad de las facturas ya emitidas.
5. **Sincronización automática diaria:**
   - Un trabajo programado que al inicio del día sincroniza primero **Fecha y Hora** y luego los otros 17 catálogos.
   - Se sincroniza de nuevo la fecha y hora **antes de cada solicitud de CUFD** [SISINF].
   - La **emisión se bloquea** si los catálogos no se sincronizaron en el día o si falla la sincronización de fecha y hora (**interpretación**: la norma exige la sincronización diaria, pero no dice que deba bloquearse la emisión).
   - **[corregido por revisión]** Ese bloqueo **no puede aplicarse fuera de línea**: si el SIN no responde, la sincronización también falla, y la norma exige seguir emitiendo fuera de línea de forma automática (`fase-ii-inspeccion.md` punto 14; `ingreso-a-contingencia.md`). Regla consolidada: en modo **en línea**, si la sincronización del día falló, se reintenta y se muestra una alerta al administrador, pero se sigue emitiendo con la última copia local de los catálogos; el bloqueo duro se reserva para lo que sí impide una factura válida (sin CUIS vigente, sin CUFD utilizable, producto sin homologar). Ver `00-indice-y-contradicciones.md`, C-10.
   - Se registra cada ejecución: catálogo, fecha, `transaccion`, mensajes y cantidad de ítems.
6. **Reloj SIAT:** guardar la diferencia entre la hora del SIN y la hora local. Todas las fechas de emisión y los controles de plazo usan la hora corregida [SISINF].
7. **Pantalla "Catálogos SIAT"** (solo para roles Administrador y Gerencia):
   - Lista de los 18 catálogos con la fecha de la última sincronización, el estado y la cantidad de ítems.
   - Botón "Sincronizar ahora", por catálogo o todos.
   - Visor con buscador de ítems.
   - Indicador de que la sincronización está vencida.
8. **Pantalla "Homologación de productos":**
   - Por cada `Product` de M-INV, elegir la **actividad económica** (filtrada a las actividades del NIT que devuelve la sincronización "Actividades") y el **código de producto SIN** (filtrado por esa actividad), con búsqueda por texto y sin distinguir acentos.
   - Operaciones masivas: homologar por categoría y copiar de otro producto.
   - Informe de "productos sin homologar", que **bloquea su facturación**.
   - Precarga sugerida para ferretería con `04-catalogo-productos-sin-ferreteria.csv`.
   - Campos nuevos en `Product` o en una tabla aparte `ProductoHomologacionSiat`: `ActividadEconomicaSin` (string de 7), `CodigoProductoSin` (int), `UnidadMedidaSin` (int) y `OrigenImportado` (bool, por la nota de importado y nacional).
9. **Homologación de otras paramétricas**, para relacionar las entidades de M-INV con los códigos SIN sincronizados:
   - `UnitOfMeasure` → `unidadMedida` (58 = unidad de servicio)
   - `PaymentMethod` → `codigoMetodoPago` (1 efectivo, 2 tarjeta, 5 otros, etc.)
   - `Currency` → `codigoMoneda`
   - `Country` → código de país
   - tipo de documento del `Customer` → `codigoTipoDocumentoIdentidad`
   - Todas se validan contra el catálogo **sincronizado**, no contra enums fijos [VER2021].
10. **Validaciones al emitir** (sector 1, Compra y Venta):
    - `codigoDocumentoSector` = 1 y `tipoFacturaDocumento` = 1.
    - `actividadEconomica` ∈ actividades sincronizadas del NIT (errores 1016 y 2010).
    - Par (actividad, `codigoProductoSin`) ∈ catálogo de productos (errores 1017 y 2011).
    - `codigoTipoDocumentoIdentidad` ∈ catálogo (1 a 5).
    - Si el tipo es CI o NIT, `numeroDocumento` debe ser **numérico** [EMI].
    - `complemento` solo se admite con CI (errores 1011 y 2004).
    - `numeroTarjeta` solo con método tarjeta, y ofuscado (errores 1012 y 2006).
    - Con moneda boliviano: `tipoCambio` = 1 y `montoTotalMoneda` = `montoTotal`.
    - `codigoExcepcion` según la sección 3.6.
    - Clientes especiales 99001, 99002 y 99003, con tipo NIT y excepción 1.
    - `leyenda` tomada **al azar** del catálogo de leyendas de la actividad, en cada emisión [FASE2].
    - Tipo de emisión: nunca "CONTINGENCIA" [EMI].
11. **Documentos sector que habilitar en M-INV:** 1 (Compra y Venta) y 24 (Nota de Crédito-Débito, tipo 3, dentro de 18 meses). Opcionales: 47 (Nota Crédito Débito Descuentos), si se usa `descuentoAdicional`, y 35 (Bonificaciones) o 41 (Tasas), si el negocio lo requiere. Hay que validar con la sincronización "Actividades Documento Sector" que el NIT los tenga habilitados (error 940).
12. **Tabla local de códigos SIAT:** cargar los 193 códigos de la sección 4 como semilla y actualizarlos con el catálogo "Mensajes Servicios".
    - Guardar en cada operación: `codigoEstado`, `codigoRecepcion`, `transaccion` y la **lista completa de mensajes** (código y descripción).
    - Tratar los códigos 2000 a 2019 y el 3008 como **advertencias**: se muestran, pero no se reintenta la emisión.
    - 901 = pendiente, que exige volver a consultar el estado.
    - 902 = rechazada.
    - 904 y 908 = resultados de la validación de paquetes.
    - 905, 906, 907 y 909 = estados de anulación y reversión.
    - 3008 = alerta de que el CUIS está por vencer, lo que genera una tarea de renovación.
    - 989 = token no válido, lo que genera una alerta al administrador.
    - 995 y 967 = problema de comunicación, que puede llevar al modo fuera de línea (lo detalla la especificación de contingencia).
13. **Pantalla o registro de errores SIAT:** una bitácora filtrable por código, categoría (estado, advertencia, error o bloqueo), sucursal y fecha, con la descripción oficial.
14. **Soporte de la Etapa II de pruebas (piloto):** un comando o pantalla "Ejecutar batería de sincronización" que repita los 36 casos de [XLS-SINC] (sucursal 0, punto de venta 1 y 0) N veces (50 por caso [FASE1]) y exporte un informe.
15. **Representación gráfica:** mostrar el **código interno** (`Product.Code`) y no el código SIN [HOM]. Imprimir las leyendas del catálogo; la tercera leyenda depende de si se emitió "en línea" o "fuera de línea" [FASE2].

---

## 8. Dudas y huecos de la documentación

1. **El WSDL, la URL, el namespace, el nombre del servicio y los nombres exactos de las 18 operaciones de sincronización no están en la documentación descargada.** [SINC] solo tiene la matriz de parámetros. Hay que obtenerlos del WSDL en el ambiente piloto.
2. **La estructura de "Lista de Códigos" y de "Lista de Mensajes" no está documentada**: nombres de campos, tipos y si traen campos extra, como el código de actividad en la lista de productos o el tipo de actividad.
3. No se documentan las longitudes máximas de `codigoSistema` y `cuis`, ni el tamaño o la paginación de la respuesta del catálogo de productos, que tiene unos 21 500 registros según el xls.
4. **El formato de la fecha y hora** que devuelve "Fecha y Hora" no está documentado.
5. **Valores de catálogos no documentados:** tipo de documento de identidad 2, 3 y 4 (y el 5 = NIT, que solo se deduce de los ejemplos); métodos de pago distintos de 1, 2 y 5; monedas (1 = Boliviano también se deduce); unidades de medida distintas de 58; motivos de anulación; países; tipos de habitación; códigos del catálogo de eventos significativos (que la numeración del 1 al 7 de [CONT] coincida con los códigos); mensajes de servicios.
6. **Faltan los documentos sector 25, 26, 27 y 32** en [TIPOS]. Además, el sector 40 aparece con el valor 13 en su página XSD.
7. **Tipo de factura 4 (Documento Equivalente)** figura en [VER2021] y [TIPOS], pero no en la tabla del CUF [CUF], que solo lista 1, 2 y 3.
8. [FASE1] menciona un catálogo de "**modalidad**" en la Etapa II, pero no hay ninguna operación de sincronización para modalidad.
9. **"50 pruebas por cada caso":** no queda claro si son 50 por hoja, por caso (36 × 50) o en total.
10. **Nota de importado y nacional [HOM]:** el catálogo actual no diferencia productos importados de nacionales en sus descripciones. No se sabe cómo aplicar esa regla en la práctica; conviene preguntar al SIN o revisar la lista sincronizada en producción.
11. [ERR] dice que son "Algunos de los codigos de error": **la lista puede estar incompleta**. Hay que completarla con el catálogo "Mensajes Servicios".
12. **No está documentado si una factura con advertencias (2xxx) queda válida.** El texto "Advertencia" sugiere que no bloquea, pero hay que confirmarlo con las pruebas piloto.
13. **Hay códigos casi duplicados** con semántica muy parecida: 929 y 973 (CUIS no vigente), 970 y 980 (CUIS vigente existente), 974 y 981 (rango de fechas de evento), 983 y 993 (fuera de plazo), 952 y 1000 (CUF ya registrado). No se documenta en qué servicio aparece cada uno.
14. **La RND 102100000011** citada en [GEN] no se pudo leer: el adjunto `rnd_491335f008.pdf` es en realidad el HTML de la portada del SIN.
15. El ejemplo de [HOM] usa códigos de producto de 4 dígitos y CAEB de 6 dígitos (por ejemplo 1377 y 463010), y los XML de ejemplo usan 451010 y 49111. En cambio, el catálogo vigente [XLS-PROD] usa 7 dígitos. No se documenta si los códigos antiguos siguen siendo válidos: hay que usar **siempre** los sincronizados.
16. **No hay equivalentes específicos para material eléctrico** (cables, interruptores) en las actividades de ferretería minorista. Hay que decidir el código genérico con el contador o el "Equipo de homologación".
