# -*- coding: utf-8 -*-
"""Icon des „DevCatSKZ Repository“ (repository.devcatskz/icon.png) in der DevCat-Bildsprache wie SKZ Monitor,
SKZ Remote und SKZ Connect: Katze, die aus einer offenen Kiste schaut (Kiste = Paketquelle).

Kopf mit Ohren und Kiste mit Klappen sind je eine geschlossene Fläche; die Kontur im Iris-Verlauf entsteht als Band
um die ganze Fläche (Dilatation minus Erosion). So gibt es keine Stoßstellen, Lücken oder doppelten Linien. Dazu
leuchtende Cyan-Augen, Amber-Nase und Amber-Download-Pfeil; Grund transparent, damit das Icon auf der
Repository-Seite und in Kodi ohne Kasten steht.
Bei Design-Änderungen ausführen: py -3 linux/kodi/make_repo_icon.py – das PNG wird eingecheckt."""
import pathlib

import numpy as np
from PIL import Image, ImageChops, ImageDraw, ImageFilter

S = 2048                      # Render-Auflösung (4× Supersampling)
OUT_S = 512
OUT = pathlib.Path(__file__).parent / 'repository.devcatskz' / 'icon.png'

TEAL = np.array([45, 225, 194], dtype=np.float32)
BLUE = np.array([86, 138, 255], dtype=np.float32)
VIOLET = np.array([124, 92, 255], dtype=np.float32)
EYE = (72, 240, 210)
AMBER = (255, 194, 75)
FACE = (10, 16, 24)
LW = int(S * 0.028)           # Konturbreite


def px(v):
    return int(round(v * S))


def P(x, y):
    return (px(x), px(y))


def iris(box):
    """Iris-Verlauf Teal→Blau→Violett über dem Motiv-Rechteck, leicht schräg (wie die anderen SKZ-Icons)."""
    x0, y0, x1, y1 = box
    y, x = np.mgrid[0:S, 0:S].astype(np.float32)
    t = ((x - x0) / max(1, x1 - x0)) * 0.85 + ((y - y0) / max(1, y1 - y0)) * 0.4
    t = np.clip(t / 1.25, 0, 1)[..., None]
    c = np.where(t < 0.5, TEAL + (BLUE - TEAL) * (t / 0.5), BLUE + (VIOLET - BLUE) * ((t - 0.5) / 0.5))
    return Image.fromarray(c.astype(np.uint8), 'RGB')


def mask():
    return Image.new('L', (S, S), 0)


def grow(m, r, shrink=False):
    """Maske um r Pixel wachsen (oder schrumpfen) lassen – wiederholter 3×3-Filter, ergibt runde Ecken."""
    f = ImageFilter.MinFilter(3) if shrink else ImageFilter.MaxFilter(3)
    for _ in range(r):
        m = m.filter(f)
    return m


