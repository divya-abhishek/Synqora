param([string]$Adb = 'adb', [switch]$PerformanceOnly, [switch]$NoBuild, [string]$Output = 'previews')
$ErrorActionPreference = 'Stop'
$testSerial = (& $Adb get-serialno).Trim()
if ($testSerial -notlike 'emulator-*') { throw 'Run backend fixtures only on a disposable Android emulator.' }
$taskRoot = Split-Path -Parent $PSScriptRoot
$localFolder = Join-Path $taskRoot '.local'
New-Item -ItemType Directory -Path $localFolder -Force | Out-Null
if (-not $NoBuild) {
    dotnet build (Join-Path $taskRoot 'tests\BackendProbe\BackendProbe.csproj') -c Release -v:q
    if ($LASTEXITCODE -ne 0) { throw 'Probe build failed.' }
}
$stopFile = Join-Path $localFolder 'backend-stop'
if (Test-Path -LiteralPath $stopFile) { Remove-Item -LiteralPath $stopFile }
$invitePath = Join-Path $localFolder 'backend-invite.txt'
if (Test-Path -LiteralPath $invitePath) { Remove-Item -LiteralPath $invitePath }
$probe = Join-Path $taskRoot 'tests\BackendProbe\bin\Release\net8.0\BackendProbe.exe'
$hostProcess = Start-Process -FilePath $probe -ArgumentList '--serve', ('"'+$localFolder+'"') -WindowStyle Hidden -PassThru
try {
    if ($PerformanceOnly) { & $Adb shell wm size 1080x2400; & $Adb shell wm density 420 }
    for ($i=0; $i -lt 50; $i++) { if (Test-Path -LiteralPath $invitePath) { break }; Start-Sleep -Milliseconds 100 }
    $invite = [IO.File]::ReadAllText($invitePath)
    $address = [Uri]$invite
    $port = [regex]::Match($address.Query, '(?:\?|&)port=(\d+)').Groups[1].Value
    & $Adb reverse "tcp:$port" "tcp:$port"
    & $Adb install -r (Join-Path $taskRoot 'android\app\build\outputs\apk\debug\app-debug.apk')
    # Stream the secret into app-private test storage; never put it in command arguments/logs.
    $env:SYNQORA_TEST_ADB = $Adb
    $env:SYNQORA_TEST_INVITE = $invitePath
    python (Join-Path $PSScriptRoot 'push_pairing.py')
    if ($LASTEXITCODE -ne 0) { throw 'Could not prepare private pairing test storage.' }
    $testClass = if ($PerformanceOnly) { 'com.synqora.StreamingFrameTest' } else { 'com.synqora.BackendIntegrationTest' }
    if ($NoBuild) {
        # Run already-built tests without a Gradle/compiler process competing for memory.
        & $Adb install -r (Join-Path $taskRoot 'android\app\build\outputs\apk\androidTest\debug\app-debug-androidTest.apk')
        if ($LASTEXITCODE -ne 0) { throw 'Could not install prebuilt instrumentation.' }
        & $Adb logcat -c
        $instrumentation = & $Adb shell am instrument -w -r -e class $testClass 'com.synqora.test/androidx.test.runner.AndroidJUnitRunner'
        $instrumentation | Write-Output
        if ($LASTEXITCODE -ne 0 -or ($instrumentation -join "`n") -notmatch 'OK \(1 test\)') { throw 'Prebuilt Android integration test failed.' }
    } else {
        & (Join-Path $taskRoot 'android\gradlew.bat') -p (Join-Path $taskRoot 'android') connectedDebugAndroidTest "-Pandroid.testInstrumentationRunnerArguments.class=$testClass" --console=plain
        if ($LASTEXITCODE -ne 0) { throw 'Android backend integration test failed.' }
    }
    Start-Sleep -Seconds 2
    $result = Get-Content (Join-Path $localFolder 'backend-result.json') -Raw | ConvertFrom-Json
    if ($result.AudioBytes -le 0 -or $result.VideoFrames -le 0) { throw 'Receiver did not confirm simultaneous media.' }
    if (-not $PerformanceOnly -and ($result.Moves -le 0 -or $result.Scrolls -le 0 -or $result.Clicks -le 0 -or $result.inputDocument -notlike '*Synqora backend test*' -or $result.Keys -notcontains '0:enter' -or $result.Keys -notcontains '0:tab')) { throw 'Receiver measurements did not confirm every tested feature.' }
    $outputPath = Join-Path $taskRoot $Output
    New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
    $evidence = Join-Path $outputPath $(if ($PerformanceOnly) { 'backend-realtime-perf.json' } else { 'backend-integration.json' })
    Copy-Item (Join-Path $localFolder 'backend-result.json') -Destination $evidence
    $frameLog = if ($NoBuild) { & $Adb logcat -d -v threadtime -s 'SynqoraFrameTimes:I' '*:S' } else {
        Get-ChildItem (Join-Path $taskRoot 'android\app\build\outputs\androidTest-results\connected\debug') -Recurse -Filter 'logcat*' | Get-Content
    }
    $timings = $frameLog | Select-String 'SynqoraFrameTimes' | ForEach-Object { $_.Line -replace '^.*SynqoraFrameTimes: ', '' }
    if ($timings) { $timings | Set-Content (Join-Path $outputPath $(if ($PerformanceOnly) { 'android-streaming-gfxinfo-realtime.txt' } else { 'android-streaming-gfxinfo.txt' })) }
    if ($PerformanceOnly) { Write-Output 'Real Android input/frame-clock workflow completed during measured simultaneous streaming.' }
    else { Write-Output 'Real Android capture, pinned TLS transport, video, audio, raw pointer, click, live IME typing and key delivery passed against the isolated receiver.' }
} finally {
    if ($PerformanceOnly) { & $Adb shell wm size reset; & $Adb shell wm density reset }
    [IO.File]::WriteAllText($stopFile, 'stop')
    if ($port) { & $Adb reverse --remove "tcp:$port" }
    $hostProcess.WaitForExit(10000) | Out-Null
}
