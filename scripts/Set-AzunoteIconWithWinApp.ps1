#requires -Version 7.0
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$azunoteDir = Join-Path $repoRoot 'src\Azunote'
$assetsDir = Join-Path $azunoteDir 'Assets'
$manifestPath = Join-Path $azunoteDir 'Package.appxmanifest'
$fullIconSvg = Join-Path $repoRoot 'assets\icons\01-paw-notebook.svg'
$smallIconSvg = Join-Path $repoRoot 'assets\icons\01-paw-notebook-small.svg'
$tinyIconSvg = Join-Path $repoRoot 'assets\icons\01-paw-notebook-16.svg'
$smallAssociationSvg = Join-Path $repoRoot 'assets\icons\03-paw-document-small.svg'
$fullAssociationSvg = Join-Path $repoRoot 'assets\icons\03-paw-document.svg'

& (Join-Path $PSScriptRoot 'New-AzunoteSmallIcons.ps1')

$winappCommand = Get-Command winapp.exe -ErrorAction SilentlyContinue
if ($null -eq $winappCommand) {
    $winappCommand = Get-Command winapp -ErrorAction Stop
}

function Invoke-WinAppAssetGeneration([string] $SourceSvg, [string] $Manifest) {
    $arguments = @(
        'manifest', 'update-assets', $SourceSvg,
        '--manifest', $Manifest,
        '--light-image', $SourceSvg
    )
    & $winappCommand @arguments
    if ($LASTEXITCODE) {
        throw "WinApp asset generation failed ($LASTEXITCODE) for $SourceSvg."
    }
}

function Set-ManifestIconReferences(
    [string] $Manifest,
    [string] $SquareLogo,
    [string] $AssociationLogo
) {
    [xml] $document = Get-Content -LiteralPath $Manifest -Raw
    $visualElements = $document.SelectSingleNode(
        "/*[local-name()='Package']/*[local-name()='Applications']/*[local-name()='Application']/*[local-name()='VisualElements']")
    if ($null -eq $visualElements) {
        throw "The manifest has no uap:VisualElements element: $Manifest"
    }
    $visualElements.SetAttribute('Square44x44Logo', $SquareLogo)

    $associationLogoNode = $document.SelectSingleNode(
        "/*[local-name()='Package']/*[local-name()='Applications']/*[local-name()='Application']/*[local-name()='Extensions']/*[local-name()='Extension']/*[local-name()='FileTypeAssociation']/*[local-name()='Logo']")
    if ($null -ne $associationLogoNode) {
        $associationLogoNode.InnerText = $AssociationLogo
    }
    $document.Save($Manifest)
}

function New-TemporaryIconManifest([string] $SquareLogo, [string] $AssociationLogo) {
    $directory = Join-Path ([IO.Path]::GetTempPath()) "AzunoteIconAssets-$([guid]::NewGuid().ToString('N'))"
    $temporaryAssets = Join-Path $directory 'Assets'
    New-Item -ItemType Directory -Path $temporaryAssets -Force | Out-Null

    $temporaryManifest = Join-Path $directory 'Package.appxmanifest'
    Copy-Item -LiteralPath $manifestPath -Destination $temporaryManifest
    Set-ManifestIconReferences $temporaryManifest $SquareLogo $AssociationLogo

    [pscustomobject] @{
        Directory = $directory
        Manifest = $temporaryManifest
        Assets = $temporaryAssets
    }
}

function Copy-SmallAppListAssets([string] $SourceAssets) {
    foreach ($source in Get-ChildItem -LiteralPath $SourceAssets -File -Filter 'AppList*') {
        # Keep the detailed artwork for the large tile and 400% scale. The
        # remaining shell sizes are the ones with too little pixel budget for
        # the full illustration.
        if ($source.Name -match '^AppList\.targetsize-256' -or
            $source.Name -match '^AppList\.scale-400') {
            continue
        }
        Copy-Item -LiteralPath $source.FullName -Destination (Join-Path $assetsDir $source.Name)
    }
}

