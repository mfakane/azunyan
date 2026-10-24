# Tool definitions

An [external tool](external-tools.md) is a TOML file with the tool's name and
menus at the top, followed by the `[launch]`, `[when]`, `[diagnostics]`, and
`[env]` tables:

```toml
name = "Format document"
shortcut = "Alt+Shift+F"
menus = ["tools", "context"]
visibility = "whenAvailable"

[launch]
command = "prettier"
args = ["--stdin-filepath", "${file}"]
input = "document"
stdin = "${input}"
stdout = ["replaceDocument", "ignore"]
stderr = ["ignore", "newDocument"]

[when]
extensions = [".js", ".ts", ".json", ".css", ".html"]

[env]
NODE_ENV = "development"
```

Azunote completes field names and values while a definition is edited in
Azunote.

## Top-level fields

| Field | Description |
| --- | --- |
| `name` | Name shown in the menu. Defaults to the file or folder name. |
| `shortcut` | Keyboard shortcut, such as `Alt+Shift+F`, shown beside the menu item. It works whether the tool is in the Tools menu, the context menu, or both. While the tool is available, the shortcut runs it instead of a built-in command on the same keys. |
| `priority` | Which tool runs when several available tools share a shortcut: the highest value wins, and equal values keep menu order. Defaults to `0`. |
| `menus` | Where the tool is shown: `tools`, `context`, or both. Defaults to `["tools"]`. |
| `visibility` | `whenAvailable` hides the tool when it does not apply or its command cannot be found. `always` shows it disabled instead. |

## `[launch]`

`[launch]` says what to run, what to give it, and what to do with its output.

