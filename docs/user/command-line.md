# Command line

`azu` opens and edits Azunote documents from a command prompt or PowerShell.
The installed app registers `azu` as a command. For the portable ZIP, add the
extracted folder, which contains `azu.exe` next to `Azunote.exe`, to `PATH`.

`azu --help` prints the command-line help. `Azunote.exe --help` shows the same
help in an editor window.

## Usage

```powershell
azu path\to\file.txt
azu notes.md todo.md log.txt
azu --wait --line 12 --column 4 path\to\file.txt
Get-Content input.md | azu --stdin
Get-Content -Wait input.log | azu --stream
azu +12:4 path\to\file.txt
```

`azu` returns as soon as Azunote has opened the document, unless `--wait` or
`--output` asks it to wait for the window to close. If Azunote is not running,
`azu` starts it. An empty, unmodified, untitled window is reused when possible.

## Options

| Option | Description |
| --- | --- |
| `path ...` | Open one or more documents. A path that is already open activates its window. |
| `--wait`, `-w` | Wait until the document window closes. |
| `--stdin` | Read a complete document from standard input after input ends. A bare `-` is the same as `--stdin`. |
| `--stream` | Read standard input and append text to the editor as it arrives. This option implies `--stdin`. |
| `--line N`, `-l N` | Open at line N, counting from 1. |
| `--column N`, `-c N` | Open at column N, counting from 1. |
| `--output values`, `-o values` | Write values from the document to standard output when the window closes. Requesting output also waits for the window. |
| `--json` | Write the requested output as one JSON object. |
| `--help`, `-h` | Show command-line help. |
| `+N[:M]` | Open at line N and, optionally, column M. |
| `--` | Treat the remaining arguments as document paths. |

Line and column values must be positive integers. A value past the end of the
document goes to the last line or column. Several paths open one window each,
and cannot be combined with options for a single document, such as `--line`,
`--output`, or `--wait`.

## Standard input

`--stdin` reads all input before opening it as a document. Use `--stream` when
input is produced over time and should appear in the editor before the source
finishes:

```powershell
Get-Content -Wait build.log | azu --stream
```

## Editing in a pipeline

`--output` accepts `filePath`, `document`, or `selection`, and `none` requests
no output. A single value is written as UTF-8 without a byte-order mark or an
added line ending:

```powershell
$text = git log --oneline | azu --output document -
$path = azu --output filePath notes.md
git config core.editor "azu --wait"
```

Closing an untitled document returns its text without asking to save it. A
document with a file asks to save as usual, and discarding the changes cancels
the output.

Several values are separated by commas and need `--json`:

```powershell
$result = azu --output filePath,document --json notes.md | ConvertFrom-Json
$result.document | Set-Content $result.filePath
```

The JSON object is written on one line and keeps each value's line endings. An
untitled document has an empty `filePath`. `--json` also works with a single
value. `none` cannot be combined with another value, a value cannot be
repeated, and `--json` requires `--output`.

In PowerShell, pass the list as one argument. Join a list held in a variable
with commas first:

```powershell
azu --output ($values -join ',') --json notes.md
```

## Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Success. |
| `1` | A document with a file was closed and its unsaved changes were discarded. |
| `2` | The command is invalid, or Azunote cannot be reached. |

## Tab completion

`azu` can complete its options and `--output` values in PowerShell. Paths are
left to the shell's own file completion.

### Microsoft Store and installer

Use [dotnet-suggest](https://github.com/dotnet/command-line-api). Install it,
add its PowerShell script to your profile, and register `azu`:

```powershell
dotnet tool install -g dotnet-suggest
dotnet-suggest script PowerShell >> $PROFILE
dotnet-suggest register --command-path "$env:LOCALAPPDATA\Microsoft\WindowsApps\azu.exe"
```

### Portable ZIP

In PowerShell 7, load `Register-AzunoteCompletion.ps1` from the extracted
folder. Add the line to `$PROFILE` to keep it enabled:

```powershell
. 'C:\path\to\Azunote\Register-AzunoteCompletion.ps1'
```
