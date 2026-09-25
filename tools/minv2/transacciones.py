"""
M-INV V2 · Capa de escritura fragmentada (sharding): 10A_ENTRADAS (Bodega) y 10B_SALIDAS (Ventas).

Cada hoja tiene dos zonas que comparten columnas de hoja (las técnicas N-P quedan ocultas para ambas):
  1. CAPTURA: una fila por usuario autorizado del dominio (la fila se asigna por su posición en 02_USUARIOS).
     Nadie escribe en la celda de otro: no hay colisiones de coautoría. Validación y disponible en vivo.
  2. BITÁCORA OFICIAL (tblEntradas / tblSalidas): valores, sin fórmulas. Solo la escriben los Office Scripts
     (Table.addRow = inserción atómica en el servidor), con ID sin coordinación, Usuario_O365 y Timestamp.
"""
from __future__ import annotations

import datetime as dt

from minv.base import C, Col, Ctx, nested_if, status_cf, this_row

from .base2 import (CAP_FIRST, CAP_HDR, CAP_LAST, CAPTURE_COLS, CAPTURE_INPUTS, LED_FIRST, LED_HDR, LEDGER_COLS,
                    MAX_CAPT, S_ENT, S_SAL, T_CAPT, T_LED, TX_HIDDEN, TX_WIDTHS, barra, ccol, celda, chip, franja,
                    lcol, lienzo, marcador_script, msg, rojo_sangre, seccion, tabla)

LED_CF_LAST = LED_FIRST + 20000      # alcance de los formatos condicionales de la bitácora (crece con el uso)

HELP_CAPT = {
    "Usuario": "Persona dueña de esta fila (según 02_USUARIOS). Escriba solo en SU fila.",
    "Tipo": "ENTRADA, SALDO INICIAL, AJUSTE (+) o AJUSTE (-). Alt + ↓ abre la lista.",
    "Fecha": "Opcional. Vacía = hoy. Para registrar un movimiento de otro día: dd/mm/aaaa.",
    "Producto": "Elija de la lista. Escriba parte del nombre o del SKU para filtrarla.",
    "Cantidad": "Siempre positiva, en la unidad del producto. El signo lo pone el tipo.",
    "Documento": "Opcional: factura, remisión, orden o cliente. Máximo 30 caracteres.",
    "Observaciones": "Obligatoria en los AJUSTES (motivo). Máximo 250 caracteres.",
    "Disponible": "Stock exacto en este instante: suma de las dos bitácoras oficiales.",
    "Validación": "✔ lista para registrar · ○ falta un dato · ✖ error que impide registrar.",
    "Resultado": "Lo escribe el script al pulsar el botón: ✔ registrado (con ID) o ✖ bloqueado.",
    "Unidad": "Unidad de medida del producto.",
    "Factor": "+1 suma al inventario, −1 resta.",
    "Correo": "Técnica (oculta): correo de Microsoft 365 del dueño de la fila.",
    "SKU": "Técnica (oculta): código extraído del producto.",
    "Queda": "Técnica (oculta): stock resultante si se registra.",
}
HELP_LED = {
    "ID": "Identificador único generado por el script (fragmento-fecha-hora-aleatorio): no requiere coordinación.",
    "Tipo": "Tipo de movimiento.", "Fecha": "Fecha del movimiento (negocio).",
    "Producto": "Etiqueta SKU · Nombre al momento del registro.", "Cantidad": "Cantidad positiva.",
    "Documento": "Soporte del movimiento.", "Observaciones": "Detalle o motivo.",
    "CantidadNeta": "Cantidad × FactorStock: lo único que suma el stock.",
    "Estado": "✔ Consolidado o ✖ Rechazado (con motivo). Los rechazados no suman.",
    "Registró": "Nombre de quien ejecutó el script.", "Unidad": "Unidad de medida.",
    "FactorStock": "+1 o −1 según el tipo.",
    "Usuario_O365": "Auditoría (oculta): correo de Microsoft 365 que ejecutó el script.",
    "SKU": "Técnica (oculta): clave del producto.",
    "Timestamp": "Auditoría (oculta): fecha y hora exactas del registro.",
}
FMT_LED = {"ID": "id", "Tipo": "text", "Fecha": "date", "Producto": "text", "Cantidad": "qty", "Documento": "center",
           "Observaciones": "text", "CantidadNeta": "signed", "Estado": "status", "Registró": "text",
           "Unidad": "center", "FactorStock": "factor", "Usuario_O365": "text", "SKU": "center", "Timestamp": "date"}


