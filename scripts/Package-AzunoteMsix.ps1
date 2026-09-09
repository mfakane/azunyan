#requires -Version 7.0
[CmdletBinding()]
param(
    [string] $Version,
    [string] $OutputDirectory,
    [ValidateSet('msix', 'appx')] [string] $Format = 'msix',
    [string] $IdentityName,
    [string] $Publisher,
    [string] $CertificateThumbprint,
    [uri] $TimestampUrl
)
. (Join-Path $PSScriptRoot 'Packaging.Common.ps1')
$context = New-AzunoteDistribution $OutputDirectory $Version
$package = Join-Path $context.Output "Azunote-$($context.Version)-win-x64.$Format"
if (Test-Path -LiteralPath $package) { throw "Output already exists: $package" }
if ($Format -eq 'appx') { $makeappx = Find-AzunoteSdkTool 'makeappx.exe' }
if ($TimestampUrl -and !$CertificateThumbprint) { throw 'TimestampUrl requires CertificateThumbprint.' }
if ($CertificateThumbprint) { $signtool = Find-AzunoteSdkTool 'signtool.exe' }
Publish-AzunoteDistribution $context
$payload = Join-Path $context.Work 'msix'
Copy-AzunotePayload $context $payload
$manifest = $context.Manifest
$manifest.Package.Identity.Version = $context.Version
if ($IdentityName) { $manifest.Package.Identity.Name = $IdentityName }
if ($Publisher) { $manifest.Package.Identity.Publisher = $Publisher }
if ($Format -eq 'msix') {
    $winapp = Find-AzunoteWinApp $context
    $manifestPath = Join-Path $context.Work 'Package.appxmanifest'
    $manifest.Save($manifestPath)
    Push-Location (Join-Path $context.Repo 'src/Azunote')
    try {
        # Keep self-contained deployment; winapp otherwise externalizes the runtime.
        & $winapp pack $payload --manifest $manifestPath --executable Azunote.exe `
            --self-contained --skip-pri --output $package
        if ($LASTEXITCODE) { throw "WinApp packaging failed ($LASTEXITCODE)." }
    } finally { Pop-Location }
} else {
    # WinApp pack documents MSIX output; retain MakeAppx for explicit APPX requests.
    $manifest.Package.Identity.SetAttribute('ProcessorArchitecture', 'x64')
    $manifest.Package.Applications.Application.Executable = 'Azunote.exe'
    $manifest.Save((Join-Path $payload 'AppxManifest.xml'))
    & $makeappx pack /d $payload /p $package
    if ($LASTEXITCODE) { throw "MakeAppx failed ($LASTEXITCODE)." }
}
if ($CertificateThumbprint) {
    $signArguments = @('sign', '/fd', 'SHA256', '/s', 'My', '/sha1', $CertificateThumbprint)
    if ($TimestampUrl) { $signArguments += @('/tr', $TimestampUrl.AbsoluteUri, '/td', 'SHA256') }
    & $signtool @signArguments $package
    if ($LASTEXITCODE) { throw "SignTool signing failed ($LASTEXITCODE)." }
    & $signtool verify /pa /v $package
    if ($LASTEXITCODE) { throw "SignTool verification failed ($LASTEXITCODE)." }
} else {
    Write-Host 'Package is unsigned. Sign it before sideloading; no certificate was created or installed.'
}
Complete-AzunoteDistribution $package