def band(m, width):
    """Kontur der Fläche: Streifen der Breite width, mittig auf dem Rand."""
    return ImageChops.subtract(grow(m, width // 2), grow(m, width // 2, shrink=True))


def layer(color, m):
    """Farbe (oder RGB-Bild) mit der Maske als Deckkraft – zum Übereinanderlegen auf transparentem Grund."""
    src = color if isinstance(color, Image.Image) else Image.new('RGB', (S, S), color)
    out = src.convert('RGBA')
    out.putalpha(m)
    return out


def main():
    # Kopf mit Ohren als eine Fläche (unten von der Kiste verdeckt)
    head = mask()
    dh = ImageDraw.Draw(head)
    dh.rounded_rectangle([*P(.31, .30), *P(.69, .60)], radius=px(.075), fill=255)
    for sgn in (-1, 1):
        c = .5 + sgn * .115
        dh.polygon([P(c - .07, .33), P(c + sgn * .025, .185), P(c + .065, .33)], fill=255)

    # Kiste mit zwei aufgeklappten Klappen als eine Fläche; die Klappen hängen an den äußeren Ecken und zeigen vom
    # Kopf weg nach außen.
    box = mask()
    db = ImageDraw.Draw(box)
    bx0, by0, bx1, by1 = .21, .53, .79, .80
    db.rounded_rectangle([*P(bx0, by0), *P(bx1, by1)], radius=px(.045), fill=255)
    for sgn, edge in ((-1, bx0), (1, bx1)):
        # Scharnier auf der Oberkante zwischen Ecke und Kopf, Klappe schräg nach außen oben aufgeklappt
        # (Außenkante geht ohne Stufe in die Kistenseite über, unterhalb der Eckenrundung)
        hinge_in = edge - sgn * .085
        db.polygon([P(hinge_in, by0 + .01), P(edge, by0 + .05),
                    P(edge + sgn * .09, by0 - .07), P(hinge_in + sgn * .07, by0 - .09)], fill=255)

    # Kopf nur, wo die Kiste ihn nicht verdeckt (mit Abstand der halben Konturbreite, damit die Kistenkontur frei steht)
    box_cover = grow(box, LW // 2)
    head_vis = ImageChops.subtract(head, box_cover)
    head_line = ImageChops.subtract(band(head, LW), box_cover)
    box_line = band(box, LW)
    line = ImageChops.lighter(head_line, box_line)
    fill = ImageChops.lighter(head_vis, box)

    img = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    img = Image.alpha_composite(img, layer(FACE, fill))
    glow = line.filter(ImageFilter.GaussianBlur(S * .008)).point(lambda v: int(v * .25))
    img = Image.alpha_composite(img, layer((110, 215, 235), glow))
    img = Image.alpha_composite(img, layer(iris(line.getbbox()), line))

    # Augen mit Leuchten (Mandelform aus zwei Kreisen), Schlitzpupille; Nase
    eyes, pupils = mask(), mask()
    ey, rx, ry = .41, .052, .027
    for sgn in (-1, 1):
        ex = .5 + sgn * .083
        R = (rx ** 2 + ry ** 2) / (2 * ry)          # Kreis durch beide Augenwinkel und die Scheitel
        a, b = mask(), mask()
        ImageDraw.Draw(a).ellipse([px(ex - R), px(ey + ry - 2 * R), px(ex + R), px(ey + ry)], fill=255)
        ImageDraw.Draw(b).ellipse([px(ex - R), px(ey - ry), px(ex + R), px(ey - ry + 2 * R)], fill=255)
        eyes.paste(255, (0, 0), Image.composite(a, mask(), b))
        ImageDraw.Draw(pupils).rounded_rectangle([px(ex - .008), px(ey - .023), px(ex + .008), px(ey + .023)],
                                                  radius=px(.008), fill=255)
    halo = eyes.filter(ImageFilter.GaussianBlur(S * .02)).point(lambda v: int(v * .5))
    img = Image.alpha_composite(img, layer(EYE, halo))
    img = Image.alpha_composite(img, layer(EYE, eyes))
    img = Image.alpha_composite(img, layer(FACE, pupils))
    nose = mask()
    ImageDraw.Draw(nose).polygon([P(.478, .462), P(.522, .462), P(.5, .484)], fill=255)
    img = Image.alpha_composite(img, layer(AMBER, nose))

    # Download-Pfeil mittig auf der Kiste (Amber)
    arrow = mask()
    da = ImageDraw.Draw(arrow)
    aw = int(LW * 1.05)
    cy = (by0 + by1) / 2
    top, tip = cy - .06, cy + .06
    da.line([P(.5, top), P(.5, tip)], fill=255, width=aw)
    da.line([P(.45, tip - .05), P(.5, tip), P(.55, tip - .05)], fill=255, width=aw, joint='curve')
    for x, y in ((.45, tip - .05), (.55, tip - .05), (.5, top)):
        da.ellipse([px(x) - aw // 2, px(y) - aw // 2, px(x) + aw // 2, px(y) + aw // 2], fill=255)
    img = Image.alpha_composite(img, layer(AMBER, arrow))

    img.resize((OUT_S, OUT_S), Image.LANCZOS).save(OUT)
    print('geschrieben:', OUT)


if __name__ == '__main__':
    main()
