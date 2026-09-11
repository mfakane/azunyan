#requires -Version 7.0
[CmdletBinding()]
param(
    [string] $Version,
    [string] $OutputDirectory,
    [ValidateSet('msix', 'appx')] [string] $Format = 'msix',
    [string] $IdentityName,
    [string] $Publisher,
    [string] $CertificatePath,
    [string] $CertificatePassword = 'password',
    [string] $CertificateThumbprint,
    [string] $WinAppPath,
    [uri] $TimestampUrl
)
. (Join-Path $PSScriptRoot 'Packaging.Common.ps1')
$context = New-AzunoteDistribution $OutputDirectory $Version
$package = Join-Path $context.Output "Azunote-$($context.Version)-win-x64.$Format"
if (Test-Path -LiteralPath $package) { throw "Output already exists: $package" }
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
Publish-AzunoteDistribution $context
$payload = Join-Path $context.Work 'msix'
Copy-AzunotePayload $context $payload
$winapp = Find-AzunoteWinApp $context $WinAppPath
$manifest = $context.Manifest
$manifest.Package.Identity.Version = $context.Version
if ($IdentityName) { $manifest.Package.Identity.Name = $IdentityName }
if ($Publisher) { $manifest.Package.Identity.Publisher = $Publisher }
if ($Format -eq 'msix') {
    $manifestPath = Join-Path $context.Work 'Package.appxmanifest'
    $manifest.Save($manifestPath)
    Push-Location (Join-Path $context.Repo 'src/Azunote')
    try {
        # Keep self-contained deployment; winapp otherwise externalizes the runtime.
        & $winapp package $payload --manifest $manifestPath --executable Azunote.exe `
            --self-contained --skip-pri --output $package
        if ($LASTEXITCODE) { throw "WinApp packaging failed ($LASTEXITCODE)." }
    } finally { Pop-Location }
} else {
    # WinApp package emits MSIX; use its MakeAppx wrapper for explicit APPX requests.
    $manifest.Package.Identity.SetAttribute('ProcessorArchitecture', 'x64')
    $manifest.Package.Applications.Application.Executable = 'Azunote.exe'
    $manifest.Save((Join-Path $payload 'AppxManifest.xml'))
    & $winapp tool makeappx pack /d $payload /p $package
    if ($LASTEXITCODE) { throw "MakeAppx failed ($LASTEXITCODE)." }
}
if ($CertificatePath) {
    $signArguments = @('sign', $package, $CertificatePath, '--password', $CertificatePassword)
    if ($TimestampUrl) { $signArguments += @('--timestamp', $TimestampUrl.AbsoluteUri) }
    & $winapp @signArguments
    if ($LASTEXITCODE) { throw "WinApp signing failed ($LASTEXITCODE)." }
} elseif ($CertificateThumbprint) {
    # Keep thumbprint support for existing certificate-store based callers.
    $signArguments = @('tool', 'signtool', 'sign', '/fd', 'SHA256', '/s', 'My', '/sha1', $CertificateThumbprint)
    if ($TimestampUrl) { $signArguments += @('/tr', $TimestampUrl.AbsoluteUri, '/td', 'SHA256') }
    & $winapp @signArguments $package
    if ($LASTEXITCODE) { throw "WinApp SignTool signing failed ($LASTEXITCODE)." }
}
if ($CertificatePath -or $CertificateThumbprint) {
    & $winapp tool signtool verify /pa /v $package
    if ($LASTEXITCODE) { throw "WinApp SignTool verification failed ($LASTEXITCODE)." }
} else {
    Write-Host 'Package is unsigned. Sign it before sideloading; no certificate was created or installed.'
}
Complete-AzunoteDistribution $package
