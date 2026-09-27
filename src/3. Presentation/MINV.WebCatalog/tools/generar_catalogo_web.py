r"""Genera el mock del catálogo web (V5) a partir del catálogo de tecnología de la V4.2.

Lee  src/2. Infrastructure/MINV.Infrastructure/Seeding/Tecnologia/catalogo-tecnologia.json
Escribe  src/3-infrastructure/data/{catalog,categories,brands,presets}.data.ts
Copia    los dibujos propios de los productos a public/images/products/

Uso (desde MINV.WebCatalog):  python tools\generar_catalogo_web.py

Es determinista (semilla fija): el mismo JSON produce el mismo TypeScript. No toca nada fuera de MINV.WebCatalog.
"""
from __future__ import annotations

import json
import random
import re
import shutil
import unicodedata
from pathlib import Path

AQUI = Path(__file__).resolve().parent
RAIZ_WEB = AQUI.parent
REPO = RAIZ_WEB.parents[2]
ORIGEN = REPO / "src" / "2. Infrastructure" / "MINV.Infrastructure" / "Seeding" / "Tecnologia"
JSON_CATALOGO = ORIGEN / "catalogo-tecnologia.json"
IMAGENES = ORIGEN / "Imagenes"
DESTINO_DATOS = RAIZ_WEB / "src" / "3-infrastructure" / "data"
DESTINO_IMAGENES = RAIZ_WEB / "public" / "images" / "products"

SEMILLA = 2026
IVA = 0.13

# Ícono de Lucide por categoría (nombre del componente en lucide-react).
ICONOS = {
    "COMP": "Cpu", "CPU": "Cpu", "GPU": "Gpu", "MB": "CircuitBoard", "RAM": "MemoryStick", "STO": "HardDrive",
    "PSU": "Zap", "CASE": "Box", "COOL": "Fan", "PC": "MonitorSmartphone", "LAPG": "Laptop", "LAPU": "Laptop",
    "DESK": "PcCase", "MON": "Monitor", "PER": "Keyboard", "KEY": "Keyboard", "MOU": "Mouse", "AUD": "Headphones",
    "PAD": "Square", "CAM": "Webcam", "CHA": "Armchair", "CON": "Gamepad2", "CPS": "Gamepad2", "CXB": "Gamepad2",
    "CNS": "Gamepad2", "JUE": "Disc3", "ACC": "Plug", "MAND": "Gamepad", "CARG": "BatteryCharging", "ALMC": "HardDrive",
    "RED": "Router", "CAB": "Cable", "SOFT": "Package", "LIC": "KeyRound", "SRV": "Wrench",
}

DESCRIPCION_RAIZ = {
    "COMP": "Componentes para armar o actualizar tu PC",
    "PC": "Computadoras de escritorio y portátiles listas para usar",
    "MON": "Monitores gaming y profesionales",
    "PER": "Teclados, mouse, audio y todo lo que va en tu escritorio",
    "CON": "PlayStation, Xbox y Nintendo",
    "JUE": "Videojuegos físicos y digitales",
    "ACC": "Mandos, cargadores y almacenamiento para consola",
    "RED": "Routers y conectividad",
    "CAB": "Cables y adaptadores",
    "SOFT": "Licencias y servicios técnicos",
}

# Ranura del armador por cada «ranura» de los armados del JSON.
RANURAS = {
    "cpu": "cpu", "placa": "motherboard", "ram": "ram", "gpu": "gpu", "almacenamiento": "storage", "fuente": "psu",
    "gabinete": "case", "refrigeracion": "cooler", "monitor": "monitor", "perifericos": "peripherals",
    "teclado": "peripherals", "mouse": "peripherals", "audifonos": "peripherals", "audio": "peripherals",
    "sistema_operativo": "software", "software": "software", "servicio": "software",
}
PERFILES = {"entrada": "entrada", "media": "media", "alta": "alta", "entusiasta": "entusiasta", "oficina": "oficina",
            "creacion": "creador", "streaming": "creador"}


def slug(texto: str) -> str:
    plano = unicodedata.normalize("NFKD", texto).encode("ascii", "ignore").decode("ascii").lower()
    plano = re.sub(r"[^a-z0-9]+", "-", plano).strip("-")
    return plano


