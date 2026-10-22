# Aligns the columns of the Markdown table piped in, one line per row. The
# delimiter row keeps each column's alignment (:--, :-:, --:). Lines that are
# not table rows are written back unchanged. Works in Windows PowerShell 5.1
# and PowerShell 7.

# East Asian wide and fullwidth characters take two columns in a monospaced font.
function Get-DisplayWidth([string] $text) {
    $width = 0
    foreach ($char in $text.ToCharArray()) {
        $code = [int] $char
        if (($code -ge 0x1100 -and $code -le 0x115F) -or
            ($code -ge 0x2E80 -and $code -le 0xA4CF) -or
            ($code -ge 0xAC00 -and $code -le 0xD7A3) -or
            ($code -ge 0xF900 -and $code -le 0xFAFF) -or
            ($code -ge 0xFE30 -and $code -le 0xFE4F) -or
            ($code -ge 0xFF00 -and $code -le 0xFF60) -or
            ($code -ge 0xFFE0 -and $code -le 0xFFE6)) {
            $width += 2
        }
        elseif (-not [char]::IsLowSurrogate($char)) {
            $width += 1
        }
    }
    $width
}

# Splits a row into trimmed cells at the pipes that are not escaped as \|.
function Split-Row([string] $line) {
    $body = $line.Trim()
    if ($body.StartsWith('|')) { $body = $body.Substring(1) }
    if ($body.EndsWith('|') -and -not $body.EndsWith('\|')) { $body = $body.Substring(0, $body.Length - 1) }
    , @([regex]::Split($body, '(?<!\\)\|') | ForEach-Object { $_.Trim() })
}

function Format-Cell([string] $text, [int] $width, [string] $alignment) {
    $padding = $width - (Get-DisplayWidth $text)
    switch ($alignment) {
        'right' { (' ' * $padding) + $text }
        'center' {
            $left = [math]::Floor($padding / 2)
            (' ' * $left) + $text + (' ' * ($padding - $left))
        }
        default { $text + (' ' * $padding) }
    }
}

$lines = @($input)
$delimiter = '^\s*\|?\s*:?-+:?\s*(\|\s*:?-+:?\s*)*\|?\s*$'
$rows = @()
$alignments = @()
foreach ($line in $lines) {
    if ($line -notmatch '\|') { continue }
    $cells = Split-Row $line
    if ($line -match $delimiter) {
        $alignments = @($cells | ForEach-Object {
            if ($_.StartsWith(':') -and $_.EndsWith(':')) { 'center' }
            elseif ($_.EndsWith(':')) { 'right' }
            else { 'left' }
        })
    }
    else {
        $rows += , $cells
    }
}

$columns = (@($rows | ForEach-Object { $_.Count }) + $alignments.Count | Measure-Object -Maximum).Maximum
$widths = @(for ($column = 0; $column -lt $columns; $column++) {
    $cellWidths = @($rows | ForEach-Object { if ($column -lt $_.Count) { Get-DisplayWidth $_[$column] } else { 0 } })
    [math]::Max(3, ($cellWidths | Measure-Object -Maximum).Maximum)
})

foreach ($line in $lines) {
    if ($line -notmatch '\|') {
        $line
        continue
    }

    $indent = [regex]::Match($line, '^\s*').Value
    $cells = Split-Row $line
    $formatted = for ($column = 0; $column -lt $columns; $column++) {
        $alignment = if ($column -lt $alignments.Count) { $alignments[$column] } else { 'left' }
        if ($line -match $delimiter) {
            $dashes = '-' * $widths[$column]
            switch ($alignment) {
                'center' { ':' + $dashes.Substring(2) + ':' }
                'right' { $dashes.Substring(1) + ':' }
                default {
                    if ($column -lt $cells.Count -and $cells[$column].StartsWith(':')) { ':' + $dashes.Substring(1) } else { $dashes }
                }
            }
        }
        else {
            $text = if ($column -lt $cells.Count) { $cells[$column] } else { '' }
            Format-Cell $text $widths[$column] $alignment
        }
    }
    $indent + '| ' + ($formatted -join ' | ') + ' |'
}
