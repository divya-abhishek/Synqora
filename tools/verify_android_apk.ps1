param(
    [Parameter(Mandatory = $true)][string]$ApkPath,
    [string]$ExpectedSignerSha256 = 'd8c22f09ce5aec5136c6d1f1ea32915435226bbdbace8cc44abbcdb9fc3a2c86',
    [string]$AndroidSdk,
    [string]$ReportPath
)
$ErrorActionPreference = 'Stop'

# Public release identity only. This script never reads a keystore or its passwords.
if ($ExpectedSignerSha256 -notmatch '^[a-fA-F0-9]{64}$') { throw 'ExpectedSignerSha256 must be a SHA-256 certificate fingerprint.' }
$resolvedApk = (Resolve-Path -LiteralPath $ApkPath).Path
if (-not $AndroidSdk) {
    $AndroidSdk = if ($env:ANDROID_HOME) { $env:ANDROID_HOME } elseif ($env:ANDROID_SDK_ROOT) { $env:ANDROID_SDK_ROOT } else { 'D:\Android\sdk' }
}
$buildTools = Get-ChildItem -LiteralPath (Join-Path $AndroidSdk 'build-tools') -Directory |
    Where-Object { $_.Name -match '^\d+\.\d+\.\d+$' } |
    Sort-Object { [version]$_.Name } -Descending |
    Where-Object { (Test-Path -LiteralPath (Join-Path $_.FullName 'apksigner.bat')) -and (Test-Path -LiteralPath (Join-Path $_.FullName 'aapt2.exe')) } |
    Select-Object -First 1
if (-not $buildTools) { throw 'Android SDK build-tools with apksigner and aapt2 are required.' }

$signatureOutput = (& (Join-Path $buildTools.FullName 'apksigner.bat') verify --verbose --print-certs $resolvedApk 2>&1 | Out-String)
if ($LASTEXITCODE -ne 0) { throw 'APK signature verification failed. Do not distribute this file.' }
$signers = [regex]::Matches($signatureOutput, '(?m)^Signer #\d+ certificate SHA-256 digest:\s*([a-fA-F0-9]{64})\s*$')
if ($signers.Count -ne 1) { throw 'Expected exactly one APK signer.' }
$fingerprint = $signers[0].Groups[1].Value.ToLowerInvariant()
if ($fingerprint -ne $ExpectedSignerSha256.ToLowerInvariant()) { throw "Unexpected APK signing certificate: $fingerprint. Preserve the official signing identity for updates." }

$badging = (& (Join-Path $buildTools.FullName 'aapt2.exe') dump badging $resolvedApk 2>&1 | Out-String)
if ($LASTEXITCODE -ne 0) { throw 'Could not inspect the signed APK manifest.' }
$identity = [regex]::Match($badging, "(?m)^package: name='([^']+)' versionCode='([^']+)' versionName='([^']+)'")
if (-not $identity.Success -or $identity.Groups[1].Value -ne 'com.synqora') { throw 'Expected the com.synqora application package.' }
if ($badging -match '(?m)^application-debuggable') { throw 'Release APK must not be debuggable.' }
$minimum = [regex]::Match($badging, "(?m)^(?:minSdkVersion|sdkVersion):'([0-9]+)'")
$target = [regex]::Match($badging, "(?m)^targetSdkVersion:'([0-9]+)'")
if (-not $minimum.Success -or -not $target.Success) { throw 'APK SDK declarations could not be read.' }
$schemes = [ordered]@{}
foreach ($scheme in [regex]::Matches($signatureOutput, '(?m)^Verified using (v[0-9.]+) scheme[^:]*:\s*(true|false)')) {
    $schemes[$scheme.Groups[1].Value] = $scheme.Groups[2].Value -eq 'true'
}
$apkFile = Get-Item -LiteralPath $resolvedApk
$report = [ordered]@{
    schemaVersion = 1
    verifiedAtUtc = [DateTime]::UtcNow.ToString('o')
    file = $apkFile.Name
    bytes = $apkFile.Length
    apkSha256 = (Get-FileHash -LiteralPath $resolvedApk -Algorithm SHA256).Hash.ToLowerInvariant()
    packageName = $identity.Groups[1].Value
    versionCode = $identity.Groups[2].Value
    versionName = $identity.Groups[3].Value
    minSdk = [int]$minimum.Groups[1].Value
    targetSdk = [int]$target.Groups[1].Value
    debuggable = $false
    signatureVerified = $true
    expectedSignerMatched = $true
    signerCertificateSubject = [regex]::Match($signatureOutput, '(?m)^Signer #1 certificate DN:\s*(.+)').Groups[1].Value.Trim()
    signerCertificateSha256 = $fingerprint
    signatureSchemes = $schemes
    permissions = @([regex]::Matches($badging, "(?m)^uses-permission: name='([^']+)'") | ForEach-Object { $_.Groups[1].Value })
    scope = 'Cryptographic identity and release manifest checks only; this is not Google approval or a malware clearance.'
}
if ($ReportPath) {
    $resolvedReport = [IO.Path]::GetFullPath($ReportPath)
    if ($resolvedReport -eq $resolvedApk) { throw 'ReportPath must not overwrite the APK.' }
    New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedReport) -Force | Out-Null
    $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $resolvedReport -Encoding UTF8
}
[pscustomobject]$report
