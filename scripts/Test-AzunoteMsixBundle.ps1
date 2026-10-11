#requires -Version 7.0
<#
.SYNOPSIS
Checks an Azunote MSIX bundle's manifest and inner packages, and that MakeAppx
can unbundle it again.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Path,
    [string] $Version,
    [switch] $SkipUnbundle,
    [string] $WinAppPath
)
. (Join-Path $PSScriptRoot 'Packaging.Common.ps1')
$Path = (Resolve-Path -LiteralPath $Path).Path
$result = Assert-AzunoteMsixBundle $Path
if ($Version -and $result.Version -ne $Version) {
    throw "$Path is version $($result.Version); expected $Version."
}
$sidecar = "$Path.sha256"
if (Test-Path -LiteralPath $sidecar) {
    $hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    $recorded = ((Get-Content -LiteralPath $sidecar -Raw).Trim() -split '\s+')[0]
    if ($recorded -ne $hash) { throw "$sidecar records $recorded; the bundle hashes to $hash." }
    Write-Host 'SHA-256 sidecar matches.'
}

if (!$SkipUnbundle) {
    $repo = Split-Path $PSScriptRoot -Parent
    $winapp = Find-AzunoteWinApp ([pscustomobject]@{ Repo = $repo }) $WinAppPath
    $work = Join-Path ([IO.Path]::GetTempPath()) ('azunote-unbundle-' + [guid]::NewGuid().ToString('N'))
    try {
        & $winapp tool makeappx unbundle /o /p $Path /d $work
        if ($LASTEXITCODE) { throw "MakeAppx unbundle failed ($LASTEXITCODE)." }
        foreach ($rid in ($AzunoteRuntimeIdentifiers.Keys | Sort-Object)) {
            $package = Join-Path $work "Azunote-$($result.Version)-$rid.msix"
            if (!(Test-Path -LiteralPath $package)) { throw "Unbundled output has no $([IO.Path]::GetFileName($package))." }
            Assert-AzunotePackageArchitecture ([pscustomobject]@{ Architecture = $AzunoteRuntimeIdentifiers[$rid].Architecture }) $package
        }
        Write-Host 'MakeAppx unbundled both packages.'
    } finally { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }
}
Write-Host "$Path is a valid bundle of $($result.Name) $($result.Version) for $($result.Architectures -join ' and ')."
