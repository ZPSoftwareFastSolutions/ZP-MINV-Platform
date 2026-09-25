"""
M-INV · 12_REGISTRO: formulario guiado para registrar movimientos (edición Plus).

La hoja solo captura y valida; el botón REGISTRAR (VBA, src/macros/modRegistro.bas) copia el movimiento a la
siguiente fila libre de 10_MOVIMIENTOS, verifica su Estado y sella la fila. Así la bitácora sigue siendo la única
capa de escritura (regla R-02). Los cálculos de la vista previa viven en 91_KPIS (nombres frm*).
"""
from __future__ import annotations

from .base import (C, ESTADOS, FONT_SB, S_REG, Ctx, action_button, app_sheet, band, check_msg, decimals_cf,
                   estado_cf, q, status_cf, textbox, toolbar_row)

GRID = [24] + [94] * 12 + [120]

# (nombre definido, etiqueta, fila Excel, obligatorio)
FIELDS = [
    ("frmTipo", "Tipo de movimiento", 8, True),
    ("frmFecha", "Fecha", 9, False),
    ("frmCategoria", "Categoría", 10, False),
    ("frmBuscar", "Buscar", 11, False),
    ("frmProducto", "Producto", 12, True),
    ("frmCantidad", "Cantidad", 13, True),
    ("frmResponsable", "Responsable", 14, True),
    ("frmDocumento", "Documento", 15, False),
    ("frmObservaciones", "Observaciones", 16, False),
]
MSG_ROW = 20          # fila Excel del mensaje de resultado (lo escribe VBA)
CHECK_ROWS = range(15, 22)   # 7 validaciones (I15:M21)


