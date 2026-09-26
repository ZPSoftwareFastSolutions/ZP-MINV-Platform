r"""Genera el ícono y el logotipo de M-INV V4.2 · Edición Tecnología y Gaming.

Ícono: cubo-chip isométrico con degradado violeta → cian y aristas de neón sobre un cuadrado redondeado casi negro con
borde de neón (dibujo propio, sin textos). Mismos tamaños y formatos que tools/generar_icono_escritorio.py.

Uso:    .venv\Scripts\python tools\tecnologia\generar_icono_gaming.py [carpeta_salida] [--vista-previa]
Salida: src/3. Presentation/MINV.DesktopClient/Assets/minv.ico (16 a 256 px) y minv-256.png
        --vista-previa agrega minv-logotipo.png (ícono + «M-INV» + «TECH · GAMING» sobre placa oscura, para la
        documentación) y _vista-previa.png (el ícono a 256/64/48/32/24/16 px sobre fondo oscuro y claro).
"""
import sys
from pathlib import Path

from PIL import Image, ImageChops, ImageColor, ImageDraw, ImageFilter, ImageFont

S = 1024
VIOLETA, VIOLETA_OSC, CIAN, CIAN_CLARO, MAGENTA = "#8B5CF6", "#5B21B6", "#22D3EE", "#A5F3FC", "#E879F9"


def _rgba(color, alpha=None):
    r, g, b, a = ImageColor.getcolor(color, "RGBA")
    return (r, g, b, a if alpha is None else alpha)


def degradado_diagonal(size, c0, c1, pesos=(0.35, 0.65)):
    """Degradado lineal de c0 (arriba a la izquierda) a c1 (abajo a la derecha)."""
    a, b = _rgba(c0), _rgba(c1)
    rampa = Image.linear_gradient("L").resize((size, size))                      # 0 arriba → 255 abajo
    horizontal = rampa.transpose(Image.Transpose.ROTATE_90)                        # 0 a la izquierda → 255 a la derecha
    t = Image.blend(horizontal, rampa, pesos[1] / (pesos[0] + pesos[1]))
    canales = [t.point(lambda v, i=i: int(a[i] + (b[i] - a[i]) * v / 255)) for i in range(4)]
    return Image.merge("RGBA", canales)


def cara(img, puntos, c0, c1):
    """Rellena un polígono con un degradado entre su vértice superior izquierdo y el inferior derecho."""
    xs, ys = [p[0] for p in puntos], [p[1] for p in puntos]
    x0, y0, x1, y1 = int(min(xs)), int(min(ys)), int(max(xs)) + 1, int(max(ys)) + 1
    lado = max(x1 - x0, y1 - y0)
    g = degradado_diagonal(lado, c0, c1).crop((0, 0, x1 - x0, y1 - y0))
    m = Image.new("L", (x1 - x0, y1 - y0), 0)
    ImageDraw.Draw(m).polygon([(x - x0, y - y0) for x, y in puntos], fill=255)
    g.putalpha(ImageChops.multiply(g.getchannel("A"), m))
    img.alpha_composite(g, (x0, y0))


