"""M-INV · 99_AYUDA: guía didáctica por rol, primeros pasos (estado en vivo) y procedimientos."""
from __future__ import annotations

import demo_data as D

from .base import (C, ESTADOS, FONT_SB, MAX_MOV, S_ALERT, S_AYUDA, S_CONTEO, S_PEDIDO, S_PORTADA, S_PROD, S_PROV,
                   S_STOCK, VERSION, Ctx, app_sheet, band, q, textbox)

GRID = [24] + [94] * 12 + [120]


def build_ayuda(ctx: Ctx):
    ws, st, wb = ctx.sheets[S_AYUDA], ctx.st, ctx.wb
    app_sheet(ws, GRID)
    band(ctx, ws, "Guía rápida", "Aprenda a operar M-INV en 5 minutos · Rutas por rol, pasos y código de colores",
         "guia", widths=GRID)
    canvas = st(bg_color=C["canvas"])
    row_h = {4: 18}
    state = {"r": 5}
    sec = st(bold=True, font_color=C["brand"], font_size=11, bg_color=C["canvas"])
    para = st(font_size=10, font_color=C["text"], bg_color=C["canvas"], text_wrap=True, valign="top")
    muted = st(font_size=9, font_color=C["muted"], bg_color=C["canvas"], text_wrap=True, valign="top")

    def section(title):
        r = state["r"]
        row_h[r] = 30
        ws.merge_range(r, 1, r, 12, title, sec)
        state["r"] += 1
        return r

    def paragraph(text, h=36, fmt=None):
        r = state["r"]
        row_h[r] = h
        ws.merge_range(r, 1, r, 12, text, fmt or para)
        state["r"] += 1

    def gap(h=14):
        row_h[state["r"]] = h
        state["r"] += 1

    def steps(items, color=C["brand"]):
        num = st(bold=True, font_color=C["white"], bg_color=color, align="center", font_size=11)
        txt = st(font_size=10, bg_color=C["white"], text_wrap=True, indent=1, border=1, border_color=C["border"])
        for i, p in enumerate(items, 1):
            r = state["r"]
            row_h[r] = 34
            ws.write_number(r, 1, i, num)
            ws.merge_range(r, 2, r, 12, p, txt)
            state["r"] += 1

    # 1. rutas por rol
    section("¿QUÉ NECESITA HACER HOY?")
    roles = [
        ("BODEGA", "Registrar lo que entra y sale de la bodega y contar la mercancía.", "#1565C0",
         [("Registrar", "internal:irRegistrar"), ("Toma física", f"internal:{q(S_CONTEO)}!A1")]),
        ("COMPRAS", "Saber qué pedir, cuánto y a qué proveedor, antes de que se agote.", "#BF360C",
         [("Ver alertas", f"internal:{q(S_ALERT)}!A1"), ("Pedido sugerido", f"internal:{q(S_PEDIDO)}!A1")]),
        ("GERENCIA", "Ver la salud y el valor del inventario de un vistazo.", "#00796B",
         [("Tablero", f"internal:{q(S_PORTADA)}!A1"), ("Stock actual", f"internal:{q(S_STOCK)}!A1")]),
        ("ADMINISTRACIÓN", "Mantener productos y proveedores al día.", "#5E35B1",
         [("Catálogo", f"internal:{q(S_PROD)}!A1"), ("Proveedores", f"internal:{q(S_PROV)}!A1")]),
    ]
    r = state["r"]
    row_h[r] = 160
    for i, (role, desc, color, buttons) in enumerate(roles):
        x = i * 286
        textbox(ws, "", x, 6, 270, 148, tag="card", desc=f"Rol {role}", fill=C["white"], anchor=(r, 1))
        textbox(ws, role, x + 14, 16, 242, 26, tag="pill", desc=f"Rol {role}", fill=color,
                font={"name": FONT_SB, "size": 9.5, "color": C["white"]}, anchor=(r, 1))
        textbox(ws, desc, x + 16, 50, 238, 44, tag="txt", desc=desc, halign="left", valign="top",
                font={"size": 9.5, "color": C["text"]}, anchor=(r, 1))
        for j, (label, url) in enumerate(buttons):
            textbox(ws, label, x + 14 + j * 124, 104, 118, 34, tag="btn", desc=f"{role}: {label}", fill=color,
                    font={"name": FONT_SB, "size": 9, "color": C["white"]}, url=url, tip=label, anchor=(r, 1))
    state["r"] += 1
    gap()

    # 2. primeros pasos
    first_row = section("PRIMEROS PASOS  ·  implementación (el estado se actualiza solo)")
    wb.define_name("irPrimerosPasos", f"={q(S_AYUDA)}!$B${first_row + 1}")
    checklist = [
        ('=IF(cfgEmpresa<>"NOMBRE DE SU EMPRESA","✔","○")',
         '="Empresa y bodega configuradas (01_CONFIG, lo hace el administrador): "&cfgEmpresa', None),
        ('=IF(kpiCategorias>0,"✔","○")', '="Categorías definidas: "&kpiCategorias&" (03_CATEGORIAS, administrador)"',
         None),
        ('=IF(kpiProveedores>0,"✔","○")', '="Proveedores registrados: "&kpiProveedores&" (recomendado para el pedido)"',
         f"internal:{q(S_PROV)}!A1"),
        ('=IF(kpiCatalogo>0,"✔","○")', '="Productos en el catálogo: "&kpiCatalogo', "internal:irFilaLibreProd"),
        ('=IF(AND(kpiActivos>0,kpiConSaldoInicial>=kpiActivos),"✔","○")',
         '="Productos con SALDO INICIAL: "&kpiConSaldoInicial&" de "&kpiActivos&" activos"', "internal:irRegistrar"),
        ('=IF(kpiOperativos>0,"✔","○")', '="Movimientos del día a día registrados: "&kpiOperativos',
         "internal:irRegistrar"),
    ]
    mark = st(bold=True, font_size=14, align="center", bg_color=C["white"], border=1, border_color=C["border"],
              font_color=C["muted"], formula=True)
    line = st(font_size=10, indent=1, bg_color=C["white"], border=1, border_color=C["border"], formula=True)
    start = state["r"]
    for i, (status, text, url) in enumerate(checklist):
        r = state["r"]
        row_h[r] = 32
        ws.write_formula(r, 1, status, mark)
        ws.merge_range(r, 2, r, 10, text, line)
        ws.merge_range(r, 11, r, 12, "", st(bg_color=C["white"], border=1, border_color=C["border"]))
        if url:
            textbox(ws, "Ir  ➜", 20, 5, 148, 22, tag="chip", desc="Ir al paso", fill=C["brand_lt"],
                    font={"size": 8.5, "bold": True, "color": C["brand"]}, url=url, tip="Ir a esta tarea",
                    anchor=(r, 11))
        state["r"] += 1
    ws.conditional_format(start, 1, state["r"] - 1, 1, {"type": "cell", "criteria": "==", "value": '"✔"',
                                                        "format": st.cf(font_color=C["green"], bg_color=C["green_lt"])})
    gap()

    # 3. cómo funciona
    section("¿CÓMO FUNCIONA M-INV?")
    paragraph("M-INV separa ESCRIBIR de LEER (arquitectura CQRS). Usted solo escribe movimientos; el stock, las "
              "alertas, el pedido y el tablero se calculan solos. Así el inventario siempre es trazable y nadie puede "
              "\"inventar\" un saldo.")
    r = state["r"]
    row_h[r] = 96
    reg = ("②  REGISTRAR\n12_REGISTRO → bitácora\nEscritura guiada" if ctx.plus
           else "②  MOVIMIENTOS\n10_MOVIMIENTOS\nEscritura · append-only")
    flow = [
        ("①  CATÁLOGO\n05_PRODUCTOS\nDatos maestros", C["purple"], f"internal:{q(S_PROD)}!A1"),
        (reg, C["brand"], "internal:irRegistrar"),
        ("③  STOCK\n15_STOCK\nLectura · proyección", C["teal"], f"internal:{q(S_STOCK)}!A1"),
        ("④  ALERTAS Y PEDIDO\n16_ALERTAS · 18_PEDIDO\nLectura · acción", "#C62828", f"internal:{q(S_ALERT)}!A1"),
    ]
    bw, gapx = 236, 61
    for i, (text, color, url) in enumerate(flow):
        x = i * (bw + gapx)
        textbox(ws, text, x, 8, bw, 80, tag="tile", desc=f"Diagrama: {text.splitlines()[0]}", fill=color,
                font={"name": FONT_SB, "size": 10, "color": C["white"]}, url=url, tip="Ir a la hoja", anchor=(r, 1))
        if i < 3:
            textbox(ws, "➜", x + bw + 8, 30, gapx - 16, 36, tag="txt", desc="Flecha del flujo",
                    font={"size": 20, "bold": True, "color": C["faint"]}, anchor=(r, 1))
    state["r"] += 1
    paragraph("Regla de oro: el stock NUNCA se escribe a mano. Cada cambio es un registro nuevo en la bitácora con su "
              "FactorStock (+1 suma, -1 resta); 15_STOCK suma esos registros con SUMAR.SI.CONJUNTO.", 36, muted)
    gap()

    # 4. registrar
    if ctx.plus:
        section("REGISTRAR UN MOVIMIENTO CON EL FORMULARIO")
        steps([
            "Pulse «REGISTRAR» en la portada o en la barra superior: se abre el formulario guiado.",
            "Elija el Tipo (ENTRADA, SALIDA, AJUSTE o SALDO INICIAL). Si deja la Fecha vacía se usa la de hoy.",
            "Encuentre el producto: escriba parte del nombre o del SKU en Buscar (o elija una Categoría) y "
            "selecciónelo en Producto (Alt + ↓).",
            "Escriba la Cantidad: la vista previa muestra el stock actual, el stock resultante y su semáforo.",
            "Elija el Responsable y, si es un ajuste, explique el motivo en Observaciones.",
            "Cuando la validación diga «✔ Listo», pulse REGISTRAR MOVIMIENTO: se guarda en la bitácora y queda sellado.",
            "El formulario conserva Tipo, Fecha y Responsable para registrar el siguiente más rápido.",
        ])
    else:
        section("REGISTRAR UN MOVIMIENTO EN LA BITÁCORA")
        steps([
            "Pulse «REGISTRAR» en la portada: el cursor queda en la siguiente fila libre, resaltada en azul claro.",
            "Fecha: escríbala (dd/mm/aaaa) o presione Ctrl + ; para la fecha de hoy.",
            "Tipo: ENTRADA, SALIDA, AJUSTE (+), AJUSTE (-) o SALDO INICIAL. El sistema asigna el FactorStock.",
            "Categoría y luego Producto: la lista de productos se filtra por la categoría elegida (Alt + ↓).",
            "Cantidad: siempre positiva y en la unidad que muestra la columna Unidad. El signo lo pone el sistema.",
            "Documento (opcional), Responsable y Observaciones (obligatorias en los ajustes).",
            "Revise la columna Estado: debe decir ✔ Registrado. Si ve ✖ o ⚠, lea el mensaje y corrija.",
        ])
    gap()

    # 5. consulta
    section("CONSULTAR UN PRODUCTO (KARDEX)")
    steps([
        "Pulse «CONSULTAR» en la portada. En Buscar escriba parte del nombre o del SKU (o elija una Categoría).",
        "Elija el producto en la lista: verá su stock, semáforo, valor, días sin movimiento y un gráfico de evolución.",
        "Abajo está su historial: cada entrada, salida y ajuste con el saldo que dejó (los 100 más recientes).",
    ], C["purple"])
    gap()

    # 6. colores
    section("CÓDIGO DE COLORES")
    r = state["r"]
    row_h[r] = 30
    ws.write_string(r, 1, "Texto", st(bg_color=C["white"], border=1, border_color=C["input_border"], indent=1))
    ws.merge_range(r, 2, r, 6, "  ✎  Celda de ingreso: usted escribe o selecciona.", para)
    ws.write_string(r, 7, "Cálculo", st(bg_color=C["calc_fill"], font_color=C["calc_text"], border=1,
                                         border_color=C["calc_border"], indent=1))
    ws.merge_range(r, 8, r, 12, "  ƒx  Celda calculada: bloqueada, la llena el sistema.", para)
    state["r"] += 1
    gap(8)
    for i, e in enumerate(ESTADOS):
        r = state["r"]
        row_h[r] = 26
        ws.merge_range(r, 1, r, 2, e[0], st(bold=True, font_size=9, font_color=e[3], bg_color=e[4], align="center",
                                            num_format='"● "@'))
        ws.merge_range(r, 3, r, 12, f'=INDEX(tblEstados[Regla],{i + 1})&"  →  "&INDEX(tblEstados[Acción],{i + 1})',
                       st(font_size=9.5, bg_color=C["canvas"], indent=1, formula=True))
        state["r"] += 1
    paragraph('=" Margen de alerta preventiva: "&FIXED(cfgMargenAlerta*100,0)&"% sobre el mínimo · Sin rotación: más '
              'de "&cfgDiasSinRotacion&" días sin movimiento."', 22,
              st(font_size=9, font_color=C["muted"], bg_color=C["canvas"], italic=True, formula=True))
    gap()

    # 7. tipos de movimiento
    section("TIPOS DE MOVIMIENTO")
    h = st(bold=True, font_size=9, font_color=C["white"], bg_color=C["ink2"], align="center")
    r = state["r"]
    row_h[r] = 26
    ws.merge_range(r, 1, r, 2, "Tipo", h)
    ws.write_string(r, 3, "FactorStock", h)
    ws.merge_range(r, 4, r, 12, "Uso", h)
    state["r"] += 1
    t0 = state["r"]
    for i in range(len(D.TIPOS_MOVIMIENTO)):
        r = state["r"]
        row_h[r] = 26
        base = dict(font_size=9.5, bg_color=C["white"], bottom=1, bottom_color=C["row_line"], formula=True)
        ws.merge_range(r, 1, r, 2, f"=INDEX(tblTiposMov[Tipo],{i + 1})", st(bold=True, indent=1, **base))
        ws.write_formula(r, 3, f"=INDEX(tblTiposMov[FactorStock],{i + 1})",
                         st(bold=True, align="center", num_format="+0;-0;0", **base))
        ws.merge_range(r, 4, r, 12, f"=INDEX(tblTiposMov[Descripción],{i + 1})", st(indent=1, **base))
        state["r"] += 1
    ws.conditional_format(t0, 3, state["r"] - 1, 3, {"type": "cell", "criteria": ">", "value": 0,
                                                     "format": st.cf(font_color=C["green"])})
    ws.conditional_format(t0, 3, state["r"] - 1, 3, {"type": "cell", "criteria": "<", "value": 0,
                                                     "format": st.cf(font_color=C["red"])})
    gap()

    # 8. corregir
    section("¿ME EQUIVOQUÉ? ASÍ SE CORRIGE")
    paragraph("La bitácora es inmutable: nunca borre ni sobrescriba un movimiento registrado. Corrija con un "
              "movimiento compensatorio y deje la explicación en Observaciones." +
              (" En esta edición los registros quedan sellados (en gris) y no se pueden editar." if ctx.plus else ""),
              36)
    paragraph("Ejemplo: se registró una SALIDA de 10 UND pero eran 8 → registre un AJUSTE (+) de 2 UND con la "
              "observación «Corrección del registro ID 245».", 36, muted)
    gap()

    # 9. toma física
    section("TOMA FÍSICA (CONTEO DE INVENTARIO)")
    final = ("Indique Fecha y Responsable arriba y pulse «Generar ajustes»: se registra un AJUSTE por cada diferencia "
             "y el conteo se limpia." if ctx.plus else
             "Registre en la bitácora el AjusteSugerido de cada fila (tipo AJUSTE con observación) y luego borre los "
             "conteos.")
    steps([
        "Abra «CONTEO». Filtre por Ubicación o Categoría para recorrer la bodega en orden.",
        "Para un conteo ciego, oculte la columna StockSistema antes de imprimir la hoja.",
        "Escriba en Conteo lo que encontró. Deje vacío lo que no contó: solo se ajusta lo contado.",
        "Revise Resultado (✔ cuadra · ▲ sobrante · ▼ faltante) y el valor de cada diferencia.",
        final,
    ], C["green"])
    gap()

    # 10. pedido sugerido
    section("PEDIDO SUGERIDO DE COMPRA")
    steps([
        "Abra «PEDIDO»: lista lo agotado, crítico y preventivo con la cantidad para volver al stock máximo.",
        "Elija un proveedor en el filtro para ver solo sus productos, su contacto y la fecha estimada de entrega.",
        "Imprima o guarde en PDF (Ctrl + P) y envíelo al proveedor.",
        "Cuando llegue la mercancía regístrela como ENTRADA" +
        (" (doble clic en la línea abre el formulario ya diligenciado)." if ctx.plus else "."),
    ], "#BF360C")
    gap()

    # 11. atajos
    section("ATAJOS")
    atajos = [("Ctrl + ;", "Inserta la fecha de hoy en la celda."),
              ("Alt + ↓", "Abre la lista desplegable de la celda seleccionada."),
              ("Tab", "Salta al siguiente campo editable (las columnas calculadas se omiten solas)."),
              ("Ctrl + Z", "Deshace el último cambio (antes de guardar)."),
              ("Ctrl + P", "Imprime la hoja activa (pedido, stock, conteo…).")]
    if ctx.plus:
        atajos += [("Doble clic", "En Alertas o Pedido: formulario de reposición. En Stock o Bitácora: consulta del "
                                  "producto.")]
    key = st(bold=True, font_size=9.5, align="center", bg_color=C["white"], border=2, border_color="#CBD5E1")
    for k, desc in atajos:
        r = state["r"]
        row_h[r] = 28
        ws.merge_range(r, 1, r, 2, k, key)
        ws.merge_range(r, 3, r, 12, "  " + desc, para)
        state["r"] += 1
    gap()

    # 12. preguntas frecuentes
    section("PREGUNTAS FRECUENTES")
    faqs = [
        ("¿Por qué no puedo escribir en algunas columnas?",
         "Son cálculos protegidos (ƒx). El bloqueo evita que una fórmula se borre por accidente."),
        ("¿Por qué aparece «✖ Stock insuficiente»?",
         "La salida supera el saldo disponible del producto. Verifique la cantidad o registre primero la entrada."),
        ("¿Cómo agrego un producto o un proveedor?",
         "En CATÁLOGO pulse «Registrar nuevo producto» (el SKU debe ser único y sin espacios); los proveedores se "
         "crean en 04_PROVEEDORES."),
        ("¿Cómo descontinúo un producto?",
         "Escriba NO en la columna Activo: sale de las listas pero conserva todo su historial."),
        ("¿Qué pasa cuando la bitácora se llena?",
         f"La capacidad es de {MAX_MOV:,} registros".replace(",", ".") +
         ". Antes del límite, Z&P realiza el cierre de período: archivo histórico + saldos iniciales en un libro "
         "nuevo."),
    ]
    if ctx.plus:
        faqs.append(("El botón REGISTRAR no hace nada, ¿por qué?",
                     "Las macros están desactivadas. Cierre el libro, clic derecho en el archivo › Propiedades › "
                     "«Desbloquear», y ábralo pulsando «Habilitar contenido»."))
    qf = st(bold=True, font_size=10, bg_color=C["canvas"], font_color=C["ink"])
    for question, answer in faqs:
        r = state["r"]
        row_h[r] = 22
        ws.merge_range(r, 1, r, 12, "▸ " + question, qf)
        state["r"] += 1
        paragraph("   " + answer, 24, muted)
    gap(20)
    r = state["r"]
    row_h[r] = 30
    ws.merge_range(r, 1, r, 12, f"M-INV V{VERSION} · Z&P Software Fast Solutions · Documentación técnica en el "
                                f"repositorio ZP-MINV-Platform (docs/)",
                   st(font_size=8.5, font_color=C["faint"], bg_color=C["canvas"], align="center"))
    state["r"] += 1
    row_h[state["r"]] = 16
    for rr in range(4, state["r"] + 1):
        ws.set_row_pixels(rr, row_h.get(rr, 20), canvas)
    ws.set_selection(0, 0, 0, 0)
