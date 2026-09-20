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
  ExternalTools/   external process execution and discovery
  Settings/        TOML settings persistence and custom definitions
  FileSystem/      text-file I/O
  Language/        built-in and custom language providers
  Theme/           Azunote's default color scheme

src/Azunote.Ipc/   command-line contract and single-instance wire format
src/Azunote.Cli/   azu.exe, the console client
```

The editor is a Windows subsystem executable, so a shell cannot capture its
standard output. `azu.exe` owns the console side of the contract and talks to
the editor over the single-instance named pipe; both halves share
`Azunote.Ipc`. `Azunote.exe` therefore writes nothing to a console of its
own: `Azunote.exe --help` shows the help in the editor, and `azu --help`
prints it to standard output.

Each message on that pipe is one length-prefixed frame, written with a
single write and parsed in memory. A response frame is a status byte and
the payload, and the status byte is the exit code `azu.exe` reports.

## Usage

The published output includes `azu.exe` next to `Azunote.exe`. Add that
directory to `PATH` to invoke Azunote as the `azu` command; the packaged
build registers it as an app execution alias instead. Normal launches return
the prompt immediately; `--wait` keeps it blocked until the opened document
window is closed.

In the portable ZIP both are shims that start the application from the `.app`
subdirectory beside them, which is where the published files live. They behave
as the executables they stand for: arguments, standard input and output, and
the exit code pass straight through.

```powershell
azu path\to\file.txt
azu --wait --line 12 --column 4 path\to\file.txt
Get-Content input.md | azu --stdin
azu +12:4 path\to\file.txt
```

### Tab completion

`azu` answers completion requests for its own command line, so the suggestions
come from the grammar the editor parses rather than a separate list. In
PowerShell 7, dot-source the shipped registration script, and add the same line
to `$PROFILE` to keep it:

```powershell
. 'C:\Program Files\Azunote\Register-AzunoteCompletion.ps1'
```

Option names and `--output` values are completed, including the value after a
comma in a list. A document path is left to
the shell's own file completion, which takes over as soon as the word being
completed is not an option.

A registration through [dotnet-suggest](https://github.com/dotnet/command-line-api)
works as well, for a shell that is already set up for it:

```powershell
dotnet tool install -g dotnet-suggest
dotnet-suggest register --command-path 'C:\Program Files\Azunote\azu.exe'
```

### Editing in a pipeline

`--output` asks the editor for a value when the document window closes and
writes it to standard output. It names the same values as the external-tool
`input` field: `none`, `filePath`, `document`, and `selection`. Because the
value only exists once the window closes, `--output` implies `--wait`.

```powershell
$text = git log --oneline | azu --output document -
$path = azu --output filePath notes.md
git config core.editor "azu --wait"
```

The payload is written as UTF-8 without a byte-order mark and without an
added line ending, so a caller receives the bytes the document holds. The
exit code reports the outcome:

| Code | Meaning |
| --- | --- |
| 0 | The document closed and the requested output was written. |
| 1 | A file-backed document was closed with its unsaved changes discarded. Nothing is written. |
| 2 | The command line was invalid, or the editor could not be reached. |

An untitled document has nowhere to be saved to, so its buffer is the result:
piped text closes without a save confirmation and is returned as it stands.
A document backed by a file keeps the ordinary confirmation, and discarding
its changes is how a caller learns the edit was cancelled.

#### Asking for more than one value

`--output` takes several values separated by commas, and then needs `--json`:

```powershell
$result = azu --output filePath,document --json notes.md | ConvertFrom-Json
$result.document | Set-Content $result.filePath
```

A document is free to contain any line ending, and a shell hands native output
on as lines, so there is no separator that could hold two documents apart in
one stream. The JSON object escapes them instead: it is written as one line,
keyed by the value names, with each field holding exactly what the same value
would have written on its own. An untitled document therefore reports an empty
`filePath` rather than a missing one, and a document keeps its own line
endings exactly. Only the characters JSON must escape are escaped, so
text stays readable.

`--json` is accepted with a single value as well, which is how a script that
always parses its answer pins the format:

```powershell
$text = (azu --output document --json - | ConvertFrom-Json).document
```

`none` asks for no output and cannot be combined with another value, no value
may be named twice, and `--json` on its own has nothing to write. Each of
these is a command-line error.

In PowerShell, write the list as one literal word. A comma between variables
is not what it looks like: `-o $a,$b` is passed through literally and an array
variable arrives as separate arguments, where the second would be taken for a
document path. Join it first instead:

```powershell
azu --output ($values -join ',') --json notes.md
```

When no editor is running, `azu` starts one and then sends the command line
to it, so a cold start and a warm start take the same path. An empty,
unmodified, untitled window is reused rather than left behind.

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

The settings directory is `%LOCALAPPDATA%\Azunote` by default. When an
`appdata` directory exists next to `Azunote.exe`, as it does in the portable
ZIP, that directory is used instead. Tools > Preferences... opens the selected
directory in Explorer. The directory is created on first launch and is watched
for valid external changes.

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
Python, JSON, YAML, TOML, Markdown, and PowerShell. Azunote exposes them from
View > Language Mode, alongside Plain Text and its Azunote note mode. Custom
language modes are described in [Settings](../../docs/settings.md).

Folding is offered by the language definitions that supply a folding provider.
TOML and the Azunote note mode fold a table section up to the next header, JSON
folds an object or an array so that a collapsed container reads as one line,
`"items": [ … ],`, and YAML folds the block indented under a key or a sequence
entry, taking in the line break of its last line so no empty row is left behind
and leaving trailing blank lines outside the fold. Each fold is
named after the path of what it covers, such as `$/items/2` or `/jobs/build`,
so a collapsed block stays collapsed while the text above it is edited.

URLs are highlighted in every language mode. Azunote layers
`Azunyan.Syntax.UrlSyntaxRule` over the selected mode with
`OverlaySyntaxProvider`, so a URL inside a comment or a string keeps the link
appearance while the surrounding text keeps its own. In Markdown mode,
`MarkdownLinkSyntaxRule` adds the destination of an inline link or image,
including the relative ones a URL scan cannot recognize: `[note](./other.md)`,
`[up](../index.md)`, `[same](other.md)`, and the `<...>` and titled forms.

Resting the pointer on a link shows the target and a "Ctrl + Click to open"
hint. Ctrl+Click opens it, and the pointer shows a hand cursor while Ctrl is
held over one. An absolute `http`, `https`, `ftp`, `ftps`, or `mailto` target goes to
the Windows default handler. Any other target is read as a path and resolved
against the folder of the current document, so a relative link needs a saved
document. The resolved file is opened in a new Azunote window when one of the
language modes covers its name, including a custom mode, and handed to the
Windows default handler otherwise; a link that resolves to no existing file
does nothing.

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
