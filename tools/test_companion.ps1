param([string]$Adb = 'adb', [string]$Output = 'previews/companion')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
if ((& $Adb get-serialno) -notlike 'emulator-*') { throw 'Companion tests require a disposable emulator.' }
dotnet build (Join-Path $taskRoot 'tests/CompanionProbe') -c Release -v:q
if ($LASTEXITCODE -ne 0) { throw 'Probe build failed.' }
$folder = Join-Path $taskRoot '.local/companion-test'
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$stop = Join-Path $folder 'companion-stop'
if (Test-Path -LiteralPath $stop) { Remove-Item -LiteralPath $stop }
$invitePath = Join-Path $folder 'companion-invite.txt'
if (Test-Path -LiteralPath $invitePath) { Remove-Item -LiteralPath $invitePath }
$probe = Start-Process -FilePath (Join-Path $taskRoot 'tests/CompanionProbe/bin/Release/net8.0/CompanionProbe.exe') -ArgumentList ('"'+$folder+'"') -WindowStyle Hidden -PassThru
try {
    for ($i = 0; $i -lt 60 -and -not (Test-Path -LiteralPath $invitePath); $i++) { Start-Sleep -Milliseconds 100 }
    $invitation = [IO.File]::ReadAllText($invitePath)
    $port = [regex]::Match(([uri]$invitation).Query, '(?:\?|&)port=(\d+)').Groups[1].Value
    & $Adb reverse "tcp:$port" "tcp:$port"
    & $Adb uninstall com.synqora | Out-Null
    & $Adb install -r (Join-Path $taskRoot 'android/app/build/outputs/apk/debug/app-debug.apk')
    if ($LASTEXITCODE -ne 0) { throw 'Could not install debug APK on emulator.' }
    & $Adb install -r (Join-Path $taskRoot 'android/app/build/outputs/apk/androidTest/debug/app-debug-androidTest.apk')
    $env:SYNQORA_TEST_ADB = $Adb; $env:SYNQORA_TEST_INVITE = $invitePath
    python (Join-Path $PSScriptRoot 'push_pairing.py')
    if ($LASTEXITCODE -ne 0) { throw 'Could not prepare private test invitation.' }
    & $Adb logcat -c
    $result = & $Adb shell am instrument -w -r -e class com.synqora.CompanionIntegrationTest com.synqora.test/androidx.test.runner.AndroidJUnitRunner
    $result | Write-Output
    $destination = Join-Path $taskRoot $Output; New-Item -ItemType Directory -Path $destination -Force | Out-Null
    & $Adb logcat -d -s 'SynqoraCompanionTest:I' 'AndroidRuntime:E' '*:S' | Set-Content (Join-Path $destination 'android-companion-log.txt')
    if (($result -join "`n") -notmatch 'OK \(1 test\)') { throw 'Companion integration test failed.' }
    Start-Sleep -Milliseconds 800
    $evidence = Get-Content (Join-Path $folder 'companion-result.json') -Raw | ConvertFrom-Json
    if (-not $evidence.actionResult -or -not $evidence.disconnected -or $evidence.audio -le 0 -or $evidence.video -le 0) { throw 'Receiver did not confirm replies and capture teardown.' }
    if (($evidence.files | Where-Object Status -eq 'Complete').Count -lt 3 -or -not $evidence.galleryPage -or -not $evidence.galleryThumb -or -not $evidence.historySeen) { throw 'Files, gallery metadata/thumbnail/original and call history must all pass.' }
    $incoming = $evidence.files | Where-Object { $_.Incoming -and $_.Size -eq 1048737 } | Select-Object -First 1
    if ((Get-FileHash -LiteralPath $incoming.Path -Algorithm SHA256).Hash -ne $evidence.expectedHash) { throw 'Round-trip file hash mismatch.' }
    # Public evidence includes measurements only, never the invitation or personal notifications.
    $evidence.files | ForEach-Object { $_.Path = $null }
    $evidence | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $destination 'companion-integration.json')
    Write-Output 'PASS: files in both directions, notification reply, gallery, call history; disconnect stops capture and releases wake lock.'
} finally {
    [IO.File]::WriteAllText($stop, 'stop')
    if ($port) { & $Adb reverse --remove "tcp:$port" }
    $probe.WaitForExit(5000) | Out-Null
}
