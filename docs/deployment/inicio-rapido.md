# Inicio rápido · Cómo entrar y usar M-INV V2.1 (paso a paso)

Esta guía es el «algoritmo» para poner en marcha el libro colaborativo y usarlo a diario. Hay tres caminos; elija el
suyo:

| Camino | Para qué | Tiempo | Qué necesita |
|---|---|---|---|
| **A. Mirar la demo** | Recorrer pantallas, colores y datos de ejemplo | 5 min | Excel (escritorio o web) |
| **B. Demo funcionando en la nube** | Probar registros, conteo y botones de verdad con su cuenta | 30 min | Cuenta de Microsoft 365 de trabajo con Office Scripts |
| **C. Puesta en producción** | Libro limpio para la empresa, con usuarios, permisos y contraseña propia | 1–2 h | Lo de B + administrador de SharePoint |

Después de instalar, la sección **D** explica el uso diario por rol y la **E** el resumen diario por correo.

```text
ALGORITMO GENERAL
 1. ¿Solo quiere ver?            → Camino A (abrir el archivo; los botones de script aún no funcionan).
 2. ¿Quiere probar con su cuenta? → Camino B (subir la demo a OneDrive, instalar 6 scripts, registrarse como ADMIN).
 3. ¿Va a usarlo en la empresa?   → Camino C (generar el Release con contraseña propia, SharePoint, usuarios, scripts).
 4. Cada día, cada persona:
      abrir el enlace → ir a la portada de su rol → seguir «Próximo paso» → escribir en SU fila → pulsar el botón
      → leer «Resultado» en su fila → (Bodega/Gerencia) «Recalcular stock» cuando necesite la instantánea al día.
```

---

## A. Mirar la demo (5 minutos)

1. En la carpeta del repositorio abra `src/M-INV_V2_Colaborativo.xlsx` con doble clic (Excel de escritorio) o súbalo a
   su OneDrive y ábralo en el navegador.
2. Llegará a **00_PORTADA_BODEGA**. Use la barra oscura de arriba para moverse: ⌂ Bodega, ⌂ Ventas, ◈ Gerencia,
   ⇩ Entradas, ⇧ Salidas, ⌕ Consulta, ▦ Stock, ✚ Pedido, ✓ Conteo, ? Guía.
3. Qué mirar:
   - **◈ Gerencia**: indicadores, gráficos, los 10 más vendidos y la actividad de cada usuario.
   - **⇧ Salidas**: la fila de Carlos Ruiz en **rojo sangre tachado** (quiere vender más de lo que hay: poka-yoke).
   - **⌕ Consulta**: tres personas consultando productos a la vez, cada una en su fila.
   - **✓ Conteo**: una toma física en curso con un sobrante (▲) y un faltante (▼).
   - **✚ Pedido**: el pedido sugerido agrupado por proveedor.
4. Los recuadros amarillos `⚙` marcan dónde van los botones de los scripts: en este camino **no funcionan** porque los
   scripts aún no están instalados (eso es el camino B).

> La contraseña de las hojas de la demo es `minv-dev` (Revisar › Desproteger hoja). Úsela solo para mirar fórmulas: no
> edite la bitácora oficial a mano.

---

## B. Demo funcionando en la nube (30 minutos)

**Requisitos:** cuenta de trabajo o educativa de Microsoft 365 con la pestaña **Automatizar** en Excel (Office Scripts
habilitado por su administrador; las cuentas personales @outlook/@hotmail no tienen Office Scripts).

