namespace Azunyan.Core;

/// <summary>
/// A UTF-16 position in one <see cref="TextSnapshot"/>. The explicit type
/// prevents document offsets from being confused with visual-line indices.
/// </summary>
public readonly record struct DocumentPosition
{
    public DocumentPosition(int offset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);

        Offset = offset;
    }

    public int Offset { get; }
}

public enum AnchorAffinity
{
    Before,
    After
}

public readonly record struct DocumentAnchor
{
    public DocumentAnchor(DocumentPosition position, AnchorAffinity affinity = AnchorAffinity.Before)
    {
        Position = position;
        Affinity = affinity;
    }

    public DocumentPosition Position { get; }

    public AnchorAffinity Affinity { get; }

    public static DocumentAnchor Before(int offset) =>
        new(new DocumentPosition(offset), AnchorAffinity.Before);

    public static DocumentAnchor After(int offset) =>
        new(new DocumentPosition(offset), AnchorAffinity.After);
}

public readonly record struct VisualPosition
{
    public VisualPosition(int visualLine, int caretStop)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(visualLine);
        ArgumentOutOfRangeException.ThrowIfNegative(caretStop);

        VisualLine = visualLine;
        CaretStop = caretStop;
    }

    public int VisualLine { get; }

    public int CaretStop { get; }
}

public sealed record AdornmentAction
{
    public AdornmentAction(string id, string label, string commandId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        Id = id;
        Label = label;
        CommandId = commandId;
    }

    public string Id { get; }

    public string Label { get; }

    public string CommandId { get; }
}

public sealed record AdornmentContent
{
    public AdornmentContent(
        string text,
        string? iconKey = null,
        IReadOnlyList<AdornmentAction>? actions = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
        IconKey = iconKey;
        Actions = actions ?? Array.Empty<AdornmentAction>();
    }

    public string Text { get; }

    public string? IconKey { get; }

    public IReadOnlyList<AdornmentAction> Actions { get; }
}

public sealed record FoldRange
{
    public FoldRange(string id, TextRange range, string placeholder = "…")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(placeholder);
        if (range.IsEmpty)
        {
            throw new ArgumentException("A fold range must contain text.", nameof(range));
        }

        Id = id;
        Range = range;
        Placeholder = placeholder;
    }

    public string Id { get; }

    public TextRange Range { get; }

    public string Placeholder { get; }
}

public abstract record ProjectionInline;

public sealed record ProjectedText(TextRange Source) : ProjectionInline;

public sealed record FoldPlaceholder(
    TextRange HiddenSource,
    string DisplayText,
    string FoldId) : ProjectionInline;

public sealed record InlineAdornment(
    string Id,
    DocumentAnchor Anchor,
    string Kind,
    AdornmentContent Content) : ProjectionInline;

public sealed record BlockAdornment
{
    public BlockAdornment(
        string id,
        DocumentAnchor anchor,
        double desiredHeight,
        string kind,
        AdornmentContent content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(content);
        Id = id;
        Anchor = anchor;
        DesiredHeight = ValidateHeight(desiredHeight);
        Kind = kind;
        Content = content;
    }

    public string Id { get; }

    public DocumentAnchor Anchor { get; }

    public double DesiredHeight { get; }

    public string Kind { get; }

    public AdornmentContent Content { get; }

    private static double ValidateHeight(double height)
    {
        if (!double.IsFinite(height) || height < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        return height;
    }
}

/// <summary>
/// One logical line after folds and inline adornments have been projected. It
/// is intentionally unwrapped; wrapping is added by the layout layer later.
/// </summary>
public sealed class ProjectedLine
{
    internal ProjectedLine(int logicalLine, TextRange sourceRange, IReadOnlyList<ProjectionInline> inlines)
    {
        LogicalLine = logicalLine;
        SourceRange = sourceRange;
        Inlines = inlines;
        VisualLength = inlines.Sum(GetVisualLength);
    }

    public int LogicalLine { get; }

    public TextRange SourceRange { get; }

    public IReadOnlyList<ProjectionInline> Inlines { get; }

    /// <summary>
    /// The number of UTF-16 display cells in this unwrapped projected line.
    /// Layout replaces this estimate with shaped caret stops later.
    /// </summary>
    public int VisualLength { get; }

