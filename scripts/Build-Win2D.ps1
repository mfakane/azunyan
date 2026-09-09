#requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$source = Join-Path $repo 'external/Win2D'
$commit = '25680382dd2136779e10ea6084f0c5ba437ae288'
$version = '1.4.0-azunyan.25680382dd21'
$feed = Join-Path $repo 'artifacts/packages'
$nuget = Join-Path $source 'build/nuget/nuget.exe'
$msbuild = & "${env:ProgramFiles(x86)}/Microsoft Visual Studio/Installer/vswhere.exe" -latest -products '*' -requires Microsoft.VisualStudio.ComponentGroup.UWP.VC.v143 -find 'MSBuild\Current\Bin\MSBuild.exe'
if (!$msbuild) { throw 'Install scripts/win2d.vsconfig and Windows SDK 19041 first.' }
if ((& git -C $source rev-parse HEAD) -ne $commit) { throw 'Initialize the pinned Win2D submodule first.' }
if (& git -C $source status --porcelain --untracked-files=no) { throw 'Win2D source has local changes.' }
if (!(Test-Path $nuget)) {
    Invoke-WebRequest 'https://dist.nuget.org/win-x86-commandline/v6.14.0/nuget.exe' -OutFile $nuget
}
if ((Get-AuthenticodeSignature $nuget).Status -ne 'Valid') { throw 'NuGet CLI signature is invalid.' }
New-Item -ItemType Directory -Force $feed | Out-Null
$previousLanguage = $env:NUGET_CLI_LANGUAGE
Push-Location $source
try {
    $env:NUGET_CLI_LANGUAGE = 'en-US'
    & $msbuild Win2D.proj /p:BuildPlatforms=x64 /p:BuildConfigurations=Release /p:BuildTests=false /p:BuildTools=false /p:BuildDocs=false /p:RunTests=false /nr:false /v:minimal
    if ($LASTEXITCODE) { throw 'Win2D native build failed.' }
    & $msbuild winrt/projection/winrt.projection.csproj /restore /p:Configuration=Release /p:Platform=x64 /p:BuildProjectReferences=false /p:EnableSourceControlManagerQueries=false /p:EnableSourceLink=false /nr:false /v:minimal
    if ($LASTEXITCODE) { throw 'Win2D projection build failed.' }
    # Package only the x64/.NET assets used by Azunyan, preserving upstream build integration.
    $spec = @"
<?xml version="1.0"?>
<package>
  <metadata>
    <id>Microsoft.Graphics.Win2D</id>
    <version>$version</version>
    <authors>Microsoft</authors>
    <description>Win2D built from pinned source for Azunyan; win-x64 only.</description>
    <license type="file">LICENSE.txt</license>
    <repository type="git" url="https://github.com/microsoft/Win2D" commit="$commit" />
    <dependencies><dependency id="Microsoft.WindowsAppSDK.WinUI" version="1.8.260204000" /></dependencies>
  </metadata>
  <files>
    <file src="LICENSE.txt" target="LICENSE.txt" />
    <file src="obj/Win2D.githash.txt" target="Win2D.githash.txt" />
    <file src="bin/uapx64/release/winrt.dll.uap/Microsoft.Graphics.Canvas.dll" target="runtimes/win-x64/native" />
    <file src="bin/uapx64/release/winrt.dll.uap/Microsoft.Graphics.Canvas.winmd" target="lib/uap10.0" />
    <file src="bin/x64/release/winrt.projection/Microsoft.Graphics.Canvas.Interop.dll" target="lib/net6.0-windows10.0.19041.0" />
    <file src="build/nuget/Microsoft.Graphics.Win2D-WinUI3.props" target="buildTransitive/net6.0-windows10.0.19041.0/Microsoft.Graphics.Win2D.props" />
    <file src="build/nuget/Microsoft.Graphics.Win2D-WinUI3.targets" target="buildTransitive/net6.0-windows10.0.19041.0/Microsoft.Graphics.Win2D.targets" />
  </files>
</package>
"@
    $specPath = Join-Path $source 'obj/Azunyan.Win2D.nuspec'
    [IO.File]::WriteAllText($specPath, $spec)
    & $nuget pack $specPath -BasePath $source -OutputDirectory $feed -NoPackageAnalysis -NonInteractive
    if ($LASTEXITCODE) { throw 'Win2D package creation failed.' }
} finally {
    Pop-Location
    $env:NUGET_CLI_LANGUAGE = $previousLanguage
}
