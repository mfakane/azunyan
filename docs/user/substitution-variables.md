# Substitution variables

Commands configured in Azunote can refer to the document and its surroundings
with substitution variables. A variable has the form `${name}`, and an
environment variable the form `${env:NAME}`.

Unknown variables are left unchanged. A known variable with no value in the
current context, such as `${documentFile}` in an untitled document, expands to
an empty string.

## Where variables are expanded

- `settings.toml`: `command`, `args`, and `workingDirectory` in
  [`[terminal]` and `[explorer]`](settings.md#settingstoml).
- [External tool definitions](tool-definitions.md): `command`, `args`, `cmd`,
  `pwsh`, `workingDirectory`, and `stdin` in `[launch]`, and the values in
  `[env]`.

Variables are expanded each time the command runs. An argument stays one
argument after expansion, even when the value contains spaces; it is not passed
through a shell.

## VS Code-style variables

These variables follow the names of VS Code's predefined variables.

| Variable | Expands to |
| --- | --- |
| `${userHome}` | The current user's home folder. |
| `${workspaceFolder}` | The [workspace folder](language-modes.md#workspace-folder) of the document: the nearest folder above it that contains `.git`, an `.editorconfig` with `root = true`, or a file matching its language mode's `workspacePatterns`, or the tool's own [`workspacePatterns`](tool-definitions.md#top-level-fields) when it has them. |
| `${workspaceFolder:pattern}` | The nearest folder above the document that contains a file matching the glob `pattern`. Separate several patterns with `\|`, for example `${workspaceFolder:*.sln\|*.slnx}`. |
| `${workspaceFile}` | The file in `${workspaceFolder}` that matches those `workspacePatterns`, or empty when none does. |
| `${workspaceFile:pattern}` | The file matching `pattern` in `${workspaceFolder:pattern}`. |
| `${workspaceFolderBasename}` | The name of `${workspaceFolder}`. |
| `${workspaceFolderBasename:pattern}` | The name of `${workspaceFolder:pattern}`. |
| `${file}` | The path of the file given to the tool. For a document with unsaved changes, or an untitled one, this is a [temporary copy](tool-definitions.md#temporary-copies). |
| `${fileWorkspaceFolder}` | The workspace folder of the document. |
| `${relativeFile}` | The document path relative to `${workspaceFolder}`. |
| `${relativeFileDirname}` | The document's folder relative to `${workspaceFolder}`. |
| `${fileBasename}` | The file name of `${file}`, including its extension. |
| `${fileBasenameNoExtension}` | The file name of `${file}` without its extension. |
| `${fileExtname}` | The extension of `${file}`, including the leading dot. |
| `${fileDirname}` | The folder containing `${file}`. |
| `${fileDirnameBasename}` | The name of the folder containing `${file}`. |
| `${cwd}` | The current folder of the Azunote process. |
| `${lineNumber}` | The caret's line number, counting from 1. |
| `${columnNumber}` | The caret's column number, counting from 1. |
| `${selectedText}` | The selected text. |
| `${input}` | The tool's [input](tool-definitions.md#input), or the current part of it for a `per` run. |
| `${input:1}` | The first numbered capture group of the current `per = "regex:..."` match. |
| `${input:groupname}` | The named capture group `groupname` of the current `per = "regex:..."` match. |
| `${execPath}` | The path of the running Azunote executable. |
| `${pathSeparator}` | The operating system's path separator. |
| `${/}` | Short for `${pathSeparator}`. |

The `${file*}` variables describe the file given to the tool, which can be a
temporary copy. Use the `${document*}` variables below when the document's own
path is needed.

`${workspaceFolder:pattern}` checks the document's folder first and then each
folder above it. Patterns are relative to the folder being checked, so use `**`
for a file that may be further down, such as `**/*.csproj`. If nothing matches,
the variable expands to an empty string.

When several files match, `${workspaceFile}` takes the one matching the
earliest pattern, and among those the first by name, so
`${workspaceFile:*.slnx|*.sln}` prefers a `.slnx` file.

A pattern searches on its own, so `${workspaceFolder:pattern}` and
`${workspaceFile:pattern}` can be in a folder above `${workspaceFolder}`, such
as the outer repository for a document in a submodule. Use `${workspaceFile}`
to name a file in the folder a [language server](language-servers.md) runs in.

## Azunote variables

| Variable | Expands to |
| --- | --- |
| `${documentFile}` | The document's path, or empty for an untitled document. |
| `${documentDirname}` | The document's folder, or empty for an untitled document. |
| `${documentDirnameBasename}` | The name of `${documentDirname}`. |
| `${documentName}` | The document's file name. |
| `${documentBasenameNoExtension}` | The document's file name without its extension. |
| `${documentExtension}` | The document's extension, including the leading dot. |
| `${tempFile}` | The path of the temporary copy for a document with unsaved changes or an untitled one; empty otherwise. |
| `${toolFolder}` | The folder containing the external tool definition. |
| `${document}` | The complete document text. |
| `${chatHistory}` | For a [prompt](prompt-tools.md), the conversation so far as a JSON array of role/content messages, including the latest user message. |
| `${languageId}` | The identifier of the document's language mode. |
| `${encoding}` | The document's encoding, such as `utf-8` or `shift_jis`. |
| `${lineEnding}` | The document's line ending, such as `Lf`. |
| `${platform}` | The operating system, such as `windows`. |
| `${architecture}` | The processor architecture, such as `x64` or `arm64`. |
| `${selectionStartLine}` | The line where the selection starts, counting from 1. |
| `${selectionStartColumn}` | The column where the selection starts, counting from 1. |
| `${selectionEndLine}` | The line where the selection ends, counting from 1. |
| `${selectionEndColumn}` | The column where the selection ends, counting from 1. |

## Environment variables

`${env:NAME}` reads an environment variable. Names are matched without regard
to case.

`[terminal]` and `[explorer]` see Azunote's own environment. External tools
also read the nearest `.env` file: the first one found in the document's folder
or a folder above it. An untitled document has no folder, so it reads none.

The `.env` syntax is intentionally small:

```dotenv
# Comments and blank lines are allowed.
API_URL=https://example.test
export API_TOKEN="secret token"
EMPTY_VALUE=
UNQUOTED=value # an inline comment
SINGLE_QUOTED='literal value'
DOUBLE_QUOTED="another literal"
```

Malformed lines are ignored, and a repeated key keeps its last value. Values
cannot span lines or refer to other variables: `A=${B}` sets `A` to the literal
text `${B}`.

For an external tool, a later source overrides an earlier one:

```text
Azunote's environment < nearest .env < the tool's [env]
```

The result is both what `${env:NAME}` reads and the environment the tool runs
with. Values in a tool's `[env]` table may use other substitution variables,
including environment variables from Azunote or `.env`:

```toml
name = "Run formatter"

[launch]
command = "formatter"
args = ["${file}", "--config", "${env:FORMATTER_CONFIG}"]

[env]
FORMATTER_CONFIG = "${workspaceFolder}/formatter.json"
FORMATTER_MODE = "check"
```

While editing a definition, completion offers the variables in Azunote's
environment. Variables that only `.env` defines are not offered, but they are
still expanded when the tool runs.
