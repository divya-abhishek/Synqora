param([string]$Exe = '', [switch]$Matrix, [string]$Output = 'previews')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
if (-not $Exe) { $Exe = Join-Path $taskRoot 'dist\windows-portable\Synqora.exe' }
$views = @(@{ Width=940; Height=760; Folder=$Output })
if ($Matrix) { $views += @{ Width=960; Height=760; Folder=(Join-Path $Output 'windows-laptop') }; $views += @{ Width=720; Height=720; Folder=(Join-Path $Output 'windows-compact') } }
foreach ($view in $views) {
    $destination = Join-Path $taskRoot $view.Folder
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    $marker = Join-Path $destination 'capture.complete'
    if (Test-Path -LiteralPath $marker) { Remove-Item -LiteralPath $marker }
    # Offscreen app rendering does not activate a desktop window or inject input.
    $parameters = @('--capture', ('"'+$destination+'"'), '--width', $view.Width, '--height', $view.Height, '--background-capture', '--no-measure', '--exit')
    $worker = Start-Process -FilePath $Exe -ArgumentList $parameters -WindowStyle Hidden -PassThru
    if (-not $worker.WaitForExit(45000)) { throw "Capture did not finish for $($view.Width)x$($view.Height)." }
    if ($worker.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $marker)) { throw 'Capture did not produce a successful completion marker.' }
}
