# Macros VBA (opcionales) · M-INV V1

La V1 se entrega como `.xlsx` **sin macros**. Este módulo es opcional y existe por una sola razón: ocultar la
**barra de fórmulas** en la portada es una opción de la *aplicación* Excel, no del archivo, así que un `.xlsx` no
puede guardarla. Todo lo demás del modo app (cuadrícula y encabezados ocultos, navegación con botones, fórmulas
ocultas en el Release) ya funciona sin VBA.

| Archivo | Tipo | Qué hace |
|---|---|---|
| `modAppMode.bas` | Módulo estándar (exportación VBE) | `AplicarModoApp` oculta la barra de fórmulas en `00_PORTADA`; `RestaurarModoExcel` la devuelve |
| `ThisWorkbook.txt` | Código para el módulo `ThisWorkbook` | Eventos de apertura, cambio de hoja/libro y cierre que llaman a `modAppMode` |

## Generación automática (recomendada)

```powershell
powershell -ExecutionPolicy Bypass -File tools\build_xlsm.ps1 -Source src\M-INV_V1_Core.xlsx
```

Crea `src/M-INV_V1_Core.xlsm`, importa el módulo, pega el código de `ThisWorkbook` y **prueba** el resultado:
barra oculta al abrir y en la portada, visible en las demás hojas y restaurada al cerrar. Requiere activar en Excel
*Archivo → Opciones → Centro de confianza → Configuración → Configuración de macros → Confiar en el acceso al modelo
de objetos de proyectos de VBA* (desactívelo al terminar).

## Instalación manual en una copia `.xlsm`

1. Abra el libro, *Archivo → Guardar como → Libro de Excel habilitado para macros (.xlsm)*.
2. `Alt + F11` → *Archivo → Importar archivo…* → `modAppMode.bas`.
3. Doble clic en **ThisWorkbook** y pegue el contenido de `ThisWorkbook.txt`.
   (No importe ese archivo: el Editor crearía un módulo de clase nuevo en lugar de usar el de libro.)
4. Guarde, cierre y vuelva a abrir: en la portada la barra de fórmulas desaparece; en las demás hojas vuelve.

## Estado

- Código estándar y sin dependencias; las rutinas son tolerantes a errores (`On Error Resume Next`) y siempre
  restauran la configuración de Excel del usuario al salir del libro.
- **Probado** en Microsoft 365 (build 16.0.20326) con `tools/build_xlsm.ps1`: 5/5 comprobaciones.
- Hallazgo de la prueba: mientras el libro se cierra, Excel **ignora** `Application.DisplayFormulaBar = True`
  (sin error). Por eso `RestaurarModoExcel` usa primero el comando de la cinta
  `CommandBars.ExecuteMso "ViewFormulaBar"`, que sí se aplica, y deja la propiedad como respaldo.
- Los archivos están en ASCII con fin de línea CRLF (`.gitattributes` lo preserva), requisito del importador de VBA.
