# Historial de cambios · M-INV

Formato basado en [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/). Versionado semántico.

## [2.0.0] · 2026-09-25 · rama `Inventario-V2`

Tema de la versión: **M-INV colaborativo en Microsoft 365**. Un solo libro en SharePoint/OneDrive que Bodega y Ventas
usan a la vez desde Excel para la web, sin colisiones de coautoría, con auditoría por correo y Office Scripts.

### Agregado

- **Libro colaborativo** `src/M-INV_V2_Colaborativo.xlsx` (Core con demo) y
  `releases/M-INV_V2_Colaborativo_Produccion.xlsx` (Release), generados por `tools/build_minv_v2.py` (`tools/minv2/`).
- **Escritura fragmentada**: `10A_ENTRADAS` (Bodega: entrada, saldo inicial, ajustes) y `10B_SALIDAS` (Ventas). En cada
  una, **captura por usuario** (una fila por persona según `02_USUARIOS`, con validación y disponible exacto en vivo) y
  **bitácora oficial** que solo escriben los scripts (`Table.addRow`, ID sin contador compartido, `Usuario_O365` y
  `Timestamp` en columnas ocultas).
- **Poka-yoke**: una salida (o ajuste negativo) mayor que el disponible se tiñe de rojo sangre con texto blanco tachado
  y el script bloquea la consolidación; si otra persona se adelanta, el registro propio queda «✖ Rechazado» y no suma.
- **Lectura a demanda**: `15_STOCK` y `16_ALERTAS` son instantáneas de valores que reconstruye `RecalcularStock.ts`;
  las portadas muestran quién y cuándo calculó y cuántos movimientos hay después.
- **Portadas por rol**: `00_PORTADA_BODEGA` (registrar entrada, registrar ajuste, alertas de stock crítico) y
  `00_PORTADA_VENTAS` (registrar salida, consultar disponibilidad), con indicadores, gráficos y últimos movimientos.
- **Control de acceso**: `02_USUARIOS` (correo de Microsoft 365 y rol ADMIN/BODEGA/VENTAS/CONSULTA) validado por los
  scripts; «Permitir editar rangos» en las capturas (contraseña opcional por rol); guía de SharePoint.
- **Office Scripts** (`src/office-scripts/`): `RegistrarEntrada`, `RegistrarSalida`, `RecalcularStock` y
  `DiagnosticoInstalacion`, con bloque común (`lib/comun.ts`) sincronizado por `tools/office_scripts.py`, que también
  genera las versiones instalables con la contraseña (`build/office-scripts/`).
- **Pruebas sin Excel**: simulador de la API `ExcelScript` y 12 pruebas que ejecutan los scripts reales
  (`tests/office-scripts/`), incluida la paridad de `RecalcularStock` con el generador.
- **Verificación en Excel** `tools/verify_minv_v2.ps1` y ciclo completo `tools/build_v2.ps1`.
- Documentación: `.claude/v2-concurrency-rules.md`, `docs/deployment/sharepoint-rbac-policies.md`,
  `docs/architecture/data-dictionary-v2.md`, `src/office-scripts/README.md`.

### Cambiado

- `tools/minv/master.py`: la tabla de catálogo y proveedores se separa de su decoración para reutilizarla en la V2.
- `tools/minv/postprocess.py`: nueva guarda que aborta el build si una fórmula tiene paréntesis o llaves desbalanceados
  (Excel rechazaba el libro completo).

### Decisiones

- La especificación pedía cálculo **Manual** para 15_STOCK: en Excel el modo de cálculo es por libro y por sesión (no por
  hoja) y congelaría el disponible y el poka-yoke. Se implementó la instantánea a demanda, que cumple el objetivo sin ese
  costo; `RecalcularStock` además dispara un recálculo completo.
- «Permitir editar rangos» no admite correos en Excel para la web: el correo lo valida el script contra `02_USUARIOS` y
  los rangos admiten contraseña por rol.

## [1.2.0] · 2026-09-25 · rama `Inventario-V1.2`

Tema de la versión: **más funciones y un uso más intuitivo para todos los perfiles** (bodega, compras, gerencia y
administración), sin romper el modelo CQRS.

### Agregado

