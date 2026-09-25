# Office Scripts · M-INV V2 (Excel para la web y de escritorio)

Lógica de negocio del libro colaborativo en TypeScript. Reemplaza al VBA de la edición Plus (que no corre en la web):
los scripts se ejecutan en Excel para la web y en Excel de escritorio de Microsoft 365 desde botones del libro.

| Script | Botón | Qué hace |
|---|---|---|
| `RegistrarEntrada.ts` | «Registrar en bodega» (10A) | Consolida la fila de captura de quien pulsa: ENTRADA, SALDO INICIAL, AJUSTE (+) o AJUSTE (-) |
| `RegistrarSalida.ts` | «Registrar salida» (10B) | Consolida la salida; bloquea si supera el disponible (poka-yoke) |
| `RecalcularStock.ts` | «Recalcular stock» (portadas, 15, 16) | Reconstruye las instantáneas 15_STOCK y 16_ALERTAS a demanda |
| `DiagnosticoInstalacion.ts` | — (editor de código) | Prueba de humo en el tenant: identidad, rol, filas de captura, contraseña y tablas |

## Cómo registra un script (RegistrarEntrada / RegistrarSalida)

1. **Identidad**: crea un comentario temporal en `92_SESION` (celda aleatoria), lee `getAuthorEmail()` y lo borra.
2. **Autorización**: busca el correo en `02_USUARIOS` (activo y con rol del fragmento).
3. **Fila propia**: ubica la fila de captura cuyo `Correo` (columna oculta) es el suyo.
4. **Validación autoritativa**: tipo del dominio, producto activo, cantidad y decimales, fecha, documento,
   observación en ajustes, SALDO INICIAL único y **disponible exacto** leído de las dos bitácoras.
5. **Consolidación**: con la protección pausada solo para su sesión, `Table.addRow` agrega al final de la bitácora
   oficial el movimiento con ID sin coordinación, `Usuario_O365` y `Timestamp`.
6. **Doble control**: en salidas y ajustes negativos vuelve a leer el disponible; si otra sesión se adelantó y el stock
   quedó negativo, marca su propio registro `✖ Rechazado` (no suma).
7. **Resultado**: escribe `✔ Registrado <ID>` o `✖ Bloqueado: <motivo>` en `Resultado` de su fila y la limpia si
   registró. La protección siempre se restaura (`finally`).

## Código compartido

Office Scripts no permite importar módulos: cada script debe ser un único archivo. El código común vive en
`lib/comun.ts` y se copia entre los marcadores `// >>> M-INV · BLOQUE COMÚN` y `// <<< M-INV · FIN DEL BLOQUE COMÚN`:

```powershell
.venv\Scripts\python tools\office_scripts.py sync       # copia lib/comun.ts dentro de cada script
.venv\Scripts\python tools\office_scripts.py check      # falla si alguno quedó desactualizado
```

No edite el bloque dentro de un script: edite `lib/comun.ts` y sincronice.

## Instalación

Los archivos de esta carpeta llevan el marcador `__MINV_PASSWORD__`. La versión que se pega en Excel se genera con la
contraseña de las hojas (fuera de Git):

```powershell
.venv\Scripts\python tools\office_scripts.py deploy --edicion release    # → build\office-scripts\release\
```

Luego, en Excel: **Automatizar › Nuevo script**, pegar cada archivo, guardarlo con el mismo nombre, ejecutar
`DiagnosticoInstalacion` y agregar los botones (*… › Agregar en el libro*) sobre los recuadros `⚙`. Pasos completos,
permisos y roles: `docs/deployment/sharepoint-rbac-policies.md`.

## Pruebas

```powershell
.venv\Scripts\python tools\build_minv_v2.py        # genera build\v2\fixture.json (estado del Core)
node tests\office-scripts\pruebas.mts             # Node 22.18+ (ejecuta TypeScript sin compilar)
```

Las pruebas cargan **los scripts reales** (Node elimina los tipos), los ejecutan contra un simulador de la API
`ExcelScript` (`tests/office-scripts/mock-excelscript.mts`) que reproduce protección de hojas, contraseñas, comentarios
con autor y tablas, y verifican: registro auditado, poka-yoke, registros simultáneos, validaciones, RBAC, identidad,
protección restaurada, Release vacío, diagnóstico y que `RecalcularStock` produzca exactamente la misma instantánea
que el generador de Python.

Límite honesto: el simulador no es Excel. La primera ejecución en el tenant real se valida con `DiagnosticoInstalacion`
(la API de identidad por comentarios y `pauseProtection` dependen de la versión de Excel del cliente; si
`pauseProtection` no existe, los scripts desprotegen y vuelven a proteger automáticamente).

## Reglas del código

- TypeScript estricto compatible con Office Scripts: sin `any`, sin módulos, sin enums ni namespaces propios.
- Toda escritura en hojas protegidas dentro de `conHojasDesbloqueadas`.
- Mensajes para el usuario en su fila de captura; nunca en celdas compartidas.
- La regla de stock de `RecalcularStock.ts` es la misma de `tools/minv2/lectura.py` (regla C-11).
