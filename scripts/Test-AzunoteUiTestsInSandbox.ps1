#requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [string]$DotNetRoot,
    [string]$OutputRoot,
    [switch]$NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = Split-Path $PSScriptRoot -Parent
$solution = Join-Path $repo 'Azunyan.slnx'
$wsbCommand = Get-Command wsb.exe -ErrorAction SilentlyContinue
if (!$wsbCommand) {
    throw 'wsb.exe was not found. Install Windows Sandbox on Windows 11 version 24H2 or later.'
}

if (!$DotNetRoot) {
    $dotnetCommand = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if (!$dotnetCommand) { throw 'dotnet.exe was not found.' }
    $DotNetRoot = Split-Path $dotnetCommand.Source -Parent
}

$DotNetRoot = [IO.Path]::GetFullPath($DotNetRoot)
$dotnet = Join-Path $DotNetRoot 'dotnet.exe'
if (!(Test-Path -LiteralPath $dotnet)) {
    throw "The portable .NET SDK was not found at $dotnet."
}
$installedSdks = @(& $dotnet --list-sdks)
if ($LASTEXITCODE -or !($installedSdks | Where-Object { $_ -match '^10\.' })) {
    throw "A .NET 10 SDK was not found in $DotNetRoot."
}

if (!$OutputRoot) {
    $OutputRoot = Join-Path $repo 'artifacts/ui-tests-sandbox'
}

$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$targetFramework = 'net10.0-windows10.0.19041.0'
$appOutput = Join-Path $repo "src/Azunote/bin/$Configuration/$targetFramework/win-x64"
$testOutput = Join-Path $repo "tests/Azunote.UiTests/bin/$Configuration/$targetFramework/win-x64"
if (!$NoBuild) {
    & $dotnet build $solution -c $Configuration
    if ($LASTEXITCODE) { throw 'The host build failed.' }
}

if (!(Test-Path -LiteralPath (Join-Path $appOutput 'Azunote.exe'))) {
    throw "Azunote was not found in $appOutput. Build it first or omit -NoBuild."
}
if (!(Test-Path -LiteralPath (Join-Path $testOutput 'Azunote.UiTests.dll'))) {
    throw "The UI test assembly was not found in $testOutput. Build it first or omit -NoBuild."
}

$runId = ([Guid]::NewGuid().ToString('N')).Substring(0, 8)
$runDirectory = Join-Path $OutputRoot "$(Get-Date -Format 'yyyyMMdd-HHmmss')-$runId"
New-Item -ItemType Directory -Force -Path $runDirectory | Out-Null

$guestScript = @'
$ErrorActionPreference = 'Stop'
$output = 'C:\test-output'
$work = 'C:\azunyan-test'
$localRepo = Join-Path $work 'repo'
$log = Join-Path $output 'sandbox-run.log'

