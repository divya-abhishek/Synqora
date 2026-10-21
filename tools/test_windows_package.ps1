param([Parameter(Mandatory=$true)][string]$Payload, [string]$Evidence = '')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$payloadPath = (Resolve-Path -LiteralPath $Payload).Path
foreach ($required in @('Synqora.exe', 'Synqora.dll', 'Synqora.pri', 'Synqora.Camera.dll', 'Synqora.CameraCheck.exe', 'Microsoft.ui.xaml.dll', 'Microsoft.Graphics.Canvas.dll', 'Microsoft.Graphics.Canvas.Interop.dll', 'Licenses\Win2D.txt', 'Assets\synqora.ico', 'Assets\synqora-mark.png', 'Drivers\VBCABLE_Driver_Pack45.zip', 'docs\INSTALLATION.md')) {
    $path = Join-Path $payloadPath $required
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -eq 0) {
        throw "Published Windows payload is incomplete: $required"
    }
}
if (-not $Evidence) { $Evidence = Join-Path $taskRoot ('.local\package-startup-' + [guid]::NewGuid().ToString('N')) }
$evidencePath = [IO.Path]::GetFullPath($Evidence)
if (Test-Path -LiteralPath $evidencePath) { throw 'Use a new evidence directory so stale captures cannot pass the test.' }
New-Item -ItemType Directory -Path $evidencePath | Out-Null
$process = Start-Process -FilePath (Join-Path $payloadPath 'Synqora.exe') -ArgumentList @('--capture', ('"'+$evidencePath+'"'), '--background-capture', '--no-measure', '--exit') -WorkingDirectory $env:TEMP -WindowStyle Hidden -PassThru
try {
    if (-not $process.WaitForExit(45000)) { throw 'Published Windows app did not finish startup and screen rendering within 45 seconds.' }
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $evidencePath 'capture.complete'))) {
        throw "Published Windows app failed startup (exit $($process.ExitCode)). See LOCALAPPDATA\Synqora\Logs\startup.log."
    }
    foreach ($theme in @('dark')) {
        foreach ($screen in @('overview','inbox','files','photos','calls','microphone','webcam','touchpad','keyboard','settings')) {
            $image = Join-Path $evidencePath "windows-$theme-$screen.png"
            if (-not (Test-Path -LiteralPath $image) -or (Get-Item -LiteralPath $image).Length -lt 1024) { throw "Missing rendered screen: $theme $screen" }
        }
    }
    Write-Host 'Published Windows startup passed: 10 native screens rendered; exit 0; unrelated working directory.'
}
finally { if (-not $process.HasExited) { Stop-Process -Id $process.Id } }
