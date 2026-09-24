using Azunyan.Core;
using Azunyan.Layout;
using Azunyan.WinUI;
using System.Globalization;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Text;

namespace Azunyan.WinUI;

/// <summary>
/// The bounded projected text surface. It realizes only visible projected rows;
/// rows may be unwrapped lines or continuation segments produced by the
/// framework-independent layout layer.
/// </summary>
internal sealed class ProjectedTextRenderer : ICanvasEditorRenderer
{
    private const double FoldChevronWidth = 20;
    private const double FoldChevronFontSize = 14;
    private readonly CanvasControl _gutterSurface;
    private readonly CanvasControl _textSurface;
    private readonly Dictionary<ProjectedLine, UnwrappedLineLayout> _lineLayouts = new();
    private readonly Dictionary<VisualRow, GutterLayoutEntry> _gutterLayouts = new();
    private readonly Dictionary<TextLayoutRowKey, TextLayoutEntry> _textLayouts = new();
    private readonly Dictionary<string, Button> _foldButtons = new(StringComparer.Ordinal);
    private readonly MonospaceLineLayoutEngine _lineLayoutEngine = new();
    private long _textLayoutCreates;
    private long _textLayoutHits;
    private ProjectedTextLayoutState? _cachedLayout;
    private ProjectedTextRenderFrame? _renderFrame;
    private DocumentChangedEventArgs? _pendingDocumentChange;
    private TextLayoutCacheKey? _textLayoutCacheKey;
    private IReadOnlyList<SyntaxSpan>? _lineLayoutSyntax;
    private bool _disposed;

    public ProjectedTextRenderer(CanvasControl gutterSurface, CanvasControl textSurface)
    {
        _gutterSurface = gutterSurface ?? throw new ArgumentNullException(nameof(gutterSurface));
        _textSurface = textSurface ?? throw new ArgumentNullException(nameof(textSurface));
        _gutterSurface.CreateResources += OnCreateResources;
        _gutterSurface.Draw += OnGutterDraw;
        _textSurface.CreateResources += OnCreateResources;
        _textSurface.Draw += OnDraw;
    }

    public event EventHandler? LayoutInvalidated;

    public double ContentHeight => _cachedLayout?.Heights.TotalHeight ?? 0;

    public void NotifyDocumentChanged(DocumentChangedEventArgs change)
    {
        if (_disposed)
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(change);
        _pendingDocumentChange = change;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gutterSurface.CreateResources -= OnCreateResources;
        _gutterSurface.Draw -= OnGutterDraw;
        _textSurface.CreateResources -= OnCreateResources;
        _textSurface.Draw -= OnDraw;
        ClearTextLayouts();
        _textLayoutCacheKey = null;
        _lineLayouts.Clear();
        _cachedLayout = null;
        _renderFrame = null;
        _pendingDocumentChange = null;
        LayoutInvalidated = null;
    }

    public bool TryGetVisibleDocumentRange(out TextRange range)
    {
        range = default;
        if (_cachedLayout is not { } layout
            || _renderFrame is not { } frame
            || layout.Rows.Rows.Count == 0)
        {
            return false;
        }

        var viewportStart = Math.Clamp(
            frame.Context.VerticalOffset,
            0,
            layout.Heights.TotalHeight);
        var viewportEnd = Math.Clamp(
            frame.Context.VerticalOffset + frame.Context.ViewportHeight,
            0,
            layout.Heights.TotalHeight);
        var firstRow = layout.Heights.FindLine(viewportStart);
        var lastRow = layout.Heights.FindLine(viewportEnd);
        var start = int.MaxValue;
        var end = 0;
        for (var rowIndex = firstRow; rowIndex <= lastRow; rowIndex++)
        {
            var row = layout.Rows.Rows[rowIndex];
            if (row.TextLine is { } textLine)
            {
                start = Math.Min(start, textLine.SourceRange.Start);
                end = Math.Max(end, textLine.SourceRange.End);
            }
            else if (row.BlockAdornment is { } block)
            {
                start = Math.Min(start, block.Anchor.Position.Offset);
                end = Math.Max(end, block.Anchor.Position.Offset);
            }
        }

        if (start == int.MaxValue)
        {
            return false;
        }

        range = TextRange.FromBounds(start, Math.Max(start, end));
        return true;
    }

    public IReadOnlyList<ProjectedTextAutomationTarget> GetAutomationTargets()
    {
        if (_renderFrame is not { } frame
            || _cachedLayout is null)
        {
            return Array.Empty<ProjectedTextAutomationTarget>();
        }

        var result = new List<ProjectedTextAutomationTarget>();
        for (var rowIndex = 0; rowIndex < frame.Layouts.Count; rowIndex++)
        {
            var rowLayout = frame.Layouts[rowIndex];
            var row = rowLayout.Row;
            if (row.BlockAdornment is { } block)
            {
                var width = Math.Max(
                    1,
                    Math.Min(
                        Math.Max(1, frame.Context.ViewportWidth - frame.Context.ContentLeft),
                        Math.Max(1, block.Content.Text.Length * frame.Context.CharacterWidth)));
                result.Add(new ProjectedTextAutomationTarget(
                    block.Id,
                    block.Kind,
                    block.Anchor,
                    TextRange.Empty(block.Anchor.Position.Offset),
                    string.IsNullOrEmpty(block.Content.Text)
                        ? block.Kind
                        : block.Content.Text,
                    IsFold: false,
                    IsBlock: true,
                    new Rect(
                        frame.Context.ContentLeft - frame.Context.HorizontalOffset,
                        frame.Context.ContentTop
                            + rowLayout.Top
                            - frame.Context.VerticalOffset,
                        width,
                        rowLayout.Height)));
                continue;
            }

            if (row.TextLine is not { } textLine)
            {
                continue;
            }

            var visualColumn = 0;
            foreach (var inline in textLine.Inlines)
            {
                switch (inline)
                {
                    case FoldPlaceholder fold:
                        AddInlineAutomationTarget(
                            result,
                            frame,
                            rowIndex,
                            row,
                            visualColumn,
                            fold.DisplayText.Length,
                            fold.FoldId,
                            "fold",
                            DocumentAnchor.Before(fold.HiddenSource.Start),
                            fold.HiddenSource,
                            string.IsNullOrEmpty(fold.DisplayText)
                                ? "Collapsed region"
                                : $"Collapsed region: {fold.DisplayText}",
                            isFold: true,
                            isBlock: false,
                            rowLayout.Height);
                        visualColumn += fold.DisplayText.Length;
                        break;
                    case InlineAdornment adornment:
                        AddInlineAutomationTarget(
                            result,
                            frame,
                            rowIndex,
                            row,
                            visualColumn,
                            adornment.Content.Text.Length,
                            adornment.Id,
                            adornment.Kind,
                            adornment.Anchor,
                            TextRange.Empty(adornment.Anchor.Position.Offset),
                            string.IsNullOrEmpty(adornment.Content.Text)
                                ? adornment.Kind
                                : adornment.Content.Text,
                            isFold: false,
                            isBlock: false,
                            rowLayout.Height);
                        visualColumn += adornment.Content.Text.Length;
                        break;
                    case ProjectedText text:
                        visualColumn += text.Source.Length;
                        break;
                }
            }
        }

        return result;
    }

    private void AddInlineAutomationTarget(
        List<ProjectedTextAutomationTarget> targets,
        ProjectedTextRenderFrame frame,
        int rowIndex,
        VisualRow row,
        int visualStart,
        int visualLength,
        string id,
        string kind,
        DocumentAnchor anchor,
        TextRange range,
        string name,
        bool isFold,
        bool isBlock,
        double rowHeight)
    {
        var visibleStart = Math.Max(visualStart, row.TextStartColumn);
        var visibleEnd = Math.Min(
            visualStart + visualLength,
            row.TextEndColumn);
        if (visibleEnd <= visibleStart)
        {
            return;
        }

        var localStart = visibleStart - row.TextStartColumn;
        var localEnd = visibleEnd - row.TextStartColumn;
        var startX = GetCaretX(rowIndex, localStart, frame.Context.CharacterWidth);
        var endX = GetCaretX(rowIndex, localEnd, frame.Context.CharacterWidth);
        targets.Add(new ProjectedTextAutomationTarget(
            id,
            kind,
            anchor,
            range,
            name,
            isFold,
            isBlock,
            new Rect(
                frame.Context.ContentLeft
                    + startX
                    - frame.Context.HorizontalOffset,
                frame.Context.ContentTop
                    + frame.Layouts[rowIndex].Top
                    - frame.Context.VerticalOffset,
                Math.Max(1, endX - startX),
                rowHeight)));
    }

    public bool TryGetViewportAnchor(
        TextSnapshot snapshot,
        double verticalOffset,
        out DocumentAnchor anchor,
        out double offsetWithinRow)
    {
        anchor = DocumentAnchor.Before(0);
        offsetWithinRow = 0;
        if (_cachedLayout is not { } layout
            || !ReferenceEquals(layout.Snapshot, snapshot)
            || !double.IsFinite(verticalOffset)
            || layout.Rows.Rows.Count == 0)
        {
            return false;
        }

        var documentOffset = Math.Clamp(
            verticalOffset,
            0,
            layout.Heights.TotalHeight);
        var rowIndex = layout.Heights.FindLine(documentOffset);
        var row = layout.Rows.Rows[rowIndex];
        anchor = row.TextLine is { } textLine
            ? textLine.GetAnchor(row.TextStartColumn)
            : row.BlockAdornment!.Anchor;
        offsetWithinRow = documentOffset - layout.Heights.GetOffset(rowIndex);
        return true;
    }