    public int GetVisualColumn(DocumentAnchor anchor)
    {
        var offset = anchor.Position.Offset;
        var column = 0;
        for (var index = 0; index < Inlines.Count; index++)
        {
            var inline = Inlines[index];
            switch (inline)
            {
                case ProjectedText text:
                    if (offset < text.Source.Start)
                    {
                        return column;
                    }

                    if (offset <= text.Source.End)
                    {
                        if (offset == text.Source.End
                            && anchor.Affinity == AnchorAffinity.After
                            && index + 1 < Inlines.Count
                            && Inlines[index + 1] is InlineAdornment nextAdornment
                            && nextAdornment.Anchor.Position.Offset == offset)
                        {
                            var after = column + text.Source.Length + nextAdornment.Content.Text.Length;
                            for (var next = index + 2; next < Inlines.Count; next++)
                            {
                                if (Inlines[next] is not InlineAdornment followingAdornment
                                    || followingAdornment.Anchor.Position.Offset != offset)
                                {
                                    break;
                                }

                                after += followingAdornment.Content.Text.Length;
                            }

                            return after;
                        }

                        return column + (offset - text.Source.Start);
                    }

                    column += text.Source.Length;
                    break;
                case FoldPlaceholder fold:
                    if (offset < fold.HiddenSource.Start)
                    {
                        return column;
                    }

                    if (offset == fold.HiddenSource.Start)
                    {
                        return anchor.Affinity == AnchorAffinity.Before
                            ? column
                            : column + fold.DisplayText.Length;
                    }

                    if (offset < fold.HiddenSource.End)
                    {
                        return anchor.Affinity == AnchorAffinity.Before
                            ? column
                            : column + fold.DisplayText.Length;
                    }

                    column += fold.DisplayText.Length;
                    break;
                case InlineAdornment adornment:
                    var adornmentOffset = adornment.Anchor.Position.Offset;
                    if (offset < adornmentOffset)
                    {
                        return column;
                    }

                    if (offset == adornmentOffset)
                    {
                        if (anchor.Affinity == AnchorAffinity.Before)
                        {
                            return column;
                        }

                        var after = column + adornment.Content.Text.Length;
                        for (var next = index + 1; next < Inlines.Count; next++)
                        {
                            if (Inlines[next] is not InlineAdornment nextAdornment
                                || nextAdornment.Anchor.Position.Offset != offset)
                            {
                                break;
                            }

                            after += nextAdornment.Content.Text.Length;
                        }

                        return after;
                    }

                    column += adornment.Content.Text.Length;
                    break;
            }
        }

        return column;
    }

    internal ProjectedLine Rebase(int logicalLine, TextRange sourceRange)
    {
        var offsetDelta = sourceRange.Start - SourceRange.Start;
        var inlines = Inlines
            .Select(inline => inline switch
            {
                ProjectedText text => new ProjectedText(
                    TextRange.FromBounds(
                        text.Source.Start + offsetDelta,
                        text.Source.End + offsetDelta)),
                _ => inline
            })
            .ToArray();
        return new ProjectedLine(logicalLine, sourceRange, inlines);
    }

