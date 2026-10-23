# -*- coding: utf-8 -*-
"""
Erzeugt die Android-App-Icons aus dem Windows-App-Icon (src/Switch2Pro.Bridge/SKZ-Connect.png), damit alle Versionen
gleich aussehen. Adaptives Icon: die blaue Fläche des Windows-Icons füllt die ganze Android-Form (Kreis, Squircle …),
das Gamepad liegt in der sicheren Zone; der Vordergrund bleibt leer. Aufruf: python android/make_icons.py
"""
import pathlib

from PIL import Image

ROOT = pathlib.Path(__file__).resolve().parent.parent
SOURCE = ROOT / 'src' / 'Switch2Pro.Bridge' / 'SKZ-Connect.png'
RES = ROOT / 'android' / 'SkzConnect.Android' / 'Resources'
DENSITIES = {'mdpi': 1, 'hdpi': 1.5, 'xhdpi': 2, 'xxhdpi': 3, 'xxxhdpi': 4}
BORDER = 16  # heller Rand des Windows-Icons wird abgeschnitten


def full_bleed(src):
    """Transparente Ecken zeilenweise mit der Blaufarbe der Bildmitte füllen und den Rand abschneiden."""
    rgba = src.convert('RGBA')
    w, h = rgba.size
    filled = Image.new('RGBA', (w, h))
    px_src, px = rgba.load(), filled.load()
    for y in range(h):
        row = px_src[w // 2, min(max(y, BORDER + 4), h - BORDER - 5)]
        for x in range(w):
            r, g, b, a = px_src[x, y]
            k = a / 255
            px[x, y] = (round(r * k + row[0] * (1 - k)), round(g * k + row[1] * (1 - k)),
                        round(b * k + row[2] * (1 - k)), 255)
    return filled.crop((BORDER, BORDER, w - BORDER, h - BORDER))


def main():
    src = Image.open(SOURCE)
    bleed = full_bleed(src)
    for name, scale in DENSITIES.items():
        folder = RES / ('mipmap-' + name)
        adaptive = round(108 * scale)
        # Android zeigt nur die inneren 72 dp der 108-dp-Fläche: Motiv auf 76 dp, außen dieselbe Fläche vergrößert.
        canvas = bleed.resize((adaptive, adaptive), Image.LANCZOS)
        inner = round(76 * scale)
        canvas.paste(bleed.resize((inner, inner), Image.LANCZOS), ((adaptive - inner) // 2, (adaptive - inner) // 2))
        canvas.save(folder / 'appicon_background.png')
        Image.new('RGBA', (adaptive, adaptive), (0, 0, 0, 0)).save(folder / 'appicon_foreground.png')
        legacy = round(48 * scale)
        src.convert('RGBA').resize((legacy, legacy), Image.LANCZOS).save(folder / 'appicon.png')
        print(name, adaptive, legacy)


if __name__ == '__main__':
    main()