1. **Genere los scripts instalables de la demo** (una sola vez, en PowerShell, dentro de la carpeta del repositorio;
   solo necesita Python 3):

   ```powershell
   python tools\office_scripts.py deploy --edicion core
   ```

   Quedan en `build\office-scripts\core\` con la contraseña `minv-dev` ya puesta. (Sin Python: copie los archivos de
   `src\office-scripts\` y reemplace el texto `__MINV_PASSWORD__` por `minv-dev`.)
2. **Suba** `src/M-INV_V2_Colaborativo.xlsx` a su OneDrive (o a una biblioteca de SharePoint) y ábralo con
   **Excel para la web** (o en el escritorio con Microsoft 365: el archivo debe seguir guardado en la nube).
3. **Regístrese como ADMIN** (la demo trae usuarios ficticios y los scripts rechazan correos que no estén en la lista):
   1. Vaya a `02_USUARIOS` › **Revisar › Desproteger hoja** › contraseña `minv-dev`.
   2. En la **primera fila vacía al final** de la tabla escriba su correo de Microsoft 365, su nombre, `ADMIN` y `SI`.
   3. **Revisar › Proteger hoja** con la misma contraseña (deje marcadas las opciones que aparecen).

   Su fila de captura aparece sola en 10A y 10B (columna *Usuario*) y también su fila en 17_CONSULTA.
4. **Instale los 6 scripts**: pestaña **Automatizar › Nuevo script**. Por cada archivo de `build\office-scripts\core\`:
   borre el código de ejemplo, pegue el contenido completo del archivo y guarde el script con **el mismo nombre**:
   `RegistrarEntrada`, `RegistrarSalida`, `RecalcularStock`, `GenerarAjustesConteo`, `ResumenDiario`,
   `DiagnosticoInstalacion`.
5. **Diagnóstico**: abra `DiagnosticoInstalacion` y pulse **Ejecutar**. Debe terminar en
   «RESULTADO: instalación correcta». Si aparece `✖`, el mismo informe dice qué corregir (ver la sección F).
6. **Botones**: en cada hoja de la tabla siguiente, desproteja la hoja, seleccione la primera celda del recuadro
   amarillo `⚙`, abra el script en el editor y use **… (Más opciones) › Agregar en el libro**. Mueva el botón sobre el
   recuadro y vuelva a proteger la hoja.

   | Hoja | Recuadro `⚙` | Script |
   |---|---|---|
   | `10A_ENTRADAS` | J6:K6 «Registrar en bodega» | `RegistrarEntrada` |
   | `10B_SALIDAS` | J6:K6 «Registrar salida» | `RegistrarSalida` |
   | `13_CONTEO` | J6:M6 «Generar ajustes del conteo» | `GenerarAjustesConteo` |
   | `15_STOCK` | G6:K6 «Recalcular stock» | `RecalcularStock` |
   | `16_ALERTAS` | H6:M6 «Recalcular stock» | `RecalcularStock` |
   | `18_PEDIDO` | I6:M6 «Recalcular stock» | `RecalcularStock` |
   | Portadas de Bodega, Ventas y Gerencia | J7:M7 «Recalcular stock» | `RecalcularStock` |

7. **Pruebe**:
   - Pulse **Recalcular stock**: la barra dice «Calculado el … por <usted>» y aparece una fila en `14_ACTIVIDAD`.
   - En `10B_SALIDAS`, en SU fila: elija un producto, escriba una cantidad y pulse **Registrar salida**. Lea
     *Resultado*: «✔ Registrado S-…». Pruebe una cantidad mayor que el disponible: se pone roja y el script la bloquea.
   - En `13_CONTEO`: escriba un conteo en la columna *Conteo*, escriba `SI` en *Confirmar* (H7) y pulse **Generar
     ajustes del conteo**. El ajuste aparece al final de la bitácora de 10A con el documento `CF-AAAAMMDD`.
   - Invite a otra persona (Compartir), regístrela en `02_USUARIOS` y registren **a la vez**: cada uno en su fila.

---

## C. Puesta en producción (libro limpio para la empresa)

**Requisitos adicionales:** Windows con Excel 2016 o posterior, Python 3.11+ y Node.js 22.18+ (solo en el equipo que
genera el libro) y un administrador de SharePoint.

1. **Prepare el entorno** (una vez):

   ```powershell
   python -m venv .venv
   .venv\Scripts\python -m pip install -r tools\requirements.txt
   ```

2. **Genere el Release con SU contraseña** (no use la de desarrollo en un cliente real):

   ```powershell
   $env:MINV_RELEASE_PASSWORD = '<contraseña de hojas del cliente>'
   $env:MINV_V2_PWD_BODEGA = '<opcional: contraseña del rango de Bodega>'
   $env:MINV_V2_PWD_VENTAS = '<opcional: contraseña del rango de Ventas>'
   powershell -ExecutionPolicy Bypass -File tools\build_v2.ps1
   ```

   Debe terminar en «RESULTADO: todos los pasos sin fallas». Obtiene:
   - `releases\M-INV_V2_Colaborativo_Produccion.xlsx` (libro limpio: sin usuarios, catálogo ni movimientos);
   - `build\office-scripts\release\` (los 6 scripts con la contraseña: **no** los suba a Git ni los envíe por correo).
3. **SharePoint** (administrador): biblioteca «M-INV» con permisos propios; grupos *Administradores* (Control total),
   *Operación* (Editar: Bodega y Ventas) y *Consulta* (Leer); control de versiones activado; sin desprotección
   obligatoria (check-out). Detalle: [`sharepoint-rbac-policies.md`](sharepoint-rbac-policies.md).
4. **Suba** el Release a la biblioteca y ábralo en Excel para la web.
5. **Usuarios**: desproteja `02_USUARIOS` con la contraseña del cliente y registre a cada persona (correo de Microsoft
   365, nombre, rol `ADMIN`/`BODEGA`/`VENTAS`/`CONSULTA`, `SI`). Siempre **al final** de la tabla y sin reordenar.
6. **Maestros**: complete `04_PROVEEDORES` y `05_PRODUCTOS` (desprotegiendo con la contraseña). Categorías y unidades
   están en hojas ocultas de configuración (`03_CATEGORIAS`, `06_UNIDADES`).
7. **Scripts, diagnóstico y botones**: igual que en B.4 a B.6, pero con los archivos de `build\office-scripts\release\`.
8. **Saldo inicial** (la forma más rápida): en `13_CONTEO` cuenten todo el inventario (cada persona su zona), escriba
   `SI` en *Confirmar* y pulse **Generar ajustes del conteo**: crea el `SALDO INICIAL` de cada producto contado. Luego
   **Recalcular stock** y revise que *Primeros pasos* de `99_AYUDA` quede todo en ✔.
9. **Comparta el enlace** del archivo (no copias) con cada persona según su rol.

---

## D. Uso diario por rol

| Rol | Algoritmo |
|---|---|
| **Bodega** | Abrir el enlace → **⌂ Bodega** → leer «Próximo paso» → *Registrar entrada* o *Registrar ajuste* (su fila en 10A: Tipo, Producto, Cantidad, Documento; en ajustes, Observaciones) → **Registrar en bodega** → leer *Resultado*. Toma física: **✓ Conteo** → contar su zona → el responsable escribe `SI` y pulsa **Generar ajustes**. |
| **Ventas** | Abrir el enlace → **⌂ Ventas** → **⌕ Consultar producto** (su fila en 17_CONSULTA: disponible exacto, cobertura, último movimiento) → *Registrar salida* (su fila en 10B) → **Registrar salida** → leer *Resultado*. Si la cantidad se pone roja tachada: no hay stock suficiente. |
| **Gerencia / ADMIN** | Abrir el enlace → **◈ Gerencia** → **Recalcular stock** si la barra dice «⚠ … recalcule» → revisar indicadores, alertas y **✚ Pedido** (filtrar por proveedor en su Vista de hoja e imprimir) → **☰ Actividad** para auditar quién hizo qué. |
| **Consulta** | Abrir el enlace (solo lectura) → **◈ Gerencia**, stock, alertas, pedido y actividad. |

Reglas de oro: escriba solo en **su** fila; nunca borre ni edite la bitácora oficial (los errores se corrigen con un
`AJUSTE`); para filtrar u ordenar use **Vista › Vista de hoja › Nueva** (sus filtros no afectan a los demás).

---

## E. Resumen diario por correo (opcional, Power Automate)

El script `ResumenDiario` **no modifica el libro**: devuelve el asunto, el HTML y los indicadores del día (movimientos,
bloqueos, productos por reponer y total del pedido sugerido), calculados con el stock exacto.

1. Abra <https://make.powerautomate.com> con la cuenta del administrador › **Crear › Flujo de nube programado**
   (por ejemplo, todos los días a las 7:00).
2. Agregue la acción **Excel Online (Business) › Ejecutar script**: ubicación (SharePoint u OneDrive), biblioteca,
   archivo `M-INV_V2_Colaborativo_Produccion.xlsx` y script `ResumenDiario`.
3. Agregue **Office 365 Outlook › Enviar un correo electrónico (V2)**: *Para* = gerencia; *Asunto* = contenido dinámico
   `asunto`; *Cuerpo* = contenido dinámico `html` (el cuerpo acepta HTML).
4. Opcional: una condición para enviarlo solo si `agotados` > 0 o `bloqueosHoy` > 0.
5. **Guardar** y **Probar › Manualmente**.

---

## F. Si algo no funciona

| Síntoma | Causa probable | Solución |
|---|---|---|
| No aparece la pestaña **Automatizar** | Cuenta personal o Office Scripts deshabilitado | Use una cuenta de trabajo; el administrador de Microsoft 365 activa Office Scripts |
| «✖ … su cuenta no está autorizada en 02_USUARIOS» | Su correo no está en la lista | ADMIN: agregarlo al final de `02_USUARIOS` |
| «No se pudo identificar su cuenta» | Abrió el libro como invitado o sin iniciar sesión | Inicie sesión con su cuenta de trabajo y vuelva a abrir el enlace |
| «La contraseña del script no coincide con la del libro» | Scripts generados con otra contraseña | Regenere con `office_scripts.py deploy` usando la contraseña del libro y reinstale |
| El botón no hace nada / no existe | Script sin agregar al libro | Paso B.6 (… › Agregar en el libro) |
| Excel pide una contraseña al escribir en la captura | Se definió `MINV_V2_PWD_BODEGA`/`VENTAS` | Pida la contraseña del rango a su ADMIN (se pide una vez por sesión) |
| La barra dice «⚠ N movimiento(s) nuevo(s): recalcule» | La instantánea es anterior a los últimos registros | Pulse **Recalcular stock** |
| «✖ Bloqueado: escriba SI en Confirmar» (13_CONTEO) | Falta la confirmación | Escriba `SI` en H7 y vuelva a pulsar |
| El diagnóstico dice «92_SESION está protegida» | Alguien protegió la hoja técnica | Desprotéjala (los scripts crean ahí el comentario de identificación) |

Más detalle: [`sharepoint-rbac-policies.md`](sharepoint-rbac-policies.md) (roles, permisos y protección),
[`../../src/office-scripts/README.md`](../../src/office-scripts/README.md) (cómo funciona cada script) y la hoja
`99_AYUDA` del propio libro.
