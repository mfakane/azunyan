# Azunyan Code Editor Architecture

Status: In progress

Implemented in the current milestone: the temporary syntax glyph overlay is
removed from Azunote, viewport layers are clipped, Core owns projection and
anchor mapping, `Azunyan.Layout` provides monospace line layout, wrapped
continuation rows, and a variable-height viewport index, provider scheduling is
split into document/viewport/position channels with stale-result,
cancellation, and provider-error isolation, and Azunote has a projected-text
renderer for visible lines. Visual rows model block adornment heights, inline
inlay identity, and fold-hidden blocks. Tooltip and completion popups are
anchored through the same projected caret geometry. The native TextBox remains
a deliberate transitional IME and accessibility host; the current wrapped
surface is a bounded DirectWrite/Win2D milestone. Final input ownership,
accessibility, and large-document performance gates remain.

This document defines the target architecture for Azunyan as a practical code
editor engine. Azunote remains a lightweight example application that enables
only a small subset of the engine.

## 1. Goals

Azunyan must support, without changing document text:

- syntax classification and text decorations;
- completion and tooltips;
- line gutters and diagnostics;
- collapsed regions and placeholders;
- inline hints that occupy horizontal layout space;
- CodeLens-like block adornments that occupy vertical layout space;
- wrapping, selection, caret movement, hit testing, and scrolling in the
  presence of those projections;
- IME input and UI Automation;
- large documents without creating one XAML element per token or line.

`Azunyan.Core` does not embed language knowledge, an LSP client, or a parser.
The optional `Azunyan.Syntax` library supplies composable lexical rules and
common language definitions; parser- and LSP-backed results remain providers
supplied by the application and can be composed into the same priority list.

## 2. Architectural decision

The editor owns one virtualized text layout and rendering pipeline. A TextBox
or RichEditBox must not own the visible text layout, and colored glyphs must not
be painted over glyphs rendered by another control.

The central pipeline is:

```text
TextSnapshot
    -> provider result stores
    -> projection model
    -> visual line map
    -> shaped line layouts
    -> clipped viewport frame
    -> drawing, hit testing, input, and accessibility
```

The immutable `TextSnapshot` remains the source of truth. Projection objects
refer to snapshot positions but never insert display-only text into the
document.

### Alternatives

| Alternative | Decision | Reason |
| --- | --- | --- |
| Native TextBox plus colored text overlays | Rejected | Two layout/rasterization owners cannot maintain identical glyph geometry, clipping, selection, and scrolling. |
| RichEditBox as the editor surface | Rejected as the long-term boundary | It is useful for formatted text, but does not expose the projection, virtualization, arbitrary inline UI, or line-layout ownership needed for folding, inlays, and CodeLens. |
| One XAML element per line or token | Rejected | Visual-tree size and allocation would scale with document content rather than viewport size. |
| Custom virtualized text surface with a replaceable input bridge | Accepted | It gives Azunyan one geometry source for rendering, hit testing, scrolling, folding, and adornments while keeping platform input replaceable. |

## 3. Package boundaries

The target solution is split into the following responsibilities. They may be
separate projects once the first custom-rendered surface is introduced.

### Azunyan.Core

- persistent document and immutable snapshots;
- UTF-16 document positions, ranges, selections, and changes;
- provider contracts and snapshot-bound result types;
- projection primitives and document/visual position mapping;
- no WinUI, DirectWrite, brush, font, or UIElement dependencies.

### Azunyan.Layout

- construction of visual lines from a snapshot and projection data;
- wrapping and tab expansion;
- line height index and viewport range lookup;
- text shaping abstraction, glyph runs, caret stops, and hit-test maps;
- caches keyed by snapshot, projection version, and typography.

The shaping implementation may be platform-specific, but its inputs and
outputs remain UI-framework independent.

### Azunyan.WinUI

- the clipped Win2D drawing surface;
- DirectWrite text layouts, styling, caret geometry, and selection regions;
- scrollbars and pointer/keyboard routing;
- IME input bridge and caret rectangle publication;
- realization and recycling of interactive adornments;
- UI Automation peer and text-range providers.

### Azunote

- window, file commands, and menus;
- the lightweight Azunote language definition;
- theme and provider composition;
- no editor layout algorithms.

Theme ownership follows the same boundary. `Azunyan.WinUI` defines the
`AzunyanColorScheme` shape and passes it through each render context, while the
application supplies concrete colors. Azunote's default scheme is assembled
from WinUI system brush resources for the light theme, so the projected text,
gutter, selection marks, and transient popups share one palette. Other hosts
may assign their own scheme through `AzunyanEditorView.ColorScheme`.

## 4. Coordinate spaces

The design uses distinct position types. An unqualified integer must never be
used for both document and visual coordinates.

