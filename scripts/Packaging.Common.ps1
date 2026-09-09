#requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function New-AzunoteDistribution([string] $OutputDirectory, [string] $Version) {
    $repo = Split-Path $PSScriptRoot -Parent
    [xml] $manifest = Get-Content (Join-Path $repo 'src/Azunote/Package.appxmanifest') -Raw
    if (!$Version) { $Version = $manifest.Package.Identity.Version }
    if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$' -or
        @($Version.Split('.') | Where-Object { [long]$_ -gt 65535 }).Count) {
        throw 'Version must contain four integers between 0 and 65535.'
    }
    if (!$OutputDirectory) { $OutputDirectory = Join-Path $repo 'artifacts/distributions' }
    $output = [IO.Path]::GetFullPath($OutputDirectory)
    New-Item -ItemType Directory -Force $output | Out-Null
    $work = Join-Path $repo ('artifacts/packaging/' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force $work | Out-Null
    return [pscustomobject]@{
        Repo = $repo; Output = $output; Work = $work; Version = $Version
        Publish = Join-Path $work 'publish'; Manifest = $manifest
    }
}

function Publish-AzunoteDistribution($Context) {
    & (Join-Path $PSScriptRoot 'Generate-ThirdPartyNotices.ps1') -Check
    $project = Join-Path $Context.Repo 'src/Azunote/Azunote.csproj'
    $previousPath = $env:PATH
    try {
        # Native AOT's linker discovery invokes vswhere by name.
        $env:PATH = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer;$previousPath"
        & dotnet publish $project -c Release -r win-x64 --self-contained true `
            -p:PublishAot=true -p:PublishTrimmed=true -p:WindowsPackageType=None `
            "-p:Version=$($Context.Version)" -o $Context.Publish
        if ($LASTEXITCODE) { throw "Native AOT publish failed ($LASTEXITCODE)." }
    } finally { $env:PATH = $previousPath }
    foreach ($file in @('Azunote.exe', 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'licenses/sources.json')) {
        if (!(Test-Path -LiteralPath (Join-Path $Context.Publish $file))) {
            throw "Publish output is missing $file."
        }
    }
    # Verify this is a native executable, rather than silently packaging an apphost.
    if (Test-Path (Join-Path $Context.Publish 'Azunote.dll')) {
        throw 'Managed Azunote.dll found in Native AOT output.'
    }
}

function Copy-AzunotePayload($Context, [string] $Destination) {
    New-Item -ItemType Directory -Force $Destination | Out-Null
    foreach ($file in Get-ChildItem -LiteralPath $Context.Publish -File -Recurse) {
        if ($file.Extension -eq '.pdb') { continue }
        $relative = [IO.Path]::GetRelativePath($Context.Publish, $file.FullName)
        $target = Join-Path $Destination $relative
        New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target
    }
}

function Find-AzunoteWinApp($Context) {
    # Use the CLI version restored by the application, not an arbitrary PATH version.
    $assets = Get-Content (Join-Path $Context.Repo 'src/Azunote/obj/project.assets.json') -Raw | ConvertFrom-Json
    $library = $assets.libraries.PSObject.Properties |
        Where-Object Name -Like 'Microsoft.Windows.SDK.BuildTools.WinApp/*' | Select-Object -First 1
    if (!$library) { throw 'WinApp CLI was not found in the restored application dependencies.' }
    foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
        $tool = Join-Path $folder "$($library.Value.path)/tools/win-x64/winapp.exe"
        if (Test-Path -LiteralPath $tool) { return $tool }
    }
    throw 'The restored WinApp CLI executable is missing.'
}

function Find-AzunoteSdkTool([string] $Name) {
    $sdkBin = "${env:ProgramFiles(x86)}/Windows Kits/10/bin"
    $tool = Get-ChildItem $sdkBin -Directory | Where-Object Name -Match '^10\.0\.\d+\.\d+$' |
        Sort-Object { [version]$_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName "x64/$Name" } |
        Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (!$tool) { throw "Install the Windows SDK: $Name was not found." }
    return $tool
}

function Complete-AzunoteDistribution([string] $Path) {
    $hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($Path))" | Set-Content "$Path.sha256" -Encoding utf8
    Write-Host "Created $Path"
    Write-Host "SHA256: $hash"
}
