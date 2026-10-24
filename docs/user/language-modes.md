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
[settings folder](settings.md). Azunote fills the folder with example
definitions on first launch and never overwrites it afterwards. Adding,
editing, or removing a definition applies right away. Custom entries in View >
Language Mode offer `Edit...` and `Show in Explorer` for their files.

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

`patterns` match the file name, or the path when a pattern contains a path
separator. When several modes match, the most specific matching suffix wins.

`completionTriggerCharacters` lists the text that opens completion after it is
typed.

The rules are applied in the listed order. The rule types are `delimited`,
`line`, `literal`, `keyword`, and `regex`. Toggle Comment uses the first `line`
rule classified as `comment`, or else the first such `delimited` rule.
