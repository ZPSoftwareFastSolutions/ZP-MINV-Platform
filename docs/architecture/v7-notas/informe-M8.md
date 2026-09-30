# Informe M8 – módulos «Documentos Fiscales», «SIAT», «Homologación» y «Libros» (tarea 11 de la V7)

Carpetas: `src/3. Presentation/MINV.WebCatalog/src/4-presentation/panel/modules/documentos-fiscales/`, `…/siat/`, `…/homologacion/` y `…/libros/`. No se tocó nada fuera de esas carpetas.

Claves de paquete: `documentos-fiscales`, `siat`, `homologacion`, `libros`.

## Resumen

### Documentos Fiscales (`/panel/documentos-fiscales`, permisos: `billing.manage`, `billing.view`)
Pantalla para el control y emisión manual de facturas y visualización de historial.
- Tabla paginada y filtrable de documentos fiscales emitidos (Facturas Electrónicas en Línea y Computarizadas).
- Visualización de detalle del documento con opción a **reimpresión gráfica** (`PrintDialog`) y **visualización del XML crudo** (`XmlDialog`).
- Acciones de anulación (`VoidDialog`) y reemisión por contingencia (`ReissueDialog`).
- Envío directo de factura por correo electrónico al cliente (`SendEmailDialog`).

### SIAT (`/panel/siat`, permisos: `siat.manage`)
Pantalla técnica para la sincronización directa con el Servicio de Impuestos Nacionales de Bolivia.
- Panel superior con el estado general de comunicaciones (`SiatStatusStat`, verificando CUFD activo, estado de contingencias).
- **Eventos Significativos** (`EventsTab`): Registro, listado y cierre de contingencias (`ManualContingencyDialog`).
- **CAFC** (`CafcTab`): Solicitud y registro de Códigos de Autorización para Facturación por Contingencia (`RegisterCafcDialog`).
- **Paquetes** (`PackagesTab`): Empaquetado, envío y validación asíncrona de lotes de facturas emitidas fuera de línea.
- **Puntos de Venta** (`PointCard`): Gestión y apertura de sucursales y puntos de venta en Impuestos Nacionales.
- Funcionalidades adicionales para la transcripción manual de contingencias (`TranscribeDialog`).

### Homologación (`/panel/homologacion`, permisos: `siat.manage`)
- Gestión de códigos del SIN (Actividades, Productos/Servicios).
- Mapeo (homologación) de productos internos con los códigos estandarizados del SIN.

### Libros (`/panel/libros`, permisos: `billing.manage`)
- Registro de Compras y Ventas IVA.
- Exportación para el sistema del SIN y declaración jurada.

## Pruebas

- Todo el código compila exitosamente bajo TypeScript estricto (`npx tsc -b --noEmit` -> 0 errores).
- Las pruebas de `vitest` unitarias (`siat.test.ts`, `fiscal.test.ts`, `homologation.test.ts`, `books.test.ts`) pasan en verde.
