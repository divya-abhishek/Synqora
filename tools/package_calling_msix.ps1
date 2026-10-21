param([string]$Payload = 'dist/windows-portable', [string]$SdkBin = '')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$gates = Get-Content (Join-Path $taskRoot 'release-gates.json') -Raw | ConvertFrom-Json
$version = $gates.version
$source = (Resolve-Path -LiteralPath (Join-Path $taskRoot $Payload)).Path
$staging = Join-Path $taskRoot ('.local/msix-stage-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging -Force | Out-Null
Copy-Item -Path (Join-Path $source '*') -Destination $staging -Recurse
Copy-Item -LiteralPath (Join-Path $taskRoot 'installer/msix/AppxManifest.xml') -Destination $staging
$assetFolder = Join-Path $staging 'MsixAssets'; New-Item -ItemType Directory -Path $assetFolder | Out-Null
$env:SYNQORA_MSIX_ASSETS = $assetFolder
@'
from PIL import Image
from pathlib import Path
import os
source=Image.open('brand/synqora-app-icon.png').convert('RGBA')
for size,name in [(44,'Square44'),(150,'Square150'),(50,'StoreLogo')]:
 source.resize((size,size),Image.Resampling.LANCZOS).save(Path(os.environ['SYNQORA_MSIX_ASSETS'])/(name+'.png'))
'@ | python -
if ($LASTEXITCODE -ne 0) { throw 'MSIX icon export failed.' }
if (-not $SdkBin) {
    $candidates = @()
    $installed = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
    if (Test-Path -LiteralPath $installed) { $candidates += Get-ChildItem -Path (Join-Path $installed '*/x64') -Directory -ErrorAction SilentlyContinue }
    $restored = Join-Path $env:USERPROFILE '.nuget/packages/microsoft.windows.sdk.buildtools'
    if (Test-Path -LiteralPath $restored) { $candidates += Get-ChildItem -Path (Join-Path $restored '*/bin/*/x64') -Directory -ErrorAction SilentlyContinue }
    $selected = $candidates | Sort-Object FullName -Descending | Where-Object { (Test-Path -LiteralPath (Join-Path $_.FullName 'makeappx.exe')) -and (Test-Path -LiteralPath (Join-Path $_.FullName 'signtool.exe')) } | Select-Object -First 1
    if ($selected) { $SdkBin = $selected.FullName }
}
if (-not $SdkBin -or -not (Test-Path -LiteralPath (Join-Path $SdkBin 'makeappx.exe')) -or -not (Test-Path -LiteralPath (Join-Path $SdkBin 'signtool.exe'))) { throw 'Windows SDK MakeAppx and SignTool are required. Supply their x64 folder using -SdkBin.' }
$out = Join-Path $taskRoot "dist/development/$version"
New-Item -ItemType Directory -Path $out -Force | Out-Null
$msix = Join-Path $out "Synqora-$version-experimental-calling-x64.msix"
& (Join-Path $SdkBin 'makeappx.exe') pack /d $staging /p $msix /o
if ($LASTEXITCODE -ne 0) { throw 'MSIX manifest or block-map validation failed.' }
# Create a file-only development signer. Never import it into a certificate store.
$signing = Join-Path $taskRoot '.local/msix-signing'; New-Item -ItemType Directory -Path $signing -Force | Out-Null
$pfx = Join-Path $signing 'preview.pfx'
$cer = Join-Path $out "Synqora-$version-preview-signing.cer"
if (-not (Test-Path -LiteralPath $pfx)) {
    $rsa = [Security.Cryptography.RSA]::Create(3072)
    try {
        $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=Synqora Preview', $rsa, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
        $usage = [Security.Cryptography.OidCollection]::new(); $usage.Add([Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.3')) | Out-Null
        $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($usage, $true))
        $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new([Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature, $true))
        $certificate = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddMinutes(-5), [DateTimeOffset]::UtcNow.AddMonths(6))
        [IO.File]::WriteAllBytes($pfx, $certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx))
        $certificate.Dispose()
    } finally { $rsa.Dispose() }
}
$public = [Security.Cryptography.X509Certificates.X509Certificate2]::new($pfx)
try { [IO.File]::WriteAllBytes($cer, $public.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert)) } finally { $public.Dispose() }
& (Join-Path $SdkBin 'signtool.exe') sign /fd SHA256 /f $pfx $msix
if ($LASTEXITCODE -ne 0) { throw 'MSIX signing failed.' }
Write-Output 'Experimental MSIX signed with a file-only preview certificate. Nothing installed or trusted. Bluetooth hardware/capability acceptance is still required.'
Get-FileHash -LiteralPath $msix,$cer -Algorithm SHA256
