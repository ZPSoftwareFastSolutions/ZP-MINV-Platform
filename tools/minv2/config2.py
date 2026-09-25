"""M-INV V2 · 01_CONFIG (parámetros, tipos con dominio, semáforo, metadatos de la instantánea) y 02_USUARIOS."""
from __future__ import annotations

import datetime as dt

import demo_data as D

from minv.base import C, ESTADOS, FIRST, HDR, Col, Ctx, nested_if, q, status_cf, this_row
from minv.config_sheets import config_band, simple_table

from .base2 import EDICION2, MAX_USR, S_USR, VERSION2, barra, chip, franja, lienzo, msg, tabla

# Tipos de movimiento: el dominio define en qué fragmento (hoja) se registran
TIPOS2 = [
    ("SALDO INICIAL", 1, "BODEGA", "Carga del inventario existente al implementar (una vez por producto)."),
    ("ENTRADA", 1, "BODEGA", "Compra, recepción de mercancía o devolución de un cliente."),
    ("AJUSTE (+)", 1, "BODEGA", "Sobrante encontrado en conteo físico. Exige observación."),
    ("AJUSTE (-)", -1, "BODEGA", "Merma, daño, pérdida o faltante. Exige observación."),
    ("SALIDA", -1, "VENTAS", "Venta, despacho a cliente o consumo interno."),
]

# Usuarios de la demo (ficticios, dominio .example reservado): correo, nombre, rol
USUARIOS_DEMO = [
    ("admin@distribuidorademo.example", "Administrador M-INV", "ADMIN"),
    ("ana.gomez@distribuidorademo.example", "Ana Gómez", "BODEGA"),
    ("laura.mendez@distribuidorademo.example", "Laura Méndez", "BODEGA"),
    ("carlos.ruiz@distribuidorademo.example", "Carlos Ruiz", "VENTAS"),
    ("sofia.lopez@distribuidorademo.example", "Sofía López", "VENTAS"),
    ("gerencia@distribuidorademo.example", "Gerencia", "CONSULTA"),
]


def params2(ctx: Ctx) -> list[tuple]:
    demo = ctx.demo
    return [
        ("cfgEmpresa", "Empresa (cliente)", D.EMPRESA_DEMO if demo else "NOMBRE DE SU EMPRESA", None,
         "Nombre que aparece en las portadas."),
        ("cfgNIT", "NIT / Identificación", D.NIT_DEMO if demo else "", None, "Identificación tributaria."),
        ("cfgBodega", "Sede / Bodega", D.BODEGA_DEMO if demo else "Bodega principal", None,
         "Ubicación física que controla este libro."),
        ("cfgMoneda", "Moneda", "COP", None, "Informativo. Los formatos de valor usan el símbolo $."),
        ("cfgMargenAlerta", "Margen de alerta preventiva", D.MARGEN_ALERTA, "0%",
         "Un producto pasa a BAJO cuando su stock es ≤ Mínimo × (1 + margen)."),
        ("cfgDiasSinRotacion", "Días para considerar sin rotación", 60, '0" días"',
         "Productos con stock y sin movimientos en este plazo se marcan como inmovilizados."),
        ("cfgFechaMin", "Fecha mínima permitida", dt.date(2020, 1, 1), "dd/mm/yyyy",
         "Los scripts rechazan fechas anteriores."),
        ("cfgVersion", "Versión del sistema", VERSION2, None, "Versión del motor M-INV."),
        ("cfgEdicion", "Edición", EDICION2, None, "Libro colaborativo en OneDrive/SharePoint con Office Scripts."),
        ("cfgFechaBuild", "Fecha de generación", ctx.fin, "dd/mm/yyyy", "Fecha en que se generó este libro."),
        ("cfgProveedor", "Implementado por", "Z&P Software Fast Solutions", None, "Proveedor del software."),
        # Metadatos de la instantánea (los escribe RecalcularStock.ts; no editar)
        ("stkActualizado", "Stock calculado el", 0, "dd/mm/yyyy hh:mm", "Lo escribe RecalcularStock.ts."),
        ("stkActualizadoPor", "Stock calculado por (correo)", "", None, "Lo escribe RecalcularStock.ts."),
        ("stkActualizadoNombre", "Stock calculado por (nombre)", "", None, "Lo escribe RecalcularStock.ts."),
        ("stkMovimientos", "Movimientos procesados", 0, "#,##0", "Lo escribe RecalcularStock.ts."),
    ]


