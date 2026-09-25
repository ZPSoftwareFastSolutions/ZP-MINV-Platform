# ZP-MINV-Platform · Contexto para agentes

M-INV V1 es el sistema de inventarios B2B de Z&P Software Fast Solutions: un libro de **Excel local** arquitectado
como aplicación transaccional inmutable (CQRS, append-only), preparado para migrar a SQL/.NET.

Rama de trabajo de la V1: `Inventario-V1`. Idioma del producto y la documentación: español.

## Reglas obligatorias

@.claude/excel-architecture-rules.md

## Comandos

```powershell
.venv\Scripts\python tools\build_minv.py                                                   # generar Core + Release
powershell -ExecutionPolicy Bypass -File tools\verify_minv.ps1 -Path src\M-INV_V1_Core.xlsx  # verificar en Excel
```

Los `.xlsx` son artefactos generados: los cambios se hacen en `tools/build_minv.py` y se verifican antes de dar
una tarea por terminada (regla R-12).

## Documentación

- Modelo de datos: `docs/architecture/data-dictionary.md`
- Sistema visual y UX: `docs/product/ux-ui-guidelines.md`