def nombre_corto(nombre: str) -> str:
    """Quita el paréntesis de especificaciones: «Procesador AMD Ryzen 5 7600 (AM5, …)» → «Procesador AMD Ryzen 5 7600»."""
    corto = re.sub(r"\s*\([^)]*\)\s*$", "", nombre).strip()
    return corto if len(corto) >= 8 else nombre


def redondear(valor: float) -> float:
    return float(f"{valor:.2f}")


def ts(valor) -> str:
    return json.dumps(valor, ensure_ascii=False, indent=2)


def formatear_valor(valor, tipo: str, unidad: str | None) -> str:
    if isinstance(valor, list):
        return ", ".join(str(v) for v in valor)
    if isinstance(valor, bool):
        return "Sí" if valor else "No"
    if isinstance(valor, float) and valor.is_integer():
        valor = int(valor)
    texto = str(valor).replace(".", ",") if isinstance(valor, float) else str(valor)
    return f"{texto} {unidad}" if unidad else texto


def main() -> None:
    datos = json.loads(JSON_CATALOGO.read_text(encoding="utf-8"))
    rnd = random.Random(SEMILLA)

    categorias = datos["categorias"]
    padre_de = {c["codigo"]: c["padre"] for c in categorias}
    nombre_cat = {c["codigo"]: c["nombre"] for c in categorias}

    # Definiciones de especificaciones por categoría, heredadas de la madre (T-01).
    defs_por_cat: dict[str, list[dict]] = {}
    for e in datos["especificaciones"]:
        defs_por_cat.setdefault(e["categoria"], []).append(e)

    def definiciones(cat: str) -> list[dict]:
        cadena = []
        actual = cat
        while actual:
            cadena.append(actual)
            actual = padre_de.get(actual)
        propias: list[dict] = []
        for c in reversed(cadena):   # la madre primero (condición), luego lo específico
            propias.extend(sorted(defs_por_cat.get(c, []), key=lambda d: d["orden"]))
        return propias

    # ---- categorías
    cats_ts = []
    for c in categorias:
        cats_ts.append({
            "code": c["codigo"],
            "name": c["nombre"],
            "slug": slug(c["nombre"]),
            "parent": c["padre"],
            "icon": ICONOS.get(c["codigo"], "Tag"),
            "description": DESCRIPCION_RAIZ.get(c["codigo"], c["ruta"]),
            "productCount": c["productos"],
        })

    # ---- marcas
    marcas_ts = [{"code": m["codigo"], "name": m["nombre"], "productCount": m["productos"]} for m in datos["marcas"]]

    # ---- productos
    productos_ts = []
    for p in sorted(datos["productos"], key=lambda x: x["sku"]):
        defs = definiciones(p["categoria"])
        valores = p.get("especificaciones", {})
        specs = []
        for d in defs:
            if d["codigo"] not in valores:
                continue
            v = valores[d["codigo"]]
            specs.append({
                "key": d["codigo"],
                "label": d["nombre"],
                "value": v,
                "text": formatear_valor(v, d["tipo"], d.get("unidad")),
                "unit": d.get("unidad"),
                "filterable": bool(d.get("filtrable")),
            })
        condicion = valores.get("condicion", "Nuevo")
        popularidad = int(p.get("popularidad", 5))
        precio = float(p["precio"])
        # Ofertas: algunos productos populares llevan precio de lista tachado (solo presentación).
        en_oferta = popularidad >= 6 and rnd.random() < 0.28
        precio_lista = redondear(precio * rnd.choice([1.08, 1.12, 1.15, 1.2])) if en_oferta else None
        # Existencias de presentación: reproducibles; unos pocos agotados.
        maximo = int(p.get("maximo", 10))
        stock = 0 if rnd.random() < 0.07 else rnd.randint(1, max(1, maximo))
        tags = []
        if popularidad >= 8:
            tags.append("destacado")
        if en_oferta:
            tags.append("oferta")
        if condicion == "Nuevo" and rnd.random() < 0.18:
            tags.append("nuevo")
        destacadas = [s for s in specs if s["filterable"] and s["key"] != "condicion"][:4]
        highlights = [f"{s['label']}: {s['text']}" for s in destacadas[:3]]
        ruta = p.get("ruta_categoria", nombre_cat.get(p["categoria"], ""))
        garantia = int(p.get("garantia_meses", 0))
        descripcion = (
            f"{nombre_corto(p['nombre'])} de {p['marca']}, en la categoría {ruta}. "
            + (" ".join(f"{s['label'].lower()} {s['text']}," for s in destacadas[:2]).rstrip(",").capitalize() + ". " if destacadas else "")
            + (f"Garantía oficial de {garantia} meses. " if garantia else "")
            + ("Se entrega con su número de serie registrado para garantía y trazabilidad. " if p.get("lleva_serie") else "")
            + f"Condición: {condicion.lower()}."
        )
        productos_ts.append({
            "sku": p["sku"],
            "slug": slug(p["sku"]),
            "name": p["nombre"],
            "shortName": nombre_corto(p["nombre"]),
            "category": p["categoria"],
            "categoryName": nombre_cat.get(p["categoria"], p["categoria"]),
            "categoryPath": ruta,
            "brand": p["marca"],
            "price": precio,
            "listPrice": precio_lista,
            "image": f"/images/products/{p['imagen']}.png",
            "stock": stock,
            "condition": condicion,
            "warrantyMonths": garantia,
            "serialized": bool(p.get("lleva_serie")),
            "popularity": popularidad,
            "tags": tags,
            "description": descripcion,
            "highlights": highlights,
            "specs": specs,
        })

    categoria_de = {p["sku"]: p["categoria"] for p in datos["productos"]}

    def ranura_por_categoria(cat: str) -> str:
        """Las líneas «extra» de un armado se ubican por la categoría del producto."""
        if cat in ("MON",):
            return "monitor"
        if cat in ("LIC", "SRV", "SOFT"):
            return "software"
        if cat in ("STO", "ALMC"):
            return "storage"
        return "peripherals"

    # ---- armados sugeridos (los de prueba de incompatibilidad no se publican)
    presets_ts = []
    ranuras_vistas = set()
    for a in datos["armados"]:
        if a["nombre"].upper().startswith("PRUEBA"):
            continue
        lineas = []
        for l in a["lineas"]:
            ranuras_vistas.add(l["ranura"])
            slot = RANURAS.get(l["ranura"]) or ranura_por_categoria(categoria_de.get(l["sku"], ""))
            lineas.append({"slot": slot, "sku": l["sku"], "quantity": int(l.get("cantidad", 1))})
        presets_ts.append({
            "id": slug(a["numero"]),
            "name": a["nombre"],
            "tier": PERFILES.get(a.get("perfil", ""), "media"),
            "lines": lineas,
        })

    DESTINO_DATOS.mkdir(parents=True, exist_ok=True)
    cabecera = "// Archivo GENERADO por tools/generar_catalogo_web.py a partir del catálogo de tecnología de la V4.2. No editar a mano.\n"
    (DESTINO_DATOS / "categories.data.ts").write_text(
        cabecera + "import type { Category } from '@/1-domain/catalog/types';\n\nexport const CATEGORIES: Category[] = " + ts(cats_ts) + ";\n",
        encoding="utf-8")
    (DESTINO_DATOS / "brands.data.ts").write_text(
        cabecera + "import type { Brand } from '@/1-domain/catalog/types';\n\nexport const BRANDS: Brand[] = " + ts(marcas_ts) + ";\n",
        encoding="utf-8")
    (DESTINO_DATOS / "catalog.data.ts").write_text(
        cabecera + "import type { Product } from '@/1-domain/catalog/types';\n\nexport const PRODUCTS: Product[] = " + ts(productos_ts) + ";\n",
        encoding="utf-8")
    (DESTINO_DATOS / "presets.data.ts").write_text(
        cabecera + "import type { BuildPreset } from '@/1-domain/builder/types';\n\nexport const PRESETS: BuildPreset[] = " + ts(presets_ts) + ";\n",
        encoding="utf-8")

    DESTINO_IMAGENES.mkdir(parents=True, exist_ok=True)
    copiadas = 0
    for png in IMAGENES.glob("*.png"):
        shutil.copyfile(png, DESTINO_IMAGENES / png.name)
        copiadas += 1

    ofertas = sum(1 for p in productos_ts if "oferta" in p["tags"])
    print(f"Catálogo web: {len(productos_ts)} productos, {len(cats_ts)} categorías, {len(marcas_ts)} marcas, "
          f"{len(presets_ts)} armados sugeridos, {ofertas} ofertas, {sum(1 for p in productos_ts if p['stock'] == 0)} agotados, "
          f"{copiadas} imágenes. Ranuras: {sorted(ranuras_vistas)}")


if __name__ == "__main__":
    main()
