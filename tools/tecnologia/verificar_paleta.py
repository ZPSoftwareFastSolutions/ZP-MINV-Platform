r"""Verifica las paletas gaming de la V4.2 y dibuja Theme/muestra-paleta.png.

1. Mismas claves, mismo orden y mismo tipo de recurso que las paletas originales del cliente de escritorio.
2. Contraste WCAG 2.x de los pares que usa la interfaz (texto ≥ 4,5; componentes y bordes de foco ≥ 3).
3. Muestra visual de ambos temas: maqueta mínima (barra lateral, tarjeta, botones, chips de estado, gráfico, degradado)
   y una ficha por clave con su color.

Uso: .venv\Scripts\python verificar_paleta.py <carpeta Theme original> <carpeta Theme nueva>
"""
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

XAML = "{http://schemas.microsoft.com/winfx/2006/xaml}"


def leer(ruta):
    raiz = ET.parse(ruta).getroot()
    res = {}
    for el in raiz:
        clave = el.get(XAML + "Key")
        tipo = el.tag.split("}")[1]
        if tipo == "SolidColorBrush":
            res[clave] = (tipo, [el.get("Color")])
        elif tipo == "Color":
            res[clave] = (tipo, [el.text.strip()])
        elif tipo == "LinearGradientBrush":
            res[clave] = (tipo, [(s.get("Color"), float(s.get("Offset"))) for s in el])
    return res


def rgb(h):
    h = h.lstrip("#")
    if len(h) == 8:
        h = h[2:]
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def luminancia(h):
    def canal(v):
        v /= 255
        return v / 12.92 if v <= 0.03928 else ((v + 0.055) / 1.055) ** 2.4
    r, g, b = (canal(v) for v in rgb(h))
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def contraste(a, b):
    la, lb = sorted((luminancia(a), luminancia(b)), reverse=True)
    return (la + 0.05) / (lb + 0.05)


def pares(p):
    c = lambda k: p[k][1][0]
    g = p["BrandGradient"][1]
    lista = []
    for fondo in ("Canvas", "Surface", "SurfaceAlt", "SurfaceHover", "InputBackground"):
        for texto in ("TextPrimary", "TextSecondary", "TextMuted"):
            lista.append((texto, fondo, c(texto), c(fondo), 4.5))
    lista.append(("TextFaint", "Surface", c("TextFaint"), c("Surface"), 2.5))
    for fondo in ("Brand", "BrandHover", "BrandPressed", "SidebarSelected"):
        lista.append(("OnBrand", fondo, c("OnBrand"), c(fondo), 4.5))
    for fondo in ("BrandSoft", "BrandSoftHover", "Surface"):
        lista.append(("BrandText", fondo, c("BrandText"), c(fondo), 4.5))
    lista += [("Brand (foco/casilla)", "Surface", c("Brand"), c("Surface"), 3.0),
              ("Brand (ícono KPI)", "BrandSoft", c("Brand"), c("BrandSoft"), 3.0),
              ("SidebarText", "Sidebar", c("SidebarText"), c("Sidebar"), 4.5),
              ("SidebarText", "SidebarHover", c("SidebarText"), c("SidebarHover"), 4.5),
              ("SidebarMuted", "Sidebar", c("SidebarMuted"), c("Sidebar"), 4.5),
              ("#FFFFFF (menú)", "SidebarSelected", "#FFFFFF", c("SidebarSelected"), 4.5),
              ("#FFFFFF", "BloodRed", "#FFFFFF", c("BloodRed"), 4.5)]
    for s in ("Success", "Warning", "Danger", "Info"):
        lista.append((s, s + "Soft", c(s), c(s + "Soft"), 4.5))
        lista.append((s, "Surface", c(s), c("Surface"), 4.5))
    for s in ("OutOfStock", "Critical", "Low", "Optimal", "Overstock", "Inconsistent", "Inactive"):
        lista.append(("Status" + s, "Status" + s + "Soft", c("Status" + s), c("Status" + s + "Soft"), 4.5))
        lista.append(("Status" + s, "Surface", c("Status" + s), c("Surface"), 4.5))
    for k in ("ChartEntries", "ChartIssues"):
        lista.append((k, "Surface", c(k), c("Surface"), 3.0))
    for color, off in g:
        lista.append(("#FFFFFF (login)", f"BrandGradient@{off:g}", "#FFFFFF", color, 4.5 if off < 1 else 3.0))
    lista.append(("#FFFFFF (DangerButton hoy)", "Danger", "#FFFFFF", c("Danger"), 4.5))
    lista.append(("Canvas (DangerButton propuesto)", "Danger", c("Canvas"), c("Danger"), 4.5))
    lista.append(("Canvas (DangerButton propuesto)", "DangerHover", c("Canvas"), c("DangerHover"), 4.5))
    return lista


