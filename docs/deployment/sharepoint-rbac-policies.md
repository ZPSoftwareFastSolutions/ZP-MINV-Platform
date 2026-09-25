# Despliegue en Microsoft 365 y control de acceso (RBAC) · M-INV V2.1

Guía para el administrador de Z&P o del cliente. Resume **quién puede hacer qué** y en qué capa se controla, y los pasos
para publicar el libro colaborativo con sus Office Scripts. Reglas técnicas: `.claude/v2-concurrency-rules.md`.
Versión paso a paso para usuarios (demo, producción, uso diario y solución de problemas):
[`inicio-rapido.md`](inicio-rapido.md).

## 1. Requisitos previos

| Requisito | Detalle |
|---|---|
| Licencias | Microsoft 365 empresarial o educativo con acceso a Office Scripts (p. ej. Business Standard/Premium, E3, E5). Las cuentas personales e invitados anónimos no ejecutan scripts. |
| Office Scripts habilitado | Centro de administración de Microsoft 365 › *Configuración* › *Configuración de la organización* › *Office Scripts*: activado para los usuarios de M-INV. |
| Almacenamiento | Una biblioteca de SharePoint (recomendado) o OneDrive para el trabajo o la escuela. |
| Clientes | Excel para la web (Edge/Chrome) o Excel de escritorio de Microsoft 365 con la pestaña **Automatizar**. |
| Cuentas | Cada persona inicia sesión con su cuenta de trabajo: su correo es la identidad que auditan los scripts. |

## 2. Matriz de roles

| Rol (`02_USUARIOS`) | Permiso en SharePoint | Hojas donde escribe | Scripts que puede ejecutar con efecto | Portada |
|---|---|---|---|---|
| **ADMIN** | Control total (o Editar + dueño del sitio) | Su fila en 10A, 10B y 17_CONSULTA; conteos; maestros y usuarios (desprotegiendo con la contraseña de hojas) | Todos; instala scripts, botones y el flujo de Power Automate | `00_PORTADA_GERENCIA` (y las demás) |
| **BODEGA** | Editar | Su fila de captura en `10A_ENTRADAS`, su fila en `17_CONSULTA`, la columna *Conteo* de `13_CONTEO` | `RegistrarEntrada` (ENTRADA, SALDO INICIAL, AJUSTE ±), `GenerarAjustesConteo`, `RecalcularStock` | `00_PORTADA_BODEGA` |
| **VENTAS** | Editar | Su fila de captura en `10B_SALIDAS` y su fila en `17_CONSULTA` | `RegistrarSalida`, `RecalcularStock` | `00_PORTADA_VENTAS` |
| **CONSULTA** | Leer | Ninguna | Ninguno (con permiso de lectura Excel no ejecuta scripts que escriben) | `00_PORTADA_GERENCIA` (solo lectura) |

`ResumenDiario` es de solo lectura: lo ejecuta el flujo programado de Power Automate con la cuenta del ADMIN (sección 6).
`DiagnosticoInstalacion` lo ejecuta cualquier editor desde el editor de código.

Cómo se aplica cada columna:

1. **SharePoint** (seguridad real): quién puede abrir y quién puede editar el archivo.
2. **`02_USUARIOS`** (lo valida cada script): un correo sin fila, inactivo o con otro rol recibe «✖ Bloqueado» y nada
   se escribe en la bitácora oficial.
3. **Protección de hojas** (anti-accidentes): todo está bloqueado salvo los rangos de captura.

> Excel para la web no permite restringir un rango a correos específicos («Permitir editar rangos» solo admite
> contraseñas). Por eso la autorización por correo la hace el script (capa 2) y los rangos llevan, si se desea, una
> contraseña por rol.

## 3. Protección por hoja