def captura_cols(hoja: str) -> list[Col]:
    t = T_CAPT[hoja]
    r = this_row(t)
    orden = "OrdenBodega" if hoja == S_ENT else "OrdenVentas"
    n = f"ROW()-ROW({t}[#Headers])"
    bodega = hoja == S_ENT
    inputs = CAPTURE_INPUTS[hoja]
    vacia = "AND(" + ",".join(f'{r(c)}=""' for c in inputs) + ")"
    activo = f'IFERROR(INDEX(tblProductos[Activo],MATCH({r("SKU")},tblProductos[SKU],0)),"")'
    dec = f'IFERROR(INDEX(lstUniDec,MATCH({r("Unidad")},lstUniCod,0)),"SI")'
    pares = [(vacia, '""'), (f'{r("Correo")}=""', '"✖ Fila sin usuario asignado (02_USUARIOS)"')]
    if bodega:
        pares.append((f'{r("Tipo")}=""', '"○ Elija el tipo de movimiento"'))
    pares += [
        (f'{r("Producto")}=""', '"○ Elija el producto"'),
        (f'{r("Unidad")}="?"', '"✖ El producto no existe en el catálogo"'),
        (f'{activo}="NO"', '"✖ Producto inactivo (descontinuado)"'),
        (f'{r("Cantidad")}=""', '"○ Escriba la cantidad"'),
        (f'OR(NOT(ISNUMBER({r("Cantidad")})),N({r("Cantidad")})<=0)', '"✖ Cantidad no válida: debe ser mayor que 0"'),
        (f'AND({r("Cantidad")}<>INT({r("Cantidad")}),{dec}="NO")', f'"✖ "&{r("Unidad")}&" no admite decimales"'),
        (f'AND({r("Fecha")}<>"",OR(NOT(ISNUMBER({r("Fecha")})),N({r("Fecha")})>TODAY(),N({r("Fecha")})<cfgFechaMin))',
         '"✖ Fecha no válida (futura o anterior a la mínima)"'),
    ]
    if bodega:
        pares += [
            (f'N({r("Factor")})=0', '"✖ Tipo no válido para Bodega"'),
            (f'AND(LEFT({r("Tipo")},6)="AJUSTE",{r("Observaciones")}="")', '"✖ Los ajustes exigen observación"'),
            (f'AND({r("Tipo")}="SALDO INICIAL",COUNTIFS(tblEntradas[SKU],{r("SKU")},tblEntradas[Tipo],'
             f'"SALDO INICIAL",tblEntradas[Estado],"✔*")>0)', '"✖ Ya tiene SALDO INICIAL: use un AJUSTE"'),
        ]
    pares.append((f'AND(ISNUMBER({r("Queda")}),{r("Queda")}<0)',
                  f'"✖ Stock insuficiente: disponible "&{r("Disponible")}&" "&{r("Unidad")}'))
    valid = nested_if(pares, f'"✔ Lista · quedarán "&{r("Queda")}&" "&{r("Unidad")}&" · pulse Registrar"')
    disp = (f'=IF(OR({r("SKU")}="",{r("Unidad")}="?"),"",SUMIFS(tblEntradas[CantidadNeta],tblEntradas[SKU],'
            f'{r("SKU")},tblEntradas[Estado],"✔*")+SUMIFS(tblSalidas[CantidadNeta],tblSalidas[SKU],{r("SKU")},'
            f'tblSalidas[Estado],"✔*"))')
    factor = (f'=IF({r("Tipo")}="","",IFERROR(INDEX(tblTiposMov[FactorStock],MATCH({r("Tipo")},tblTiposMov[Tipo],0))'
              f'*(INDEX(tblTiposMov[Dominio],MATCH({r("Tipo")},tblTiposMov[Tipo],0))="BODEGA"),0))'
              if bodega else f'=IF({r("Correo")}="","",-1)')
    tipo = Col("Tipo", "in", 0, "text", HELP_CAPT["Tipo"]) if bodega else \
        Col("Tipo", "calc", 0, "text", "Siempre SALIDA en este fragmento.", formula=f'=IF({r("Correo")}="","","SALIDA")')
    spec = {
        "Usuario": Col("Usuario", "calc", 0, "text", HELP_CAPT["Usuario"],
                       formula=f'=IFERROR(INDEX(tblUsuarios[Nombre],MATCH({n},tblUsuarios[{orden}],0)),"")'),
        "Tipo": tipo,
        "Fecha": Col("Fecha", "in", 0, "date", HELP_CAPT["Fecha"]),
        "Producto": Col("Producto", "in", 0, "text", HELP_CAPT["Producto"]),
        "Cantidad": Col("Cantidad", "in", 0, "qty", HELP_CAPT["Cantidad"]),
        "Documento": Col("Documento", "in", 0, "text", HELP_CAPT["Documento"]),
        "Observaciones": Col("Observaciones", "in", 0, "text", HELP_CAPT["Observaciones"]),
        "Disponible": Col("Disponible", "calc", 0, "strong", HELP_CAPT["Disponible"], formula=disp),
        "Validación": Col("Validación", "calc", 0, "status", HELP_CAPT["Validación"], formula="=" + valid),
        "Resultado": Col("Resultado", "calc", 0, "status", HELP_CAPT["Resultado"]),
        "Unidad": Col("Unidad", "calc", 0, "center", HELP_CAPT["Unidad"],
                      formula=f'=IF({r("SKU")}="","",IFERROR(INDEX(tblProductos[Unidad],MATCH({r("SKU")},'
                              f'tblProductos[SKU],0)),"?"))'),
        "Factor": Col("Factor", "calc", 0, "factor", HELP_CAPT["Factor"], formula=factor),
        "Correo": Col("Correo", "calc", 0, "text", HELP_CAPT["Correo"],
                      formula=f'=IFERROR(INDEX(tblUsuarios[Correo],MATCH({n},tblUsuarios[{orden}],0)),"")'),
        "SKU": Col("SKU", "calc", 0, "center", HELP_CAPT["SKU"],
                   formula=f'=IF({r("Producto")}="","",TRIM(LEFT({r("Producto")},FIND(" · ",{r("Producto")}&" · ")-1)))'),
        "Queda": Col("Queda", "calc", 0, "qty", HELP_CAPT["Queda"],
                     formula=f'=IF(OR({r("Disponible")}="",NOT(ISNUMBER({r("Cantidad")})),N({r("Factor")})=0),"",'
                             f'{r("Disponible")}+{r("Cantidad")}*{r("Factor")})'),
    }
    cols = [spec[name] for name in CAPTURE_COLS]
    for i, c in enumerate(cols):
        c.width = TX_WIDTHS[1 + i]
    return cols


