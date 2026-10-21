param([switch]$Development, [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$gates = Get-Content (Join-Path $taskRoot 'release-gates.json') -Raw | ConvertFrom-Json
if (-not $Development) {
    $requiredGates = @('virtualMicrophoneImplemented', 'virtualAudioProductionSigned', 'virtualCameraDeviceValidated', 'physicalPhoneStreamingValidated', 'streamingFrameTimeTargetMet', 'cleanMachineOfflineInstallValidated', 'upgradeAndUninstallValidated')
    $missingGates = @($requiredGates | Where-Object { $gates.$_ -ne $true })
    if ($missingGates.Count) { throw ('Production packaging is not ready: ' + ($missingGates -join ', ') + '. Use -Development for a review build.') }
}
if (-not $SkipBuild) {
    & (Join-Path $taskRoot 'android\gradlew.bat') -p (Join-Path $taskRoot 'android') assembleRelease --console=plain
    if ($LASTEXITCODE -ne 0) { throw 'Android release build failed.' }
}
$apk = Join-Path $taskRoot 'android\app\build\outputs\apk\release\app-release.apk'
if (-not (Test-Path -LiteralPath $apk)) { throw 'Signed APK missing.' }
$androidSdk = if ($env:ANDROID_HOME) { $env:ANDROID_HOME } elseif ($env:ANDROID_SDK_ROOT) { $env:ANDROID_SDK_ROOT } else { 'D:\Android\sdk' }
$signer = Get-ChildItem -LiteralPath (Join-Path $androidSdk 'build-tools') -Filter apksigner.bat -Recurse | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $signer) { throw 'Android build-tools apksigner is required to verify the APK.' }
& $signer.FullName verify --verbose $apk
if ($LASTEXITCODE -ne 0) { throw 'APK signature verification failed.' }
$version = $gates.version
$destination = if ($Development) { Join-Path $taskRoot "dist\development\$version\Synqora-$version-dev-android.apk" } else { Join-Path $taskRoot "dist\Synqora-$version-android.apk" }
New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
Copy-Item -LiteralPath $apk -Destination $destination
Get-FileHash -LiteralPath $destination -Algorithm SHA256