| Hoja | Protegida | Rango editable («Permitir editar rangos») | Quién | Contraseña del rango |
|---|---|---|---|---|
| `00_PORTADA_BODEGA`, `00_PORTADA_VENTAS`, `00_PORTADA_GERENCIA`, `99_AYUDA` | Sí | — (solo vínculos) | Todos | — |
| `02_USUARIOS`, `04_PROVEEDORES`, `05_PRODUCTOS` | Sí | — | ADMIN (desprotege con la contraseña de hojas) | — |
| `10A_ENTRADAS` | Sí | **Captura Bodega** (`C9:H23`) | BODEGA, ADMIN | `MINV_V2_PWD_BODEGA` (opcional) |
| `10B_SALIDAS` | Sí | **Captura Ventas** (`D9:H23`) | VENTAS, ADMIN | `MINV_V2_PWD_VENTAS` (opcional) |
| `13_CONTEO` | Sí | **Conteo** (`I9:I508`), **Fecha del conteo** (`D7`), **Confirmar conteo** (`H7`) | BODEGA, ADMIN | `MINV_V2_PWD_BODEGA` (opcional) |
| `17_CONSULTA` | Sí | **Consulta** (`C9:C28`: la columna *Producto*, una fila por persona) | ADMIN, BODEGA, VENTAS | — |
| `14_ACTIVIDAD` | Sí | — | Solo los scripts (auditoría) | — |
| `15_STOCK`, `16_ALERTAS`, `18_PEDIDO` | Sí | — | Solo `RecalcularStock` | — |
| `01_CONFIG`, `03`, `06`, `90`, `91` | Sí (ocultas; muy ocultas en Release) | — | ADMIN | — |
| `92_SESION` | **No** (técnica, oculta) | — | Scripts (identidad) | — |

- La contraseña de hojas es `MINV_PASSWORD` (Core) o `MINV_RELEASE_PASSWORD` (Release) al generar el libro, y es la misma
  que llevan los scripts instalables. Los scripts pausan la protección **solo para su sesión** y la reanudan al terminar.
- Si se definen `MINV_V2_PWD_BODEGA`/`MINV_V2_PWD_VENTAS` al generar, las celdas de captura quedan bloqueadas y Excel
  pide la contraseña del rol la primera vez que alguien escribe en ese rango durante la sesión.

## 4. Configuración recomendada de SharePoint

1. Sitio o biblioteca exclusiva «M-INV» con **herencia de permisos rota**.
2. Grupos: `M-INV Administradores` (Control total), `M-INV Operación` (Editar: Bodega y Ventas), `M-INV Consulta` (Leer).
3. Uso compartido: solo personas de la organización; sin vínculos anónimos; sin «cualquiera con el vínculo puede editar».
4. **Control de versiones** activado (versiones principales, 500): permite restaurar el libro completo si algo sale mal.
5. **No** exigir desprotección (check-out): impide la coautoría.
6. Opcional: etiqueta de confidencialidad de Microsoft Purview si el inventario es información sensible.

## 5. Publicación paso a paso

1. **Generar** el Release con contraseñas propias (en PowerShell, en la carpeta del repositorio):

   ```powershell
   $env:MINV_RELEASE_PASSWORD = '<contraseña de hojas>'          # obligatoria para un cliente real
   $env:MINV_V2_PWD_BODEGA = '<opcional>'; $env:MINV_V2_PWD_VENTAS = '<opcional>'
   powershell -ExecutionPolicy Bypass -File tools\build_v2.ps1
   ```

   Resultado: `releases/M-INV_V2_Colaborativo_Produccion.xlsx` y los 6 scripts instalables en
   `build/office-scripts/release/` (contienen la contraseña: no los versione ni los envíe por correo).
2. **Subir** el libro a la biblioteca y ajustar permisos (sección 4).
3. **Registrar usuarios** en `02_USUARIOS` (desproteja la hoja, complete Correo, Nombre, Rol y Activo, vuelva a
   proteger). La fila de captura de cada persona aparece sola en 10A/10B.
4. **Instalar los scripts**: abra el libro en Excel › pestaña **Automatizar** › **Nuevo script**, pegue el contenido de
   cada archivo de `build/office-scripts/release/` y guárdelo con el mismo nombre:
   `RegistrarEntrada`, `RegistrarSalida`, `RecalcularStock`, `GenerarAjustesConteo`, `ResumenDiario`,
   `DiagnosticoInstalacion`.
5. **Diagnóstico**: ejecute `DiagnosticoInstalacion` desde el editor de código. Debe terminar en
   «RESULTADO: instalación correcta» (hojas, tablas y nombres de la 2.1, identidad, rol, filas de captura y de consulta,
   contraseña de cada hoja que escriben los scripts). El informe también queda en `92_SESION` y en `14_ACTIVIDAD`.
6. **Botones**: desproteja temporalmente cada hoja, seleccione la celda del recuadro amarillo `⚙` y, en el editor de
   código del script, use *… › Agregar en el libro*. Así el script queda **compartido con el libro** y cualquier editor
   puede ejecutarlo. Botones y ubicación:

   | Hoja | Recuadro | Script |
   |---|---|---|
   | `10A_ENTRADAS` | «Registrar en bodega» (J6:K6) | `RegistrarEntrada` |
   | `10B_SALIDAS` | «Registrar salida» (J6:K6) | `RegistrarSalida` |
   | `13_CONTEO` | «Generar ajustes del conteo» (J6:M6) | `GenerarAjustesConteo` |
   | `15_STOCK` (G6:K6), `16_ALERTAS` (H6:M6), `18_PEDIDO` (I6:M6) | «Recalcular stock» | `RecalcularStock` |
   | Portadas de Bodega, Ventas y Gerencia | «Recalcular stock» (J7:M7) | `RecalcularStock` |

   Vuelva a proteger las hojas con la misma contraseña.