    public DocumentAnchor GetAnchor(int visualColumn)
    {
        if (visualColumn < 0 || visualColumn > VisualLength)
        {
            throw new ArgumentOutOfRangeException(nameof(visualColumn));
        }

        var column = 0;
        for (var index = 0; index < Inlines.Count; index++)
        {
            var inline = Inlines[index];
            switch (inline)
            {
                case ProjectedText text:
                    {
                        var end = column + text.Source.Length;
                        if (visualColumn <= end)
                        {
                            var offset = text.Source.Start + (visualColumn - column);
                            var beforeNextAdornment = visualColumn == end
                                && index + 1 < Inlines.Count
                                && Inlines[index + 1] is InlineAdornment next
                                && next.Anchor.Position.Offset == text.Source.End;
                            return new DocumentAnchor(
                                new DocumentPosition(offset),
                                beforeNextAdornment ? AnchorAffinity.Before : AnchorAffinity.After);
                        }

                        column = end;
                        break;
                    }
                case FoldPlaceholder fold:
                    {
                        var end = column + fold.DisplayText.Length;
                        if (visualColumn <= column)
                        {
                            return DocumentAnchor.Before(fold.HiddenSource.Start);
                        }

                        if (visualColumn < end)
                        {
                            var midpoint = column + (fold.DisplayText.Length / 2);
                            return visualColumn < midpoint
                                ? DocumentAnchor.Before(fold.HiddenSource.Start)
                                : DocumentAnchor.After(fold.HiddenSource.End);
                        }

                        column = end;
                        break;
                    }
                case InlineAdornment adornment:
                    {
                        var end = column + adornment.Content.Text.Length;
                        if (visualColumn <= column)
                        {
                            return new DocumentAnchor(adornment.Anchor.Position, AnchorAffinity.Before);
                        }

                        if (visualColumn <= end)
                        {
                            return new DocumentAnchor(adornment.Anchor.Position, AnchorAffinity.After);
                        }

                        column = end;
                        break;
                    }
            }
        }

        return Inlines[^1] is FoldPlaceholder lastFold
            ? DocumentAnchor.After(lastFold.HiddenSource.End)
            : DocumentAnchor.After(SourceRange.End);
    }

    private static int GetVisualLength(ProjectionInline inline) => inline switch
    {
        ProjectedText text => text.Source.Length,
        FoldPlaceholder fold => fold.DisplayText.Length,
        InlineAdornment adornment => adornment.Content.Text.Length,
        _ => throw new ArgumentOutOfRangeException(nameof(inline))
    };
}

/// <summary>
/// A snapshot-bound, unwrapped projection. It maps document anchors through
/// folded ranges and inline adornments without changing document offsets.
/// </summary>
public sealed class TextProjection
{
    private readonly int[] _logicalToVisual;
    private readonly IReadOnlyList<FoldRange> _folds;

    internal TextProjection(
        TextSnapshot snapshot,
        IReadOnlyList<ProjectedLine> lines,
        IReadOnlyList<FoldRange> folds,
        int[] logicalToVisual)
    {
        Snapshot = snapshot;
        Lines = lines;
        _folds = folds;
        _logicalToVisual = logicalToVisual;
    }

    public TextSnapshot Snapshot { get; }

    public IReadOnlyList<ProjectedLine> Lines { get; }

    public int VisualLineCount => Lines.Count;

    public VisualPosition MapDocumentPosition(DocumentAnchor anchor)
    {
        ValidateDocumentAnchor(anchor);
        var offset = anchor.Position.Offset;
        var containingFold = FindContainingFold(offset, anchor.Affinity);
        var logicalLine = Snapshot.Lines.GetLine(containingFold?.Range.Start ?? offset);
        var visualLine = _logicalToVisual[logicalLine];
        if (visualLine < 0)
        {
            throw new InvalidOperationException("The projection has no visible line for the requested position.");
        }

        return new VisualPosition(visualLine, Lines[visualLine].GetVisualColumn(anchor));
    }

    public bool IsHidden(DocumentAnchor anchor)
    {
        ValidateDocumentAnchor(anchor);
        var offset = anchor.Position.Offset;
        return _folds.Any(fold => offset > fold.Range.Start && offset < fold.Range.End);
    }

    public bool TryGetVisualLine(int logicalLine, out int visualLine)
    {
        if (logicalLine < 0 || logicalLine >= _logicalToVisual.Length)
        {
            visualLine = -1;
            return false;
        }

        visualLine = _logicalToVisual[logicalLine];
        return visualLine >= 0;
    }

