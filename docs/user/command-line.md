# Command-line reference

The published output includes `azu.exe` next to `Azunote.exe`. Add that
directory to `PATH` to invoke Azunote as `azu`; the packaged build registers
`azu` as an app execution alias. In the portable ZIP, both executable names
are shims that forward arguments, standard input and output, and the exit code
to the application.

`Azunote.exe` is a Windows subsystem application, so its help is shown in the
editor. `azu --help` writes the same command-line help to standard output.

## Usage

```powershell
azu path\to\file.txt
azu notes.md todo.md log.txt
azu --wait --line 12 --column 4 path\to\file.txt
Get-Content input.md | azu --stdin
Get-Content -Wait input.log | azu --stream
azu +12:4 path\to\file.txt
```

Path commands return after the editor processes the request. `--wait` keeps the
CLI blocked until the opened document window closes. Standard-input commands
stay connected while input is being read; `--stream` updates the editor during
that time. If Azunote is not running, `azu` starts it and forwards the command;
an empty, unmodified, untitled window is reused when possible.

## Options

| Option | Description |
| --- | --- |
| `path ...` | Open one or more documents. A path already open activates its existing window. |
| `--wait`, `-w` | Wait until the document window closes. |
| `--stdin` | Read a complete document from standard input after input ends. A bare `-` is the same as `--stdin`. |
| `--stream` | Read standard input and append arriving text to the editor as it is received. This option implies `--stdin`. |
| `--line N`, `-l N` | Open at one-based line N. |
| `--column N`, `-c N` | Open at one-based column N. |
| `--output values`, `-o values` | Write selected document values to standard output when the window closes. Requesting output also waits for the window. |
| `--json` | Write requested output as one JSON object. |
| `--help`, `-h` | Show command-line help. |
| `+N[:M]` | Open at one-based line N and optional column M. |
| `--` | Treat the remaining arguments as document paths. |

Line and column values must be positive integers and are clamped to the opened
document. Several paths open one window each. A command that names several
paths cannot use options that apply to a single document, such as `--line`,
`--output`, or `--wait`.

## Standard input

`--stdin` reads all input before opening it as a document. Use `--stream` when
input is produced over time and should appear in the editor before the source
process finishes:

```powershell
Get-Content -Wait build.log | azu --stream
```

## Editing in a pipeline

`--output` accepts `filePath`, `document`, or `selection`. `none` requests no
output. A single requested value is written as UTF-8 without a byte-order mark
or added line ending:

```powershell
$text = git log --oneline | azu --output document -
$path = azu --output filePath notes.md
git config core.editor "azu --wait"
```

An untitled document has no file to save, so closing it returns its buffer
without a save prompt. A file-backed document uses the ordinary save
confirmation; discarding unsaved changes cancels the command output.

Several values must be comma-separated and used with `--json`:

```powershell
$result = azu --output filePath,document --json notes.md | ConvertFrom-Json
$result.document | Set-Content $result.filePath
```

The JSON object is written as one line and preserves each value's line
endings. An untitled document has an empty `filePath`. `--json` also works
with one requested value. `none` cannot be combined with another value, a
value cannot be repeated, and `--json` requires `--output`.

In PowerShell, pass a list of output values as one argument. Join a variable
list with commas before passing it:

```powershell
azu --output ($values -join ',') --json notes.md
```

The command returns exit code 0 on success, 1 when a file-backed document is
closed with unsaved changes discarded, and 2 when the command is invalid or
Azunote cannot be reached.

## Tab completion

`azu` answers completion requests from the same command-line grammar used by
the editor. In PowerShell 7, load the shipped script and add the line to
`$PROFILE` to keep completion enabled:

```powershell
. 'C:\Program Files\Azunote\Register-AzunoteCompletion.ps1'
```

Option names and `--output` values are completed; paths are left to the
shell's file completion. A shell configured for
[dotnet-suggest](https://github.com/dotnet/command-line-api) can use it too:

```powershell
dotnet tool install -g dotnet-suggest
dotnet-suggest register --command-path 'C:\Program Files\Azunote\azu.exe'
```