def fuente(t, negrita=False):
    try:
        return ImageFont.truetype("segoeuib.ttf" if negrita else "segoeui.ttf", t)
    except OSError:
        return ImageFont.load_default()


def maqueta(p, ancho):
    c = lambda k: p[k][1][0]
    im = Image.new("RGB", (ancho, 330), c("Canvas"))
    d = ImageDraw.Draw(im)
    f, fb, fs = fuente(13), fuente(13, True), fuente(11)
    # barra lateral
    d.rectangle((0, 0, 170, 330), fill=c("Sidebar"))
    d.line((170, 0, 170, 330), fill=c("SidebarBorder"), width=1)
    d.text((16, 16), "M-INV", font=fuente(17, True), fill="#FFFFFF")
    d.text((16, 38), "Tech · Gaming", font=fs, fill=c("SidebarMuted"))
    for i, (t, estado) in enumerate((("Tablero", "sel"), ("Punto de venta", "hover"), ("Armador de PC", ""),
                                     ("Series e IMEI", ""), ("Garantías y RMA", ""))):
        y = 72 + i * 40
        if estado == "sel":
            d.rounded_rectangle((10, y, 160, y + 32), 8, fill=c("SidebarSelected"))
        elif estado == "hover":
            d.rounded_rectangle((10, y, 160, y + 32), 8, fill=c("SidebarHover"))
        d.text((24, y + 8), t, font=f, fill="#FFFFFF" if estado else c("SidebarText"))
    # tarjeta
    x0 = 186
    d.rounded_rectangle((x0, 14, ancho - 14, 176), 12, fill=c("Surface"), outline=c("Border"))
    d.text((x0 + 16, 26), "RTX 4070 SUPER 12 GB", font=fuente(15, True), fill=c("TextPrimary"))
    d.text((x0 + 16, 50), "Tarjeta de video · garantía 36 meses", font=f, fill=c("TextSecondary"))
    d.text((x0 + 16, 70), "Serie GPU-7F3K-2291 · Plataforma PC", font=f, fill=c("TextMuted"))
    d.text((x0 + 16, 90), "Buscar por especificación…", font=f, fill=c("TextFaint"))
    for i, (t, k) in enumerate((("Guardar", "Brand"), ("Cotizar", "BrandSoft"), ("Anular", "Danger"))):
        bx = x0 + 16 + i * 104
        d.rounded_rectangle((bx, 120, bx + 94, 156), 8, fill=c(k))
        color = c("OnBrand") if k == "Brand" else c("BrandText") if k == "BrandSoft" else "#FFFFFF"
        d.text((bx + 47, 138), t, font=fb, fill=color, anchor="mm")
    d.text((x0 + 16 + 3 * 104 + 4, 138), "blanco", font=fs, fill=c("TextMuted"), anchor="lm")
    # chips de estado
    y = 190
    chips = (("AGOTADO", "StatusOutOfStock"), ("CRÍTICO", "StatusCritical"), ("BAJO", "StatusLow"),
             ("ÓPTIMO", "StatusOptimal"), ("SOBRESTOCK", "StatusOverstock"), ("INCONSISTENTE", "StatusInconsistent"),
             ("INACTIVO", "StatusInactive"), ("Éxito", "Success"), ("Aviso", "Warning"), ("Error", "Danger"), ("Info", "Info"))
    x = x0
    for t, k in chips:
        w = d.textlength(t, font=fs) + 16
        if x + w > ancho - 14:
            x, y = x0, y + 26
        d.rounded_rectangle((x, y, x + w, y + 20), 10, fill=c(k + "Soft"))
        d.text((x + w / 2, y + 10), t, font=fs, fill=c(k), anchor="mm")
        x += w + 6
    # gráfico y degradado
    gy = 262
    d.rounded_rectangle((x0, gy, x0 + 200, 318), 10, fill=c("Surface"), outline=c("Border"))
    for i in range(8):
        d.line((x0 + 10, gy + 10 + i * 6, x0 + 190, gy + 10 + i * 6), fill=c("ChartGrid"))
    for i, (a, b) in enumerate(((30, 18), (40, 26), (22, 34), (44, 20), (36, 30), (28, 40))):
        bx = x0 + 16 + i * 30
        d.rectangle((bx, 312 - a, bx + 10, 312), fill=c("ChartEntries"))
        d.rectangle((bx + 12, 312 - b, bx + 22, 312), fill=c("ChartIssues"))
    g = p["BrandGradient"][1]
    gx0, gx1 = x0 + 212, ancho - 14
    ancho_g = gx1 - gx0
    banda = Image.new("RGB", (ancho_g, 56))
    bd = banda.load()
    for xx in range(ancho_g):
        for yy in range(56):
            t = (xx / ancho_g + yy / 56) / 2
            for (ca, oa), (cb, ob) in zip(g, g[1:]):
                if oa <= t <= ob:
                    f_ = (t - oa) / (ob - oa)
                    bd[xx, yy] = tuple(int(u + (v - u) * f_) for u, v in zip(rgb(ca), rgb(cb)))
                    break
    mascara = Image.new("L", banda.size, 0)
    ImageDraw.Draw(mascara).rounded_rectangle((0, 0, ancho_g - 1, 55), 10, fill=255)
    im.paste(banda, (gx0, gy), mascara)
    d.text((gx0 + 14, gy + 16), "Su tienda gamer, al día.", font=fb, fill="#FFFFFF")
    d.text((gx0 + 14, gy + 34), "BrandGradient", font=fs, fill="#E9E2FF")
    return im