    public DocumentAnchor MapVisualPosition(VisualPosition position)
    {
        if (position.VisualLine < 0 || position.VisualLine >= Lines.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        return Lines[position.VisualLine].GetAnchor(position.CaretStop);
    }

    private FoldRange? FindContainingFold(int offset, AnchorAffinity affinity)
    {
        foreach (var fold in _folds)
        {
            if (offset > fold.Range.Start && offset < fold.Range.End)
            {
                return fold;
            }

            if (offset == fold.Range.End && affinity == AnchorAffinity.Before)
            {
                return fold;
            }
        }

        return null;
    }

    private void ValidateDocumentAnchor(DocumentAnchor anchor)
    {
        if (anchor.Position.Offset > Snapshot.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(anchor));
        }
    }
}

public sealed class TextProjectionBuilder
{
    public static TextProjection Build(
        TextSnapshot snapshot,
        IEnumerable<FoldRange>? folds = null,
        IEnumerable<InlineAdornment>? inlays = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var normalizedFolds = NormalizeFolds(snapshot, folds ?? Array.Empty<FoldRange>());
        var normalizedInlays = NormalizeInlays(snapshot, inlays ?? Array.Empty<InlineAdornment>());
        var lines = new List<ProjectedLine>();
        var logicalToVisual = Enumerable.Repeat(-1, snapshot.Lines.LineCount).ToArray();

        if (normalizedFolds.Count == 0 && normalizedInlays.Length == 0)
        {
            BuildPlainProjection(snapshot, lines, logicalToVisual);
            return new TextProjection(snapshot, lines, normalizedFolds, logicalToVisual);
        }

        for (var logicalLine = 0; logicalLine < snapshot.Lines.LineCount; logicalLine++)
        {
            var sourceRange = snapshot.Lines.GetLineRange(logicalLine);
            var inlines = BuildLineInlines(sourceRange, normalizedFolds, normalizedInlays);
            if (inlines is null)
            {
                continue;
            }

            logicalToVisual[logicalLine] = lines.Count;
            lines.Add(new ProjectedLine(logicalLine, sourceRange, inlines));
        }

        return new TextProjection(snapshot, lines, normalizedFolds, logicalToVisual);
    }

    /// <summary>
    /// Rebuilds a plain projection after one document replacement while
    /// reusing unaffected line projections. Fold and inlay projections are
    /// intentionally delegated to <see cref="Build"/> because their anchors
    /// can change across a wider provider result than the text edit itself.
    /// </summary>
    public static TextProjection BuildIncremental(
        TextSnapshot oldSnapshot,
        TextSnapshot snapshot,
        TextProjection previous,
        TextChange change)
    {
        ArgumentNullException.ThrowIfNull(oldSnapshot);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(previous);
        if (!ReferenceEquals(oldSnapshot, previous.Snapshot)
            || !IsPlain(previous)
            || change.OldRange.End > oldSnapshot.Length
            || change.NewRange.End > snapshot.Length)
        {
            return Build(snapshot);
        }

        var oldLines = previous.Snapshot.Lines;
        var newLines = snapshot.Lines;
        var lines = new List<ProjectedLine>(newLines.LineCount);
        var logicalToVisual = Enumerable.Repeat(-1, newLines.LineCount).ToArray();
        var delta = change.NewText.Length - change.OldRange.Length;

        for (var logicalLine = 0; logicalLine < newLines.LineCount; logicalLine++)
        {
            var sourceRange = newLines.GetLineRange(logicalLine);
            ProjectedLine line;
            if (sourceRange.End < change.OldRange.Start
                && logicalLine < previous.Lines.Count
                && previous.Lines[logicalLine].SourceRange == sourceRange)
            {
                line = previous.Lines[logicalLine];
            }
            else if (sourceRange.Start >= change.NewRange.End)
            {
                var oldStart = sourceRange.Start - delta;
                if (oldStart >= 0
                    && oldStart <= previous.Snapshot.Length
                    && oldLines.GetLineStart(oldLines.GetLine(oldStart)) == oldStart)
                {
                    var oldLineIndex = oldLines.GetLine(oldStart);
                    var oldRange = oldLines.GetLineRange(oldLineIndex);
                    if (oldRange.End + delta == sourceRange.End
                        && oldLineIndex < previous.Lines.Count)
                    {
                        line = previous.Lines[oldLineIndex].Rebase(
                            logicalLine,
                            sourceRange);
                    }
                    else
                    {
                        line = CreatePlainLine(logicalLine, sourceRange);
                    }
                }
                else
                {
                    line = CreatePlainLine(logicalLine, sourceRange);
                }
            }
            else
            {
                line = CreatePlainLine(logicalLine, sourceRange);
            }

            logicalToVisual[logicalLine] = lines.Count;
            lines.Add(line);
        }

        return new TextProjection(
            snapshot,
            lines,
            Array.Empty<FoldRange>(),
            logicalToVisual);
    }