7. **Primeros pasos**: pulse «Recalcular stock» y cargue el `SALDO INICIAL` de cada producto: desde 10A o, más rápido,
   con una toma física completa en `13_CONTEO` («Generar ajustes del conteo» crea el SALDO INICIAL de los productos sin
   movimientos). Revise que la lista de *Primeros pasos* de `99_AYUDA` quede en ✔.
8. **Resumen diario** (opcional): configure el flujo de Power Automate de la sección 6.

## 6. Resumen diario con Power Automate

`ResumenDiario.ts` no escribe en el libro: devuelve `asunto`, `html`, `texto` y los indicadores (`movimientosHoy`,
`unidadesVendidasHoy`, `rechazosHoy`, `bloqueosHoy`, `agotados`, `criticos`, `bajos`, `totalPedido`), calculados con el
stock exacto de las bitácoras.

1. Power Automate › **Flujo de nube programado** (ej. cada día a las 7:00, zona horaria de la empresa).
2. **Excel Online (Business) › Ejecutar script**: ubicación, biblioteca, archivo del libro y script `ResumenDiario`.
3. **Office 365 Outlook › Enviar un correo electrónico (V2)**: asunto = `asunto`; cuerpo = `html`.
4. Opcional: condición `agotados > 0` o `bloqueosHoy > 0` para enviar solo cuando haya algo que atender.

El flujo corre con la cuenta de quien lo crea (ADMIN), que necesita permiso de edición del archivo para ejecutar
scripts; la cuenta del flujo no necesita fila en `02_USUARIOS` porque el resumen no registra nada.

## 7. Operación

| Tarea | Cómo |
|---|---|
| Alta de usuario | Agregar al **final** de `02_USUARIOS` (nunca reordenar) cuando nadie esté registrando |
| Baja de usuario | `Activo = NO` en `02_USUARIOS` y retirar el permiso en SharePoint |
| Auditoría | `14_ACTIVIDAD` (cada ejecución: quién, cuándo, script, resultado y detalle, también los bloqueos) y las columnas N:P de 10A/10B (ADMIN): filtre en su Vista de hoja por `Usuario_O365` o `Timestamp` |
| Toma física | Recalcular stock → cada persona cuenta su zona en `13_CONTEO` → el responsable escribe `SI` en *Confirmar* y pulsa «Generar ajustes del conteo» → Recalcular stock |
| Pedido a proveedores | Recalcular stock → `18_PEDIDO` → filtrar por proveedor en su Vista de hoja → Archivo › Imprimir o PDF |
| Rotar contraseña | Regenerar con la nueva `MINV_RELEASE_PASSWORD`, cambiar la contraseña de las hojas del libro en uso y reinstalar los scripts |
| Restaurar | Historial de versiones de SharePoint (el libro completo) |
| Capacidad | Las bitácoras crecen al agregar filas; archive un período (libro nuevo con `SALDO INICIAL`) al superar ~20.000 registros por fragmento o si «Recalcular stock» tarda más de un minuto |

## 8. Limitaciones conocidas

- La protección de Excel evita errores, no ataques: un editor que conozca la contraseña (visible en el código de los
  scripts compartidos) puede desproteger. La trazabilidad (`Usuario_O365`, `Timestamp`, historial de versiones de
  SharePoint) y los permisos de SharePoint son la defensa real.
- Excel para la web no restringe rangos por correo; los scripts sí (capa 2).
- La identidad se obtiene con un comentario temporal (la API no expone el usuario). Requiere que la hoja `92_SESION`
  exista y esté sin proteger.
- Sin conexión no hay coautoría ni scripts: para trabajar sin internet use la edición local M-INV V1.2.
- La toma física compara contra el stock exacto **al generar los ajustes**: si se vende mientras se cuenta, registre
  esas ventas antes de generar los ajustes (o cuente en un horario sin movimientos).
- `17_CONSULTA` admite 20 usuarios operativos (ADMIN, BODEGA, VENTAS) y las capturas 15 por rol: más usuarios requieren
  ampliar las capacidades en el generador (`tools/minv2/base2.py`).
- Google Sheets no ejecuta Office Scripts; una versión para Google requeriría portar los scripts a Apps Script.
