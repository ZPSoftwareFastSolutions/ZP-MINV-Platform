# Reglas de concurrencia y coautoría · M-INV V2 (Microsoft 365)

> **Documento normativo** para personas y agentes que trabajen en el libro colaborativo
> (`src/M-INV_V2_Colaborativo.xlsx`, `src/office-scripts/`, `tools/minv2/`).
> Complementa `.claude/excel-architecture-rules.md`; donde ambas hablen del libro colaborativo, **prevalece esta**.
> **DEBE** = obligatorio · **NO DEBE** = prohibido · **PUEDE** = permitido.

## 0. El problema que resuelve

En Excel para la web varias personas editan el mismo archivo a la vez. La coautoría fusiona cambios **por celda**:
si dos personas escriben en la misma celda (por ejemplo, «la siguiente fila libre» de una bitácora compartida),
gana la última y el otro dato se pierde sin aviso. Además, cada recálculo pesado (cientos de `SUMAR.SI.CONJUNTO` y
rankings) se ejecuta para todos los que tienen el libro abierto y congela el navegador.

La V2 elimina ambos riesgos con tres fragmentaciones y una lectura a demanda:

```text
             POR DOMINIO                         POR USUARIO                     POR OPERACIÓN
┌──────────────────────────────┐   ┌───────────────────────────────┐   ┌──────────────────────────────┐
│ 10A_ENTRADAS  (Bodega)       │   │ Zona de CAPTURA: 1 fila por   │   │ BITÁCORA OFICIAL: la escribe │
│ 10B_SALIDAS   (Ventas)       │ ─►│ usuario (nadie comparte celda)│ ─►│ solo el Office Script con    │
│ tablas, hojas y cálculos     │   │ validación y disponible vivos │   │ Table.addRow (atómico) +     │
│ separados: 0 filas comunes   │   │                               │   │ ID, Usuario_O365, Timestamp  │
└──────────────────────────────┘   └───────────────────────────────┘   └──────────────┬───────────────┘
                                                                                      │ RecalcularStock.ts
                                                                    15_STOCK / 16_ALERTAS (instantánea de valores)
```

## 1. Reglas

### C-01 · Fragmentación por dominio
- Los movimientos de **Bodega** (`ENTRADA`, `SALDO INICIAL`, `AJUSTE (+)`, `AJUSTE (-)`) se registran solo en
  `10A_ENTRADAS` (`tblEntradas`); las **salidas** de Ventas solo en `10B_SALIDAS` (`tblSalidas`).
- El dominio de cada tipo vive en `tblTiposMov[Dominio]` (`01_CONFIG`). Un script NO DEBE aceptar un tipo de otro
  dominio.
- Las dos bitácoras DEBEN tener el mismo esquema de columnas (se unen sin transformación en la migración a SQL).

### C-02 · Fragmentación por usuario (captura)
- Cada usuario autorizado del dominio tiene **una fila de captura propia**; su posición la define el orden de
  `02_USUARIOS` (columnas `OrdenBodega` / `OrdenVentas`). El ADMIN tiene fila en ambos fragmentos.
- NO DEBE existir una zona de escritura compartida («siguiente fila libre», formulario común, celda de búsqueda común).
- Los usuarios nuevos se agregan **al final** de `02_USUARIOS`; nunca se reordena ni se borra (retiro = `Activo = NO`).
  Esos cambios se hacen cuando nadie está registrando: mueven la asignación de filas.

### C-03 · La bitácora oficial solo la escriben los scripts
- `tblEntradas` y `tblSalidas` contienen **valores** (sin fórmulas), están en celdas bloqueadas y NO DEBEN editarse,
  ordenarse ni borrarse a mano. Son append-only.
- Todo registro entra con `Table.addRow(-1, …)`: una inserción atómica del servidor. Dos registros simultáneos producen
  dos filas; nunca se pisan.
- Las correcciones son movimientos compensatorios (`AJUSTE`), como en la V1 (R-02).

