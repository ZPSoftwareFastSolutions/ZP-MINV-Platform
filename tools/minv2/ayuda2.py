"""M-INV V2.1 · 99_AYUDA: acceso paso a paso, guía por rol y por pantalla, instalación en Microsoft 365 (solo celdas)."""
from __future__ import annotations

import math

from minv.base import C, FONT_SB, S_AYUDA, Ctx

from .base2 import franja, lienzo

GRID = [24, 150] + [110] * 9 + [24]     # etiqueta + texto combinado en C:K (la barra usa 10 píldoras)
LAST = len(GRID) - 2
LINE_CHARS = 152

SECCIONES = [
    ("CÓMO ENTRAR AL LIBRO (PASO A PASO)", [
        ("1", "Abra el enlace que le compartió el administrador (correo, Teams o la biblioteca de SharePoint) e inicie "
              "sesión con su cuenta de trabajo de Microsoft 365. No descargue copias: todos trabajan sobre este archivo."),
        ("2", "El libro se abre en Excel para la web. Si prefiere el escritorio: Editar › Abrir en la aplicación de "
              "escritorio (sigue siendo el mismo archivo, con guardado automático)."),
        ("3", "Use la barra oscura superior para ir a la portada de su rol: ⌂ Bodega, ⌂ Ventas o ◈ Gerencia. La "
              "portada le dice el «Próximo paso» en una frase."),
        ("4", "Para registrar: escriba en SU fila de captura, revise que Validación diga «✔ Lista» y pulse el botón "
              "del script. Lea el Resultado en su misma fila."),
        ("5", "Para mirar sin molestar a nadie: Vista › Vista de hoja › Nueva. Sus filtros y órdenes quedan solo para "
              "usted (los demás siguen viendo la hoja completa)."),
        ("6", "Si algo no funciona, revise PREGUNTAS FRECUENTES al final de esta guía o pida al administrador que "
              "ejecute DiagnosticoInstalacion."),
    ]),
    ("LA VERSIÓN COLABORATIVA EN 30 SEGUNDOS", [
        ("Un solo libro", "El libro vive en SharePoint o OneDrive y lo usan varias personas a la vez desde Excel para la "
                          "web o de escritorio. No envíe copias por correo: todos trabajan sobre el mismo archivo."),
        ("Escritura por rol", "Bodega registra en 10A_ENTRADAS (entradas, saldo inicial y ajustes) y Ventas en "
                              "10B_SALIDAS. Cada persona tiene SU fila de captura, SU fila de consulta y cuenta SUS "
                              "productos en la toma física: nadie escribe en la celda de otro."),
        ("Botón = registro", "Al pulsar el botón del script, el movimiento pasa a la bitácora oficial con su correo de "
                             "Microsoft 365 y la hora exacta, y queda anotado en 14_ACTIVIDAD. Las bitácoras oficiales "
                             "no se pueden editar."),
        ("Stock a demanda", "15_STOCK, 16_ALERTAS y 18_PEDIDO son una instantánea: no se recalculan mientras la gente "
                            "trabaja (así Excel para la web no se pone lento). Pulse «Recalcular stock» cuando necesite "
                            "verlas al día. El disponible de su fila de captura y de su consulta siempre es exacto."),
    ]),
    ("SU ROL", [
        ("BODEGA", "Portada 00_PORTADA_BODEGA. Registra ENTRADA, SALDO INICIAL y AJUSTE (+/-) en su fila de "
                   "10A_ENTRADAS con el botón «Registrar en bodega», dirige la toma física (13_CONTEO) y atiende las "
                   "alertas y el pedido sugerido."),
        ("VENTAS", "Portada 00_PORTADA_VENTAS. Registra SALIDAS en su fila de 10B_SALIDAS con el botón «Registrar "
                   "salida». Antes de ofrecer, consulte el producto en su fila de 17_CONSULTA: disponible exacto, "
                   "cobertura y último movimiento."),
        ("ADMIN", "Portada 00_PORTADA_GERENCIA. Administra usuarios (02_USUARIOS), catálogo, proveedores y "
                  "configuración; puede registrar en ambos fragmentos. Instala los scripts, sus botones y el resumen "
                  "diario por correo."),
        ("CONSULTA", "Solo lectura (permiso Ver en SharePoint): portada de Gerencia, stock, alertas, pedido, actividad "
                     "y bitácoras."),
    ]),
    ("REGISTRAR UN MOVIMIENTO (Bodega y Ventas)", [
        ("1", "Abra su portada y pulse REGISTRAR: llegará a la zona de captura de su hoja (10A o 10B)."),
        ("2", "Busque la fila con SU nombre en la columna Usuario. Escriba solo en esa fila."),
        ("3", "Complete Tipo (solo Bodega), Producto (elíjalo de la lista: escriba parte del nombre o del SKU para "
              "filtrarla), Cantidad y, si aplica, Fecha, Documento y Observaciones (obligatoria en los ajustes)."),
        ("4", "Revise Disponible y Validación: debe decir «✔ Lista». Si dice ○ falta un dato; si dice ✖, corrija."),
        ("5", "Pulse el botón del script (junto a la barra superior de la hoja). Espere unos segundos."),
        ("6", "Lea Resultado: «✔ Registrado» con el ID del movimiento (su fila queda limpia para el siguiente) o "
              "«✖ Bloqueado» con el motivo (su fila se conserva para que la corrija)."),
    ]),
    ("CONSULTAR UN PRODUCTO (17_CONSULTA)", [
        ("Su fila", "Cada usuario operativo tiene una fila con su nombre. Elija el producto en la columna Producto (la "
                    "lista se filtra al escribir) y la fila muestra la ficha al instante. No hace falta ningún botón."),
        ("Qué muestra", "Disponible exacto (calculado en vivo desde las dos bitácoras), estado del semáforo, mínimo, "
                        "máximo, cantidad sugerida, proveedor, ubicación, último movimiento (fecha, tipo y quién), "
                        "entradas y salidas de 30 días y la cobertura en días al ritmo de venta actual."),
        ("Historial", "Para ver todos los movimientos de un producto: en 10A o 10B cree una Vista de hoja y filtre la "
                      "columna Producto. Solo usted verá ese filtro."),
    ]),
    ("TOMA FÍSICA COLABORATIVA (13_CONTEO)", [
        ("1", "Bodega pulse «Recalcular stock» justo antes de contar: la columna Sistema muestra la instantánea."),
        ("2", "Repartan el conteo por zonas (columna Ubicación). Cada contador escribe en la columna Conteo solo las "
              "filas de su zona: son celdas distintas, así que varias personas pueden contar a la vez."),
        ("3", "La columna Resultado se actualiza sola: ✔ Cuadra, ▲ Sobrante, ▼ Faltante, ● Saldo inicial (producto "
              "sin movimientos) o ✖ si la cantidad no es válida. Deje en blanco lo que no contó."),
        ("4", "El responsable (BODEGA o ADMIN) escribe la fecha del conteo, escribe SI en «Confirmar» y pulsa «Generar "
              "ajustes del conteo»."),
        ("5", "El script compara cada conteo contra las bitácoras en ese momento y registra en 10A un AJUSTE (+), AJUSTE "
              "(-) o SALDO INICIAL por diferencia, con el documento CF-AAAAMMDD. Luego limpia los conteos procesados."),
        ("6", "Pulse «Recalcular stock» para ver el inventario ajustado. Si cuenta mientras se vende, registre las "
              "ventas antes de generar los ajustes (o cuente en un horario sin movimientos)."),
    ]),
    ("PEDIDO SUGERIDO (18_PEDIDO)", [
        ("Qué es", "Lo calcula «Recalcular stock»: cada producto activo AGOTADO, CRÍTICO o BAJO con la cantidad para "
                   "llegar al máximo, agrupado por proveedor, con costo, subtotal, días de entrega, fecha estimada y "
                   "datos de contacto del proveedor."),
        ("Cómo usarlo", "Filtre la columna Proveedor en su Vista de hoja y use Archivo › Imprimir (o Guardar como PDF) "
                        "para enviar la orden. Cuando llegue la mercancía, Bodega registra la ENTRADA en 10A."),
    ]),
    ("ACTIVIDAD Y AUDITORÍA (14_ACTIVIDAD)", [
        ("Qué guarda", "Cada ejecución de un script: registros, bloqueos (con el motivo), recálculos y tomas físicas, "
                       "con el correo de Microsoft 365, el nombre, la hora y el detalle. Solo la escriben los scripts."),
        ("Para qué", "Responde «¿quién registró esto?», «¿por qué me bloqueó?» y «¿cuándo se recalculó?». La portada de "
                     "Gerencia resume los registros y bloqueos de cada usuario en el mes."),
    ]),
    ("PORTADA DE GERENCIA", [
        ("Indicadores", "Valor del inventario, productos que requieren acción, total del pedido sugerido, movimientos "
                        "del mes, unidades vendidas en 30 días, cobertura mediana, bloqueos del mes y usuarios activos."),
        ("Gráficos y listas", "Unidades despachadas por mes, valor del inventario por categoría, los 10 productos más "
                              "vendidos en 30 días (con su cobertura) y la actividad de cada usuario en el mes."),
    ]),
    ("¿POR QUÉ LA CANTIDAD SE PONE ROJA Y TACHADA?", [
        ("Poka-yoke", "Porque ese movimiento dejaría el stock en negativo (sale más de lo disponible). Excel lo marca "
                      "en rojo sangre con texto blanco tachado y el script se niega a consolidarlo. Corrija la cantidad "
                      "o confirme con Bodega si falta registrar una entrada."),
        ("En la bitácora", "Si dos personas sacan el mismo producto al mismo tiempo y la segunda salida deja el stock "
                           "en negativo, el script la marca «✖ Rechazado» (en rojo tachado) y no suma al stock: el "
                           "registro queda como evidencia, pero no afecta el inventario."),
    ]),
    ("EL STOCK ES UNA INSTANTÁNEA: RECALCULAR", [
        ("Cuándo", "La barra superior de 15_STOCK, 16_ALERTAS, 18_PEDIDO y de las portadas dice cuándo y quién calculó "
                   "el stock y si hay movimientos nuevos desde entonces. Si ve «⚠ … recalcule», pulse «Recalcular "
                   "stock»."),
        ("Qué hace", "El script RecalcularStock lee las dos bitácoras oficiales y reescribe en una sola operación el "
                     "stock, el semáforo, el valor, las salidas y la cobertura de 30 días, el ranking de ventas, las "
                     "alertas y el pedido sugerido. Recalcule también después de modificar el catálogo."),
    ]),
    ("¿QUÉ PASA SI VARIAS PERSONAS REGISTRAN A LA VEZ?", [
        ("Filas propias", "Cada persona escribe en su propia fila de captura y de consulta: no hay dos personas "
                          "editando la misma celda, que es lo que corrompe los datos en la coautoría."),
        ("Agregar al final", "Los scripts agregan cada movimiento al final de la bitácora oficial con una sola operación "
                             "del servidor e IDs que no dependen de un contador compartido: dos registros simultáneos "
                             "producen dos filas, nunca se pisan."),
        ("Doble control", "Después de agregar una salida o un ajuste negativo, el script vuelve a calcular el "
                          "disponible; si otra persona se adelantó y el stock quedó en negativo, marca su propio "
                          "registro como rechazado."),
    ]),
    ("CORREGIR UN ERROR", [
        ("Nunca borre", "La bitácora oficial es inmutable: no se borra ni se edita. Un error se corrige con un "
                        "movimiento compensatorio: AJUSTE (+) o AJUSTE (-) en 10A_ENTRADAS con la explicación en "
                        "Observaciones (ej: «Corrige la salida S-20260925-… registrada por 10 en vez de 8»)."),
    ]),
    ("RESUMEN DIARIO POR CORREO (OPCIONAL, POWER AUTOMATE)", [
        ("Qué envía", "El script ResumenDiario no modifica el libro: devuelve el resumen del día (movimientos, "
                      "bloqueos, productos en alerta y pedido sugerido) listo para un correo."),
        ("Cómo", "En Power Automate cree un flujo programado (ej. cada día a las 7:00) con la acción «Ejecutar script» "
                 "de Excel Online (Business) sobre este libro y «Enviar un correo (V2)» con el HTML del resultado. "
                 "Detalle: docs/deployment/sharepoint-rbac-policies.md."),
    ]),
    ("INSTALACIÓN EN MICROSOFT 365 (ADMINISTRADOR)", [
        ("1", "Suba este libro a una biblioteca de SharePoint (o a OneDrive) y compártalo: Editar para ADMIN, BODEGA y "
              "VENTAS; Ver para CONSULTA. Detalle: docs/deployment/sharepoint-rbac-policies.md."),
        ("2", "Registre a cada persona en 02_USUARIOS con su correo de Microsoft 365 y su rol, en orden y al final."),
        ("3", "En Excel (web o escritorio), pestaña Automatizar › Nuevo script: pegue cada archivo instalable "
              "(RegistrarEntrada, RegistrarSalida, RecalcularStock, GenerarAjustesConteo, ResumenDiario y "
              "DiagnosticoInstalacion) y guárdelo con el mismo nombre."),
        ("4", "Ejecute una vez DiagnosticoInstalacion: comprueba identidad, permisos, contraseña, tablas y filas."),
        ("5", "Agregue los botones: en el editor de código, «…» › Agregar en el libro, sobre cada recuadro amarillo "
              "⚙ (10A, 10B, 13_CONTEO, 15_STOCK, 18_PEDIDO y portadas). Así el script queda compartido con el libro."),
        ("6", "Pulse «Recalcular stock» y cargue el SALDO INICIAL de cada producto: en 10A o, más rápido, con una toma "
              "física completa en 13_CONTEO (genera los saldos iniciales de los productos sin movimientos)."),
    ]),
    ("PREGUNTAS FRECUENTES", [
        ("No veo el botón", "El administrador aún no instaló el script o no tiene permiso de edición. Revise la "
                            "instalación (arriba) o pida acceso de edición en SharePoint."),
        ("Mi fila dice sin asignar", "Su correo no está en 02_USUARIOS o no tiene el rol de ese fragmento. Pida al "
                                     "administrador que lo agregue (al final de la tabla)."),
        ("El script dice que no me identifica", "Los scripts leen su correo de Microsoft 365. Abra el libro con su cuenta "
                                                "de trabajo (no como invitado anónimo) y vuelva a intentar."),
        ("El conteo no genera ajustes", "Revise que escribió SI en Confirmar, que la fecha es válida, que su rol es "
                                        "BODEGA o ADMIN y que los conteos no tengan ✖. El motivo queda en la celda "
                                        "Resultado del conteo y en 14_ACTIVIDAD."),
        ("¿Funciona sin internet?", "No: la versión colaborativa necesita Excel conectado a Microsoft 365. Para trabajar "
                                    "sin conexión use la edición local M-INV V1.2."),
    ]),
]

