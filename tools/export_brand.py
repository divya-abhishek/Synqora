"""Render the original Veil vector mark into platform icon formats.
The glyph stays inside Android's adaptive-icon safe area.
"""
from pathlib import Path
import json
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parents[1]
brand = root / 'brand'
palette = json.loads((root / 'design/tokens.json').read_text())['themes']['dark']
path = 'M 66 36 C 60 29 48 28 40 35 C 30 44 38 51 48 55 L 60 60 C 72 66 64 79 53 79 C 45 79 39 75 35 70'
(brand / 'synqora-mark.svg').write_text(f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 108 108"><path d="{path}" fill="none" stroke="{palette["accent"]}" stroke-width="7.5" stroke-linecap="round" stroke-linejoin="round"/></svg>\n')

def mark(size):
    scale = 8
    image = Image.new('RGBA', (size * scale, size * scale))
    draw = ImageDraw.Draw(image)
    points = [(66, 36)]
    def curve(start, a, b, end):
        for step in range(1, 81):
            t = step / 80; u = 1-t
            points.append((u**3*start[0]+3*u*u*t*a[0]+3*u*t*t*b[0]+t**3*end[0], u**3*start[1]+3*u*u*t*a[1]+3*u*t*t*b[1]+t**3*end[1]))
    curve(points[-1], (60,29), (48,28), (40,35))
    curve(points[-1], (30,44), (38,51), (48,55))
    points.append((60,60))
    curve(points[-1], (72,66), (64,79), (53,79))
    curve(points[-1], (45,79), (39,75), (35,70))
    points = [(x/108*size*scale, y/108*size*scale) for x,y in points]
    width = round(7.5/108*size*scale)
    draw.line(points, fill=palette['accent'], width=width, joint='curve')
    for x,y in (points[0], points[-1]): draw.ellipse((x-width/2,y-width/2,x+width/2,y+width/2), fill=palette['accent'])
    return image.resize((size,size), Image.Resampling.LANCZOS)

def icon(size):
    image = Image.new('RGBA', (size, size))
    ImageDraw.Draw(image).rounded_rectangle((0, 0, size - 1, size - 1), radius=size * .22, fill=palette['solid'], outline=palette['border'], width=max(1,size//128))
    image.alpha_composite(mark(size))
    return image

assets = root / 'windows/Synqora/Assets'
assets.mkdir(parents=True, exist_ok=True)
mark(256).save(assets / 'synqora-mark.png')
icon(256).save(assets / 'synqora.ico', sizes=[(s, s) for s in [16, 24, 32, 48, 64, 128, 256]])
icon(512).save(brand / 'synqora-app-icon.png')
mark(512).save(brand / 'synqora-mark.png')
res = root / 'android/app/src/main/res'
(res / 'drawable').mkdir(parents=True, exist_ok=True)
mark(512).save(res / 'drawable/synqora_mark.png')
for density, size in [('mdpi', 48), ('hdpi', 72), ('xhdpi', 96), ('xxhdpi', 144), ('xxxhdpi', 192)]:
    directory = res / ('mipmap-' + density)
    directory.mkdir(exist_ok=True)
    icon(size).save(directory / 'ic_launcher.png')
(res / 'values/colors.xml').write_text(f'<resources><color name="synqora_icon_background">{palette["solid"]}</color></resources>\n')
alpha = mark(512).getchannel('A').getbbox()
assert alpha and min(alpha[:2]) >= 512*.22 and max(alpha[2:]) <= 512*.8, alpha
print('Exported Veil vector mark and padded platform icons; safe area verified.')
