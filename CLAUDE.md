# ZP-MINV-Platform · Contexto para agentes

M-INV es el sistema de inventarios B2B de Z&P Software Fast Solutions: un libro de **Excel local** arquitectado
como aplicación transaccional inmutable (CQRS, append-only), preparado para migrar a SQL/.NET.

Versión actual: **1.2.0** (rama `Inventario-V1.2`; la V1.0/1.1 vive en `Inventario-V1`). Dos ediciones desde el
mismo generador: **Estándar** (`.xlsx`, sin macros) y **Plus** (`.xlsm`, VBA de `src/macros/`).
Idioma del producto y la documentación: español.

## Reglas obligatorias

@.claude/excel-architecture-rules.md

## Comandos

```powershell
powershell -ExecutionPolicy Bypass -File tools\build_all.ps1 -Capturas                     # ciclo completo (DoD)
.venv\Scripts\python tools\build_minv.py                                                   # solo generar
powershell -ExecutionPolicy Bypass -File tools\build_xlsm.ps1                              # edición Plus + pruebas VBA
powershell -ExecutionPolicy Bypass -File tools\verify_minv.ps1 -Path src\M-INV_V1_Core.xlsx  # verificar un libro
```

Los libros de `src/` y `releases/` son artefactos generados: los cambios se hacen en `tools/minv/` (o en
`src/macros/` para el VBA) y se verifican con `tools/build_all.ps1` antes de dar una tarea por terminada (regla R-12).
Los scripts `.ps1` deben ser ASCII (PowerShell 5.1); los fuentes VBA, UTF-8 compatible con Windows-1252.

## Documentación

- Modelo de datos: `docs/architecture/data-dictionary.md`
- Sistema visual y UX: `docs/product/ux-ui-guidelines.md`
- VBA de la edición Plus: `src/macros/README.md`
- Historial: `CHANGELOG.md`
