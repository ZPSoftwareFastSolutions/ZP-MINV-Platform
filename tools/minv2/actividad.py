"""
M-INV V2.1 · 14_ACTIVIDAD: registro de actividad (auditoría de los Office Scripts).

Cada ejecución de un script agrega una fila (Table.addRow, igual que las bitácoras): quién (correo de Microsoft 365),
cuándo, qué script y con qué resultado, incluidos los intentos BLOQUEADOS que no llegan a la bitácora oficial.
Solo la escriben los scripts; nadie la edita.
"""
from __future__ import annotations

import datetime as dt

from minv.base import C, FIRST, HDR, Col, Ctx

from .base2 import S_ACT, barra, celda, chip, con_margen, franja, lienzo, tabla

ACT_COLS = ["ID", "Timestamp", "Usuario_O365", "Nombre", "Script", "Resultado", "Detalle"]
ACT_W = [190, 140, 250, 170, 190, 190, 560]
ACT_FMT = ["id", "date", "text", "text", "text", "status", "text"]
ACT_HELP = {
    "ID": "Identificador de la ejecución (A-fecha-hora-aleatorio).",
    "Timestamp": "Fecha y hora de la ejecución.",
    "Usuario_O365": "Correo de Microsoft 365 de quien ejecutó el script.",
    "Nombre": "Nombre visible (02_USUARIOS o cuenta de Microsoft 365).",
    "Script": "Office Script ejecutado.",
    "Resultado": "✔ correcto · ✖ bloqueado (el motivo está en Detalle).",
    "Detalle": "Mensaje completo que recibió el usuario.",
}
CF_LAST = FIRST + 50000


def build_actividad(ctx: Ctx, filas: list[dict]):
    ws, st = ctx.sheets[S_ACT], ctx.st
    widths = con_margen([16] + ACT_W + [16])
    lienzo(ws, widths, zoom=90)
    franja(ctx, ws, widths, "Registro de actividad · auditoría",
           "Lo escriben solo los Office Scripts · Cada registro, bloqueo, conteo y recálculo con su correo y su hora · "
           "Filtre por Usuario o Resultado en su propia Vista de hoja", None)
    barra(ctx, ws)
    cols = [Col(n, "calc", w, f, ACT_HELP[n]) for n, w, f in zip(ACT_COLS, ACT_W, ACT_FMT)]
    tabla(ctx, ws, "tblActividad", cols, HDR, max(1, len(filas)), badges=False)
    base = dict(bg_color=C["calc_fill"], border=1, border_color=C["calc_border"], font_size=9, formula=True)
    fmts = {
        "ID": st(font_name="Consolas", font_size=8.5, align="center", font_color=C["faint"],
                 **{k: v for k, v in base.items() if k != "font_size"}),
        "Timestamp": st(num_format="dd/mm/yyyy hh:mm:ss", align="center", **base),
        "Resultado": st(bold=True, indent=1, **base),
    }
    for n in ACT_COLS:
        fmts.setdefault(n, st(indent=1, font_color=C["calc_text"], **base))
    for k, fila in enumerate(filas or [{}]):
        r0 = FIRST - 1 + k
        for j, n in enumerate(ACT_COLS):
            v = fila.get(n, "")
            if isinstance(v, dt.datetime):
                ws.write_datetime(r0, 1 + j, v, fmts[n])
            elif isinstance(v, (int, float)) and not isinstance(v, bool):
                ws.write_number(r0, 1 + j, v, fmts[n])
            elif v:
                ws.write_string(r0, 1 + j, str(v), fmts[n])
            else:
                ws.write_blank(r0, 1 + j, None, fmts[n])
    res = f"$G{FIRST}"
    ws.conditional_format(f"G{FIRST}:G{CF_LAST}", {"type": "formula", "criteria": f'=LEFT({res},1)="✖"',
                                                   "format": st.cf(font_color=C["red"], bg_color=C["red_lt"])})
    ws.conditional_format(f"G{FIRST}:G{CF_LAST}", {"type": "formula", "criteria": f'=LEFT({res},1)="✔"',
                                                   "format": st.cf(font_color=C["green"])})
    ws.conditional_format(f"B{FIRST}:H{CF_LAST}", {"type": "formula",
                                                   "criteria": f'=AND($B{FIRST}<>"",MOD(ROW(),2)=0)',
                                                   "format": st.cf(bg_color=C["zebra"])})
    chip(ctx, ws, 5, 1, 1, '="Ejecuciones: "&FIXED(kpiEjecuciones,0)')
    chip(ctx, ws, 5, 2, 3, '=IF(kpiBloqMes=0,"✔ Sin bloqueos este mes","✖ Bloqueos del mes: "&kpiBloqMes)')
    chip(ctx, ws, 5, 4, 6, "=txtUltimaEjecucion")
    ws.conditional_format(5, 2, 5, 3, {"type": "formula", "criteria": "=kpiBloqMes>0",
                                       "format": st.cf(font_color=C["red"], bg_color=C["red_lt"])})
    tip = st(font_size=8.5, italic=True, font_color=C["muted"], bg_color=C["canvas"], indent=1)
    celda(ws, st, 5, 7, 7, "Las filas nuevas se agregan al final (Ctrl + ↓ lleva a la más reciente). Filtre en su "
                           "propia Vista de hoja (Vista › Vista de hoja) sin afectar a los demás.", tip)
    ws.freeze_panes(HDR, 0)
    ws.set_selection("B8")
    ws.set_landscape()
    ws.fit_to_pages(1, 0)
    ws.repeat_rows(HDR - 1)
