# Azunote Native AOT distributions

Run these PowerShell 7 scripts on Windows with the .NET 10 SDK, Visual Studio
C++ build tools, and Windows SDK installed. First initialize and build the
[pinned Win2D package](Win2D-build.md). MSIX creation uses the winapp CLI from
the application's restored `Microsoft.Windows.SDK.BuildTools.WinApp` package
(currently 0.6.1); a global CLI installation is not required. Explicit APPX
output uses the Windows SDK's `makeappx.exe`; signing uses `signtool.exe`.

```powershell
git submodule update --init external/Win2D
./scripts/Build-Win2D.ps1
./scripts/Package-AzunoteZip.ps1
./scripts/Package-AzunoteMsix.ps1
```

Both scripts publish Release/win-x64 with Native AOT and a self-contained .NET
deployment. The application project enables self-contained Windows App SDK.
Each run uses a new staging directory under `artifacts/packaging` to avoid
including stale files. PDBs remain in staging but are omitted from distributions.
The executable, native dependencies, assets, default configuration, launchers,
`LICENSE`, `THIRD-PARTY-NOTICES.md`, and `licenses/` are preserved.

Outputs default to `artifacts/distributions`, with a SHA-256 sidecar for each
archive. The default version comes from `src/Azunote/Package.appxmanifest`.
Existing output files are not overwritten. Neither script installs the app.

```powershell
./scripts/Package-AzunoteZip.ps1 -Version 0.2.0.0 -OutputDirectory C:/releases
./scripts/Package-AzunoteMsix.ps1 -Version 0.2.0.0 -Format appx
```

The ZIP contains a top-level `Azunote-<version>-win-x64` folder. Extract the
entire folder and run `Azunote.exe` or the adjacent `azu` launchers. Here
"portable" means no installer or separately installed .NET/Windows App SDK
runtime is required; settings still use Azunote's normal user configuration
location. It does not enable a separate USB-local settings mode.

## Package identity and signing

MSIX/APPX uses the existing manifest, replacing the executable token, version,
and architecture in the staging copy only. `-IdentityName` and `-Publisher`
override identity fields when needed for Store or organizational distribution.
For MSIX, `winapp pack` resolves the executable token and architecture. It uses
`--skip-pri` to preserve the resources produced by `dotnet publish`, and
`--self-contained` to retain runtime bundling. Without the latter, winapp removes
runtime payload files and adds an external Windows App Runtime dependency even
when the publish folder already contains the runtime.
APPX retains the direct MakeAppx path with validation enabled. The SDK build
tools needed by winapp are resolved by the CLI from the project and NuGet cache.

Without `-CertificateThumbprint`, the package is unsigned. Sign it before
sideloading. To use an existing code-signing certificate with a private key in
the current user's `My` certificate store:

```powershell
./scripts/Package-AzunoteMsix.ps1 -Version 0.2.0.0 `
    -Publisher 'CN=Your Publisher' -CertificateThumbprint YOUR_THUMBPRINT
```

The certificate subject must match the manifest publisher, and the installing
machine must trust it. `-TimestampUrl` optionally supplies an RFC 3161 timestamp
service. The script verifies the signature after signing; it never creates
certificates or changes trust settings. Unsigned packages can also be handed to
an external signing service.

The scripts verify the generated notice inventory before publishing. The
distribution review items in `THIRD-PARTY-NOTICES.md` still apply.
