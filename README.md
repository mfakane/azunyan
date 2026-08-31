# Azunote

Azunote is a small WinUI 3 document-per-window text editor using Windows App SDK
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
`src/Azunote/bin/Release/net10.0-windows10.0.19041.0/win-x64/publish`.

The UI-independent editing model lives in `src/Azunyan.Core` and can be
tested without WinUI:

```powershell
dotnet test tests/Azunyan.Core.Tests/Azunyan.Core.Tests.csproj
```

The project boundary is intentional: `Azunyan.Core`, `Azunyan.Syntax`,
`Azunyan.Layout`, and `Azunyan.WinUI` contain only reusable editor-component code. Azunote-specific
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
  Azunyan.Syntax/    composable lexical rules and built-in language definitions
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
  Azunyan.Syntax.Tests/ syntax rules, composition, and language definition tests
  Azunote.Tests/       application and external-tool tests
  Azunote.UiTests/     opt-in Windows UI Automation tests
```

If the plain .NET CLI cannot locate the Visual Studio Appx/PRI build tasks, run
the command from a Visual Studio Developer PowerShell or pass the installed
Visual Studio AppxPackage directory explicitly with `-p:AppxMSBuildToolsPath`.

## Usage

```powershell
azu path\to\file.txt
```

The publish output includes `azu.cmd` and `azu.ps1` next to `Azunote.exe`.
Add that directory to `PATH` to invoke Azunote as the `azu` command.
Normal launches return the prompt immediately; `--wait` keeps it blocked until
the opened document window is closed.

The command-line contract also accepts external-editor positions and standard
input:

```powershell
azu --wait --line 12 --column 4 path\to\file.txt
Get-Content input.md | azu --stdin
azu +12:4 path\to\file.txt
```

Line and column are one-based and are clamped to the opened document. Azunote
uses one process per user session: launching `Azunote.exe` again activates the
running instance and forwards the command line to it. A forwarded path is
opened in a new document window. `--wait` keeps the launching process blocked
until that document window is closed, including any save confirmation.

External commands are available from Tools > Run External Tool... and through
the `ExternalToolRunner` API. Commands run without a shell and can receive
`FilePath`, `Document`, or `Selection` through stdin. Arguments support the
VS Code-style placeholders `${userHome}`, `${workspaceFolder}`,
`${workspaceFolderBasename}`, `${file}`, `${fileWorkspaceFolder}`,
`${relativeFile}`, `${relativeFileDirname}`, `${fileBasename}`,
`${fileBasenameNoExtension}`, `${fileExtname}`, `${fileDirname}`,
`${fileDirnameBasename}`, `${cwd}`, `${lineNumber}`, `${columnNumber}`,
`${selectedText}`, `${execPath}`, `${pathSeparator}`, and `${/}`.
Azunote-specific placeholders include `${documentFile}`, `${documentDirname}`,
`${documentName}`, `${documentBasenameNoExtension}`, `${documentExtension}`, `${tempFile}`,
`${toolFolder}`, `${document}`, `${languageId}`, `${encoding}`, `${lineEnding}`,
`${platform}`, `${architecture}`, `${selectionStartLine}`,
`${selectionStartColumn}`, `${selectionEndLine}`, and `${selectionEndColumn}`.
Azunote mode offers completion for these placeholders in fields that expand
values. Environment variables are available as `${env:NAME}` and are
completed from the current process environment. The selected completion hint
also shows a representative expansion example, such as `${file}` ->
`C:\work\notes\current.azunote`. Output can be ignored,
inserted into the document or selection, opened as a new document, or used to
reload the current file. When the current buffer is unsaved, the tool receives
a temporary file containing the current text through `${file}` or `FilePath`;
the temporary file is removed after the tool finishes. Non-zero exit codes
leave the document unchanged and show stderr.

`${workspaceFolder}` expands to the nearest ancestor of the document directory
that contains `.git` (directory or file) or an `.editorconfig` with
`root = true`. It is empty when no such ancestor exists.

Files opened from disk are watched for external changes. A clean document is
reloaded automatically; if it has unsaved edits, Azunote asks whether to reload
or keep the local changes.

The settings folder is `%LOCALAPPDATA%\Azunote`. Tools > Preferences... opens
that folder in Explorer. The folder is created on first launch and contains a
TOML-based `settings.toml`, an application-managed `state.toml`, plus `tools`
and `modes` folders. `state.toml` stores the last window size, Word Wrap and
Status Bar preferences, and up to 20 recently opened file paths; it is
rewritten automatically and should not be edited. Azunyan watches
the settings folder recursively and reloads valid external changes without
replacing the previous tool or language-mode menu when a definition is invalid.

The status bar's `Open Folder in Terminal` command can be configured in
`settings.toml`. The default is equivalent to:

```toml
fontFamily = "Consolas"
fontSize = 14

