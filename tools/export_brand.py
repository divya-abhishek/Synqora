"""Export the user-supplied overlapping-squares/S logo without cropping it.
The supplied raster is authoritative; vectors are compact monochrome notification counterparts.
"""
from pathlib import Path
from PIL import Image
root = Path(__file__).resolve().parents[1]
source = Image.open(root / 'brand/synqora-source.png').convert('RGBA')
brand = root/'brand'; assets=root/'windows/Synqora/Assets'; res=root/'android/app/src/main/res'
def icon(size):
    image=Image.new('RGBA',(size,size),'#171717')
    image.alpha_composite(source.resize((size,size),Image.Resampling.LANCZOS))
    return image
# Preserve the supplied PNG's transparent canvas and uncropped safe margin on PC.
source.resize((256,256),Image.Resampling.LANCZOS).save(assets/'synqora-mark.png')
source.save(assets/'synqora.ico',sizes=[(s,s) for s in [16,24,32,48,64,128,256]])
icon(512).save(brand/'synqora-app-icon.png'); icon(512).save(brand/'synqora-mark.png')
# Equivalent small monochrome geometry; no legacy logo remains in notification assets.
paths=['M44 22 H73 Q85 22 85 34 V63 Q85 75 73 75 H44 Q32 75 32 63 V34 Q32 22 44 22 Z', 'M32 31 H62 Q75 31 75 44 V73 Q75 85 62 85 H32 Q20 85 20 73 V44 Q20 31 32 31 Z', 'M64 46 C63 35 44 36 42 45 C39 55 50 57 57 57 M54 54 L59 57 L54 60 M42 57 C43 69 64 68 65 58 C66 49 58 47 50 47 M53 44 L49 47 L53 50']
(brand/'synqora-mark.svg').write_text('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 108 108"><g fill="none" stroke="#FFF4D6" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">'+''.join(f'<path d="{p}"/>' for p in paths)+'</g></svg>')
for name in ['synqora_mark','synqora_launcher_mark']:
    (res/f'drawable/{name}.xml').unlink(missing_ok=True)
foreground = Image.new('RGBA', (432,432), (0,0,0,0))
foreground.alpha_composite(source.resize((232,232), Image.Resampling.LANCZOS), (100,100))
foreground.save(res/'drawable/synqora_launcher_mark.png')
# Notification small icons must be transparent monochrome Android vectors.
(res/'drawable/synqora_mark.xml').write_text('<vector xmlns:android="http://schemas.android.com/apk/res/android" android:width="32dp" android:height="32dp" android:viewportWidth="108" android:viewportHeight="108">'+''.join(f'<path android:pathData="{p}" android:fillColor="@android:color/transparent" android:strokeColor="#FFFFFFFF" android:strokeWidth="2.4" android:strokeLineCap="round" android:strokeLineJoin="round"/>' for p in paths)+'</vector>')
# In-app brand uses the actual supplied image; not the reduced notification vector.
source.resize((384,384), Image.Resampling.LANCZOS).save(res/'drawable/synqora_brand.png')
for density,size in [('mdpi',48),('hdpi',72),('xhdpi',96),('xxhdpi',144),('xxxhdpi',192)]: icon(size).save(res/f'mipmap-{density}/ic_launcher.png')
(res/'values/colors.xml').write_text('<resources><color name="synqora_icon_background">#171717</color></resources>')
print('Exported supplied logo into desktop, launcher and in-app assets.')
