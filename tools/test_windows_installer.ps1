param([Parameter(Mandatory=$true)][string]$Msi, [Parameter(Mandatory=$true)][string]$PublishedPayload)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
$wix = Join-Path $nugetRoot 'wixtoolset.sdk\5.0.2\tools\net6.0\wix.dll'
if (-not (Test-Path -LiteralPath $wix)) { throw 'Restore the installer project to obtain its pinned WiX SDK first.' }
$work = Join-Path $taskRoot ('.local\installer-check-' + [guid]::NewGuid().ToString('N'))
$extracted = Join-Path $work 'extracted'
$payload = Join-Path $work 'payload'
$definition = Join-Path $work 'package.wxs'
New-Item -ItemType Directory -Path $work,$payload | Out-Null
# Read and extract cabinets only. Never execute MSI actions or register devices.
dotnet $wix msi decompile ([IO.Path]::GetFullPath($Msi)) -x $extracted -o $definition
if ($LASTEXITCODE -ne 0) { throw 'Could not read the MSI payload.' }
[xml]$document = Get-Content -LiteralPath $definition -Raw
$count = 0
foreach ($file in $document.SelectNodes('//*[local-name()="File"]')) {
    $parts = [Collections.Generic.List[string]]::new()
    $node = $file.ParentNode
    while ($node -and $node.GetAttribute('Id') -ne 'INSTALLFOLDER') {
        if ($node.LocalName -eq 'Directory') { $parts.Insert(0, $node.GetAttribute('Name')) }
        $node = $node.ParentNode
    }
    if (-not $node) { throw 'Installer file is outside the application directory.' }
    $parts.Add($file.GetAttribute('Name'))
    $relative = [IO.Path]::Combine($parts.ToArray())
    $target = [IO.Path]::GetFullPath((Join-Path $payload $relative))
    if (-not $target.StartsWith($payload + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe installer payload path.' }
    $source = Join-Path $extracted ('File\' + $file.GetAttribute('Id'))
    $original = Join-Path ([IO.Path]::GetFullPath($PublishedPayload)) $relative
    if ((Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $original).Hash) { throw "Installer payload differs from the tested publish: $relative" }
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $target
    $count++
}
$expected = @(Get-ChildItem -LiteralPath $PublishedPayload -Recurse -File).Count
if ($count -ne $expected) { throw "Installer has $count files; published payload has $expected." }
& (Join-Path $PSScriptRoot 'test_windows_package.ps1') -Payload $payload -Evidence (Join-Path $work 'screens')
Write-Host "MSI cabinet verification passed: $count files match; extracted app renders all screens. No installer actions executed."
