# -*- coding: utf-8 -*-
"""N-Connect-Icon — DevCat-Verwandtschaft (wie SKZ Monitor / SKZ Remote).

Motiv: Gamepad mit Katzenohren — Rahmen im Iris-Verlauf
(Teal->Blau->Violett), dunkles Glas-Gesicht mit zwei leuchtenden
Katzenaugen (Cyan) und kleiner amber Nase. Wi-Fi-Boegen oben mittig
zwischen den Ohren als Connect-Signatur, D-Pad links und zwei
Aktionsknöpfe rechts in Dim-Teal. Hintergrund Navy wie bei SKZ.

Erzeugt:
- src/Switch2Pro.Bridge/N-Connect.png (512, Master für Android/Kodi)
- src/Switch2Pro.Bridge/N-Connect.ico (16..256 PNG-Frames, App + Tray)
- installer/art/N-Connect.png (512, für Installer + Kodi-Add-ons)
- installer/art/N-Connect.ico (BMP-Frames, Inno SetupIconFile)
- installer/art/wizard-*.bmp + wizard-small-*.bmp (Setup-Seitenbilder)
- installer/art/icon_preview.png (Vorschau)

Aufruf: python installer/art/make_icon.py
Danach:  python android/make_icons.py (Android-Dichten aus dem Master)

Render-Pipeline: 4096 px, danach LANCZOS-Downscale (4x-Supersampling).
"""
import pathlib

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

S = 4096
ROOT = pathlib.Path(__file__).resolve().parent.parent.parent
OUT_SRC = ROOT / 'src' / 'Switch2Pro.Bridge'
OUT_ART = ROOT / 'installer' / 'art'

TEAL = np.array([45, 225, 194], dtype=np.float32)
BLUE = np.array([86, 138, 255], dtype=np.float32)
VIOLET = np.array([124, 92, 255], dtype=np.float32)
GOLD = np.array([255, 194, 75], dtype=np.float32)
EYE = np.array([72, 240, 210], dtype=np.float32)
# Hintergrund identisch zum SKZ-Monitor-/SKZ-Remote-Icon
BG_TOP = np.array([6, 13, 20], dtype=np.float32)
BG_BOT = np.array([10, 18, 27], dtype=np.float32)


def iris_rgb(size, angle=0.62):
    """Iris-Verlauf Teal->Blau->Violett (DevCat-Farbwelt)."""
    y, x = np.mgrid[0:size, 0:size].astype(np.float32) / size
    t = x * np.cos(angle) + y * np.sin(angle)
    t = (t - t.min()) / (t.max() - t.min())
    c = np.empty((size, size, 3), dtype=np.float32)
    lo = t < 0.5
    tt = np.clip(t / 0.5, 0, 1)
    c[lo] = TEAL + (BLUE - TEAL) * tt[lo, None]
    tt2 = np.clip((t - 0.5) / 0.5, 0, 1)
    c[~lo] = BLUE + (VIOLET - BLUE) * tt2[~lo, None]
    return Image.fromarray(c.astype(np.uint8), 'RGB')


def background():
    y = (np.mgrid[0:S, 0:S][0].astype(np.float32) / S)
    base = BG_TOP + (BG_BOT - BG_TOP) * y[..., None]
    return Image.fromarray(
        np.clip(base, 0, 255).astype(np.uint8), 'RGB').convert('RGBA')