| Field | Description |
| --- | --- |
| `command` | Program or script to run, with `args`. |
| `args` | Arguments for `command`. Each item is passed as one argument. |
| `cmd` | A command line run by `cmd.exe`. |
| `pwsh` | A command line run by PowerShell. See [PowerShell commands](powershell-tools.md). |
| `path` | Folders searched before `PATH` to find `command`. Missing folders are skipped. |
| `workingDirectory` | Folder the tool runs in. A relative path starts at the definition's folder. Defaults to the document's folder, or Azunote's own when the document is untitled. If the folder does not exist, the tool cannot run. |
| `input` | What `${input}` holds: `none` (the default), `filePath`, `document`, `selection`, or `prompt:<placeholder>`. See [Input](#input). |
| `preserveChatHistory` | Keep a prompt's conversation after its chat closes. See [Prompts and chat](prompt-tools.md). Defaults to `false`. |
| `per` | Run once (`none`, the default), once for each line (`line`), or once for each match (`regex:<pattern>`). See [Running per line or match](#running-per-line-or-match). |
| `stdin` | Text written to standard input. Nothing is written by default. |
| `output` | What to do with standard output and standard error together. See [Output](#output). |
| `stdout` | What to do with standard output. |
| `stderr` | What to do with standard error. |
| `stream` | Outputs applied while the tool runs instead of after it exits. See [Streaming output](streaming-output.md). |

Exactly one of `command`, `cmd`, or `pwsh` is required. `command` runs a
program without a shell. `cmd` and `pwsh` hand their whole value to the shell
as one command line; when the value is an array, its items are joined with
spaces, and items containing spaces are quoted:

```toml
[launch]
cmd = "echo Hello"
```

`command`, `args`, `cmd`, `pwsh`, `workingDirectory`, `stdin`, and the `[env]`
values can use [substitution variables](substitution-variables.md).

### Input

`input` chooses what `${input}` holds:

- `none`: an empty string.
- `filePath`: the path of the file given to the tool.
- `document`: the whole document text.
- `selection`: the selected text.
- `prompt:<placeholder>`: text typed into a chat when the tool runs. See
  [Prompts and chat](prompt-tools.md).

The input is not sent to the tool by itself. Refer to it in `stdin`, `args`, or
elsewhere:

```toml
input = "selection"
stdin = "${input}"
```

Standard input, output, and error use UTF-8 without a byte-order mark, so set
the tool to read and write UTF-8 for text beyond ASCII.

### Temporary copies

When the document has unsaved changes or is untitled, Azunote saves its current
text to a temporary file for the tool, and `${file}` refers to that file. The
file is removed when the tool finishes. `${documentFile}` always refers to the
document's own file.

### Running per line or match

`per = "line"` runs the tool once for each line of the input, empty lines
included. `per = "regex:<pattern>"` runs it once for each match of a .NET
regular expression, and does not run it at all when nothing matches. In each
run, `${input}` is that line or match, and `${input:1}` or `${input:name}` is a
capture group of the match. `stdin` is expanded again for each run:

```toml
input = "selection"
per = "line"
stdin = "${input}\n"
```

The runs happen one after another, and all of them run even when one fails.
Their outputs are joined, with nothing added between them, before the output
action applies.

### Output

`output`, `stdout`, and `stderr` each take an action, or a pair
`[success, failure]` that chooses by exit code: the first for 0, the second for
anything else. All three default to `ignore`. `output` gets standard output and
standard error mixed in the order they arrive, and the actions apply in the
order `output`, `stdout`, `stderr`.

| Action | Effect |
| --- | --- |
| `ignore` | Nothing. |
| `replaceDocument` | Replace the whole document. |
| `replaceSelection` | Replace the selection and select the output. With no selection, insert at the caret and leave the caret after the output, as typing would. |
| `newDocument` | Open the output in a new window, in Plain Text. `newDocument:<mode>` opens it in that language mode, such as `newDocument:diff`. |
| `reloadFile` | Load `${file}` back into the document, for a tool that rewrites the file in place. A [temporary copy](#temporary-copies) is loaded back the same way. |
| `showCompletion` | Offer each non-empty line as a completion at the caret. |
| `showPromptResponse` | Add the output to the [chat](prompt-tools.md) as a response. |
| `diagnostics` | Show the output as [problems](problems.md) in the document. |
| `proposeDocument` | Offer the output as a [proposed change](proposed-changes.md) to the whole document. |
| `proposeSelection` | Offer the output as a [proposed change](proposed-changes.md) to the selection. |

## `[when]`

`[when]` limits a tool to the documents it applies to. Every condition given
must hold. The menus check the conditions each time they open, and a shortcut
checks them when pressed.

| Field | Description |
| --- | --- |
| `extensions` | File extensions, such as `.md` or `.cs`. An untitled document matches the extensions of its language mode. |
| `patterns` | File-name patterns, such as `*.test.js`. An untitled document matches none. |
| `languages` | Language-mode identifiers. |
| `file` | `any`, `backed` (the document has a file), or `untitled`. |
| `selection` | `any`, `empty`, `nonEmpty`, or `regex:<pattern>`. A selection longer than 16 KiB, or one that takes too long to match, does not match a pattern. |
| `document` | `any`, `clean` (no unsaved changes), or `dirty`. |
| `os` | Operating systems, such as `windows`. |
| `exists` | A path or glob, or an array of them that must all exist. See below. |
| `executable` | A program name, or an array of them, that must be found where `command` would be: in `path`, then in `PATH`. See below. |

A saved document is always matched by its own extension, whichever language
mode it shows. An untitled document in the JSON mode, for example, gets the
tools for `.json`, `.jsonc`, and `.json5`.

`exists` looks for a file or folder:

- A bare name, such as `.git`, is looked for in the document's folder and each
  folder above it.
- `./name` looks only in the document's folder.
- `${workspaceFolder}/name` or `${workspaceFolder:pattern}/name` looks in that
  folder.
- `|` separates alternatives within one entry, and `!` before an alternative
  negates it.

`executable` checks for a program that a `cmd` or `pwsh` line calls, which
Azunote cannot see otherwise. The extension can be left out, so `pwsh` finds
`pwsh.exe`, and `*` and `?` match any characters. `|` and `!` work as in
`exists`, so `!prettier` holds when Prettier is not installed.

## `[diagnostics]`

| Field | Description |
| --- | --- |
| `pattern` | Regular expression the `diagnostics` action reads each output line with. See [Problems](problems.md#other-formats). |

## `[env]`

`[env]` sets environment variables for the tool. See
[Environment variables](substitution-variables.md#environment-variables).
