#requires -Version 7.0
<#
.SYNOPSIS
Combines the per-architecture Azunote MSIX packages into one MSIX bundle.

.DESCRIPTION
Takes the win-x64 and win-arm64 packages made by Package-AzunoteMsix.ps1,
checks that they share one identity and version, and bundles them as
Azunote-<version>.msixbundle with MakeAppx through WinApp CLI. Windows installs
the package matching the device from the bundle. The bundle is unsigned unless
a certificate is given, like the packages it is made from.
#>
[CmdletBinding()]
param(
    # The per-architecture packages. Defaults to Azunote-<version>-<rid>.msix
    # for every supported runtime identifier in InputDirectory.
    [string[]] $Path,
    [string] $InputDirectory,
    [string] $Version,
    [string] $OutputDirectory,
    [string] $CertificatePath,
    [string] $CertificatePassword = 'password',
    [string] $CertificateThumbprint,
    [string] $WinAppPath,
    [uri] $TimestampUrl
)
. (Join-Path $PSScriptRoot 'Packaging.Common.ps1')
$repo = Split-Path $PSScriptRoot -Parent
if (!$InputDirectory) { $InputDirectory = Join-Path $repo 'artifacts/distributions' }
if (!$OutputDirectory) { $OutputDirectory = $InputDirectory }
if ($CertificatePath -and $CertificateThumbprint) {
    throw 'CertificatePath and CertificateThumbprint are mutually exclusive.'
}
if ($TimestampUrl -and !$CertificatePath -and !$CertificateThumbprint) {
    throw 'TimestampUrl requires a certificate.'
}
if ($CertificatePath) {
    $resolvedCertificate = Resolve-Path -LiteralPath $CertificatePath -ErrorAction SilentlyContinue
    if (!$resolvedCertificate) { throw "Certificate was not found: $CertificatePath" }
    $CertificatePath = $resolvedCertificate.Path
}

if (!$Path) {
    if (!$Version) {
        # Pick the only version that has a package for every architecture.
        $versions = @(Get-ChildItem -LiteralPath $InputDirectory -Filter 'Azunote-*-win-*.msix' -File |
            ForEach-Object { if ($_.Name -match '^Azunote-(\d+\.\d+\.\d+\.\d+)-win-[a-z0-9]+\.msix$') { $Matches[1] } } |
            Group-Object | Where-Object Count -eq $AzunoteRuntimeIdentifiers.Count | ForEach-Object Name)
        if ($versions.Count -ne 1) {
            throw "Pass -Version or -Path; $InputDirectory has complete package sets for $($versions.Count) versions."
        }
        $Version = $versions[0]
    }
    $Path = $AzunoteRuntimeIdentifiers.Keys | Sort-Object | ForEach-Object {
        Join-Path $InputDirectory "Azunote-$Version-$_.msix"
    }
}
$packages = foreach ($package in $Path) {
    if (!(Test-Path -LiteralPath $package -PathType Leaf)) { throw "Package was not found: $package" }
    Get-AzunotePackageIdentity $package
}

# MakeAppx requires one identity and version across a bundle's packages.
$first = $packages[0]
foreach ($package in $packages) {
    foreach ($field in @('Name', 'Publisher', 'Version')) {
        if ($package.$field -ne $first.$field) {
            throw "$($package.Path) has $field '$($package.$field)'; $($first.Path) has '$($first.$field)'."
        }
    }
}
if ($Version -and $first.Version -ne $Version) {
    throw "The packages are version $($first.Version); expected $Version."
}
$Version = $first.Version
$architectures = @($packages.Architecture | Sort-Object)
$expected = @($AzunoteRuntimeIdentifiers.Values | ForEach-Object { $_.Architecture } | Sort-Object)
if (($architectures -join ',') -ne ($expected -join ',')) {
    throw "The packages are for '$($architectures -join ', ')'; a bundle needs exactly '$($expected -join ', ')'."
}

$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force $output | Out-Null
$bundle = Join-Path $output "Azunote-$Version.msixbundle"
if (Test-Path -LiteralPath $bundle) { throw "Output already exists: $bundle" }

# MakeAppx bundles a whole directory, so stage only the packages to include.
$work = Join-Path $repo ('artifacts/packaging/' + [guid]::NewGuid().ToString('N'))
$staging = Join-Path $work 'bundle'
New-Item -ItemType Directory -Force $staging | Out-Null
foreach ($package in $packages) { Copy-Item -LiteralPath $package.Path -Destination $staging }

$winapp = Find-AzunoteWinApp ([pscustomobject]@{ Repo = $repo }) $WinAppPath
# Without /bv MakeAppx derives the bundle version from the clock; keep it equal
# to the packages so the bundle and its contents read the same.
& $winapp tool makeappx bundle /o /d $staging /bv $Version /p $bundle
if ($LASTEXITCODE) { throw "MakeAppx bundle failed ($LASTEXITCODE)." }

$result = Assert-AzunoteMsixBundle $bundle
Write-Host "Bundle identity: $($result.Name) $($result.Version) ($($result.Architectures -join ', '))"

if ($CertificatePath) {
    $signArguments = @('sign', $bundle, $CertificatePath, '--password', $CertificatePassword)
    if ($TimestampUrl) { $signArguments += @('--timestamp', $TimestampUrl.AbsoluteUri) }
    & $winapp @signArguments
    if ($LASTEXITCODE) { throw "WinApp signing failed ($LASTEXITCODE)." }
} elseif ($CertificateThumbprint) {
    $signArguments = @('tool', 'signtool', 'sign', '/fd', 'SHA256', '/s', 'My', '/sha1', $CertificateThumbprint)
    if ($TimestampUrl) { $signArguments += @('/tr', $TimestampUrl.AbsoluteUri, '/td', 'SHA256') }
    & $winapp @signArguments $bundle
    if ($LASTEXITCODE) { throw "WinApp SignTool signing failed ($LASTEXITCODE)." }
}
if ($CertificatePath -or $CertificateThumbprint) {
    & $winapp tool signtool verify /pa /v $bundle
    if ($LASTEXITCODE) { throw "WinApp SignTool verification failed ($LASTEXITCODE)." }
} else {
    Write-Host 'Bundle is unsigned. Sign it before sideloading; no certificate was created or installed.'
}
Complete-AzunoteDistribution $bundle
