#requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Where the portable distribution keeps the published application, matching
# PublishShimDirectory in src/Azunote/Azunote.csproj.
$AzunoteShimDirectory = '.app'

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

function Publish-AzunoteDistribution($Context, [switch] $Portable) {
    & (Join-Path $PSScriptRoot 'Generate-ThirdPartyNotices.ps1') -Check
    $project = Join-Path $Context.Repo 'src/Azunote/Azunote.csproj'
    $publishProperties = @(
        '-p:WindowsPackageType=None'
        '-p:EnableMsixTooling=true'
        '-p:PublishAot=true'
        '-p:PublishTrimmed=true'
    )
    if ($Portable) {
        # Native AOT leaves the Windows App SDK native payload as loose files.
        # The portable distribution keeps them, and everything else the publish
        # produced, in a subdirectory that shims named after the executables
        # start, so the folder a user opens holds what they came for.
        $publishProperties += @(
            '-p:AzunotePortableShim=true'
            '-p:WindowsAppSdkUndockedRegFreeWinRTInitialize=true'
        )
    }
    $previousPath = $env:PATH
    try {
        # Native AOT's linker discovery invokes vswhere by name.
        $env:PATH = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer;$previousPath"
        & dotnet publish $project -c Release -r win-x64 --self-contained true `
            @publishProperties `
            "-p:Version=$($Context.Version)" -o $Context.Publish
        if ($LASTEXITCODE) { throw "Azunote publish failed ($LASTEXITCODE)." }
    } finally { $env:PATH = $previousPath }
    # The console client is published as its own executable and bundled next
    # to the editor, so both distribution modes carry the two of them. The
    # legal documents are read from beside the executable a user launched, so
    # they stay in the publish root even when the application does not.
    $executables = @('Azunote.exe', 'azu.exe')
    $rootFiles = $executables + @('LICENSE', 'THIRD-PARTY-NOTICES.md', 'licenses/sources.json')
    $application = $Context.Publish
    $requiredFiles = $rootFiles
    if ($Portable) {
        $application = Join-Path $Context.Publish $AzunoteShimDirectory
        $requiredFiles += $executables | ForEach-Object { Join-Path $AzunoteShimDirectory $_ }
    }
    foreach ($file in $requiredFiles) {
        if (!(Test-Path -LiteralPath (Join-Path $Context.Publish $file))) {
            throw "Publish output is missing $file."
        }
    }
    if ($Portable) {
        # Nothing but the shims and the documents they are read beside may be
        # left where a user looks; the publish output belongs behind them.
        $unexpectedFiles = @(Get-ChildItem -LiteralPath $Context.Publish -File |
            Where-Object { $_.Extension -ne '.pdb' -and $_.Name -notin $rootFiles })
        if ($unexpectedFiles.Count) {
            throw "Portable publish left loose files beside the shims: $(($unexpectedFiles.Name) -join ', ')"
        }
    }
    # Every distribution mode must produce an executable, not a loose apphost.
    if (Test-Path (Join-Path $application 'Azunote.dll')) {
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

function New-AzunotePackageResources($Context, [string] $Payload, [string] $WinApp, [string] $IdentityName) {
    $appPri = Join-Path $Payload 'Azunote.pri'
    if (!(Test-Path -LiteralPath $appPri -PathType Leaf)) {
        throw 'Publish output is missing Azunote.pri; shell icons cannot be indexed.'
    }

    # Self-contained publish also ships a resources.pri belonging to the Windows
    # App Runtime. The shell needs the app's index at this well-known filename.
    # Import the compiled PRI instead of re-indexing loose files: this preserves
    # embedded XBF/WinUI resources and remaps the root for -IdentityName overrides.
    $config = Join-Path $PSScriptRoot 'PackageResources.priconfig.xml'
    $packagePri = Join-Path $Context.Work 'package-resources.pri'
    & $WinApp tool makepri new /pr $Payload /cf $config /in $IdentityName /of $packagePri
    if ($LASTEXITCODE) { throw "Package PRI generation failed ($LASTEXITCODE)." }

    $dump = Join-Path $Context.Work 'package-resources.xml'
    & $WinApp tool makepri dump /if $packagePri /of $dump /dt detailed
    if ($LASTEXITCODE) { throw "Package PRI inspection failed ($LASTEXITCODE)." }
    [xml] $index = Get-Content -LiteralPath $dump -Raw
    $map = $index.SelectSingleNode('/PriInfo/ResourceMap[@primary="true"]')
    if ($null -eq $map -or $map.GetAttribute('name') -ne $IdentityName) {
        throw "Package PRI does not contain the primary map for $IdentityName."
    }
    foreach ($family in @('AppList', 'FileAssociation')) {
        $resource = $map.SelectSingleNode("ResourceMapSubtree[@name='Files']/ResourceMapSubtree[@name='Assets']/NamedResource[@name='$family.png']")
        if ($null -eq $resource) { throw "Package PRI has no Assets/$family.png resource." }
        foreach ($size in @(16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256)) {
            foreach ($form in @('', 'UNPLATED', 'LIGHTUNPLATED')) {
                $formPredicate = if ($form) {
                    "QualifierSet/Qualifier[@name='AlternateForm' and @value='$form']"
                } else {
                    "not(QualifierSet/Qualifier[@name='AlternateForm'])"
                }
                $candidate = $resource.SelectSingleNode("Candidate[QualifierSet/Qualifier[@name='TargetSize' and @value='$size'] and $formPredicate]")
                if ($null -eq $candidate -or
                    !(Test-Path -LiteralPath (Join-Path $Payload $candidate.Value) -PathType Leaf)) {
                    throw "Package PRI is missing a usable $family ${size}px '$form' candidate. Regenerate assets and publish again."
                }
            }
        }
    }
    Copy-Item -LiteralPath $packagePri -Destination (Join-Path $Payload 'resources.pri')
}

function Copy-AzunotePortablePayload($Context, [string] $Destination) {
    # The publish output already has the shape the distribution wants: the
    # shims and the legal texts that must accompany a redistribution at the
    # top, everything the publish produced behind them.
    Copy-AzunotePayload $Context $Destination
    New-Item -ItemType Directory -Force (Join-Path $Destination 'appdata') | Out-Null

    $files = @(
        @{ Source = Join-Path $Context.Repo 'src/Azunote/README.md'; Name = 'README.md' }
        @{ Source = Join-Path $Context.Repo 'src/Azunote/Register-AzunoteCompletion.ps1'
           Name = 'Register-AzunoteCompletion.ps1' }
    )
    foreach ($file in $files) {
        if (!(Test-Path -LiteralPath $file.Source -PathType Leaf)) {
            throw "Portable distribution source is missing $($file.Name)."
        }
        Copy-Item -LiteralPath $file.Source -Destination (Join-Path $Destination $file.Name)
    }
}

function Find-AzunoteWinApp($Context, [string] $PreferredPath) {
    if ($PreferredPath) {
        $resolved = Resolve-Path -LiteralPath $PreferredPath -ErrorAction SilentlyContinue
        if (!$resolved) { throw "WinApp CLI was not found: $PreferredPath" }
        return $resolved.Path
    }

    # Prefer the version installed by setup-WinAppCli or another explicit PATH setup.
    foreach ($commandName in @('winapp', 'winapp.exe')) {
        $command = Get-Command $commandName -CommandType Application -ErrorAction SilentlyContinue
        if ($command) { return $command.Source }
    }

    # Keep a local fallback for developers who only restore the application's package.
    $assets = Get-Content (Join-Path $Context.Repo 'src/Azunote/obj/project.assets.json') -Raw | ConvertFrom-Json
    $library = $assets.libraries.PSObject.Properties |
        Where-Object Name -Like 'Microsoft.Windows.SDK.BuildTools.WinApp/*' | Select-Object -First 1
    if (!$library) { throw 'WinApp CLI was not found on PATH or in the restored application dependencies.' }
    foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
        $tool = Join-Path $folder "$($library.Value.path)/tools/win-x64/winapp.exe"
        if (Test-Path -LiteralPath $tool) { return $tool }
    }
    throw 'The restored WinApp CLI executable is missing.'
}

function Complete-AzunoteDistribution([string] $Path) {
    $hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($Path))" | Set-Content "$Path.sha256" -Encoding utf8
    Write-Host "Created $Path"
    Write-Host "SHA256: $hash"
}
