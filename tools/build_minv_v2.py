#!/usr/bin/env python3
"""
M-INV V2 · Generador del libro colaborativo (Excel para la web + Office Scripts)
Z&P Software Fast Solutions

Salidas:
    src/M-INV_V2_Colaborativo.xlsx                   Core · datos demo (usuarios, catálogo, bitácoras, instantánea)
    releases/M-INV_V2_Colaborativo_Produccion.xlsx   Release · limpio y blindado
    build/v2/fixture.json                             Estado del Core para las pruebas de los Office Scripts

Uso:
    .venv\\Scripts\\python tools\\build_minv_v2.py --fin-demo 2026-09-25
    (ciclo completo con pruebas: powershell -ExecutionPolicy Bypass -File tools\\build_v2.ps1)

Variables de entorno:
    MINV_PASSWORD / MINV_RELEASE_PASSWORD   contraseña de hojas (Core / Release), igual que la V1
    MINV_V2_PWD_BODEGA / MINV_V2_PWD_VENTAS contraseña opcional de los rangos de captura por rol
                                            («Permitir editar rangos»); sin ellas, los rangos no piden contraseña
"""
from __future__ import annotations

import argparse
import datetime as dt
import json
import os
import sys
from pathlib import Path

import xlsxwriter

sys.path.insert(0, str(Path(__file__).resolve().parent))
import demo_data as D  # noqa: E402
from minv.base import (C, DEV_PASSWORD, ESTADOS, FIRST, MAX_PROD, ROOT, S_ALERT, S_AYUDA, S_CAT, S_CONFIG, S_KPI,  # noqa: E402
                       S_LISTAS, S_PROD, S_PROV, S_STOCK, S_UNI, Ctx, Styles, this_row)
from minv.config_sheets import build_categorias, build_unidades  # noqa: E402
from minv.master import productos_cols, productos_tabla, proveedores_cols, proveedores_tabla  # noqa: E402
from minv.postprocess import postprocess  # noqa: E402
from minv2 import demo2, motor  # noqa: E402
from minv2.ayuda2 import build_ayuda2  # noqa: E402
from minv2.actividad import ACT_COLS, build_actividad  # noqa: E402
from minv2.base2 import (HIDDEN2, S_ACT, S_CONS, S_CONTEO, S_ENT, S_PB, S_PED, S_PG, S_PV, S_SAL, S_SES,  # noqa: E402
                         S_USR, VERSION2, con_margen, franja, lienzo)
from minv2.config2 import TIPOS2, build_config2, build_usuarios, usuarios_cols  # noqa: E402
from minv2.consulta2 import CONS_COLS, build_consulta  # noqa: E402
from minv2.conteo2 import CONTEO_COLS, build_conteo2  # noqa: E402
from minv2.gerencia import build_portada_gerencia  # noqa: E402
from minv2.lectura import (ALERT_COLS, PEDIDO_COLS, STOCK_COLS, build_alertas2, build_pedido2,  # noqa: E402
                           build_stock2)
from minv2.portadas import build_portada_bodega, build_portada_ventas, series_graficos  # noqa: E402
from minv2.transacciones import build_transaccional  # noqa: E402

OUT = {
    "core": ROOT / "src" / "M-INV_V2_Colaborativo.xlsx",
    "release": ROOT / "releases" / "M-INV_V2_Colaborativo_Produccion.xlsx",
}
FIXTURE = ROOT / "build" / "v2" / "fixture.json"
ORDER = [S_PB, S_PV, S_PG, S_CONFIG, S_USR, S_CAT, S_PROV, S_PROD, S_UNI, S_ENT, S_SAL, S_CONTEO, S_ACT, S_STOCK,
         S_ALERT, S_CONS, S_PED, S_LISTAS, S_KPI, S_SES, S_AYUDA]
TABS = {S_PB: C["green"], S_PV: C["brand"], S_PG: C["ink2"], S_USR: C["slate"], S_PROV: "#8D6E63",
        S_PROD: C["purple"], S_ENT: "#2E7D32", S_SAL: C["brand_dk"], S_CONTEO: C["purple"], S_ACT: C["slate"],
        S_STOCK: C["teal"], S_ALERT: "#C62828", S_CONS: C["teal"], S_PED: "#BF360C", S_AYUDA: "#455A64"}


