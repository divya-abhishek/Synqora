"""Smoke-test a signed, non-debuggable APK on a disposable emulator only."""
import argparse
import json
from pathlib import Path
import re
import subprocess
import time
import xml.etree.ElementTree as ET

p = argparse.ArgumentParser()
p.add_argument('--adb', default='adb')
p.add_argument('--apk', required=True)
p.add_argument('--output', required=True)
a = p.parse_args()

def adb(*args, check=True):
    return subprocess.run([a.adb, *args], check=check, capture_output=True).stdout.decode(errors='replace')

serial = adb('get-serialno').strip()
if not serial.startswith('emulator-'):
    raise SystemExit('Package smoke tests may reset only a disposable Android emulator.')

out = Path(a.output)
out.mkdir(parents=True, exist_ok=True)
adb('uninstall', 'com.synqora', check=False)
adb('install', '-r', str(Path(a.apk).resolve()))
package = adb('shell', 'dumpsys', 'package', 'com.synqora')
assert 'DEBUGGABLE' not in package, 'Release APK is debuggable'
version = re.search(r'versionName=([^\s]+)', package).group(1)
adb('shell', 'am', 'start', '-W', '-n', 'com.synqora/.MainActivity')

def drawn(text):
    for _ in range(12):
        adb('shell', 'uiautomator', 'dump', '/sdcard/synqora-release.xml')
        tree = ET.fromstring(adb('shell', 'cat', '/sdcard/synqora-release.xml'))
        if any(n.get('package') == 'com.synqora' and n.get('text') == text for n in tree.iter('node')):
            return tree
        time.sleep(.4)
    raise AssertionError(f'Release screen did not draw: {text}')

def tap(tree, label):
    targets = [n for n in tree.iter('node') if n.get('package') == 'com.synqora' and n.get('text') == label]
    assert targets, f'Release control not found: {label}'
    bounds = [list(map(int, re.findall(r'\d+', n.get('bounds', '')))) for n in targets]
    x1, y1, x2, y2 = max(bounds, key=lambda b: b[3])
    adb('shell', 'input', 'tap', str((x1+x2)//2), str((y1+y2)//2))

def capture(name):
    with (out / name).open('wb') as f:
        subprocess.run([a.adb, 'exec-out', 'screencap', '-p'], check=True, stdout=f)

home = drawn('Phone and computer. Together.')
capture('android-release-overview.png')
tap(home, 'Settings')
settings = drawn('Comfort & accessibility')
capture('android-release-settings.png')
tap(settings, 'Home')
drawn('Phone and computer. Together.')
(out / 'android-package-smoke.json').write_text(json.dumps(dict(version=version, debuggable=False, dashboard=True, settings=True, returnHome=True, emulatorOnly=True), indent=2))
print(f'Signed release {version}: startup, native screens and navigation passed; not debuggable.')
