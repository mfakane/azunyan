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
The fixture starts only the process it owns and closes it after the test class finishes.

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