def productos_cols_v2():
    """Catálogo de la V1 con dos cambios: la etiqueta marca los inactivos y los movimientos se cuentan en 10A + 10B."""
    r = this_row("tblProductos")
    cols = productos_cols()
    for c in cols:
        if c.name == "Etiqueta":
            c.formula = (f'=IF(OR({r("SKU")}="",{r("Producto")}=""),"",{r("SKU")}&" · "&{r("Producto")}&'
                         f'IF({r("Activo")}="NO","  (inactivo)",""))')
            c.help = "Texto de la lista de productos de la captura: SKU · Producto (marca los inactivos)."
        elif c.name == "Movimientos":
            c.formula = (f'=IF({r("SKU")}="","",COUNTIF(tblEntradas[SKU],{r("SKU")})+'
                         f'COUNTIF(tblSalidas[SKU],{r("SKU")}))')
            c.help = "Registros de este SKU en las dos bitácoras oficiales (10A y 10B)."
        elif c.name == "Proveedor":
            c.help = "Proveedor habitual (lista de 04_PROVEEDORES). Agrupa las alertas por proveedor."
        elif c.name == "CostoUnitario":
            c.help = "Costo por unidad (moneda local). Se usa para valorizar el inventario."
    return cols


def build(mode: str, out: Path, password: str, fin: dt.date) -> dict:
    demo = mode == "core"
    out.parent.mkdir(parents=True, exist_ok=True)
    tmp = out.with_name(out.stem + ".tmp.xlsx")
    wb = xlsxwriter.Workbook(str(tmp), {"strings_to_numbers": False})
    wb.set_properties({
        "title": "M-INV · Inventario colaborativo", "subject": "Inventario B2B en Microsoft 365 (CQRS fragmentado)",
        "author": "Z&P Software Fast Solutions", "company": "Z&P Software Fast Solutions", "category": "Inventarios",
        "keywords": "M-INV, inventario, Office Scripts, coautoría, CQRS",
        "comments": "Generado por tools/build_minv_v2.py. No editar a mano: regenerar desde el repositorio.",
        "status": "Desarrollo (datos demo)" if demo else "Producción"})
    wb.set_custom_property("M-INV Version", VERSION2)
    wb.set_custom_property("M-INV Build", mode)
    wb.set_custom_property("M-INV Edicion", "colaborativa")
    ctx = Ctx(wb=wb, st=Styles(wb, hide_formulas=not demo), edition="v2", demo=demo, fin=fin, password=password)
    ctx.layout["pwd_bodega"] = os.environ.get("MINV_V2_PWD_BODEGA") or None
    ctx.layout["pwd_ventas"] = os.environ.get("MINV_V2_PWD_VENTAS") or None
    ctx.sheets = {name: wb.add_worksheet(name) for name in ORDER}
    datos = demo2.generar(fin) if demo else demo2.DatosV2()
    ctx.layout["n_cats"] = len(D.CATEGORIAS_DEMO if demo else D.CATEGORIAS_BASE)

    ctx.tables["tblProveedores"] = proveedores_cols()
    ctx.tables["tblProductos"] = productos_cols_v2()
    for name, ref in (("lstCategorias", "tblCategorias[Categoría]"), ("lstUniCod", "tblUnidades[Código]"),
                      ("lstUniDec", "tblUnidades[Decimales]")):
        wb.define_name(name, "=" + ref)

    meta = {}
    if demo:
        meta = dict(datos.meta)
        meta = {k: meta[k] for k in ("stkActualizado", "stkActualizadoPor", "stkActualizadoNombre", "stkMovimientos")}
    build_config2(ctx, overrides=meta)
    ctx.st.lock_inputs = True            # usuarios y maestros: solo el ADMIN (desprotegiendo la hoja)
    build_usuarios(ctx)
    for sheet, titulo, sub, activo, cols, fn in (
            (S_PROV, "Proveedores", "Datos maestros · Solo ADMIN · Quién le vende cada producto y en cuántos días",
             None, ctx.tables["tblProveedores"], proveedores_tabla),
            (S_PROD, "Catálogo de productos", "Datos maestros · Solo ADMIN · Después de modificarlo pulse "
                                              "«Recalcular stock» · Nunca borre filas (use Activo = NO)",
             "catalogo", ctx.tables["tblProductos"], productos_tabla)):
        ws = ctx.sheets[sheet]
        widths = con_margen([16] + [c.width for c in cols] + [16])
        lienzo(ws, widths, zoom=90)
        franja(ctx, ws, widths, titulo, sub, activo)
        fn(ctx, ws, cols)
    ctx.st.lock_inputs = False
    build_categorias(ctx, ctx.sheets[S_CAT])
    build_unidades(ctx, ctx.sheets[S_UNI])
    build_transaccional(ctx, S_ENT, datos.entradas, datos.captura.get(S_ENT, {}))
    build_transaccional(ctx, S_SAL, datos.salidas, datos.captura.get(S_SAL, {}))
    build_conteo2(ctx, datos.conteo)
    build_actividad(ctx, datos.actividad)
    build_stock2(ctx, datos.stock)
    build_alertas2(ctx, datos.alertas)
    build_consulta(ctx, datos.consulta)
    build_pedido2(ctx, datos.pedido)
    motor.build_listas2(ctx)
    series_graficos(ctx)
    motor.build_kpis2(ctx)
    motor.define_names2(ctx, ctx.col("tblProductos", "Etiqueta"))
    motor.build_sesion(ctx)
    build_portada_bodega(ctx)
    build_portada_ventas(ctx)
    build_portada_gerencia(ctx)
    build_ayuda2(ctx)

    # Protección por capa (la contraseña la usan también los scripts: pauseProtection)
    data = {"autofilter": True, "format_columns": True, "select_locked_cells": True, "select_unlocked_cells": True}
    for name in (S_USR, S_PROV, S_PROD, S_ENT, S_SAL, S_CONTEO, S_ACT, S_STOCK, S_ALERT, S_CONS, S_PED):
        ctx.sheets[name].protect(password, data)
    for name in (S_PB, S_PV, S_PG, S_AYUDA):   # vínculos en celdas: la selección debe estar permitida
        ctx.sheets[name].protect(password, {"select_locked_cells": True, "select_unlocked_cells": True})
    for name in HIDDEN2:
        if name != S_SES:                 # 92_SESION sin proteger: los scripts crean y borran comentarios
            ctx.sheets[name].protect(password, {"select_locked_cells": True, "select_unlocked_cells": True})
        if demo:
            ctx.sheets[name].hide()
        else:
            ctx.sheets[name].very_hidden()
        ctx.sheets[name].set_tab_color("#94A3B8")
    for name, color in TABS.items():
        ctx.sheets[name].set_tab_color(color)
    ctx.sheets[S_PB].activate()
    ctx.sheets[S_PB].set_first_sheet()
    wb.close()
    postprocess(tmp, out, None if demo else password)

    if demo:
        FIXTURE.parent.mkdir(parents=True, exist_ok=True)
        FIXTURE.write_text(json.dumps(_fixture(ctx, datos, password, fin), ensure_ascii=False), encoding="utf-8")
    return {"entradas": len(datos.entradas), "salidas": len(datos.salidas), "actividad": len(datos.actividad)}


