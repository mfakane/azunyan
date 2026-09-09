#requires -Version 7.0
[CmdletBinding()]
param([string] $Version, [string] $OutputDirectory)
. (Join-Path $PSScriptRoot 'Packaging.Common.ps1')
$context = New-AzunoteDistribution $OutputDirectory $Version
$name = "Azunote-$($context.Version)-win-x64"
$archive = Join-Path $context.Output "$name.zip"
if (Test-Path -LiteralPath $archive) { throw "Output already exists: $archive" }
Publish-AzunoteDistribution $context
$payload = Join-Path $context.Work $name
Copy-AzunotePayload $context $payload
[IO.Compression.ZipFile]::CreateFromDirectory($payload, $archive,
    [IO.Compression.CompressionLevel]::Optimal, $true)
Complete-AzunoteDistribution $archive
