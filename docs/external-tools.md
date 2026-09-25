# External Tools

External tools are user-defined processes available from the Tools menu and,
when configured, the editor context menu. They can also be invoked through the
`ExternalToolRunner` API. See
[Substitution Variables](substitution-variables.md) for command expansion and
environment-variable behavior.

`command` runs without a shell and its `args` remain separate arguments. On
Windows, command and PowerShell scripts are launched through the appropriate
system launcher. Use `[launch].cmd` or `[launch].pwsh` when the value itself
should be evaluated as one shell command.

## Discovery and menu layout

External tools are discovered below `%LOCALAPPDATA%\Azunote\tools`:

- A file ending in `.tool.toml` is one tool definition.
- A normal directory becomes a submenu. For example,
  `tools\Formatting\CSharp\format.tool.toml` appears under
  Tools > External Tools > Formatting > CSharp.
- A directory ending in `.tool` with a `manifest.toml` file is one bundled
  tool. It appears as a tool leaf and is not traversed as a submenu.
- Hidden and system entries are skipped, and directories are skipped when
  they are a reparse point such as a junction or a directory symbolic
  link.

Skipping hidden entries keeps a cloned tool collection cheap to scan. Git
for Windows marks the `.git` directory hidden, so cloning a repository of
tool definitions directly into the tools folder works without further
configuration. It also gives a way to disable part of the tools folder
without moving it: `attrib +h` on a directory or definition file removes it
and anything below it from the menu, and `attrib -h` brings it back. A tool
that has gone missing from the menu is worth checking for the hidden
attribute.

Each Tools-menu external-tool item is a split item. Its main area runs the
tool; the `...` area provides `Edit...` and `Show in Explorer` for the
definition file.

The `menus` field controls where a tool is shown. Tools menu entries retain the
discovered folder hierarchy. Context-menu entries are added after the built-in
editor actions as top-level items; their names include the full folder path,
such as `Formatting: CSharp: Format`. Context-menu entries run the tool
directly and do not include the definition-file actions.

The settings watcher reloads tool definitions when files are added, edited, or
removed. It ignores the entries the scan skips, so work below a hidden
directory, such as a `git pull` writing to `.git`, does not reload anything.
Invalid definitions are reported and do not replace the last valid tool menu.

## Definition format

A tool definition uses TOML. The following example formats the current file
and reloads it after the command succeeds:

```toml
name = "Format document"
shortcut = "Alt+Shift+F"
menus = ["tools"]
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
extensions = [".js", ".jsx", ".ts", ".tsx", ".json", ".jsonc", ".json5", ".css", ".scss", ".html"]

[env]
NODE_ENV = "development"
```

### Top-level fields

| Field | Values | Description |
| --- | --- | --- |
| `name` | String | Name shown in the selected menu. If omitted, the definition file or bundle name is used. |
| `shortcut` | Shortcut string | Optional keyboard shortcut, such as `Alt+Shift+F`. It is shown beside the item in both menus. |
| `priority` | Integer | Shortcut tie-break when several tools share a key. The highest value among enabled tools runs. Defaults to `0`. Equal values keep discovery order. Menu order is unchanged. |
| `menus` | Array of `tools`, `context` | Menu surfaces where the tool is shown. Use both values to show it in both menus. Defaults to `["tools"]`. |
| `visibility` | `always`, `whenAvailable` | Whether the item remains visible when its conditions or command are unavailable. |

### `[launch]`

| Field | Values | Description |
| --- | --- | --- |
| `command` | Optional string | Executable or script to launch. Use this with `args`; it is mutually exclusive with `cmd` and `pwsh`. |
| `path` | Optional string or array | Directories searched before `PATH` when resolving `command`. A missing directory is skipped, so a later entry or the process `PATH` is used. |
| `args` | Optional array of strings | Arguments passed to `command`. Defaults to an empty array. Cannot be used with `cmd` or `pwsh`. |
| `cmd` | Optional string or array of strings | One command line evaluated by `cmd.exe`. Array elements are joined with spaces; elements containing spaces are automatically quoted. Mutually exclusive with `command` and `pwsh`. |
| `pwsh` | Optional string or array of strings | One command line evaluated by PowerShell (`pwsh.exe`, falling back to `powershell.exe`). Array elements are joined with spaces; elements containing spaces are automatically quoted. Mutually exclusive with `command` and `cmd`. |
| `workingDirectory` | Optional string | Working directory. Relative paths are resolved from the tool definition directory. If the expanded directory does not exist, the tool is not executable. If omitted, the current document's directory is used when available; otherwise the Azunote process directory is used. |
| `input` | `none`, `filePath`, `document`, `selection` | Selects the value exposed as `${input}`. It is not written to standard input automatically. |
| `per` | `none`, `line`, `regex:<pattern>` | Runs once for the input, each line, or each regex match. |
| `stdin` | String | Text written to standard input after substitution expansion. Empty by default. |
| `output` | Action or two-item array | Handles the mixed stdout/stderr stream. An array is `[zero, non-zero]`. |
| `stdout` | Action or two-item array | Handles stdout only. An array is `[zero, non-zero]`. |
| `stderr` | Action or two-item array | Handles stderr only. An array is `[zero, non-zero]`. |
| `stream` | Optional channel name or array | Channels whose output is applied while the tool runs instead of after it exits. Empty by default. |