    private static ProjectedLine CreatePlainLine(int logicalLine, TextRange sourceRange) =>
        new(
            logicalLine,
            sourceRange,
            new ProjectionInline[] { new ProjectedText(sourceRange) });

    private static bool IsPlain(TextProjection projection) =>
        projection.Lines.All(line => line.Inlines.All(inline => inline is ProjectedText));

    private static void BuildPlainProjection(
        TextSnapshot snapshot,
        List<ProjectedLine> lines,
        int[] logicalToVisual)
    {
        for (var logicalLine = 0; logicalLine < snapshot.Lines.LineCount; logicalLine++)
        {
            var sourceRange = snapshot.Lines.GetLineRange(logicalLine);
            logicalToVisual[logicalLine] = lines.Count;
            lines.Add(new ProjectedLine(
                logicalLine,
                sourceRange,
                new ProjectionInline[] { new ProjectedText(sourceRange) }));
        }
    }

    private static List<ProjectionInline>? BuildLineInlines(
        TextRange line,
        IReadOnlyList<FoldRange> folds,
        IReadOnlyList<InlineAdornment> inlays)
    {
        var coveringFold = folds.FirstOrDefault(fold =>
            fold.Range.Start < line.Start && fold.Range.End > line.Start);
        var cursor = coveringFold?.Range.End ?? line.Start;
        if (cursor > line.End)
        {
            return null;
        }

        var result = new List<ProjectionInline>();
        foreach (var fold in folds)
        {
            if (fold.Range.Start < cursor || fold.Range.Start > line.End)
            {
                continue;
            }

            AppendText(result, cursor, fold.Range.Start, inlays, includeEnd: false);
            result.Add(new FoldPlaceholder(fold.Range, fold.Placeholder, fold.Id));
            cursor = fold.Range.End;
            if (cursor > line.End)
            {
                break;
            }
        }

        if (cursor <= line.End)
        {
            AppendText(result, cursor, line.End, inlays);
        }

        if (result.Count == 0)
        {
            result.Add(new ProjectedText(TextRange.Empty(line.Start)));
        }

        return result;
    }

    private static void AppendText(
        List<ProjectionInline> result,
        int start,
        int end,
        IReadOnlyList<InlineAdornment> inlays,
        bool includeEnd = true)
    {
        if (end < start)
        {
            return;
        }

        var cursor = start;
        foreach (var inlay in inlays)
        {
            var position = inlay.Anchor.Position.Offset;
            if (position < start || position > end || (position == end && !includeEnd))
            {
                continue;
            }

            if (position > cursor)
            {
                result.Add(new ProjectedText(TextRange.FromBounds(cursor, position)));
            }

            result.Add(inlay);
            cursor = position;
        }

        if (cursor < end)
        {
            result.Add(new ProjectedText(TextRange.FromBounds(cursor, end)));
        }
    }

    private static List<FoldRange> NormalizeFolds(
        TextSnapshot snapshot,
        IEnumerable<FoldRange> candidates)
    {
        var accepted = new List<FoldRange>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var fold in candidates
            .Where(fold => fold.Range.Start <= snapshot.Length && fold.Range.End <= snapshot.Length)
            .OrderBy(fold => fold.Range.Start)
            .ThenByDescending(fold => fold.Range.End)
            .ThenBy(fold => fold.Id, StringComparer.Ordinal))
        {
            if (!ids.Add(fold.Id) || accepted.Any(existing => Overlaps(existing.Range, fold.Range)))
            {
                continue;
            }

            accepted.Add(fold);
        }

        return accepted;
    }

    private static InlineAdornment[] NormalizeInlays(
        TextSnapshot snapshot,
        IEnumerable<InlineAdornment> candidates) => candidates
        .Where(inlay => inlay.Anchor.Position.Offset <= snapshot.Length)
        .OrderBy(inlay => inlay.Anchor.Position.Offset)
        .ThenBy(inlay => inlay.Anchor.Affinity)
        .ThenBy(inlay => inlay.Id, StringComparer.Ordinal)
        .ToArray();

    private static bool Overlaps(TextRange left, TextRange right) =>
        left.Start < right.End && right.Start < left.End;
}
