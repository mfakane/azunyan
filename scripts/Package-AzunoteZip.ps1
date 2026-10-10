#requires -Version 7.0
[CmdletBinding()]
param(
    [string] $Version,
    [string] $OutputDirectory,
    [switch] $Release,
    [ValidateSet('win-x64', 'win-arm64')] [string] $RuntimeIdentifier = 'win-x64'
)
. (Join-Path $PSScriptRoot 'Packaging.Common.ps1')
$context = New-AzunoteDistribution $OutputDirectory $Version -Release:$Release -RuntimeIdentifier $RuntimeIdentifier
$name = "Azunote-$($context.Version)-$($context.RuntimeIdentifier)"
$archive = Join-Path $context.Output "$name.zip"
if (Test-Path -LiteralPath $archive) { throw "Output already exists: $archive" }
Publish-AzunoteDistribution $context -Portable
$payload = Join-Path $context.Work $name
Copy-AzunotePortablePayload $context $payload
[IO.Compression.ZipFile]::CreateFromDirectory($payload, $archive,
    [IO.Compression.CompressionLevel]::Optimal, $true)
Complete-AzunoteDistribution $archive