def fichas(p, ancho):
    claves = list(p.keys())
    col = 2
    filas = (len(claves) + col - 1) // col
    alto_f = 23
    im = Image.new("RGB", (ancho, filas * alto_f + 16), "#FFFFFF")
    d = ImageDraw.Draw(im)
    f, fm = fuente(12), fuente(11)
    sup = p["Surface"][1][0]
    for i, k in enumerate(claves):
        x = 10 + (i // filas) * (ancho // col)
        y = 8 + (i % filas) * alto_f
        tipo, vals = p[k]
        if tipo == "LinearGradientBrush":
            for j in range(40):
                t = j / 39
                for (ca, oa), (cb, ob) in zip(vals, vals[1:]):
                    if oa <= t <= ob:
                        f_ = (t - oa) / (ob - oa)
                        d.line((x + j, y, x + j, y + 18), fill=tuple(int(u + (v - u) * f_) for u, v in zip(rgb(ca), rgb(cb))))
                        break
            valor = " → ".join(cv for cv, _ in vals)
        else:
            hexa = vals[0]
            d.rounded_rectangle((x, y, x + 40, y + 18), 4, fill=rgb(hexa), outline="#9097AE")
            valor = hexa
            if tipo == "SolidColorBrush" and len(hexa) == 7 and k != "Surface":
                valor += f"   {contraste(hexa, sup):.1f}:1"
        d.text((x + 48, y + 2), k, font=f, fill="#0B1020")
        d.text((x + 200, y + 3), valor, font=fm, fill="#5B6380")
    return im


def main():
    original, nueva = Path(sys.argv[1]), Path(sys.argv[2])
    ok = True
    temas = {}
    for nombre in ("Palette.Dark.xaml", "Palette.Light.xaml"):
        a, b = leer(original / nombre), leer(nueva / nombre)
        if list(a) != list(b) or any(a[k][0] != b[k][0] for k in a):
            ok = False
            print(f"✖ {nombre}: claves o tipos distintos. Faltan {set(a) - set(b)}; sobran {set(b) - set(a)}")
        else:
            print(f"✔ {nombre}: {len(b)} claves, mismo orden y mismo tipo que el original")
        texto = (nueva / nombre).read_text(encoding="utf-8")
        if re.search(r"Color=\"#[0-9A-Fa-f]{3}\"", texto):
            print(f"⚠ {nombre}: color de 3 dígitos")
        temas[nombre] = b
    for nombre, p in temas.items():
        print(f"\n{nombre}")
        for a, b, ca, cb, minimo in pares(p):
            r = contraste(ca, cb)
            marca = "✔" if r >= minimo else "✖"
            if r < minimo and "hoy" not in a:
                ok = False
            print(f"  {marca} {a:34} sobre {b:24} {ca:>9} / {cb:<9} {r:5.2f}:1 (mín. {minimo:g})")
    ancho = 760
    partes = []
    for titulo, nombre in (("Tema OSCURO (predeterminado) · Palette.Dark.xaml", "Palette.Dark.xaml"),
                           ("Tema CLARO · Palette.Light.xaml", "Palette.Light.xaml")):
        cab = Image.new("RGB", (ancho, 40), "#FFFFFF")
        ImageDraw.Draw(cab).text((12, 10), titulo, font=fuente(16, True), fill="#0B1020")
        partes.append([cab, maqueta(temas[nombre], ancho), fichas(temas[nombre], ancho)])
    alto = max(sum(x.height for x in col) for col in partes)
    hoja = Image.new("RGB", (ancho * 2 + 30, alto + 20), "#E3E5F0")
    for i, col in enumerate(partes):
        y = 10
        for parte in col:
            hoja.paste(parte, (10 + i * (ancho + 10), y))
            y += parte.height
    hoja.save(nueva / "muestra-paleta.png", optimize=True)
    print("\n" + ("ok" if ok else "REVISAR") + f" · muestra en {nueva / 'muestra-paleta.png'}")


if __name__ == "__main__":
    main()
