# Settings

Azunote stores user-owned configuration below `%LOCALAPPDATA%\Azunote`.
Tools > Preferences... opens this directory in Explorer. The directory is
created on first launch and contains:

| Path | Purpose |
| --- | --- |
| `settings.toml` | User-editable application settings. |
| `state.toml` | Application-managed window, view, and recent-file state. |
| `tools/` | [External tool definitions](external-tools.md). |
| `modes/` | Custom language-mode definitions. |
| `backups/` | [Unsaved documents](#unsaved-documents) kept for the next launch. |

Azunote watches this directory recursively and reloads valid external changes.
If a tool or language-mode definition is invalid, the previous menu remains in
place until a valid definition is available.

The directory is watched once and read once for the whole application, however
many windows are open, and every window shows the same reading. A change that
arrives while a reading is still in progress cancels it rather than racing it.

## settings.toml

The file uses TOML syntax. The top-level settings are:

| Setting | Description |
| --- | --- |
| `fontFamily` | Font used by the editor and renderer. The default is `Consolas`. |
| `fontSize` | Font size used by the editor and renderer. The default is `14`. |
| `windowSnapping` | Enables magnetic alignment between Azunote windows while moving or resizing. The default is `true`. |
| `[debug].logging` | Detailed diagnostic categories: `all`, `render`, `clipboard`, `key`, and `input`. An empty array disables high-volume diagnostic logging. |
| `[tools].powerShellWarmProcesses` | Most PowerShell processes kept waiting at once for `[launch].pwsh` tools, 1 to 16. The default is half the logical processor count, between 1 and 4. |
| `[tools].powerShellWarmIdleProcesses` | PowerShell processes kept waiting while no tool is running, 0 to `powerShellWarmProcesses`. The default is `1`. |
| `[terminal]` | Command used by the status bar's Open Folder in Terminal action. |
| `[explorer]` | Command used by the status bar's Show in Explorer action. |

The default shell-command configuration is equivalent to:

```toml
# Azunote settings. This file uses TOML syntax.
fontFamily = "Consolas"
fontSize = 14
windowSnapping = true

[debug]
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

`terminal.*` and `explorer.*` accept the substitution variables described in
[Substitution Variables](substitution-variables.md). If `workingDirectory` is
omitted, the current document's folder is used. These commands run without a
shell.

## state.toml

`state.toml` is rewritten automatically and should not be edited. It stores
the last window layout, Word Wrap and Status Bar preferences, and up to 10
recently opened file paths.

## Unsaved documents

While a document has unsaved changes, Azunote copies its text into `backups/`
a moment after each edit, and removes the copy once the document is saved or
its changes are discarded. Closing the last window, File > Exit (which closes
every window), and a Windows shutdown or restart therefore close without
asking about unsaved changes. Closing any other window still asks.

On the next launch, each backed-up document opens in its own window with its
changes still unsaved. A document that has a file reopens that file with the
changes applied on top, so Undo returns to the file as it is on disk; if the
file no longer exists, the text opens as an untitled document. The same
restore happens after Azunote stops unexpectedly.

Besides edits to the text, two things leave a document unsaved:

- Choosing a line ending from Edit > Line Ending or the status bar. It
  applies from the next save.
- Another program deleting the file. The title shows `(deleted)` until the
  document is saved or the file reappears unchanged.

File > Save with Encoding saves in the chosen encoding at once, and File >
Reopen with Encoding reads the file again in the chosen encoding instead of
the detected one. Both are also available from the encoding in the status bar.

A window that a command line waits on (`--wait`) still asks before closing,
because the command line reports what became of its document. Untitled text
that a command line reads back (`--output`) is never backed up.

## Custom language modes

On first launch, the bundled definitions under
`Resources/DefaultAppData/modes` are copied to `%LOCALAPPDATA%\Azunote\modes`.
Once the directory exists it is user-owned and is never overwritten. Adding,
editing, or removing a `.toml` definition is picked up by the settings
watcher. Custom entries in View > Language Mode provide `Edit...` and
`Show in Explorer` for their definition files.

Each file describes one language mode and applies its rules in the listed
order. The supported rule types are `delimited`, `line`, `literal`, `keyword`,
and `regex`. A pattern without a path separator matches the file name; when
multiple modes match, the most specific matching suffix wins.

Edit > Toggle Comment (Ctrl+/) uses the first `line` rule classified as
`comment` to comment or uncomment the selected lines. A mode without one wraps
the lines in the first `delimited` rule classified as `comment`, such as
`<!--` and `-->`. Plain Text has no comment syntax, so the command does nothing
there.

When a file is opened, its path is matched against the built-in and custom
patterns; unmatched files use Plain Text. A manual menu selection is kept
until another file is opened. Plain Text is the default mode and its supported
extensions are `.txt` and `.log`.

The Open and Save dialogs use the same mode definitions for their file-type
filters. Open starts on supported formats, while Save As starts on the active
language mode.

Language modes without a dedicated completion provider, including Plain Text,
use document-word completion. Ctrl+Space suggests longer words already present
in the current document and replaces the word prefix at the caret. A mode's
`completionTriggerCharacters` can also request the same completion after a
configured literal trigger.

A custom mode can also request completion after insertion with literal
`completionTriggerCharacters` such as `->`. Explicit completion is available
from Edit > Show Completions with Ctrl+Space.

For example:

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
