# -*- coding: utf-8 -*-
"""Icon des „DevCatSKZ Repository“ (repository.devcatskz/icon.png) in der DevCat-Bildsprache wie SKZ Monitor,
SKZ Remote und SKZ Connect: Katze, die aus einer offenen Kiste schaut (Kiste = Paketquelle), dünne Kontur im
Iris-Verlauf, Ohren als offene Dreiecke auf dem Kopf (wie beim Monitor-Icon), leuchtende Cyan-Augen, Amber-Nase und
Amber-Download-Pfeil. Grund transparent, damit das Icon auf der Repository-Seite und in Kodi ohne Kasten steht.
Bei Design-Änderungen ausführen: py -3 linux/kodi/make_repo_icon.py – das PNG wird eingecheckt."""
import pathlib

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

S = 2048                      # Render-Auflösung (4× Supersampling)
OUT_S = 512
OUT = pathlib.Path(__file__).parent / 'repository.devcatskz' / 'icon.png'

TEAL = np.array([45, 225, 194], dtype=np.float32)
BLUE = np.array([86, 138, 255], dtype=np.float32)
VIOLET = np.array([124, 92, 255], dtype=np.float32)
EYE = (72, 240, 210)
AMBER = (255, 194, 75)
FACE = (10, 16, 24)
LW = int(S * 0.026)           # Konturbreite


def px(v):
    return int(round(v * S))


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


def layer(color, m):
    """Farbe (oder RGB-Bild) mit der Maske als Deckkraft – zum Übereinanderlegen auf transparentem Grund."""
    src = color if isinstance(color, Image.Image) else Image.new('RGB', (S, S), color)
    out = src.convert('RGBA')
    out.putalpha(m)
    return out


def main():
    img = Image.new('RGBA', (S, S), (0, 0, 0, 0))

    line = mask()                 # alles, was im Iris-Verlauf leuchtet
    fill = mask()                 # dunkle Flächen (Kopf, Kiste)
    dl, df = ImageDraw.Draw(line), ImageDraw.Draw(fill)

    # Kopf (unten von der Kiste verdeckt) und Ohren wie beim Monitor-Icon: zwei Schenkel, die auf der geraden
    # Oberkante stehen – die Kopflinie schließt das Dreieck, nichts liegt doppelt übereinander.
    hx0, hy0, hx1, hy1 = px(.31), px(.29), px(.69), px(.58)
    for sgn in (-1, 1):
        bcx = .5 + sgn * .11
        dl.line([(px(bcx - .055), hy0), (px(bcx + sgn * .02), px(.19)), (px(bcx + .055), hy0)],
                fill=255, width=LW, joint='curve')
    dl.rounded_rectangle([hx0, hy0, hx1, hy1], radius=px(.07), outline=255, width=LW)
    df.rounded_rectangle([hx0 + LW // 2, hy0 + LW // 2, hx1 - LW // 2, hy1], radius=px(.06), fill=255)

    # Kiste: Vorderseite deckt den Kopf ab; Klappen als kurze Striche schräg nach außen, weg vom Kopf.
    bx0, by0, bx1, by1 = px(.24), px(.52), px(.76), px(.80)
    cover = mask()
    ImageDraw.Draw(cover).rounded_rectangle([bx0 - LW, by0 - LW // 2, bx1 + LW, by1 + LW], radius=px(.05), fill=255)
    line.paste(0, (0, 0), cover)
    fill.paste(0, (0, 0), cover)
    dl.rounded_rectangle([bx0, by0, bx1, by1], radius=px(.04), outline=255, width=LW)
    df.rounded_rectangle([bx0, by0, bx1, by1], radius=px(.04), fill=255)
    for sgn, x_in in ((-1, bx0 + px(.02)), (1, bx1 - px(.02))):
        end = (x_in + sgn * px(.09), by0 - px(.075))
        dl.line([(x_in, by0), end], fill=255, width=LW)
        dl.ellipse([end[0] - LW // 2, end[1] - LW // 2, end[0] + LW // 2, end[1] + LW // 2], fill=255)

    bbox = line.getbbox()
    img = Image.alpha_composite(img, layer(FACE, fill))
    glow = line.filter(ImageFilter.GaussianBlur(S * .008)).point(lambda v: int(v * .25))
    img = Image.alpha_composite(img, layer((110, 215, 235), glow))
    img = Image.alpha_composite(img, layer(iris(bbox), line))

    # Augen mit Leuchten (Mandelform aus zwei Kreisen), Schlitzpupille; Nase
    eyes, pupils = mask(), mask()
    ey, rx, ry = .395, .052, .027
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
    ImageDraw.Draw(nose).polygon([(px(.478), px(.448)), (px(.522), px(.448)), (px(.5), px(.47))], fill=255)
    img = Image.alpha_composite(img, layer(AMBER, nose))

    # Download-Pfeil auf der Kiste (Amber)
    arrow = mask()
    da = ImageDraw.Draw(arrow)
    aw = int(LW * 1.05)
    da.line([(px(.5), px(.605)), (px(.5), px(.72))], fill=255, width=aw)
    da.line([(px(.45), px(.675)), (px(.5), px(.725)), (px(.55), px(.675))], fill=255, width=aw, joint='curve')
    for x, y in ((.45, .675), (.55, .675), (.5, .605)):
        da.ellipse([px(x) - aw // 2, px(y) - aw // 2, px(x) + aw // 2, px(y) + aw // 2], fill=255)
    img = Image.alpha_composite(img, layer(AMBER, arrow))

    img.resize((OUT_S, OUT_S), Image.LANCZOS).save(OUT)
    print('geschrieben:', OUT)


if __name__ == '__main__':
    main()
