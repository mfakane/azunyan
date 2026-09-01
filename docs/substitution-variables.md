# Substitution Variables

Azunote expands substitution variables in command-related settings. A variable
has the form `${name}`. Environment variables use the form `${env:NAME}`.

Unknown variables are left unchanged. If a known variable has no value in the
current context, it expands to an empty string.

## Where variables are expanded

Variables are expanded in the following fields:

- `settings.toml`: `terminal.command`, `terminal.args`,
  `terminal.workingDirectory`, and the corresponding `explorer` fields.
- External tool definitions: `[launch].command`, `[launch].args`,
  `[launch].workingDirectory`, and values in the `[env]` table.

Expansion is performed before an external process is started. The expanded
arguments remain separate arguments; they are not passed through a shell.

## VS Code-style variables

These variables follow the names used by VS Code's predefined variables.

| Variable | Expands to |
| --- | --- |
| `${userHome}` | The current user's home directory. |
| `${workspaceFolder}` | The workspace folder containing the document. Azunote discovers it from the nearest ancestor containing `.git` or an `.editorconfig` with `root = true`. |
| `${workspaceFolderBasename}` | The name of `${workspaceFolder}`. |
| `${file}` | The path of the file supplied to the tool. For a dirty or untitled document this is the temporary execution file. |
| `${fileWorkspaceFolder}` | The workspace folder containing the document. |
| `${relativeFile}` | The document path relative to `${workspaceFolder}`. |
| `${relativeFileDirname}` | The document directory relative to `${workspaceFolder}`. |
| `${fileBasename}` | The name of the execution file, including its extension. |
| `${fileBasenameNoExtension}` | The execution file name without its extension. |
| `${fileExtname}` | The extension of the execution file, including the leading dot. |
| `${fileDirname}` | The directory containing the execution file. |
| `${fileDirnameBasename}` | The name of the directory containing the execution file. |
| `${cwd}` | The current working directory of the Azunote process. |
| `${lineNumber}` | The one-based line number of the caret. |
| `${columnNumber}` | The one-based column number of the caret. |
| `${selectedText}` | The currently selected text. |
| `${execPath}` | The path of the running Azunote executable. |
| `${pathSeparator}` | The operating system's path separator. |
| `${/}` | A shorthand for `${pathSeparator}`. |

`${file}` and the other `file*` variables describe the file used for the
 execution. `${file}` can therefore point to a temporary file when the current
 document has unsaved changes or has not been saved yet. Use the `document*`
 variables below when the original document path is required.

## Azunote-specific variables

| Variable | Expands to |
| --- | --- |
| `${documentFile}` | The original document path on disk, or empty for an untitled document. |
| `${documentDirname}` | The directory containing the original document, or empty for an untitled document. |
| `${documentDirnameBasename}` | The name of `${documentDirname}`. |
| `${documentName}` | The original document file name. |
| `${documentBasenameNoExtension}` | The original document file name without its extension. |
| `${documentExtension}` | The original document extension, including the leading dot. |
| `${tempFile}` | The temporary execution-file path when the document is dirty or untitled; empty otherwise. |
| `${toolFolder}` | The directory containing the external tool definition. |
| `${document}` | The complete document text. |
| `${languageId}` | The active language-mode identifier. |
| `${encoding}` | The current document encoding, such as `Utf8`. |
| `${lineEnding}` | The current document line-ending style, such as `Lf`. |
| `${platform}` | The operating-system identifier, such as `windows`, `linux`, or `macos`. |
| `${architecture}` | The process architecture, such as `x64` or `arm64`. |
| `${selectionStartLine}` | The one-based line number where the selection starts. |
| `${selectionStartColumn}` | The one-based column where the selection starts. |
| `${selectionEndLine}` | The one-based line number where the selection ends. |
| `${selectionEndColumn}` | The one-based column where the selection ends. |

For an untitled document, variables that require the original document path
expand to an empty string. When an external tool is run, `${file}` and the
other `file*` variables can still refer to the temporary execution file.

## Environment variables

Use `${env:NAME}` to read an environment variable. Names are matched without
regard to case.

For `terminal.*` and `explorer.*`, the value comes from the Azunote process
environment. External tools additionally load a `.env` file:

1. Start at the original document's directory.
2. Walk toward the filesystem root.
3. Use the first `.env` file found.

An untitled document has no original document directory, so it does not load a
`.env` file. The nearest `.env` also applies when the execution file is a
temporary file; the search is based on the original document path.

The supported `.env` syntax is intentionally small:

```dotenv
# Comments and blank lines are allowed.
API_URL=https://example.test
export API_TOKEN="secret token"
EMPTY_VALUE=
UNQUOTED=value # an inline comment
SINGLE_QUOTED='literal value'
DOUBLE_QUOTED="another literal"
```

Malformed lines are ignored. Duplicate keys use the last value. Variable
interpolation and multiline values are not supported; for example,
`A=${B}` in `.env` remains the literal value `${B}`.

The precedence for external tools is:

```text
inherited process environment < nearest .env < external tool [env]
```

The resulting values are used both for `${env:NAME}` expansion and for the
environment of the child process. Values in an external tool's `[env]` table
may reference other substitution variables, including inherited process or
`.env` values:

```toml
name = "Run formatter"

[launch]
command = "formatter"
args = ["${file}", "--config", "${env:FORMATTER_CONFIG}"]

[env]
FORMATTER_CONFIG = "${workspaceFolder}/formatter.json"
FORMATTER_MODE = "check"
```

Environment-variable completion is based on the current Azunote process
environment. Variables that exist only in `.env` are still expanded when the
external tool runs, but are not added to the completion list.
