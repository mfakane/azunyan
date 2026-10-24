# Settings

Azunote keeps its configuration in `%LOCALAPPDATA%\Azunote`. The portable ZIP
uses the `appdata` folder next to `Azunote.exe` instead. Tools > Preferences...
opens the folder in Explorer. It is created on first launch and contains:

| Path | Purpose |
| --- | --- |
| `settings.toml` | Application settings. |
| `state.toml` | Window layout and recent files, managed by Azunote. |
| `default-tools.toml` | Record of the [default tools](external-tools.md#default-tools) offered so far, managed by Azunote. |
| `tools/` | [External tools](external-tools.md). |
| `modes/` | [Custom language modes](language-modes.md). |
| `backups/` | Copies of [unsaved documents](documents.md#unsaved-changes) for the next launch. |
| `logs/` | Error logs, one file per day. |

Changes made to these files outside Azunote apply right away in every window.
If a tool or language-mode definition is invalid, the previous menu stays in
place until the definition is fixed.

## settings.toml

The file uses TOML syntax. Azunote completes setting names and values while it
is edited in Azunote.

| Setting | Description |
| --- | --- |
| `fontFamily` | Editor font. The default is `Consolas`. |
| `fontSize` | Editor font size. The default is `14`. |
| `windowSnapping` | Align Azunote windows to each other while moving or resizing them. The default is `true`. |
| `encodings` | Encodings listed in the encoding menus besides UTF-8, UTF-8 BOM, UTF-16 LE, UTF-16 BE, and the system's code page, such as `["euc-jp", "iso-2022-jp"]`. Use encoding names or code page numbers. The default is `[]`. |
| `[debug].logging` | Extra logging for troubleshooting: any of `all`, `render`, `clipboard`, `key`, and `input`. The default, `[]`, logs errors only. |
| `[tools].powerShellWarmProcesses` | Most PowerShell processes kept ready at once for [PowerShell commands](powershell-tools.md#start-up-time), 1 to 16. The default is half the logical processor count, between 1 and 4. |
| `[tools].powerShellWarmIdleProcesses` | PowerShell processes kept ready while no tool is running, 0 to `powerShellWarmProcesses`. The default is `1`. |
| `[terminal]` | Command run by Open Folder in Terminal in the status bar. |
| `[explorer]` | Command run by Show in Explorer in the status bar. |

The defaults are equivalent to:

```toml
fontFamily = "Consolas"
fontSize = 14
windowSnapping = true

[debug]
logging = []

[terminal]
command = "wt.exe"
args = ["-d", "${documentDirname}"]
workingDirectory = "${documentDirname}"

[explorer]
command = "explorer.exe"
args = ["/select,\"${file}\""]
workingDirectory = "${documentDirname}"
```

`[terminal]` and `[explorer]` accept
[substitution variables](substitution-variables.md). If `workingDirectory` is
omitted, the document's folder is used. The command runs without a shell, and
each item of `args` is passed as one argument.

## state.toml

Azunote rewrites `state.toml` itself, so it should not be edited. It holds the
last window layout, the Word Wrap and Status Bar choices, and up to 10 recently
opened files.
