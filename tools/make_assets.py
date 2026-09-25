#!/usr/bin/env python3
"""
M-INV V1 · Generador de activos gráficos (branding + iconografía).

Produce, a partir de una sola definición, las versiones vectoriales (SVG, fuente
de verdad para V2 web) y rasterizadas (PNG @2x, las que admite Excel) de:

  assets/icons/     iconos de navegación (trazo 2 px sobre rejilla 24x24)
  assets/branding/  marca Z&P y placeholder de logo del cliente (tenant)

Uso:
    .venv\\Scripts\\python tools\\make_assets.py
"""
from __future__ import annotations

import math
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
ICONS_DIR = ROOT / "assets" / "icons"
BRAND_DIR = ROOT / "assets" / "branding"

INK = "#0B1F33"
BRAND = "#1565C0"
BRAND_DARK = "#0D47A1"
BRAND_LIGHT = "#64B5F6"
WHITE = "#FFFFFF"

FONT_DIR = Path("C:/Windows/Fonts")
FONT_FILES = {
    "regular": ["segoeui.ttf", "arial.ttf"],
    "semibold": ["seguisb.ttf", "segoeuib.ttf", "arialbd.ttf"],
    "bold": ["segoeuib.ttf", "arialbd.ttf"],
    "black": ["seguibl.ttf", "segoeuib.ttf", "arialbd.ttf"],
}


def font(weight: str, size: int) -> ImageFont.FreeTypeFont:
    for name in FONT_FILES[weight]:
        path = FONT_DIR / name
        if path.exists():
            return ImageFont.truetype(str(path), size)
    return ImageFont.load_default()


# ---------------------------------------------------------------------------
# Iconografía: primitivas sobre rejilla 24x24 -> SVG exacto + PNG antialias
# ---------------------------------------------------------------------------
# ("line", x1, y1, x2, y2) | ("poly", [(x, y), ...], cerrado) | ("circle", cx, cy, r)
# ("dot", cx, cy, r) relleno | ("arc", cx, cy, r, grados_inicio, grados_fin) horario
ICONS: dict[str, list] = {
    "inicio": [
        ("poly", [(3, 10.5), (12, 3), (21, 10.5)], False),
        ("poly", [(5.5, 9), (5.5, 20.5), (18.5, 20.5), (18.5, 9)], False),
        ("poly", [(10, 20.5), (10, 14.5), (14, 14.5), (14, 20.5)], False),
    ],
    "movimiento": [("circle", 12, 12, 9), ("line", 12, 8, 12, 16), ("line", 8, 12, 16, 12)],
    "stock": [
        ("poly", [(12, 2.8), (20.5, 7.4), (20.5, 16.6), (12, 21.2), (3.5, 16.6), (3.5, 7.4)], True),
        ("poly", [(3.5, 7.4), (12, 12), (20.5, 7.4)], False),
        ("line", 12, 12, 12, 21.2),
    ],
    "alertas": [
        ("poly", [(12, 3.2), (21.6, 19.8), (2.4, 19.8)], True),
        ("line", 12, 9.5, 12, 13.8),
        ("dot", 12, 16.9, 1.15),
    ],
    "catalogo": [
        ("line", 9, 6, 20, 6), ("line", 9, 12, 20, 12), ("line", 9, 18, 20, 18),
        ("dot", 4.5, 6, 1.25), ("dot", 4.5, 12, 1.25), ("dot", 4.5, 18, 1.25),
    ],
    "ayuda": [
        ("circle", 12, 12, 9),
        ("arc", 12, 9.6, 2.6, 180, 450),
        ("line", 12, 12.2, 12, 13.6),
        ("dot", 12, 16.9, 1.15),
    ],
}


def _arc_point(cx, cy, r, deg):
    rad = math.radians(deg)
    return cx + r * math.cos(rad), cy + r * math.sin(rad)


