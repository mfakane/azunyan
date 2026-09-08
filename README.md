![Icon of Azunote](assets/icons/01-paw-notebook-128.png)![Icon of Azunyan](assets/icons/02-paw-editor-128.png)

# Azunyan

Azunyan is a set of reusable text-editor components together with Azunote, a
small document-per-window text editor built with WinUI 3. Azunote-specific
build, usage, configuration, and runtime documentation is maintained in
[src/Azunote/README.md](src/Azunote/README.md).

## Components

| Project | Responsibility |
| --- | --- |
| `Azunyan.Core` | Documents, provider contracts, projections, line layout, wrapping, and viewport realization |
| `Azunyan.Syntax` | Composable lexical rules and built-in language definitions |
| `Azunyan.WinUI` | Reusable WinUI editor control, DirectWrite/Win2D rendering, input, and accessibility |
| `Azunote` | Application shell, file I/O, settings, language modes, external tools, and command-line integration |

`Azunyan.Core` and `Azunyan.Syntax` target `net10.0` and do not depend on WinUI.
`Azunyan.WinUI` and Azunote target Windows x64 and use Windows App SDK 2.4.0.

## Requirements

- .NET 10 SDK
- Windows x64 for the full solution, `Azunyan.WinUI`, and Azunote

If the plain .NET CLI cannot locate the Visual Studio Appx/PRI build tasks, run
the command from a Visual Studio Developer PowerShell or pass the installed
Visual Studio AppxPackage directory explicitly with `-p:AppxMSBuildToolsPath`.

## Build and test

Build the complete solution from the repository root:

```powershell
dotnet build Azunyan.slnx -c Debug
```

After building, run all normal tests with:

```powershell
dotnet test Azunyan.slnx -c Debug --no-build
```

The solution test command includes the Core/Layout, Syntax, and Azunote test
projects. Windows UI Automation tests are skipped unless explicitly enabled;
their interactive-desktop requirements and run instructions are documented in
[tests/Azunote.UiTests/README.md](tests/Azunote.UiTests/README.md).

The UI-independent component tests can also be run separately:

```powershell
dotnet test tests/Azunyan.Core.Tests/Azunyan.Core.Tests.csproj
dotnet test tests/Azunyan.Syntax.Tests/Azunyan.Syntax.Tests.csproj
```

Release publishing and application-specific test details are documented in
[src/Azunote/README.md](src/Azunote/README.md).

## Repository layout

The source tree is grouped by responsibility:

```text
src/
  Azunyan.Core/
    Documents/         document model, snapshots, ranges, and editing commands
    Providers/         provider contracts, frames, and scheduling
    Projection/        projected rows, folds, adornments, and height indexing
    Layout/
      LineLayout/      logical-line and wrapping layout
      Viewport/        bounded viewport realization and scrolling calculations
  Azunyan.Syntax/      composable lexical rules and built-in languages
  Azunyan.WinUI/
    Editor/            WinUI editor control, rendering host, input, and accessibility
    Rendering/         DirectWrite/Win2D primitives and color scheme
  Azunote/             WinUI application layer
tests/
  Azunyan.Core.Tests/  Core, projection, and layout tests
  Azunyan.Syntax.Tests/ syntax rules, composition, and language-definition tests
  Azunote.Tests/       application, external-tool, and command-line tests
  Azunote.UiTests/     opt-in Windows UI Automation tests
benchmarks/
  Azunyan.EditorBenchmarks/ editor model, syntax, projection, and layout benchmarks
docs/                  architecture and Azunote configuration documentation
```

## Editor components

### Providers

Provider APIs live in `Azunyan.Core`. Syntax, decoration, tooltip, completion,
gutter, folding, inlay, and block-adornment providers receive an immutable
`EditorProviderContext` containing a snapshot, caret position, selection, and,
for viewport-scoped requests, a visible range.

`EditorProviderScheduler` runs document-, viewport-, and position-scoped
channels independently and away from the caller thread. Each channel cancels
superseded work, rejects stale results, and isolates provider exceptions.
`EditorProviderCoordinator` remains as a compatibility aggregate.
Applications inject providers through `AzunyanEditorView.Providers`.
`EditorProviderFrame` exposes channel-specific results to the renderer, while
`EditorProviderResults` is retained as the legacy aggregate view.

### Syntax

`Azunyan.Syntax` supplies providers for delimited ranges, line remainders,
keywords, literal tokens, and regular-expression matches.
`CompositeSyntaxProvider` invokes its sources, collects their results with
`Task.WhenAll`, and then performs a left-to-right lexical scan. At each position
the first matching source wins and its complete token is consumed. This
prevents comment markers inside strings and quotes inside comments from
leaking into later classifications.

Rules and an LSP-backed or other custom provider can share the same priority
list. Earlier entries win when candidates start at the same position:

```csharp
Editor.Providers.Syntax = new CompositeSyntaxProvider(new ISyntaxProvider[]
{
    new DelimitedSyntaxRule("/*", "*/", "comment"),
    new LineRemainderSyntaxRule("//", "comment"),
    new DelimitedSyntaxRule("\"", "\"", "string", false, "\\"),
    semanticTokenProvider,
    new KeywordSyntaxRule(new[] { "class", "return" })
});
```

Applications can color classifications supplied by external providers without
changing the renderer. The default `AzunyanColorScheme` maps `heading`,
`keyword`, `string`, `code`, `number`, `comment`, `task-marker`, and `variable`
to distinct foreground colors. Returning `null` from a custom resolver retains
the built-in mapping and editor-foreground fallback:

```csharp
Editor.ColorScheme = Editor.ColorScheme with
{
    SyntaxForegroundResolver = classification =>
        classification == "variable" ? variableColor : null
};
```

### Projection, layout, and rendering

`Azunyan.Core.Projection` maps folds and inline adornments through document
anchors, creates wrapped and block-adornment visual rows, and maintains the
variable-height row index. The `Azunyan.Layout` namespace in `Azunyan.Core`
measures projected line runs and selects a bounded viewport window. The WinUI
projected surface uses those same rows for drawing and pointer hit testing,
including fold toggling, inlay identity, wrapped rows, and block-row anchors.

`Azunyan.WinUI` supplies the DirectWrite-backed Win2D layout and rendering
surface. `AzunyanEditorView.ColorScheme` provides one palette for text, gutter,
selection, caret, composition marks, inlays, and completion and tooltip popups.
An embedding application can replace the palette.

The current implementation uses bounded `CanvasControl` surfaces for visible
text and a sliding native input window for IME and text-service integration. A
projected UI Automation peer exposes text and value patterns; the remaining
acceptance work includes real IME, Narrator/NVDA, BiDi, DPI, theme, and GPU
matrices.

Design, performance targets, and migration notes are documented in
[docs/editor-architecture.md](docs/editor-architecture.md).
