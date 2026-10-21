"""Stage a pinned MS-PL SysVAD base with the reviewed Synqora changes.
The staged base is ignored build input; no downloaded driver binaries are used.
"""
import pathlib
import re
import shutil
import subprocess

root = pathlib.Path(__file__).resolve().parents[1]
revision = '4d4618400525f9b2ce66fa2e267f58a55043be40'
checkout = root / '.local/windows-driver-samples'
source = checkout / 'audio/sysvad'
if not source.exists():
    raise SystemExit('Fetch microsoft/Windows-driver-samples at the revision in native/audio/README.md first.')
actual = subprocess.check_output(['git', '-C', str(checkout), 'rev-parse', 'HEAD'], text=True).strip()
if actual != revision:
    raise SystemExit(f'Unreviewed SysVAD revision: {actual}')
target = root / 'native/audio/build/source'
target.mkdir(parents=True, exist_ok=True)
for path in source.rglob('*.h'):
    relative = path.relative_to(source)
    destination = target / relative
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(path, destination)
common = ['adapter', 'basetopo', 'common', 'hw', 'kshelper', 'savedata', 'tonegenerator']
endpoints = ['AudioModuleHelper', 'MiniportAudioEngineNode', 'MiniportStreamAudioEngineNode', 'mintopo', 'minwavert', 'minwavertstream', 'NewDelete', 'micarraytopo', 'speakertopo', 'speakerhptopo']
tablet = ['hdmitopo', 'micintopo', 'spdiftopo']
files = [f'{name}.cpp' for name in common] + [f'EndpointsCommon/{name}.cpp' for name in endpoints] + [f'TabletAudioSample/{name}.cpp' for name in tablet]
for relative in files:
    shutil.copy2(source / relative, target / relative)
shutil.copy2(checkout / 'LICENSE', root / 'native/audio/LICENSE.SysVAD.txt')

def edit(relative, before, after):
    path = target / relative
    text = path.read_text()
    if text.count(before) != 1:
        raise RuntimeError(f'Patch context changed: {relative}: {before[:60]}')
    path.write_text(text.replace(before, after))

edit('adapter.cpp', '#define PUT_GUIDS_HERE', '#include "Bridge.h"\n#define PUT_GUIDS_HERE')
edit('adapter.cpp', '    ReleaseRegistryStringBuffer();\n\n    if (DriverObject == NULL)', '    SynqoraAudioShutdown();\n    ReleaseRegistryStringBuffer();\n\n    if (DriverObject == NULL)')
edit('adapter.cpp', '    DriverObject->MajorFunction[IRP_MJ_PNP] = PnpHandler;', '    ntStatus = SynqoraAudioInitialize(DriverObject);\n    if (!NT_SUCCESS(ntStatus)) goto Done;\n    DriverObject->MajorFunction[IRP_MJ_PNP] = PnpHandler;')
edit('EndpointsCommon/minwavertstream.h', '    VOID WriteBytes', '    ULONGLONG m_SynqoraCursor;\n    VOID WriteBytes')
edit('EndpointsCommon/minwavertstream.cpp', '#include <sysvad.h>', '#include <sysvad.h>\n#include "Bridge.h"')
edit('EndpointsCommon/minwavertstream.cpp', '    m_bCapture = FALSE;', '    m_bCapture = FALSE;\n    m_SynqoraCursor = MAXULONGLONG;')
edit('EndpointsCommon/minwavertstream.cpp', 'm_ToneGenerator.GenerateSine(m_pDmaBuffer + bufferOffset, runWrite);', 'SynqoraAudioRead(m_pDmaBuffer + bufferOffset, runWrite, m_SynqoraCursor);')
edit('EndpointsCommon/minwavertstream.cpp', 'This function writes the audio buffer using a sine wave generator', 'Synqora modification: fill capture from bounded phone PCM, with silence on starvation')
edit('EndpointsCommon/minwavertstream.cpp', '// Write sine wave to buffer.', '// Fill capture from received phone PCM, or silence.')
stream = target / 'EndpointsCommon/minwavertstream.cpp'
text = stream.read_text()
position = text.index('VOID CMiniportWaveRTStream::WriteBytes')
before = '    // Normally this will loop no more than once for a single wrap, but if\n    // many bytes have been displaced then this may loops many times.'
if text[position:].count(before) != 2:
    raise RuntimeError('Unexpected capture/render loop patch context')