### C-04 · Identificadores sin coordinación
- El ID es `<fragmento>-AAAAMMDD-HHMMSS-XXXX` (`E-…` en 10A, `S-…` en 10B; `XXXX` aleatorio hexadecimal).
- NO DEBEN usarse contadores compartidos (`MAX(ID)+1`, `FILA()`): dos sesiones leerían el mismo máximo.

### C-05 · Identidad y auditoría
- `Usuario_O365` es el correo de la cuenta de Microsoft 365 que ejecuta el script. La API de Office Scripts no lo expone
  directamente: el script crea un comentario temporal en `92_SESION` (celda aleatoria), lee
  `Comment.getAuthorEmail()` y lo borra. Sin correo válido el script NO DEBE registrar.
- `Timestamp` es la fecha y hora local de la ejecución (número de serie de Excel). `Registró` guarda el nombre visible.
- Las columnas de auditoría (`Usuario_O365`, `SKU`, `Timestamp`) van en las columnas **N:P ocultas**; captura y bitácora
  comparten columnas de hoja, así que ocultarlas no oculta datos de la captura.
- `92_SESION` NO DEBE protegerse (los comentarios no se pueden crear en hojas protegidas).

### C-06 · Control de acceso por capas (RBAC)
1. **SharePoint / OneDrive** decide quién abre y quién edita el archivo (única barrera de seguridad real).
2. **`02_USUARIOS`** asigna el rol por correo; cada script lo valida (`BODEGA`/`ADMIN` en 10A, `VENTAS`/`ADMIN` en 10B).
3. **Protección de hojas + «Permitir editar rangos»**: la captura es el único rango editable; opcionalmente con
   contraseña por rol (`MINV_V2_PWD_BODEGA`, `MINV_V2_PWD_VENTAS`). Es una barrera anti-accidentes (R-07).
- Excel NO permite restringir un rango por correo electrónico en la web: esa restricción la hace el script (capa 2).
  Detalle operativo en `docs/deployment/sharepoint-rbac-policies.md`.

### C-07 · Poka-yoke en dos niveles
- **Visual**: si la cantidad dejaría el stock en negativo, la celda `Cantidad` (y `Disponible`) de la captura se tiñe
  de **rojo sangre `#8A0303` con texto blanco tachado** mediante formato condicional sobre una columna auxiliar oculta
  (`Queda`). Los formatos condicionales y validaciones NO DEBEN usar referencias estructuradas (R-08).
- **Transaccional**: el script recalcula el disponible desde las bitácoras (no confía en fórmulas) y **bloquea** la
  consolidación. Después de insertar una salida o un ajuste negativo vuelve a verificar: si otra sesión se adelantó y
  el stock quedó negativo, marca su propio registro `✖ Rechazado: …` (rojo tachado, no suma) y lo informa.

### C-08 · Lectura a demanda (lazy)
- `15_STOCK` y `16_ALERTAS` son **instantáneas de valores** que reconstruye `RecalcularStock.ts` con una escritura por
  tabla; `stkActualizado`, `stkActualizadoPor`, `stkActualizadoNombre` y `stkMovimientos` registran cuándo y quién.
- Las portadas muestran la frescura (`kpiNuevosDesdeCalculo`: movimientos con `Timestamp` posterior al cálculo).
- El libro se mantiene en cálculo **automático** porque sus fórmulas son livianas (≈ 2.800). NO DEBE usarse el cálculo
  manual como mecanismo: en Excel es una opción por libro y por sesión (no por hoja) y congelaría el disponible y el
  poka-yoke de la captura. Si el administrador lo activa por rendimiento, `RecalcularStock.ts` también ejecuta
  `calculate(full)`.
- NO DEBEN reintroducirse proyecciones pesadas en vivo (SUMAR.SI.CONJUNTO por producto, rankings de 500 filas).

