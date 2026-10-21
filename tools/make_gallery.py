"""Build a portable gallery of actual native screenshots; no generated UI mockups."""
import html
import json
import argparse
import os
from pathlib import Path
from PIL import Image

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument('--folder', default='previews')
parser.add_argument('--version', default=json.loads((root / 'release-gates.json').read_text())['version'])
options = parser.parse_args()
folder = root / options.folder
items = []
for path in sorted(folder.rglob('*.png')):
    if not path.name.startswith(('windows-', 'android-')):
        continue
    with Image.open(path) as image:
        width, height = image.size
    relative = path.relative_to(folder).as_posix()
    platform = path.name.split('-')[0]
    theme = 'light' if '-light-' in path.name else 'dark' if '-dark-' in path.name else 'live / matrix'
    items.append(dict(file=relative, platform=platform, theme=theme, width=width, height=height))
(folder / 'screenshots.json').write_text(json.dumps(items, indent=2), encoding='utf-8')
cards = '\n'.join(
    f'<figure data-platform="{x["platform"]}" data-theme="{x["theme"]}"><button class="preview" aria-label="Enlarge {html.escape(x["file"])}"><img loading="lazy" src="{html.escape(x["file"])}" alt="Native {html.escape(x["file"])}"></button><figcaption>{html.escape(x["file"])}<small>{x["width"]} × {x["height"]} · native capture</small></figcaption></figure>'
    for x in items
)
page = '''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Synqora — native previews</title>
<style>body{margin:0;background:#151719;color:#eef0f1;font:15px/1.6 system-ui,sans-serif}main{max-width:1500px;margin:auto;padding:32px}h1{font-size:36px;margin:0}p{max-width:880px;color:#b5b9bd}a{color:#d4d7da}nav{display:flex;gap:12px;flex-wrap:wrap;margin:28px 0}label{display:grid;gap:5px}select,button{font:inherit;color:inherit;background:#2c2f32;border:1px solid #63686c;border-radius:12px;padding:10px}button{cursor:pointer}button:focus-visible,select:focus-visible{outline:3px solid #e3e5e7;outline-offset:3px}section{display:grid;grid-template-columns:repeat(auto-fit,minmax(290px,1fr));gap:24px}figure{margin:0;background:#202325;border:1px solid #42474b;border-radius:20px;overflow:hidden}figure[hidden]{display:none}.preview{padding:0;border:0;border-radius:0;width:100%;height:380px;display:block;background:#111315}.preview img{width:100%;height:100%;object-fit:contain}figcaption{padding:16px;overflow-wrap:anywhere}small{display:block;color:#b5b9bd}dialog{background:#151719;border:1px solid #858b90;border-radius:20px;padding:16px;max-width:94vw;max-height:94vh;color:#eef0f1}dialog::backdrop{background:#000c}dialog img{display:block;max-width:88vw;max-height:80vh;object-fit:contain}dialog button{margin-bottom:12px}@media(max-width:600px){main{padding:20px}h1{font-size:30px}}</style>
<main><h1>Synqora</h1><p>Synqora VERSION development preview. Actual native dark-mode screens and size-matrix captures. Physical-phone battery, Bluetooth calling and frame-time acceptance remain unfinished.</p><p><a href="VALIDATION_LINK">Validation coverage</a> · <a href="screenshots.json">Screenshot dimensions</a></p>
<nav aria-label="Screenshot filters"><label>Platform<select id="platform"><option value="all">All platforms</option><option value="windows">Windows</option><option value="android">Android</option></select></label><label>Theme / scenario<select id="theme"><option value="all">All scenarios</option><option value="dark">Dark</option><option value="live / matrix">Live / size matrix</option></select></label></nav><p id="count" aria-live="polite"></p><section>CARDS</section></main>
<dialog><button id="close">Close preview</button><img alt="Enlarged native screenshot"></dialog><script>const figures=[...document.querySelectorAll('figure')],platform=document.querySelector('#platform'),theme=document.querySelector('#theme'),dialog=document.querySelector('dialog');function filter(){let count=0;figures.forEach(f=>{f.hidden=!((platform.value==='all'||f.dataset.platform===platform.value)&&(theme.value==='all'||f.dataset.theme===theme.value));if(!f.hidden)count++});document.querySelector('#count').textContent=count+' native screenshots'}platform.onchange=theme.onchange=filter;filter();document.querySelectorAll('.preview').forEach(b=>b.onclick=()=>{const im=b.querySelector('img');dialog.querySelector('img').src=im.src;dialog.querySelector('img').alt=im.alt;dialog.showModal()});document.querySelector('#close').onclick=()=>dialog.close();</script></html>'''
page = page.replace('VERSION', html.escape(options.version)).replace('VALIDATION_LINK', Path(os.path.relpath(root / 'docs/VALIDATION.md', folder)).as_posix())
(folder / 'index.html').write_text(page.replace('CARDS', cards), encoding='utf-8')
print(f'Gallery contains {len(items)} native screenshots.')