def bitacora_cols() -> list[Col]:
    cols = [Col(name, "calc", TX_WIDTHS[1 + i], FMT_LED[name], HELP_LED[name]) for i, name in enumerate(LEDGER_COLS)]
    return cols


def build_transaccional(ctx: Ctx, hoja: str, ledger_rows: list[dict], capture_inputs: dict[int, dict]):
    """Construye 10A_ENTRADAS o 10B_SALIDAS.

    ledger_rows: filas de la bitácora oficial (dict por nombre de columna); capture_inputs: {n.º de fila: valores}.
    """
    ws, st = ctx.sheets[hoja], ctx.st
    bodega = hoja == S_ENT
    lienzo(ws, TX_WIDTHS, zoom=85, hidden_cols=TX_HIDDEN)
    if bodega:
        franja(ctx, ws, TX_WIDTHS, "Entradas y ajustes · Bodega",
               "Fragmento de escritura de Bodega · Cada persona escribe en SU fila y pulsa Registrar · "
               "El script consolida en la bitácora oficial con su correo y la hora", "entradas")
    else:
        franja(ctx, ws, TX_WIDTHS, "Salidas · Ventas",
               "Fragmento de escritura de Ventas · Cada vendedor escribe en SU fila · Si no hay stock, la cantidad "
               "se tiñe de rojo tachado y el script bloquea la salida", "salidas")
    barra(ctx, ws)
    canvas = st(bg_color=C["canvas"])
    for r0 in (6, CAP_LAST, CAP_LAST + 1, CAP_LAST + 2):
        ws.set_row_pixels(r0, 22 if r0 in (6, CAP_LAST + 1) else 12, canvas)

    # --- 1. CAPTURA
    tcap, tled = T_CAPT[hoja], T_LED[hoja]
    ccols = captura_cols(hoja)
    seccion(ctx, ws, 6, 1, "1 · CAPTURA — cada persona escribe solo en SU fila (nadie comparte celdas) y pulsa "
                           "el botón Registrar")
    tabla(ctx, ws, tcap, ccols, CAP_HDR, MAX_CAPT, badges=False)
    ws.set_row_pixels(CAP_HDR - 1, 34)
    lock_inputs = bool(ctx.layout.get("pwd_bodega" if bodega else "pwd_ventas"))
    fin = {c.name: st.cell("in", c.fmt) for c in ccols if c.kind == "in"}
    if lock_inputs:
        fin = {k: st(**{**_props_in(c.fmt), "locked": True}) for k, c in ((c.name, c) for c in ccols if c.kind == "in")}
    res = st(font_size=9, bold=True, indent=1, bg_color=C["calc_fill"], border=1, border_color=C["calc_border"])
    for k in range(MAX_CAPT):
        r0 = CAP_FIRST - 1 + k
        ws.set_row_pixels(r0, 24)
        vals = capture_inputs.get(k + 1, {})
        for i, c in enumerate(ccols):
            col = 1 + i
            if c.kind == "in":
                v = vals.get(c.name)
                if isinstance(v, (int, float)):
                    ws.write_number(r0, col, v, fin[c.name])
                elif isinstance(v, dt.date):
                    ws.write_datetime(r0, col, dt.datetime.combine(v, dt.time()), fin[c.name])
                elif v:
                    ws.write_string(r0, col, v, fin[c.name])
                else:
                    ws.write_blank(r0, col, None, fin[c.name])
            elif c.name == "Resultado":
                v = vals.get("Resultado", "")
                if v:
                    ws.write_string(r0, col, v, res)
                else:
                    ws.write_blank(r0, col, None, res)
    _captura_validaciones(ctx, ws, hoja, ccols)
    _captura_formatos(ctx, ws, hoja)
    first_in = CAPTURE_INPUTS[hoja][0]
    rango_in = f"{ccol(first_in)}{CAP_FIRST}:{ccol('Observaciones')}{CAP_LAST}"
    ws.unprotect_range(rango_in, "Captura Bodega" if bodega else "Captura Ventas",
                       ctx.layout.get("pwd_bodega" if bodega else "pwd_ventas"))

    # --- 2. BITÁCORA OFICIAL
    seccion(ctx, ws, CAP_LAST + 1, 1, "2 · BITÁCORA OFICIAL — la escribe solo el script: inmutable y auditada "
                                      "(Usuario_O365 y Timestamp en columnas ocultas)")
    ws.write_string(CAP_LAST + 2, 1, "Los registros se agregan al final. Un rechazo por registro simultáneo queda en rojo "
                                     "tachado y no suma. Para ver el historial de un producto, filtre por Producto "
                                     "(use Vista de hoja para no afectar a los demás).",
                    st(font_size=8.5, italic=True, font_color=C["muted"], bg_color=C["canvas"], indent=1))
    ws.set_row_pixels(CAP_LAST + 2, 20, canvas)
    lcols = bitacora_cols()
    tabla(ctx, ws, tled, lcols, LED_HDR, max(1, len(ledger_rows)), badges=False)
    fl = {c.name: st.cell("calc", c.fmt) for c in lcols}
    fl["Timestamp"] = st(num_format="dd/mm/yyyy hh:mm:ss", align="center", font_size=9, font_color=C["calc_text"],
                         bg_color=C["calc_fill"], border=1, border_color=C["calc_border"], formula=True)
    fl["ID"] = st(font_name="Consolas", font_size=8.5, align="center", font_color=C["faint"],
                  bg_color=C["calc_fill"], border=1, border_color=C["calc_border"], formula=True)
    for k, row in enumerate(ledger_rows or [{}]):
        r0 = LED_FIRST - 1 + k
        for i, name in enumerate(LEDGER_COLS):
            v = row.get(name, "")
            f = fl[name]
            if isinstance(v, dt.datetime):
                ws.write_datetime(r0, 1 + i, v, f)
            elif isinstance(v, dt.date):
                ws.write_datetime(r0, 1 + i, dt.datetime.combine(v, dt.time()), f)
            elif isinstance(v, (int, float)) and not isinstance(v, bool):
                ws.write_number(r0, 1 + i, v, f)
            elif v:
                ws.write_string(r0, 1 + i, v, f)
            else:
                ws.write_blank(r0, 1 + i, None, f)
    _bitacora_formatos(ctx, ws)

    # --- barra de herramientas (fila 6): contadores + lugar del botón del script
    pre = "Ent" if bodega else "Sal"
    chip(ctx, ws, 5, 1, 1, f'="Registros: "&FIXED(kpiReg{pre},0)')
    chip(ctx, ws, 5, 2, 3, f'=IF(kpiRech{pre}=0,"✔ Sin rechazos","✖ Rechazados: "&kpiRech{pre})')
    chip(ctx, ws, 5, 4, 4, f'="Pendientes en captura: "&kpiPend{pre}')
    marcador_script(ctx, ws, 5, 9, 10, "Registrar en bodega" if bodega else "Registrar salida",
                    "RegistrarEntrada" if bodega else "RegistrarSalida")
    tip = st(font_size=8.5, italic=True, font_color=C["muted"], bg_color=C["canvas"], indent=1, text_wrap=True)
    celda(ws, st, 5, 5, 8, "Complete su fila, revise que Validación diga ✔ y pulse el botón de la derecha. "
                           "Rojo tachado = no hay stock suficiente.", tip)
    ws.freeze_panes(6, 0)
    ws.set_selection(f"{ccol(first_in)}{CAP_FIRST}")
    ws.set_landscape()
    ws.set_paper(1)
    ws.fit_to_pages(1, 0)


