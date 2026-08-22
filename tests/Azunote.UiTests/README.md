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

The current tests verify that the projected editor exposes UI Automation text and visible
range geometry, and that a projected text range can find and select document text.
Actual IME composition, DPI changes, GPU antialiasing, and theme changes remain manual
acceptance cases because they depend on the active Windows desktop, IME, monitor, and GPU.
