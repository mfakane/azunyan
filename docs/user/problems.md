# Problems

A tool whose output uses the `diagnostics` action shows the errors and warnings
it prints in the document, and a [language server](language-servers.md) shows
the ones it finds. Each problem is underlined in red for an error,
yellow for a warning, or blue for information, and resting the pointer on it
shows the message and the tool that reported it.

The status bar counts the problems and shows the message of the one at the
caret. Edit > Go to Next Problem (F8) and Go to Previous Problem (Shift+F8)
select the next or previous one. Edit > Show Problems... (Ctrl+Shift+M), or
clicking the counts in the status bar, lists them in the command palette, and
choosing one selects it.

```toml
[launch]
command = "ruff"
args = ["check", "--output-format", "concise", "${file}"]
stdout = "diagnostics"

[when]
extensions = [".py"]
```

## Output formats

Azunote reads the two forms most compilers and linters print:

```text
src\Program.cs(10,5): warning CS0168: The variable 'x' is declared but never used
src/app.py:3:8: error: F401 `os` imported but unused
```

The column, the end position (`(line,col,endLine,endCol)`), and the severity
are optional. A line without a severity is an error, and `note`, `hint`, and
`info` are information. Without an end, the problem covers the word at its
column, or the whole line when there is no column. Other lines are skipped.

Only problems in the document the tool ran on are shown: those naming its path,
the [temporary copy](tool-definitions.md#temporary-copies) given to the tool,
a path ending in the document's relative path, or no file at all. `-` and
`<stdin>` also mean the document, for a tool that reads it from standard input.

## Other formats

`[diagnostics].pattern` reads any other format. It is a regular expression
matched against each line, with the named groups `line` and `message` and,
optionally, `file`, `column`, `endLine`, `endColumn`, and `severity`:

```toml
[launch]
command = "mylint"
args = ["${file}"]
stdout = "diagnostics"

[diagnostics]
pattern = '^line (?<line>\d+), col (?<column>\d+), (?<severity>\w+) - (?<message>.+)$'
```

## Keeping problems up to date

Running a tool again replaces the problems it reported before, so a run that
prints none clears them. Problems from other tools stay. Editing moves the
problems after the edit along and removes the ones whose text it changes, and
opening another file clears them all. If the document is edited while the tool
runs, that run's problems are not shown.
