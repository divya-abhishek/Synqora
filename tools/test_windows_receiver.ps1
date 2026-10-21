param([string]$Adb = 'adb', [string]$Output = 'previews')
$ErrorActionPreference = 'Stop'
$testSerial = (& $Adb get-serialno).Trim()
if ($testSerial -notlike 'emulator-*') { throw 'Run receiver fixtures only on a disposable Android emulator.' }
$taskRoot = Split-Path -Parent $PSScriptRoot
$folder = Join-Path $taskRoot '.local\native-receiver'
New-Item -ItemType Directory -Path $folder -Force | Out-Null
foreach ($name in @('backend-invite.txt', 'windows-receiver-metrics.json', 'windows-receiver-error.txt', 'receiver-stage.txt', 'windows-live-webcam.png', 'windows-live-microphone.png')) {
    $old = Join-Path $folder $name
    if (Test-Path -LiteralPath $old) { Remove-Item -LiteralPath $old }
}
dotnet build (Join-Path $taskRoot 'windows\Synqora\Synqora.csproj') -c Release -v:q
if ($LASTEXITCODE -ne 0) { throw 'WinUI build failed.' }
$exe = Join-Path $taskRoot 'windows\Synqora\bin\Release\net8.0-windows10.0.26100.0\win-x64\Synqora.exe'
$worker = Start-Process -FilePath $exe -ArgumentList '--receiver-test', ('"'+$folder+'"'), '--background-capture' -WindowStyle Hidden -PassThru
try {
    $invitePath = Join-Path $folder 'backend-invite.txt'
    for ($i=0; $i -lt 100; $i++) { if (Test-Path -LiteralPath $invitePath) { break }; Start-Sleep -Milliseconds 100 }
    $address = [Uri][IO.File]::ReadAllText($invitePath)
    $port = [regex]::Match($address.Query, '(?:\?|&)port=(\d+)').Groups[1].Value
    & $Adb reverse "tcp:$port" "tcp:$port"
    & $Adb install -r (Join-Path $taskRoot 'android\app\build\outputs\apk\debug\app-debug.apk')
    $env:SYNQORA_TEST_ADB = $Adb; $env:SYNQORA_TEST_INVITE = $invitePath
    python (Join-Path $PSScriptRoot 'push_pairing.py')
    if ($LASTEXITCODE -ne 0) { throw 'Could not prepare private pairing test storage.' }
    & (Join-Path $taskRoot 'android\gradlew.bat') -p (Join-Path $taskRoot 'android') connectedDebugAndroidTest '-Pandroid.testInstrumentationRunnerArguments.class=com.synqora.WindowsReceiverTest' --console=plain
    if ($LASTEXITCODE -ne 0) { throw 'Native Windows receiver integration test failed.' }
    if (-not $worker.WaitForExit(10000)) { throw 'Native receiver test did not finish.' }
    if ($worker.ExitCode -ne 0) { throw "Native receiver process failed ($($worker.ExitCode))." }
    if (Test-Path (Join-Path $folder 'windows-receiver-error.txt')) { throw (Get-Content (Join-Path $folder 'windows-receiver-error.txt') -Raw) }
    $metricsPath = Join-Path $folder 'windows-receiver-metrics.json'
    $result = Get-Content $metricsPath -Raw | ConvertFrom-Json
    if ($result.decodedFrames -lt 8 -or $result.AudioBytes -le 0 -or $result.frameError -or $result.cameraBridgeError -or $result.nativeSource.Width -ne 1280 -or $result.nativeSource.MaximumLuma -le $result.nativeSource.MinimumLuma) { throw 'Native receiver metrics did not confirm the UI and independent camera source paths.' }
    $outputPath = Join-Path $taskRoot $Output
    New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
    Copy-Item $metricsPath -Destination $outputPath
    Copy-Item (Join-Path $folder 'windows-live-*.png') -Destination $outputPath
    Write-Output 'Android media reached the real native Windows receiver, decoder, waveform and camera surfaces.'
} finally { if ($port) { & $Adb reverse --remove "tcp:$port" } }