def icon_svg(name: str, prims: list) -> str:
    parts = []
    for p in prims:
        kind = p[0]
        if kind == "line":
            _, x1, y1, x2, y2 = p
            parts.append(f'<line x1="{x1}" y1="{y1}" x2="{x2}" y2="{y2}"/>')
        elif kind == "poly":
            _, pts, closed = p
            tag = "polygon" if closed else "polyline"
            coords = " ".join(f"{x},{y}" for x, y in pts)
            parts.append(f'<{tag} points="{coords}"/>')
        elif kind == "circle":
            _, cx, cy, r = p
            parts.append(f'<circle cx="{cx}" cy="{cy}" r="{r}"/>')
        elif kind == "dot":
            _, cx, cy, r = p
            parts.append(f'<circle cx="{cx}" cy="{cy}" r="{r}" fill="currentColor" stroke="none"/>')
        elif kind == "arc":
            _, cx, cy, r, a0, a1 = p
            x0, y0 = _arc_point(cx, cy, r, a0)
            x1, y1 = _arc_point(cx, cy, r, a1)
            large = 1 if (a1 - a0) % 360 > 180 or (a1 - a0) >= 360 else 0
            parts.append(f'<path d="M{x0:.2f} {y0:.2f} A{r} {r} 0 {large} 1 {x1:.2f} {y1:.2f}"/>')
    body = "\n  ".join(parts)
    return (
        f'<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" '
        f'fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" '
        f'stroke-linejoin="round" role="img" aria-label="{name}">\n  {body}\n</svg>\n'
    )


def icon_png(prims: list, color: str, size_px: int) -> Image.Image:
    scale = 16  # supermuestreo para antialias
    canvas = 24 * scale
    img = Image.new("RGBA", (canvas, canvas), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    w = 2 * scale
    rr = w / 2

    def cap(x, y):
        d.ellipse([x * scale - rr, y * scale - rr, x * scale + rr, y * scale + rr], fill=color)

    for p in prims:
        kind = p[0]
        if kind == "line":
            _, x1, y1, x2, y2 = p
            d.line([(x1 * scale, y1 * scale), (x2 * scale, y2 * scale)], fill=color, width=w)
            cap(x1, y1)
            cap(x2, y2)
        elif kind == "poly":
            _, pts, closed = p
            seq = list(pts) + ([pts[0]] if closed else [])
            d.line([(x * scale, y * scale) for x, y in seq], fill=color, width=w, joint="curve")
            for x, y in pts:
                cap(x, y)
        elif kind == "circle":
            _, cx, cy, r = p
            d.ellipse([(cx - r) * scale - rr, (cy - r) * scale - rr, (cx + r) * scale + rr, (cy + r) * scale + rr],
                      outline=color, width=w)
        elif kind == "dot":
            _, cx, cy, r = p
            d.ellipse([(cx - r) * scale, (cy - r) * scale, (cx + r) * scale, (cy + r) * scale], fill=color)
        elif kind == "arc":
            _, cx, cy, r, a0, a1 = p
            box = [(cx - r) * scale, (cy - r) * scale, (cx + r) * scale, (cy + r) * scale]
            start = a0
            while start < a1:  # tramos <= 90° para evitar ambigüedades de ángulo
                end = min(a1, start + 90)
                d.arc(box, start % 360, end % 360 if end % 360 else 360, fill=color, width=w)
                start = end
            cap(*_arc_point(cx, cy, r, a0))
            cap(*_arc_point(cx, cy, r, a1))
    return img.resize((size_px, size_px), Image.LANCZOS)


# ---------------------------------------------------------------------------
# Branding
# ---------------------------------------------------------------------------
def _gradient(size, top, bottom):
    w, h = size
    base = Image.new("RGBA", size)
    t = tuple(int(top[i:i + 2], 16) for i in (1, 3, 5))
    b = tuple(int(bottom[i:i + 2], 16) for i in (1, 3, 5))
    px = base.load()
    for y in range(h):
        k = y / max(1, h - 1)
        col = tuple(int(t[i] + (b[i] - t[i]) * k) for i in range(3)) + (255,)
        for x in range(w):
            px[x, y] = col
    return base


def zp_mark(size: int) -> Image.Image:
    """Insignia Z&P: cuadrado redondeado con degradado de marca."""
    grad = _gradient((size, size), "#1E88E5", BRAND_DARK)
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, size - 1, size - 1], radius=int(size * 0.22), fill=255)
    mark = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    mark.paste(grad, (0, 0), mask)
    d = ImageDraw.Draw(mark)
    f = font("black", int(size * 0.36))
    d.text((size / 2, size * 0.47), "Z&P", font=f, fill=WHITE, anchor="mm")
    # acento "fast": tres barras de velocidad
    y = int(size * 0.76)
    for i, wfrac in enumerate((0.34, 0.24, 0.14)):
        x0 = int(size * 0.5 - size * wfrac / 2)
        d.rounded_rectangle([x0, y + i * int(size * 0.055), x0 + int(size * wfrac), y + i * int(size * 0.055) + int(size * 0.03)],
                            radius=int(size * 0.015), fill=BRAND_LIGHT)
    return mark