def icon():
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    # cuadrado redondeado casi negro con un leve tinte violeta → azul noche
    mascara = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mascara).rounded_rectangle((32, 32, S - 32, S - 32), radius=230, fill=255)
    fondo = degradado_diagonal(S, "#1C1045", "#06121E")
    fondo.putalpha(mascara)
    img.alpha_composite(fondo)

    # halo detrás del cubo y circuito tenue
    halo = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    hd = ImageDraw.Draw(halo)
    hd.ellipse((212, 190, 812, 790), fill=_rgba(VIOLETA, 120))
    hd.ellipse((330, 420, 830, 900), fill=_rgba(CIAN, 70))
    halo = halo.filter(ImageFilter.GaussianBlur(90))
    circuito = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    cd = ImageDraw.Draw(circuito)
    for pts, col in ((((120, 300), (230, 300), (290, 360)), VIOLETA), (((120, 720), (250, 720), (300, 670)), CIAN),
                     (((904, 300), (794, 300), (734, 360)), CIAN), (((904, 720), (774, 720), (724, 670)), VIOLETA),
                     (((512, 120), (512, 190)), VIOLETA), (((512, 904), (512, 860)), CIAN)):
        cd.line(pts, fill=_rgba(col, 150), width=10, joint="curve")
        x, y = pts[0]
        cd.ellipse((x - 16, y - 16, x + 16, y + 16), outline=_rgba(col, 170), width=8)
    for capa in (halo, circuito):
        capa.putalpha(ImageChops.multiply(capa.getchannel("A"), mascara))
        img.alpha_composite(capa)

    # cubo isométrico (el paquete de inventario de M-INV, ahora como chip de neón) en su propia capa
    cubo = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    cx, cy, r = S / 2, S / 2 + 24, 300
    h = r * 0.5
    arriba = [(cx, cy - r), (cx + r * 0.866, cy - h), (cx, cy), (cx - r * 0.866, cy - h)]
    izq = [(cx - r * 0.866, cy - h), (cx, cy), (cx, cy + r), (cx - r * 0.866, cy + h)]
    der = [(cx + r * 0.866, cy - h), (cx + r * 0.866, cy + h), (cx, cy + r), (cx, cy)]
    cara(cubo, izq, "#A78BFA", "#5B21B6")
    cara(cubo, der, "#6D28D9", "#0E7490")
    cara(cubo, arriba, CIAN_CLARO, "#38BDF8")
    k = 0.52                                                                        # «dado» del chip
    dado = [(cx + (x - cx) * k, (cy - h) + (y - (cy - h)) * k) for x, y in arriba]
    cara(cubo, dado, "#312E81", "#0B1B3A")

    brillo = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    bd, d = ImageDraw.Draw(brillo), ImageDraw.Draw(cubo)
    aristas = [arriba + [arriba[0]], [(cx, cy), (cx, cy + r)], [izq[0], izq[3], izq[2]], [der[0], der[1], der[2]]]
    for pts in aristas:
        bd.line(pts, fill=_rgba(CIAN), width=34, joint="curve")
        d.line(pts, fill=_rgba("#E0FBFF"), width=12, joint="curve")
    bd.line(dado + [dado[0]], fill=_rgba(MAGENTA), width=26, joint="curve")
    d.line(dado + [dado[0]], fill=_rgba("#F5D0FE"), width=8, joint="curve")
    for i in range(1, 4):                                                           # patas del chip
        f = i / 4
        for (ax, ay), (bx, by) in ((izq[3], izq[2]), (der[2], der[1])):
            x, y = ax + (bx - ax) * f, ay + (by - ay) * f
            d.line([(x, y - 34), (x, y - 4)], fill=_rgba("#E0FBFF", 230), width=12)
    brillo = brillo.filter(ImageFilter.GaussianBlur(24))
    brillo.putalpha(ImageChops.multiply(brillo.getchannel("A"), mascara))
    resultado = img
    resultado.alpha_composite(brillo)
    resultado.alpha_composite(brillo)
    resultado.alpha_composite(cubo)

    # borde de neón del cuadrado
    borde = Image.new("L", (S, S), 0)
    ImageDraw.Draw(borde).rounded_rectangle((40, 40, S - 40, S - 40), radius=222, outline=255, width=14)
    color = degradado_diagonal(S, VIOLETA, CIAN)
    color.putalpha(borde)
    halo_borde = color.filter(ImageFilter.GaussianBlur(10))
    halo_borde.putalpha(ImageChops.multiply(halo_borde.getchannel("A"), mascara))
    resultado.alpha_composite(halo_borde)
    resultado.alpha_composite(color)
    return resultado


