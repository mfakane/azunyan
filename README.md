# Azunote

Azunote is a small WinUI 3 single-document text editor using Windows App SDK
2.4.0. Each window owns one document; there is intentionally no project or
workspace layer in this phase.

## Build

The project targets Windows x64 and uses the Windows App SDK 2.4.0.

```powershell
dotnet build Azunyan.slnx -c Debug
```

Release publishing uses Native AOT, full trimming, and a self-contained
Windows App SDK runtime:

```powershell
dotnet publish src/Azunote/Azunote.csproj -c Release -r win-x64 --self-contained true
```

The runnable output is written below
`src/Azunote/bin/Release/net8.0-windows10.0.19041.0/win-x64/publish`.

The UI-independent editing model lives in `src/Azunyan.Core` and can be
tested without WinUI:

```powershell
dotnet test tests/Azunyan.Core.Tests/Azunyan.Core.Tests.csproj
```

The project boundary is intentional: `Azunyan.Core`, `Azunyan.Layout`, and
`Azunyan.WinUI` contain only reusable editor-component code. Azunote-specific
application concerns—including the command-line contract, external tool
runner, and TOML settings service—live under `src/Azunote` and are not part
of `Azunyan.Core`.

Azunote application tests, including the external-tool and command-line tests,
are kept separate from the editor-component tests:

```powershell
dotnet test tests/Azunote.Tests/Azunote.Tests.csproj
```

## Repository layout

The source tree is grouped by responsibility rather than keeping all files at
the project root:

```text
src/
  Azunyan.Core/
    Documents/       document model, snapshots, ranges, and editing commands
    Providers/        provider contracts, frames, and scheduling
    Projection/       projected rows, folds, adornments, and height indexing
  Azunyan.Layout/
    LineLayout/      logical-line and wrapping layout
    Viewport/        viewport realization and scrolling calculations
  Azunyan.WinUI/
    Editor/          reusable WinUI editor control, rendering host, and accessibility
    Rendering/       DirectWrite/Win2D rendering primitives and color scheme
  Azunote/
    Application/     application startup and error reporting
    Shell/            main window, menus, and file commands
    CommandLine/     executable command-line parsing
    ExternalTools/   external process execution
    Settings/        TOML settings persistence
    FileSystem/      text-file I/O
    Language/        Azunote's built-in language providers
    Theme/           Azunote's default color scheme
tests/
  Azunyan.Core.Tests/  editor-component tests grouped like Core
  Azunote.Tests/       application and external-tool tests
  Azunote.UiTests/     opt-in Windows UI Automation tests
```

If the plain .NET CLI cannot locate the Visual Studio Appx/PRI build tasks, run
the command from a Visual Studio Developer PowerShell or pass the installed
Visual Studio AppxPackage directory explicitly with `-p:AppxMSBuildToolsPath`.

## Usage

```powershell
Azunote.exe path\to\file.txt
```

The command-line contract also accepts external-editor positions and standard
input:

```powershell
Azunote.exe --wait --line 12 --column 4 path\to\file.txt
Get-Content input.md | Azunote.exe --stdin
Azunote.exe +12:4 path\to\file.txt
```

Line and column are one-based and are clamped to the opened document. `--wait`
is accepted as the external-editor wait contract; because Azunote is a desktop
application, its process remains alive until the editor window closes.

External commands are available from Tools > Run External Tool... and through
the `ExternalToolRunner` API. Commands run without a shell and can receive
`FilePath`, `Document`, or `Selection` through stdin. Arguments support the
placeholders `${file}`, `${fileDir}`, `${fileName}`, `${document}`,
`${selection}`, `${userHome}`, `${lineNumber}`, and `${columnNumber}`.
Environment variables are available as `${env:NAME}`. Output can be ignored,
inserted into the document or selection, opened as a new document, or used to
reload the current file. Non-zero exit codes leave the document unchanged and
show stderr.

Files opened from disk are watched for external changes. A clean document is
reloaded automatically; if it has unsaved edits, Azunote asks whether to reload
or keep the local changes.

The settings folder is `%LOCALAPPDATA%\Azunote`. Tools > Preferences... opens
that folder in Explorer. The folder is created on first launch and contains a
TOML-based `settings.toml` plus a `tools` folder. Azunyan watches the settings
folder recursively and reloads valid external changes without replacing the
previous tool menu when a definition is invalid.

Every file ending in `.tool.toml` below `tools` is one tool definition. Normal
folders become menu submenus, so a file at `tools\Formatting\CSharp\format.tool.toml`
appears under Tools > External Tools > Formatting > CSharp. A folder whose name
ends in `.tool` and contains `manifest.toml` is instead one bundled tool; its
contents are not turned into additional menu levels. The tool definition uses
TOML fields for `name`, `command`, `arguments`, `input`, `output`, and optional
`workingDirectory`.

For example:

