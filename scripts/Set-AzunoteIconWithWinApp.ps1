$ErrorActionPreference = 'Stop'

# Azunote project root
$repoRoot = Split-Path -Parent $PSScriptRoot
$azunoteDir = Join-Path $repoRoot 'src\Azunote'
$iconSvg = Join-Path $repoRoot 'assets\icons\01-paw-notebook.svg'
$manifestPath = Join-Path $azunoteDir 'Package.appxmanifest'

Push-Location $azunoteDir

try {
    # Regenerate icon assets from SVG
    winapp manifest update-assets $iconSvg --manifest $manifestPath --light-image $iconSvg

    Write-Host "Generated assets: $(Join-Path $azunoteDir 'Assets')"
} finally {
    Pop-Location
}
