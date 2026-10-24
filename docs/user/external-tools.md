# External tools

External tools run other programs on the document from the Tools menu, the
editor context menu, or a keyboard shortcut. A tool can insert its output,
replace the document or the selection, open it in a new window, offer it as a
[proposed change](proposed-changes.md), or show it as [problems](problems.md)
in the document.

Each tool is a small TOML file. This one formats the current file with Prettier
and reloads it:

```toml
name = "Format document"
shortcut = "Alt+Shift+F"

[launch]
command = "prettier"
args = ["--write", "${file}"]
output = ["reloadFile", "ignore"]

[when]
extensions = [".js", ".ts", ".json", ".css", ".html"]
```

See [Tool definitions](tool-definitions.md) for every field.

## Where tools live

Tools are read from the `tools` folder of the [settings folder](settings.md):

- A file ending in `.tool.toml` is one tool.
- A folder becomes a submenu. For example,
  `tools\Formatting\CSharp\format.tool.toml` appears under
  Tools > External Tools > Formatting > CSharp.
- A folder ending in `.tool` with a `manifest.toml` file inside is one tool
  together with its scripts. It appears as a single menu item.
- Hidden files and folders are skipped, as are junctions and folder symbolic
  links.

Because hidden entries are skipped, a Git repository of tools can be cloned
straight into the tools folder: Git for Windows hides its `.git` folder. Hiding
an entry is also a way to turn tools off without moving them: `attrib +h` on a
folder or a definition file removes it and everything below it from the menu,
and `attrib -h` brings it back. A tool missing from the menu may have been
hidden.

Adding, editing, or removing a definition applies right away. An invalid
definition is reported, and the previous menu stays until it is fixed.

## Menus

Each external tool in the Tools menu has two parts: the item runs the tool, and
its `...` button offers `Edit...` and `Show in Explorer` for the definition.
The command palette (Ctrl+Shift+P) finds tools along with the other menu
commands.

The `menus` field also puts a tool in the editor context menu, after the
built-in commands. There, a tool's name includes its folders, such as
`Formatting: CSharp: Format`.

## Default tools

Azunote comes with tools under `Edit`, `Insert`, `Check`, `Format`, `Run`, and
`Git`, and keeps them up to date in the tools folder each time it starts. For
example, Insert Date/Time (F5) inserts the current date and time as
`2026-10-22 14:30`, and the `Check` tools report [problems](problems.md) in
PowerShell, JSON, and XML documents. Check JSON and Run PowerShell Script need
PowerShell 7 (`pwsh.exe`) and are hidden without it.

The default tools double as examples; the `...` button of a tool opens its
file:

| Tool | Shows |
| --- | --- |
| Sort Selected Lines, Join Lines | `pwsh` with `input = "selection"` and `replaceSelection` |
| Trim Trailing Whitespace | `replaceDocument` and a `shortcut` |
| Insert Date/Time, Insert GUID | A tool without input that inserts at the caret |
| Calculate | `input = "prompt:..."` with `showPromptResponse`, a chat that answers each message |
| Complete File Name | `showCompletion`, with `workingDirectory` |
| Check JSON, Check XML | `diagnostics`; Check JSON also `[when].executable` |
| Format Markdown Table | A `.tool` folder whose `manifest.toml` runs a script beside it through `${toolFolder}` |
| Prettier, dotnet format | `command` and `args` with `reloadFile` |
| Prettier (Preview) | `proposeDocument`, formatting from standard input as a change to apply or discard |
| ConvertFrom-Json \| ConvertTo-Json | `priority`, sharing Alt+Shift+F with Prettier |
| Run Batch File, Run Python Script | `cmd` and `command`, streaming `output` into a new window, with `[when].patterns` and `document = "clean"` |
| Blame File, File History | `newDocument`, with `[when].exists = ".git"` |
| Show File Diff | `newDocument:diff`, opening the output in the Diff mode |

An update replaces a default tool's file only while the file is as Azunote
wrote it. A file you have edited is left alone, and a default tool whose file
you deleted does not come back.