[debug]
# Empty disables detailed operation logging.
logging = []

[terminal]
command = "wt.exe"
args = ["-d", "${documentDirname}"]
workingDirectory = "${documentDirname}"

[explorer]
command = "explorer.exe"
args = ["/select,\"${file}\""]
workingDirectory = "${documentDirname}"
```

For crash or input troubleshooting, enable only the detailed categories you
need in the `[debug]` section. Use `logging = ["all"]`, or select from
`"render"`, `"clipboard"`, `"key"`, and `"input"`, for example
`logging = ["render", "clipboard", "key"]`. Caught exceptions and crash
reports remain in the log regardless of this setting.

`terminal.*` and `explorer.*` support the same placeholders as external tools,
including `${documentDirname}`, `${file}`, `${fileBasename}`, and `${env:NAME}`. If
`workingDirectory` is omitted, the current document's folder is used. The
bundled example is copied from `Resources/DefaultAppData/settings.toml` when
the settings file is created.

Every file ending in `.tool.toml` below `tools` is one tool definition. Normal
folders become menu submenus, so a file at `tools\Formatting\CSharp\format.tool.toml`
appears under Tools > External Tools > Formatting > CSharp. A folder whose name
ends in `.tool` and contains `manifest.toml` is instead one bundled tool; its
contents are not turned into additional menu levels. The tool definition uses
TOML fields for `name`, `command`, `arguments`, `input`, `output`, and optional
`workingDirectory`.

Each external tool menu item is a split item: its main area runs the tool, and
the `...` area provides `Edit...` and `Show in Explorer` for the definition
file.

For example:

```toml
name = "Format document"
command = "prettier"
arguments = ["--write", "${file}"]
input = "FilePath"
output = "ReloadFile"
```

The editor supports File/Edit/View/Window/Help menus, Open, Open Recent, Save,
Save As with encoding and line-ending choices, UTF-8/UTF-8 BOM/UTF-16 detection,
line-ending reporting, dirty-title tracking, find/replace, basic editing
commands, automatic indentation on Enter, keyboard shortcuts, and dropping a
file onto the editor.
New creates a new document window. Open reuses a clean Untitled window and
opens a new window when the current window already contains a file or unsaved
text. Window provides creation-order cycling with Ctrl+Tab/Ctrl+Shift+Tab,
restoring or minimizing all windows, and per-window always-on-top control.

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

`Azunyan.Syntax` supplies reusable lexical providers for delimited ranges,
line remainders, keywords, literal tokens, and regular-expression matches.
`CompositeSyntaxProvider` runs its sources in parallel, then performs a
left-to-right lexical scan: at each position the first matching source wins
and its complete token is consumed. This keeps comment markers inside strings
and quotes inside comments from leaking into later classifications.

Built-in definitions are available for C#, JavaScript, TypeScript, Python,
JSON, TOML, Markdown, and PowerShell. Selection is deliberately application-owned:
Azunote exposes them from View > Language Mode, alongside Plain Text and its
Azunote note mode.

```csharp
Editor.Providers.Syntax = BuiltInSyntaxLanguages.CSharp;
```

Azunote also discovers custom language modes from
%LOCALAPPDATA%\Azunote\modes. On first launch, the bundled definitions under
Resources/DefaultAppData/modes are copied there. Once the folder exists it is
user-owned and is never overwritten; adding, editing, or removing a .toml
definition is picked up by the settings watcher.
Custom entries in View > Language Mode also provide `Edit...` and `Show in
Explorer` for their definition files.
When a file is opened, its path is matched against the built-in and custom
patterns; unmatched files use Plain Text. A manual menu selection
is kept until another file is opened.
Plain Text is the default mode and its supported extensions are `.txt` and
`.log`.
The Open and Save dialogs use the same mode definitions for their file-type
filters: supported formats, one entry per language mode, and all files. Open
starts on supported formats, while Save As starts on the active language mode.

Each file describes one mode and applies its rules in the listed order. The
supported rule types are delimited, line, literal, keyword, and regex. A
pattern without a path separator matches the file name; when multiple modes
match, the most specific matching suffix wins. For
example, a small mode definition looks like this:

~~~toml
id = "ini"
displayName = "INI"
patterns = ["*.ini", "*.cfg", "*.conf"]
completionTriggerCharacters = [".", "(", "{", "[", "->"]

[[rules]]
type = "line"
classification = "comment"
token = ";"

[[rules]]
type = "delimited"
classification = "string"
open = "\""
close = "\""
allowLineBreaks = false
escapePrefix = "\\"

[[rules]]
type = "keyword"
classification = "keyword"
words = ["true", "false"]
~~~

TOML is built into Azunyan and is also the syntax layer used by Azunote's
configuration mode. The bundled `ini.toml` definition demonstrates a custom
mode without duplicating the built-in TOML definition.
`completionTriggerCharacters` contains literal strings that request completion
after insertion (multi-character values such as `->` are allowed). Explicit
completion is an application command; Azunote exposes it as Edit > Show
Completions with Ctrl+Space.

Rules and an LSP-backed or other custom provider can occupy the same priority
list. Earlier entries win when candidates start at the same position:

```csharp
Editor.Providers.Syntax = new CompositeSyntaxProvider(new ISyntaxProvider[]
{
    new DelimitedSyntaxRule("/*", "*/", "comment"),
    new LineRemainderSyntaxRule("//", "comment"),
    new DelimitedSyntaxRule("\"", "\"", "string", false, "\\"),
    semanticTokenProvider,
    new KeywordSyntaxRule(new[] { "class", "return" })
});
```

Applications can color classifications supplied by external providers without
changing the renderer. The default scheme gives `heading`, `keyword`, `string`,
`code`, `number`, `comment`, `task-marker`, and `variable` distinct foreground
colors so each syntax category is visible while inspecting a definition.
Returning `null` retains the built-in mapping and the normal editor-foreground
fallback:

```csharp
Editor.ColorScheme = Editor.ColorScheme with
{
    SyntaxForegroundResolver = classification =>
        classification == "variable" ? variableColor : null
};
```

`Azunyan.Layout` contains the framework-independent projection-to-layout
boundary: fold placeholders and inline adornments map through document anchors,
inlays occupy measured horizontal layout runs with provider identity, wrapped
lines become continuation visual rows, block adornments become visual rows with
independent heights, and a variable-height index realizes only a bounded
viewport window. The projected surface maps pointer presses back through that
same visual-row cache, including fold placeholder toggling, inlay identity, and
block-row anchors. `Azunyan.WinUI` supplies the DirectWrite-backed Win2D text layout
backend used by Azunote's projected surface, including font-aware wrapping and
gutter text. It is still a bounded viewport renderer; the native
input/accessibility bridge remains transitional.

Rendering colors are injected through `Azunyan.WinUI.AzunyanColorScheme` rather
than hard-coded in the renderer. `AzunyanEditorView.ColorScheme` supplies the
same palette to DirectWrite text, the gutter, selection/caret/composition
marks, inlays, and completion/tooltip popups. Azunote initializes this with a
light-theme palette read from WinUI `SystemControl*Brush` resources; an
embedding application can replace it with its own scheme.

Azunote's configuration mode uses the built-in TOML provider and a
NativeAOT-safe schema catalog for completion candidates in settings, tool,
manifest, and custom-mode definition files. The view can accept the selected
item against its snapshot-bound replacement range.

Unexpected UI exceptions are logged and shown in an error dialog. Unhandled
AppDomain and unobserved task exceptions are logged as well. Logs are written
to `%LOCALAPPDATA%\Azunote\logs\azunote-YYYYMMDD.log` when the per-user local
application-data directory is available.

The target architecture for folding, inlay hints, CodeLens, virtualized text
layout, IME, and accessibility is documented in
[`docs/editor-architecture.md`](docs/editor-architecture.md). The current
bounded `CanvasControl`/DirectWrite renderer owns visible text layout; input,
IME, and accessibility integration remain the next acceptance gates.
