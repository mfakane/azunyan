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
bar shows ⚡ before the mode's name as a shortcut to it. Choosing it turns the
server on for that workspace folder and starts it; choosing it again turns it
off and stops it. Azunote remembers which servers are on for which folders, and
starts them again when a document in the folder is opened. Other windows on the
same folder follow along.

A server runs programs and reads settings from the workspace folder, so turn it
on only for folders you trust. A server that is on for one folder stays off for
others, and changing its `command` turns it off everywhere.

The ⚡ is dimmed while the server is off, turns into a spinner while it starts,
and is highlighted while it runs. If the server fails to start or stops by
itself, the ⚡ shows a warning and resting the pointer on it tells why. The menu
item stays checked, so choosing it turns the server off and choosing it again
starts it anew.

## What a server provides

- **Problems**: what the server reports appears as [problems](problems.md),
  with the server's command as their source. They are updated as the document
  changes.
- **Completion**: Edit > Show Completions (Ctrl+Space) and the server's trigger
  characters suggest what the server offers instead of document words.
- **Descriptions**: resting the pointer on a symbol shows what the server says
  about it, after any problem there.

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

`command`, `args`, and `path` accept
[substitution variables](substitution-variables.md) such as
`${workspaceFolder}`. A relative `command` or `path` is relative to the folder
of the definition file.

The server runs in the workspace folder. A `.cmd` or `.bat` command, such as
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

~~~toml
# modes/json.toml
[languageServer]
command = "vscode-json-language-server"
args = ["--stdio"]
~~~
