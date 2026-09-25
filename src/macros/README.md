# Macros VBA · edición Plus de M-INV V1.2

La edición **Estándar** (`.xlsx`) funciona sin macros. La edición **Plus** (`.xlsm`) agrega este código para que el
uso diario sea más rápido y guiado, **sin cambiar el modelo**: las macros solo escriben en la bitácora
(`10_MOVIMIENTOS`) y comprueban el `Estado` que ella misma calcula (reglas R-02 y R-13).

| Archivo | Tipo | Qué hace |
|---|---|---|
| `modConfig.bas` | Módulo | Contraseña (marcador `__MINV_PASSWORD__`), acceso a hojas/nombres/tablas, símbolos `✔ ✖ ○` (`ChrW`), avisos con modo silencioso, protección `UserInterfaceOnly`, aviso «sin macros», búsquedas por SKU |
| `modRegistro.bas` | Módulo | Botones del formulario (`RegistrarMovimiento`, `LimpiarFormulario`), reposición (`AbrirFormularioReposicion`), `RegistrarDesdeKardex`, sellado (`SellarBitacora`) y escritura en la bitácora |
| `modConteo.bas` | Módulo | `GenerarAjustesConteo`: diferencias de la toma física → `AJUSTE (+/-)` sellados |
| `modNavegacion.bas` | Módulo | `SkuDeFila` y `AbrirKardex` (atajos de doble clic) |
| `modAppMode.bas` | Módulo | Modo app: barra de fórmulas oculta en la portada y restaurada al salir |
| `ThisWorkbook.txt` | Código del libro | Apertura (protección, aviso, portada), guardado (sellado + aviso visible en el archivo), cambio de hoja y cierre |
| `Hoja_10_MOVIMIENTOS.txt` · `Hoja_15_STOCK.txt` | Código de hoja | Doble clic en un registro o producto → consulta (`17_KARDEX`) |
| `Hoja_16_ALERTAS.txt` · `Hoja_18_PEDIDO.txt` | Código de hoja | Doble clic en un producto → formulario de reposición con la cantidad sugerida |

## Qué ve el usuario

- **Formulario `12_REGISTRO`**: completa los campos, revisa la vista previa (stock antes → después, semáforo y 7
  validaciones) y pulsa **REGISTRAR MOVIMIENTO**. La macro escribe la fila en la bitácora, verifica que diga
  `✔ Registrado` (si no, la borra y explica por qué), la **sella** y deja el formulario listo para el siguiente
  movimiento (conserva tipo, categoría y responsable).
- **Sellado**: los registros correctos quedan bloqueados y en gris; ya no se pueden editar (se corrigen con un AJUSTE).
  Ocurre al registrar desde el formulario, al generar ajustes y antes de cada guardado.
- **Toma física**: **Generar ajustes** pide confirmación, registra un AJUSTE por cada diferencia (documento
  `CF-AAAAMMDD`), los sella y vacía el conteo.
- **Doble clic**: en una alerta o en el pedido → formulario de reposición; en el stock o en un registro → consulta.
- Si las macros están deshabilitadas, el formulario muestra un aviso amarillo con los pasos para habilitarlas y el
  libro funciona como la edición Estándar.

## Construcción y pruebas (recomendado)

```powershell
powershell -ExecutionPolicy Bypass -File tools\build_xlsm.ps1              # Core y Release
powershell -ExecutionPolicy Bypass -File tools\build_xlsm.ps1 -Solo core   # solo el Core
```

El script toma las bases `build/*.plus.xlsx` (generadas por `tools/build_minv.py`), instala los módulos con
`AddFromString`, inyecta la contraseña (`MINV_PASSWORD` / `MINV_RELEASE_PASSWORD`), guarda los `.xlsm`, sella la
bitácora demo y **prueba el VBA en Excel** sobre una copia temporal: apertura en modo app, protección reaplicada,
macros de los botones, registro válido e inválido, sellado, reposición con la cantidad sugerida, consulta, ajustes del
conteo, guardado (aviso visible en el archivo), cierre (barra de fórmulas restaurada) y reapertura sin macros.

Requiere activar temporalmente *Archivo → Opciones → Centro de confianza → Configuración → Configuración de macros →
Confiar en el acceso al modelo de objetos de proyectos de VBA* (desactívelo al terminar).

## Instalación manual (solo si no puede usar el script)

1. Abra el `.xlsx` y guárdelo como *Libro de Excel habilitado para macros (.xlsm)*.
2. `Alt + F11`. Por cada `.bas`: *Insertar → Módulo*, cambie su nombre (propiedad *Name*) al del archivo y pegue el
   contenido **sin** la primera línea (`Attribute VB_Name = …`). En `modConfig` reemplace `__MINV_PASSWORD__` por la
   contraseña de protección del libro.
3. Pegue `ThisWorkbook.txt` en el módulo del libro (*ThisWorkbook* / *EsteLibro*) y cada `Hoja_*.txt` en el módulo de
   la hoja correspondiente.
4. Guarde, cierre y vuelva a abrir con las macros habilitadas.

Se pega (no se importa) porque los fuentes están en UTF-8: el importador del Editor de VBA espera la página de
códigos del sistema y dañaría las tildes; al pegar, Windows hace la conversión correctamente.

## Convenciones del código

- Fuentes en **UTF-8 con CRLF**, compatibles con Windows-1252 (el script rechaza otros caracteres): los símbolos
  `✔ ✖ ○` se generan con `ChrW` (`CHK()`, `CRUZ()`, `PENDIENTE()`).
- Cada punto de entrada maneja errores y avisa con `Avisar`/`Confirmar`, que respetan `ModoSilencioso` (las pruebas
  automáticas no abren cuadros de diálogo). `Confirmar` usa «No» como botón predeterminado.
- Las hojas se reprotegen en `Workbook_Open` con `UserInterfaceOnly` (no se guarda en el archivo) y con los mismos
  permisos que aplica el generador.
- Nunca se escribe en proyecciones (`15`–`18`), maestros ni configuración, salvo `cfgSelladoHasta`.
- La contraseña queda dentro del proyecto VBA: antes de entregar un Release Plus, bloquee el proyecto
  (*Herramientas → Propiedades de VBAProject → Protección*).

## Hallazgos de las pruebas

- Mientras el libro se cierra, Excel **ignora** `Application.DisplayFormulaBar = True`; `RestaurarModoExcel` usa
  primero `CommandBars.ExecuteMso "ViewFormulaBar"`, que sí se aplica.
- Probado en Microsoft 365 (build 16.0.20326, configuración en español).
