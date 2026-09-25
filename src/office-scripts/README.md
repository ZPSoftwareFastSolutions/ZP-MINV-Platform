# Office Scripts · M-INV V2.1 (Excel para la web y de escritorio)

Lógica de negocio del libro colaborativo en TypeScript. Reemplaza al VBA de la edición Plus (que no corre en la web):
los scripts se ejecutan en Excel para la web y en Excel de escritorio de Microsoft 365 desde botones del libro (y
`ResumenDiario`, desde Power Automate).

| Script | Botón | Qué hace |
|---|---|---|
| `RegistrarEntrada.ts` | «Registrar en bodega» (10A) | Consolida la fila de captura de quien pulsa: ENTRADA, SALDO INICIAL, AJUSTE (+) o AJUSTE (-) |
| `RegistrarSalida.ts` | «Registrar salida» (10B) | Consolida la salida; bloquea si supera el disponible (poka-yoke) |
| `RecalcularStock.ts` | «Recalcular stock» (portadas, 15, 16, 18) | Reconstruye a demanda 15_STOCK (con salidas de 30 días, cobertura y ranking), 16_ALERTAS y 18_PEDIDO |
| `GenerarAjustesConteo.ts` | «Generar ajustes del conteo» (13) | Toma física: convierte los conteos en AJUSTE (±) o SALDO INICIAL contra el stock exacto |
| `ResumenDiario.ts` | — (Power Automate) | Solo lectura: asunto, HTML e indicadores del día para un correo programado |
| `DiagnosticoInstalacion.ts` | — (editor de código) | Prueba de humo en el tenant: hojas, tablas y nombres, identidad, rol, filas, contraseña |

Todos (salvo `ResumenDiario`) dejan su ejecución en `14_ACTIVIDAD`: quién (correo de Microsoft 365), cuándo, qué
script, resultado y detalle, **incluidos los intentos bloqueados** que no llegan a la bitácora oficial.

## Cómo registra un script (RegistrarEntrada / RegistrarSalida)

1. **Identidad**: crea un comentario temporal en `92_SESION` (celda aleatoria), lee `getAuthorEmail()` y lo borra.
2. **Fila propia**: ubica la fila de captura cuyo `Correo` (columna oculta) es el suyo; si no la tiene, deja el intento
   en `14_ACTIVIDAD` e informa el error.
3. **Autorización**: busca el correo en `02_USUARIOS` (activo y con rol del fragmento).
4. **Validación autoritativa**: tipo del dominio, producto activo, cantidad y decimales, fecha, documento,
   observación en ajustes, SALDO INICIAL único y **disponible exacto** leído de las dos bitácoras.
5. **Consolidación**: con la protección pausada solo para su sesión, `Table.addRow` agrega al final de la bitácora
   oficial el movimiento con ID sin coordinación, `Usuario_O365` y `Timestamp`.
6. **Doble control**: en salidas y ajustes negativos vuelve a leer el disponible; si otra sesión se adelantó y el stock
   quedó negativo, marca su propio registro `✖ Rechazado` (no suma).
7. **Resultado**: escribe `✔ Registrado <ID>` o `✖ Bloqueado: <motivo>` en `Resultado` de su fila y la limpia si
   registró; agrega la fila de auditoría en `14_ACTIVIDAD`. La protección siempre se restaura (`finally`).

## Toma física (GenerarAjustesConteo)

1. Identifica y autoriza (BODEGA o ADMIN) y exige `SI` en *Confirmar* (`ctConfirmar`, H7): evita ejecuciones
   accidentales. La fecha del conteo (`ctFecha`, D7) es opcional (vacía = hoy; nunca futura).
2. Valida **todos** los conteos (número ≥ 0; decimales solo si la unidad los admite). Un solo error bloquea el proceso
   completo y la respuesta dice qué filas corregir: nunca registra la mitad de un conteo.
3. Lee el stock **exacto** de todos los productos con una sola pasada por las dos bitácoras (`saldos`).
4. Por producto: sin movimientos → `SALDO INICIAL` (si contó más de 0); con movimientos → `AJUSTE (+)` o `AJUSTE (-)`
   por la diferencia; igual → nada. Documento `CF-AAAAMMDD`, observación con sistema, contado y diferencia.
5. Agrega todos los movimientos con **una** inserción atómica (`Table.addRows`, IDs únicos también dentro del lote).
6. Vuelve a verificar los ajustes negativos (una venta simultánea pudo dejar el stock en negativo) y marca
   `✖ Rechazado` los que no alcancen.
7. Limpia **solo** las celdas de conteo procesadas (nunca la columna entera: otros pueden seguir contando), borra la
   confirmación y deja el resultado en `ctResultado` (I7) y en `14_ACTIVIDAD`.

## Instantánea (RecalcularStock)

