# Cliente de escritorio M-INV V3.1 · guía de la interfaz

La V3.1 reemplaza las tablas «tipo Excel» de la V3 por una aplicación de escritorio completa: pantalla de carga, inicio
de sesión con demostración, menú lateral según el rol, tablero con gráficos, registro guiado con vista previa y
poka-yoke, toma física, alertas y pedido por proveedor, ficha del producto con kardex, actividad, configuración y ayuda,
en tema claro y oscuro. El ejecutable se llama **`M-INV.exe`**.

Todas las imágenes de esta guía las genera la propia aplicación (`M-INV.exe --capturas`, ver al final) con la
demostración: los datos reales del libro colaborativo de la V2.1 migrados a memoria.

## 1. Arranque

| Pantalla | Qué hace |
|---|---|
| ![Pantalla de carga](capturas/v3.1/01-pantalla-de-carga.png) | **Pantalla de carga**: prepara la aplicación, carga las preferencias de la estación (tema, periféricos) y comprueba en menos de 5 segundos si PostgreSQL responde y si su versión es 15 o superior. |
| ![Inicio de sesión](capturas/v3.1/02-inicio-de-sesion.png) | **Inicio de sesión**: empresa, correo y contraseña (recuerda empresa y correo, nunca la contraseña), aviso de Bloq Mayús y estado de la base de datos con «Reintentar». Si no hay base, sugiere la demostración. |
| ![Demostración](capturas/v3.1/03-demostracion-elegir-rol.png) | **Explorar la demostración**: migra el libro de la V2.1 a memoria (34 productos, 473 movimientos, auditoría) con el mismo importador de `minv import-v21`, verifica la paridad y deja elegir con qué persona y rol entrar. Nada se guarda en disco. |

## 2. Ventana principal

![Inicio](capturas/v3.1/04-inicio.png)

- **Menú lateral por secciones** (General, Inventario, Reposición, Control) que muestra solo lo que el rol puede usar
  (matriz RBAC). Se contrae a solo íconos con el botón ☰ o `Ctrl+B`; la preferencia se recuerda.
- **Barra superior**: búsqueda global de productos (`Ctrl+K`, por nombre, SKU o código de barras, sin importar tildes),
  aviso de **DEMOSTRACIÓN**, fecha y hora, tema claro/oscuro y campana con el número de alertas.
- **Cuenta** (abajo a la izquierda): nombre, rol, empresa y conexión; cambiar contraseña, configuración y cerrar sesión.
- **Avisos** en la esquina inferior derecha (✔ verde, ⚠ ámbar, ✖ rojo: los mismos significados de la V2.1) y
  **confirmaciones** dentro de la ventana para lo que no se puede deshacer.

## 3. Pantallas

| Pantalla | Qué hace |
|---|---|
| **Inicio** | Saludo y próximo paso sugerido según el rol; valor del inventario, alertas, pedido sugerido y movimientos de hoy (tarjetas que llevan a su pantalla); entradas y salidas de 14 días (pase el mouse por las columnas); semáforo en dona (clic en un estado filtra el stock); alertas urgentes, más vendidos, últimos movimientos y actividad reciente. |
| ![Stock](capturas/v3.1/05-stock.png) | **Stock** al instante (sin «Recalcular»): búsqueda por SKU, nombre o proveedor; chips por estado con contador; filtro por categoría; columnas ordenables; barra de nivel de color según el semáforo; exportación a Excel (CSV en español); doble clic o 👁 abre la ficha y ＋ registra un movimiento del producto. La tabla es virtualizada: 100.000 filas sin demora. |
| ![Registrar movimiento](capturas/v3.1/06-registrar-movimiento.png) | **Registrar movimiento** en tres pasos: tipo (solo los que permite el rol), producto (buscador o escáner) y posición, cantidad (± y Enter para registrar), documento y observaciones (obligatorias en ajustes y devoluciones). La **vista previa** muestra el stock total, el de la posición y lo que quedará. |
| ![Poka-yoke](capturas/v3.1/08-poka-yoke-salida-bloqueada.png) | **Poka-yoke visual de la V2.1**: si una salida dejaría la posición en negativo, la vista previa se pinta de **rojo sangre `#8A0303` con el número tachado** y el botón se deshabilita. La validación definitiva la hace el dominio al guardar (con reintento optimista si otra caja se adelantó). |
| ![Toma física](capturas/v3.1/09-toma-fisica.png) | **Toma física**: iniciar (una por almacén), contar producto por producto (a mano o con el escáner: el foco pasa solo a la cantidad), ver sobrantes y faltantes contra el stock exacto, quitar un conteo equivocado, anular, y **Generar ajustes** con confirmación: todos los AJUSTE (+)/(−) en una operación. |
| ![Alertas](capturas/v3.1/11-alertas.png) | **Alertas** priorizadas como 16_ALERTAS de la V2.1: resumen por severidad, chips por estado, acción sugerida, cantidad sugerida y «Registrar entrada» con el producto ya elegido. |
| ![Pedido sugerido](capturas/v3.1/12-pedido-sugerido.png) | **Pedido sugerido** por proveedor (18_PEDIDO): contacto, teléfono, correo, plazo y fecha estimada de entrega, líneas con cantidad a pedir y subtotal; **Copiar** (para correo o WhatsApp) y **Exportar a Excel**. |
| ![Actividad](capturas/v3.1/13-actividad.png) | **Actividad** (auditoría inmutable, 14_ACTIVIDAD): quién hizo qué y cuándo, con su resultado (correcto, rechazado, con error), búsqueda y filtros. Incluye la actividad migrada de la V2.1. |
| ![Ficha del producto](capturas/v3.1/16-ficha-del-producto.png) | **Ficha del producto** (panel lateral, `Esc` la cierra): semáforo, stock, disponible, reservado, mínimo/máximo, costo y valor, gráfico de la evolución del saldo con la línea del mínimo, existencias por posición y lote, y el **kardex** con saldo acumulado. |
| ![Configuración](capturas/v3.1/14-configuracion.png) | **Configuración** de la estación: tema (según Windows, claro, oscuro), impresora ESC/POS (serie, red o USB de Windows) con **página de prueba**, prueba del lector de códigos, sesión y permisos del rol, conexión y versión. |
| ![Ayuda](capturas/v3.1/15-ayuda.png) | **Ayuda**: guías paso a paso por tarea, atajos de teclado y el significado de cada estado del semáforo. |

