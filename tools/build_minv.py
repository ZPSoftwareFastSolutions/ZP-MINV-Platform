#!/usr/bin/env python3
"""
M-INV V1.2 · Generador del libro Excel (Excel-as-code)
Z&P Software Fast Solutions

Este script ES la fuente de verdad del producto. Los .xlsx/.xlsm se generan, nunca se editan a mano
(ver .claude/excel-architecture-rules.md, regla R-01). El código vive en el paquete tools/minv/ (un módulo por capa).

Ediciones:
    Estándar  .xlsx sin macros (bitácora directa, conteo manual)
    Plus      .xlsm con formulario guiado, ajustes automáticos del conteo, doble clic y sellado.
              Este script genera su base .xlsx en build/; tools/build_xlsm.ps1 le agrega el VBA y la prueba.

Salidas:
    src/M-INV_V1_Core.xlsx                          Estándar · datos demo
    releases/M-INV_V1_Produccion_Bloqueado.xlsx     Estándar · limpio y blindado
    build/M-INV_V1_Core.plus.xlsx                   base Plus (→ src/M-INV_V1_Core.xlsm)
    build/M-INV_V1_Produccion_Bloqueado.plus.xlsx   base Plus (→ releases/M-INV_V1_Produccion_Bloqueado.xlsm)

Uso:
    .venv\\Scripts\\python tools\\build_minv.py
    .venv\\Scripts\\python tools\\build_minv.py --solo core --edicion plus --fin-demo 2026-09-25
    (ciclo completo con verificación: powershell -ExecutionPolicy Bypass -File tools\\build_all.ps1)

Variables de entorno:
    MINV_PASSWORD           contraseña de protección del Core (por defecto: "minv-dev")
    MINV_RELEASE_PASSWORD   contraseña del Release (si falta, usa la del Core y avisa)
"""
from __future__ import annotations

import argparse
import datetime as dt
import os
import sys
from pathlib import Path

import xlsxwriter

sys.path.insert(0, str(Path(__file__).resolve().parent))
import demo_data as D  # noqa: E402
from minv import engine  # noqa: E402
from minv.ayuda import build_ayuda  # noqa: E402
from minv.base import (C, DEV_PASSWORD, HIDDEN_SHEETS, ROOT, S_ALERT, S_AYUDA, S_CAT, S_CONFIG, S_CONTEO,  # noqa: E402
                       S_KARDEX, S_KPI, S_LISTAS, S_MOV, S_PEDIDO, S_PORTADA, S_PROD, S_PROV, S_REG, S_STOCK, S_UNI,
                       VERSION, Ctx, Styles)
from minv.config_sheets import build_categorias, build_config, build_unidades  # noqa: E402
from minv.conteo import build_conteo, conteo_cols  # noqa: E402
from minv.form import build_registro  # noqa: E402
from minv.kardex import build_kardex  # noqa: E402
from minv.ledger import build_movimientos, movimientos_cols  # noqa: E402
from minv.master import build_productos, build_proveedores, productos_cols, proveedores_cols  # noqa: E402
from minv.pedido import build_pedido  # noqa: E402
from minv.portada import build_portada  # noqa: E402
from minv.postprocess import postprocess  # noqa: E402
from minv.projections import build_alertas, build_stock, stock_cols  # noqa: E402

OUT = {
    ("core", "estandar"): ROOT / "src" / "M-INV_V1_Core.xlsx",
    ("release", "estandar"): ROOT / "releases" / "M-INV_V1_Produccion_Bloqueado.xlsx",
    ("core", "plus"): ROOT / "build" / "M-INV_V1_Core.plus.xlsx",
    ("release", "plus"): ROOT / "build" / "M-INV_V1_Produccion_Bloqueado.plus.xlsx",
}

TAB_COLORS = {S_PORTADA: C["brand"], S_PROV: "#8D6E63", S_PROD: C["purple"], S_MOV: C["brand_dk"],
              S_REG: "#1565C0", S_CONTEO: C["green"], S_STOCK: C["teal"], S_ALERT: "#C62828",
              S_KARDEX: "#283593", S_PEDIDO: "#BF360C", S_AYUDA: "#455A64"}


