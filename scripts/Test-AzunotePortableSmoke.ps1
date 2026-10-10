#requires -Version 7.0
<#
.SYNOPSIS
Extracts a portable ZIP and checks that it runs natively on this machine.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Path,
    [ValidateSet('win-x64', 'win-arm64')] [string] $RuntimeIdentifier = 'win-x64',
    [int] $StartupSeconds = 15
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Packaging.Common.ps1')

$target = $AzunoteRuntimeIdentifiers[$RuntimeIdentifier]
$context = [pscustomobject]@{ RuntimeIdentifier = $RuntimeIdentifier; Machine = $target.Machine }

function Test-ManagedImage([string] $File) {
    # IL-only assemblies carry a CLR header and no machine-specific code.
    $bytes = [IO.File]::ReadAllBytes($File)
    $pe = [BitConverter]::ToInt32($bytes, 0x3C)
    $optional = $pe + 24
    $directories = if ([BitConverter]::ToUInt16($bytes, $optional) -eq 0x20B) { $optional + 112 } else { $optional + 96 }
    $clrHeader = $directories + 14 * 8
    return $clrHeader + 4 -le $bytes.Length -and [BitConverter]::ToUInt32($bytes, $clrHeader) -ne 0
}

$work = Join-Path ([IO.Path]::GetTempPath()) ('azunote-smoke-' + [guid]::NewGuid().ToString('N'))
Expand-Archive -LiteralPath $Path -DestinationPath $work
$root = @(Get-ChildItem -LiteralPath $work -Directory)
if ($root.Count -ne 1 -or $root[0].Name -notlike "*-$RuntimeIdentifier") {
    throw "Expected one top-level *-$RuntimeIdentifier folder in $Path."
}
$root = $root[0].FullName

# Every native image a user can load must match the architecture.
$native = 0
foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File -Include *.exe, *.dll) {
    if (Test-ManagedImage $file.FullName) { continue }
    Assert-AzunoteExecutableArchitecture $context $file.FullName
    $native++
}
Write-Host "$native native images are $RuntimeIdentifier."

# The console shim forwards to the AOT-compiled client and returns its output.
$help = & (Join-Path $root 'azu.exe') --help 2>&1 | Out-String
if ($LASTEXITCODE -ne 0 -or $help -notmatch 'Azunote') {
    throw "azu --help failed ($LASTEXITCODE):`n$help"
}
Write-Host 'azu --help succeeded through the portable shim.'

# The GUI shim starts the editor from .app and exits; the editor must stay up.
$editor = Join-Path $root "$AzunoteShimDirectory\Azunote.exe"
Start-Process -FilePath (Join-Path $root 'Azunote.exe') -WorkingDirectory $root
try {
    $deadline = (Get-Date).AddSeconds(30)
    do {
        Start-Sleep -Seconds 1
        $process = Get-Process -Name Azunote -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -eq $editor } | Select-Object -First 1
    } while (!$process -and (Get-Date) -lt $deadline)
    if (!$process) { throw "The editor did not start from $editor." }

    Start-Sleep -Seconds $StartupSeconds
    $process.Refresh()
    if ($process.HasExited) {
        throw "The editor exited during startup with code $($process.ExitCode)."
    }
    Write-Host "The editor is running from .app (PID $($process.Id))."
} finally {
    Get-Process -Name Azunote -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -like "$root*" } | Stop-Process -Force
}
