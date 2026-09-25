"""M-INV V2 · 99_AYUDA: guía de uso por rol, instalación en Microsoft 365 y primeros pasos (solo celdas)."""
from __future__ import annotations

import math

from minv.base import C, FONT_SB, S_AYUDA, Ctx

from .base2 import franja, lienzo

GRID = [24, 150] + [122] * 8 + [24]     # etiqueta + texto combinado en C:J (la barra usa 8 columnas)
LAST = len(GRID) - 2
LINE_CHARS = 150

SECCIONES = [
    ("LA VERSIÓN COLABORATIVA EN 30 SEGUNDOS", [
        ("Un solo libro", "El libro vive en SharePoint o OneDrive y lo usan varias personas a la vez desde Excel para la "
                          "web o de escritorio. No envíe copias por correo: todos trabajan sobre el mismo archivo."),
        ("Escritura por rol", "Bodega registra en 10A_ENTRADAS (entradas, saldo inicial y ajustes) y Ventas en "
                              "10B_SALIDAS. Cada persona tiene SU fila de captura: nadie escribe en la celda de otro."),
        ("Botón = registro", "Al pulsar el botón del script, el movimiento pasa a la bitácora oficial con su correo de "
                             "Microsoft 365 y la hora exacta. La bitácora oficial no se puede editar."),
        ("Stock a demanda", "15_STOCK y 16_ALERTAS son una instantánea: no se recalculan mientras la gente trabaja "
                            "(así Excel para la web no se pone lento). Pulse «Recalcular stock» cuando necesite verlas "
                            "al día. El disponible de su fila de captura siempre es exacto."),
    ]),
    ("SU ROL", [
        ("BODEGA", "Portada 00_PORTADA_BODEGA. Registra ENTRADA, SALDO INICIAL y AJUSTE (+/-) en su fila de "
                   "10A_ENTRADAS con el botón «Registrar en bodega». Atiende las alertas de stock crítico."),
        ("VENTAS", "Portada 00_PORTADA_VENTAS. Registra SALIDAS en su fila de 10B_SALIDAS con el botón «Registrar "
                   "salida». Antes de ofrecer, mire el disponible exacto en su fila o la instantánea de 15_STOCK."),
        ("ADMIN", "Administra usuarios (02_USUARIOS), catálogo, proveedores y configuración. Puede registrar en ambos "
                  "fragmentos. Instala los scripts y sus botones."),
        ("CONSULTA", "Solo lectura: portadas, stock, alertas y bitácoras (en SharePoint con permiso de lectura)."),
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
    ("¿POR QUÉ LA CANTIDAD SE PONE ROJA Y TACHADA?", [
        ("Poka-yoke", "Porque ese movimiento dejaría el stock en negativo (sale más de lo disponible). Excel lo marca "
                      "en rojo sangre con texto blanco tachado y el script se niega a consolidarlo. Corrija la cantidad "
                      "o confirme con Bodega si falta registrar una entrada."),
        ("En la bitácora", "Si dos personas sacan el mismo producto al mismo tiempo y la segunda salida deja el stock "
                           "en negativo, el script la marca «✖ Rechazado» (en rojo tachado) y no suma al stock: el "
                           "registro queda como evidencia, pero no afecta el inventario."),
    ]),
    ("EL STOCK ES UNA INSTANTÁNEA: RECALCULAR", [
        ("Cuándo", "La barra superior de 15_STOCK, 16_ALERTAS y de las portadas dice cuándo y quién calculó el stock "
                   "y si hay movimientos nuevos desde entonces. Si ve «⚠ … recalcule», pulse «Recalcular stock»."),
        ("Qué hace", "El script RecalcularStock lee las dos bitácoras oficiales, calcula el stock, el semáforo, las "
                     "alertas y el valor de cada producto y reescribe 15_STOCK y 16_ALERTAS en una sola operación. "
                     "Recalcule también después de modificar el catálogo."),
    ]),
    ("¿QUÉ PASA SI VARIAS PERSONAS REGISTRAN A LA VEZ?", [
        ("Filas propias", "Cada persona escribe en su propia fila de captura: no hay dos personas editando la misma "
                          "celda, que es lo que corrompe los datos en la coautoría."),
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
    ("INSTALACIÓN EN MICROSOFT 365 (ADMINISTRADOR)", [
        ("1", "Suba este libro a una biblioteca de SharePoint (o a OneDrive) y compártalo: Editar para ADMIN, BODEGA y "
              "VENTAS; Ver para CONSULTA. Detalle: docs/deployment/sharepoint-rbac-policies.md."),
        ("2", "Registre a cada persona en 02_USUARIOS con su correo de Microsoft 365 y su rol."),
        ("3", "En Excel (web o escritorio), pestaña Automatizar › Nuevo script: pegue cada archivo de "
              "src/office-scripts (versión con la contraseña del libro) y guárdelo con el mismo nombre."),
        ("4", "Ejecute una vez DiagnosticoInstalacion: comprueba identidad, permisos, contraseña y tablas."),
        ("5", "Agregue los botones: en el editor de código, «…» › Agregar en el libro, sobre cada recuadro amarillo "
              "⚙ (10A, 10B, 15_STOCK y portadas). Así el script queda compartido con el libro."),
        ("6", "Pulse «Recalcular stock» y cargue el SALDO INICIAL de cada producto desde 10A_ENTRADAS."),
    ]),
    ("PREGUNTAS FRECUENTES", [
        ("No veo el botón", "El administrador aún no instaló el script o no tiene permiso de edición. Revise la "
                            "instalación (arriba) o pida acceso de edición en SharePoint."),
        ("Mi fila dice sin asignar", "Su correo no está en 02_USUARIOS o no tiene el rol de ese fragmento. Pida al "
                                     "administrador que lo agregue."),
        ("El script dice que no me identifica", "Los scripts leen su correo de Microsoft 365. Abra el libro con su cuenta "
                                                "de trabajo (no como invitado anónimo) y vuelva a intentar."),
        ("¿Funciona sin internet?", "No: la versión colaborativa necesita Excel conectado a Microsoft 365. Para trabajar "
                                    "sin conexión use la edición local M-INV V1.2."),
    ]),
]

PASOS = [
    ('AND(kpiUsuarios>0,kpiAdmins>0)', "Usuarios registrados en 02_USUARIOS (al menos un ADMIN)", '"Usuarios activos: "&kpiUsuarios'),
    ("kpiCatalogo>0", "Productos en el catálogo (05_PRODUCTOS)", '"Productos: "&kpiCatalogo'),
    ("kpiProveedores>0", "Proveedores registrados (opcional, agrupa las alertas)", '"Proveedores: "&kpiProveedores'),
    ("N(stkActualizado)>0", "Stock calculado al menos una vez (Recalcular stock)", "txtStockActualizado"),
    ("AND(kpiActivos>0,kpiSaldosIni>=kpiActivos)", "SALDO INICIAL cargado para todos los productos activos",
     '"Con saldo inicial: "&kpiSaldosIni&" de "&kpiActivos'),
]


def _alto(texto: str, chars: int = LINE_CHARS) -> int:
    return 8 + 17 * max(1, math.ceil(len(texto) / chars))


def build_ayuda2(ctx: Ctx):
    ws, st = ctx.sheets[S_AYUDA], ctx.st
    lienzo(ws, GRID)
    franja(ctx, ws, GRID, "Guía de uso · M-INV colaborativo",
           "Cómo registrar sin chocar con otros usuarios · Qué significa cada color · Instalación en Microsoft 365",
           "guia")
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
    ws.write_string(r + 1, 1, "Documentación técnica: README.md · .claude/v2-concurrency-rules.md · "
                              "docs/deployment/sharepoint-rbac-policies.md · src/office-scripts/README.md",
                    st(font_size=8.5, italic=True, font_color=C["muted"], bg_color=C["canvas"]))
    ws.set_row_pixels(r + 1, 20, canvas)
    ws.set_selection("B7")