The defaults are:

- `menus = ["tools"]`
- `args = []`
- `path` is omitted, so `command` is resolved from the process `PATH` only.
- `workingDirectory` is omitted; the current document's directory is used when
  available, otherwise the Azunote process directory.
- `input = "none"`
- `per = "none"`
- `stdin` is empty, so nothing is sent to standard input.
- `output = "ignore"`, `stdout = "ignore"`, and `stderr = "ignore"` for both
  zero and non-zero exit codes.
- `stream` is empty, so every channel is applied after the tool exits.

Exactly one of `command`, `cmd`, or `pwsh` is required. `command` and `args`
launch an executable without a shell. `cmd` and `pwsh` each launch their
respective shell with the configured value as one command string:

```toml
[launch]
cmd = "echo Hello"
```

A `pwsh` command is one command line, and it runs with the semantics of
`Get-Content document.txt | <command line>`: the text configured in `stdin` is
what `Get-Content` would have produced, and it reaches the line as its lines.
The one deliberate difference from that pipeline is `$input`, which is bound as
an array rather than the one-shot enumerator PowerShell hands a script block.

- A line that names `$input` gets the lines there as an array, so it can work on
  the input as a whole. Wrapping the work in `&{ ... }` reads the same way it
  does at a prompt:

  ```toml
  [launch]
  pwsh = '&{ ($input | Sort-Object -Unique) -join [Environment]::NewLine }'
  input = "selection"
  stdin = "${input}"
  stdout = "replaceSelection"
  ```

  `$input` is an array rather than the one-shot enumerator PowerShell hands a
  script block, so `$input -join ...`, `$input.Count`, `$input[0]` and a second
  pass all work, as does everything an enumerator supports, such as
  `$input | Sort-Object`.

- A line that names no `$input` and is one pipeline starting with a command
  receives the lines as that pipeline's input, exactly as in
  `Get-Content document.txt | Sort-Object`:

  ```toml
  [launch]
  pwsh = "Sort-Object -Unique"
  ```

  A `process` block works there the way it does at a prompt, running once per
  line: `pwsh = '&{ process { $_.TrimEnd() } }'`.

- Anything else runs with standard input unread. Read it to get the text exactly
  as it was written, line endings and a trailing newline included, which line
  splitting does not preserve:

  ```toml
  [launch]
  pwsh = '$text = [Console]::In.ReadToEnd(); [Console]::Out.Write($text.TrimEnd())'
  ```

What the line produces is written without a trailing newline, so an output
action such as `replaceSelection` inserts no blank line the input did not have.
Strings are written as they are, joined by the same newline standard input used,
so a line-wise command leaves the line endings of the document alone; anything
else is formatted the way PowerShell would, again without the trailing newline.
A line that writes to `[Console]::Out` itself keeps every byte it wrote, trailing
newline included, and what a line produced before calling `exit` is still
written. The text is written and read as UTF-8 throughout.

While at least one tool definition uses `pwsh`, Azunote keeps PowerShell
processes started and waiting so that the interpreter start-up cost is paid
before a run is requested. A waiting process serves exactly one run and then
exits; processes are never reused, so exit codes, standard input, stream
separation and `[env]` isolation are the same as a plain launch. Waiting
processes are replaced after each run, terminated when they stay unused, and
terminated when Azunote exits. If no waiting process is available, the tool is
launched the usual way and behaves identically.

How many wait is controlled by `[tools].powerShellWarmProcesses` and
`[tools].powerShellWarmIdleProcesses` in
[settings.toml](settings.md). The idle value is how many wait while nothing is
running. A run whose launch count is known in advance raises the count to that
many, up to `powerShellWarmProcesses`, and it returns to the idle value when
the run ends. `per` is what makes a run launch several processes, and the count
comes from the parts the current input actually produces, not from the `per`
value alone: `per = "line"` over a one-line selection still needs one process.
Nothing waits while no `pwsh` tool is configured.

The extra processes for a run are started while that run's earlier parts are
already executing, so raising `powerShellWarmProcesses` helps the later parts
rather than the first one, and a large value competes with the run for CPU. How
deep is useful therefore depends on how many cores are free, which is why the
default is derived from the logical processor count rather than fixed. In the
[recorded measurements](../tools/Azunote.Performance/README.md) a depth of 4 was
the fastest on 8 and 32 logical processors but slower than a depth of 1 on 4.

`command`, `args`, `cmd`, `pwsh`, and `workingDirectory` support substitution variables. The
`[env]` values do as well; see the [substitution variable documentation](substitution-variables.md)
for details.

### `[when]`

Conditions control whether a tool applies to the current context. All
specified conditions must match.

