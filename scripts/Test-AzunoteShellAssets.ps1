#requires -Version 7.0
[CmdletBinding()]
param([string] $PublishedDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Packaging.Common.ps1')

$repo = Split-Path $PSScriptRoot -Parent
$assets = Join-Path $repo 'src/Azunote/Assets'
$sizes = @(16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256)
$ico = [IO.File]::ReadAllBytes((Join-Path $assets 'app.ico'))
if ([BitConverter]::ToUInt16($ico, 0) -ne 0 -or
    [BitConverter]::ToUInt16($ico, 2) -ne 1 -or
    [BitConverter]::ToUInt16($ico, 4) -ne $sizes.Count) {
    throw 'Expected a 14-frame ICO header.'
}
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $entry = 6 + 16 * $i
    $size = $sizes[$i]
    if ($ico[$entry] -ne ($size % 256) -or $ico[$entry + 1] -ne ($size % 256)) {
        throw "Wrong ICO dimensions for frame $i."
    }
    $length = [BitConverter]::ToUInt32($ico, $entry + 8)
    $offset = [BitConverter]::ToUInt32($ico, $entry + 12)
    $png = [IO.File]::ReadAllBytes((Join-Path $assets "AppList.targetsize-$size.png"))
    if ($offset + $length -gt $ico.Length -or $length -ne $png.Length -or
        [Convert]::ToHexString($ico[$offset..($offset + $length - 1)]) -cne
        [Convert]::ToHexString($png)) {
        throw "ICO ${size}px frame does not contain the exact generated PNG."
    }
}
Write-Host 'PASS: all 14 ICO frames contain the exact-size shell PNGs.'

if (!$PublishedDirectory) {
    Write-Host 'PRI tests not run. Pass -PublishedDirectory with a compiled Azunote.pri and Assets folder.'
    return
}
$winapp = (Get-Command winapp -CommandType Application).Source
$testRoot = Join-Path $repo ('artifacts/icon-tests/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$sourceDump = Join-Path $testRoot 'source.xml'
& $winapp tool makepri dump /if (Join-Path $PublishedDirectory 'Azunote.pri') /of $sourceDump /dt detailed
if ($LASTEXITCODE) { throw 'Could not inspect the compiled app PRI.' }
[xml] $sourceIndex = Get-Content -LiteralPath $sourceDump -Raw

function Get-CandidateSignatures([xml] $Index) {
    foreach ($resource in $Index.SelectNodes('//NamedResource')) {
        $path = ([uri] $resource.uri).AbsolutePath
        foreach ($candidate in $resource.Candidate) {
            $qualifiers = @($candidate.SelectNodes('QualifierSet/Qualifier') |
                ForEach-Object { "$($_.name)=$($_.value)" } | Sort-Object) -join ';'
            # Embedded XBF data as well as file paths/strings must survive.
            $value = $candidate.SelectSingleNode('Value | Base64Value')
            if ($null -eq $value) { throw "Unrecognized PRI candidate value: $path" }
            "$path|$($candidate.type)|$qualifiers|$($value.InnerXml)"
        }
    }
}
$expected = @(Get-CandidateSignatures $sourceIndex | Sort-Object)
foreach ($identity in @('Azunote', 'Azunote.IconVerification')) {
    $work = Join-Path $testRoot $identity
    $payload = Join-Path $work 'payload'
    New-Item -ItemType Directory -Path $payload -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PublishedDirectory 'Azunote.pri') -Destination $payload
    Copy-Item -LiteralPath (Join-Path $PublishedDirectory 'Assets') -Destination $payload -Recurse
    if (Test-Path -LiteralPath (Join-Path $PublishedDirectory 'resources.pri')) {
        Copy-Item -LiteralPath (Join-Path $PublishedDirectory 'resources.pri') -Destination $payload
    }
    $context = [pscustomobject] @{ Work = $work }
    New-AzunotePackageResources $context $payload $winapp $identity
    [xml] $actualIndex = Get-Content -LiteralPath (Join-Path $work 'package-resources.xml') -Raw
    $actual = @(Get-CandidateSignatures $actualIndex | Sort-Object)
    if (@(Compare-Object $expected $actual -CaseSensitive).Count) {
        throw "Resource candidates changed for $identity."
    }
    if ((Get-FileHash (Join-Path $payload 'resources.pri')).Hash -ne
        (Get-FileHash (Join-Path $work 'package-resources.pri')).Hash) {
        throw 'The verified index was not copied to the package-root resources.pri.'
    }
    Write-Host "PASS: $identity preserves all $($expected.Count) resource candidates and indexes all shell icon variants."
}
Write-Host "Inspection artifacts: $testRoot"
