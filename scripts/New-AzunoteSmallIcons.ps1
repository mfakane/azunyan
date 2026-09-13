#requires -Version 7.0
# Derive small artwork from the master geometry; never redraw the silhouettes.
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$iconDirectory = Join-Path (Split-Path $PSScriptRoot -Parent) 'assets/icons'

foreach ($name in @('01-paw-notebook', '03-paw-document')) {
    [xml] $svg = Get-Content -LiteralPath (Join-Path $iconDirectory "$name.svg") -Raw

    # Remove only effects and subpixel highlights. Preserve the original viewBox,
    # paths, transforms, gradients, rings, four note lines and folded corners.
    foreach ($node in @($svg.SelectNodes('//*[@id]'))) {
        $id = $node.GetAttribute('id')
        if ($id -in @('paper-shadow-layer', 'curl-cast-shadow', 'corner-shadow',
                      'paw-rims', 'diagonal-edge-glint') -or
            $id -match '^binding-\d+-(shadow|fine-rim)$') {
            [void] $node.ParentNode.RemoveChild($node)
        }
    }
    foreach ($node in @($svg.SelectNodes('//*[@filter]'))) {
        $node.RemoveAttribute('filter')
    }
    foreach ($node in @($svg.SelectNodes("//*[local-name()='filter']"))) {
        [void] $node.ParentNode.RemoveChild($node)
    }

    # ~0.75px at 32px: a crisp paper edge replaces the original diffuse shadow.
    $sheetId = if ($name -eq '01-paw-notebook') { 'back-sheet' } else { 'document-sheet' }
    $sheet = $svg.SelectSingleNode("//*[@id='$sheetId']")
    $sheet.SetAttribute('stroke', '#68254b')
    $sheet.SetAttribute('stroke-width', '22')
    $sheet.SetAttribute('stroke-linejoin', 'round')

    foreach ($node in $svg.SelectNodes("//*[@id='paw-blue']/*")) {
        $node.SetAttribute('stroke', '#6c1f49')
        $node.SetAttribute('stroke-width', '6')
        $node.SetAttribute('stroke-linejoin', 'round')
    }
    foreach ($node in $svg.SelectNodes("//*[@id='note-lines']/*")) {
        # Enlarge the ink symmetrically without moving the original line boxes.
        $node.SetAttribute('style', 'fill:#b9789e;stroke:#b9789e;stroke-width:9')
    }
    foreach ($node in $svg.SelectNodes("//*[@id='binding']/*/*")) {
        if ($node.GetAttribute('id') -match '^binding-\d+-ring$') {
            $node.SetAttribute('stroke', '#a56087')
            $node.SetAttribute('stroke-width', '8')
        }
    }
    foreach ($id in @('curl-edge-highlight', 'fold-edge-glint')) {
        $node = $svg.SelectSingleNode("//*[@id='$id']")
        if ($null -ne $node) {
            $node.SetAttribute('stroke', '#bc88a6')
            $node.SetAttribute('stroke-width', '10')
            $node.SetAttribute('stroke-opacity', '1')
        }
    }
    $svg.SelectSingleNode("//*[@id='icon-description']").InnerText =
        "Generated from $name.svg by scripts/New-AzunoteSmallIcons.ps1. Original geometry and gradients; reduced effects and reinforced small-size edges."
    $svg.Save((Join-Path $iconDirectory "$name-small.svg"))

    if ($name -eq '01-paw-notebook') {
        # At 16px the front sheet hides half of the rear sheet's subpixel
        # stroke, leaving a pale gap at the lower-left corner. Paint the same
        # silhouette over the paper, with a 1px stroke at this target size.
        # Keep this optical correction separate from the 20px+ artwork.
        $outline = $sheet.CloneNode($true)
        $outline.SetAttribute('id', 'paper-outline-16')
        $outline.RemoveAttribute('style')
        $outline.SetAttribute('fill', 'none')
        $viewBoxWidth = [double]::Parse($svg.DocumentElement.GetAttribute('viewBox').Split(' ')[2],
            [Globalization.CultureInfo]::InvariantCulture)
        $outline.SetAttribute('stroke-width', ($viewBoxWidth / 16).ToString([Globalization.CultureInfo]::InvariantCulture))
        $sheet.RemoveAttribute('stroke')
        $sheet.RemoveAttribute('stroke-width')
        [void] $svg.SelectSingleNode("//*[@id='paper']").AppendChild($outline)
        $svg.SelectSingleNode("//*[@id='icon-description']").InnerText =
            "Generated from $name.svg by scripts/New-AzunoteSmallIcons.ps1. Original geometry with a foreground 1px paper outline for 16px only."
        $svg.Save((Join-Path $iconDirectory "$name-16.svg"))
    }
}
