param([string]$Exe = '', [switch]$Matrix, [string]$Output = 'previews')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
if (-not $Exe) { $Exe = Join-Path $taskRoot 'dist\windows-portable\Synqora.exe' }
$views = @(@{ Width=1080; Height=820; Folder=$Output })
if ($Matrix) { $views += @{ Width=1920; Height=1080; Folder=(Join-Path $Output 'windows-fullhd') }; $views += @{ Width=2560; Height=1440; Folder=(Join-Path $Output 'windows-qhd') }; $views += @{ Width=720; Height=720; Folder=(Join-Path $Output 'windows-compact') } }
foreach ($view in $views) {
    $destination = Join-Path $taskRoot $view.Folder
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    $marker = Join-Path $destination 'capture.complete'
    if (Test-Path -LiteralPath $marker) { Remove-Item -LiteralPath $marker }
    # Offscreen app rendering does not activate a desktop window or inject input.
    $parameters = @('--capture', ('"'+$destination+'"'), '--width', $view.Width, '--height', $view.Height, '--background-capture', '--no-measure', '--resize-test', '--exit')
    $worker = Start-Process -FilePath $Exe -ArgumentList $parameters -WindowStyle Hidden -PassThru
    if (-not $worker.WaitForExit(60000)) { throw "Capture did not finish for $($view.Width)x$($view.Height)." }
    if ($worker.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $marker)) { throw 'Capture did not produce a successful completion marker.' }
}