def _fixture(ctx: Ctx, datos, password: str, fin: dt.date) -> dict:
    """Tablas y parámetros tal como los leen los scripts (columnas con fórmula ya evaluadas)."""
    prods = demo2.productos_dict()
    pcols = [c.name for c in ctx.tables["tblProductos"]]
    filas_p = []
    for i in range(MAX_PROD):
        p = prods[i] if i < len(prods) else {}
        fila = [p.get(c, "") for c in pcols]
        if p:
            fila[pcols.index("Etiqueta")] = f'{p["SKU"]} · {p["Producto"]}' + ("  (inactivo)" if p["Activo"] == "NO"
                                                                                else "")
        filas_p.append(fila)
    ucols = [c.name for c in usuarios_cols()]
    orden = {"OrdenBodega": 0, "OrdenVentas": 0, "OrdenConsulta": 0}
    filas_u = []
    for correo, nombre, rol in demo2.USUARIOS_DEMO:
        f = {"Correo": correo, "Nombre": nombre, "Rol": rol, "Activo": "SI", "OrdenBodega": "", "OrdenVentas": "",
             "OrdenConsulta": "", "Validación": "✔ Autorizado"}
        for clave, roles in (("OrdenBodega", ("BODEGA", "ADMIN")), ("OrdenVentas", ("VENTAS", "ADMIN")),
                             ("OrdenConsulta", ("ADMIN", "BODEGA", "VENTAS"))):
            if rol in roles:
                orden[clave] += 1
                f[clave] = orden[clave]
        filas_u.append([f[c] for c in ucols])
    extra = {
        "tblUsuarios": {"hoja": S_USR, "encabezados": ucols, "filas": filas_u},
        "tblProductos": {"hoja": S_PROD, "encabezados": pcols, "filas": filas_p},
        "tblUnidades": {"hoja": S_UNI, "encabezados": ["Código", "Unidad", "Decimales", "Descripción"],
                        "filas": [list(u) for u in D.UNIDADES]},
        "tblTiposMov": {"hoja": S_CONFIG, "encabezados": ["Tipo", "FactorStock", "Dominio", "Descripción"],
                        "filas": [list(t) for t in TIPOS2]},
        "tblEstados": {"hoja": S_CONFIG, "encabezados": ["Estado", "Prioridad", "Regla", "Acción"],
                       "filas": [[e[0], e[1], e[6], e[7]] for e in ESTADOS]},
        "tblStock": {"hoja": S_STOCK, "encabezados": STOCK_COLS,
                     "filas": [["" for _ in STOCK_COLS] for _ in range(MAX_PROD)]},
        "tblAlertas": {"hoja": S_ALERT, "encabezados": ALERT_COLS,
                       "filas": [["" for _ in ALERT_COLS] for _ in range(MAX_PROD)]},
        "tblPedido": {"hoja": S_PED, "encabezados": PEDIDO_COLS,
                      "filas": [["" for _ in PEDIDO_COLS] for _ in range(MAX_PROD)]},
        "tblProveedores": {"hoja": S_PROV, "encabezados": [c.name for c in ctx.tables["tblProveedores"]],
                           "filas": _filas_proveedores(ctx)},
        "tblConteo": {"hoja": S_CONTEO, "encabezados": CONTEO_COLS, "filas": _filas_conteo(prods, datos)},
        "tblConsulta": {"hoja": S_CONS, "encabezados": CONS_COLS, "filas": _filas_consulta(datos)},
        "tblActividad": {"hoja": S_ACT, "encabezados": ACT_COLS,
                         "filas": [[demo2._v(a[c]) for c in ACT_COLS] for a in datos.actividad] or [["" for _ in ACT_COLS]]},
    }
    cfg = {"cfgMargenAlerta": D.MARGEN_ALERTA, "cfgDiasSinRotacion": 60, "cfgFechaMin": dt.date(2020, 1, 1),
           **{k: datos.meta[k] for k in ("stkActualizado", "stkActualizadoPor", "stkActualizadoNombre",
                                         "stkMovimientos")}}
    fx = demo2.fixture(datos, password, fin, cfg, extra)
    for n in ("ctFecha", "ctConfirmar", "ctResultado"):
        fx["nombres"][n] = {"hoja": S_CONTEO, "valor": ""}
    fx["nombres"]["cfgEmpresa"] = {"hoja": S_CONFIG, "valor": D.EMPRESA_DEMO}
    fx["hojas"] = [{"nombre": n, "protegida": n != S_SES} for n in ORDER]
    fx["primera_fila"] = FIRST
    return fx


