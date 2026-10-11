# Azunote distributions

Run these PowerShell 7 scripts on Windows with the .NET 10 SDK, Visual Studio
C++ build tools, and Windows SDK installed. First initialize and build the
[pinned Win2D package](Win2D-build.md). MSIX and APPX creation use WinApp CLI
0.7.1. CI pins this version through `setup-WinAppCli`; the script prefers a
`winapp` executable on `PATH` and falls back to the application's restored
`Microsoft.Windows.SDK.BuildTools.WinApp` package at the same version. Explicit
APPX output and legacy certificate-store signing are routed through
`winapp tool`.

```powershell
git submodule update --init external/Win2D
./scripts/Build-Win2D.ps1
./scripts/Package-AzunoteZip.ps1
./scripts/Package-AzunoteMsix.ps1
./scripts/Package-AzunoteZip.ps1 -RuntimeIdentifier win-arm64
./scripts/Package-AzunoteMsix.ps1 -RuntimeIdentifier win-arm64
./scripts/Package-AzunoteMsixBundle.ps1
```

Both scripts publish Release for `-RuntimeIdentifier` (`win-x64` by default, or
`win-arm64`) as self-contained Native AOT deployments
using the development identity by default. Normal local builds and CI packages
display `Azunote (Dev)`, use package ID `45929MFAlie.Azunote.Dev`, the
`azu-dev.exe` execution alias, and `%LOCALAPPDATA%/Azunote.Dev` for user data.
They use a separate mutex and command pipe from the release application.
Pass `-Release` to either packaging script to create a production distribution;
the release workflow does this explicitly. For a direct production build,
pass `-p:AzunoteRelease=true`, regardless of the Debug/Release configuration.

