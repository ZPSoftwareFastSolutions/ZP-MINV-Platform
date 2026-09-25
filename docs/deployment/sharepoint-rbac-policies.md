# Despliegue en Microsoft 365 y control de acceso (RBAC) · M-INV V2

Guía para el administrador de Z&P o del cliente. Resume **quién puede hacer qué** y en qué capa se controla, y los pasos
para publicar el libro colaborativo con sus Office Scripts. Reglas técnicas: `.claude/v2-concurrency-rules.md`.

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
| **ADMIN** | Control total (o Editar + dueño del sitio) | Su fila en 10A y en 10B; maestros y usuarios (desprotegiendo con la contraseña de hojas) | Todos; instala scripts y botones | Ambas |
| **BODEGA** | Editar | Su fila de captura en `10A_ENTRADAS` | `RegistrarEntrada` (ENTRADA, SALDO INICIAL, AJUSTE ±), `RecalcularStock` | `00_PORTADA_BODEGA` |
| **VENTAS** | Editar | Su fila de captura en `10B_SALIDAS` | `RegistrarSalida`, `RecalcularStock` | `00_PORTADA_VENTAS` |
| **CONSULTA** | Leer | Ninguna | Ninguno (con permiso de lectura Excel no ejecuta scripts que escriben) | Ambas (solo lectura) |

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
| `00_PORTADA_BODEGA`, `00_PORTADA_VENTAS`, `99_AYUDA` | Sí | — (solo vínculos) | Todos | — |
| `02_USUARIOS`, `04_PROVEEDORES`, `05_PRODUCTOS` | Sí | — | ADMIN (desprotege con la contraseña de hojas) | — |
| `10A_ENTRADAS` | Sí | **Captura Bodega** (`C9:H23`) | BODEGA, ADMIN | `MINV_V2_PWD_BODEGA` (opcional) |
| `10B_SALIDAS` | Sí | **Captura Ventas** (`D9:H23`) | VENTAS, ADMIN | `MINV_V2_PWD_VENTAS` (opcional) |
| `15_STOCK`, `16_ALERTAS` | Sí | — | Solo `RecalcularStock` | — |
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

   Resultado: `releases/M-INV_V2_Colaborativo_Produccion.xlsx` y los scripts instalables en
   `build/office-scripts/release/` (contienen la contraseña: no los versione ni los envíe por correo).
2. **Subir** el libro a la biblioteca y ajustar permisos (sección 4).
3. **Registrar usuarios** en `02_USUARIOS` (desproteja la hoja, complete Correo, Nombre, Rol y Activo, vuelva a
   proteger). La fila de captura de cada persona aparece sola en 10A/10B.
4. **Instalar los scripts**: abra el libro en Excel › pestaña **Automatizar** › **Nuevo script**, pegue el contenido de
   cada archivo de `build/office-scripts/release/` y guárdelo con el mismo nombre:
   `RegistrarEntrada`, `RegistrarSalida`, `RecalcularStock`, `DiagnosticoInstalacion`.
5. **Diagnóstico**: ejecute `DiagnosticoInstalacion` desde el editor de código. Debe terminar en
   «RESULTADO: instalación correcta» (identidad, rol, filas de captura, contraseña y tablas). El informe también queda en
   `92_SESION`.
6. **Botones**: desproteja temporalmente cada hoja, seleccione la celda del recuadro amarillo `⚙` y, en el editor de
   código del script, use *… › Agregar en el libro*. Así el script queda **compartido con el libro** y cualquier editor
   puede ejecutarlo. Botones y ubicación:

   | Hoja | Recuadro | Script |
   |---|---|---|
   | `10A_ENTRADAS` | «Registrar en bodega» (J6:K6) | `RegistrarEntrada` |
   | `10B_SALIDAS` | «Registrar salida» (J6:K6) | `RegistrarSalida` |
   | `15_STOCK`, `16_ALERTAS`, portadas | «Recalcular stock» | `RecalcularStock` |

   Vuelva a proteger las hojas con la misma contraseña.
7. **Primeros pasos**: pulse «Recalcular stock», cargue el `SALDO INICIAL` de cada producto desde 10A y revise que la
   lista de *Primeros pasos* de `99_AYUDA` quede en ✔.

## 6. Operación

| Tarea | Cómo |
|---|---|
| Alta de usuario | Agregar al **final** de `02_USUARIOS` (nunca reordenar) cuando nadie esté registrando |
| Baja de usuario | `Activo = NO` en `02_USUARIOS` y retirar el permiso en SharePoint |
| Auditoría | Mostrar las columnas N:P de 10A/10B (ADMIN) y filtrar por `Usuario_O365` o `Timestamp` |
| Rotar contraseña | Regenerar con la nueva `MINV_RELEASE_PASSWORD`, cambiar la contraseña de las hojas del libro en uso y reinstalar los scripts |
| Restaurar | Historial de versiones de SharePoint (el libro completo) |
| Capacidad | Las bitácoras crecen al agregar filas; archive un período (libro nuevo con `SALDO INICIAL`) al superar ~20.000 registros por fragmento o si «Recalcular stock» tarda más de un minuto |

## 7. Limitaciones conocidas

- La protección de Excel evita errores, no ataques: un editor que conozca la contraseña (visible en el código de los
  scripts compartidos) puede desproteger. La trazabilidad (`Usuario_O365`, `Timestamp`, historial de versiones de
  SharePoint) y los permisos de SharePoint son la defensa real.
- Excel para la web no restringe rangos por correo; los scripts sí (capa 2).
- La identidad se obtiene con un comentario temporal (la API no expone el usuario). Requiere que la hoja `92_SESION`
  exista y esté sin proteger.
- Sin conexión no hay coautoría ni scripts: para trabajar sin internet use la edición local M-INV V1.2.
- Google Sheets no ejecuta Office Scripts; una versión para Google requeriría portar los scripts a Apps Script.
