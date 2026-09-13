#requires -Version 7.0
[CmdletBinding()]
param(
    [string] $AssetsDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'src/Azunote/Assets')
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Use the same exact-size artwork as the shell PNGs, without another resize.
# 256px retains the master artwork; the smaller frames use the optimized SVG.
$sizes = @(16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256)
$frames = foreach ($size in $sizes) {
    $path = Join-Path $AssetsDirectory "AppList.targetsize-$size.png"
    $bytes = [IO.File]::ReadAllBytes($path)
    if ($bytes.Length -lt 24 -or
        [Convert]::ToHexString($bytes[0..7]) -ne '89504E470D0A1A0A' -or
        [Convert]::ToHexString($bytes[16..19]) -ne $size.ToString('X8') -or
        [Convert]::ToHexString($bytes[20..23]) -ne $size.ToString('X8')) {
        throw "Expected a ${size}x${size} PNG: $path"
    }
    [pscustomobject] @{ Size = $size; Bytes = $bytes }
}

$stream = [IO.MemoryStream]::new()
$writer = [IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16] 0) # reserved
    $writer.Write([uint16] 1) # icon
    $writer.Write([uint16] $frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = [byte] ($frame.Size % 256) # zero represents 256px
        $writer.Write($dimension)
        $writer.Write($dimension)
        $writer.Write([byte] 0) # no palette
        $writer.Write([byte] 0) # reserved
        $writer.Write([uint16] 1) # planes
        $writer.Write([uint16] 32) # RGBA
        $writer.Write([uint32] $frame.Bytes.Length)
        $writer.Write([uint32] $offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]] $frame.Bytes) }
    $writer.Flush()
    [IO.File]::WriteAllBytes((Join-Path $AssetsDirectory 'app.ico'), $stream.ToArray())
} finally {
    $writer.Dispose()
    $stream.Dispose()
}
Write-Host "Generated app.ico with $($frames.Count) exact-size PNG frames."