def build_config2(ctx: Ctx, overrides: dict | None = None):
    """01_CONFIG oculta. `overrides` permite a la demo precargar los metadatos de la instantánea."""
    wb, ws, st = ctx.wb, ctx.sheets["01_CONFIG"], ctx.st
    overrides = overrides or {}
    config_band(ctx, ws, "Configuración del sistema",
                "Capa de configuración (oculta) · Solo administrador · Los scripts leen estos parámetros",
                [24, 250, 260, 520, 24])
    lbl = st(bold=True, font_color=C["text"], border=1, border_color=C["border"], indent=1)
    note = st(font_color=C["muted"], font_size=9, border=1, border_color=C["border"], indent=1, text_wrap=True)
    sec = st(bold=True, font_color=C["brand"], font_size=10.5)
    ws.write_string(6, 1, "PARÁMETROS DEL TENANT", sec)
    ws.write_string(7, 1, "Parámetro", st.hdr("calc"))
    ws.write_string(7, 2, "Valor", st.hdr("in"))
    ws.write_string(7, 3, "Descripción", st.hdr("calc"))
    params = params2(ctx)
    for i, (name, label, value, nf, desc) in enumerate(params):
        value = overrides.get(name, value)
        r = 8 + i
        f = st(bg_color=C["white"], border=1, border_color=C["input_border"], locked=False, indent=1,
               font_color=C["brand_dk"], bold=True, **({"num_format": nf} if nf else {}))
        ws.write_string(r, 1, label, lbl)
        if isinstance(value, dt.datetime):
            ws.write_datetime(r, 2, value, f)
        elif isinstance(value, dt.date):
            ws.write_datetime(r, 2, dt.datetime.combine(value, dt.time()), f)
        elif isinstance(value, (int, float)):
            ws.write_number(r, 2, value, f)
        elif value:
            ws.write_string(r, 2, value, f)
        else:
            ws.write_blank(r, 2, None, f)
        ws.write_string(r, 3, desc, note)
        wb.define_name(name, f"={q('01_CONFIG')}!$C${r + 1}")
    ctx.layout["cfg_rows"] = {p[0]: 9 + i for i, p in enumerate(params)}

    r0 = 8 + len(params) + 2
    ws.write_string(r0 - 1, 1, "TIPOS DE MOVIMIENTO  (FactorStock y dominio: BODEGA → 10A · VENTAS → 10B)", sec)
    txt = st(indent=1, border=1, border_color=C["border"], text_wrap=True)
    ws.set_column_pixels(4, 4, 220)
    simple_table(ctx, ws, r0, "tblTiposMov", ["Tipo", "FactorStock", "Dominio", "Descripción"],
                 [list(t) for t in TIPOS2],
                 [st(bold=True, indent=1, border=1, border_color=C["border"]),
                  st(align="center", bold=True, num_format="+0;-0;0", border=1, border_color=C["border"]),
                  st(align="center", border=1, border_color=C["border"]), txt])

    r1 = r0 + len(TIPOS2) + 3
    ws.write_string(r1 - 1, 1, "SEMÁFORO DE STOCK  (reglas que aplica RecalcularStock.ts)", sec)
    center = st(align="center", bold=True, border=1, border_color=C["border"])
    simple_table(ctx, ws, r1, "tblEstados", ["Estado", "Prioridad", "Regla", "Acción"],
                 [[e[0], e[1], e[6], e[7]] for e in ESTADOS],
                 [st(bold=True, indent=1, border=1, border_color=C["border"]), center, txt, txt])
    for i, e in enumerate(ESTADOS):
        ws.write_string(r1 + 1 + i, 1, e[0], st(bold=True, indent=1, font_color=e[3], bg_color=e[4],
                                                 border=1, border_color=C["border"]))


# ---------------------------------------------------------------------------
# 02_USUARIOS · autorización por correo de Microsoft 365 (RBAC que aplican los scripts)
# ---------------------------------------------------------------------------
def usuarios_cols() -> list[Col]:
    r = this_row("tblUsuarios")
    orden = {}
    for key, roles in (("OrdenBodega", ("BODEGA", "ADMIN")), ("OrdenVentas", ("VENTAS", "ADMIN"))):
        rango = lambda c: f"INDEX(tblUsuarios[{c}],1):{r(c)}"  # noqa: E731
        cond = "+".join(f'({rango("Rol")}="{x}")' for x in roles)
        es = f'AND({r("Correo")}<>"",{r("Activo")}<>"NO",OR(' + ",".join(f'{r("Rol")}="{x}"' for x in roles) + "))"
        orden[key] = (f'=IF({es},SUMPRODUCT(({rango("Correo")}<>"")*({rango("Activo")}<>"NO")*({cond})),"")')
    valid = nested_if([
        (f'AND({r("Correo")}="",{r("Nombre")}="")', '""'),
        (f'OR({r("Correo")}="",{r("Nombre")}="")', '"⚠ Faltan datos"'),
        (f'OR(ISERROR(FIND("@",{r("Correo")})),ISNUMBER(FIND(" ",{r("Correo")})))', '"✖ Correo no válido"'),
        (f'COUNTIF(tblUsuarios[Correo],{r("Correo")})>1', '"✖ Correo duplicado"'),
        (f'ISNA(MATCH({r("Rol")},lstRoles,0))', '"✖ Rol no válido"'),
        (f'{r("Activo")}="NO"', '"● Inactivo"'),
    ], '"✔ Autorizado"')
    return [
        Col("Correo", "in", 300, "text", "Correo de Microsoft 365 con el que la persona inicia sesión. Los scripts "
                                         "lo comparan con el usuario que ejecuta el botón."),
        Col("Nombre", "in", 200, "text", "Nombre visible en la captura y en la bitácora."),
        Col("Rol", "in", 110, "center", "ADMIN (todo), BODEGA (10A: entradas y ajustes), VENTAS (10B: salidas), "
                                        "CONSULTA (solo lectura)."),
        Col("Activo", "in", 76, "center", "Escriba NO para retirar el acceso sin borrar la fila (auditoría)."),
        Col("OrdenBodega", "calc", 110, "center", "Fila de captura que le corresponde en 10A_ENTRADAS.",
            formula=orden["OrdenBodega"]),
        Col("OrdenVentas", "calc", 110, "center", "Fila de captura que le corresponde en 10B_SALIDAS.",
            formula=orden["OrdenVentas"]),
        Col("Validación", "calc", 170, "status", "Control del registro.", formula="=" + valid),
    ]