function Copy-TitleBarAssets([string] $SourceAssets) {
    # TitleBar.IconSource is a 16-DIP XAML element. Use scale-qualified
    # physical sizes so MRT can select a sharp source on each monitor.
    $scaleTargetSizes = [ordered] @{
        100 = 16
        125 = 20
        150 = 24
        200 = 32
        250 = 40
        300 = 48
        400 = 64
        450 = 72
    }
    foreach ($entry in $scaleTargetSizes.GetEnumerator()) {
        $scale = $entry.Key
        $targetSize = $entry.Value
        $source = Join-Path $SourceAssets "AppList.targetsize-$targetSize.png"
        if (!(Test-Path -LiteralPath $source)) {
            throw "Missing AppList target-size asset for title bar scale $scale`: $source"
        }
        $name = if ($scale -eq 100) { 'TitleBarIcon.png' } else { "TitleBarIcon.scale-$scale.png" }
        Copy-Item -LiteralPath $source -Destination (Join-Path $assetsDir $name)
    }
}

if (!(Test-Path -LiteralPath $fullIconSvg)) {
    throw "Missing full-size icon SVG: $fullIconSvg"
}
foreach ($sourceSvg in @($smallIconSvg, $tinyIconSvg, $smallAssociationSvg)) {
    if (!(Test-Path -LiteralPath $sourceSvg)) {
        throw "Missing small-size icon SVG: $sourceSvg"
    }
}
if (!(Test-Path -LiteralPath $manifestPath)) {
    throw "Missing package manifest: $manifestPath"
}

$temporaryManifests = [System.Collections.Generic.List[string]]::new()
try {
    # Generate the normal app/tile/store assets from the detailed vector artwork.
    # WinApp's ICO is replaced below after the small PNGs have been generated.
    Invoke-WinAppAssetGeneration $fullIconSvg $manifestPath

    # WinApp accepts one source image per invocation. Use temporary manifests
    # to make it generate resource-qualified assets under a different basename,
    # then copy only the low-resolution families back to the real Assets folder.
    $smallAppListManifest = New-TemporaryIconManifest 'Assets\AppList.png' 'Assets\FileAssociation.png'
    $temporaryManifests.Add($smallAppListManifest.Directory)
    Invoke-WinAppAssetGeneration $smallIconSvg $smallAppListManifest.Manifest
    Copy-SmallAppListAssets $smallAppListManifest.Assets

    # A foreground outline is needed only at 16px. Do not alter larger icons.
    Invoke-WinAppAssetGeneration $tinyIconSvg $smallAppListManifest.Manifest
    foreach ($source in Get-ChildItem -LiteralPath $smallAppListManifest.Assets -File -Filter 'AppList.targetsize-16*.png') {
        Copy-Item -LiteralPath $source.FullName -Destination (Join-Path $assetsDir $source.Name)
    }

    Copy-TitleBarAssets $assetsDir

    $smallAssociationManifest = New-TemporaryIconManifest 'Assets\FileAssociation.png' 'Assets\FileAssociation.png'
    $temporaryManifests.Add($smallAssociationManifest.Directory)
    Invoke-WinAppAssetGeneration $fullAssociationSvg $smallAssociationManifest.Manifest
    foreach ($source in Get-ChildItem -LiteralPath $smallAssociationManifest.Assets -File -Filter 'FileAssociation*') {
        Copy-Item -LiteralPath $source.FullName -Destination (Join-Path $assetsDir $source.Name)
    }
    Invoke-WinAppAssetGeneration $smallAssociationSvg $smallAssociationManifest.Manifest
    foreach ($source in Get-ChildItem -LiteralPath $smallAssociationManifest.Assets -File -Filter 'FileAssociation*') {
        if ($source.Name -match '\.targetsize-256|\.scale-400') { continue }
        Copy-Item -LiteralPath $source.FullName -Destination (Join-Path $assetsDir $source.Name)
    }

    & (Join-Path $PSScriptRoot 'New-AzunoteIco.ps1') -AssetsDirectory $assetsDir
    Write-Host "Generated icon assets: $assetsDir"
} finally {
    foreach ($directory in $temporaryManifests) {
        if (Test-Path -LiteralPath $directory) {
            $resolvedDirectory = (Resolve-Path -LiteralPath $directory).Path
            $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
            if ([IO.Path]::GetDirectoryName($resolvedDirectory) -ne $temporaryRoot -or
                [IO.Path]::GetFileName($resolvedDirectory) -notmatch '^AzunoteIconAssets-[a-f0-9]{32}$') {
                throw "Unexpected temporary directory: $resolvedDirectory"
            }
            Remove-Item -LiteralPath $resolvedDirectory -Recurse -Force
        }
    }
}