def pad_silhouette(draw, inset, fill):
    """Gamepad-Silhouette: Kapsel-Körper + zwei Griff-Ellipsen unten.
    inset schrumpft die Form (für Rahmen-Innenseite)."""
    cx, cy = S // 2, int(S * .585)
    w, h = int(S * .62) - 2 * inset, int(S * .36) - 2 * inset
    x0, y0 = cx - w // 2, cy - h // 2
    x1, y1 = cx + w // 2, cy + h // 2
    rad = max(4, h // 2)
    draw.rounded_rectangle([x0, y0, x1, y1], radius=rad, fill=fill)
    # Griffe: Ellipsen an den unteren Außenseiten
    hr_x = int(S * .088) - inset
    hr_y = int(S * .115) - inset
    for sgn in (-1, 1):
        hx = cx + sgn * int(S * .265)
        hy = y1 - int(S * .075)
        draw.ellipse([hx - hr_x, hy - hr_y, hx + hr_x, hy + hr_y],
                     fill=fill)
    return x0, y0, x1, y1


def cat_group():
    """Gamepad-Katze. Gibt (line, glass, eyes, pupils, nose, wifi,
    controls) als L-Masken zurück."""
    lw = int(S * .027)

    # Rahmen = gefüllte Silhouette minus eingezogene Silhouette
    sil = Image.new('L', (S, S), 0)
    x0, y0, x1, y1 = pad_silhouette(ImageDraw.Draw(sil), 0, 255)
    inner = Image.new('L', (S, S), 0)
    pad_silhouette(ImageDraw.Draw(inner), lw, 255)
    line = Image.composite(Image.new('L', (S, S), 0), sil, inner)

    # Katzenohren auf der Oberkante (Iris-gefüllt mit dunklem Kern)
    d = ImageDraw.Draw(line)
    w = x1 - x0
    ear_h = int(S * .115)
    for sgn in (-1, 1):
        bcx = (x0 + x1) // 2 + sgn * int(w * .30)
        b_half = int(w * .185)
        apex = (bcx + sgn * int(w * .02), y0 - ear_h)
        base_y = y0 + int(lw * .6)
        d.polygon([apex, (bcx - b_half, base_y),
                   (bcx + b_half, base_y)], fill=255)
        im = int(b_half * .52)
        iap = (apex[0] + int((bcx - apex[0]) * .18),
               apex[1] + int(ear_h * .34))
        d.polygon([iap, (bcx - im, base_y), (bcx + im, base_y)], fill=0)

    # Glas-Gesicht (dunkel, leicht transparent -> paste alpha)
    glass = Image.new('L', (S, S), 0)
    gd = ImageDraw.Draw(glass)
    inset = lw + int(S * .008)
    gw, gh = x1 - x0 - 2 * inset, y1 - y0 - 2 * inset
    gd.rounded_rectangle([x0 + inset, y0 + inset, x1 - inset,
                          y1 - inset], radius=gh // 2, fill=30)

    cx = (x0 + x1) // 2
    # Katzenaugen (Leaf-Form, leuchtend cyan)
    eyes = Image.new('L', (S, S), 0)
    pupils = Image.new('L', (S, S), 0)
    R = int(S * .092)
    off = int(R * .72)
    ecy = y0 + int((y1 - y0) * .30)
    for sgn in (-1, 1):
        ecx = cx + sgn * int(S * .105)
        a = Image.new('L', (S, S), 0)
        b = Image.new('L', (S, S), 0)
        ImageDraw.Draw(a).ellipse(
            [ecx - R, ecy - off - R, ecx + R, ecy - off + R], fill=255)
        ImageDraw.Draw(b).ellipse(
            [ecx - R, ecy + off - R, ecx + R, ecy + off + R], fill=255)
        leaf = Image.composite(a, Image.new('L', (S, S), 0), b)
        eyes.paste(leaf, (0, 0), leaf)
        pw, ph = int(R * .17), int((R - off) * 1.15 + R * .16)
        ImageDraw.Draw(pupils).rounded_rectangle(
            [ecx - pw, ecy - ph, ecx + pw, ecy + ph],
            radius=pw, fill=255)

    # Nase (Amber-Dreieck)
    nose = Image.new('L', (S, S), 0)
    nw, nh = int(S * .050), int(S * .038)
    ny = ecy + int(R * 1.15)
    ImageDraw.Draw(nose).polygon(
        [(cx - nw, ny - nh // 2), (cx + nw, ny - nh // 2),
         (cx, ny + nh)], fill=255)

    # Wi-Fi-Bögen oben mittig zwischen den Ohren (Connect-Signatur)
    wifi = Image.new('L', (S, S), 0)
    wd = ImageDraw.Draw(wifi)
    wx, wy = cx, y0 - int(S * .012)
    wwl = int(S * .017)
    for rr in (int(S * .048), int(S * .088)):
        wd.arc([wx - rr, wy - rr, wx + rr, wy + rr], 200, 340,
               fill=255, width=wwl)
    pr = int(S * .013)
    wd.ellipse([wx - pr, wy - int(S * .004) - pr, wx + pr,
                wy - int(S * .004) + pr], fill=255)

    # D-Pad links + zwei Knöpfe rechts (Dim-Teal)
    ctrl = Image.new('L', (S, S), 0)
    cd = ImageDraw.Draw(ctrl)
    cy_pad = y0 + int((y1 - y0) * .66)
    dx = x0 + int(w * .155)
    arm, thick = int(S * .034), int(S * .026)
    cd.rounded_rectangle([dx - arm, cy_pad - thick // 2, dx + arm,
                          cy_pad + thick // 2], radius=thick // 2,
                         fill=255)
    cd.rounded_rectangle([dx - thick // 2, cy_pad - arm,
                          dx + thick // 2, cy_pad + arm],
                         radius=thick // 2, fill=255)
    bx = x1 - int(w * .155)
    br = int(S * .018)
    for dy, dxo in ((-1, -1), (1, -1), (-1, 1), (1, 1)):
        cd.ellipse([bx + dxo * int(S * .042) - br,
                    cy_pad + dy * int(S * .042) - br,
                    bx + dxo * int(S * .042) + br,
                    cy_pad + dy * int(S * .042) + br], fill=255)

    return line, glass, eyes, pupils, nose, wifi, ctrl


def foreground():
    line, glass, eyes, pupils, nose, wifi, ctrl = cat_group()

    fg = Image.new('RGBA', (S, S), (0, 0, 0, 0))

    gl = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    gl.paste(Image.new('RGB', (S, S), (200, 235, 228)), (0, 0), glass)
    fg = Image.alpha_composite(fg, gl)

    gm = eyes.filter(ImageFilter.GaussianBlur(S * .030))
    glow = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    glow.paste(Image.new('RGB', (S, S), tuple(EYE.astype(int))), (0, 0),
               gm.point(lambda v: int(v * .55)))
    fg = Image.alpha_composite(fg, glow)

    rm = line.filter(ImageFilter.GaussianBlur(S * .010))
    rim = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    rim.paste(Image.new('RGB', (S, S), (110, 215, 235)), (0, 0),
              rm.point(lambda v: int(v * .25)))
    fg = Image.alpha_composite(fg, rim)

    bb = line.getbbox()
    gw2, gh2 = bb[2] - bb[0], bb[3] - bb[1]
    grad_full = Image.new('RGB', (S, S))
    grad_full.paste(
        iris_rgb(max(gw2, gh2)).crop((0, 0, gw2, gh2)), (bb[0], bb[1]))
    fg.paste(grad_full, (0, 0), line)

    fg.paste(Image.new('RGB', (S, S), tuple(EYE.astype(int))), (0, 0),
             eyes)
    fg.paste(Image.new('RGB', (S, S), (10, 16, 24)), (0, 0), pupils)

    gold = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    gold.paste(Image.new('RGB', (S, S), tuple(GOLD.astype(int))),
               (0, 0), nose)
    fg = Image.alpha_composite(fg, gold)
    gold2 = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    gold2.paste(Image.new('RGB', (S, S), tuple(GOLD.astype(int))),
                (0, 0), wifi)
    fg = Image.alpha_composite(fg, gold2)

    cl = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    cl.paste(Image.new('RGB', (S, S), (110, 190, 200)), (0, 0), ctrl)
    fg = Image.alpha_composite(fg, cl)
    return fg


def write_bmp_ico(images, path):
    """Klassisches ICO mit BMP/DIB-Frames (Inno-kompatibel)."""
    import struct
    frames = []
    for size, img in sorted(images):
        img = img.convert('RGBA').resize((size, size), Image.LANCZOS)
        w = h = size
        px = img.tobytes()
        xor = bytearray()
        for yy in range(h - 1, -1, -1):
            row = px[yy * w * 4:(yy + 1) * w * 4]
            for i in range(0, len(row), 4):
                xor += bytes((row[i + 2], row[i + 1], row[i],
                              row[i + 3]))
        mask_row = (w + 31) // 32 * 4
        andm = bytearray()
        for yy in range(h - 1, -1, -1):
            bits = 0
            nbytes = 0
            for xx in range(w):
                a = px[(yy * w + xx) * 4 + 3]
                bits = (bits << 1) | (1 if a < 128 else 0)
                if (xx + 1) % 8 == 0:
                    andm.append(bits & 0xFF)
                    bits = 0
                    nbytes += 1
            if w % 8:
                andm.append((bits << (8 - w % 8)) & 0xFF)
                nbytes += 1
            andm += bytes(mask_row - nbytes)
        hdr = struct.pack('<IIIHHIIIIII', 40, w, h * 2, 1, 32, 0,
                          len(xor) + len(andm), 0, 0, 0, 0)
        frames.append(bytes(hdr) + bytes(xor) + bytes(andm))

    count = len(frames)
    out = [struct.pack('<HHH', 0, 1, count)]
    offset = 6 + 16 * count
    for (size, _), blob in zip(sorted(images), frames):
        b = size if size < 256 else 0
        out.append(struct.pack('<BBBBHHII', b, b, 0, 0, 1, 32,
                               len(blob), offset))
        offset += len(blob)
    open(path, 'wb').write(b''.join(out) + b''.join(frames))


def _font(size, bold=False):
    base = 'C:/Windows/Fonts'
    name = 'segoeuib.ttf' if bold else 'segoeui.ttf'
    try:
        return ImageFont.truetype(f'{base}/{name}', size)
    except OSError:
        return ImageFont.load_default()


def wizard_banner(fg_full, w, h, path):
    """Inno-Seitenbild: Navy-Fläche, Icon oben, Name + Untertitel,
    Iris-Akzentbalken unten. Wird 3x gerendert und skaliert."""
    k = 3
    W, H = w * k, h * k
    bg = background().resize((W, H), Image.LANCZOS).convert('RGBA')
    isz = int(W * .56)
    bg.alpha_composite(fg_full.resize((isz, isz), Image.LANCZOS),
                       ((W - isz) // 2, int(H * .10)))
    d = ImageDraw.Draw(bg)
    ft = _font(int(W * .125), bold=True)
    fs = _font(int(W * .075))
    cy = int(H * .52)
    t = 'N-Connect'
    d.text((W // 2 - d.textlength(t, font=ft) // 2, cy), t,
           font=ft, fill=(238, 244, 248))
    cy += int(ft.size * 1.5)
    for line_txt in ('Switch · Switch 2 · Wii · Wii U',
                     'Controller für Windows'):
        d.text((W // 2 - d.textlength(line_txt, font=fs) // 2, cy),
               line_txt, font=fs, fill=(150, 170, 185))
        cy += int(fs.size * 1.45)
    bar_h = max(2, int(H * .013))
    bg.paste(iris_rgb(W).crop((0, 0, W, bar_h)), (0, H - bar_h))
    bg.convert('RGB').resize((w, h), Image.LANCZOS).save(
        path, format='BMP')


def main():
    bg = background()
    fg = foreground()
    full = Image.alpha_composite(bg, fg)

    # Windows-ICO (App + Tray): nur das Glyph, transparent —
    # wie bei SKZ Remote, im Tray soll kein Kasten kleben.
    win = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    wz = int(S * 1.18)
    win.paste(fg.resize((wz, wz), Image.LANCZOS),
              ((S - wz) // 2, (S - wz) // 2))

    # Master-PNG (512): dunkle Kachel + Glyph — für Android,
    # Kodi-Add-ons, Installer-Art und Website
    full.resize((512, 512), Image.LANCZOS).convert('RGB').save(
        OUT_SRC / 'N-Connect.png')
    full.resize((512, 512), Image.LANCZOS).convert('RGB').save(
        OUT_ART / 'N-Connect.png')
    full.resize((512, 512), Image.LANCZOS).convert('RGB').save(
        OUT_ART / 'icon_preview.png')

    win.save(OUT_SRC / 'N-Connect.ico', format='ICO',
             sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40),
                    (48, 48), (64, 64), (96, 96), (128, 128),
                    (256, 256)])
    write_bmp_ico([(z, win) for z in (16, 24, 32, 48, 64)],
                  OUT_ART / 'N-Connect.ico')

    # Setup-Seitenbilder
    tile = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    tz = int(S * 0.94)
    tile.paste(full.resize((tz, tz), Image.LANCZOS),
               ((S - tz) // 2, (S - tz) // 2))
    for pct, (bw_, bh_) in ((100, (164, 314)), (150, (246, 471)),
                            (200, (328, 628))):
        wizard_banner(fg, bw_, bh_, OUT_ART / f'wizard-{pct}.bmp')
    for pct, sz in ((100, 55), (150, 83), (200, 110)):
        tile.resize((sz, sz), Image.LANCZOS).convert('RGB').save(
            OUT_ART / f'wizard-small-{pct}.bmp', format='BMP')

    print('N-Connect-Icon geschrieben — danach android/make_icons.py')


if __name__ == '__main__':
    main()