def logotipo(ico):
    """Ícono + «M-INV» + «TECH · GAMING» sobre una placa oscura (sirve sobre fondos claros y oscuros)."""
    try:
        titulo = ImageFont.truetype("bahnschrift.ttf", 200)
        titulo.set_variation_by_name("Bold")
        sub = ImageFont.truetype("bahnschrift.ttf", 80)
        sub.set_variation_by_name("SemiBold")
    except (OSError, ValueError):
        titulo = ImageFont.truetype("segoeuib.ttf", 190)
        sub = ImageFont.truetype("segoeuib.ttf", 74)
    alto, x_texto = 512, 540
    medir = ImageDraw.Draw(Image.new("L", (1, 1)))
    caja_t = medir.textbbox((x_texto, 285), "M-INV", font=titulo, anchor="ls")
    caja_s = medir.textbbox((x_texto + 6, 420), "TECH · GAMING", font=sub, anchor="ls")
    ancho = max(caja_t[2], caja_s[2]) + 70
    lienzo = Image.new("RGBA", (ancho, alto), (0, 0, 0, 0))
    placa = Image.new("L", (ancho, alto), 0)
    ImageDraw.Draw(placa).rounded_rectangle((0, 0, ancho - 1, alto - 1), radius=96, fill=255)
    fondo = degradado_diagonal(ancho, "#0F0A26", "#07101B").crop((0, 0, ancho, alto))
    fondo.putalpha(placa)
    lienzo.alpha_composite(fondo)
    lienzo.alpha_composite(ico.resize((alto - 48, alto - 48), Image.LANCZOS), (24, 24))
    d = ImageDraw.Draw(lienzo)
    d.text((x_texto, 285), "M-INV", font=titulo, fill="#F5F3FF", anchor="ls")
    mascara = Image.new("L", lienzo.size, 0)
    ImageDraw.Draw(mascara).text((x_texto + 6, 420), "TECH · GAMING", font=sub, fill=255, anchor="ls")
    x0, x1 = caja_s[0], caja_s[2]
    tira = Image.new("RGBA", (256, 1))
    for i in range(256):
        a, b = _rgba("#A78BFA"), _rgba(CIAN)
        tira.putpixel((i, 0), tuple(int(a[j] + (b[j] - a[j]) * i / 255) for j in range(4)))
    color = Image.new("RGBA", lienzo.size, (0, 0, 0, 0))
    color.paste(tira.resize((x1 - x0, alto), Image.BILINEAR), (x0, 0))
    color.putalpha(ImageChops.multiply(color.getchannel("A"), mascara))
    lienzo.alpha_composite(color)
    return lienzo.resize((ancho // 2, alto // 2), Image.LANCZOS)


def vista_previa(ico):
    tam = [256, 64, 48, 32, 24, 16]
    hoja = Image.new("RGB", (700, 620), "#070A12")
    ImageDraw.Draw(hoja).rectangle((0, 310, 700, 620), fill="#F3F5FA")
    for fila, y in ((0, 20), (1, 330)):
        x = 20
        for t in tam:
            im = ico.resize((t, t), Image.LANCZOS)
            hoja.paste(im, (x, y + (256 - t) // 2), im)
            x += t + 24
    return hoja


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    out = Path(args[0]) if args else Path(__file__).resolve().parents[2] / "src" / "3. Presentation" / "MINV.DesktopClient" / "Assets"
    out.mkdir(parents=True, exist_ok=True)
    img = icon()
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    img.resize((256, 256), Image.LANCZOS).save(out / "minv.ico", sizes=[(s, s) for s in sizes])
    img.resize((256, 256), Image.LANCZOS).save(out / "minv-256.png")
    if "--vista-previa" in sys.argv:
        logotipo(img).save(out / "minv-logotipo.png")
        vista_previa(img).save(out / "_vista-previa.png")
    print("ok")


if __name__ == "__main__":
    main()
