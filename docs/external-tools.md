# External Tools

External tools are user-defined processes available from Tools > External
Tools. They can also be invoked through the `ExternalToolRunner` API. See
[Substitution Variables](substitution-variables.md) for command expansion and
environment-variable behavior.

Commands run without a shell. Arguments remain separate arguments. On Windows,
command and PowerShell scripts are launched through the appropriate system
launcher.

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
input = "document"
per = "none"
stdin = "${input}"
output = ["ignore", "ignore"]
stdout = "ignore"
stderr = "ignore"

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
| `args` | Optional array of strings | Arguments passed to the process. Defaults to an empty array. |
| `workingDirectory` | Optional string | Working directory. Relative paths are resolved from the tool definition directory. If omitted, the current document's directory is used when available; otherwise the Azunote process directory is used. |
| `input` | `none`, `filePath`, `document`, `selection` | Selects the value exposed as `${input}`. It is not written to standard input automatically. |
| `per` | `none`, `line`, `regex:<pattern>` | Runs once for the input, each line, or each regex match. |
| `stdin` | String | Text written to standard input after substitution expansion. Empty by default. |
| `output` | Action or two-item array | Handles the mixed stdout/stderr stream. An array is `[zero, non-zero]`. |
| `stdout` | Action or two-item array | Handles stdout only. An array is `[zero, non-zero]`. |
| `stderr` | Action or two-item array | Handles stderr only. An array is `[zero, non-zero]`. |

The defaults are:

- `args = []`
- `workingDirectory` is omitted; the current document's directory is used when
  available, otherwise the Azunote process directory.
- `input = "none"`
- `per = "none"`
- `stdin` is empty, so nothing is sent to standard input.
- `output = "ignore"`, `stdout = "ignore"`, and `stderr = "ignore"` for both
  zero and non-zero exit codes.

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

`input` selects the value exposed through `${input}`:

- `none`: expose an empty string.
- `filePath`: expose the execution-file path.
- `document`: expose the complete document text.
- `selection`: expose the selected text.

`per` controls how the selected input is partitioned. `none` runs the tool once.
`line` removes CRLF/LF/CR separators and preserves empty parts. `regex:<pattern>`
uses .NET regular-expression matching and runs the tool once for each match.
For a regex run, `${input}` is the complete match, while `${input:1}` and
`${input:groupname}` refer to numbered and named capture groups. If the regex
has no matches, the tool is not invoked. Runs are sequential, and all runs
happen even when one exits with a non-zero code.

`stdin` is expanded separately for each run and is the only configured value
written to standard input. For example:

```toml
input = "selection"
per = "line"
stdin = "${input}\n"
```

When a document is dirty or untitled, Azunote writes the current text to a
temporary execution file. `${file}` refers to that file, and it is removed
after the tool finishes.

`output`, `stdout`, and `stderr` accept one action or an array of two actions.
An array is ordered `[zero-exit-action, non-zero-exit-action]`; a single action
is used for both exit statuses. All three fields support `ignore`,
`replaceDocument`, `replaceSelection`, `newDocument`, and `reloadFile`.

`output` receives the mixed stdout/stderr stream. The mixed stream is assembled
from the order in which stdout/stderr read chunks arrive. The individual
`stdout` and `stderr` streams remain available independently. Actions are
applied in the order `output`, `stdout`, `stderr`.

For `per` runs, each stream is concatenated without adding a separator. The
default for all three output fields is `ignore`.

No migration is provided for the changed input semantics.
