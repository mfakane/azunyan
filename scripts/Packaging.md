# Azunote distributions

Run these PowerShell 7 scripts on Windows with the .NET 10 SDK, Visual Studio
C++ build tools, and Windows SDK installed. First initialize and build the
[pinned Win2D package](Win2D-build.md). MSIX and APPX creation use the winapp
CLI (currently 0.6.1). The script prefers a `winapp` executable on `PATH`, such
as one installed by `setup-WinAppCli`, and falls back to the application's
restored `Microsoft.Windows.SDK.BuildTools.WinApp` package. Explicit APPX
output and legacy certificate-store signing are routed through `winapp tool`.

```powershell
git submodule update --init external/Win2D
./scripts/Build-Win2D.ps1
./scripts/Package-AzunoteZip.ps1
./scripts/Package-AzunoteMsix.ps1
```

Both scripts publish Release/win-x64 as self-contained deployments. The
MSIX/APPX script uses Native AOT and keeps the self-contained Windows App SDK
runtime as loose package files. The ZIP script uses the managed Windows App SDK
single-file mode (without Native AOT), so the native runtime, WinUI resources,
and application content are bundled into `Azunote.exe` and extracted to a
temporary directory when it starts. Each run uses a new staging directory
under `artifacts/packaging` to avoid including stale files. PDBs remain in
staging but are omitted from distributions.

Outputs default to `artifacts/distributions`, with a SHA-256 sidecar for each
archive. The default version comes from `src/Azunote/Package.appxmanifest`.
Existing output files are not overwritten. Neither script installs the app.

```powershell
./scripts/Package-AzunoteZip.ps1 -Version 0.2.0.0 -OutputDirectory C:/releases
./scripts/Package-AzunoteMsix.ps1 -Version 0.2.0.0 -Format appx
```

The ZIP contains a top-level `Azunote-<version>-win-x64` folder. Its visible
top level contains `Azunote.exe`, the `azu.exe` console client,
`Register-AzunoteCompletion.ps1`, `README.md`, `LICENSE`,
`THIRD-PARTY-NOTICES.md`, and an empty `appdata/` directory. The
redistribution-required `licenses/` directory is kept with the notices; build
artifacts such as DLLs, WinMDs, PRI files, satellite resources, manifests, and
PDBs are not placed in the ZIP. Extract the entire folder and run
`Azunote.exe` or the adjacent `azu.exe` client. Here "portable" means no
installer or separately installed .NET/Windows App SDK runtime is required;
the first launch extracts the bundled runtime under `%TEMP%/.net`. Because the
ZIP includes `appdata/`, settings, application state, custom modes, and external
tools are stored below that directory instead of the normal
`%LOCALAPPDATA%/Azunote` directory. Removing `appdata/` before launching again
restores the normal user configuration location. This is a folder-local data
mode, not a separate USB-local settings mode.

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

The scripts verify the generated notice inventory before publishing. The
distribution review items in `THIRD-PARTY-NOTICES.md` still apply.