def build_usuarios(ctx: Ctx):
    ws, st, wb = ctx.sheets[S_USR], ctx.st, ctx.wb
    cols = usuarios_cols()
    widths = [16] + [c.width for c in cols] + [96, 96, 16]   # margen derecho: la barra de navegación cabe
    lienzo(ws, widths, zoom=90)
    franja(ctx, ws, widths, "Usuarios autorizados",
           "Control de acceso por rol · Lo administra el ADMIN · Los scripts validan el correo de Microsoft 365",
           None)
    barra(ctx, ws)
    ws.set_row_pixels(6, 16, st(bg_color=C["canvas"]))
    tabla(ctx, ws, "tblUsuarios", cols, HDR, MAX_USR, badges=False)
    L = {c.name: ctx.col("tblUsuarios", c.name) for c in cols}
    last = FIRST + MAX_USR - 1
    rng = lambda n: f"{L[n]}{FIRST}:{L[n]}{last}"  # noqa: E731
    e = L["Correo"]
    ws.data_validation(rng("Correo"), {
        "validate": "custom",
        "value": f'=AND(ISNUMBER(FIND("@",{e}{FIRST})),ISERROR(FIND(" ",{e}{FIRST})),LEN({e}{FIRST})<=120,'
                 f'COUNTIF(${e}${FIRST}:${e}${last},{e}{FIRST})=1)',
        **msg("Correo de Microsoft 365", "El mismo con que la persona inicia sesión (ej: ana@empresa.com). Único."),
        "error_title": "Correo no válido", "error_message": "Escriba un correo válido, sin espacios y sin repetir."})
    ws.data_validation(rng("Nombre"), {"validate": "length", "criteria": "between", "minimum": 2, "maximum": 60,
                                       **msg("Nombre", "Nombre visible (2 a 60 caracteres)."),
                                       "error_title": "Nombre no válido", "error_message": "Entre 2 y 60 caracteres."})
    ws.data_validation(rng("Rol"), {"validate": "list", "source": "=lstRoles",
                                    **msg("Rol", "ADMIN, BODEGA, VENTAS o CONSULTA (Alt + ↓)."),
                                    "error_title": "Rol no válido", "error_message": "Elija un rol de la lista."})
    ws.data_validation(rng("Activo"), {"validate": "list", "source": ["SI", "NO"],
                                       **msg("¿Activo?", "NO = sin acceso (la fila se conserva para auditoría)."),
                                       "error_title": "Valor no válido", "error_message": "Escriba SI o NO."})
    fin = {c.name: st.cell("in", c.fmt) for c in cols if c.kind == "in"}
    for rr in range(MAX_USR):
        for c in cols:
            if c.kind == "in":
                ws.write_blank(FIRST - 1 + rr, 1 + cols.index(c), None, fin[c.name])
    if ctx.demo:
        for i, (correo, nombre, rol) in enumerate(USUARIOS_DEMO):
            r0 = FIRST - 1 + i
            ws.write_string(r0, 1, correo, fin["Correo"])
            ws.write_string(r0, 2, nombre, fin["Nombre"])
            ws.write_string(r0, 3, rol, fin["Rol"])
            ws.write_string(r0, 4, "SI", fin["Activo"])
    status_cf(ws, st, rng("Validación"), f"${L['Validación']}{FIRST}")
    ws.conditional_format(f"{L['Correo']}{FIRST}:{L['Activo']}{last}", {
        "type": "formula", "criteria": f'=AND(${L["Correo"]}{FIRST}="",${L["Correo"]}{FIRST - 1}<>"")',
        "format": st.cf(bg_color=C["brand_lt"])})

    chip(ctx, ws, 5, 1, 1, '="Usuarios activos: "&kpiUsuarios')
    f = st(font_size=8.5, italic=True, font_color=C["muted"], bg_color=C["canvas"], indent=1)
    ws.write_string(5, 2, "Agregue usuarios al final y no reordene: la posición define su fila de captura. "
                          "Para retirar a alguien escriba NO en Activo.", f)
    ws.freeze_panes(HDR, 0)
    n = len(USUARIOS_DEMO) if ctx.demo else 0
    ws.set_selection(FIRST - 1 + n, 1, FIRST - 1 + n, 1)
