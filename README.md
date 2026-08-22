# Azunote

Azunote is a small WinUI 3 single-document text editor using Windows App SDK
2.4.0. Each window owns one document; there is intentionally no project or
workspace layer in this phase.

## Build

The project targets Windows x64 and uses the Windows App SDK 2.4.0.

```powershell
dotnet build Azunyan.slnx -c Debug
```

The UI-independent editing model lives in `src/Azunyan.Core` and can be
tested without WinUI:

```powershell
dotnet test tests/Azunyan.Core.Tests/Azunyan.Core.Tests.csproj
```

If the plain .NET CLI cannot locate the Visual Studio Appx/PRI build tasks, run
the command from a Visual Studio Developer PowerShell or pass the installed
Visual Studio AppxPackage directory explicitly with `-p:AppxMSBuildToolsPath`.

## Usage

```powershell
Azunote.exe path\to\file.txt
```

The editor supports File/Edit/View/Help menus, Open, Save, Save As,
UTF-8/UTF-8 BOM/UTF-16 detection, line-ending reporting, dirty-title tracking,
find/replace, basic editing commands, keyboard shortcuts, and dropping a file
onto the editor.

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