    public bool TryGetViewportOffset(
        DocumentAnchor anchor,
        double offsetWithinRow,
        out double verticalOffset)
    {
        verticalOffset = 0;
        if (_cachedLayout is not { } layout
            || !double.IsFinite(offsetWithinRow)
            || offsetWithinRow < 0)
        {
            return false;
        }

        foreach (var index in layout.Rows.GetBlockRowIndices(anchor))
        {
            var row = layout.Rows.Rows[index];
            if (row.BlockAdornment is not null)
            {
                verticalOffset = layout.Heights.GetOffset(index) + offsetWithinRow;
                return true;
            }
        }

        if (!TryMapDocumentPosition(
                layout.Rows.Projection,
                layout.Snapshot,
                anchor,
                out var position))
        {
            return false;
        }

        var textLine = layout.Rows.Projection.Lines[position.VisualLine];
        foreach (var index in layout.Rows.GetTextRowIndices(textLine))
        {
            var row = layout.Rows.Rows[index];
            if (ContainsCaret(row, position.CaretStop, anchor.Affinity))
            {
                verticalOffset = layout.Heights.GetOffset(index) + offsetWithinRow;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the caret rectangle in the input-control coordinate space. The
    /// row lookup includes block adornment rows, so a completion popup can be
    /// anchored without falling back to the native TextBox's unprojected
    /// geometry.
    /// </summary>
    public bool TryGetCaretRect(
        DocumentAnchor anchor,
        double contentLeft,
        double contentTop,
        double horizontalOffset,
        double verticalOffset,
        double characterWidth,
        double lineHeight,
        out Rect rect)
    {
        rect = default;
        if (_cachedLayout is not { } layout
            || !double.IsFinite(contentLeft)
            || !double.IsFinite(contentTop)
            || !double.IsFinite(horizontalOffset)
            || !double.IsFinite(verticalOffset)
            || !double.IsFinite(characterWidth)
            || !double.IsFinite(lineHeight)
            || characterWidth <= 0
            || lineHeight <= 0)
        {
            return false;
        }

        var projection = layout.Rows.Projection;
        if (!TryMapDocumentPosition(projection, layout.Snapshot, anchor, out var position))
        {
            return false;
        }

        var projectedLine = projection.Lines[position.VisualLine];
        var rowIndex = -1;
        foreach (var index in layout.Rows.GetTextRowIndices(projectedLine))
        {
            var row = layout.Rows.Rows[index];
            if (ContainsCaret(row, position.CaretStop, anchor.Affinity))
            {
                rowIndex = index;
                break;
            }
        }

        if (rowIndex < 0)
        {
            return false;
        }

        var caretRow = layout.Rows.Rows[rowIndex];
        var localCaretStop = position.CaretStop - caretRow.TextStartColumn;
        var caretX = GetCaretXForGlobalRow(
            rowIndex,
            caretRow,
            localCaretStop,
            characterWidth);
        rect = new Rect(
            contentLeft
                + caretX
                - horizontalOffset,
            contentTop + layout.Heights.GetOffset(rowIndex) - verticalOffset,
            characterWidth,
            lineHeight);
        return true;
    }

    public bool TryGetCaretRect(DocumentAnchor anchor, out Rect rect)
    {
        rect = default;
        if (_renderFrame is not { } frame)
        {
            return false;
        }

        return TryGetCaretRect(
            anchor,
            frame.Context.ContentLeft,
            frame.Context.ContentTop,
            frame.Context.HorizontalOffset,
            frame.Context.VerticalOffset,
            frame.Context.CharacterWidth,
            frame.Context.LineHeight,
            out rect);
    }

    public bool TryGetRangeRectangles(
        TextRange range,
        out IReadOnlyList<Rect> rectangles)
    {
        rectangles = Array.Empty<Rect>();
        if (_cachedLayout is not { } layout
            || _renderFrame is not { } frame
            || range.Start < 0
            || range.Start > layout.Snapshot.Length
            || range.End < range.Start
            || range.End > layout.Snapshot.Length)
        {
            return false;
        }

        if (range.IsEmpty)
        {
            if (TryGetCaretRect(
                DocumentAnchor.Before(range.Start),
                frame.Context.ContentLeft,
                frame.Context.ContentTop,
                frame.Context.HorizontalOffset,
                frame.Context.VerticalOffset,
                frame.Context.CharacterWidth,
                frame.Context.LineHeight,
                out var caretRect))
            {
                rectangles = new[] { caretRect };
            }

            return true;
        }

        var projection = layout.Rows.Projection;
        var result = new List<Rect>();
        var viewportStart = frame.Context.VerticalOffset;
        var viewportEnd = viewportStart + frame.Context.ViewportHeight;
        for (var rowIndex = 0; rowIndex < frame.Layouts.Count; rowIndex++)
        {
            var rowLayout = frame.Layouts[rowIndex];
            var rowTop = rowLayout.Top;
            var rowBottom = rowTop + rowLayout.Height;
            if (rowBottom < viewportStart || rowTop > viewportEnd)
            {
                continue;
            }

            var row = rowLayout.Row;
            if (row.TextLine is not { } textLine
                || textLine.SourceRange.End < range.Start
                || textLine.SourceRange.Start > range.End)
            {
                continue;
            }

            var startColumn = row.TextStartColumn;
            if (range.Start > textLine.SourceRange.Start)
            {
                if (!TryMapDocumentPosition(
                        projection,
                        layout.Snapshot,
                        DocumentAnchor.Before(range.Start),
                        out var position))
                {
                    return false;
                }

                if (projection.TryGetVisualLine(
                        textLine.LogicalLine,
                        out var textLineVisualLine)
                    && position.VisualLine == textLineVisualLine)
                {
                    startColumn = Math.Clamp(
                        position.CaretStop,
                        row.TextStartColumn,
                        row.TextEndColumn);
                }
            }

            var endColumn = row.TextEndColumn;
            if (range.End < textLine.SourceRange.End)
            {
                if (!TryMapDocumentPosition(
                        projection,
                        layout.Snapshot,
                        DocumentAnchor.Before(range.End),
                        out var position))
                {
                    return false;
                }

                if (projection.TryGetVisualLine(
                        textLine.LogicalLine,
                        out var textLineVisualLine)
                    && position.VisualLine == textLineVisualLine)
                {
                    endColumn = Math.Clamp(
                        position.CaretStop,
                        row.TextStartColumn,
                        row.TextEndColumn);
                }
            }

            if (endColumn <= startColumn)
            {
                continue;
            }

            var localStart = startColumn - row.TextStartColumn;
            var localEnd = endColumn - row.TextStartColumn;
            var startX = GetCaretX(rowIndex, localStart, frame.Context.CharacterWidth);
            var endX = GetCaretX(rowIndex, localEnd, frame.Context.CharacterWidth);
            result.Add(new Rect(
                frame.Context.ContentLeft + startX - frame.Context.HorizontalOffset,
                frame.Context.ContentTop + rowTop - frame.Context.VerticalOffset,
                Math.Max(1, endX - startX),
                rowLayout.Height));
        }

        rectangles = result;
        return true;
    }

    private static bool TryMapDocumentPosition(
        TextProjection projection,
        TextSnapshot snapshot,
        DocumentAnchor anchor,
        out VisualPosition position)
    {
        position = default;
        var offset = anchor.Position.Offset;
        if (offset < 0 || offset > snapshot.Length)
        {
            return false;
        }

        var logicalLine = snapshot.Lines.GetLine(offset);
        if (!projection.TryGetVisualLine(logicalLine, out var visualLine)
            || visualLine < 0
            || visualLine >= projection.VisualLineCount)
        {
            return false;
        }

        try
        {
            position = projection.MapDocumentPosition(anchor);
            return position.VisualLine >= 0
                && position.VisualLine < projection.VisualLineCount;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private float GetCaretX(int rowIndex, int localStop, double characterWidth)
    {
        if (_renderFrame is { } frame
            && rowIndex >= 0
            && rowIndex < frame.Layouts.Count
            && TryGetTextLayout(frame.Layouts[rowIndex].Row, out var directWriteLayout))
        {
            return directWriteLayout.GetCaretPosition(localStop).X;
        }

        return (float)(localStop * characterWidth);
    }

    private float GetCaretXForGlobalRow(
        int rowIndex,
        VisualRow row,
        int localStop,
        double characterWidth)
    {
        var directWriteLayout = GetTextLayoutForGlobalRow(row);
        return directWriteLayout is not null
            ? directWriteLayout.GetCaretPosition(localStop).X
            : localStop * (float)characterWidth;
    }

    private DirectWriteTextLayout? GetTextLayoutForGlobalRow(
        VisualRow row)
    {
        return TryGetTextLayout(row, out var layout)
            ? layout
            : null;
    }

    private bool TryGetTextLayout(VisualRow row, out DirectWriteTextLayout layout)
    {
        if (TryGetTextLayoutKey(row, out var key)
            && _textLayouts.TryGetValue(key, out var entry))
        {
            layout = entry.Layout;
            return true;
        }

        layout = null!;
        return false;
    }

    private UnwrappedLineLayout? GetLineLayoutForGlobalRow(
        int rowIndex,
        VisualRow row)
    {
        if (_renderFrame is not { } frame)
        {
            return null;
        }

        return frame.Layouts
            .FirstOrDefault(layout => ReferenceEquals(layout.Row, row))
            ?.TextLayout;
    }

    /// <summary>
    /// Maps a pointer in the projected surface back to a document anchor. The
    /// result is based on the same visual-row cache used by the render pass,
    /// so folds and variable-height block adornments cannot drift from what
    /// the user sees.
    /// </summary>
    public bool TryHitTest(
        double x,
        double y,
        double contentLeft,
        double contentTop,
        double horizontalOffset,
        double verticalOffset,
        double characterWidth,
        out DocumentAnchor anchor,
        out string? foldId) => TryHitTest(
            x,
            y,
            contentLeft,
            contentTop,
            horizontalOffset,
            verticalOffset,
            characterWidth,
            out anchor,
            out foldId,
            out _,
            out _);

    public bool TryHitTest(
        double x,
        double y,
        double contentLeft,
        double contentTop,
        double horizontalOffset,
        double verticalOffset,
        double characterWidth,
        out DocumentAnchor anchor,
        out string? foldId,
        out string? adornmentId) => TryHitTest(
            x,
            y,
            contentLeft,
            contentTop,
            horizontalOffset,
            verticalOffset,
            characterWidth,
            out anchor,
            out foldId,
            out adornmentId,
            out _);

    public bool TryHitTest(
        double x,
        double y,
        double contentLeft,
        double contentTop,
        double horizontalOffset,
        double verticalOffset,
        double characterWidth,
        out DocumentAnchor anchor,
        out string? foldId,
        out string? adornmentId,
        out TextBlockPosition blockPosition) => TryHitTest(
            x,
            y,
            contentLeft,
            contentTop,
            horizontalOffset,
            verticalOffset,
            characterWidth,
            out anchor,
            out foldId,
            out adornmentId,
            out blockPosition,
            out _);

    public bool TryHitTest(
        double x,
        double y,
        double contentLeft,
        double contentTop,
        double horizontalOffset,
        double verticalOffset,
        double characterWidth,
        out DocumentAnchor anchor,
        out string? foldId,
        out string? adornmentId,
        out TextBlockPosition blockPosition,
        out int? textPosition)
    {
        anchor = DocumentAnchor.Before(0);
        foldId = null;
        adornmentId = null;
        blockPosition = new TextBlockPosition(0, 0);
        textPosition = null;
        if (_cachedLayout is not { } layout
            || !double.IsFinite(x)
            || !double.IsFinite(y)
            || !double.IsFinite(contentLeft)
            || !double.IsFinite(contentTop)
            || !double.IsFinite(horizontalOffset)
            || !double.IsFinite(verticalOffset)
            || !double.IsFinite(characterWidth)
            || characterWidth <= 0
            || layout.Rows.Rows.Count == 0)
        {
            return false;
        }

        // Treat the empty area above or below the document as the nearest
        // document edge. This lets pointer selection start and continue from
        // outside the range of realized text rows.
        var documentY = Math.Clamp(
            y + verticalOffset - contentTop,
            0,
            layout.Heights.TotalHeight);

        var rowIndex = layout.Heights.FindLine(documentY);
        var row = layout.Rows.Rows[rowIndex];
        if (row.TextLine is not { } textLine)
        {
            anchor = row.BlockAdornment!.Anchor;
            blockPosition = new TextBlockPosition(
                layout.Snapshot.Lines.GetLine(anchor.Position.Offset),
                0);
            adornmentId = row.BlockAdornment.Id;
            return true;
        }

        // DirectWrite reports caret positions in pixels, so keep the pointer
        // position in the same coordinate space while choosing a caret stop.
        // The fixed-width fallback performs the conversion using
        // characterWidth below.
        var xInTextPixels = x + horizontalOffset - contentLeft;
        blockPosition = new TextBlockPosition(
            layout.WrapColumns > 0 ? row.VisualRowIndex : textLine.LogicalLine,
            GetDisplayColumn(xInTextPixels, characterWidth));
        var localColumn = GetNearestCaretStop(
            rowIndex,
            row,
            xInTextPixels,
            characterWidth);
        var visualColumn = row.TextStartColumn + localColumn;
        anchor = GetLineLayoutForGlobalRow(rowIndex, row)
            ?.GetDocumentAnchorAtCaretStop(localColumn)
            ?? textLine.GetAnchor(visualColumn);

        if (GetTextElementStartAtPoint(
                row,
                xInTextPixels,
                characterWidth)
            is int hitLocalColumn)
        {
            var hitVisualColumn = row.TextStartColumn + hitLocalColumn;
            var hitColumn = 0;
            foreach (var inline in textLine.Inlines)
            {
                switch (inline)
                {
                    case FoldPlaceholder fold:
                        hitColumn += fold.DisplayText.Length;
                        break;
                    case ProjectedText text:
                        if (hitVisualColumn >= hitColumn
                            && hitVisualColumn < hitColumn + text.Source.Length)
                        {
                            textPosition = text.Source.Start
                                + hitVisualColumn
                                - hitColumn;
                        }

                        hitColumn += text.Source.Length;
                        break;
                    case InlineAdornment adornment:
                        hitColumn += adornment.Content.Text.Length;
                        break;
                }
            }
        }

        var column = 0;
        foreach (var inline in textLine.Inlines)
        {
            switch (inline)
            {
                case FoldPlaceholder fold:
                    {
                        var end = column + fold.DisplayText.Length;
                        if (visualColumn >= column && visualColumn < end)
                        {
                            foldId = fold.FoldId;
                            return true;
                        }

                        column = end;
                        break;
                    }
                case ProjectedText text:
                    column += text.Source.Length;
                    break;
                case InlineAdornment adornment:
                    if (visualColumn >= column
                        && visualColumn < column + adornment.Content.Text.Length)
                    {
                        adornmentId = adornment.Id;
                        return true;
                    }

                    column += adornment.Content.Text.Length;
                    break;
            }
        }

        return true;
    }

    private int? GetTextElementStartAtPoint(
        VisualRow row,
        double x,
        double characterWidth)
    {
        if (!double.IsFinite(x) || x < 0)
        {
            return null;
        }

        if (GetTextLayoutForGlobalRow(row) is { } textLayout)
        {
            var stops = textLayout.GetGraphemeCaretStops();
            for (var index = 0; index + 1 < stops.Count; index++)
            {
                var start = textLayout.GetCaretPosition(stops[index]).X;
                var end = textLayout.GetCaretPosition(stops[index + 1]).X;
                if (x >= start && x < end)
                {
                    return stops[index];
                }
            }

            return null;
        }

        if (x >= row.TextLength * characterWidth)
        {
            return null;
        }

        return Math.Clamp(
            (int)Math.Floor(x / characterWidth),
            0,
            Math.Max(0, row.TextLength - 1));
    }

    internal bool TryGetFoldIdAtBodyPoint(
        double x,
        double y,
        double contentLeft,
        double contentTop,
        double horizontalOffset,
        double verticalOffset,
        double characterWidth,
        out string foldId)
    {
        foldId = string.Empty;
        if (_cachedLayout is not { } layout
            || _renderFrame is not { } frame
            || !double.IsFinite(x)
            || !double.IsFinite(y)
            || !double.IsFinite(contentLeft)
            || !double.IsFinite(contentTop)
            || !double.IsFinite(horizontalOffset)
            || !double.IsFinite(verticalOffset)
            || !double.IsFinite(characterWidth)
            || characterWidth <= 0
            || layout.Rows.Rows.Count == 0
            || frame.Context.Folds is not { Count: > 0 } folds)
        {
            return false;
        }

        var documentY = Math.Clamp(
            y + verticalOffset - contentTop,
            0,
            layout.Heights.TotalHeight);
        var rowIndex = layout.Heights.FindLine(documentY);
        var row = layout.Rows.Rows[rowIndex];
        if (row.TextLine is not { } textLine)
        {
            return false;
        }

        var xInTextPixels = x + horizontalOffset - contentLeft;
        if (xInTextPixels < 0)
        {
            return false;
        }

        var textWidth = GetTextLayoutForGlobalRow(row)?.Width
            ?? row.TextLength * characterWidth;
        if (xInTextPixels > textWidth)
        {
            return false;
        }

        foreach (var fold in folds)
        {
            if (frame.Context.Snapshot.Lines.GetLine(fold.Range.Start)
                == textLine.LogicalLine)
            {
                foldId = fold.Id;
                return true;
            }
        }

        return false;
    }

    internal bool TryNavigateCarets(
        TextCaretSet carets,
        AzunyanEditorNavigationKind kind,
        bool extendSelection,
        int tabDisplaySize,
        out TextCaretSet result)
    {
        ArgumentNullException.ThrowIfNull(carets);
        result = carets;
        if (_cachedLayout is not { } layout
            || _renderFrame is not { } frame
            || !ReferenceEquals(layout.Snapshot, frame.Context.Snapshot))
        {
            return false;
        }

        var states = new List<TextCaretState>(carets.Count);
        foreach (var caret in carets)
        {
            var direction = kind is AzunyanEditorNavigationKind.Up
                or AzunyanEditorNavigationKind.PageUp
                or AzunyanEditorNavigationKind.SmartHome
                    ? -1
                    : 1;
            var sourceAnchor = GetNavigationSource(caret, direction, extendSelection);
            if (!TryGetCaretRow(layout, sourceAnchor, out var sourceRowIndex, out var sourceStop))
            {
                states.Add(caret);
                continue;
            }

            DocumentAnchor targetAnchor;
            double? preferredHorizontalOffset = null;
            switch (kind)
            {
                case AzunyanEditorNavigationKind.Up:
                case AzunyanEditorNavigationKind.Down:
                    {
                        var targetRowIndex = FindAdjacentTextRow(
                            layout.Rows,
                            sourceRowIndex,
                            direction);
                        preferredHorizontalOffset = caret.PreferredHorizontalOffset
                            ?? GetCaretXForGlobalRow(
                                sourceRowIndex,
                                layout.Rows.Rows[sourceRowIndex],
                                sourceStop - layout.Rows.Rows[sourceRowIndex].TextStartColumn,
                                frame.Context.CharacterWidth);
                        targetAnchor = GetAnchorAtHorizontalOffset(
                            targetRowIndex,
                            preferredHorizontalOffset.Value,
                            frame.Context.CharacterWidth);
                        break;
                    }
                case AzunyanEditorNavigationKind.PageUp:
                case AzunyanEditorNavigationKind.PageDown:
                    {
                        var pageDistance = Math.Max(
                            frame.Context.LineHeight,
                            frame.Context.ViewportHeight - frame.Context.LineHeight);
                        var sourceTop = layout.Heights.GetOffset(sourceRowIndex);
                        var targetOffset = Math.Clamp(
                            sourceTop + (direction * pageDistance),
                            0,
                            layout.Heights.TotalHeight);
                        var targetRowIndex = FindTextRowNear(
                            layout.Rows,
                            layout.Heights.FindLine(targetOffset),
                            direction);
                        preferredHorizontalOffset = caret.PreferredHorizontalOffset
                            ?? GetCaretXForGlobalRow(
                                sourceRowIndex,
                                layout.Rows.Rows[sourceRowIndex],
                                sourceStop - layout.Rows.Rows[sourceRowIndex].TextStartColumn,
                                frame.Context.CharacterWidth);
                        targetAnchor = GetAnchorAtHorizontalOffset(
                            targetRowIndex,
                            preferredHorizontalOffset.Value,
                            frame.Context.CharacterWidth);
                        break;
                    }
                case AzunyanEditorNavigationKind.SmartHome:
                    targetAnchor = GetSmartHomeAnchor(
                        layout.Snapshot,
                        layout.Rows.Rows[sourceRowIndex],
                        sourceAnchor);
                    break;
                case AzunyanEditorNavigationKind.End:
                    targetAnchor = GetRowBoundaryAnchor(
                        layout.Rows.Rows[sourceRowIndex],
                        end: true);
                    break;
                default:
                    states.Add(caret);
                    continue;
            }

            var target = targetAnchor.Position.Offset;
            var selection = extendSelection
                ? new TextSelection(caret.Selection.Anchor, target)
                : TextSelection.Caret(target);
            states.Add(TextCaretState.CreateNavigation(
                selection,
                TextBlockSelectionOperations.GetDisplayColumn(
                    layout.Snapshot,
                    target,
                    tabDisplaySize),
                targetAnchor,
                preferredHorizontalOffset));
        }

        result = new TextCaretSet(states, carets.PrimaryIndex);
        return true;
    }

    private static bool TryGetCaretRow(
        ProjectedTextLayoutState layout,
        DocumentAnchor anchor,
        out int rowIndex,
        out int caretStop)
    {
        rowIndex = -1;
        caretStop = 0;
        if (!TryMapDocumentPosition(layout.Rows.Projection, layout.Snapshot, anchor, out var position))
        {
            return false;
        }

        var line = layout.Rows.Projection.Lines[position.VisualLine];
        foreach (var index in layout.Rows.GetTextRowIndices(line))
        {
            if (ContainsCaret(layout.Rows.Rows[index], position.CaretStop, anchor.Affinity))
            {
                rowIndex = index;
                caretStop = position.CaretStop;
                return true;
            }
        }

        return false;
    }

    private DocumentAnchor GetAnchorAtHorizontalOffset(
        int rowIndex,
        double horizontalOffset,
        double characterWidth)
    {
        var row = _cachedLayout!.Rows.Rows[rowIndex];
        var localStop = GetNearestCaretStop(
            rowIndex,
            row,
            horizontalOffset,
            characterWidth);
        return GetLineLayoutForGlobalRow(rowIndex, row)
            ?.GetDocumentAnchorAtCaretStop(localStop)
            ?? row.TextLine!.GetAnchor(row.TextStartColumn + localStop);
    }

    private static DocumentAnchor GetNavigationSource(
        TextCaretState caret,
        int direction,
        bool extendSelection)
    {
        if (extendSelection || caret.Selection.IsEmpty)
        {
            return caret.CaretAnchor;
        }

        return direction < 0
            ? DocumentAnchor.Before(caret.Selection.Start)
            : DocumentAnchor.After(caret.Selection.End);
    }

    private static int FindAdjacentTextRow(
        VisualRowMap rows,
        int sourceRowIndex,
        int direction)
    {
        var index = sourceRowIndex;
        while (index + direction >= 0 && index + direction < rows.Rows.Count)
        {
            index += direction;
            if (rows.Rows[index].Kind == VisualRowKind.Text)
            {
                return index;
            }
        }

        return sourceRowIndex;
    }

    private static int FindTextRowNear(
        VisualRowMap rows,
        int candidate,
        int direction)
    {
        candidate = Math.Clamp(candidate, 0, rows.Rows.Count - 1);
        if (rows.Rows[candidate].Kind == VisualRowKind.Text)
        {
            return candidate;
        }

        var forward = candidate;
        while (forward >= 0 && forward < rows.Rows.Count)
        {
            if (rows.Rows[forward].Kind == VisualRowKind.Text)
            {
                return forward;
            }

            forward += direction;
        }

        return FindAdjacentTextRow(rows, candidate, -direction);
    }

    private static DocumentAnchor GetSmartHomeAnchor(
        TextSnapshot snapshot,
        VisualRow row,
        DocumentAnchor sourceAnchor)
    {
        var projectedText = string.Concat(row.TextLine!.Inlines.Select(inline =>
            GetProjectedInlineText(snapshot, inline)));
        var rowText = projectedText.Substring(row.TextStartColumn, row.TextLength);
        var firstNonWhitespace = 0;
        while (firstNonWhitespace < rowText.Length
            && char.IsWhiteSpace(rowText[firstNonWhitespace]))
        {
            firstNonWhitespace++;
        }

        if (firstNonWhitespace == rowText.Length)
        {
            firstNonWhitespace = 0;
        }

        var rowStart = GetRowBoundaryAnchor(row, end: false);
        var indentationEnd = row.TextLine.GetAnchor(
            row.TextStartColumn + firstNonWhitespace);
        return sourceAnchor.Position.Offset == indentationEnd.Position.Offset
                ? rowStart
                : indentationEnd;
    }

    private static DocumentAnchor GetRowBoundaryAnchor(VisualRow row, bool end)
    {
        var column = end ? row.TextEndColumn : row.TextStartColumn;
        var anchor = row.TextLine!.GetAnchor(column);
        if (row.TextStartColumn > 0 && !end)
        {
            return new DocumentAnchor(anchor.Position, AnchorAffinity.Before);
        }

        if (row.TextEndColumn < row.TextLine.VisualLength && end)
        {
            return new DocumentAnchor(anchor.Position, AnchorAffinity.After);
        }

        return anchor;
    }

    internal bool TryCreateVisualBlockSelectionCaretSet(
        TextBlockSelection selection,
        int tabDisplaySize,
        out TextCaretSet caretSet)
    {
        caretSet = null!;
        if (selection.CoordinateSpace != TextBlockSelectionCoordinateSpace.VisualRows
            || _cachedLayout is not { } layout
            || layout.WrapColumns <= 0
            || !ReferenceEquals(layout.Snapshot, _renderFrame?.Context.Snapshot))
        {
            return false;
        }

        caretSet = TextCaretSetOperations.FromVisualBlockSelection(
            layout.Snapshot,
            selection,
            layout.Rows,
            tabDisplaySize);
        return true;
    }

    private static int GetDisplayColumn(double x, double characterWidth)
    {
        if (x <= 0)
        {
            return 0;
        }

        var column = x / characterWidth;
        return column >= int.MaxValue
            ? int.MaxValue
            : Math.Max(0, (int)Math.Round(column, MidpointRounding.AwayFromZero));
    }

    private int GetNearestCaretStop(
        int rowIndex,
        VisualRow row,
        double x,
        double characterWidth)
    {
        var directWriteLayout = GetTextLayoutForGlobalRow(row);
        if (directWriteLayout is null)
        {
            return Math.Clamp(
                (int)Math.Round(x / characterWidth, MidpointRounding.AwayFromZero),
                0,
                row.TextLength);
        }

        var nearest = 0;
        var nearestDistance = double.PositiveInfinity;
        foreach (var stop in directWriteLayout.GetGraphemeCaretStops())
        {
            var caretX = directWriteLayout.GetCaretPosition(stop).X;
            var distance = Math.Abs(x - caretX);
            if (distance < nearestDistance)
            {
                nearest = stop;
                nearestDistance = distance;
            }
        }

        return Math.Clamp(nearest, 0, row.TextLength);
    }

    public void Render(AzunyanEditorRenderContext context)
    {
        if (_disposed)
        {
            return;
        }

        var traceLayout = context.DiagnosticSink is not null;
        var layoutStarted = traceLayout ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        var wrapWidth = GetWrapWidth(context);
        var wrapColumns = GetWrapColumns(context);
        var layoutState = GetLayoutState(context, wrapColumns, wrapWidth);
        var stateElapsed = traceLayout
            ? System.Diagnostics.Stopwatch.GetElapsedTime(layoutStarted)
            : TimeSpan.Zero;
        var metrics = new LayoutMetrics(
            context.CharacterWidth,
            context.LineHeight,
            Math.Min(context.LineHeight * 0.8, context.LineHeight));
        EnsureTextLayoutCache(context);
        var layouts = ViewportLayoutEngine.LayoutVisibleRows(
            context.Snapshot,
            layoutState.Rows,
            layoutState.Heights,
            new LayoutViewport(context.VerticalOffset, context.ViewportHeight),
            overscan: context.LineHeight,
            context.DocumentResults?.Syntax ?? Array.Empty<SyntaxSpan>(),
            metrics,
            _lineLayoutEngine,
            _lineLayouts);
        var invalidateGutter = _renderFrame is null
            || !GutterMatches(_renderFrame, context, layouts);
        if (traceLayout)
        {
            context.DiagnosticSink!(
                $"renderer-layout input={context.DiagnosticInputSequence}; "
                + $"stateMs={stateElapsed.TotalMilliseconds:F3}; "
                + $"visibleMs={System.Diagnostics.Stopwatch.GetElapsedTime(layoutStarted).TotalMilliseconds - stateElapsed.TotalMilliseconds:F3}; "
                + $"rows={layouts.Count}; lineCache={_lineLayouts.Count}; "
                + $"gutterInvalidated={invalidateGutter}");
        }

        _renderFrame = new ProjectedTextRenderFrame(context, layouts);
        PruneLayoutCaches(layouts);
        if (_pendingDocumentChange is { } change
            && ReferenceEquals(change.NewSnapshot, context.Snapshot))
        {
            _pendingDocumentChange = null;
        }
        RenderFoldChevrons(context, layoutState, layouts);
        if (invalidateGutter)
        {
            _gutterSurface.Invalidate();
        }
        _textSurface.Invalidate();
    }

    private static bool GutterMatches(
        ProjectedTextRenderFrame previous,
        AzunyanEditorRenderContext context,
        IReadOnlyList<ViewportRowLayout> layouts)
    {
        var previousContext = previous.Context;
        var previousGutter = previousContext.ViewportResults?.Gutter;
        var currentGutter = context.ViewportResults?.Gutter;
        if (!Equals(previousContext.ColorScheme, context.ColorScheme)
            || !string.Equals(
                previousContext.FontFamily.Source,
                context.FontFamily.Source,
                StringComparison.Ordinal)
            || previousContext.FontSize != context.FontSize
            || previousContext.LineHeight != context.LineHeight
            || previousContext.GutterWidth != context.GutterWidth
            || previousContext.ViewportHeight != context.ViewportHeight
            || previousContext.ContentTop != context.ContentTop
            || previousContext.VerticalOffset != context.VerticalOffset
            || previousContext.ShowLineNumbers != context.ShowLineNumbers
            || !ReferenceEquals(previousGutter, currentGutter)
                && (previousGutter is null
                    || currentGutter is null
                    || !previousGutter.SequenceEqual(currentGutter))
            || previous.Layouts.Count != layouts.Count)
        {
            return false;
        }

        for (var index = 0; index < layouts.Count; index++)
        {
            var oldLayout = previous.Layouts[index];
            var newLayout = layouts[index];
            if (oldLayout.Top != newLayout.Top
                || oldLayout.Row.Kind != newLayout.Row.Kind
                || oldLayout.Row.IsContinuation != newLayout.Row.IsContinuation
                || oldLayout.Row.LogicalLine != newLayout.Row.LogicalLine)
            {
                return false;
            }
        }

        return true;
    }

    private void RenderFoldChevrons(
        AzunyanEditorRenderContext context,
        ProjectedTextLayoutState layoutState,
        IReadOnlyList<ViewportRowLayout> layouts)
    {
        if (context.ToggleFold is null
            || layoutState.FoldsByLogicalLine.Count == 0
            || context.GutterWidth < FoldChevronWidth)
        {
            ClearFoldChevrons(context.GutterLayer);
            return;
        }

        var visible = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rowLayout in layouts)
        {
            var row = rowLayout.Row;
            if (row.Kind != VisualRowKind.Text || row.IsContinuation)
            {
                continue;
            }

            if (!layoutState.FoldsByLogicalLine.TryGetValue(row.LogicalLine, out var folds))
            {
                continue;
            }

            foreach (var fold in folds)
            {
                var collapsed = context.CollapsedFoldIds.Contains(fold.Id);
                if (!_foldButtons.TryGetValue(fold.Id, out var button))
                {
                    button = new Button
                    {
                        Content = new FontIcon { FontSize = FoldChevronFontSize },
                        Width = FoldChevronWidth,
                        Padding = new Thickness(0),
                        Margin = new Thickness(0),
                        BorderThickness = new Thickness(0),
                        BorderBrush = new SolidColorBrush(Colors.Transparent),
                        Background = new SolidColorBrush(Colors.Transparent),
                        HorizontalContentAlignment = HorizontalAlignment.Center,
                        VerticalContentAlignment = VerticalAlignment.Center,
                        IsTabStop = true,
                        Tag = fold.Id
                    };
                    button.Click += OnFoldChevronClick;
                    _foldButtons.Add(fold.Id, button);
                    context.GutterLayer.Children.Add(button);
                }

                ((FontIcon)button.Content).Glyph = collapsed ? "\uE76C" : "\uE70D";
                button.Height = context.LineHeight;
                if (button.Foreground is not SolidColorBrush foreground
                    || foreground.Color != context.ColorScheme.GutterForeground)
                {
                    button.Foreground = new SolidColorBrush(context.ColorScheme.GutterForeground);
                }
                AutomationProperties.SetName(
                    button,
                    collapsed ? "Expand section" : "Collapse section");
                AutomationProperties.SetAutomationId(
                    button,
                    $"azunyan-fold-{fold.Id}");
                AutomationProperties.SetHelpText(button, fold.Id);
                ToolTipService.SetToolTip(
                    button,
                    collapsed ? "Expand section" : "Collapse section");

                Canvas.SetLeft(button, 0);
                Canvas.SetTop(
                    button,
                    context.ContentTop + rowLayout.Top - context.VerticalOffset);
                visible.Add(fold.Id);
            }
        }

        foreach (var pair in _foldButtons.Where(pair => !visible.Contains(pair.Key)).ToArray())
        {
            context.GutterLayer.Children.Remove(pair.Value);
            _foldButtons.Remove(pair.Key);
        }
    }

    private void OnFoldChevronClick(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { Tag: string foldId }
            && _renderFrame?.Context.ToggleFold is { } toggleFold)
        {
            toggleFold(foldId);
        }
    }

    private void ClearFoldChevrons(Canvas gutterLayer)
    {
        foreach (var button in _foldButtons.Values)
        {
            gutterLayer.Children.Remove(button);
        }

        _foldButtons.Clear();
    }

    private void OnCreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args)
    {
        if (_disposed)
        {
            return;
        }

        ClearTextLayouts();
        _textLayoutCacheKey = null;
        if (_renderFrame?.Context.TextWrapping == TextWrapping.Wrap)
        {
            LayoutInvalidated?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ClearTextLayouts()
    {
        foreach (var entry in _textLayouts.Values)
        {
            entry.Layout.Dispose();
        }

        foreach (var gutterLayout in _gutterLayouts.Values)
        {
            gutterLayout.Layout.Dispose();
        }

        _gutterLayouts.Clear();
        _textLayouts.Clear();
    }

    private void EnsureTextLayoutCache(AzunyanEditorRenderContext context)
    {
        var syntax = context.DocumentResults?.Syntax;
        if (!ReferenceEquals(_lineLayoutSyntax, syntax))
        {
            if (_pendingDocumentChange is not { } change
                || !ReferenceEquals(change.NewSnapshot, context.Snapshot))
            {
                _lineLayouts.Clear();
            }
            _lineLayoutSyntax = syntax;
        }

        var key = new TextLayoutCacheKey(
            context.ColorScheme,
            context.FontFamily.Source,
            context.FontSize,
            context.CharacterWidth,
            context.LineHeight,
            context.GutterWidth,
            context.TabDisplaySize,
            context.TextWrapping);
        if (_textLayoutCacheKey is null || !_textLayoutCacheKey.Matches(key))
        {
            ClearTextLayouts();
            _lineLayouts.Clear();
            _textLayoutCacheKey = key;
        }
    }

    private void PruneLayoutCaches(IReadOnlyList<ViewportRowLayout> layouts)
    {
        var textLayoutKeys = layouts
            .Select(layout => TryGetTextLayoutKey(layout.Row, out var key)
                ? key
                : (TextLayoutRowKey?)null)
            .Where(key => key is not null)
            .Select(key => key!.Value)
            .ToHashSet();
        var rows = layouts.Select(layout => layout.Row).ToHashSet();
        var textLines = layouts
            .Select(layout => layout.Row.TextLine)
            .Where(line => line is not null)
            .Cast<ProjectedLine>()
            .ToHashSet();
        foreach (var line in _lineLayouts.Keys.Where(line => !textLines.Contains(line)).ToArray())
        {
            _lineLayouts.Remove(line);
        }

        foreach (var key in _textLayouts.Keys.Where(key => !textLayoutKeys.Contains(key)).ToArray())
        {
            _textLayouts[key].Layout.Dispose();
            _textLayouts.Remove(key);
        }

        foreach (var row in _gutterLayouts.Keys.Where(row => !rows.Contains(row)).ToArray())
        {
            _gutterLayouts[row].Layout.Dispose();
            _gutterLayouts.Remove(row);
        }
    }

    private void OnGutterDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (_renderFrame is not { } frame)
        {
            return;
        }

        var colors = frame.Context.ColorScheme;
        args.DrawingSession.Clear(colors.GutterBackground);

        for (var index = 0; index < frame.Layouts.Count; index++)
        {
            var rowLayout = frame.Layouts[index];
            var row = rowLayout.Row;
            if (row.Kind != VisualRowKind.Text || row.IsContinuation)
            {
                continue;
            }

            var text = GetGutterText(frame.Context, row);
            if (text is null)
            {
                continue;
            }

            if (!_gutterLayouts.TryGetValue(row, out var gutterLayout)
                || !string.Equals(gutterLayout.Text, text, StringComparison.Ordinal))
            {
                gutterLayout?.Layout.Dispose();
                var layout = DirectWriteTextLayout.Create(
                    args.DrawingSession,
                    new[] { new DirectWriteTextRun(text, colors.GutterForeground) },
                    frame.Context.FontFamily.Source,
                    (float)frame.Context.FontSize,
                    Math.Max(1, (float)frame.Context.GutterWidth),
                    (float)frame.Context.LineHeight,
                    (float)frame.Context.LineHeight,
                    Math.Min((float)frame.Context.LineHeight * 0.8f, (float)frame.Context.LineHeight));
                gutterLayout = new GutterLayoutEntry(text, layout);
                _gutterLayouts[row] = gutterLayout;
            }

            var top = frame.Context.ContentTop
                + rowLayout.Top
                - frame.Context.VerticalOffset;
            gutterLayout.Layout.Draw(
                args.DrawingSession,
                (float)(frame.Context.GutterWidth - 8 - gutterLayout.Layout.Width),
                (float)top,
                colors.GutterForeground);
        }
    }

    private static string? GetGutterText(
        AzunyanEditorRenderContext context,
        VisualRow row)
    {
        var providerItem = context.ViewportResults?.Gutter
            .FirstOrDefault(item => item.Line == row.LogicalLine);
        if (providerItem is not null)
        {
            return providerItem.Text;
        }

        return context.ShowLineNumbers
            ? (row.LogicalLine + 1).ToString(CultureInfo.InvariantCulture)
            : null;
    }

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (_renderFrame is not { } frame)
        {
            return;
        }

        var drawStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        var creates = _textLayoutCreates;
        var hits = _textLayoutHits;
        args.DrawingSession.Clear(frame.Context.ColorScheme.EditorBackground);

        for (var index = 0; index < frame.Layouts.Count; index++)
        {
            var rowLayout = frame.Layouts[index];
            var top = frame.Context.ContentTop
                + rowLayout.Top
                - frame.Context.VerticalOffset;
            if (rowLayout.TextLayout is { } layout)
            {
                DrawTextRow(args.DrawingSession, frame.Context, layout, top, rowLayout.Row);
            }
            else if (rowLayout.Row.BlockAdornment is { } block)
            {
                DrawBlock(args.DrawingSession, frame.Context, block, rowLayout.Height, top);
            }
        }

        if (frame.Context.DiagnosticSink is { } diagnosticSink)
        {
            var inputElapsed = frame.Context.DiagnosticInputStarted == 0
                ? 0
                : System.Diagnostics.Stopwatch.GetElapsedTime(
                    frame.Context.DiagnosticInputStarted).TotalMilliseconds;
            diagnosticSink(
                $"canvas-text-draw input={frame.Context.DiagnosticInputSequence}; "
                + $"inputElapsedMs={inputElapsed:F3}; rows={frame.Layouts.Count}; "
                + $"queueMs={System.Diagnostics.Stopwatch.GetElapsedTime(frame.PreparedAt, drawStarted).TotalMilliseconds:F3}; "
                + $"drawMs={System.Diagnostics.Stopwatch.GetElapsedTime(drawStarted).TotalMilliseconds:F3}; "
                + $"layoutCreates={_textLayoutCreates - creates}; layoutHits={_textLayoutHits - hits}");
        }
    }

    private ProjectedTextLayoutState GetLayoutState(
        AzunyanEditorRenderContext context,
        int wrapColumns,
        double wrapWidth)
    {
        var documentFolds = context.Folds;
        var inlays = context.ViewportResults?.Inlays;
        var blocks = context.ViewportResults?.BlockAdornments;
        var collapsedFoldIdSet = context.CollapsedFoldIds;
        var collapsedFoldIds = context.CollapsedFoldIds
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        var cachedLayout = _cachedLayout;
        if (_cachedLayout is { } cached
            && cached.Matches(
                context.Snapshot,
                context.LineHeight,
                documentFolds,
                inlays,
                blocks,
                collapsedFoldIds,
                wrapColumns,
                wrapWidth,
                context.TabDisplaySize))
        {
            if (context.TextWrapping != TextWrapping.Wrap
                || !_textSurface.ReadyToDraw)
            {
                return cached;
            }

            var measuredBreaks = MeasureVisibleWrapBreaks(context, cached, wrapWidth);
            return WrapBreaksEqual(measuredBreaks, cached.WrapBreaks)
                ? cached
                : BuildLayoutState(
                    context,
                    documentFolds,
                    inlays,
                    blocks,
                    collapsedFoldIds,
                    wrapColumns,
                    wrapWidth,
                    cached.Rows.Projection,
                    measuredBreaks,
                    cached);
        }

        var folds = documentFolds is null
            ? Array.Empty<FoldRange>()
            : documentFolds
                .Where(fold => collapsedFoldIdSet.Contains(fold.Id))
                .ToArray();
        var projection = TryBuildIncrementalProjection(
            context,
            cached: cachedLayout,
            inlays,
            folds);
        var layout = BuildLayoutState(
            context,
            documentFolds,
            inlays,
            blocks,
            collapsedFoldIds,
            wrapColumns,
            wrapWidth,
            projection,
            new Dictionary<int, IReadOnlyList<int>>(),
            cachedLayout);
        if (context.TextWrapping != TextWrapping.Wrap
            || !_textSurface.ReadyToDraw)
        {
            return layout;
        }

        var measured = MeasureVisibleWrapBreaks(context, layout, wrapWidth);
        return WrapBreaksEqual(measured, layout.WrapBreaks)
            ? layout
            : BuildLayoutState(
                context,
                documentFolds,
                inlays,
                blocks,
                collapsedFoldIds,
                wrapColumns,
                wrapWidth,
                projection,
                measured,
                cachedLayout);
    }

    private TextProjection TryBuildIncrementalProjection(
        AzunyanEditorRenderContext context,
        ProjectedTextLayoutState? cached,
        IReadOnlyList<InlineAdornment>? inlays,
        FoldRange[] folds)
    {
        if (cached is not null
            && ReferenceEquals(cached.Snapshot, context.Snapshot))
        {
            return TextProjectionBuilder.BuildIncremental(
                context.Snapshot,
                cached.Rows.Projection,
                folds,
                inlays ?? Array.Empty<InlineAdornment>());
        }

        if (cached is not null
            && _pendingDocumentChange is { } change
            && ReferenceEquals(change.OldSnapshot, cached.Snapshot)
            && ReferenceEquals(change.NewSnapshot, context.Snapshot)
            )
        {
            return TextProjectionBuilder.BuildIncremental(
                change.OldSnapshot,
                context.Snapshot,
                cached.Rows.Projection,
                change.Change,
                folds,
                inlays ?? Array.Empty<InlineAdornment>());
        }

        return TextProjectionBuilder.Build(
            context.Snapshot,
            folds,
            inlays ?? Array.Empty<InlineAdornment>());
    }

    private ProjectedTextLayoutState BuildLayoutState(
        AzunyanEditorRenderContext context,
        IReadOnlyList<FoldRange>? documentFolds,
        IReadOnlyList<InlineAdornment>? inlays,
        IReadOnlyList<BlockAdornment>? blocks,
        IReadOnlyList<string> collapsedFoldIds,
        int wrapColumns,
        double wrapWidth,
        TextProjection projection,
        IReadOnlyDictionary<int, IReadOnlyList<int>> measuredBreaks,
        ProjectedTextLayoutState? previousLayout)
    {
        var currentBlocks = blocks ?? Array.Empty<BlockAdornment>();
        var pendingChange = _pendingDocumentChange;
        var canBuildIncrementally = previousLayout is not null
            && (ReferenceEquals(previousLayout.Snapshot, context.Snapshot)
                    && previousLayout.LineHeight == context.LineHeight
                    && previousLayout.WrapColumns == wrapColumns
                    && previousLayout.WrapWidth == wrapWidth
                    && previousLayout.TabDisplaySize == context.TabDisplaySize
                || pendingChange is { } change
                    && ReferenceEquals(change.OldSnapshot, previousLayout.Snapshot)
                    && ReferenceEquals(change.NewSnapshot, context.Snapshot));
        var rows = canBuildIncrementally
            ? VisualRowMapBuilder.BuildIncremental(
                previousLayout!.Rows.Projection,
                projection,
                previousLayout.Rows,
                currentBlocks,
                wrapColumns,
                measuredBreaks,
                pendingChange?.Change)
            : VisualRowMapBuilder.Build(
                projection,
                currentBlocks,
                wrapColumns,
                wrappedLineBreaksByVisualLine: measuredBreaks);
        var heights = BuildHeightIndex(context, rows, previousLayout);
        var layout = new ProjectedTextLayoutState(
            context.Snapshot,
            context.LineHeight,
            documentFolds,
            inlays,
            blocks,
            collapsedFoldIds,
            wrapColumns,
            wrapWidth,
            context.TabDisplaySize,
            rows.GetWrapBreaksByVisualLine(),
            rows,
            heights);
        _cachedLayout = layout;
        return layout;
    }

    private static VisualLineHeightIndex BuildHeightIndex(
        AzunyanEditorRenderContext context,
        VisualRowMap rows,
        ProjectedTextLayoutState? previousLayout)
    {
        if (rows.HasUniformTextHeights)
        {
            return VisualLineHeightIndex.CreateUniform(rows.Rows.Count, context.LineHeight);
        }

        if (previousLayout is not null
            && rows.ChangeWindow is { } changeWindow
            && changeWindow.OldStart >= 0
            && changeWindow.OldEnd >= changeWindow.OldStart
            && changeWindow.NewStart >= 0
            && changeWindow.NewEnd >= changeWindow.NewStart
            && changeWindow.OldEnd <= previousLayout.Heights.Count
            && changeWindow.NewEnd <= rows.Rows.Count
            && previousLayout.Heights.Count
                - (changeWindow.OldEnd - changeWindow.OldStart)
                + (changeWindow.NewEnd - changeWindow.NewStart)
                == rows.Rows.Count)
        {
            var heights = previousLayout.Heights.Clone();
            heights.Splice(
                changeWindow.OldStart,
                changeWindow.OldEnd - changeWindow.OldStart,
                rows.Rows
                    .Skip(changeWindow.NewStart)
                    .Take(changeWindow.NewEnd - changeWindow.NewStart)
                    .Select(row => GetRowHeight(row, context.LineHeight)));
            return heights;
        }

        return new VisualLineHeightIndex(
            rows.Rows.Select(row => GetRowHeight(row, context.LineHeight)));
    }

    private static double GetRowHeight(VisualRow row, double lineHeight) =>
        row.BlockAdornment is { } block
            ? Math.Max(1, block.DesiredHeight)
            : lineHeight;

    private static bool WrapBreaksEqual(
        Dictionary<int, IReadOnlyList<int>> previous,
        IReadOnlyDictionary<int, IReadOnlyList<int>> current) =>
        previous.Count == current.Count
        && previous.All(pair =>
            current.TryGetValue(pair.Key, out var currentBreaks)
            && pair.Value.SequenceEqual(currentBreaks));

    private static void DrawBlock(
        CanvasDrawingSession drawingSession,
        AzunyanEditorRenderContext context,
        BlockAdornment block,
        double height,
        double top)
    {
        using var layout = DirectWriteTextLayout.Create(
            drawingSession,
            new[] { new DirectWriteTextRun(block.Content.Text, context.ColorScheme.FoldForeground) },
            context.FontFamily.Source,
            (float)context.FontSize,
            Math.Max(1, (float)(context.ViewportWidth - context.ContentLeft)),
            Math.Max(1, (float)height),
            (float)height,
            Math.Min((float)context.LineHeight * 0.8f, (float)height));
        layout.Draw(
            drawingSession,
            (float)(context.ContentLeft - context.HorizontalOffset),
            (float)top + Math.Max(0, (float)(height - context.LineHeight) / 2),
            context.ColorScheme.FoldForeground);
    }

    private void DrawTextRow(
        CanvasDrawingSession drawingSession,
        AzunyanEditorRenderContext context,
        UnwrappedLineLayout line,
        double top,
        VisualRow row)
    {
        if (!TryGetTextLayoutKey(row, out var key))
        {
            return;
        }

        TextLayoutEntry currentEntry;
        if (!_textLayouts.TryGetValue(key, out var entry)
            || !entry.Matches(line.Runs))
        {
            if (entry is not null)
            {
                entry.Layout.Dispose();
            }

            _textLayoutCreates++;
            var runs = line.Runs
                .Select(run => new DirectWriteTextRun(
                    run.Text,
                    GetForeground(context.ColorScheme, run),
                    run.Kind == LayoutRunKind.InlineAdornment,
                    IsLink(run)))
                .ToArray();
            var textLayout = DirectWriteTextLayout.Create(
                drawingSession,
                runs,
                context.FontFamily.Source,
                (float)context.FontSize,
                Math.Max(1, (float)(line.Width + context.CharacterWidth)),
                (float)context.LineHeight,
                (float)context.LineHeight,
                Math.Min((float)context.LineHeight * 0.8f, (float)context.LineHeight),
                (float)(context.CharacterWidth * context.TabDisplaySize));
            currentEntry = TextLayoutEntry.Create(
                textLayout,
                line.Runs,
                context.ColorScheme);
            _textLayouts[key] = currentEntry;
        }
        else
        {
            currentEntry = entry;
            _textLayoutHits++;
        }

        currentEntry.EnsureForegrounds(line.Runs, context.ColorScheme);
        DrawSelection(drawingSession, context, line, currentEntry.Layout, top, row);
        if (context.BlockSelection is not null
            || !context.Selection.IsEmpty
            || context.CaretSet?.Any(caret => !caret.Selection.IsEmpty) == true)
        {
            currentEntry.MarkForegroundsDirty();
        }
        currentEntry.Layout.Draw(
            drawingSession,
            (float)(context.ContentLeft - context.HorizontalOffset),
            (float)top,
            context.ColorScheme.EditorForeground);
        DrawComposition(drawingSession, context, line, currentEntry.Layout, top);
        DrawCaret(drawingSession, context, line, currentEntry.Layout, top);
    }

    private static void DrawComposition(
        CanvasDrawingSession drawingSession,
        AzunyanEditorRenderContext context,
        UnwrappedLineLayout layout,
        DirectWriteTextLayout textLayout,
        double top)
    {
        if (context.CompositionRange is not { } composition)
        {
            return;
        }

        var range = layout.SourceLine.SourceRange;
        var start = Math.Max(range.Start, composition.Start);
        var end = Math.Min(range.End, composition.End);
        if (end <= start)
        {
            return;
        }

        var globalStart = layout.SourceLine.GetVisualColumn(DocumentAnchor.Before(start));
        var globalEnd = layout.SourceLine.GetVisualColumn(DocumentAnchor.After(end));
        var selectedStart = Math.Max(globalStart, layout.VisualStart);
        var selectedEnd = Math.Min(globalEnd, layout.VisualEnd);
        if (selectedEnd <= selectedStart)
        {
            return;
        }

        var startColumn = selectedStart - layout.VisualStart;
        var endColumn = selectedEnd - layout.VisualStart;
        var left = context.ContentLeft - context.HorizontalOffset;
        foreach (var bounds in textLayout.GetCharacterBounds(
            startColumn,
            endColumn - startColumn))
        {
            var underlineY = top + context.LineHeight - 2;
            drawingSession.DrawLine(
                (float)(left + bounds.X),
                (float)underlineY,
                (float)(left + bounds.X + Math.Max(1, bounds.Width)),
                (float)underlineY,
                context.ColorScheme.CompositionForeground,
                1.5f);
        }
    }

    private static void DrawSelection(
        CanvasDrawingSession drawingSession,
        AzunyanEditorRenderContext context,
        UnwrappedLineLayout layout,
        DirectWriteTextLayout textLayout,
        double top,
        VisualRow row)
    {
        if (context.BlockSelection is { } blockSelection)
        {
            if (blockSelection.CoordinateSpace ==
                TextBlockSelectionCoordinateSpace.VisualRows)
            {
                if (row.VisualRowIndex < blockSelection.TopLine
                    || row.VisualRowIndex > blockSelection.BottomLine)
                {
                    return;
                }

                var startColumn = Math.Clamp(
                    blockSelection.LeftColumn,
                    0,
                    row.TextLength);
                var endColumn = Math.Clamp(
                    blockSelection.RightColumn,
                    0,
                    row.TextLength);
                DrawSelectionColumns(
                    drawingSession,
                    context,
                    textLayout,
                    top,
                    startColumn,
                    endColumn);
                return;
            }

            if (context.TextWrapping == TextWrapping.NoWrap)
            {
                var logicalLine = layout.SourceLine.LogicalLine;
                if (logicalLine < blockSelection.TopLine
                    || logicalLine > blockSelection.BottomLine)
                {
                    return;
                }

                var blockRange = TextBlockSelectionOperations.GetLineRange(
                    context.Snapshot,
                    logicalLine,
                    blockSelection.LeftColumn,
                    blockSelection.RightColumn,
                    context.TabDisplaySize);
                DrawSelectionRange(
                    drawingSession,
                    context,
                    layout,
                    textLayout,
                    top,
                    blockRange);
            }

            return;
        }

        var range = layout.SourceLine.SourceRange;
        if (context.CaretSet is { } caretSet)
        {
            foreach (var caret in caretSet)
            {
                var start = Math.Max(range.Start, caret.Selection.Start);
                var end = Math.Min(range.End, caret.Selection.End);
                if (end > start)
                {
                    DrawSelectionRange(
                        drawingSession,
                        context,
                        layout,
                        textLayout,
                        top,
                        TextRange.FromBounds(start, end));
                }
            }

            return;
        }

        var selectionStart = Math.Max(range.Start, context.Selection.Start);
        var selectionEnd = Math.Min(range.End, context.Selection.End);
        if (selectionEnd > selectionStart)
        {
            DrawSelectionRange(
                drawingSession,
                context,
                layout,
                textLayout,
                top,
                TextRange.FromBounds(selectionStart, selectionEnd));
        }
    }

    private static void DrawSelectionRange(
        CanvasDrawingSession drawingSession,
        AzunyanEditorRenderContext context,
        UnwrappedLineLayout layout,
        DirectWriteTextLayout textLayout,
        double top,
        TextRange range)
    {
        var sourceRange = layout.SourceLine.SourceRange;
        var start = Math.Max(sourceRange.Start, range.Start);
        var end = Math.Min(sourceRange.End, range.End);
        if (end <= start)
        {
            return;
        }

        var globalStart = layout.SourceLine.GetVisualColumn(DocumentAnchor.Before(start));
        var globalEnd = layout.SourceLine.GetVisualColumn(DocumentAnchor.After(end));
        var selectedStart = Math.Max(globalStart, layout.VisualStart);
        var selectedEnd = Math.Min(globalEnd, layout.VisualEnd);
        if (selectedEnd <= selectedStart)
        {
            return;
        }

        var startColumn = selectedStart - layout.VisualStart;
        var endColumn = selectedEnd - layout.VisualStart;
        textLayout.SetForegroundColor(
            startColumn,
            endColumn - startColumn,
            context.ColorScheme.SelectionForeground);
        var left = context.ContentLeft - context.HorizontalOffset;
        foreach (var bounds in textLayout.GetCharacterBounds(
            startColumn,
            endColumn - startColumn))
        {
            drawingSession.FillRectangle(
                new Rect(
                    left + bounds.X,
                    top,
                    Math.Max(1, bounds.Width),
                    context.LineHeight),
                context.ColorScheme.SelectionBackground);
        }
    }

    private static void DrawSelectionColumns(
        CanvasDrawingSession drawingSession,
        AzunyanEditorRenderContext context,
        DirectWriteTextLayout textLayout,
        double top,
        int startColumn,
        int endColumn)
    {
        startColumn = Math.Clamp(startColumn, 0, textLayout.Text.Length);
        endColumn = Math.Clamp(endColumn, startColumn, textLayout.Text.Length);
        if (endColumn <= startColumn)
        {
            return;
        }

        textLayout.SetForegroundColor(
            startColumn,
            endColumn - startColumn,
            context.ColorScheme.SelectionForeground);
        foreach (var bounds in textLayout.GetCharacterBounds(
            startColumn,
            endColumn - startColumn))
        {
            drawingSession.FillRectangle(
                new Rect(
                    context.ContentLeft - context.HorizontalOffset + bounds.X,
                    top,
                    Math.Max(1, bounds.Width),
                    context.LineHeight),
                context.ColorScheme.SelectionBackground);
        }
    }

    private static void DrawCaret(
        CanvasDrawingSession drawingSession,
        AzunyanEditorRenderContext context,
        UnwrappedLineLayout layout,
        DirectWriteTextLayout textLayout,
        double top)
    {
        if (context.CaretSet is { } caretSet)
        {
            foreach (var caret in caretSet)
            {
                DrawCaretAt(
                    drawingSession,
                    context,
                    layout,
                    textLayout,
                    top,
                    caret.CaretAnchor);
            }

            return;
        }

        DrawCaretAt(
            drawingSession,
            context,
            layout,
            textLayout,
            top,
            context.PrimaryCaretAnchor);
    }

    private static void DrawCaretAt(
        CanvasDrawingSession drawingSession,
        AzunyanEditorRenderContext context,
        UnwrappedLineLayout layout,
        DirectWriteTextLayout textLayout,
        double top,
        DocumentAnchor anchor)
    {
        var position = anchor.Position.Offset;
        var range = layout.SourceLine.SourceRange;
        if (position < range.Start || position > range.End)
        {
            return;
        }

        var column = layout.SourceLine.GetVisualColumn(anchor);
        if (column < layout.VisualStart
            || column > layout.VisualEnd
            || (column == layout.VisualStart
                && layout.VisualStart > 0
                && anchor.Affinity == AnchorAffinity.After)
            || (column == layout.VisualEnd
                && layout.VisualEnd < layout.SourceLine.VisualLength
                && anchor.Affinity != AnchorAffinity.After))
        {
            return;
        }

        var caretPosition = textLayout.GetCaretPosition(column - layout.VisualStart);
        drawingSession.FillRectangle(
            new Rect(
                context.ContentLeft - context.HorizontalOffset + caretPosition.X,
                top,
                1.5,
                context.LineHeight),
            context.ColorScheme.CaretForeground);
    }

    private static bool IsLink(LayoutRun run) =>
        run.Kind == LayoutRunKind.Text
        && run.Classification == SyntaxClassifications.Link;

    private static Color GetForeground(AzunyanColorScheme colors, LayoutRun run)
    {
        if (run.Kind == LayoutRunKind.FoldPlaceholder)
        {
            return colors.FoldForeground;
        }

        if (run.Kind == LayoutRunKind.InlineAdornment)
        {
            return colors.InlayForeground;
        }

        return colors.ResolveSyntaxForeground(run.Classification);
    }

    private sealed class ProjectedTextLayoutState
    {
        public ProjectedTextLayoutState(
            TextSnapshot snapshot,
            double lineHeight,
            IReadOnlyList<FoldRange>? folds,
            IReadOnlyList<InlineAdornment>? inlays,
            IReadOnlyList<BlockAdornment>? blocks,
            IReadOnlyList<string> collapsedFoldIds,
            int wrapColumns,
            double wrapWidth,
            int tabDisplaySize,
            IReadOnlyDictionary<int, IReadOnlyList<int>> measuredBreaks,
            VisualRowMap rows,
            VisualLineHeightIndex heights)
        {
            Snapshot = snapshot;
            LineHeight = lineHeight;
            Folds = folds;
            Inlays = inlays;
            Blocks = blocks;
            CollapsedFoldIds = collapsedFoldIds;
            WrapColumns = wrapColumns;
            WrapWidth = wrapWidth;
            TabDisplaySize = tabDisplaySize;
            WrapBreaks = measuredBreaks;
            Rows = rows;
            Heights = heights;
            FoldsByLogicalLine = (folds ?? Array.Empty<FoldRange>())
                .GroupBy(fold => snapshot.Lines.GetLine(fold.Range.Start))
                .ToDictionary(group => group.Key, group => (IReadOnlyList<FoldRange>)group.ToArray());
        }

        public TextSnapshot Snapshot { get; }

        public double LineHeight { get; }

        public IReadOnlyList<FoldRange>? Folds { get; }

        public IReadOnlyList<InlineAdornment>? Inlays { get; }

        public IReadOnlyList<BlockAdornment>? Blocks { get; }

        public IReadOnlyList<string> CollapsedFoldIds { get; }

        public int WrapColumns { get; }

        public double WrapWidth { get; }

        public int TabDisplaySize { get; }

        public IReadOnlyDictionary<int, IReadOnlyList<int>> WrapBreaks { get; }

        public VisualRowMap Rows { get; }

        public VisualLineHeightIndex Heights { get; }

        public Dictionary<int, IReadOnlyList<FoldRange>> FoldsByLogicalLine { get; }

        public bool Matches(
            TextSnapshot snapshot,
            double lineHeight,
            IReadOnlyList<FoldRange>? folds,
            IReadOnlyList<InlineAdornment>? inlays,
            IReadOnlyList<BlockAdornment>? blocks,
            IReadOnlyList<string> collapsedFoldIds,
            int wrapColumns,
            double wrapWidth,
            int tabDisplaySize) =>
            ReferenceEquals(Snapshot, snapshot)
            && LineHeight == lineHeight
            && ReferenceEquals(Folds, folds)
            && ReferenceEquals(Inlays, inlays)
            && ReferenceEquals(Blocks, blocks)
            && CollapsedFoldIds.SequenceEqual(collapsedFoldIds, StringComparer.Ordinal)
            && WrapColumns == wrapColumns
            && WrapWidth == wrapWidth
            && TabDisplaySize == tabDisplaySize;
    }

    private sealed class ProjectedTextRenderFrame
    {
        public ProjectedTextRenderFrame(
            AzunyanEditorRenderContext context,
            IReadOnlyList<ViewportRowLayout> layouts)
        {
            Context = context;
            Layouts = layouts;
            PreparedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        }

        public AzunyanEditorRenderContext Context { get; }

        public IReadOnlyList<ViewportRowLayout> Layouts { get; }

        public long PreparedAt { get; }
    }

    private Dictionary<int, IReadOnlyList<int>> MeasureVisibleWrapBreaks(
        AzunyanEditorRenderContext context,
        ProjectedTextLayoutState layout,
        double wrapWidth)
    {
        var measuredBreaks = new Dictionary<int, IReadOnlyList<int>>(layout.WrapBreaks);
        if (context.TextWrapping != TextWrapping.Wrap
            || wrapWidth <= 0
            || !_textSurface.ReadyToDraw
            || layout.Rows.Rows.Count == 0)
        {
            return measuredBreaks;
        }

        var startOffset = Math.Min(
            layout.Heights.TotalHeight,
            Math.Max(0, context.VerticalOffset - context.LineHeight));
        var endOffset = Math.Min(
            layout.Heights.TotalHeight,
            context.VerticalOffset
                + context.ViewportHeight
                + context.LineHeight);
        var firstRow = layout.Heights.FindLine(startOffset);
        var lastRow = layout.Heights.FindLine(endOffset);
        var projection = layout.Rows.Projection;

        // The estimated row count is good enough for virtualization while the
        // user is moving through the document, but it cannot describe the
        // true end of a wrapped document. Once the estimated viewport reaches
        // the end, resolve every remaining line so the scrollbar gets the
        // final extent and the last visual row can be reached.
        var reachesEstimatedEnd = context.VerticalOffset + context.ViewportHeight
            >= layout.Heights.TotalHeight - context.LineHeight;
        if (reachesEstimatedEnd)
        {
            for (var visualLine = 0; visualLine < projection.Lines.Count; visualLine++)
            {
                MeasureWrapBreaksForLine(
                    context,
                    projection.Lines[visualLine],
                    visualLine,
                    measuredBreaks,
                    wrapWidth);
            }

            return measuredBreaks;
        }

        for (var rowIndex = firstRow; rowIndex <= lastRow; rowIndex++)
        {
            var row = layout.Rows.Rows[rowIndex];
            if (row.TextLine is null
                || !projection.TryGetVisualLine(row.LogicalLine, out var visualLine)
                || measuredBreaks.ContainsKey(visualLine))
            {
                continue;
            }

            MeasureWrapBreaksForLine(
                context,
                row.TextLine,
                visualLine,
                measuredBreaks,
                wrapWidth);
        }

        return measuredBreaks;
    }

    private void MeasureWrapBreaksForLine(
        AzunyanEditorRenderContext context,
        ProjectedLine line,
        int visualLine,
        Dictionary<int, IReadOnlyList<int>> measuredBreaks,
        double wrapWidth)
    {
        if (measuredBreaks.ContainsKey(visualLine))
        {
            return;
        }

        measuredBreaks[visualLine] = DirectWriteTextLayout.MeasureWrapBreaks(
            _textSurface,
            line.Inlines
                .Select(inline => new DirectWriteTextRun(
                    GetProjectedInlineText(context.Snapshot, inline),
                    context.ColorScheme.EditorForeground,
                    inline is InlineAdornment))
                .ToArray(),
            context.FontFamily.Source,
            (float)context.FontSize,
            (float)wrapWidth,
            (float)context.LineHeight,
            Math.Min((float)context.LineHeight * 0.8f, (float)context.LineHeight),
            (float)(context.CharacterWidth * context.TabDisplaySize));
    }

    private static string GetProjectedInlineText(
        TextSnapshot snapshot,
        ProjectionInline inline) => inline switch
        {
            ProjectedText text => snapshot.GetText(text.Source),
            FoldPlaceholder fold => fold.DisplayText,
            InlineAdornment adornment => adornment.Content.Text,
            _ => string.Empty
        };

    private static int GetWrapColumns(AzunyanEditorRenderContext context)
    {
        if (context.TextWrapping != TextWrapping.Wrap)
        {
            return 0;
        }

        var availableWidth = GetWrapWidth(context);
        return Math.Max(1, (int)Math.Floor(availableWidth / context.CharacterWidth));
    }

    private static double GetWrapWidth(AzunyanEditorRenderContext context)
    {
        if (context.TextWrapping != TextWrapping.Wrap)
        {
            return 0;
        }

        return Math.Max(
            context.CharacterWidth,
            context.ViewportWidth - context.ContentLeft);
    }

    private static bool ContainsCaret(
        VisualRow row,
        int caretStop,
        AnchorAffinity affinity) =>
        row.TextLine is not null
        && caretStop >= row.TextStartColumn
        && caretStop <= row.TextEndColumn
        && (caretStop != row.TextStartColumn
            || row.TextStartColumn == 0
            || affinity != AnchorAffinity.After)
        && (caretStop != row.TextEndColumn
            || caretStop == row.TextLine.VisualLength
            || affinity == AnchorAffinity.After);

    private sealed class GutterLayoutEntry
    {
        public GutterLayoutEntry(string text, DirectWriteTextLayout layout)
        {
            Text = text;
            Layout = layout;
        }

        public string Text { get; }

        public DirectWriteTextLayout Layout { get; }
    }

    private sealed class TextLayoutCacheKey
    {
        public TextLayoutCacheKey(
            AzunyanColorScheme colorScheme,
            string fontFamily,
            double fontSize,
            double characterWidth,
            double lineHeight,
            double gutterWidth,
            int tabDisplaySize,
            TextWrapping textWrapping)
        {
            ColorScheme = colorScheme;
            FontFamily = fontFamily;
            FontSize = fontSize;
            CharacterWidth = characterWidth;
            LineHeight = lineHeight;
            GutterWidth = gutterWidth;
            TabDisplaySize = tabDisplaySize;
            TextWrapping = textWrapping;
        }

        public AzunyanColorScheme ColorScheme { get; }

        public string FontFamily { get; }

        public double FontSize { get; }

        public double CharacterWidth { get; }

        public double LineHeight { get; }

        public double GutterWidth { get; }

        public int TabDisplaySize { get; }

        public TextWrapping TextWrapping { get; }

        public bool Matches(TextLayoutCacheKey other) =>
            Equals(ColorScheme, other.ColorScheme)
            && string.Equals(FontFamily, other.FontFamily, StringComparison.Ordinal)
            && FontSize == other.FontSize
            && CharacterWidth == other.CharacterWidth
            && LineHeight == other.LineHeight
            && GutterWidth == other.GutterWidth
            && TabDisplaySize == other.TabDisplaySize
            && TextWrapping == other.TextWrapping;
    }

    private static bool TryGetTextLayoutKey(VisualRow row, out TextLayoutRowKey key)
    {
        if (row.TextLine is not { } line)
        {
            key = default;
            return false;
        }

        key = new TextLayoutRowKey(
            line.LayoutCacheIdentity,
            row.TextStartColumn,
            row.TextLength);
        return true;
    }

    private readonly record struct TextLayoutRowKey(
        object LineIdentity,
        int TextStartColumn,
        int TextLength);

    private readonly record struct DirectWriteRunShape(
        int VisualStart,
        int Length,
        bool IsInlineAdornment,
        bool IsLink);

    private readonly record struct DirectWriteForeground(
        int VisualStart,
        int Length,
        Color Color);

    private sealed class TextLayoutEntry
    {
        private TextLayoutEntry(
            DirectWriteTextLayout layout,
            DirectWriteRunShape[] shapes,
            DirectWriteForeground[] foregrounds)
        {
            Layout = layout;
            Shapes = shapes;
            _foregrounds = foregrounds;
        }

        private DirectWriteForeground[] _foregrounds;
        private bool _foregroundsDirty;

        public DirectWriteTextLayout Layout { get; }

        private DirectWriteRunShape[] Shapes { get; }

        public bool Matches(IReadOnlyList<LayoutRun> runs)
        {
            var shapeIndex = 0;
            foreach (var run in runs)
            {
                var isAdornment = run.Kind == LayoutRunKind.InlineAdornment;
                var isLink = IsLink(run);
                if (!isAdornment && !isLink)
                {
                    continue;
                }

                if (shapeIndex >= Shapes.Length
                    || Shapes[shapeIndex++] != new DirectWriteRunShape(
                        run.VisualStart,
                        run.Text.Length,
                        isAdornment,
                        isLink))
                {
                    return false;
                }
            }

            return shapeIndex == Shapes.Length;
        }

        public void EnsureForegrounds(
            IReadOnlyList<LayoutRun> runs,
            AzunyanColorScheme colors)
        {
            if (!_foregroundsDirty && ForegroundsMatch(runs, colors))
            {
                return;
            }

            _foregrounds = CreateForegrounds(runs, colors);
            foreach (var foreground in _foregrounds)
            {
                Layout.SetForegroundColor(
                    foreground.VisualStart,
                    foreground.Length,
                    foreground.Color);
            }

            _foregroundsDirty = false;
        }

        public void MarkForegroundsDirty() => _foregroundsDirty = true;

        private bool ForegroundsMatch(
            IReadOnlyList<LayoutRun> runs,
            AzunyanColorScheme colors)
        {
            var foregroundIndex = 0;
            foreach (var run in runs)
            {
                if (run.Text.Length == 0)
                {
                    continue;
                }

                if (foregroundIndex >= _foregrounds.Length
                    || _foregrounds[foregroundIndex++] != new DirectWriteForeground(
                        run.VisualStart,
                        run.Text.Length,
                        GetForeground(colors, run)))
                {
                    return false;
                }
            }

            return foregroundIndex == _foregrounds.Length;
        }

        public static TextLayoutEntry Create(
            DirectWriteTextLayout layout,
            IReadOnlyList<LayoutRun> runs,
            AzunyanColorScheme colors) =>
            new(
                layout,
                runs
                    .Where(run => run.Kind == LayoutRunKind.InlineAdornment || IsLink(run))
                    .Select(run => new DirectWriteRunShape(
                        run.VisualStart,
                        run.Text.Length,
                        run.Kind == LayoutRunKind.InlineAdornment,
                        IsLink(run)))
                    .ToArray(),
                CreateForegrounds(runs, colors));

        private static DirectWriteForeground[] CreateForegrounds(
            IReadOnlyList<LayoutRun> runs,
            AzunyanColorScheme colors) =>
            runs
                .Where(run => run.Text.Length > 0)
                .Select(run => new DirectWriteForeground(
                    run.VisualStart,
                    run.Text.Length,
                    GetForeground(colors, run)))
                .ToArray();
    }
}

internal sealed class AzunyanEditorRenderer :
    IAzunyanEditorRenderer,
    IAzunyanEditorNavigationGeometry
{
    private readonly Canvas _gutterLayer;
    private readonly Canvas _textLayer;
    private readonly Canvas _overlayLayer;

    public AzunyanEditorRenderer(
        CanvasControl gutterSurface,
        CanvasControl textSurface,
        Canvas gutterLayer,
        Canvas textLayer,
        Canvas overlayLayer,
        Action<string>? toggleFold = null)
    {
        TextRenderer = new ProjectedTextRenderer(gutterSurface, textSurface);
        _gutterLayer = gutterLayer ?? throw new ArgumentNullException(nameof(gutterLayer));
        _textLayer = textLayer ?? throw new ArgumentNullException(nameof(textLayer));
        _overlayLayer = overlayLayer ?? throw new ArgumentNullException(nameof(overlayLayer));
        ToggleFold = toggleFold;
    }

    public ProjectedTextRenderer TextRenderer { get; }

    private Action<string>? ToggleFold { get; }

    public event EventHandler? LayoutInvalidated
    {
        add => TextRenderer.LayoutInvalidated += value;
        remove => TextRenderer.LayoutInvalidated -= value;
    }

    public void Render(AzunyanEditorRenderFrame frame) =>
        TextRenderer.Render(new AzunyanEditorRenderContext(
            frame,
            _gutterLayer,
            _textLayer,
            _overlayLayer,
            ToggleFold));

    public bool TryGetCaretRect(DocumentAnchor anchor, out Rect rect) =>
        TextRenderer.TryGetCaretRect(anchor, out rect);

    public bool TryNavigate(
        AzunyanEditorNavigationRequest request,
        out TextCaretSet result) =>
        TextRenderer.TryNavigateCarets(
            request.Carets,
            request.Kind,
            request.ExtendSelection,
            request.TabDisplaySize,
            out result);

    public void Dispose() => TextRenderer.Dispose();
}
