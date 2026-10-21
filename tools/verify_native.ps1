$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$probeFolder = Join-Path $taskRoot '.local\camera-probe'
& (Join-Path $taskRoot 'native\camera\build\CameraProbe.exe') $probeFolder
if ($LASTEXITCODE -ne 0) { throw 'Isolated camera source checks failed.' }
& (Join-Path $taskRoot 'native\audio\build\RingProbe.exe')
if ($LASTEXITCODE -ne 0) { throw 'Audio ring checks failed.' }
$infVerifier = Join-Path $taskRoot '.local\wdk\package\c\tools\10.0.26100.0\x64\infverif.exe'
$audioInf = Join-Path $taskRoot 'native\audio\SynqoraMicrophone.inf'
& $infVerifier /u $audioInf
if ($LASTEXITCODE -ne 0) { throw 'Universal INF check failed.' }
& $infVerifier /w $audioInf
if ($LASTEXITCODE -ne 0) { throw 'Windows INF check failed.' }
$signature = Get-AuthenticodeSignature -LiteralPath (Join-Path $taskRoot 'native\audio\build\SynqoraMicrophone.sys')
$result = [ordered]@{
    scenario = 'Isolated native camera COM/source reader and user-mode PCM ring; no device installation'
    cameraSourcePassed = $true
    pcmRingPassed = $true
    infUniversalPassed = $true
    infWindowsPassed = $true
    audioDriverSignature = $signature.Status.ToString()
    kernelEndpointAcceptance = 'Not performed'
    cameraSystemEnumeration = 'Not performed; temporary registration test blocked by policy'
}
$result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskRoot 'previews\native-source-checks.json')