after = ('    // Bound catch-up after sleep/stalls to one physical DMA buffer.\n'
         '    if (ByteDisplacement > m_ulDmaBufferSize) {\n'
         '        const ULONG skipped = ByteDisplacement - m_ulDmaBufferSize;\n'
         '        bufferOffset = (bufferOffset + skipped) % m_ulDmaBufferSize;\n'
         '        ByteDisplacement = m_ulDmaBufferSize;\n'
         '    }')
stream.write_text(text[:position] + text[position:].replace(before, after, 1))

# The upstream audio-engine helper assumes optional sideband support is enabled.
# Strip those eight branches for this capture-only build, retaining their ordinary-device else paths.
engine = target / 'EndpointsCommon/MiniportAudioEngineNode.cpp'
text = engine.read_text()
count = 0
while True:
    match = re.search(r'    if \(IsSidebandDevice\(\).*\n    \{', text)
    if not match:
        break
    block = text.index('{', match.start())
    depth, finish = 1, block + 1
    while depth:
        depth += (text[finish] == '{') - (text[finish] == '}')
        finish += 1
    # Preserve the fallback body, including any validation it performs.
    tail = re.match(r'\s*else\s*\{', text[finish:])
    if tail:
        text = text[:match.start()] + '    {' + text[finish + tail.end():]
    else:
        text = text[:match.start()] + text[finish:]
    count += 1
if count != 8:
    raise RuntimeError(f'Unexpected sideband branch count {count}')
engine.write_text(text)

# Expose one capture endpoint, 48 kHz 16-bit mono. No demo speakers or extra arrays.
pairs = (source / 'TabletAudioSample/minipairs.h').read_text()
begin = pairs.index('static\nPHYSICALCONNECTIONTABLE MicInTopologyPhysicalConnections')
end = pairs.index('/*********************************************************************', begin)
declarations = pairs[pairs.index('NTSTATUS\nCreateMiniportWaveRTSYSVAD'):pairs.index('//\n// Describe buffer size constraints')]
(target / 'TabletAudioSample/minipairs.h').write_text(
    '// Copyright (c) Microsoft Corporation. All rights reserved.\n'
    '// Synqora reduced endpoint table; Microsoft SysVAD derivative (MS-PL).\n'
    '#pragma once\n#include "micintopo.h"\n#include "micintoptable.h"\n#include "micinwavtable.h"\n#include "micarraytopo.h"\n#include "micarray1toptable.h"\n'
    + declarations + pairs[begin:end] + '\nstatic PENDPOINT_MINIPAIR g_RenderEndpoints[] = { nullptr };\n#define g_cRenderEndpoints 0\n'
    'static PENDPOINT_MINIPAIR g_CaptureEndpoints[] = { &MicInMiniports };\n#define g_cCaptureEndpoints 1\n#define g_MaxMiniports 2\n')
wave = target / 'TabletAudioSample/micinwavtable.h'
text = wave.read_text()
formats = text.index('KSDATAFORMAT_WAVEFORMATEXTENSIBLE MicInPinSupportedDeviceFormats[]')
opening = text.index('{', formats)
last = text.index('    { // 7', opening)
text = text[:opening + 1] + '\n' + text[last:]
text = text.replace('MICIN_MIN_SAMPLE_RATE               8000', 'MICIN_MIN_SAMPLE_RATE               48000')
wave.write_text(text)
edit('TabletAudioSample/micintoptable.h', '0xd48deb08, 0xfd1c, 0x4d1e, 0xb8, 0x21, 0x90, 0x64, 0xd4, 0x9a, 0xe9, 0x6e',
     '0x9ce8e587, 0x6835, 0x44a9, 0x8e, 0x57, 0xc3, 0x9e, 0xe0, 0x1e, 0xa1, 0xa7')
shutil.copy2(root / 'native/audio/SynqoraMicrophone.inf', root / 'native/audio/build/SynqoraMicrophone.inf')
(root / 'native/audio/build/sources.rsp').write_text('\n'.join('"source/' + name + '"' for name in files))
print('Prepared pinned MS-PL SysVAD base, one PCM capture endpoint and original Synqora bridge.')
