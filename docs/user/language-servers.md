# Language servers

A language server is a program that understands a language and reports
problems, completions, and descriptions for a document, using the
[Language Server Protocol](https://microsoft.github.io/language-server-protocol/).
A [language mode](language-modes.md) names the server to use with
`[languageServer]`. Azunote comes with none set up; the
[default mode definitions](language-modes.md#default-definitions) include
commented-out examples for common servers.

~~~toml
# modes/typescript.toml
workspacePatterns = ["package.json", "tsconfig.json", "jsconfig.json"]

[languageServer]
command = "typescript-language-server"
args = ["--stdio"]
path = ["${workspaceFolder}/node_modules/.bin"]
~~~

## Turning a server on

When the document's mode has a server, the document is saved in a
[workspace folder](language-modes.md#workspace-folder), and the server's
command is found, **View > Language Server** becomes available and the status
bar shows ⚡ before the mode's name as a shortcut to its **Enable** item.
Choosing it turns the server on for that workspace folder and starts it;
choosing it again turns it off and stops it. Azunote remembers which servers
are on for which folders, and starts them again when a document in the folder
is opened. Other windows on the same folder follow along.

A server runs programs and reads settings from the workspace folder, so turn it
on only for folders you trust. A server that is on for one folder stays off for
others, and changing its `command` turns it off everywhere.

A document outside every workspace folder gets the menu and the ⚡ only
when the server has `allowOutsideWorkspace = true`. Such a server is turned on
once for all files outside a workspace folder, and runs without one, which suits
servers that look at a single file, such as those for JSON and YAML.

The ⚡ is dimmed while the server is off, turns into a spinner while it starts,
and is highlighted while it runs. If the server fails to start or stops by
itself, the ⚡ shows a warning and resting the pointer on it tells why.
**Enable** stays checked, and **Restart** starts the server anew for every
document it serves. Restart also picks up an updated server program, or settings
the server reads only when it starts.

**Show Log** opens a read-only window with what the server and Azunote have said
to each other: how the server was started, every message either way, what the
server wrote to standard error, and why it failed. It starts with the last 2,000
lines and keeps adding new ones until it is closed. Very long messages, such as
those carrying a whole document, are cut short.

## What a server provides

- **Problems**: what the server reports appears as [problems](problems.md),
  with the server's command as their source. They are updated as the document
  changes.
- **Completion**: Edit > Show Completions (Ctrl+Space) and the server's trigger
  characters suggest what the server offers instead of document words. A
  suggestion with placeholders is inserted with their default text.
- **Descriptions**: resting the pointer on a symbol shows what the server says
  about it, after any problem there.
- **Inlay hints**: the server's hints, such as parameter names before
  arguments and inferred types after names, appear in the text in a dimmed
  style. They are asked for the visible lines once typing or scrolling pauses.
  Many servers show them only when a setting turns them on.
- **Formatting**: Edit > Format Document (Shift+Alt+F) rewrites the document
  as the server suggests, with the document's indentation, as one step to undo.
  It is available while a server that formats runs. An enabled
  [tool](tool-definitions.md) with the same shortcut takes the shortcut.

One server process serves every document in its workspace folder, from every
window and every mode that names the same command and arguments. It is stopped
a minute after its last document is closed.

## `[languageServer]`

| Field | Description |
| --- | --- |
| `command` | The program to run. Required. |
| `args` | Arguments for the program. Most servers need one such as `--stdio` to talk over standard input and output. |
| `path` | Folders searched for `command` before `PATH`, as in a [tool definition](tool-definitions.md). |
| `languageId` | The language identifier sent to the server. Defaults to the mode's `id`. |
| `initializationOptions` | A table sent to the server when it starts. |
| `settings` | A table of settings the server asks for, such as `[languageServer.settings.python.analysis]`. |
| `allowOutsideWorkspace` | `true` also offers the server for files outside every workspace folder. Defaults to `false`. |

`command`, `args`, and `path` accept
[substitution variables](substitution-variables.md) such as
`${workspaceFolder}`. A relative `command` or `path` is relative to the folder
of the definition file.

The server runs in the workspace folder, if it has one. A `.cmd` or `.bat` command, such as
one installed by npm, runs through `cmd.exe`, and a `.ps1` command through
PowerShell.

## Examples

~~~toml
# modes/python.toml
workspacePatterns = ["pyproject.toml"]

[languageServer]
command = "pyright-langserver"
args = ["--stdio"]

[languageServer.settings.python.analysis]
typeCheckingMode = "basic"
~~~

A server that loads one project file can be given the file that marks the
workspace folder. csharp-ls otherwise loads the first solution it finds
anywhere below the folder, which can be one in a submodule. In a folder that is
a workspace only because of `.git`, `${workspaceFile}` is empty and the server
gets an empty argument.

~~~toml
# modes/csharp.toml
workspacePatterns = ["*.slnx", "*.sln"]

[languageServer]
command = "csharp-ls"
args = ["--solution", "${workspaceFile}"]
~~~

~~~toml
# modes/json.toml
[languageServer]
command = "vscode-json-language-server"
args = ["--stdio"]
allowOutsideWorkspace = true

# The JSON server formats only when asked to.
[languageServer.initializationOptions]
provideFormatter = true
~~~
