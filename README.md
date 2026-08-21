# Azunote

Azunote is a small WinUI 3 single-document text editor using Windows App SDK
2.4.0. Each window owns one document; there is intentionally no project or
workspace layer in this phase.

## Build

The project targets Windows x64 and uses the Windows App SDK 2.4.0.

```powershell
dotnet build Azunyan.slnx -c Debug
```

The UI-independent editing model lives in `src/Azunyan.Core` and can be
tested without WinUI:

```powershell
dotnet test tests/Azunyan.Core.Tests/Azunyan.Core.Tests.csproj
```

If the plain .NET CLI cannot locate the Visual Studio Appx/PRI build tasks, run
the command from a Visual Studio Developer PowerShell or pass the installed
Visual Studio AppxPackage directory explicitly with `-p:AppxMSBuildToolsPath`.

## Usage

```powershell
Azunote.exe path\to\file.txt
```

The editor supports File/Edit/View/Help menus, Open, Save, Save As,
UTF-8/UTF-8 BOM/UTF-16 detection, line-ending reporting, dirty-title tracking,
find/replace, basic editing commands, keyboard shortcuts, and dropping a file
onto the editor.

The editor surface is `AzunyanEditorControl`, a `TextBox`-derived control that
keeps Windows' native text-service integration. This provides Japanese IME
composition, candidate-window placement, scrolling, selection rendering, and
the standard Edit UI Automation patterns. Its mirrored `Azunyan.Core.Document`
uses UTF-16 offsets at the WinUI boundary but exposes scalar and extended
grapheme-cluster movement/deletion so surrogate pairs, combining sequences,
and joined emoji are not split by editor commands.

`AzunyanEditorView` is the reusable host for future editor rendering. It keeps
the native editor as the IME/input host and exposes independent gutter, text,
and overlay layers through `IAzunyanEditorRenderer`. The default renderer draws
line numbers; syntax text, decorations, and inlay hints can be added without
changing the document or input API. Native text remains the default until a
custom text renderer is ready to own selection and caret painting as well.