```toml
name = "Format document"
command = "prettier"
arguments = ["--write", "${file}"]
input = "FilePath"
output = "ReloadFile"
```

The editor supports File/Edit/View/Help menus, Open, Save, Save As with
encoding and line-ending choices, UTF-8/UTF-8 BOM/UTF-16 detection,
line-ending reporting, dirty-title tracking, find/replace, basic editing
commands, automatic indentation on Enter, keyboard shortcuts, and dropping a
file onto the editor.

The editor surface is `AzunyanEditorControl`, a `TextBox`-derived control that
keeps Windows' native text-service integration. This provides Japanese IME
composition, candidate-window placement, scrolling, selection rendering, and
the standard Edit UI Automation patterns. Its mirrored `Azunyan.Core.Document`
uses UTF-16 offsets at the WinUI boundary but exposes scalar and extended
grapheme-cluster movement/deletion so surrogate pairs, combining sequences,
and joined emoji are not split by editor commands.

`AzunyanEditorView` is the reusable host for future editor rendering. It keeps
the native editor as the IME/input host and exposes independent gutter, text,
and overlay layers through `IAzunyanEditorRenderer`. In NoWrap mode, the
default renderer realizes only visible lines from the snapshot projection and
draws syntax, selection, and caret through the projected text layer; native
glyphs and selection highlight are hidden. Word-wrap mode uses DirectWrite's
measured line metrics to create continuation rows in the same projected
surface, including wrapped selection, caret geometry, and pointer hit testing.
The gutter is drawn through the same measured text backend, so line numbers
follow visual-row scrolling without per-line XAML elements. In projected mode the
vertical scrollbar uses the visual-row height index; the native TextBox keeps
its internal scroll state only as an input/IME synchronization aid. Position
completion results and provider tooltips are shown in projected-mode popups
anchored to the same caret geometry; arrow keys, Enter/Tab, Escape, and
Ctrl+Space are supported.

Provider APIs live in `Azunyan.Core`. `ISyntaxProvider`,
`IDecorationProvider`, `ITooltipProvider`, `ICompletionProvider`, and
`IGutterProvider` receive an immutable `EditorProviderContext` containing a
snapshot, caret position, and selection. `IFoldingProvider`, `IInlayProvider`,
and `IBlockAdornmentProvider` extend the same boundary for folding, inlays,
and CodeLens-like rows. `EditorProviderScheduler` runs document, viewport,
and position channels independently, off the caller thread; each channel
cancels superseded work, rejects stale results, and isolates provider
exceptions. `EditorProviderCoordinator` remains as a compatibility aggregate
for callers that have not migrated. `AzunyanEditorView.Providers` is the
injection point for application providers; `ProviderFrame` exposes the
channel-bound results used by the renderer, while `ProviderResults` remains a
compatibility view.

`Azunyan.Layout` contains the framework-independent projection-to-layout
boundary: fold placeholders and inline adornments map through document anchors,
inlays occupy measured horizontal layout runs with provider identity, wrapped
lines become continuation visual rows, block adornments become visual rows with
independent heights, and a variable-height index realizes only a bounded
viewport window. The projected surface maps pointer presses back through that
same visual-row cache, including fold placeholder toggling, inlay identity, and
block-row anchors. Azunote's lightweight provider also supplies heading-based
fold candidates and keyword tooltips; the editor owns which fold IDs are
collapsed. `Azunyan.WinUI` supplies the DirectWrite-backed Win2D text layout
backend used by Azunote's projected surface, including font-aware wrapping and
gutter text. It is still a bounded viewport renderer; the native
input/accessibility bridge remains transitional.

Rendering colors are injected through `Azunyan.WinUI.AzunyanColorScheme` rather
than hard-coded in the renderer. `AzunyanEditorView.ColorScheme` supplies the
same palette to DirectWrite text, the gutter, selection/caret/composition
marks, inlays, and completion/tooltip popups. Azunote initializes this with a
light-theme palette read from WinUI `SystemControl*Brush` resources; an
embedding application can replace it with its own scheme.

Azunote ships deliberately small `AzunoteSyntaxProvider`,
`AzunoteTooltipProvider`, and `AzunoteCompletionProvider` implementations. The
completion provider combines note keywords with distinct identifier-like words
from the current snapshot, and the view can accept the selected item against
its snapshot-bound replacement range.

Unexpected UI exceptions are logged and shown in an error dialog. Unhandled
AppDomain and unobserved task exceptions are logged as well. Logs are written
to `%LOCALAPPDATA%\Azunote\logs\azunote-YYYYMMDD.log` when the per-user local
application-data directory is available.

The target architecture for folding, inlay hints, CodeLens, virtualized text
layout, IME, and accessibility is documented in
[`docs/editor-architecture.md`](docs/editor-architecture.md). The current
bounded `CanvasControl`/DirectWrite renderer owns visible text layout; input,
IME, and accessibility integration remain the next acceptance gates.