PASOS = [
    ('AND(kpiUsuarios>0,kpiAdmins>0)', "Usuarios registrados en 02_USUARIOS (al menos un ADMIN)", '"Usuarios activos: "&kpiUsuarios'),
    ("kpiCatalogo>0", "Productos en el catálogo (05_PRODUCTOS)", '"Productos: "&kpiCatalogo'),
    ("kpiProveedores>0", "Proveedores registrados (agrupan las alertas y el pedido)", '"Proveedores: "&kpiProveedores'),
    ("kpiEjecuciones>0", "Scripts instalados y ejecutados (14_ACTIVIDAD registra cada ejecución)", "txtUltimaEjecucion"),
    ("N(stkActualizado)>0", "Stock calculado al menos una vez (Recalcular stock)", "txtStockActualizado"),
    ("AND(kpiActivos>0,kpiSaldosIni>=kpiActivos)", "SALDO INICIAL cargado para todos los productos activos (10A o toma "
                                                   "física)", '"Con saldo inicial: "&kpiSaldosIni&" de "&kpiActivos'),
]


def _alto(texto: str, chars: int = LINE_CHARS) -> int:
    return 8 + 17 * max(1, math.ceil(len(texto) / chars))


def build_ayuda2(ctx: Ctx):
    ws, st = ctx.sheets[S_AYUDA], ctx.st
    lienzo(ws, GRID)
    franja(ctx, ws, GRID, "Guía de uso · M-INV colaborativo",
           "Cómo entrar · Cómo registrar sin chocar con otros usuarios · Consulta, toma física, pedido y auditoría · "
           "Instalación en Microsoft 365", "guia")
    canvas = st(bg_color=C["canvas"])
    sec = st(font_name=FONT_SB, font_size=11, font_color=C["brand"], bg_color=C["canvas"])
    lbl = st(bold=True, font_size=9.5, align="center", valign="vcenter", text_wrap=True, font_color=C["white"],
             bg_color=C["brand"], border=1, border_color=C["white"])
    txt = st(font_size=10, valign="vcenter", text_wrap=True, indent=1, bg_color=C["white"], border=1,
             border_color=C["border"])
    r = 5
    ws.set_row_pixels(r, 12, canvas)
    r += 1
    # Primeros pasos (se marcan solos)
    ws.set_row_pixels(r, 26, canvas)
    ws.merge_range(r, 1, r, LAST, "PRIMEROS PASOS DEL ADMINISTRADOR  (se marcan solos)", sec)
    r += 1
    for cond, texto, detalle in PASOS:
        ws.set_row_pixels(r, 24)
        ws.write_formula(r, 1, f'=IF({cond},"✔","○")', st(font_size=14, bold=True, align="center",
                                                             bg_color=C["white"], border=1, border_color=C["border"],
                                                             formula=True))
        fp = st(font_size=10, indent=1, bg_color=C["white"], border=1, border_color=C["border"], formula=True)
        ws.merge_range(r, 2, r, LAST, "", fp)
        ws.write_formula(r, 2, f'="{texto}  ·  "&{detalle}', fp)
        ws.conditional_format(r, 1, r, 1, {"type": "formula", "criteria": f"={cond}",
                                           "format": st.cf(font_color=C["green"])})
        ws.conditional_format(r, 1, r, 1, {"type": "formula", "criteria": f"=NOT({cond})",
                                           "format": st.cf(font_color=C["amber"])})
        r += 1
    for titulo, filas in SECCIONES:
        ws.set_row_pixels(r, 14, canvas)
        r += 1
        ws.set_row_pixels(r, 26, canvas)
        ws.merge_range(r, 1, r, LAST, titulo, sec)
        r += 1
        for etiqueta, texto in filas:
            ws.set_row_pixels(r, max(26, _alto(texto)))
            ws.write_string(r, 1, etiqueta, lbl)
            ws.merge_range(r, 2, r, LAST, texto, txt)
            r += 1
    ws.set_row_pixels(r, 20, canvas)
    ws.write_string(r + 1, 1, "Documentación técnica: README.md · docs/deployment/inicio-rapido.md · "
                              ".claude/v2-concurrency-rules.md · docs/deployment/sharepoint-rbac-policies.md · "
                              "src/office-scripts/README.md",
                    st(font_size=8.5, italic=True, font_color=C["muted"], bg_color=C["canvas"]))
    ws.set_row_pixels(r + 1, 20, canvas)
    ws.set_selection("B7")
