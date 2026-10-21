param([switch]$SkipPublish, [switch]$Development)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$gates = Get-Content (Join-Path $taskRoot 'release-gates.json') -Raw | ConvertFrom-Json
if (-not $Development) {
    $requiredGates = @('virtualMicrophoneImplemented', 'virtualAudioProductionSigned', 'virtualCameraImplemented', 'virtualCameraDeviceValidated', 'physicalPhoneStreamingValidated', 'streamingFrameTimeTargetMet', 'cleanMachineOfflineInstallValidated', 'upgradeAndUninstallValidated')
    $missingGates = @($requiredGates | Where-Object { $gates.$_ -ne $true })
    if ($missingGates.Count) { throw ('Production packaging is not ready: ' + ($missingGates -join ', ') + '. Use -Development for a clearly labeled review build.') }
}
$artifactFolder = if ($Development) { Join-Path $taskRoot 'dist\development\0.3.0' } else { Join-Path $taskRoot 'dist' }
$artifactName = if ($Development) { 'Synqora-0.3.0-dev-win-x64' } else { 'Synqora-0.3.0-win-x64' }
if (-not (Test-Path -LiteralPath (Join-Path $taskRoot 'native\camera\build\Synqora.Camera.dll'))) { throw 'Build the native camera source with tools/build_camera.cmd before packaging.' }
$publishFolder = Join-Path $taskRoot 'dist\windows-portable'
if (-not $SkipPublish) {
    dotnet publish (Join-Path $taskRoot 'windows\Synqora\Synqora.csproj') -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o $publishFolder
    if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed.' }
}
$localFolder = Join-Path $taskRoot '.local'
New-Item -ItemType Directory -Path $localFolder -Force | Out-Null
$vcArchive = Join-Path $localFolder 'Microsoft.VCLibs.x64.14.00.Desktop.appx'
if (-not (Test-Path -LiteralPath $vcArchive)) {
    Invoke-WebRequest 'https://aka.ms/Microsoft.VCLibs.x64.14.00.Desktop.appx' -OutFile $vcArchive
}
$vcFolder = Join-Path $localFolder 'vclibs'
if (-not (Test-Path -LiteralPath $vcFolder)) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($vcArchive, $vcFolder)
}
Get-ChildItem -LiteralPath $vcFolder -Filter '*.dll' | Copy-Item -Destination $publishFolder
Copy-Item (Join-Path $taskRoot 'README.md') -Destination (Join-Path $publishFolder 'README.md')
Copy-Item (Join-Path $taskRoot 'THIRD-PARTY-NOTICES.md') -Destination $publishFolder
# Redistribute the original vendor archive unchanged. Its installer is not run by
# this MSI. The app explains the separate vendor setup, attribution and donationware.
$microphoneArchive = Join-Path $localFolder 'vbcable\VBCABLE_Driver_Pack45.zip'
if ((Get-FileHash -LiteralPath $microphoneArchive -Algorithm SHA256).Hash -ne 'B950E39F01AF1D04EA623C8F6D8EB9B6EA5C477C637295FABF20631C85116BFB') { throw 'The original VB-CABLE package hash does not match the reviewed vendor archive.' }
$driverFolder = Join-Path $publishFolder 'Drivers'
New-Item -ItemType Directory -Path $driverFolder -Force | Out-Null
Copy-Item -LiteralPath $microphoneArchive -Destination $driverFolder
# Test the published payload, not the build directory. Missing merged WinUI
# resources only surfaced after deployment in 0.1.0.
& (Join-Path $PSScriptRoot 'test_windows_package.ps1') -Payload $publishFolder
# Harvested payload changes are not reliably tracked by WiX's incremental target.
# Rebind the MSI so fresh binaries/notices cannot be paired with an older cabinet.
dotnet build (Join-Path $taskRoot 'installer\Synqora.Setup.wixproj') -t:Rebuild -c Release -o $artifactFolder "-p:OutputName=$artifactName"
if ($LASTEXITCODE -ne 0) { throw 'MSI build failed.' }
& (Join-Path $PSScriptRoot 'test_windows_installer.ps1') -Msi (Join-Path $artifactFolder ($artifactName + '.msi')) -PublishedPayload $publishFolder
$zip = Join-Path $artifactFolder ($artifactName + '-portable.zip')
Compress-Archive -Path (Join-Path $publishFolder '*') -DestinationPath $zip -Force
Get-FileHash (Join-Path $artifactFolder ($artifactName + '.msi')), $zip -Algorithm SHA256
