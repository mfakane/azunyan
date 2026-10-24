# PowerShell commands

`pwsh` in a [tool definition](tool-definitions.md) runs one PowerShell command
line, with PowerShell 7 (`pwsh.exe`) or else Windows PowerShell
(`powershell.exe`). It behaves like `Get-Content document.txt | <command line>`
at a prompt: the text in `stdin` arrives as its lines.

## Reading the input

How the line gets the input depends on how it is written.

A line that names `$input` gets the lines as an array, so it can work on the
input as a whole. Wrapping the work in `&{ ... }` reads as it would at a
prompt:

```toml
[launch]
pwsh = '&{ ($input | Sort-Object -Unique) -join [Environment]::NewLine }'
input = "selection"
stdin = "${input}"
stdout = "replaceSelection"
```

Unlike in a PowerShell script block, `$input` here is an array, so
`$input -join ...`, `$input.Count`, `$input[0]`, and reading it twice all work.

A line that does not name `$input` and is one pipeline starting with a command
gets the lines as that pipeline's input, as in
`Get-Content document.txt | Sort-Object`:

```toml
[launch]
pwsh = "Sort-Object -Unique"
input = "selection"
stdin = "${input}"
stdout = "replaceSelection"
```

A `process` block runs once per line there, as it does at a prompt:
`pwsh = '&{ process { $_.TrimEnd() } }'`.

Anything else runs with standard input unread. Read it yourself to get the text
exactly as written, including its line endings and any trailing newline, which
splitting into lines loses:

```toml
[launch]
pwsh = '$text = [Console]::In.ReadToEnd(); [Console]::Out.Write($text.TrimEnd())'
```

## Output

What the line produces is written without a trailing newline, so an action
such as `replaceSelection` adds no blank line that the input did not have.
Strings are joined with the newline that standard input used, so a command that
works line by line keeps the document's line endings. Other values are
formatted as PowerShell would show them.

Text written to `[Console]::Out` directly is kept exactly, trailing newline
included. Output produced before `exit` is still written. Input and output are
UTF-8.

## Start-up time

While any tool uses `pwsh`, Azunote keeps PowerShell processes started and
ready, so a tool does not wait for PowerShell to start. Each process serves
one run and then exits, so every run starts fresh, exactly as if it were
launched on its own.

`[tools].powerShellWarmIdleProcesses` in [settings.toml](settings.md#settingstoml)
sets how many processes are kept ready while nothing runs.
`[tools].powerShellWarmProcesses` sets how many may be ready at once for a
`per` run that launches several. A higher value helps long `per` runs on a
machine with many processor cores, but competes with the run for the
processor on one with few.