def _props_in(fmt: str) -> dict:
    from minv.base import FMT
    p = dict(FMT[fmt])
    p.update(bg_color=C["white"], border=1, border_color=C["input_border"])
    return {"font_name": "Segoe UI", "font_size": 10, "font_color": C["text"], "valign": "vcenter", **p}


def _captura_validaciones(ctx: Ctx, ws, hoja: str, ccols: list[Col]):
    rng = lambda n: f"{ccol(n)}{CAP_FIRST}:{ccol(n)}{CAP_LAST}"  # noqa: E731
    if hoja == S_ENT:
        ws.data_validation(rng("Tipo"), {"validate": "list", "source": "=lstTiposBodega",
                                         **msg("Tipo de movimiento", "ENTRADA, SALDO INICIAL, AJUSTE (+) o AJUSTE (-)."
                                                                     " Alt + ↓ abre la lista."),
                                         "error_title": "Tipo no válido",
                                         "error_message": "Elija un tipo de la lista. Las salidas se registran en "
                                                          "10B_SALIDAS."})
    ws.data_validation(rng("Fecha"), {"validate": "date", "criteria": "between", "minimum": "=cfgFechaMin",
                                      "maximum": "=TODAY()",
                                      **msg("Fecha (opcional)", "Vacía = hoy. Otro día: dd/mm/aaaa. No se admiten "
                                                                "fechas futuras."),
                                      "error_title": "Fecha no válida",
                                      "error_message": "Use dd/mm/aaaa, sin fechas futuras ni anteriores a la mínima."})
    ws.data_validation(rng("Producto"), {"validate": "list", "source": "=lstProductos",
                                         **msg("Producto", "Elija de la lista. Escriba parte del nombre o del SKU "
                                                           "para filtrarla. Nunca lo escriba completo a mano."),
                                         "error_title": "Producto no válido",
                                         "error_message": "Elija un producto de la lista del catálogo."})
    q_ = f"{ccol('Cantidad')}{CAP_FIRST}"
    u_ = f"${ccol('Unidad')}{CAP_FIRST}"
    ws.data_validation(rng("Cantidad"), {
        "validate": "custom",
        "value": f'=AND(ISNUMBER({q_}),{q_}>0,IFERROR(OR(INDEX(lstUniDec,MATCH({u_},lstUniCod,0))="SI",'
                 f'{q_}=INT({q_})),TRUE))',
        **msg("Cantidad", "Mayor que 0, en la unidad del producto. UND, CAJA, PAQ, PAR y ROLLO: solo enteros."),
        "error_title": "Cantidad no válida",
        "error_message": "Debe ser mayor que 0. Si la unidad no admite decimales, use un entero."})
    ws.data_validation(rng("Documento"), {"validate": "length", "criteria": "<=", "value": 30,
                                          **msg("Documento (opcional)", "Factura, remisión, orden o cliente."),
                                          "error_title": "Texto muy largo", "error_message": "Máximo 30 caracteres."})
    ws.data_validation(rng("Observaciones"), {"validate": "length", "criteria": "<=", "value": 250,
                                              **msg("Observaciones", "Obligatoria en los AJUSTES: explique el motivo."),
                                              "error_title": "Texto muy largo",
                                              "error_message": "Máximo 250 caracteres."})


