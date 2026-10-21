"""Check readable semantic text against every material endpoint in both themes."""
import json
from pathlib import Path

t = json.loads((Path(__file__).resolve().parents[1] / 'design/tokens.json').read_text())

def rgb(hex_value):
    return [int(hex_value[i:i + 2], 16) / 255 for i in (1, 3, 5)]

def luminance(c):
    v = [(s / 12.92 if s <= .04045 else ((s + .055) / 1.055) ** 2.4) for s in c]
    return sum(a * b for a, b in zip(v, [.2126, .7152, .0722]))

def contrast(a, b):
    a, b = sorted([luminance(a), luminance(b)])
    return (b + .05) / (a + .05)

results = []
for name, colors in t['themes'].items():
    minimum = 100
    for foreground in ['text', 'muted', 'accent', 'cyan']:
        for background in ['background', 'surface', 'solid', 'elevated', 'ambience']:
            ratio = contrast(rgb(colors[foreground]), rgb(colors[background]))
            minimum = min(minimum, ratio)
            assert ratio >= 4.5, f'{name} {foreground}/{background}: {ratio:.2f}:1'
    assert contrast(rgb(colors['onAccent']), rgb(colors['accent'])) >= 4.5
    results.append(f'{name}: worst semantic text endpoint {minimum:.2f}:1')
assert t['rendering']['waveformHz'] <= 30
assert t['spring']['dampingRatio'] >= .8
print('\n'.join(results))
