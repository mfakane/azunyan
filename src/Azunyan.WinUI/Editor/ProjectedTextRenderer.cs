using Azunyan.Core;
using Azunyan.Layout;
using Azunyan.WinUI;
using System.Globalization;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
    private readonly CanvasControl _gutterSurface;
    private readonly CanvasControl _textSurface;
    private readonly Dictionary<ProjectedLine, UnwrappedLineLayout> _lineLayouts = new();
    private readonly Dictionary<VisualRow, GutterLayoutEntry> _gutterLayouts = new();
    private readonly Dictionary<VisualRow, DirectWriteTextLayout> _textLayouts = new();
    private ProjectedTextLayoutState? _cachedLayout;
    private ProjectedTextRenderFrame? _renderFrame;
    private DocumentChangedEventArgs? _pendingDocumentChange;
    private TextLayoutCacheKey? _textLayoutCacheKey;
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

        var position = layout.Rows.Projection.MapDocumentPosition(anchor);
        var textLine = layout.Rows.Projection.Lines[position.VisualLine];
        foreach (var index in layout.Rows.GetTextRowIndices(textLine))
        {
            var row = layout.Rows.Rows[index];
            if (ContainsCaret(row, position.CaretStop))
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
        var position = projection.MapDocumentPosition(anchor);
        var projectedLine = projection.Lines[position.VisualLine];
        var rowIndex = -1;
        foreach (var index in layout.Rows.GetTextRowIndices(projectedLine))
        {
            var row = layout.Rows.Rows[index];
            if (ContainsCaret(row, position.CaretStop))
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
                var position = projection.MapDocumentPosition(
                    DocumentAnchor.Before(range.Start));
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
                var position = projection.MapDocumentPosition(
                    DocumentAnchor.Before(range.End));
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

    private float GetCaretX(int rowIndex, int localStop, double characterWidth)
    {
        if (_renderFrame is { } frame
            && rowIndex >= 0
            && rowIndex < frame.Layouts.Count
            && _textLayouts.TryGetValue(frame.Layouts[rowIndex].Row, out var directWriteLayout))
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
        return _textLayouts.TryGetValue(row, out var layout)
            ? layout
            : null;
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
        out string? adornmentId)
    {
        anchor = DocumentAnchor.Before(0);
        foldId = null;
        adornmentId = null;
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

        var documentY = y + verticalOffset - contentTop;
        if (documentY < 0 || documentY > layout.Heights.TotalHeight)
        {
            return false;
        }

        var rowIndex = layout.Heights.FindLine(documentY);
        var row = layout.Rows.Rows[rowIndex];
        if (row.TextLine is not { } textLine)
        {
            anchor = row.BlockAdornment!.Anchor;
            adornmentId = row.BlockAdornment.Id;
            return true;
        }

        var xInText = (x + horizontalOffset - contentLeft) / characterWidth;
        var localColumn = GetNearestCaretStop(
            rowIndex,
            row,
            xInText,
            characterWidth);
        var visualColumn = row.TextStartColumn + localColumn;
        anchor = GetLineLayoutForGlobalRow(rowIndex, row)
            ?.GetDocumentAnchorAtCaretStop(localColumn)
            ?? textLine.GetAnchor(visualColumn);

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

        var wrapWidth = GetWrapWidth(context);
        var wrapColumns = GetWrapColumns(context);
        var layoutState = GetLayoutState(context, wrapColumns, wrapWidth);
        var metrics = new LayoutMetrics(
            context.CharacterWidth,
            context.LineHeight,
            Math.Min(context.LineHeight * 0.8, context.LineHeight));
        var layouts = ViewportLayoutEngine.LayoutVisibleRows(
            context.Snapshot,
            layoutState.Rows,
            layoutState.Heights,
            new LayoutViewport(context.VerticalOffset, context.ViewportHeight),
            overscan: context.LineHeight,
            context.DocumentResults?.Syntax ?? Array.Empty<SyntaxSpan>(),
            metrics,
            new MonospaceLineLayoutEngine(),
            _lineLayouts);

        EnsureTextLayoutCache(context);
        _renderFrame = new ProjectedTextRenderFrame(context, layouts);
        PruneLayoutCaches(layouts);
        if (_pendingDocumentChange is { } change
            && ReferenceEquals(change.NewSnapshot, context.Snapshot))
        {
            _pendingDocumentChange = null;
        }
        _gutterSurface.Invalidate();
        _textSurface.Invalidate();
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
        foreach (var textLayout in _textLayouts.Values)
        {
            textLayout.Dispose();
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
        var key = new TextLayoutCacheKey(
            context.Snapshot,
            context.DocumentResults?.Syntax,
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
        var rows = layouts
            .Select(layout => layout.Row)
            .ToHashSet();
        var textLines = layouts
            .Select(layout => layout.Row.TextLine)
            .Where(line => line is not null)
            .Cast<ProjectedLine>()
            .ToHashSet();
        foreach (var line in _lineLayouts.Keys.Where(line => !textLines.Contains(line)).ToArray())
        {
            _lineLayouts.Remove(line);
        }

        foreach (var row in _textLayouts.Keys.Where(row => !rows.Contains(row)).ToArray())
        {
            _textLayouts[row].Dispose();
            _textLayouts.Remove(row);
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
    }

    private ProjectedTextLayoutState GetLayoutState(
        AzunyanEditorRenderContext context,
        int wrapColumns,
        double wrapWidth)
    {
        var documentFolds = context.DocumentResults?.Folds;
        var inlays = context.ViewportResults?.Inlays;
        var blocks = context.ViewportResults?.BlockAdornments;
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
                .Where(fold => collapsedFoldIds.Contains(fold.Id, StringComparer.Ordinal))
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
            && !ReferenceEquals(previousLayout.Snapshot, context.Snapshot)
            && pendingChange is { } change
            && ReferenceEquals(change.OldSnapshot, previousLayout.Snapshot)
            && ReferenceEquals(change.NewSnapshot, context.Snapshot);
        var rows = canBuildIncrementally
            ? VisualRowMapBuilder.BuildIncremental(
                previousLayout!.Rows.Projection,
                projection,
                previousLayout.Rows,
                currentBlocks,
                wrapColumns,
                measuredBreaks,
                pendingChange!.Change)
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

        return rows.HasUniformTextHeights
            ? VisualLineHeightIndex.CreateUniform(rows.Rows.Count, context.LineHeight)
            : new VisualLineHeightIndex(rows.Rows.Select(row => GetRowHeight(row, context.LineHeight)));
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
        if (!_textLayouts.TryGetValue(row, out var textLayout))
        {
            var runs = line.Runs
                .Select(run => new DirectWriteTextRun(
                    run.Text,
                    GetForeground(context.ColorScheme, run),
                    run.Kind == LayoutRunKind.InlineAdornment))
                .ToArray();
            textLayout = DirectWriteTextLayout.Create(
                drawingSession,
                runs,
                context.FontFamily.Source,
                (float)context.FontSize,
                Math.Max(1, (float)(line.Width + context.CharacterWidth)),
                (float)context.LineHeight,
                (float)context.LineHeight,
                Math.Min((float)context.LineHeight * 0.8f, (float)context.LineHeight),
                (float)(context.CharacterWidth * context.TabDisplaySize));
            _textLayouts.Add(row, textLayout);
        }

        DrawSelection(drawingSession, context, line, textLayout, top);
        textLayout.Draw(
            drawingSession,
            (float)(context.ContentLeft - context.HorizontalOffset),
            (float)top,
            context.ColorScheme.EditorForeground);
        DrawComposition(drawingSession, context, line, textLayout, top);
        DrawCaret(drawingSession, context, line, textLayout, top);
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
        double top)
    {
        var range = layout.SourceLine.SourceRange;
        var start = Math.Max(range.Start, context.Selection.Start);
        var end = Math.Min(range.End, context.Selection.End);
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
            drawingSession.FillRectangle(
                new Rect(
                    left + bounds.X,
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
        var position = context.Selection.CaretPosition;
        var range = layout.SourceLine.SourceRange;
        if (position < range.Start || position > range.End)
        {
            return;
        }

        var column = layout.SourceLine.GetVisualColumn(DocumentAnchor.Before(position));
        if (column < layout.VisualStart
            || column > layout.VisualEnd
            || (column == layout.VisualEnd && layout.VisualEnd < layout.SourceLine.VisualLength))
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
        }

        public AzunyanEditorRenderContext Context { get; }

        public IReadOnlyList<ViewportRowLayout> Layouts { get; }
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
        for (var rowIndex = firstRow; rowIndex <= lastRow; rowIndex++)
        {
            var row = layout.Rows.Rows[rowIndex];
            if (row.TextLine is null
                || !projection.TryGetVisualLine(row.LogicalLine, out var visualLine)
                || measuredBreaks.ContainsKey(visualLine))
            {
                continue;
            }

            measuredBreaks[visualLine] = DirectWriteTextLayout.MeasureWrapBreaks(
                _textSurface,
                row.TextLine.Inlines
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

        return measuredBreaks;
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
            context.ViewportWidth - context.ContentLeft - 18);
    }

    private static bool ContainsCaret(VisualRow row, int caretStop) =>
        row.TextLine is not null
        && caretStop >= row.TextStartColumn
        && (caretStop < row.TextEndColumn
            || caretStop == row.TextEndColumn && caretStop == row.TextLine.VisualLength);

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
            TextSnapshot snapshot,
            IReadOnlyList<SyntaxSpan>? syntax,
            AzunyanColorScheme colorScheme,
            string fontFamily,
            double fontSize,
            double characterWidth,
            double lineHeight,
            double gutterWidth,
            int tabDisplaySize,
            TextWrapping textWrapping)
        {
            Snapshot = snapshot;
            Syntax = syntax;
            ColorScheme = colorScheme;
            FontFamily = fontFamily;
            FontSize = fontSize;
            CharacterWidth = characterWidth;
            LineHeight = lineHeight;
            GutterWidth = gutterWidth;
            TabDisplaySize = tabDisplaySize;
            TextWrapping = textWrapping;
        }

        public TextSnapshot Snapshot { get; }

        public IReadOnlyList<SyntaxSpan>? Syntax { get; }

        public AzunyanColorScheme ColorScheme { get; }

        public string FontFamily { get; }

        public double FontSize { get; }

        public double CharacterWidth { get; }

        public double LineHeight { get; }

        public double GutterWidth { get; }

        public int TabDisplaySize { get; }

        public TextWrapping TextWrapping { get; }

        public bool Matches(TextLayoutCacheKey other) =>
            ReferenceEquals(Snapshot, other.Snapshot)
            && ReferenceEquals(Syntax, other.Syntax)
            && Equals(ColorScheme, other.ColorScheme)
            && string.Equals(FontFamily, other.FontFamily, StringComparison.Ordinal)
            && FontSize == other.FontSize
            && CharacterWidth == other.CharacterWidth
            && LineHeight == other.LineHeight
            && GutterWidth == other.GutterWidth
            && TabDisplaySize == other.TabDisplaySize
            && TextWrapping == other.TextWrapping;
    }
}

internal sealed class AzunyanEditorRenderer : IAzunyanEditorRenderer
{
    private readonly Canvas _gutterLayer;
    private readonly Canvas _textLayer;
    private readonly Canvas _overlayLayer;

    public AzunyanEditorRenderer(
        CanvasControl gutterSurface,
        CanvasControl textSurface,
        Canvas gutterLayer,
        Canvas textLayer,
        Canvas overlayLayer)
    {
        TextRenderer = new ProjectedTextRenderer(gutterSurface, textSurface);
        _gutterLayer = gutterLayer ?? throw new ArgumentNullException(nameof(gutterLayer));
        _textLayer = textLayer ?? throw new ArgumentNullException(nameof(textLayer));
        _overlayLayer = overlayLayer ?? throw new ArgumentNullException(nameof(overlayLayer));
    }

    public ProjectedTextRenderer TextRenderer { get; }

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
            _overlayLayer));

    public void Dispose() => TextRenderer.Dispose();
}
