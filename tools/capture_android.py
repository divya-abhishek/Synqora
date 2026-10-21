"""Native emulator screenshots, viewport matrix, and actual gfxinfo measurements.
Requires a running Android emulator and installed debug APK. No fixture telemetry.
"""
import argparse
import pathlib
import subprocess
import time
import re
import xml.etree.ElementTree as ET

p = argparse.ArgumentParser()
p.add_argument('--adb', default='adb')
p.add_argument('--matrix', action='store_true')
p.add_argument('--measure', action='store_true', help='Measure navigation separately from other host build/capture work')
p.add_argument('--out', default='previews', help='Keep work-in-progress evidence separate from published builds')
p.add_argument('--screen', action='append', choices=['Overview', 'Microphone', 'Webcam', 'Touchpad', 'Keyboard', 'Settings'], help='Refresh selected screens after a focused UI change')
a = p.parse_args()
root = pathlib.Path(__file__).resolve().parents[1]
out = root / a.out
out.mkdir(exist_ok=True, parents=True)
screens = a.screen or ['Overview', 'Microphone', 'Webcam', 'Touchpad', 'Keyboard', 'Settings']

def adb(*args):
    return subprocess.run([a.adb, *args], check=True, capture_output=True).stdout.decode(errors='replace')

def launch(screen, theme, *extra):
    adb('shell', 'am', 'force-stop', 'com.synqora')
    adb('shell', 'am', 'start', '-W', '-n', 'com.synqora/.MainActivity', '-e', 'screen', screen, '-e', 'theme', theme, *extra)
    # Activity launch completion precedes Compose's first drawn frame on cold starts.
    # Require app semantics before capturing; a splash image is not a screen preview.
    for _ in range(12):
        time.sleep(.5)
        adb('shell', 'uiautomator', 'dump', '/sdcard/synqora-capture.xml')
        hierarchy = adb('shell', 'cat', '/sdcard/synqora-capture.xml')
        expected = {
            'Overview': 'Phone and computer. Together.',
            'Microphone': 'Phone audio, ready for your PC.',
            'Webcam': 'Camera off',
            'Touchpad': 'One finger to move. Two fingers to scroll.',
            'Keyboard': 'Type here. It appears on your PC immediately.',
            'Settings': 'Make Synqora feel right for you.',
        }[screen]
        # Feature titles also occur on Home tiles; require page-specific content.
        nodes = ET.fromstring(hierarchy).iter('node')
        drawn = any(node.get('package') == 'com.synqora' and node.get('text') == expected for node in nodes)
        if drawn and 'Connection details' in hierarchy:
            time.sleep(.4)
            return
    raise RuntimeError(f'{screen} did not draw its app controls in time')

def capture(name):
    with (out / (name + '.png')).open('wb') as f:
        subprocess.run([a.adb, 'exec-out', 'screencap', '-p'], stdout=f, check=True)

try:
    adb('shell', 'wm', 'size', '1080x2400')
    adb('shell', 'wm', 'density', '420')
    launch('Overview', 'Dark', '--ez', 'reset_preferences', 'true')
    for theme in ['Dark', 'Light']:
        for screen in screens:
            launch(screen, theme)
            capture(f'android-{theme.lower()}-{screen.lower()}')
    if a.matrix:
        for size, density, label in [('720x1600', '320', 'small'), ('1920x1080', '360', 'landscape')]:
            adb('shell', 'wm', 'size', size)
            adb('shell', 'wm', 'density', density)
            for screen in screens:
                launch(screen, 'Dark')
                capture(f'android-{label}-{screen.lower()}')
        adb('shell', 'wm', 'size', '1080x2400')
        adb('shell', 'wm', 'density', '420')
        adb('shell', 'settings', 'put', 'system', 'font_scale', '1.6')
        launch('Settings', 'Light')
        capture('android-large-text-settings')
        adb('shell', 'settings', 'put', 'system', 'font_scale', '1.0')
    launch('Overview', 'Dark', '-e', 'effects', 'Economy')
    capture('android-solid-fallback-overview')
    launch('Overview', 'Dark', '-e', 'effects', 'Balanced')
    if a.measure:
        # Resolve the two actual navigation targets rather than keeping coordinates
        # for a previous layout. The streaming benchmark is test_backend -PerformanceOnly.
        adb('shell', 'uiautomator', 'dump', '/sdcard/synqora-capture.xml')
        hierarchy = ET.fromstring(adb('shell', 'cat', '/sdcard/synqora-capture.xml'))
        targets = []
        for label in ['Settings', 'Home']:
            matches = [node for node in hierarchy.iter('node') if node.get('text') == label]
            bounds = [list(map(int, re.findall(r'\d+', node.get('bounds', '')))) for node in matches]
            x1, y1, x2, y2 = max((b for b in bounds if len(b) == 4), key=lambda b: b[3])
            targets.append((str((x1 + x2) // 2), str((y1 + y2) // 2)))
        adb('shell', 'dumpsys', 'gfxinfo', 'com.synqora', 'reset')
        for i in range(24):
            adb('shell', 'input', 'tap', *targets[i % 2])
            time.sleep(.32)
        (out / 'android-gfxinfo.txt').write_text(adb('shell', 'dumpsys', 'gfxinfo', 'com.synqora', 'framestats'))
    print('Native Android captures and navigation frame samples saved to previews/.', flush=True)
finally:
    adb('shell', 'settings', 'put', 'system', 'font_scale', '1.0')
    adb('shell', 'wm', 'size', 'reset')
    adb('shell', 'wm', 'density', 'reset')
    adb('shell', 'am', 'force-stop', 'com.synqora')