### C-09 · Interfaz para Excel en la web
- La interfaz se construye con **celdas**: navegación y botones son hipervínculos internos en celdas (o `HIPERVINCULO`),
  tarjetas y listas son celdas con formato. NO DEBEN usarse formas con vínculo, cuadros de texto vinculados a celdas ni
  imágenes clicables (en la web no son fiables). PUEDEN usarse gráficos nativos.
- Los botones de script los inserta Excel (Automatizar › script › *Agregar en el libro*) sobre los marcadores `⚙`.

### C-10 · Office Scripts
- Un archivo por script en `src/office-scripts/` (Office Scripts no importa módulos). El código compartido vive en
  `lib/comun.ts` y se copia entre los marcadores con `tools/office_scripts.py sync`; `check` DEBE pasar.
- TypeScript estricto: sin `any`, sin sintaxis no borrable (enums propios, namespaces), sin dependencias externas.
- Toda escritura en hojas protegidas DEBE ir dentro de `conHojasDesbloqueadas`: pausa la protección solo para la sesión
  (`pauseProtection`) o, si el anfitrión no lo permite, desprotege y vuelve a proteger con las mismas opciones, siempre
  en `finally`.
- El resultado para el usuario se escribe en su fila de captura (`Resultado`), nunca en una celda compartida.
- La contraseña se inyecta en la versión instalable (`build/office-scripts/`, fuera de Git). NO DEBE versionarse.

### C-11 · Paridad de algoritmos
- El proyector de `tools/minv2/lectura.py` (demo y pruebas) y `RecalcularStock.ts` implementan la misma regla de stock,
  semáforo y prioridad de alertas con la misma aritmética (`r6`). Si una cambia, DEBE cambiar la otra; la prueba
  `RecalcularStock: instantánea idéntica a la del generador` lo verifica.

### C-12 · Definición de terminado de la V2
Un cambio en el libro colaborativo o en sus scripts está terminado solo si:
1. `tools/build_v2.ps1` termina sin fallas: genera Core y Release, verifica el bloque común, ejecuta las pruebas de los
   Office Scripts (`tests/office-scripts`) y pasa `tools/verify_minv_v2.ps1` en ambos libros.
2. Las capturas se revisaron y la documentación refleja el cambio (este documento, el diccionario de datos V2 y la
   guía de despliegue).
3. En cada despliegue real se ejecuta `DiagnosticoInstalacion` en el tenant del cliente y su informe queda sin `✖`.

## 2. Qué cambia respecto de las reglas de la V1

| Regla V1 | En la V2 colaborativa |
|---|---|
| R-02 Bitácora única `10_MOVIMIENTOS` | Dos fragmentos (`10A`, `10B`) con el mismo esquema; captura por usuario + script (C-01 a C-03) |
| R-04 Stock por fórmulas en vivo | Instantánea de valores reconstruida a demanda (C-08) |
| R-05 `Estado` por fórmula en cada fila | Validación viva en la captura + validación autoritativa en el script; la bitácora guarda `✔ Consolidado` o `✖ Rechazado` |
| R-06 Filas alineadas 05 ↔ 15 | La instantánea se escribe compacta en el orden del catálogo |
| R-13 VBA (edición Plus) | Office Scripts (TypeScript), sin macros en el `.xlsx` (C-10) |

## 3. Checklist para agentes

- [ ] ¿El cambio crea una celda o zona que dos personas podrían editar a la vez? → rediseñar (C-02).
- [ ] ¿Algo escribe en `tblEntradas`/`tblSalidas` fuera de un Office Script, o edita filas existentes? → rechazar (C-03).
- [ ] ¿Se usa un contador compartido para IDs? → usar el ID sin coordinación (C-04).
- [ ] ¿Se agregó una fórmula pesada en vivo o se propuso cálculo manual? → instantánea a demanda (C-08).
- [ ] ¿Se agregó una forma con vínculo o un cuadro vinculado? → celdas (C-09).
- [ ] ¿Se tocó `lib/comun.ts`? → `python tools/office_scripts.py sync` y pruebas (C-10).
- [ ] ¿Cambió la regla de stock o alertas? → mismo cambio en Python y TypeScript (C-11).
