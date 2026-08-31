$scriptArguments = @($args)
$exePath = Join-Path -Path $PSScriptRoot -ChildPath 'Azunote.exe'

if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
    Write-Error "Azunote.exe was not found next to this script: $exePath"
    exit 1
}

$waitForExit = $false
$optionsAllowed = $true
foreach ($argument in $scriptArguments) {
    if ($optionsAllowed -and $argument -eq '--') {
        $optionsAllowed = $false
        continue
    }

    if ($optionsAllowed -and (
            $argument -eq '-w' -or
            $argument -eq '--wait' -or
            $argument -eq '-h' -or
            $argument -eq '--help')) {
        $waitForExit = $true
    }
}

function ConvertTo-WindowsCommandLineArgument {
    param(
        [AllowEmptyString()]
        [string] $Value
    )

    if ($null -eq $Value -or $Value.Length -eq 0) {
        return '""'
    }

    if ($Value -notmatch '[\s"]') {
        return $Value
    }

    $builder = New-Object System.Text.StringBuilder
    [void]$builder.Append('"')
    $backslashCount = 0

    foreach ($character in $Value.ToCharArray()) {
        if ($character -eq '\') {
            $backslashCount++
            continue
        }

        if ($character -eq '"') {
            for ($index = 0; $index -lt (($backslashCount * 2) + 1); $index++) {
                [void]$builder.Append('\')
            }

            [void]$builder.Append('"')
            $backslashCount = 0
            continue
        }

        for ($index = 0; $index -lt $backslashCount; $index++) {
            [void]$builder.Append('\')
        }

        [void]$builder.Append($character)
        $backslashCount = 0
    }

    for ($index = 0; $index -lt ($backslashCount * 2); $index++) {
        [void]$builder.Append('\')
    }

    [void]$builder.Append('"')
    return $builder.ToString()
}

$startInfo = New-Object System.Diagnostics.ProcessStartInfo
$startInfo.FileName = $exePath
$startInfo.UseShellExecute = $false
$quotedArguments = @(
    $scriptArguments | ForEach-Object {
        ConvertTo-WindowsCommandLineArgument ([string]$_)
    }
)
$startInfo.Arguments = [string]::Join(' ', $quotedArguments)

try {
    $process = [System.Diagnostics.Process]::Start($startInfo)
    if ($waitForExit) {
        $process.WaitForExit()
        $exitCode = $process.ExitCode
        $process.Dispose()
        exit $exitCode
    }

    $process.Dispose()
    exit 0
}
catch {
    Write-Error "Could not start Azunote.exe: $($_.Exception.Message)"
    exit 1
}