- **Dos ediciones** generadas desde el mismo código:
  - **Estándar** (`.xlsx`, sin macros): todo lo nuevo de lectura y consulta.
  - **Plus** (`.xlsm`): formulario guiado, sellado de la bitácora, ajustes automáticos del conteo y atajos de doble clic.
- **Portada renovada**: asistente **«Próximo paso»** que indica qué hacer ahora (errores, catálogo vacío, saldo
  inicial, conteo en curso, agotados, reposición, inventario sin rotación) con un botón que lleva al lugar exacto;
  8 mosaicos con iconos (Registrar, Consultar, Stock, Alertas, Pedido, Conteo, Catálogo, Guía); tarjeta
  **Sin rotación**; indicador de integridad de la bitácora.
- **Barra de navegación** con iconos en todas las hojas visibles (la hoja activa resaltada).
- **`04_PROVEEDORES`** (maestro nuevo): contacto, teléfono, correo, días de entrega; productos y alertas por proveedor.
  `05_PRODUCTOS` gana la columna `Proveedor` (lista validada).
- **`12_REGISTRO`** (Plus): formulario guiado con búsqueda por texto, vista previa del stock antes → después, semáforo
  antes → después y 7 validaciones en vivo. El botón **REGISTRAR** escribe en la bitácora, comprueba el `Estado` y
  sella la fila; conserva tipo y responsable para registrar varios movimientos seguidos.
- **`13_CONTEO`** (toma física): conteo contra sistema, diferencia en unidades y en dinero, resultado
  (cuadra / sobrante / faltante) y ajuste sugerido. En Plus, **Generar ajustes** registra y sella los `AJUSTE (+/-)`.
- **`17_KARDEX`** (consulta de producto): búsqueda por texto, ficha (stock, estado, valor, último movimiento, días sin
  movimiento), gráfico de evolución del saldo e historial de los últimos 100 movimientos.
- **`18_PEDIDO`** (pedido sugerido de compra): agrupado por proveedor y priorizado, cantidad a pedir hasta el máximo,
  costo y subtotal; filtro por proveedor con contacto y fecha estimada de entrega; listo para imprimir.
- **`99_AYUDA`**: «¿Qué necesita hacer hoy?» por perfil y lista de **primeros pasos** que se marca sola.
- `15_STOCK`: columnas `Proveedor` y `DiasSinMov` (inventario inmovilizado en ámbar); `16_ALERTAS`: columna `Proveedor`.
- Parámetros `cfgDiasSinRotacion` (60 días), `cfgEdicion` y `cfgSelladoHasta`.
- Edición Plus: **sellado** de la bitácora (las filas registradas quedan bloqueadas y en gris al registrar desde el
  formulario, al generar ajustes y al guardar), **doble clic** (alerta o pedido → formulario de reposición con la
  cantidad sugerida; stock o bitácora → consulta del producto) y aviso visible cuando las macros están deshabilitadas.
- Herramientas: generador modular (`tools/minv/`), `tools/build_xlsm.ps1` generalizado (Core y Release, con 20+
  pruebas automáticas del VBA en Excel), `tools/build_all.ps1` (ciclo completo) y guarda del post-proceso que impide
  referencias estructuradas en formatos condicionales y validaciones.

### Cambiado

- `src/M-INV_V1_Core.xlsm` pasa de variante opcional (V1.1, solo modo app) a **edición Plus** completa; se agrega
  `releases/M-INV_V1_Produccion_Bloqueado.xlsm`.
- Los fuentes VBA (`src/macros/`) pasan a UTF-8; el script los instala con `AddFromString`.
- Datos demo: 6 proveedores ficticios y 2 productos de baja rotación.

### Corregido

- Excel rechazaba el libro completo cuando un formato condicional usaba referencias estructuradas (`tblStock[...]`):
  se reemplazaron por nombres definidos y el generador ahora lo impide (regla R-08).

## [1.1.0] · rama `Inventario-V1`

- Variante `.xlsm` con modo app: la barra de fórmulas se oculta en la portada y se restaura al salir (probado en Excel).

## [1.0.0] · rama `Inventario-V1`

- Primera versión: libro CQRS (bitácora append-only → stock → alertas → tablero), Core con datos demo y Release
  blindado, generador, verificador en Excel real y documentación.
