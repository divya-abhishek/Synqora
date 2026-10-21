param([switch]$Development, [switch]$SkipBuild, [string]$ExpectedSignerSha256)
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
$verificationOptions = @{}
if ($ExpectedSignerSha256) { $verificationOptions.ExpectedSignerSha256 = $ExpectedSignerSha256 }
$verification = & (Join-Path $PSScriptRoot 'verify_android_apk.ps1') -ApkPath $apk @verificationOptions
$version = $gates.version
if ($verification.versionName -ne $version) { throw "APK version $($verification.versionName) does not match release version $version. Rebuild before packaging." }
$destination = if ($Development) { Join-Path $taskRoot "dist\development\$version\Synqora-$version-dev-android.apk" } else { Join-Path $taskRoot "dist\Synqora-$version-android.apk" }
New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
Copy-Item -LiteralPath $apk -Destination $destination
Get-FileHash -LiteralPath $destination -Algorithm SHA256