Both scripts keep the self-contained Windows App SDK runtime as loose files. The ZIP
script additionally moves the published application into an `.app`
subdirectory and leaves a [publish shim](https://www.nuget.org/packages/PublishShim.MSBuild/)
named after each executable in its place, so the folder a user opens holds the
program and its documents rather than the runtime. Each run uses a new staging
directory under `artifacts/packaging` to avoid including stale files. PDBs
remain in staging but are omitted from distributions.

Each ZIP and MSIX/APPX is for one architecture, and its name ends in the
runtime identifier (`Azunote-<version>-win-arm64.msix`). The
scripts check that every executable, including the portable shims, is native
for that architecture, and that the MSIX/APPX manifest declares the matching
`ProcessorArchitecture` (`x64` or `arm64`). Cross-publishing `win-arm64` on an
x64 machine needs the MSVC ARM64 build tools
(`Microsoft.VisualStudio.Component.VC.Tools.ARM64`).
`./scripts/Test-AzunotePortableSmoke.ps1 -Path <zip> -RuntimeIdentifier <rid>`
extracts a ZIP on a machine of that architecture, checks its native images,
runs `azu --help`, and starts the editor briefly; CI runs it on ARM64.

Outputs default to `artifacts/distributions`, with a SHA-256 sidecar for each
archive. The default version comes from `src/Azunote/Package.appxmanifest`.
Existing output files are not overwritten. Neither script installs the app.

```powershell
./scripts/Package-AzunoteZip.ps1 -Version 0.2.0.0 -OutputDirectory C:/releases
./scripts/Package-AzunoteMsix.ps1 -Version 0.2.0.0 -Format appx
```

The ZIP contains a top-level `Azunote-<version>-<rid>` folder. Its visible
top level contains the `Azunote.exe` and `azu.exe` shims,
`Register-AzunoteCompletion.ps1`, `README.md`, `LICENSE`,
`THIRD-PARTY-NOTICES.md`, an empty `appdata/` directory, and `docs/` with the
user guides. The package README links to the bundled command-line, settings,
external-tools, and substitution-variable guides. Developer documentation is
not included. The redistribution-required `licenses/` directory is kept with
the notices. Build
artifacts such as DLLs, WinMDs, PRI files, satellite resources, and manifests
are in `.app` with the application the shims start; PDBs are not placed in the
ZIP at all. Extract the entire folder and run `Azunote.exe` or the adjacent
`azu.exe` client. Here "portable" means no installer or separately installed
.NET/Windows App SDK runtime is required, and nothing is extracted at
startup. Because the
ZIP includes `appdata/`, settings, application state, custom modes, and external
tools are stored below that directory instead of the normal
`%LOCALAPPDATA%/Azunote` directory. Removing `appdata/` before launching again
restores the normal user configuration location. This is a folder-local data
mode, not a separate USB-local settings mode.

## MSIX bundle

`./scripts/Package-AzunoteMsixBundle.ps1` combines the `win-x64` and
`win-arm64` MSIX packages into `Azunote-<version>.msixbundle`, from which
Windows installs the package matching the device. By default it reads both
packages from `artifacts/distributions` and writes the bundle there; it picks
the only version with a package for every architecture, or takes `-Version`,
`-InputDirectory`, or explicit `-Path` packages. It refuses packages whose
identity name, publisher, or version differ, or that do not cover exactly `x64`
and `arm64`. The bundle is made with `winapp tool makeappx bundle`, with the
bundle version set to the package version (`/bv`) instead of MakeAppx's
clock-based default.

After bundling, the script reads `AppxMetadata/AppxBundleManifest.xml`, checks
that it lists one application package per architecture, and opens each inner
package to check that its identity, version, and `ProcessorArchitecture` match
the bundle manifest. Like the MSIX script, it writes a SHA-256 sidecar, leaves
the bundle unsigned by default, and accepts `-CertificatePath` or
`-CertificateThumbprint` (with `-TimestampUrl`) to sign the bundle itself; the
inner packages need not be signed separately.

`./scripts/Test-AzunoteMsixBundle.ps1 -Path <bundle>` repeats the manifest
checks, verifies the sidecar, and unbundles with MakeAppx to confirm both
packages come back with the right architecture (`-SkipUnbundle` omits that
step). CI runs both scripts in the `Bundle MSIX (x64 + ARM64)` job after the
per-architecture packages and uploads the bundle as the `azunote-msixbundle`
artifact. The bundle is not installed in CI because it is unsigned.

## Package identity and signing

MSIX/APPX uses the existing manifest, replacing the executable token, version,
and architecture in the staging copy only. `-IdentityName` and `-Publisher`
override identity fields when needed for Store or organizational distribution.
For MSIX, `winapp pack` resolves the executable token and architecture. It uses
`--skip-pri` to preserve the package index prepared by the script, and
`--self-contained` to retain runtime bundling. Without the latter, winapp removes
runtime payload files and adds an external Windows App Runtime dependency even
when the publish folder already contains the runtime.
APPX uses `winapp tool makeappx`; the SDK build tools needed by winapp are
resolved by the CLI from the project, NuGet cache, or its configured tool cache.
MSIX/APPX staging also includes the package README and the same user-only
`docs/` directory beside the executable.

Both formats import the compiled `Azunote.pri` into the package-root
`resources.pri`, retaining WinUI/XBF resources and using the package identity
as the resource map name. The `resources.pri` from self-contained runtime
publish is not the app's index and does not index its shell icons. Packaging
checks all 14 target sizes and three variants for both AppList and file
association icons before packing.

To regenerate icons, run `./scripts/Set-AzunoteIconWithWinApp.ps1`. It derives
small SVGs from the master geometry, generates the PNG families, then combines
the exact-size AppList PNGs into a 14-frame `app.ico`. Rebuild and
reinstall the package to update shell assets; regeneration alone does not update
an installed app.

Run `./scripts/Test-AzunoteShellAssets.ps1 -PublishedDirectory <publish-folder>`
to verify the ICO frames, default/custom package identities, and preservation of
all compiled resource candidates, including embedded XBF data.

Without `-CertificateThumbprint`, the package is unsigned. Sign it before
sideloading. For a PFX certificate, use the WinApp CLI signing path:

```powershell
./scripts/Package-AzunoteMsix.ps1 -Version 0.2.0.0 `
    -Publisher 'CN=Your Publisher' `
    -CertificatePath C:/secure/devcert.pfx `
    -CertificatePassword password
```

The certificate subject must match the manifest publisher, and the installing
machine must trust it. `-TimestampUrl` optionally supplies an RFC 3161 timestamp
service. The script verifies the signature through `winapp tool signtool`; it
never creates certificates or changes trust settings. The existing
`-CertificateThumbprint` parameter remains supported through the same CLI tool
wrapper for callers using the current user's `My` certificate store. Unsigned
packages can also be handed to an external signing service.

## Draft release

`.github/workflows/release.yml` is started by hand. Enter a `major.minor`
version, such as `0.2`. It tests the selected commit, packages unsigned MSIX
and portable ZIPs for both architectures as `major.minor.<run number>.0`,
bundles the MSIX packages, then tags `v<major>.<minor>.<run number>` and opens
a draft release with the MSIX bundle and the `win-x64` and `win-arm64` ZIPs,
each with its SHA-256 sidecar. The bundle is the release's only installer;
per-architecture MSIX packages remain in the workflow's CI artifacts. A failed
package or bundle does not create the tag.

The scripts verify the generated notice inventory before publishing. The
distribution review items in `THIRD-PARTY-NOTICES.md` still apply.