| Field | Description |
| --- | --- |
| `extensions` | File extensions, such as `.md` or `.cs`. An untitled document has none of its own, so the extensions of its language mode are matched instead. |
| `patterns` | File-name patterns. |
| `languages` | Language-mode identifiers. |
| `file` | `any`, `backed`, or `untitled`. |
| `selection` | `any`, `empty`, or `nonEmpty`. |
| `document` | `any`, `clean`, or `dirty`. |
| `os` | Operating-system identifiers, such as `windows`. |
| `exists` | A path or glob, or an array of them. Array entries are all required. `|` separates alternatives inside one entry, and `!` negates that alternative. A bare name walks from the document directory toward the root. `./` checks only the document directory. `${workspaceFolder}` and `${workspaceFolder:pattern}` name a specific root. A match may be a file or a directory. |

An untitled document is matched by `extensions` through its language mode:
choosing the JSON mode in an untitled document makes the tools for `.json`,
`.jsonc`, and `.json5` apply to it. `patterns` is about file names, so it
still needs a saved document, and a document that has been saved is always
matched by its own extension, whichever mode was chosen for it.

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

External-tool stdin, stdout, and stderr use UTF-8 without a BOM. Configure the
external tool to read and write UTF-8 for Japanese and other non-ASCII text.

When a document is dirty or untitled, Azunote writes the current text to a
temporary execution file. `${file}` refers to that file, and it is removed
after the tool finishes.

`output`, `stdout`, and `stderr` accept one action or an array of two actions.
An array is ordered `[zero-exit-action, non-zero-exit-action]`; a single action
is used for both exit statuses. All three fields support `ignore`,
`replaceDocument`, `replaceSelection`, `newDocument`, `reloadFile`, and
`showCompletion`.

`showCompletion` treats each non-empty output line as one completion candidate
and opens the completion window. Empty and whitespace-only lines are ignored.

Shortcuts are available regardless of whether the tool is shown in the Tools
menu, the context menu, or both.

`output` receives the mixed stdout/stderr stream. The mixed stream is assembled
from the order in which stdout/stderr read chunks arrive. The individual
`stdout` and `stderr` streams remain available independently. Actions are
applied in the order `output`, `stdout`, `stderr`.

For `per` runs, each stream is concatenated without adding a separator. The
default for all three output fields is `ignore`.

## Streaming output

By default a channel's output is applied once the tool has exited. `stream`
names the channels that are applied while the tool is still running, so a slow
tool fills the document as it produces text rather than in one step at the end.
It takes one channel name or an array of them, and the names are the output
fields themselves:

```toml
[launch]
pwsh = "Get-Content .\build.log -Wait -Tail 0"
stdout = "newDocument"
stream = "stdout"
```

Streaming is opt-in per channel because it changes what a tool can be relied on
to do, not only when its output appears:

- The output is applied before the exit code is known, so a streamed channel
  cannot choose its action by exit status. A streamed channel's action must be
  a single action rather than a `[zero, non-zero]` array.
- Only `replaceDocument`, `replaceSelection`, and `newDocument` can stream.
  `reloadFile` has no output to apply, and `showCompletion` needs the whole
  candidate list before it opens a window; `stream` naming a channel that uses
  either of them is an error.
- `output` carries the same text as `stdout` and `stderr`, so `stream` may name
  `output`, or `stdout` and `stderr`, but not both at once.
- Two streamed channels may not use the same action, since their text would be
  interleaved into one place.

A definition that breaks one of those rules is reported the way any other
invalid definition is, and does not replace the last valid tool menu.

What finally lands in the document is the same text a non-streaming run would
have applied. Output is applied a line at a time; a line that is still being
written appears once the tool has produced nothing for a moment, so a tool that
reports progress without newlines is still visible. A line ending is never split
across two applications, and neither is a surrogate pair.

`newDocument` opens its window when the first output arrives rather than when
the tool starts, so a tool that produces nothing opens no window. The other two
actions apply their first output as the replacement they describe and append
what follows, so `replaceSelection` leaves the selection covering the whole
output once the tool has exited, exactly as it does without `stream`.

One run is one undo step, including a `per` run that launched several
processes, and including a run that was cancelled or exited non-zero: what had
already been applied stays in the document, and one undo removes all of it. An
edit made between two of a run's own writes ends that step, so a document
edited while a tool streams into it takes more than one undo to get back.

Editing the document while a streamed run writes to it is allowed. The run owns
the part of the document it is writing: the selection until its first output
arrives, and its own output after that. An edit made before that part moves it,
so the next output still lands where the last output ended, and an edit made
inside it becomes part of what the run owns. An edit that takes part of it away,
such as replacing the whole document, stops the run and reports it.

A `pwsh` tool streams as well. Its output is still written without the trailing
newline the host would add, and strings are still joined by the newline standard
input used. The one difference from a buffered run is that a command producing
values that are not strings has each value formatted on its own, rather than the
whole sequence formatted together, so a tool that streams is best written to
produce strings.

No migration is provided for the changed input semantics.