```csharp
public readonly record struct DocumentPosition(int Offset);

public enum AnchorAffinity
{
    Before,
    After
}

public readonly record struct DocumentAnchor(
    DocumentPosition Position,
    AnchorAffinity Affinity);

public readonly record struct VisualPosition(
    int VisualLine,
    int CaretStop);
```

`DocumentPosition` is a UTF-16 offset in one specific `TextSnapshot`.
`VisualPosition` addresses a caret stop in one specific projection and layout
version. Public APIs carrying either type also carry or are owned by the
corresponding snapshot/frame.

The mapping must satisfy these invariants:

1. Mapping is monotonic in document order.
2. Every visible document boundary has at least one visual caret stop.
3. An inline adornment has two visual caret stops but consumes no document
   positions. Affinity distinguishes its left and right sides.
4. Positions inside a collapsed range map to the fold placeholder boundary.
5. A selection remains a pair of document positions. Projection affects only
   its painting and hit testing.
6. A visual position from an old frame is never accepted by a new frame
   without remapping through its document anchor.

## 5. Projection model

A projection is immutable and bound to one snapshot. It is composed from
provider results plus editor-owned user state such as which folding ranges are
collapsed.

```csharp
public abstract record ProjectionInline;

public sealed record ProjectedText(TextRange Source)
    : ProjectionInline;

public sealed record FoldPlaceholder(
    TextRange HiddenSource,
    string DisplayText,
    string FoldId)
    : ProjectionInline;

public sealed record InlineAdornment(
    string Id,
    DocumentAnchor Anchor,
    string Kind,
    AdornmentContent Content)
    : ProjectionInline;

public sealed record BlockAdornment(
    string Id,
    DocumentAnchor Anchor,
    double DesiredHeight,
    string Kind,
    AdornmentContent Content);

public sealed record AdornmentContent(
    string Text,
    string? IconKey,
    IReadOnlyList<AdornmentAction> Actions);

public sealed record AdornmentAction(
    string Id,
    string Label,
    string CommandId);
```

These declarations are conceptual API shapes. The production Core types must
use immutable, provider-neutral payloads rather than WinUI objects.

### Syntax and decorations

Syntax classification does not change projection geometry. Classification
spans are resolved to theme styles while shaped runs are built. Adjacent runs
with identical effective typography are merged before shaping.

Decorations such as underline, squiggle, background highlight, and diagnostic
markers are separate drawing primitives. They reuse the glyph hit-test map and
do not create duplicate text.

### Folding

A folding provider supplies candidate source ranges and stable fold IDs. The
editor owns collapsed/expanded state separately from provider results.

When collapsed, one `FoldPlaceholder` replaces the entire source range,
including any line delimiters it contains. Hidden positions cannot receive the
caret. Editing at a fold boundary either expands the fold or applies an
explicit command policy; it must never silently edit a hidden location.

Fold state is remapped after edits by stable ID when available, then by mapped
range as a fallback. Invalid or overlapping folding ranges are normalized
deterministically before projection.

### Inline adornments

Inlay hints and parameter hints are declarative inline adornments. Their
renderer returns metrics such as width, height, and baseline; those metrics
participate in line breaking and hit testing.

The default form is draw-only text or an icon with an optional command and
tooltip. It does not instantiate a XAML element. This allows thousands of
inlays without thousands of visual-tree nodes.

### Block adornments

CodeLens is a block adornment anchored before or after a logical line. Its
height contributes to the visual line height index and therefore to scrolling,
gutter positions, selection geometry, and viewport lookup.

Only visible interactive block adornments are realized as UI elements. They
are keyed by ID and recycled while scrolling. Non-interactive CodeLens can be
drawn directly by the editor surface.

## 6. Visual lines and variable heights

A logical document line can produce zero, one, or multiple visual text rows:

- zero when fully hidden by a fold;
- one when unwrapped;
- multiple when wrapped;
- additional block rows before or after it.

```csharp
public sealed class VisualLine
{
    public required int LogicalLine { get; init; }
    public required TextRange SourceRange { get; init; }
    public required IReadOnlyList<ProjectionInline> Inlines { get; init; }
    public required double Height { get; init; }
    public required double Baseline { get; init; }
}
```

The editor maintains a height index supporting:

- prefix height before a visual line;
- visual line lookup by vertical offset;
- local height replacement after re-layout;
- total extent for the scrollbar.

The intended implementation is an augmented balanced tree or chunked Fenwick
index, with `O(log n)` lookup and update. A flat scan over all lines on every
scroll or layout pass is prohibited.

For unwrapped fixed-height text, all document rows have exact heights without
being realized. With wrapping, unmeasured chunks use an estimate. When exact
heights become known, scrolling preserves a document anchor at the top of the
viewport so content does not jump.