def _filas_proveedores(ctx: Ctx) -> list[list]:
    cols = [c.name for c in ctx.tables["tblProveedores"]]
    filas = []
    for pv in demo2.proveedores_dict():
        f = {c: "" for c in cols}
        f.update(pv)
        filas.append([f[c] for c in cols])
    filas += [["" for _ in cols] for _ in range(100 - len(filas))]
    return filas


def _filas_conteo(prods: list[dict], datos) -> list[list]:
    """Columnas con fórmula de 13_CONTEO ya evaluadas (el script solo lee SKU, Unidad y Conteo)."""
    filas = []
    for i in range(MAX_PROD):
        pr = prods[i] if i < len(prods) else None
        f = {c: "" for c in CONTEO_COLS}
        if pr:
            f.update({"SKU": pr["SKU"], "Producto": pr["Producto"], "Categoría": pr["Categoría"],
                      "Ubicación": pr["Ubicación"], "Unidad": pr["Unidad"], "Activo": pr["Activo"]})
            v = datos.conteo.get(i + 1)
            if v is not None:
                f["Conteo"] = v
        filas.append([f[c] for c in CONTEO_COLS])
    return filas


def _filas_consulta(datos) -> list[list]:
    cons = demo2.orden_consulta()
    filas = []
    for n in range(1, 21):
        u = cons[n - 1] if n <= len(cons) else None
        f = {c: "" for c in CONS_COLS}
        if u:
            f.update({"Usuario": u[1], "Correo": u[0]})
        prod = datos.consulta.get(n, "")
        if prod:
            f.update({"Producto": prod, "SKU": prod.split(" · ")[0]})
        filas.append([f[c] for c in CONS_COLS])
    return filas


def main():
    ap = argparse.ArgumentParser(description="Genera el libro colaborativo M-INV V2.")
    ap.add_argument("--solo", choices=("core", "release"))
    ap.add_argument("--fin-demo", type=dt.date.fromisoformat, default=dt.date.today(),
                    help="Fecha final de la simulación demo (AAAA-MM-DD). Por defecto: hoy.")
    args = ap.parse_args()
    pwd = os.environ.get("MINV_PASSWORD", DEV_PASSWORD)
    rel_pwd = os.environ.get("MINV_RELEASE_PASSWORD")
    for mode, out in OUT.items():
        if args.solo and args.solo != mode:
            continue
        if mode == "release" and not rel_pwd:
            print("[aviso]   MINV_RELEASE_PASSWORD no definida: el release usa la contraseña de desarrollo.")
        info = build(mode, out, pwd if mode == "core" else (rel_pwd or pwd), args.fin_demo)
        extra = (f"  ({info['entradas']} entradas/ajustes · {info['salidas']} salidas · "
                 f"{info['actividad']} registros de actividad demo)" if mode == "core" else "")
        print(f"[v2/{mode}] {out.relative_to(ROOT)}{extra}")
    if FIXTURE.exists() and (not args.solo or args.solo == "core"):
        print(f"[v2/fixture] {FIXTURE.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
