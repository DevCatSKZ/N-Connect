# -*- coding: utf-8 -*-
"""
Erzeugt die Android-App-Icons aus den Dateien von --render-brand (installer/art), damit alle Versionen gleich aussehen.
Adaptives Icon wie bei SKZ Monitor und SKZ Remote: der Hintergrund ist nur der Navy-Verlauf, das Glyph liegt als
eigener Vordergrund in der sicheren Zone. Die Altform (appicon.png) ist das PNG-Master-Icon.
Aufruf: python android/make_icons.py
"""
import pathlib

from PIL import Image

ROOT = pathlib.Path(__file__).resolve().parent.parent
ART = ROOT / 'installer' / 'art'
RES = ROOT / 'android' / 'SkzConnect.Android' / 'Resources'
DENSITIES = {'mdpi': 1, 'hdpi': 1.5, 'xhdpi': 2, 'xxhdpi': 3, 'xxxhdpi': 4}
# Navy-Verlauf identisch zu Branding.TileTop/TileBottom und den SKZ-Icons.
TOP, BOTTOM = (6, 13, 20), (10, 18, 27)
GLYPH_WIDTH = 0.46  # Anteil der 108-dp-Fläche – wie das Monitor-Glyph (~43 %), auch unter der Kreismaske ganz sichtbar


def navy(size):
    img = Image.new('RGBA', (size, size))
    px = img.load()
    for y in range(size):
        t = y / max(1, size - 1)
        c = tuple(round(a + (b - a) * t) for a, b in zip(TOP, BOTTOM)) + (255,)
        for x in range(size):
            px[x, y] = c
    return img


def main():
    master = Image.open(ART / 'SKZ-Connect.png').convert('RGBA')
    glyph = Image.open(ART / 'SKZ-Connect-glyph.png').convert('RGBA')
    glyph = glyph.crop(glyph.getbbox())
    for name, scale in DENSITIES.items():
        folder = RES / ('mipmap-' + name)
        adaptive = round(108 * scale)
        navy(adaptive).save(folder / 'appicon_background.png')
        w = round(adaptive * GLYPH_WIDTH)
        h = round(glyph.height * w / glyph.width)
        fg = Image.new('RGBA', (adaptive, adaptive), (0, 0, 0, 0))
        fg.alpha_composite(glyph.resize((w, h), Image.LANCZOS), ((adaptive - w) // 2, (adaptive - h) // 2))
        fg.save(folder / 'appicon_foreground.png')
        legacy = round(48 * scale)
        master.resize((legacy, legacy), Image.LANCZOS).save(folder / 'appicon.png')
        print(name, adaptive, legacy)


if __name__ == '__main__':
    main()
