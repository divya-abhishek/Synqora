"""Render Synqora's original paired-loop symbol into platform icon formats.
The glyph stays inside Android's adaptive-icon safe area.
"""
from pathlib import Path
import json
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parents[1]
brand = root / 'brand'
palette = json.loads((root / 'design/tokens.json').read_text())['themes']['dark']
paths = ['M54 40 H44 C36 40 30 46 30 54 V65 C30 73 36 79 44 79 H52 C60 79 66 73 66 65 V57', 'M54 68 H64 C72 68 78 62 78 54 V43 C78 35 72 29 64 29 H56 C48 29 42 35 42 43 V51']
(brand / 'synqora-mark.svg').write_text(f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="18 18 72 72"><g fill="none" stroke="{palette["accent"]}" stroke-width="8" stroke-linecap="round" stroke-linejoin="round">'+''.join(f'<path d="{p}"/>' for p in paths)+'</g></svg>\n')

def mark(size):
    scale = 8
    image = Image.new('RGBA', (size * scale, size * scale))
    draw = ImageDraw.Draw(image)
    lines = []
    points = []
    def curve(start, a, b, end):
        for step in range(1, 81):
            t = step / 80; u = 1-t
            points.append((u**3*start[0]+3*u*u*t*a[0]+3*u*t*t*b[0]+t**3*end[0], u**3*start[1]+3*u*u*t*a[1]+3*u*t*t*b[1]+t**3*end[1]))
    points = [(54,40),(44,40)]; curve(points[-1], (36,40),(30,46),(30,54)); points.append((30,65)); curve(points[-1], (30,73),(36,79),(44,79)); points.append((52,79)); curve(points[-1], (60,79),(66,73),(66,65)); points.append((66,57)); lines.append(points)
    points = [(54,68),(64,68)]; curve(points[-1], (72,68),(78,62),(78,54)); points.append((78,43)); curve(points[-1], (78,35),(72,29),(64,29)); points.append((56,29)); curve(points[-1], (48,29),(42,35),(42,43)); points.append((42,51)); lines.append(points)
    width = round(8/108*size*scale)
    for line in lines:
        line = [(x/108*size*scale, y/108*size*scale) for x,y in line]
        draw.line(line, fill=palette['accent'], width=width, joint='curve')
        for x,y in line: draw.ellipse((x-width/2,y-width/2,x+width/2,y+width/2),fill=palette['accent'])
    return image.resize((size,size), Image.Resampling.LANCZOS)

def icon(size):
    image = Image.new('RGBA', (size, size))
    ImageDraw.Draw(image).rounded_rectangle((0, 0, size - 1, size - 1), radius=size * .22, fill=palette['solid'])
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
old = res / 'drawable/synqora_mark.png'
if old.exists(): old.unlink()
vector_paths = ''.join(f'<path android:pathData="{p}" android:fillColor="@android:color/transparent" android:strokeColor="#FFFFFFFF" android:strokeWidth="8" android:strokeLineCap="round" android:strokeLineJoin="round"/>' for p in paths)
(res / 'drawable/synqora_mark.xml').write_text('<vector xmlns:android="http://schemas.android.com/apk/res/android" android:width="32dp" android:height="32dp" android:viewportWidth="72" android:viewportHeight="72"><group android:translateX="-18" android:translateY="-18">'+vector_paths+'</group></vector>\n')
(res / 'drawable/synqora_launcher_mark.xml').write_text('<vector xmlns:android="http://schemas.android.com/apk/res/android" android:width="108dp" android:height="108dp" android:viewportWidth="108" android:viewportHeight="108">'+vector_paths+'</vector>\n')
for density, size in [('mdpi', 48), ('hdpi', 72), ('xhdpi', 96), ('xxhdpi', 144), ('xxxhdpi', 192)]:
    directory = res / ('mipmap-' + density)
    directory.mkdir(exist_ok=True)
    icon(size).save(directory / 'ic_launcher.png')
(res / 'values/colors.xml').write_text(f'<resources><color name="synqora_icon_background">{palette["solid"]}</color></resources>\n')
alpha = mark(512).getchannel('A').getbbox()
assert alpha and min(alpha[:2]) >= 512*.22 and max(alpha[2:]) <= 512*.8, alpha
print('Exported paired-loop symbol, native vector and padded platform icons; safe area verified.')