**Tema oscuro** (sigue al de Windows o se elige en Configuración; `Ctrl+Shift+L` alterna):

| | |
|---|---|
| ![Inicio oscuro](capturas/v3.1/20-oscuro-inicio.png) | ![Registro oscuro](capturas/v3.1/22-oscuro-registro.png) |

**Según el rol** (Ventas no ve la toma física ni la auditoría y solo registra salidas, ventas y devoluciones):

| | |
|---|---|
| ![Ventas: inicio](capturas/v3.1/30-rol-ventas-inicio.png) | ![Ventas: registro](capturas/v3.1/31-rol-ventas-registro.png) |

## 4. Teclado y lector de códigos

| Atajo | Acción |
|---|---|
| `Ctrl+K` | Buscar un producto y abrir su ficha |
| `Ctrl+1` … `Ctrl+7` | Ir a cada pantalla del menú |
| `Ctrl+N` | Registrar un movimiento |
| `F5` | Actualizar la pantalla |
| `Ctrl+B` | Contraer o expandir el menú |
| `Ctrl+Shift+L` | Tema claro / oscuro |
| `Esc` | Cerrar la ficha o la confirmación |
| `F1` | Ayuda |
| ↑ ↓ `Enter` | Moverse y elegir en las sugerencias del buscador |

El **lector de códigos en modo teclado** funciona sin configurar en cualquier pantalla: la aplicación distingue la
ráfaga del lector (teclas a menos de 35 ms + Enter) del tecleo de una persona, quita la lectura del campo donde se
escribió y la entrega a la pantalla: en *Registrar movimiento* y *Toma física* elige el producto; en las demás abre su
ficha; en *Configuración* muestra la lectura para probar el lector.

## 5. Sistema visual

- **Paletas** `Theme/Palette.Light.xaml` y `Theme/Palette.Dark.xaml` con las mismas claves (superficies, texto, marca
  `#1565C0`, semánticos y los siete colores del semáforo). Todo usa `DynamicResource`: el tema cambia sin reiniciar.
- **Estilos** en `Theme/Controls.xaml`: botones (primario, secundario, suave, fantasma, peligro, ícono, enlace, tarjeta),
  campos con ícono, texto de ayuda y anillo de foco, combos, casillas e interruptor, chips de filtro, control
  segmentado, tablas sin cuadrícula con filas de 46–58 px, barras de desplazamiento finas, tooltips, barra y anillo de
  carga, insignia del semáforo y barra de nivel.
- **Controles propios** (`Controls/`): gráfico de dona, columnas dobles con detalle al pasar el mouse y línea en
  escalón del kardex, dibujados con WPF (sin librerías externas).
- **Íconos**: Segoe Fluent Icons (Windows 11) con respaldo en Segoe MDL2 Assets (Windows 10). **Tipografía**: Segoe UI.
- **Ícono de la aplicación**: `Assets/minv.ico`, generado con `tools/generar_icono_escritorio.py`.
- **Español** en toda la interfaz: números y dinero con la cultura de Windows si es española (si no, es-BO), fechas
  `dd/MM/aaaa`, tiempo relativo («hace 5 min») y cantidades que aceptan coma o punto decimal.

## 6. Capturas y ejecutable

```powershell
powershell -ExecutionPolicy Bypass -File tools\build_v3.ps1 -Capturas     # regenera docs\product\capturas\v3.1 (24 imágenes)
powershell -ExecutionPolicy Bypass -File tools\publicar_escritorio.ps1    # dist\M-INV-<versión>-win-x64\M-INV.exe
```

`M-INV.exe --capturas <carpeta>` abre la demostración fuera de la pantalla visible, recorre todas las pantallas en tema
claro y oscuro y con dos roles, y guarda las imágenes y un `capturas.log`. No toca las preferencias del usuario ni
muestra cuadros de diálogo: si algo falla, lo escribe en el registro y termina con código 1.
