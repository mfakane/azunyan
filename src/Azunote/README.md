![Icon of Azunote](../../assets/icons/01-paw-notebook-128.png)

# Azunote

Azunote is a small WinUI 3 document-per-window text editor using Windows App
SDK 2.4.0. Each window owns one document.

This directory contains the application layer built on the reusable
`Azunyan.Core`, `Azunyan.Syntax`, and `Azunyan.WinUI` components. The
framework-independent layout APIs live in `Azunyan.Core` under the
`Azunyan.Layout` namespace. The application-specific command-line contract,
shell, external tools, TOML settings, file I/O, language modes, and theme are
kept here.

## Non-goals

Azunote is intended to remain a lightweight document-per-window text editor.
Its non-goals are:

- Becoming a full integrated development environment (IDE).
- Providing an application layer for managing projects or workspaces.
  Workspace-folder detection for substitution variables is only context for
  external commands, not project or workspace management.
- Providing an integrated build or debugging environment. Such workflows
  belong in external tools invoked through Azunote's external-tool integration.
- Exposing every editor feature supported by Azunyan. Azunote uses a selected
  subset; these non-goals do not limit the reusable Azunyan components.

Language modes, syntax highlighting, and configuration completion remain part
of Azunote's lightweight editing experience.

## License

Azunote and the Azunyan editor components are licensed under the
[zlib license](../../LICENSE). Third-party components retain their own
licenses. See [Third-party notices](../../THIRD-PARTY-NOTICES.md) for the
dependency inventory and redistribution requirements.

Build and publish output includes `LICENSE`, `THIRD-PARTY-NOTICES.md`, and
the `licenses` directory. Keep these files with binary distributions.
Help > View License and Help > Third Party Notices display the bundled texts
in Azunote itself. Bundled legal documents, including files under `licenses`,
open read-only and show `[READONLY]` in the title bar. Selection, copying,
search, and navigation remain available; editing, saving, and external tools
are disabled. Duplicate windows preserve the same read-only state.

## Build and test

For Native AOT MSIX/APPX and portable ZIP outputs, see
[distribution scripts](../../scripts/Packaging.md).

First run `git submodule update --init external/Win2D` and
`./scripts/Build-Win2D.ps1` from the repository root to generate the pinned local
Win2D package. See [build prerequisites](../../scripts/Win2D-build.md).

The application targets Windows x64. Build it from the repository root with:

```powershell
dotnet build Azunyan.slnx -c Debug
```

Release publishing uses Native AOT, full trimming, and a self-contained
Windows App SDK runtime:

```powershell
dotnet publish src/Azunote/Azunote.csproj -c Release -r win-x64 --self-contained true
```

The runnable output is written below
`src/Azunote/bin/Release/net10.0-windows10.0.19041.0/win-x64/publish`.

Application tests, including the external-tool and command-line tests, are
kept separate from the editor-component tests:

```powershell
dotnet test tests/Azunote.Tests/Azunote.Tests.csproj
```

## Application layout

```text
src/Azunote/
  Application/     application startup and error reporting
  Shell/           main window, menus, and file commands
  CommandLine/     executable command-line parsing
  ExternalTools/   external process execution and discovery
  Settings/        TOML settings persistence and custom definitions
  FileSystem/      text-file I/O
  Language/        built-in and custom language providers
  Theme/           Azunote's default color scheme
```

## Usage

The published output includes `azu.cmd` and `azu.ps1` next to
`Azunote.exe`. Add that directory to `PATH` to invoke Azunote as the `azu`
command. Normal launches return the prompt immediately; `--wait` keeps it
blocked until the opened document window is closed.

```powershell
azu path\to\file.txt
azu --wait --line 12 --column 4 path\to\file.txt
Get-Content input.md | azu --stdin
azu +12:4 path\to\file.txt
```

Line and column are one-based and are clamped to the opened document. Azunote
uses one process per user session: launching `Azunote.exe` again activates the
running instance and forwards the command line to it. A forwarded path
activates its existing document window when it is already open; otherwise it is
opened in a new document window. `--wait` also waits for any save confirmation
when that document window is closed.

Files opened from disk are watched for external changes. A clean document is
reloaded automatically; if it has unsaved edits, Azunote asks whether to reload
or keep the local changes.

## Configuration and external tools

See the user-facing documentation for the application configuration:

- [Settings](../../docs/settings.md), including `settings.toml`, state, and custom language modes
- [External Tools](../../docs/external-tools.md), including tool discovery and launch definitions
- [Substitution Variables](../../docs/substitution-variables.md), including `.env` loading and expansion

The settings directory is `%LOCALAPPDATA%\Azunote`. Tools > Preferences... opens
it in Explorer. The directory is created on first launch and is watched for
valid external changes.

## Editor integration

`AzunyanEditorView` owns the `Azunyan.Core.Document` and the full-document
rendering contract. `AzunyanTextInputWindow` is a separate, transparent native
WinUI `TextBox` host for IME and text-service events. It keeps a configurable
sliding window (2048 UTF-16 code units before and after the active selection by
default), aligns its edges to text-element boundaries, and publishes changes
in document coordinates with a generation and window origin. It never owns the
document, projection, or drawing.

In NoWrap mode, the default renderer realizes only visible lines from the
snapshot projection and draws syntax, selection, and caret through the
projected text layer; native glyphs and selection highlight are hidden.
Word-wrap mode uses DirectWrite's measured line metrics to create continuation
rows in the same projected surface, including wrapped selection, caret geometry,
and pointer hit testing. `IAzunyanEditorRenderer.TryGetCaretRect` supplies the
renderer caret in `EditorHost` coordinates; the view places the input window
there and reconciles it with the native caret rectangle for IME candidate
placement. Completion results and provider tooltips use the same caret geometry.

Built-in language definitions are available for C#, JavaScript, TypeScript,
Python, JSON, TOML, Markdown, and PowerShell. Azunote exposes them from
View > Language Mode, alongside Plain Text and its Azunote note mode. Custom
language modes are described in [Settings](../../docs/settings.md).

Azunote's configuration mode uses the built-in TOML provider and a
NativeAOT-safe schema catalog for completion candidates in settings, tool,
manifest, and custom-mode definition files. The view can accept the selected
item against its snapshot-bound replacement range.

Azunote initializes the editor color scheme with a light-theme palette read
from WinUI `SystemControl*Brush` resources. The application can replace it
when embedding or customizing the editor.

## Diagnostics

Unexpected UI exceptions are logged and shown in an error dialog. Unhandled
AppDomain and unobserved task exceptions are logged as well. Logs are written
to `%LOCALAPPDATA%\Azunote\logs\azunote-YYYYMMDD.log` when the per-user local
application-data directory is available. Detailed operation logging is
configured in [Settings](../../docs/settings.md).