Una lectura de catálogo, proveedores, estados y las columnas `SKU`, `CantidadNeta`, `Fecha`, `Estado` y `Tipo` de las
dos bitácoras; una escritura por tabla (`tblStock` 20 columnas, `tblAlertas`, `tblPedido`) más los metadatos
`stk*`. Reglas (idénticas a `tools/minv2/lectura.py`, regla C-11):

- **Salidas30d**: Σ unidades de tipo `SALIDA` consolidadas con `Fecha` ≥ hoy − 29.
- **CoberturaDias**: `ENTERO(máx(0, stock) ÷ (Salidas30d ÷ 30))`; vacía si no hubo salidas.
- **RankSalidas30d**: 1 = más vendido en 30 días (empates por orden del catálogo); vacío si no hubo salidas.
- **Pedido**: productos activos AGOTADO, CRÍTICO o BAJO; `APedir` = máximo (o 2 × mínimo) − máx(0, stock);
  agrupados por proveedor en el orden de `04_PROVEEDORES` (los sin proveedor al final), con subtotal, días de entrega,
  fecha estimada y contacto.

## Código compartido

Office Scripts no permite importar módulos: cada script debe ser un único archivo. El código común vive en
`lib/comun.ts` y se copia entre los marcadores `// >>> M-INV · BLOQUE COMÚN` y `// <<< M-INV · FIN DEL BLOQUE COMÚN`:

```powershell
.venv\Scripts\python tools\office_scripts.py sync       # copia lib/comun.ts dentro de cada script
.venv\Scripts\python tools\office_scripts.py check      # falla si alguno quedó desactualizado
```

No edite el bloque dentro de un script: edite `lib/comun.ts` y sincronice. Las constantes de nivel superior propias de
un script van **antes** del bloque común y no pueden usar constantes del bloque (se evalúan antes; use literales).

## Instalación

Los archivos de esta carpeta llevan el marcador `__MINV_PASSWORD__`. La versión que se pega en Excel se genera con la
contraseña de las hojas (fuera de Git):

```powershell
.venv\Scripts\python tools\office_scripts.py deploy --edicion release    # → build\office-scripts\release\
```

Luego, en Excel: **Automatizar › Nuevo script**, pegar cada archivo, guardarlo con el mismo nombre, ejecutar
`DiagnosticoInstalacion` y agregar los botones (*… › Agregar en el libro*) sobre los recuadros `⚙`. Paso a paso:
`docs/deployment/inicio-rapido.md`; permisos y roles: `docs/deployment/sharepoint-rbac-policies.md`.

## Pruebas

```powershell
.venv\Scripts\python tools\build_minv_v2.py        # genera build\v2\fixture.json (estado del Core)
node tests\office-scripts\pruebas.mts             # Node 22.18+ (ejecuta TypeScript sin compilar)
```

Las pruebas (22) cargan **los scripts reales** (Node elimina los tipos), los ejecutan contra un simulador de la API
`ExcelScript` (`tests/office-scripts/mock-excelscript.mts`) que reproduce protección de hojas, contraseñas, comentarios
con autor, tablas (`addRow`/`addRows`) y nombres, y verifican: registro auditado, poka-yoke, registros simultáneos,
validaciones, RBAC, identidad, protección restaurada, Release vacío, actividad (también cuando no se puede escribir),
toma física (confirmación, sobrante/faltante, saldo inicial, conteos inválidos, fecha, rechazo por venta simultánea),
resumen diario de solo lectura (y HTML escapado), diagnóstico y que `RecalcularStock` produzca exactamente la misma
instantánea (stock, cobertura, ranking, alertas y pedido) que el generador de Python.

**Tipos:** `tools/build_v2.ps1` compila cada script con TypeScript estricto contra
`tests/office-scripts/excelscript-tipos.d.ts` si encuentra `tsc` (en el PATH o en la variable `MINV_TSC`); si no, el
paso se omite con un aviso.

Límite honesto: el simulador no es Excel. La primera ejecución en el tenant real se valida con `DiagnosticoInstalacion`
(la API de identidad por comentarios y `pauseProtection` dependen de la versión de Excel del cliente; si
`pauseProtection` no existe, los scripts desprotegen y vuelven a proteger automáticamente).

## Reglas del código

- TypeScript estricto compatible con Office Scripts: sin `any`, sin módulos, sin enums ni namespaces propios.
- Toda escritura en hojas protegidas dentro de `conHojasDesbloqueadas`.
- Mensajes para el usuario en su fila de captura (o en `ctResultado` del conteo); nunca en celdas compartidas.
- Toda ejecución deja una fila en `14_ACTIVIDAD` con `registrarActividad`, que nunca interrumpe la operación principal.
- Varias filas nuevas = una sola `agregarFilas` (`addRows`); nunca un contador compartido para IDs.
- La regla de stock, alertas y pedido de `RecalcularStock.ts` es la misma de `tools/minv2/lectura.py` (regla C-11).
