# Language modes

A language mode sets the highlighting, folding, comment syntax, and completion
for a document. Azunote has modes for C#, JavaScript, TypeScript, Python, JSON
(including JSONC and JSON5), XML, YAML, TOML, Markdown, PowerShell, and diffs,
besides Plain Text. Custom modes can be added for other formats.

When a file is opened, its name picks the mode; a file that no mode covers uses
Plain Text, which covers `.txt` and `.log`. View > Language Mode, or the mode
in the status bar, changes the mode until another file is opened. The Open and
Save As dialogs offer the same modes as file types.

## Comments

Edit > Toggle Comment (Ctrl+/) comments or uncomments the selected lines with
the mode's line comment, such as `//`. A mode without one wraps the lines in its
block comment, such as `<!--` and `-->`. Plain Text has no comment syntax, so
the command does nothing there.

## Completion

Edit > Show Completions (Ctrl+Space) suggests completions at the caret. A mode
without completions of its own, including Plain Text, suggests longer words
that already appear in the document. A custom mode can also open the
suggestions after typing a trigger such as `.` or `->`.

## Custom language modes

Custom modes are TOML files in the `modes` folder of the
[settings folder](settings.md). Adding, editing, or removing a definition
applies right away. Entries in View > Language Mode that have a definition file
offer `Edit...` and `Show in Explorer` for it.

Each file describes one mode:

~~~toml
id = "ini"
displayName = "INI"
patterns = ["*.ini", "*.cfg", "*.conf"]
completionTriggerCharacters = [".", "(", "{", "[", "->"]

[[rules]]
type = "line"
classification = "comment"
token = ";"

[[rules]]
type = "delimited"
classification = "string"
open = "\""
close = "\""
allowLineBreaks = false
escapePrefix = "\\"

[[rules]]
type = "keyword"
classification = "keyword"
words = ["true", "false"]
~~~

`id` identifies the mode, and defaults to the file name without `.toml`.
`displayName` is the name shown in menus and the status bar, and defaults to
the `id`.

`patterns` match the file name, or the path when a pattern contains a path
separator. When several modes match, the most specific matching suffix wins.

`completionTriggerCharacters` lists the text that opens completion after it is
typed.

The rules are applied in the listed order. The rule types are `delimited`,
`line`, `literal`, `keyword`, and `regex`. Toggle Comment uses the first `line`
rule classified as `comment`, or else the first such `delimited` rule.

`workspacePatterns` adds files that mark the
[workspace folder](#workspace-folder) of the mode's documents.

`[languageServer]` connects the mode to a
[language server](language-servers.md).

## Changing a built-in mode

A file whose `id` is that of a built-in mode changes that mode instead of
adding one. Only the settings written in the file change, and an array replaces
the built-in one rather than adding to it. A built-in mode keeps its own
highlighting, so such a file cannot have `rules`. The built-in ids are
`plain-text`, `azunote`, `csharp`, `javascript`, `typescript`, `python`,
`json`, `xml`, `yaml`, `toml`, `markdown`, `powershell`, and `diff`.

Since `id` defaults to the file name, `modes/typescript.toml` changes the
TypeScript mode:

~~~toml
workspacePatterns = ["package.json", "tsconfig.json"]

[languageServer]
command = "typescript-language-server"
args = ["--stdio"]
~~~

## Default definitions

Azunote keeps example definitions in the `modes` folder: an INI mode, and
files for several built-in modes whose settings are all commented out, so they
change nothing until a line is uncommented. Each start brings them up to date
with Azunote. A file you have edited is left alone, and a file you deleted does
not come back.

## Workspace folder

The workspace folder of a document is the nearest folder above it that contains
`.git`, or an `.editorconfig` with `root = true`. `workspacePatterns` adds
glob patterns to look for, in the same form as
[`${workspaceFolder:pattern}`](substitution-variables.md#vs-code-style-variables).
Whichever is found first while going up from the document wins, so with
`package.json` added, a package inside a repository is its own workspace.

The workspace folder is what
[`${workspaceFolder}`](substitution-variables.md) expands to and where a
[language server](language-servers.md) runs. Changing the language mode of a
document can change it.