def zp_wordmark(text_color: str, accent: str) -> Image.Image:
    h = 176
    mark = zp_mark(h)
    f1 = font("bold", 58)
    f2 = font("semibold", 30)
    line1, line2 = "Z&P Software", "F A S T   S O L U T I O N S"
    tmp = ImageDraw.Draw(Image.new("RGBA", (1, 1)))
    w1 = tmp.textbbox((0, 0), line1, font=f1)[2]
    w2 = tmp.textbbox((0, 0), line2, font=f2)[2]
    width = h + 36 + max(w1, w2) + 12
    img = Image.new("RGBA", (width, h), (0, 0, 0, 0))
    img.paste(mark, (0, 0), mark)
    d = ImageDraw.Draw(img)
    d.text((h + 36, h * 0.40), line1, font=f1, fill=text_color, anchor="lm")
    d.text((h + 38, h * 0.74), line2, font=f2, fill=accent, anchor="lm")
    return img


def tenant_placeholder() -> Image.Image:
    w, h = 480, 144
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    line = (255, 255, 255, 150)
    r, inset, dash, gap, lw = 22, 3, 18, 12, 4
    # esquinas sólidas + lados discontinuos (estilo "placeholder")
    d.arc([inset, inset, inset + 2 * r, inset + 2 * r], 180, 270, fill=line, width=lw)
    d.arc([w - inset - 2 * r, inset, w - inset, inset + 2 * r], 270, 360, fill=line, width=lw)
    d.arc([inset, h - inset - 2 * r, inset + 2 * r, h - inset], 90, 180, fill=line, width=lw)
    d.arc([w - inset - 2 * r, h - inset - 2 * r, w - inset, h - inset], 0, 90, fill=line, width=lw)

    def dashed(x0, y0, x1, y1):
        length = math.hypot(x1 - x0, y1 - y0)
        ux, uy = (x1 - x0) / length, (y1 - y0) / length
        pos = 0.0
        while pos < length:
            end = min(length, pos + dash)
            d.line([(x0 + ux * pos, y0 + uy * pos), (x0 + ux * end, y0 + uy * end)], fill=line, width=lw)
            pos += dash + gap

    dashed(inset + r, inset + 1, w - inset - r, inset + 1)
    dashed(inset + r, h - inset - 2, w - inset - r, h - inset - 2)
    dashed(inset + 1, inset + r, inset + 1, h - inset - r)
    dashed(w - inset - 2, inset + r, w - inset - 2, h - inset - r)
    d.text((w / 2, h * 0.43), "SU LOGO AQUÍ", font=font("semibold", 40), fill=(255, 255, 255, 225), anchor="mm")
    d.text((w / 2, h * 0.74), "PNG transparente · 480 × 144 px", font=font("regular", 20),
           fill=(255, 255, 255, 150), anchor="mm")
    return img


SVG_MARK = """<svg xmlns="http://www.w3.org/2000/svg" width="176" height="176" viewBox="0 0 176 176" role="img" aria-label="Z&amp;P Software Fast Solutions">
  <defs>
    <linearGradient id="g" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="#1E88E5"/>
      <stop offset="1" stop-color="#0D47A1"/>
    </linearGradient>
  </defs>
  <rect width="176" height="176" rx="38.7" fill="url(#g)"/>
  <text x="88" y="84" text-anchor="middle" dominant-baseline="middle" font-family="Segoe UI Black, Segoe UI, Arial, sans-serif" font-weight="900" font-size="63" fill="#FFFFFF">Z&amp;P</text>
  <rect x="58.1" y="133.8" width="59.8" height="5.3" rx="2.6" fill="#64B5F6"/>
  <rect x="66.9" y="143.4" width="42.2" height="5.3" rx="2.6" fill="#64B5F6"/>
  <rect x="75.7" y="153.1" width="24.6" height="5.3" rx="2.6" fill="#64B5F6"/>
</svg>
"""

