# Streaming output

A tool's output is normally applied once the tool exits. `stream` applies the
named outputs while the tool is still running, so a slow tool shows its text as
it produces it. It takes one of `output`, `stdout`, and `stderr`, or an array
of them:

```toml
[launch]
pwsh = "Get-Content .\build.log -Wait -Tail 0"
stdout = "newDocument"
stream = "stdout"
```

For a [chat](prompt-tools.md) that shows a response as it is generated, use
`stdout = "showPromptResponse"` with `stream = "stdout"`, or
`output = "showPromptResponse"` with `stream = "output"`.

## Rules

Because streamed output is applied before the tool exits:

- A streamed output takes a single action, not a `[success, failure]` pair,
  since the exit code is not known yet.
- Only `replaceDocument`, `replaceSelection`, `newDocument`, and
  `showPromptResponse` can stream.
- `output` carries the same text as `stdout` and `stderr`, so `stream` can name
  `output`, or `stdout` and `stderr`, but not both.
- Two streamed outputs cannot use the same action.

A definition that breaks these rules is reported as invalid.

## How the text arrives

Streaming ends with the same result as waiting for the tool. Text goes into the
document a line at a time; a line that is still being written appears once the
tool pauses for a moment. A chat response grows as text arrives.

`newDocument` opens its window when the first output arrives, so a tool that
prints nothing opens no window. `replaceSelection` replaces the selection with
the first output and adds the rest after it, ending with the whole output
selected, or the caret after it, as it does without `stream`.

All of a run's changes to the document are one undo step, even when the tool
was canceled or failed partway: the text it wrote so far stays, and one undo
removes it.

## Editing while a tool writes

The document stays editable while a tool streams into it. The tool writes into
its own part of the document: the selection until its first output, and its own
output after that. Editing before that part moves it along, and editing inside
it makes the edit part of the tool's output. An edit that removes part of it,
such as replacing the whole document, stops the tool. An edit made while the
tool writes also splits its undo step.

## PowerShell commands

A [PowerShell command](powershell-tools.md) streams as well, and its output is
written the same way. The one difference is that values other than strings are
formatted one at a time, rather than together, so a streaming PowerShell
command is best written to produce strings.
