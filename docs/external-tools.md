# External Tools

External tools are user-defined processes available from Tools > External
Tools. They can also be invoked through the `ExternalToolRunner` API. See
[Substitution Variables](substitution-variables.md) for command expansion and
environment-variable behavior.

Commands run without a shell and receive input through standard input when
configured. Arguments remain separate arguments. On Windows, command and
PowerShell scripts are launched through the appropriate system launcher.

## Discovery and menu layout

External tools are discovered below `%LOCALAPPDATA%\Azunote\tools`:

- A file ending in `.tool.toml` is one tool definition.
- A normal directory becomes a submenu. For example,
  `tools\Formatting\CSharp\format.tool.toml` appears under
  Tools > External Tools > Formatting > CSharp.
- A directory ending in `.tool` with a `manifest.toml` file is one bundled
  tool. It appears as a tool leaf and is not traversed as a submenu.

Each external-tool menu item is a split item. Its main area runs the tool; the
`...` area provides `Edit...` and `Show in Explorer` for the definition file.

The settings watcher reloads tool definitions when files are added, edited, or
removed. Invalid definitions are reported and do not replace the last valid
tool menu.

## Definition format

A tool definition uses TOML. The following example formats the current file
and reloads it after the command succeeds:

```toml
name = "Format document"
shortcut = "Alt+Shift+F"
visibility = "whenAvailable"

[launch]
command = "prettier"
args = ["--write", "${file}"]
workingDirectory = "${documentDirname}"
input = "none"
output = "reloadFile"

[when]
extensions = [".js", ".jsx", ".ts", ".tsx", ".json", ".css", ".scss", ".html"]

[env]
NODE_ENV = "development"
```

### Top-level fields

| Field | Values | Description |
| --- | --- | --- |
| `name` | String | Name shown in the Tools menu. If omitted, the definition file or bundle name is used. |
| `shortcut` | Shortcut string | Optional keyboard shortcut, such as `Alt+Shift+F`. |
| `visibility` | `always`, `whenAvailable` | Whether the item remains visible when its conditions or command are unavailable. |

### `[launch]`

| Field | Values | Description |
| --- | --- | --- |
| `command` | String | Executable or script to launch. |
| `args` | Array of strings | Arguments passed to the process. |
| `workingDirectory` | String | Working directory. Relative paths are resolved from the tool definition directory. If omitted, the current document's directory is used when available. |
| `input` | `none`, `filePath`, `document`, `selection` | Data written to standard input. |
| `output` | `ignore`, `replaceDocument`, `replaceSelection`, `newDocument`, `reloadFile` | How successful standard output is applied. |

`command`, `args`, and `workingDirectory` support substitution variables. The
`[env]` values do as well; see the [substitution variable documentation](substitution-variables.md)
for details.

### `[when]`

Conditions control whether a tool applies to the current context. All
specified conditions must match.

| Field | Description |
| --- | --- |
| `extensions` | File extensions, such as `.md` or `.cs`. |
| `patterns` | File-name patterns. |
| `languages` | Language-mode identifiers. |
| `file` | `any`, `backed`, or `untitled`. |
| `selection` | `any`, `empty`, or `nonEmpty`. |
| `document` | `any`, `clean`, or `dirty`. |
| `os` | Operating-system identifiers, such as `windows`. |

With `visibility = "whenAvailable"`, a tool is hidden when its conditions do
not match or its command cannot be resolved. With `visibility = "always"`, it
remains visible but is disabled in those cases.

## Input and output

`input` controls standard input:

- `none`: close standard input without sending data.
- `filePath`: send the execution-file path.
- `document`: send the complete document text.
- `selection`: send the selected text.

When a document is dirty or untitled, Azunote writes the current text to a
temporary execution file. `${file}` refers to that file, and it is removed
after the tool finishes.

`output` controls successful standard output:

- `ignore`: discard standard output.
- `replaceDocument`: replace the complete document.
- `replaceSelection`: replace the current selection.
- `newDocument`: open the output as a new document.
- `reloadFile`: reload the file written by the tool.

Non-zero exit codes leave the document unchanged and display standard error.