## 7. Layout and rendering

The layout engine receives projected inlines, effective styles, available
width, tab size, and typography. It returns:

- glyph runs and their drawing positions;
- visual bounds for decorations;
- caret stops mapped to document anchors;
- hit-test intervals;
- baseline and row height;
- continuation-row information for wrapping.

The renderer follows these rules:

- one shaping and rasterization path owns all visible glyphs;
- the viewport is always clipped before any editor content is drawn;
- visible rows plus bounded overscan are laid out and drawn;
- no XAML element is created per glyph, token, decoration, or ordinary line;
- gutter and text use the same frame and vertical height index;
- selection and caret are drawn from the same hit-test geometry as text;
- scroll events reuse cached line layouts and do not recreate the document
  projection.

A frame is immutable while it is painted:

```csharp
public sealed record EditorFrame(
    TextSnapshot Snapshot,
    long ProjectionVersion,
    long StyleVersion,
    ViewportState Viewport,
    IReadOnlyList<VisibleLineLayout> Lines);
```

Provider results that arrive during painting schedule a later frame. They do
not mutate the current frame.

## 8. Provider scheduling

The production boundary is `EditorProviderScheduler`; providers are separated
by invalidation scope so a caret move never restarts syntax analysis. The
aggregate coordinator remains only as a compatibility API.

### Snapshot-scoped providers

- syntax classification;
- decorations and diagnostics;
- folding ranges.

They run when the snapshot changes. Incremental providers may additionally
receive the previous snapshot and `TextChange`, but the editor must also accept
full snapshot results.

### Viewport-scoped providers

- inlay hints;
- CodeLens/block adornments;
- expensive gutter annotations.

They receive the snapshot and an expanded visible document range. Their cache
is invalidated by snapshot identity and requested range.

### Position-scoped providers

- completion;
- tooltip and hover;
- signature help.

They run on caret or hover changes and use their own cancellation generation.

Each provider channel has independent cancellation, request IDs, result store,
and error isolation. A result is publishable only when its snapshot and request
generation still match that channel. Partial result-store updates are allowed;
syntax need not wait for CodeLens or completion.

Proposed additional contracts are:

```csharp
public interface IFoldingProvider;
public interface IInlayProvider;
public interface IBlockAdornmentProvider;
```

Provider payloads contain IDs, document anchors/ranges, declarative display
data, and optional command IDs. They never contain brushes, UI elements, or
dispatcher-bound objects.

## 9. Input and IME

Input is isolated behind `ITextInputBridge`. Rendering does not depend on which
input bridge is active.

The first WinUI implementation may use a native text control as a small IME
proxy. It owns neither the document nor visible text. The bridge:

- translates committed text into document replacement commands;
- publishes composition updates as transient projection state;
- receives the rendered caret rectangle for candidate-window placement;
- synchronizes selection and surrounding text needed by the platform;
- never requires the renderer to duplicate native glyphs.

Composition state has a snapshot/document anchor and is invalidated or remapped
explicitly when an external edit occurs. Normal editor commands cannot split a
composition or grapheme cluster.

The bridge can later be replaced by a lower-level Windows text-services
implementation without changing projection, layout, provider, or rendering
contracts.

## 10. Accessibility

The custom surface supplies an AutomationPeer implementing the applicable text
patterns. Automation ranges are document ranges, not visual-line indices.

The peer must provide:

- document text and selection;
- caret range;
- visible ranges;
- range bounding rectangles from current layout data;
- point-to-range hit testing;
- scrolling a range into view;
- child providers for realized interactive adornments.

Fold placeholders and inlays expose names through annotations or realized
children, while copied text remains document text. Accessibility support is a
release requirement for replacing the native visible TextBox, not deferred
polish.

## 11. Threading and ownership

- `Document` mutations occur on the editor/UI owner thread.
- `TextSnapshot` and provider result values are immutable and worker-safe.
- provider computation and projection normalization run off the UI thread.
- font/device-bound layout objects are created on their owning render thread.
- frame publication is atomic; stale frames are discarded by snapshot and
  version checks.
- no provider callback executes synchronously in a pointer, keyboard, or scroll
  event.

## 12. Cache and invalidation keys

A shaped line cache key includes at least:

- snapshot identity or stable text-line content identity;
- logical line and projected segment identity;
- projection version for that line;
- effective style version;
- font family, size, weight, features, and DPI;
- tab size and wrapping width.

An edit invalidates touched logical lines plus provider-declared dependency
ranges. Changing a syntax color invalidates drawing data but not shaping when
font metrics are unchanged. Changing font weight, inlays, folding, CodeLens
height, tabs, or wrapping width invalidates layout geometry.

## 13. Performance acceptance envelope

The first production-capable prototype is measured against this initial
envelope. Exact limits can be revised from benchmark evidence.