def _captura_formatos(ctx: Ctx, ws, hoja: str):
    st = ctx.st
    full = f"B{CAP_FIRST}:{ccol('Queda')}{CAP_LAST}"
    correo = f"${ccol('Correo')}{CAP_FIRST}"
    queda = f"${ccol('Queda')}{CAP_FIRST}"
    # Poka-yoke: la cantidad que dejaría el stock en negativo se tiñe de rojo sangre con texto blanco tachado
    for name in ("Cantidad", "Disponible"):
        ws.conditional_format(f"{ccol(name)}{CAP_FIRST}:{ccol(name)}{CAP_LAST}", {
            "type": "formula", "criteria": f"=AND(ISNUMBER({queda}),{queda}<0)", "format": rojo_sangre(st),
            "stop_if_true": True})
    ws.conditional_format(full, {"type": "formula", "criteria": f'={correo}=""',
                                 "format": st.cf(bg_color="#F8FAFC", font_color=C["faint"])})
    status_cf(ws, st, f"{ccol('Validación')}{CAP_FIRST}:{ccol('Validación')}{CAP_LAST}",
              f"${ccol('Validación')}{CAP_FIRST}")
    status_cf(ws, st, f"{ccol('Resultado')}{CAP_FIRST}:{ccol('Resultado')}{CAP_LAST}",
              f"${ccol('Resultado')}{CAP_FIRST}")
    ws.conditional_format(f"{ccol('Usuario')}{CAP_FIRST}:{ccol('Usuario')}{CAP_LAST}", {
        "type": "formula", "criteria": f'={correo}<>""',
        "format": st.cf(bold=True, font_color=C["brand_dk"])})
    for name in ("Cantidad", "Disponible"):
        ref = f"{ccol(name)}{CAP_FIRST}"
        ws.conditional_format(f"{ccol(name)}{CAP_FIRST}:{ccol(name)}{CAP_LAST}", {
            "type": "formula", "criteria": f"=AND(ISNUMBER({ref}),{ref}<>INT({ref}))",
            "format": st.cf(num_format="#,##0.00")})
    # celda del usuario en blanco con texto guía
    ws.conditional_format(f"{ccol('Usuario')}{CAP_FIRST}:{ccol('Usuario')}{CAP_LAST}", {
        "type": "formula", "criteria": f'={correo}=""', "format": st.cf(num_format=';;;"(sin asignar)"')})


