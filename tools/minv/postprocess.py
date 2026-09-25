"""
M-INV · Post-proceso del paquete OOXML (lo que XlsxWriter no expone):
  - Esquinas redondeadas, sombras y márgenes internos de las formas según su etiqueta [tag].
  - Orden de capas: marcos [bg] al fondo (detrás de gráficos); iconos [icon] al frente.
  - Macros asignadas a botones: etiqueta [tag|Macro] → atributo macro="[0]!Macro" (edición Plus).
  - Protección de la estructura del libro (Release).
"""
from __future__ import annotations

import re
import zipfile
from pathlib import Path

from .base import legacy_hash

ANCHOR_RE = re.compile(r"<xdr:(twoCellAnchor|oneCellAnchor|absoluteAnchor)\b.*?</xdr:\1>", re.S)
TAG_RE = re.compile(r'descr="\[(\w+)(?:\|(\w+))?\]\s*([^"]*)"')
RADIUS = {"btn": 22000, "tile": 9000, "card": 6500, "bg": 4000, "pill": 50000, "chip": 50000, "bar": 50000,
          "banner": 8000}
SHADOW = {
    "soft": '<a:effectLst><a:outerShdw blurRad="63500" dist="12700" dir="5400000" algn="t" rotWithShape="0">'
            '<a:srgbClr val="0B1F33"><a:alpha val="14000"/></a:srgbClr></a:outerShdw></a:effectLst>',
    "lift": '<a:effectLst><a:outerShdw blurRad="50800" dist="19050" dir="5400000" algn="t" rotWithShape="0">'
            '<a:srgbClr val="0B1F33"><a:alpha val="26000"/></a:srgbClr></a:outerShdw></a:effectLst>',
}
PX = 9525  # EMU por píxel
INSETS = {"txt": (0, 0, 0, 0), "pill": (26 * PX, 0, 8 * PX, 0), "chip": (8 * PX, 0, 8 * PX, 0),
          "tile": (8 * PX, 5 * PX, 8 * PX, 12 * PX), "btn": (8 * PX, 0, 8 * PX, 0),
          "banner": (12 * PX, 4 * PX, 12 * PX, 4 * PX)}


def _style_anchor(a: str) -> tuple[str, str | None]:
    m = TAG_RE.search(a)
    if not m:
        return a, None
    tag, macro, desc = m.group(1), m.group(2), m.group(3)
    a = a.replace(m.group(0), f'descr="{desc}"', 1)
    if tag in RADIUS:
        a = a.replace('<a:prstGeom prst="rect"><a:avLst/></a:prstGeom>',
                      f'<a:prstGeom prst="roundRect"><a:avLst><a:gd name="adj" fmla="val {RADIUS[tag]}"/></a:avLst>'
                      f'</a:prstGeom>', 1)
    if tag in ("card", "bg"):
        a = re.sub(r"(</a:ln>)(</xdr:spPr>)", lambda mm: mm.group(1) + SHADOW["soft"] + mm.group(2), a, count=1)
    elif tag in ("tile", "btn"):
        a = re.sub(r"(</a:ln>)(</xdr:spPr>)", lambda mm: mm.group(1) + SHADOW["lift"] + mm.group(2), a, count=1)
    if tag in INSETS:
        l, t, r, b = INSETS[tag]
        a = a.replace("<a:bodyPr ", f'<a:bodyPr lIns="{l}" tIns="{t}" rIns="{r}" bIns="{b}" ', 1)
    if macro:
        a = a.replace('<xdr:sp macro=""', f'<xdr:sp macro="[0]!{macro}"', 1)
    return a, tag


def _fix_drawing(xml: str) -> str:
    anchors = list(ANCHOR_RE.finditer(xml))
    if not anchors:
        return xml
    head, tail = xml[:anchors[0].start()], xml[anchors[-1].end():]
    back, middle, front = [], [], []
    for m in anchors:
        a, tag = _style_anchor(m.group(0))
        (back if tag == "bg" else front if tag == "icon" else middle).append(a)
    return head + "".join(back + middle + front) + tail


RULE_RE = re.compile(r"<(cfRule|dataValidation)\b.*?</\1>", re.S)
TABLE_REF_RE = re.compile(r"\btbl\w+\[")


def _guard_rules(part: str, xml: str):
    """Excel rechaza el libro completo si un formato condicional o una validación usa referencias a tablas."""
    for m in RULE_RE.finditer(xml):
        if TABLE_REF_RE.search(m.group(0)):
            raise ValueError(f"{part}: referencia estructurada en formato condicional/validación "
                             f"(use un nombre definido): {m.group(0)[:160]}")


FORMULA_RE = re.compile(r'<c r="([A-Z]+\d+)"[^>]*><f[^>]*>(.*?)</f>', re.S)
STRING_RE = re.compile(r'"(?:[^"]|"")*"')


def _guard_formulas(part: str, xml: str):
    """Una fórmula con llaves o paréntesis desbalanceados hace que Excel rechace el libro completo."""
    for ref, f in FORMULA_RE.findall(xml):
        f = STRING_RE.sub('""', f.replace("&quot;", '"'))
        if f.count("(") != f.count(")") or f.count("{") != f.count("}"):
            raise ValueError(f"{part} {ref}: fórmula con paréntesis o llaves desbalanceados: {f[:160]}")


def postprocess(src: Path, dst: Path, lock_password: str | None):
    with zipfile.ZipFile(src) as zin, zipfile.ZipFile(dst, "w", zipfile.ZIP_DEFLATED) as zout:
        for item in zin.infolist():
            data = zin.read(item.filename)
            if item.filename.startswith("xl/worksheets/sheet"):
                _guard_rules(item.filename, data.decode("utf-8"))
                _guard_formulas(item.filename, data.decode("utf-8"))
            if item.filename.startswith("xl/drawings/drawing") and item.filename.endswith(".xml"):
                data = _fix_drawing(data.decode("utf-8")).encode("utf-8")
            elif item.filename == "xl/workbook.xml" and lock_password:
                xml = data.decode("utf-8")
                prot = f'<workbookProtection workbookPassword="{legacy_hash(lock_password)}" lockStructure="1"/>'
                xml = xml.replace("<bookViews>", prot + "<bookViews>", 1)
                data = xml.encode("utf-8")
            zout.writestr(item, data)
    src.unlink()
