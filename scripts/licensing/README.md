# Generating third-party notices

Run from the repository root with PowerShell 7:

```powershell
pwsh -File scripts/Generate-ThirdPartyNotices.ps1
pwsh -File scripts/Generate-ThirdPartyNotices.ps1 -Check
```

The generator writes `THIRD-PARTY-NOTICES.md`, `licenses/sources.json`, and
the upstream documents listed in `manifest.json`. `LICENSE` is the manually
maintained zlib license for this project and is not generated.

`-Check` is offline and read-only. It fails on missing or changed documents
or stale generated Markdown/JSON. Use `-OutputDirectory <directory>` to
regenerate into a separate directory. Output is deterministic: the review
date comes from the manifest, not the current clock.

Generation first uses hash-verified committed documents, then the NuGet cache
(`NUGET_PACKAGES` or the usual user-profile location). Missing source documents
are fetched from the exact NuGet package or recorded upstream URL. Downloads
must match the reviewed SHA-256 hash; a mismatch stops generation. All sources
are resolved before outputs are written. Generation does not delete old files.

## Updating the reviewed snapshot

This is a generator for a **reviewed, pinned inventory**, not automatic legal
analysis or discovery of new dependencies. Restore the application and review
`src/Azunote/obj/project.assets.json`, including runtime download dependencies,
and the actual Release publish output when dependencies or deployment settings
change. The script does not automatically compare the inventory to that graph.

Update `manifest.json` with the reviewed component descriptions, document paths,
package-relative paths or upstream URLs, and SHA-256 hashes. Update its review
date and target settings, and update `notices.template.md` when the conclusions
or open release checks change. Regenerate, inspect the diff, and run `-Check`.
Remove obsolete license files explicitly after reviewing the distribution.
Commit both inputs and generated outputs. Do not change upstream document text
or normalize its encoding or line endings; `.gitattributes` preserves its bytes.

Win2D now uses a pinned local source build with its MIT license. Other binary
distribution requirements remain in the generated notice. Successful generation
is not a redistribution approval.