def build(mode: str, edition: str, out: Path, password: str, fin: dt.date) -> dict:
    demo, plus = mode == "core", edition == "plus"
    out.parent.mkdir(parents=True, exist_ok=True)
    tmp = out.with_name(out.stem + ".tmp.xlsx")
    wb = xlsxwriter.Workbook(str(tmp), {"strings_to_numbers": False})
    wb.set_properties({
        "title": "M-INV · Sistema de Inventarios",
        "subject": "Inventario B2B transaccional (CQRS sobre Excel)",
        "author": "Z&P Software Fast Solutions",
        "company": "Z&P Software Fast Solutions",
        "category": "Inventarios",
        "keywords": "M-INV, inventario, CQRS, kardex",
        "comments": "Generado por tools/build_minv.py. No editar a mano: regenerar desde el repositorio.",
        "status": ("Desarrollo (datos demo)" if demo else "Producción") + (" · Plus" if plus else " · Estándar"),
    })
    wb.set_custom_property("M-INV Version", VERSION)
    wb.set_custom_property("M-INV Build", mode)
    wb.set_custom_property("M-INV Edicion", edition)
    ctx = Ctx(wb=wb, st=Styles(wb, hide_formulas=not demo), edition=edition, demo=demo, fin=fin, password=password)

    order = [S_PORTADA, S_CONFIG, S_CAT, S_PROV, S_PROD, S_UNI, S_MOV] + ([S_REG] if plus else []) + \
            [S_CONTEO, S_STOCK, S_ALERT, S_KARDEX, S_PEDIDO, S_LISTAS, S_KPI, S_AYUDA]
    ctx.sheets = {name: wb.add_worksheet(name) for name in order}
    movs, stock_final = D.generar_movimientos(fin) if demo else ([], {})
    cats = D.CATEGORIAS_DEMO if demo else D.CATEGORIAS_BASE

    # Esquemas de tablas primero: las hojas se referencian entre sí por letra de columna.
    ctx.tables["tblProveedores"] = proveedores_cols()
    ctx.tables["tblProductos"] = productos_cols()
    ctx.tables["tblMovimientos"] = movimientos_cols()
    ctx.tables["tblStock"] = stock_cols(ctx)
    ctx.tables["tblConteo"] = conteo_cols(ctx)
    engine.allocate(ctx)
    if movs:  # la consulta abre con el producto de mayor historial (demo)
        conteo = {}
        for m in movs:
            conteo[m.producto] = conteo.get(m.producto, 0) + 1
        ctx.layout["kardex_default"] = max(conteo, key=conteo.get)

    for name, ref in (("lstTiposMov", "tblTiposMov[Tipo]"), ("lstCategorias", "tblCategorias[Categoría]"),
                      ("lstUniCod", "tblUnidades[Código]"), ("lstUniDec", "tblUnidades[Decimales]"),
                      ("lstResponsables", "tblResponsables[Nombre]")):
        wb.define_name(name, "=" + ref)

    build_config(ctx)
    build_categorias(ctx, ctx.sheets[S_CAT])
    build_unidades(ctx, ctx.sheets[S_UNI])
    build_proveedores(ctx)
    build_productos(ctx)
    build_movimientos(ctx, movs)
    if plus:
        build_registro(ctx)
    build_conteo(ctx)
    build_stock(ctx)
    build_alertas(ctx)
    build_kardex(ctx)
    build_pedido(ctx)
    engine.build_listas(ctx)
    engine.build_kpis(ctx, len(cats))
    engine.define_navigation(ctx)
    build_portada(ctx, len(cats))
    build_ayuda(ctx)

    # Protección y visibilidad por capa
    data = {"autofilter": True, "format_columns": True, "select_locked_cells": True, "select_unlocked_cells": True}
    for name in (S_PROV, S_PROD, S_MOV, S_CONTEO, S_STOCK, S_ALERT, S_PEDIDO):
        ctx.sheets[name].protect(password, data)
    if plus:
        ctx.sheets[S_REG].protect(password, {"select_locked_cells": False, "select_unlocked_cells": True})
    ctx.sheets[S_KARDEX].protect(password, {"select_locked_cells": True, "select_unlocked_cells": True})
    ctx.sheets[S_PORTADA].protect(password, {"select_locked_cells": False, "select_unlocked_cells": False})
    ctx.sheets[S_AYUDA].protect(password, {"select_locked_cells": True})
    for name in HIDDEN_SHEETS:
        ctx.sheets[name].protect(password, {"select_locked_cells": True, "select_unlocked_cells": True})
        if demo:
            ctx.sheets[name].hide()
        else:
            ctx.sheets[name].very_hidden()
        ctx.sheets[name].set_tab_color("#94A3B8")
    for name, color in TAB_COLORS.items():
        if name in ctx.sheets:
            ctx.sheets[name].set_tab_color(color)
    ctx.sheets[S_PORTADA].activate()
    ctx.sheets[S_PORTADA].set_first_sheet()
    wb.close()
    postprocess(tmp, out, None if demo else password)
    return {"movimientos": len(movs), "stock_final": stock_final}


def main():
    ap = argparse.ArgumentParser(description="Genera los libros Excel de M-INV.")
    ap.add_argument("--solo", choices=("core", "release"), help="Generar solo Core o solo Release.")
    ap.add_argument("--edicion", choices=("estandar", "plus"), help="Generar solo una edición.")
    ap.add_argument("--fin-demo", type=dt.date.fromisoformat, default=dt.date.today(),
                    help="Fecha final de la simulación demo (AAAA-MM-DD). Por defecto: hoy.")
    args = ap.parse_args()
    pwd = os.environ.get("MINV_PASSWORD", DEV_PASSWORD)
    rel_pwd = os.environ.get("MINV_RELEASE_PASSWORD")
    warned = False
    for (mode, edition), out in OUT.items():
        if (args.solo and args.solo != mode) or (args.edicion and args.edicion != edition):
            continue
        if mode == "release" and not rel_pwd and not warned:
            print("[aviso]   MINV_RELEASE_PASSWORD no definida: el release usa la contraseña de desarrollo.")
            warned = True
        info = build(mode, edition, out, pwd if mode == "core" else (rel_pwd or pwd), args.fin_demo)
        extra = f"  ({info['movimientos']} movimientos demo)" if mode == "core" else ""
        print(f"[{mode}/{edition}] {out.relative_to(ROOT)}{extra}")


if __name__ == "__main__":
    main()