def build_registro(ctx: Ctx):
    ws, st, wb = ctx.sheets[S_REG], ctx.st, ctx.wb
    app_sheet(ws, GRID)
    band(ctx, ws, "Registrar movimiento",
         "Formulario guiado · Valida antes de guardar · Cada registro queda sellado en la bitácora",
         "registrar", widths=GRID)
    toolbar_row(ws, st)
    canvas = st(bg_color=C["canvas"])
    heights = {5: 12, 6: 30, 22: 18}
    for r in range(7, 22):
        heights[r] = 34
    for r, h in heights.items():
        ws.set_row_pixels(r, h, canvas)

    # --- barra superior: último registro + accesos
    textbox(ws, "", 16, 5, 560, 24, tag="chip", desc="Último registro", fill=C["white"],
            line={"color": C["border"], "width": 1}, font={"size": 9, "bold": True},
            textlink=ctx.kref("txtUltimoRegistro"), anchor=(4, 0))
    action_button(ws, "Ver bitácora  ➜", 592, 160, url="internal:irBitacora", fill=C["slate"],
                  tip="Abrir 10_MOVIMIENTOS")
    action_button(ws, "Consultar producto  ➜", 762, 190, url="internal:irConsultar", fill=C["slate"],
                  tip="Abrir la consulta de producto (kardex)")

    sec = st(bold=True, font_color=C["brand"], font_size=10.5, bg_color=C["canvas"])
    ws.merge_range(6, 1, 6, 6, "1 · DATOS DEL MOVIMIENTO", sec)
    ws.merge_range(6, 8, 6, 12, "2 · VISTA PREVIA Y VALIDACIÓN", sec)

    # --- campos
    lbl = st(bold=True, font_size=10, bg_color=C["canvas"], align="right", indent=1)
    opt = st(font_size=9, font_color=C["muted"], bg_color=C["canvas"], align="right", indent=1, italic=True)
    for name, label, row, req in FIELDS:
        r = row - 1
        ws.merge_range(r, 1, r, 2, "", lbl)
        ws.write_rich_string(r, 1, lbl, label, (st(font_color=C["red"], bold=True, bg_color=C["canvas"]) if req
                                                else opt), " *" if req else "  (opcional)", lbl)
        fmt = st.field(num_format="dd/mm/yyyy", align="left") if name == "frmFecha" else \
            st.field(num_format="#,##0") if name == "frmCantidad" else st.field()
        ws.merge_range(r, 3, r, 6, "", fmt)
        wb.define_name(name, f"={q(S_REG)}!$D${row}")

    def dv(name, opts, title, msg):
        check_msg(title, msg)
        row = next(f[2] for f in FIELDS if f[0] == name)
        ws.data_validation(row - 1, 3, row - 1, 3, {**opts, "input_title": title, "input_message": msg})

    dv("frmTipo", {"validate": "list", "source": "=lstTiposMov", "error_title": "Tipo no válido",
                   "error_message": "Seleccione un tipo de la lista (Alt + ↓)."},
       "Tipo de movimiento", "ENTRADA suma, SALIDA resta, AJUSTE corrige y SALDO INICIAL carga el inventario "
                             "existente. Alt + ↓ abre la lista.")
    dv("frmFecha", {"validate": "date", "criteria": "between", "minimum": "=cfgFechaMin", "maximum": "=TODAY()",
                    "error_title": "Fecha no válida",
                    "error_message": "Use dd/mm/aaaa. No se permiten fechas futuras ni anteriores a la mínima."},
       "Fecha (opcional)", "Vacía = hoy. Si es de otro día escríbala dd/mm/aaaa (Ctrl + ; inserta la de hoy).")
    dv("frmCategoria", {"validate": "list", "source": "=lstCategorias", "error_title": "Categoría no válida",
                        "error_message": "Seleccione una categoría de la lista."},
       "Categoría (opcional)", "Filtra la lista de productos. Déjela vacía para ver todos.")
    dv("frmBuscar", {"validate": "length", "criteria": "<=", "value": 40, "error_title": "Texto muy largo",
                     "error_message": "Máximo 40 caracteres."},
       "Buscar (opcional)", "Escriba parte del nombre o del SKU (ej: tornillo, FER-00) y pulse Enter: la lista de "
                            "Producto mostrará solo lo que coincide.")
    dv("frmProducto", {"validate": "list", "source": "=lfForm", "error_title": "Producto no válido",
                       "error_message": "Elija un producto de la lista. Si no aparece, revise Buscar y Categoría."},
       "Producto", "Elija de la lista (Alt + ↓). Está filtrada por Categoría y Buscar. Nunca escriba el nombre a "
                   "mano.")
    dv("frmCantidad", {"validate": "custom",
                       "value": '=AND(ISNUMBER(D13),D13>0,IFERROR(OR(frmDecimales="SI",D13=INT(D13)),TRUE))',
                       "error_title": "Cantidad no válida",
                       "error_message": "Debe ser mayor que 0. Si la unidad no admite decimales, use un entero."},
       "Cantidad", "Siempre positiva y en la unidad del producto. El signo lo pone el tipo de movimiento.")
    dv("frmResponsable", {"validate": "list", "source": "=lstResponsables", "error_title": "Responsable no válido",
                          "error_message": "Seleccione un nombre de la lista."},
       "Responsable", "Quién realiza el movimiento (lista de responsables).")
    dv("frmDocumento", {"validate": "length", "criteria": "<=", "value": 30, "error_title": "Texto muy largo",
                        "error_message": "Máximo 30 caracteres."},
       "Documento (opcional)", "Factura, remisión u orden. Ej: FC-10234.")
    dv("frmObservaciones", {"validate": "length", "criteria": "<=", "value": 250, "error_title": "Texto muy largo",
                            "error_message": "Máximo 250 caracteres."},
       "Observaciones", "Obligatoria en los AJUSTES: explique el motivo (merma, conteo, devolución...).")

    # Campos con error: borde rojo; observación faltante en ajustes: ámbar
    bad = st.cf(bg_color=C["red_lt"], border=2, border_color=C["red"])
    warn = st.cf(bg_color=C["amber_lt"], border=2, border_color=C["amber_mid"])
    ws.conditional_format("D13:G13", {"type": "formula", "criteria": '=AND($D$13<>"",NOT(frmCantOK))', "format": bad})
    ws.conditional_format("D12:G12", {"type": "formula", "criteria": '=AND($D$12<>"",NOT(frmExiste))',
                                      "format": bad})
    ws.conditional_format("D9:G9", {"type": "formula", "criteria": '=LEFT(frmChk7,1)="✖"', "format": bad})
    ws.conditional_format("D16:G16", {"type": "formula", "criteria": '=LEFT(frmChk6,1)="✖"', "format": warn})
    ws.conditional_format("D12:G12", {"type": "formula", "criteria": '=AND($D$12<>"",LEFT(frmChk4,1)="✖")',
                                      "format": bad})

    # --- vista previa (tarjeta blanca)
    card = dict(bg_color=C["white"], formula=True)
    ws.merge_range(7, 8, 7, 12, '=IF(frmProducto="","Elija un producto para ver su información",frmProducto)',
                   st(bold=True, font_size=11, indent=1, top=1, top_color=C["border"], left=1,
                      left_color=C["border"], right=1, right_color=C["border"], **card))
    ws.merge_range(8, 8, 8, 12, "=frmMeta", st(font_size=9, font_color=C["muted"], indent=1, left=1,
                                                left_color=C["border"], right=1, right_color=C["border"], **card))
    ws.merge_range(9, 8, 9, 9, "STOCK ACTUAL", st(font_size=8.5, bold=True, font_color=C["muted"], align="center",
                                                  left=1, left_color=C["border"], bg_color=C["white"]))
    ws.write_string(9, 10, "", st(bg_color=C["white"]))
    ws.merge_range(9, 11, 9, 12, "DESPUÉS DEL MOVIMIENTO", st(font_size=8.5, bold=True, font_color=C["muted"],
                                                              align="center", right=1, right_color=C["border"],
                                                              bg_color=C["white"]))
    big = dict(font_name=FONT_SB, font_size=24, align="center", num_format="#,##0;-#,##0;0", **card)
    ws.merge_range(10, 8, 11, 9, '=IF(frmExiste,frmActual,"—")', st(left=1, left_color=C["border"], **big))
    ws.merge_range(10, 10, 11, 10, "➜", st(font_size=18, font_color=C["faint"], align="center", bg_color=C["white"]))
    ws.merge_range(10, 11, 11, 12, '=IF(frmDespues="","—",frmDespues)', st(right=1, right_color=C["border"], **big))
    pill = dict(bold=True, align="center", font_size=9.5, **card)
    ws.merge_range(12, 8, 12, 9, "=frmEstadoAct", st(left=1, left_color=C["border"], **pill))
    ws.write_string(12, 10, "", st(bg_color=C["white"]))
    ws.merge_range(12, 11, 12, 12, "=frmEstadoDesp", st(right=1, right_color=C["border"], **pill))
    ws.merge_range(13, 8, 13, 12, "VALIDACIÓN", st(font_size=8.5, bold=True, font_color=C["muted"], indent=1,
                                                   left=1, left_color=C["border"], right=1,
                                                   right_color=C["border"], bg_color=C["white"]))
    chk = st(font_size=9.5, indent=1, left=1, left_color=C["border"], right=1, right_color=C["border"], **card)
    for i, r in enumerate(CHECK_ROWS):
        ws.merge_range(r - 1, 8, r - 1, 12, f"=frmChk{i + 1}", chk)
    ws.merge_range(21, 8, 21, 12, "=frmResumen", st(font_size=10.5, bold=True, indent=1, left=1,
                                                    left_color=C["border"], right=1, right_color=C["border"],
                                                    bottom=1, bottom_color=C["border"], **card))
    estado_cf(ws, st, "I13:J13", "$I$13")
    estado_cf(ws, st, "L13:M13", "$L$13")
    for e in ESTADOS:
        color = e[4] if e[3] == "#FFFFFF" else e[3]
        ws.conditional_format("L11:M12", {"type": "formula", "criteria": f'=$L$13="{e[0]}"',
                                          "format": st.cf(font_color=color)})
    decimals_cf(ws, st, "D13:G13", "$D$13", "#,##0.00")
    decimals_cf(ws, st, "I11:J12", "$I$11", "#,##0.00")
    decimals_cf(ws, st, "L11:M12", "$L$11", "#,##0.00")
    status_cf(ws, st, "I15:M21", "I15")
    ws.conditional_format("I22:M22", {"type": "formula", "criteria": "=frmValido",
                                      "format": st.cf(font_color=C["green"], bg_color=C["green_lt"])})
    ws.conditional_format("I22:M22", {"type": "formula", "criteria": "=NOT(frmValido)",
                                      "format": st.cf(font_color=C["amber"], bg_color=C["amber_lt"])})

    # --- botones (macros de la edición Plus) y mensaje de resultado
    action_button(ws, "✔  REGISTRAR MOVIMIENTO", 0, 300, action="RegistrarMovimiento", fill=C["green"], y=4, h=48,
                  anchor=(16, 1), size=12, tip="Guardar en la bitácora (verifica y sella el registro)",
                  desc="Acción: registrar movimiento")
    action_button(ws, "Limpiar", 316, 150, action="LimpiarFormulario", fill="#64748B", y=4, h=48, anchor=(16, 1),
                  size=11, tip="Vaciar el formulario", desc="Acción: limpiar formulario")
    ws.merge_range(MSG_ROW - 1, 1, MSG_ROW - 1, 6, "", st(font_size=10, bold=True, bg_color=C["canvas"],
                                                          font_color=C["green"], indent=1))
    wb.define_name("frmMensaje", f"={q(S_REG)}!$B${MSG_ROW}")
    ws.conditional_format(f"B{MSG_ROW}:G{MSG_ROW}", {"type": "formula", "criteria": f'=LEFT($B${MSG_ROW},1)="✖"',
                                                     "format": st.cf(font_color=C["red"])})
    ws.conditional_format(f"B{MSG_ROW}:G{MSG_ROW}", {"type": "formula", "criteria": f'=LEFT($B${MSG_ROW},1)="⚠"',
                                                     "format": st.cf(font_color=C["amber"])})
    textbox(ws, "⚠  Si ve este aviso, las macros están desactivadas y el botón REGISTRAR no funcionará. Cierre el "
                "libro, clic derecho en el archivo › Propiedades › marque «Desbloquear» y ábralo pulsando «Habilitar "
                "contenido». Mientras tanto puede registrar directamente en la Bitácora.",
            0, 4, 564, 62, tag="banner", desc="Aviso sin macros: habilite el contenido", fill=C["amber_lt"],
            line={"color": C["amber_mid"], "width": 1}, halign="left",
            font={"size": 8.5, "color": C["amber"], "bold": True}, url="internal:irBitacora", anchor=(20, 1))

    ws.set_selection("D8")