SVG_WORDMARK = """<svg xmlns="http://www.w3.org/2000/svg" width="620" height="176" viewBox="0 0 620 176" role="img" aria-label="Z&amp;P Software Fast Solutions">
  <!-- Variante para fondo claro. Para fondo oscuro cambie #0B1F33 por #FFFFFF y #1565C0 por #64B5F6. -->
  <defs>
    <linearGradient id="g" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="#1E88E5"/>
      <stop offset="1" stop-color="#0D47A1"/>
    </linearGradient>
  </defs>
  <rect width="176" height="176" rx="38.7" fill="url(#g)"/>
  <text x="88" y="84" text-anchor="middle" dominant-baseline="middle" font-family="Segoe UI Black, Segoe UI, Arial, sans-serif" font-weight="900" font-size="63" fill="#FFFFFF">Z&amp;P</text>
  <rect x="58.1" y="133.8" width="59.8" height="5.3" rx="2.6" fill="#64B5F6"/>
  <rect x="66.9" y="143.4" width="42.2" height="5.3" rx="2.6" fill="#64B5F6"/>
  <rect x="75.7" y="153.1" width="24.6" height="5.3" rx="2.6" fill="#64B5F6"/>
  <text x="212" y="72" dominant-baseline="middle" font-family="Segoe UI, Arial, sans-serif" font-weight="700" font-size="58" fill="#0B1F33">Z&amp;P Software</text>
  <text x="214" y="130" dominant-baseline="middle" font-family="Segoe UI Semibold, Segoe UI, Arial, sans-serif" font-weight="600" font-size="30" letter-spacing="6" fill="#1565C0">FAST SOLUTIONS</text>
</svg>
"""

SVG_TENANT = """<svg xmlns="http://www.w3.org/2000/svg" width="480" height="144" viewBox="0 0 480 144" role="img" aria-label="Espacio para el logo del cliente">
  <!-- Placeholder del logo del tenant. Reemplace por el logo real (PNG transparente 480x144). -->
  <rect x="3" y="3" width="474" height="138" rx="22" fill="none" stroke="#FFFFFF" stroke-opacity="0.6" stroke-width="4" stroke-dasharray="18 12"/>
  <text x="240" y="62" text-anchor="middle" dominant-baseline="middle" font-family="Segoe UI Semibold, Segoe UI, Arial, sans-serif" font-size="40" fill="#FFFFFF" fill-opacity="0.9">SU LOGO AQUÍ</text>
  <text x="240" y="107" text-anchor="middle" dominant-baseline="middle" font-family="Segoe UI, Arial, sans-serif" font-size="20" fill="#FFFFFF" fill-opacity="0.6">PNG transparente · 480 × 144 px</text>
</svg>
"""


def main() -> None:
    ICONS_DIR.mkdir(parents=True, exist_ok=True)
    BRAND_DIR.mkdir(parents=True, exist_ok=True)
    for name, prims in ICONS.items():
        (ICONS_DIR / f"{name}.svg").write_text(icon_svg(name, prims), encoding="utf-8")
        icon_png(prims, WHITE, 64).save(ICONS_DIR / f"{name}-blanco@2x.png")
        icon_png(prims, INK, 64).save(ICONS_DIR / f"{name}-tinta@2x.png")
    zp_mark(176).save(BRAND_DIR / "zp-insignia@2x.png")
    zp_wordmark(INK, BRAND).save(BRAND_DIR / "zp-logo-oscuro@2x.png")
    zp_wordmark(WHITE, BRAND_LIGHT).save(BRAND_DIR / "zp-logo-claro@2x.png")
    tenant_placeholder().save(BRAND_DIR / "tenant-logo-placeholder@2x.png")
    (BRAND_DIR / "zp-insignia.svg").write_text(SVG_MARK, encoding="utf-8")
    (BRAND_DIR / "zp-logo.svg").write_text(SVG_WORDMARK, encoding="utf-8")
    (BRAND_DIR / "tenant-logo-placeholder.svg").write_text(SVG_TENANT, encoding="utf-8")
    print(f"Activos generados en {ICONS_DIR.relative_to(ROOT)} y {BRAND_DIR.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
