# Azunote UI tests

These tests drive the unpackaged WinUI 3 application through Windows UI Automation.
They require a Windows machine with an unlocked interactive desktop. They are opt-in
so that the normal core test run never starts a GUI process.

Build the application first:

```powershell
dotnet build Azunyan.slnx -c Debug --no-restore
```

Run the UI smoke tests:

```powershell
$env:AZUNOTE_UI_TESTS = "1"
dotnet test tests/Azunote.UiTests/Azunote.UiTests.csproj -c Debug --no-build
```

If the executable is in a non-default location, set `AZUNOTE_EXE` to its full path.
Each test starts its own process and closes it when the test finishes, so document,
window, and menu state cannot leak into subsequent tests.

## Windows Sandbox

Use the repository script to run the tests on a disposable Windows desktop instead
of the host desktop:

```powershell
pwsh ./scripts/Test-AzunoteUiTestsInSandbox.ps1
```

The script requires Windows 11 version 24H2 or later with Windows Sandbox and its
`wsb.exe` CLI installed, plus an x64 .NET 10 SDK. Keep the host desktop unlocked
while it runs. By default, the script builds the solution on the host, finds the
`dotnet.exe` root from `PATH`, and mounts that directory read-only as a portable SDK
inside the Sandbox. An extracted SDK can be selected explicitly:

```powershell
pwsh ./scripts/Test-AzunoteUiTestsInSandbox.ps1 `
    -DotNetRoot C:\tools\dotnet-sdk-10
```

Pass `-NoBuild` to reuse existing `Debug` outputs, or `-Configuration Release` to
build and test Release outputs. Each run writes its available TRX, log, and exit-code
artifacts under `artifacts/ui-tests-sandbox/<run-id>`.

The repository and SDK mappings are read-only. Only the per-run result directory is
writable from the Sandbox. The script disables Sandbox networking, clipboard
redirection, and vGPU; copies the application and test outputs to the disposable
guest disk; runs `dotnet vstest` in the interactive Sandbox login; then stops only
the Sandbox instance it created. A failed run keeps every result file produced before
the failure.

The external-tool tests create uniquely named definitions in the executable's portable
`appdata/tools` directory and remove those definitions afterwards. Use a writable test
build directory. The selection test verifies disabled/enabled/disabled transitions,
reacquiring items because other tools can change visibility and rebuild the menu.
The filesystem test creates, edits and deletes `.env` in a separate temporary document
directory, outside the settings watcher. It verifies that an open menu updates without
editor input and retains the item's UI Automation runtime ID for enabled-only changes.
No external command is executed. To exercise the Native AOT build, set `AZUNOTE_EXE` to
its executable.

The current tests verify that the projected editor exposes TextPattern and a read/write
ValuePattern, reports geometry for empty caret ranges, keeps ranges bound to the snapshot
from which they were created, and does not expose the native IME input window as a
duplicate Edit control. They also cover visible range geometry, finding/selecting
document text, and long document values that require the input window to slide.
The Help-menu test checks bundled legal text, `[READONLY]` window titles,
read-only ValuePattern behavior, text selection, and disabled save commands.

The remaining accessibility acceptance is manual: focus the editor with Narrator or NVDA,
read through Japanese, emoji, combining-mark, Arabic, and Hebrew lines, move by character,
word, and line, select text, and edit through the value/text actions. Confirm that focus,
selection, text-change, layout, and interactive-child announcements remain coherent.
Actual IME composition, DPI changes, GPU antialiasing, and theme changes remain manual
acceptance cases because they depend on the active Windows desktop, IME, monitor, and GPU.
