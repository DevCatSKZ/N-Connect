# -*- coding: utf-8 -*-
"""Icon des „DevCatSKZ Repository“ (repository.devcatskz/icon.png) in der DevCat-Bildsprache wie SKZ Monitor,
SKZ Remote und SKZ Connect: Katze, die aus einer offenen Kiste schaut (Kiste = Paketquelle), dünne Kontur im
Iris-Verlauf, leuchtende Cyan-Augen, Amber-Nase und Amber-Download-Pfeil auf dem gemeinsamen Navy-Verlauf.
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
TOP, BOTTOM = np.array([6, 13, 20], np.float32), np.array([10, 18, 27], np.float32)
LW = int(S * 0.026)           # Konturbreite


def px(v):
    return int(round(v * S))


def iris(box):
    """Iris-Verlauf Teal→Blau→Violett über dem Motiv-Rechteck, leicht schräg (wie die anderen SKZ-Icons)."""
    x0, y0, x1, y1 = box
    y, x = np.mgrid[0:S, 0:S].astype(np.float32)
    t = ((x - x0) / max(1, x1 - x0)) * 0.85 + ((y - y0) / max(1, y1 - y0)) * 0.4
    t = np.clip((t - 0.0) / 1.25, 0, 1)[..., None]
    c = np.where(t < 0.5, TEAL + (BLUE - TEAL) * (t / 0.5), BLUE + (VIOLET - BLUE) * ((t - 0.5) / 0.5))
    return Image.fromarray(c.astype(np.uint8), 'RGB')


def mask():
    return Image.new('L', (S, S), 0)


def main():
    y = np.linspace(0, 1, S, dtype=np.float32)[:, None, None]
    img = Image.fromarray(np.repeat(TOP + (BOTTOM - TOP) * y, S, axis=1).astype(np.uint8), 'RGB').convert('RGBA')

    line = mask()                 # alles, was im Iris-Verlauf leuchtet
    fill = mask()                 # dunkle Flächen (Kopf, Kiste)
    dl, df = ImageDraw.Draw(line), ImageDraw.Draw(fill)

    # Kopf mit Ohren (wird unten von der Kiste verdeckt)
    hx0, hy0, hx1, hy1 = px(.32), px(.30), px(.68), px(.60)
    for sgn in (-1, 1):
        bcx = .5 + sgn * .115
        ear = [(px(bcx - .07), px(.345)), (px(bcx + sgn * .012), px(.215)), (px(bcx + .07), px(.345))]
        dl.line(ear, fill=255, width=LW, joint='curve')
    dl.rounded_rectangle([hx0, hy0, hx1, hy1], radius=px(.12), outline=255, width=LW)
    df.rounded_rectangle([hx0 + LW // 2, hy0 + LW // 2, hx1 - LW // 2, hy1], radius=px(.11), fill=255)

    # Kiste: Vorderseite deckt den Kopf ab, zwei Klappen schräg nach außen
    bx0, by0, bx1, by1 = px(.25), px(.53), px(.75), px(.80)
    cover = mask()
    ImageDraw.Draw(cover).rounded_rectangle([bx0 - LW, by0 - LW // 2, bx1 + LW, by1 + LW], radius=px(.05), fill=255)
    line.paste(0, (0, 0), cover)
    fill.paste(0, (0, 0), cover)
    dl.rounded_rectangle([bx0, by0, bx1, by1], radius=px(.04), outline=255, width=LW)
    df.rounded_rectangle([bx0, by0, bx1, by1], radius=px(.04), fill=255)
    for sgn, x_in in ((-1, bx0), (1, bx1)):
        flap = [(x_in, by0), (x_in + sgn * px(.085), by0 - px(.075)), (x_in + sgn * px(.02) - sgn * px(.095), by0 - px(.075))]
        dl.line([flap[0], flap[1], flap[2]], fill=255, width=LW, joint='curve')
    # Pfoten auf dem Rand
    for cx in (.42, .58):
        r = px(.035)
        c = (px(cx), by0)
        df.ellipse([c[0] - r, c[1] - r, c[0] + r, c[1] + r], fill=255)
        dl.ellipse([c[0] - r, c[1] - r, c[0] + r, c[1] + r], outline=255, width=int(LW * .8))

    bbox = line.getbbox()
    img.paste(FACE, (0, 0), fill)
    glow = line.filter(ImageFilter.GaussianBlur(S * .008)).point(lambda v: int(v * .25))
    img.paste((110, 215, 235), (0, 0), glow)
    img.paste(iris(bbox), (0, 0), line)

    # Augen mit Leuchten, Schlitzpupille; Nase
    eyes, pupils = mask(), mask()
    ey, rx, ry = .43, .058, .03
    for sgn in (-1, 1):
        ex = .5 + sgn * .085
        a, b = mask(), mask()
        off = ry * .9
        R = ((rx ** 2 + off ** 2) / (2 * off))
        ImageDraw.Draw(a).ellipse([px(ex - R), px(ey - off * 0 - R + (R - ry)), px(ex + R), px(ey + R + (R - ry))], fill=255)
        ImageDraw.Draw(b).ellipse([px(ex - R), px(ey - R - (R - ry)), px(ex + R), px(ey + R - (R - ry))], fill=255)
        leaf = Image.composite(a, mask(), b)
        eyes.paste(255, (0, 0), leaf)
        ImageDraw.Draw(pupils).rounded_rectangle([px(ex - .009), px(ey - .026), px(ex + .009), px(ey + .026)],
                                                  radius=px(.009), fill=255)
    halo = eyes.filter(ImageFilter.GaussianBlur(S * .03)).point(lambda v: int(v * .6))
    img.paste(EYE, (0, 0), halo)
    img.paste(EYE, (0, 0), eyes)
    img.paste(FACE, (0, 0), pupils)
    nose = mask()
    ImageDraw.Draw(nose).polygon([(px(.475), px(.485)), (px(.525), px(.485)), (px(.5), px(.51))], fill=255)
    img.paste(AMBER, (0, 0), nose)

    # Download-Pfeil auf der Kiste (Amber)
    arrow = mask()
    da = ImageDraw.Draw(arrow)
    aw = int(LW * 1.05)
    da.line([(px(.5), px(.605)), (px(.5), px(.72))], fill=255, width=aw)
    da.line([(px(.45), px(.675)), (px(.5), px(.725)), (px(.55), px(.675))], fill=255, width=aw, joint='curve')
    for x in (.45, .55):
        da.ellipse([px(x) - aw // 2, px(.675) - aw // 2, px(x) + aw // 2, px(.675) + aw // 2], fill=255)
    da.ellipse([px(.5) - aw // 2, px(.605) - aw // 2, px(.5) + aw // 2, px(.605) + aw // 2], fill=255)
    img.paste(AMBER, (0, 0), arrow)

    img.convert('RGB').resize((OUT_S, OUT_S), Image.LANCZOS).save(OUT)
    print('geschrieben:', OUT)


if __name__ == '__main__':
    main()
