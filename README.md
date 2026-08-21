# Azunote

Azunote is a small WinUI 3 single-document text editor. Each window owns one
document; there is intentionally no project or workspace layer in this phase.

## Build

The project targets Windows x64 and uses the Windows App SDK 1.6.

```powershell
dotnet build Azunyan.slnx -c Debug
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
