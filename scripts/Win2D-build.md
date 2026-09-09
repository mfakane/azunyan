# Win2D source build

The `external/Win2D` submodule is pinned to
`25680382dd2136779e10ea6084f0c5ba437ae288` (upstream version 1.4.0).

Initialize the exact source revision with:

```powershell
git submodule update --init external/Win2D
```

The upstream build requires the v143 C++ UWP build tools and Windows SDK
10.0.19041.0. Its `runbuild.ps1` hard-codes a Visual Studio 2019 path;
use `build.cmd` from a suitable Visual Studio developer command prompt instead,
with NuGet CLI available on PATH. On a Japanese-language machine, set
`NUGET_CLI_LANGUAGE=en-US`: the upstream NuGet version check matches English
output. See the submodule's build scripts for the complete build requirements.

The required tools were installed on 2026-09-10 into Visual Studio Enterprise
2026. Import [win2d.vsconfig](win2d.vsconfig) through Visual Studio Installer
to add the v143 UWP tools and v143 x64/x86 Spectre libraries. These components
installed MSVC 14.44.35207 alongside the existing compiler.

Windows SDK 19041 is not listed in this Visual Studio catalog. Obtain its
installer from the [official Windows SDK archive](https://learn.microsoft.com/en-us/windows/apps/windows-sdk/downloads-archive).
The installer version is 10.1.19041.685; its headers and libraries are installed
under `10.0.19041.0`. The following arguments installed the C++ and UWP
components (run with administrator privileges):

```text
/quiet /norestart /features OptionId.DesktopCPPx64 OptionId.DesktopCPPx86 OptionId.UWPCPP OptionId.UWPManaged
```

Both installers completed with exit code 0. The native `winrt.lib.uap.vcxproj`
Release/x64 build then succeeded without SDK or toolset overrides, producing
`bin/uapx64/release/winrt.lib.uap/winrt.lib`. No local package has been generated
or substituted for Azunyan's existing NuGet reference yet.

At this commit, `build/nuget/build-nupkg.cmd` explicitly distinguishes the
Microsoft signed distribution (EULA URL and license acceptance) from local
builds (repository license URL and no license acceptance). Preserve the pinned
source's `LICENSE.txt` with a locally built distribution and review the notices
for dependencies included in the resulting artifacts.
