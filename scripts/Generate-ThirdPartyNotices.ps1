#Requires -Version 7.0
<#
.SYNOPSIS
Regenerates the reviewed third-party license snapshot, or verifies it offline.
#>
[CmdletBinding()]
param(
    [string] $OutputDirectory = (Split-Path -Parent $PSScriptRoot),
    [switch] $Check
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$manifest = Get-Content (Join-Path $PSScriptRoot 'licensing/manifest.json') -Raw | ConvertFrom-Json
$utf8 = [Text.UTF8Encoding]::new($false)

function Get-SafeOutputPath([string] $relativePath) {
    if ($relativePath -notmatch '^licenses/' -or [IO.Path]::IsPathRooted($relativePath)) {
        throw "Invalid license output path: $relativePath"
    }
    $path = [IO.Path]::GetFullPath((Join-Path $outputRoot $relativePath))
    $prefix = [IO.Path]::Combine($outputRoot, 'licenses') + [IO.Path]::DirectorySeparatorChar
    if (!$path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "License path escapes the output directory: $relativePath"
    }
    return $path
}

function Get-Sha256([byte[]] $bytes) {
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

function Get-SourceBytes($entry) {
    # Committed, hash-verified documents are an offline cache as well.
    $candidates = @((Join-Path $repoRoot $entry.file))
    if ($entry.PSObject.Properties['packageFile']) {
        $packageRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES }
            else { Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget/packages' }
        $candidates += Join-Path $packageRoot ($entry.component.ToLowerInvariant() + '/' + $entry.packageFile)
    }
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            $bytes = [IO.File]::ReadAllBytes($candidate)
            if ((Get-Sha256 $bytes) -eq $entry.sha256) { return ,$bytes }
        }
    }

    $temporaryFile = [IO.Path]::GetTempFileName()
    try {
        if ($entry.PSObject.Properties['packageFile']) {
            $id, $version = $entry.component.ToLowerInvariant().Split('/')
            $url = "https://api.nuget.org/v3-flatcontainer/$id/$version/$id.$version.nupkg"
            Invoke-WebRequest -Uri $url -OutFile $temporaryFile
            $archive = [IO.Compression.ZipFile]::OpenRead($temporaryFile)
            try {
                $item = $archive.GetEntry($entry.packageFile)
                if ($null -eq $item) { throw "Missing $($entry.packageFile) in $url" }
                $stream = $item.Open()
                $memory = [IO.MemoryStream]::new()
                try {
                    $stream.CopyTo($memory)
                    $bytes = $memory.ToArray()
                }
                finally { $stream.Dispose(); $memory.Dispose() }
            }
            finally { $archive.Dispose() }
        }
        else {
            Invoke-WebRequest -Uri $entry.source -OutFile $temporaryFile
            $bytes = [IO.File]::ReadAllBytes($temporaryFile)
        }
        if ((Get-Sha256 $bytes) -ne $entry.sha256) {
            throw "Upstream hash mismatch for $($entry.file); review the source before updating the manifest."
        }
        return ,$bytes
    }
    finally { Remove-Item -LiteralPath $temporaryFile -Force }
}

function Write-OrCheck([string] $path, [byte[]] $bytes) {
    $matches = (Test-Path -LiteralPath $path -PathType Leaf) -and
        ((Get-Sha256 ([IO.File]::ReadAllBytes($path))) -eq (Get-Sha256 $bytes))
    if ($matches) { return }
    if ($Check) { throw "Generated file is missing or out of date: $path" }
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
    [IO.File]::WriteAllBytes($path, $bytes)
}

# Resolve and validate every source before changing any generated files.
$documents = [ordered]@{}
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($entry in ($manifest.sources | Sort-Object file)) {
    $path = Get-SafeOutputPath $entry.file
    if (!$seen.Add($path)) { throw "Duplicate output path: $($entry.file)" }
    if ($Check) {
        if (!(Test-Path -LiteralPath $path -PathType Leaf) -or
            (Get-Sha256 ([IO.File]::ReadAllBytes($path))) -ne $entry.sha256) {
            throw "Missing or modified license document: $($entry.file)"
        }
    }
    else { $documents[$path] = Get-SourceBytes $entry }
}

$rows = @('| Component | Version | Terms and bundled documents |', '| --- | --- | --- |')
foreach ($component in $manifest.components) {
    $rows += "| $($component.name) | $($component.version) | $($component.notice) |"
}
$template = Get-Content (Join-Path $PSScriptRoot 'licensing/notices.template.md') -Raw
$notice = $template.Replace('{{COMPONENT_TABLE}}', ($rows -join "`n")).
    Replace('{{REVIEW_DATE}}', $manifest.reviewDate).
    Replace('{{TARGET_FRAMEWORK}}', $manifest.targetFramework).
    Replace('{{RUNTIME_IDENTIFIER}}', $manifest.runtimeIdentifier)
if ($notice -match '\{\{[A-Z_]+\}\}') { throw 'Unresolved template placeholder.' }
foreach ($link in [regex]::Matches($notice, '\]\((licenses/[^)]+)\)')) {
    if ($link.Groups[1].Value -ne 'licenses/sources.json' -and
        $link.Groups[1].Value -notin $manifest.sources.file) {
        throw "Notice links to an unlisted license document: $($link.Groups[1].Value)"
    }
}
$sources = ConvertTo-Json -InputObject @($manifest.sources | Sort-Object file) -Depth 8
# Match the repository's CRLF convention; upstream document bytes stay untouched.
$notice = ($notice.TrimEnd() -replace '\r?\n', "`r`n") + "`r`n"
$sources = ($sources.TrimEnd() -replace '\r?\n', "`r`n") + "`r`n"

foreach ($document in $documents.GetEnumerator()) { Write-OrCheck $document.Key $document.Value }
Write-OrCheck (Join-Path $outputRoot 'THIRD-PARTY-NOTICES.md') ($utf8.GetBytes($notice))
Write-OrCheck (Join-Path $outputRoot 'licenses/sources.json') ($utf8.GetBytes($sources))
Write-Host "$(if ($Check) { 'Verified' } else { 'Generated' }) notices and $($manifest.sources.Count) upstream documents."