def _bitacora_formatos(ctx: Ctx, ws):
    st = ctx.st
    a, b = LED_FIRST, LED_CF_LAST
    est = f"${lcol('Estado')}{a}"
    fac = f"${lcol('FactorStock')}{a}"
    # Rechazo (p. ej., registro simultáneo que dejaría stock negativo): rojo sangre, blanco tachado
    for name in ("Cantidad", "CantidadNeta", "Estado"):
        ws.conditional_format(f"{lcol(name)}{a}:{lcol(name)}{b}", {
            "type": "formula", "criteria": f'=LEFT({est},1)="✖"', "format": rojo_sangre(st), "stop_if_true": True})
    status_cf(ws, st, f"{lcol('Estado')}{a}:{lcol('Estado')}{b}", est)
    for name in ("Tipo", "CantidadNeta", "FactorStock"):
        ws.conditional_format(f"{lcol(name)}{a}:{lcol(name)}{b}", {
            "type": "formula", "criteria": f"={fac}=1", "format": st.cf(font_color=C["green"], bold=True)})
        ws.conditional_format(f"{lcol(name)}{a}:{lcol(name)}{b}", {
            "type": "formula", "criteria": f"={fac}=-1", "format": st.cf(font_color=C["red"], bold=True)})
    for name, nf in (("Cantidad", "#,##0.00"), ("CantidadNeta", "+#,##0.00;-#,##0.00;0")):
        ref = f"{lcol(name)}{a}"
        ws.conditional_format(f"{lcol(name)}{a}:{lcol(name)}{b}", {
            "type": "formula", "criteria": f"=AND(ISNUMBER({ref}),{ref}<>INT({ref}))", "format": st.cf(num_format=nf)})
    key = f"${lcol('ID')}{a}"
    ws.conditional_format(f"B{a}:{lcol('Timestamp')}{b}", {
        "type": "formula", "criteria": f'=AND({key}<>"",MOD(ROW(),2)=0)', "format": st.cf(bg_color=C["zebra"])})