try {
    "Started: $(Get-Date -Format o)" | Set-Content -LiteralPath $log -Encoding utf8
    $env:DOTNET_ROOT = 'C:\portable-dotnet'
    $env:PATH = "$env:DOTNET_ROOT;$env:PATH"
    $env:DOTNET_CLI_HOME = Join-Path $work 'dotnet-home'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:DOTNET_NOLOGO = '1'
    $env:AZUNOTE_UI_TESTS = '1'
    $env:AZUNOTE_EXE = Join-Path $work 'app\Azunote.exe'

    New-Item -ItemType Directory -Force -Path $work, $localRepo, $env:DOTNET_CLI_HOME | Out-Null
    & (Join-Path $env:DOTNET_ROOT 'dotnet.exe') --info 2>&1 |
        Out-File -LiteralPath $log -Append -Encoding utf8
    if ($LASTEXITCODE) { throw "Portable dotnet failed with exit code $LASTEXITCODE." }

    $framework = 'net10.0-windows10.0.19041.0'
    $configuration = '__CONFIGURATION__'
    $appSource = "C:\azunyan-input\src\Azunote\bin\$configuration\$framework\win-x64"
    $testSource = "C:\azunyan-input\tests\Azunote.UiTests\bin\$configuration\$framework\win-x64"
    $localTests = Join-Path $localRepo "tests\Azunote.UiTests\bin\$configuration\$framework\win-x64"

    & robocopy.exe $appSource (Join-Path $work 'app') /E /NFL /NDL /NJH /NJS /NP 2>&1 |
        Out-File -LiteralPath $log -Append -Encoding utf8
    if ($LASTEXITCODE -gt 7) { throw "App copy failed with robocopy exit code $LASTEXITCODE." }
    & robocopy.exe $testSource $localTests /E /NFL /NDL /NJH /NJS /NP 2>&1 |
        Out-File -LiteralPath $log -Append -Encoding utf8
    if ($LASTEXITCODE -gt 7) { throw "Test copy failed with robocopy exit code $LASTEXITCODE." }

    Copy-Item -LiteralPath 'C:\azunyan-input\Azunyan.slnx' -Destination (Join-Path $localRepo 'Azunyan.slnx') -Force
    $testAssets = Join-Path $localRepo 'tests\Azunote.UiTests\TestAssets'
    New-Item -ItemType Directory -Force -Path $testAssets | Out-Null
    Copy-Item -Path 'C:\azunyan-input\tests\Azunote.UiTests\TestAssets\*' -Destination $testAssets -Force

    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    & (Join-Path $env:DOTNET_ROOT 'dotnet.exe') vstest (Join-Path $localTests 'Azunote.UiTests.dll') `
        '/Logger:trx;LogFileName=azunote-ui.trx' "/ResultsDirectory:$output" 2>&1 |
        Out-File -LiteralPath $log -Append -Encoding utf8
    $testExitCode = $LASTEXITCODE
    $ErrorActionPreference = $previousErrorActionPreference
    if ($testExitCode) { throw "UI tests failed with exit code $testExitCode." }

    '0' | Set-Content -LiteralPath (Join-Path $output 'exit-code.txt') -Encoding ascii
}
catch {
    $_ | Out-String | Out-File -LiteralPath $log -Append -Encoding utf8
    '1' | Set-Content -LiteralPath (Join-Path $output 'exit-code.txt') -Encoding ascii
    exit 1
}

exit 0
'@.Replace('__CONFIGURATION__', $Configuration)

$guestScriptPath = Join-Path $runDirectory 'run-ui-tests.ps1'
[IO.File]::WriteAllText($guestScriptPath, $guestScript, [Text.UTF8Encoding]::new($false))

$wsb = $wsbCommand.Source
$sandboxId = [Guid]::NewGuid().ToString()
try {
    $config = '<Configuration><Networking>Disable</Networking><ClipboardRedirection>Disable</ClipboardRedirection><VGpu>Disable</VGpu></Configuration>'
    & $wsb start --id $sandboxId --config $config --raw | Out-Null
    if ($LASTEXITCODE) { throw 'Windows Sandbox did not start.' }

    & $wsb share --id $sandboxId --host-path $DotNetRoot --sandbox-path 'C:\portable-dotnet' --raw
    if ($LASTEXITCODE) { throw 'Could not share the portable .NET SDK with Windows Sandbox.' }
    & $wsb share --id $sandboxId --host-path $repo --sandbox-path 'C:\azunyan-input' --raw
    if ($LASTEXITCODE) { throw 'Could not share the repository with Windows Sandbox.' }
    & $wsb share --id $sandboxId --host-path $runDirectory --sandbox-path 'C:\test-output' --allow-write --raw
    if ($LASTEXITCODE) { throw 'Could not share the test output directory with Windows Sandbox.' }

    & $wsb connect --id $sandboxId --raw
    if ($LASTEXITCODE) { throw 'Could not open the Windows Sandbox interactive session.' }
    $execDeadline = [DateTime]::UtcNow.AddSeconds(60)
    $executed = $null
    $execOutput = ''
    do {
        $previousErrorActionPreference = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try {
            $execOutput = & $wsb exec --id $sandboxId `
                --command 'powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File C:\test-output\run-ui-tests.ps1' `
                --run-as ExistingLogin --raw 2>&1 | Out-String
            $execExitCode = $LASTEXITCODE
        }
        finally {
            $ErrorActionPreference = $previousErrorActionPreference
        }

        if (!$execExitCode) {
            $executed = $execOutput | ConvertFrom-Json
            break
        }
        if (Test-Path -LiteralPath (Join-Path $runDirectory 'sandbox-run.log')) {
            $executed = [pscustomobject]@{ ExitCode = 1 }
            break
        }

        Start-Sleep -Seconds 2
    } while ([DateTime]::UtcNow -lt $execDeadline)

    if (!$executed) {
        [IO.File]::WriteAllText((Join-Path $runDirectory 'sandbox-exec-error.log'), $execOutput)
        throw "The Windows Sandbox interactive login did not become ready. See $runDirectory."
    }
    if ($executed.ExitCode) {
        throw "Sandbox UI tests failed. See $runDirectory."
    }

    $exitCodePath = Join-Path $runDirectory 'exit-code.txt'
    if (!(Test-Path -LiteralPath $exitCodePath) -or (Get-Content -LiteralPath $exitCodePath -Raw).Trim() -ne '0') {
        throw "Sandbox UI tests did not report success. See $runDirectory."
    }

    Write-Host "Sandbox UI tests passed. Results: $runDirectory"
}
finally {
    & $wsb stop --id $sandboxId --raw 2>$null | Out-Null
}