- 100,000 logical lines and a 10 MiB UTF-8 source file;
- typing and caret movement do not scan or format the whole document;
- pointer, keyboard, and scroll handlers perform no provider work;
- scrolling realizes only the viewport plus at most two viewports of overscan;
- no per-token or per-line XAML elements for ordinary text;
- cached scrolling sustains the display refresh rate on the reference machine;
- a slow or cancellation-ignorant provider cannot block input or publish stale
  state;
- memory growth is proportional to document storage, line/projection indexes,
  provider data, and cached visible/chunk layouts rather than total glyph
  count.

Benchmarks cover plain text, dense syntax spans, one inlay per token, many fold
ranges, sparse CodeLens, wrapped long lines, mixed Japanese/emoji text, and
rapid edits while providers return out of order.

## 14. Migration from the current surface

### Phase A: contain the prototype

- remove colored `TextBlock` glyph overlays;
- clip gutter and overlay layers;
- retain the current TextBox as the visible editor temporarily;
- keep provider results data-only.

### Phase B: projection and mapping

- add document anchor/affinity types;
- implement projected text, fold placeholders, and inline adornments;
- add exhaustive monotonicity and round-trip mapping tests;
- add the variable-height visual line index.

### Phase C: custom unwrapped text surface

- draw visible unwrapped lines, selection, and caret through one text layout;
- synchronize gutter and scrolling from the visual line map;
- introduce the native IME proxy;
- retain a runtime fallback to the native TextBox until IME and accessibility
  acceptance tests pass.

Azunote now has the first bounded viewport version of this phase using cached
DirectWrite text layouts for visible rows and a paired gutter surface.
Selection, caret, syntax colors, fold placeholders, inlay styling, and line
numbers are drawn through the same measured text backend rather than through
per-line XAML text controls. The native TextBox remains the input/IME host
until the acceptance gates below pass.

### Phase D: provider channels and syntax

- split snapshot-, viewport-, and position-scoped provider scheduling;
- render syntax and decorations as style/drawing runs;
- consume completion results through an input-preserving popup;
- add incremental line invalidation and frame publication.

### Phase E: folding and adornments

- add fold provider and editor-owned collapse state;
- add inlay measurement and hit testing;
- add variable-height block adornments and visible UI realization;
- preserve viewport anchors while heights change.

The Core/Layout portion of this phase is now present: editor-owned collapsed
IDs feed the projection, inlays and block adornments are represented in visual
rows, inlays retain their provider identity through layout runs, and block
height participates in viewport realization. Azunote exposes
`SetFoldCollapsed`, `ToggleFold`, and `ExpandAllFolds`; its projected surface
also maps pointer presses through the same visual rows, including fold
placeholder clicks, wrapped rows, inlay identity, and block-row anchors. The
DirectWrite surface now consumes those layouts and uses DirectWrite line
metrics to choose wrapped-row boundaries. Richer inlay interaction and
input/accessibility replacement remain next gates.

### Phase F: wrapping and hardening

- harden continuation visual rows and wrapped hit testing with additional real
  font-metric cases;
- complete UI Automation text patterns;
- run IME, BiDi, grapheme, DPI, theme, and performance matrices;
- remove the native visible-text fallback only after all gates pass.

## 15. Effect on the current provider API

The following current work remains useful:

- immutable snapshots and UTF-16 ranges;
- syntax, decoration, tooltip, completion, and gutter result models;
- cancellation and stale-result principles;
- Azunote's lightweight provider implementations.

The following parts are explicitly transitional:

- one aggregate request that runs every provider for every caret change;
- `IAzunyanEditorRenderer` receiving mutable XAML Canvas layers;
- the native TextBox remaining visible as the input/IME proxy;
- line coordinates inferred from one measured character and a ScrollViewer
  offset.

Azunote's application shell also installs a failure boundary around the WinUI
dispatcher, AppDomain, and unobserved-task paths. Managed UI failures are
shown to the user with the per-user log path; process-level failures are
recorded even when the UI can no longer be trusted.

No new editor feature should depend on those transitional rendering details.

## 16. Open implementation choices

The following choices require focused prototypes, but do not change the
architecture above:

- Win2D is the selected DirectWrite/Direct2D backend for the current WinUI
  surface; direct COM interop remains an optimization option if profiling
  requires it;
- native TextBox proxy versus a lower-level Windows text-services bridge;
- exact augmented-tree/chunk structure for lazy visual-line heights;
- whether interactive adornments use pooled XAML controls or composition
  visuals with explicit accessibility peers.

The custom surface cannot replace the native visible editor by default until
the chosen prototypes pass Japanese IME composition/candidate placement,
screen-reader text navigation, mixed-script shaping, and the performance
envelope in this document.
