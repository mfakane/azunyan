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
        Actions = Array.AsReadOnly((actions ?? Array.Empty<AdornmentAction>()).ToArray());
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
                FoldPlaceholder fold => new FoldPlaceholder(
                    TextRange.FromBounds(
                        fold.HiddenSource.Start + offsetDelta,
                        fold.HiddenSource.End + offsetDelta),
                    fold.DisplayText,
                    fold.FoldId),
                InlineAdornment adornment => new InlineAdornment(
                    adornment.Id,
                    new DocumentAnchor(
                        new DocumentPosition(adornment.Anchor.Position.Offset + offsetDelta),
                        adornment.Anchor.Affinity),
                    adornment.Kind,
                    adornment.Content),
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
internal readonly record struct ProjectionChangeWindow(
    int OldStartLine,
    int OldEndLine,
    int NewStartLine,
    int NewEndLine);

public sealed class TextProjection
{
    private readonly int[] _logicalToVisual;
    private readonly FoldRange[] _folds;
    private readonly InlineAdornment[] _inlays;
    private readonly int[] _foldStarts;
    private readonly ProjectedLineTable _lineTable;

    internal TextProjection(
        TextSnapshot snapshot,
        ProjectedLineTable lines,
        IReadOnlyList<FoldRange> folds,
        IReadOnlyList<InlineAdornment> inlays,
        int[]? logicalToVisual,
        bool isPlain,
        ProjectionChangeWindow? changeWindow = null)
    {
        Snapshot = snapshot;
        Lines = lines;
        _lineTable = lines;
        _folds = folds.ToArray();
        _inlays = inlays.ToArray();
        _foldStarts = _folds.Select(fold => fold.Range.Start).ToArray();
        _logicalToVisual = logicalToVisual ?? Array.Empty<int>();
        IsPlain = isPlain;
        ChangeWindow = changeWindow;
    }

    public TextSnapshot Snapshot { get; }

    public IReadOnlyList<ProjectedLine> Lines { get; }

    internal ProjectedLineTable LineTable => _lineTable;

    internal IReadOnlyList<FoldRange> Folds => _folds;

    internal IReadOnlyList<InlineAdornment> Inlays => _inlays;

    internal ProjectionChangeWindow? ChangeWindow { get; }

    internal int GetVisualLineForLogicalLine(int logicalLine) =>
        IsPlain ? logicalLine : _logicalToVisual[logicalLine];

    internal bool IsPlain { get; }

    public int VisualLineCount => Lines.Count;

    public VisualPosition MapDocumentPosition(DocumentAnchor anchor)
    {
        ValidateDocumentAnchor(anchor);
        var offset = anchor.Position.Offset;
        var containingFold = FindContainingFold(offset, anchor.Affinity);
        var logicalLine = Snapshot.Lines.GetLine(containingFold?.Range.Start ?? offset);
        var visualLine = IsPlain
            ? logicalLine
            : _logicalToVisual[logicalLine];
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
        return FindContainingFold(offset, AnchorAffinity.After) is not null;
    }

    public bool TryGetVisualLine(int logicalLine, out int visualLine)
    {
        if (logicalLine < 0 || logicalLine >= Snapshot.Lines.LineCount)
        {
            visualLine = -1;
            return false;
        }

        visualLine = GetVisualLineForLogicalLine(logicalLine);
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
        var firstAtOrAfter = LowerBound(_foldStarts, offset);
        if (firstAtOrAfter < _folds.Length
            && _foldStarts[firstAtOrAfter] == offset)
        {
            if (affinity == AnchorAffinity.Before
                && firstAtOrAfter > 0
                && _folds[firstAtOrAfter - 1].Range.End == offset)
            {
                return _folds[firstAtOrAfter - 1];
            }

            return null;
        }

        var candidate = firstAtOrAfter - 1;
        if (candidate < 0)
        {
            return null;
        }

        var fold = _folds[candidate];
        return offset < fold.Range.End
            || offset == fold.Range.End && affinity == AnchorAffinity.Before
            ? fold
            : null;
    }

    private static int LowerBound(int[] values, int value)
    {
        var low = 0;
        var high = values.Length;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (values[middle] < value)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
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

        if (normalizedFolds.Count == 0 && normalizedInlays.Length == 0)
        {
            BuildPlainProjection(snapshot, lines);
            return new TextProjection(
                snapshot,
                ProjectedLineTable.FromLines(lines),
                normalizedFolds,
                normalizedInlays,
                logicalToVisual: null,
                isPlain: true);
        }

        var logicalToVisual = Enumerable.Repeat(-1, snapshot.Lines.LineCount).ToArray();

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

        return new TextProjection(
            snapshot,
            ProjectedLineTable.FromLines(lines),
            normalizedFolds,
            normalizedInlays,
            logicalToVisual,
            isPlain: false);
    }

    /// <summary>
    /// Rebuilds a projection after one document replacement while reusing
    /// unaffected line projections. The four-argument overload is the plain
    /// projection convenience API.
    /// </summary>
    public static TextProjection BuildIncremental(
        TextSnapshot oldSnapshot,
        TextSnapshot snapshot,
        TextProjection previous,
        TextChange change)
        => BuildIncremental(
            oldSnapshot,
            snapshot,
            previous,
            change,
            folds: null,
            inlays: null);

    /// <summary>
    /// Incrementally rebuilds the projection and its fold/inlay metadata.
    /// Only lines affected by the document edit or by an adornment change are
    /// materialized; suffix lines are rebased lazily through their chunks.
    /// </summary>
    public static TextProjection BuildIncremental(
        TextSnapshot oldSnapshot,
        TextSnapshot snapshot,
        TextProjection previous,
        TextChange change,
        IEnumerable<FoldRange>? folds,
        IEnumerable<InlineAdornment>? inlays)
    {
        ArgumentNullException.ThrowIfNull(oldSnapshot);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(previous);
        if (!ReferenceEquals(oldSnapshot, previous.Snapshot)
            || change.OldRange.End > oldSnapshot.Length
            || change.NewRange.End > snapshot.Length)
        {
            return Build(snapshot, folds, inlays);
        }

        var normalizedFolds = NormalizeFolds(snapshot, folds ?? Array.Empty<FoldRange>());
        var normalizedInlays = NormalizeInlays(snapshot, inlays ?? Array.Empty<InlineAdornment>());
        if (previous.IsPlain
            && previous.Folds.Count == 0
            && previous.Inlays.Count == 0
            && normalizedFolds.Count == 0
            && normalizedInlays.Length == 0)
        {
            return BuildPlainIncremental(oldSnapshot, snapshot, previous, change);
        }

        var oldImpact = new List<TextRange> { change.OldRange };
        var newImpact = new List<TextRange> { change.NewRange };
        AddChangedFoldRanges(previous.Folds, normalizedFolds, change, oldImpact, newImpact);
        AddChangedInlayRanges(previous.Inlays, normalizedInlays, change, oldImpact, newImpact);

        var oldWindow = GetLineWindow(
            oldSnapshot,
            CombineRanges(oldImpact),
            EndsAfterLineBreak(oldSnapshot, change.OldRange));
        var newWindow = GetLineWindow(snapshot, CombineRanges(newImpact));
        newWindow = IncludeTrailingLineIfNeeded(
            oldWindow,
            oldSnapshot.Lines,
            snapshot,
            newWindow);
        var oldLines = previous.Snapshot.Lines;
        var newLines = snapshot.Lines;
        var delta = change.NewText.Length - change.OldRange.Length;
        var chunks = new List<ProjectedLineChunk>();
        previous.LineTable.AddRange(chunks, 0, oldWindow.StartLine);

        var changedLines = new List<ProjectedLine>(newWindow.EndLine - newWindow.StartLine);
        var logicalToVisual = Enumerable.Repeat(-1, newLines.LineCount).ToArray();
        CopyPrefixVisualLines(previous, logicalToVisual, oldWindow.StartLine);
        var prefixVisualCount = CountVisibleLines(previous, oldWindow.StartLine);
        for (var logicalLine = newWindow.StartLine; logicalLine < newWindow.EndLine; logicalLine++)
        {
            var sourceRange = newLines.GetLineRange(logicalLine);
            var line = BuildLineInlines(sourceRange, normalizedFolds, normalizedInlays);
            if (line is null)
            {
                continue;
            }

            logicalToVisual[logicalLine] = prefixVisualCount + changedLines.Count;
            changedLines.Add(new ProjectedLine(logicalLine, sourceRange, line));
        }

        ProjectedLineTable.FromLines(changedLines).AddRange(
            chunks,
            0,
            changedLines.Count);
        previous.LineTable.AddRange(
            chunks,
            oldWindow.EndLine,
            oldLines.LineCount - oldWindow.EndLine,
            newWindow.EndLine - oldWindow.EndLine,
            delta);

        var oldChangedVisualCount = CountVisibleLines(
            previous,
            oldWindow.StartLine,
            oldWindow.EndLine);
        var visualDelta = changedLines.Count - oldChangedVisualCount;
        var logicalDelta = newWindow.EndLine - oldWindow.EndLine;
        for (var logicalLine = newWindow.EndLine; logicalLine < newLines.LineCount; logicalLine++)
        {
            var oldLogicalLine = logicalLine - logicalDelta;
            if (oldLogicalLine < 0 || oldLogicalLine >= oldLines.LineCount)
            {
                continue;
            }

            var oldVisualLine = previous.GetVisualLineForLogicalLine(oldLogicalLine);
            logicalToVisual[logicalLine] = oldVisualLine < 0
                ? -1
                : oldVisualLine + visualDelta;
        }

        return new TextProjection(
            snapshot,
            ProjectedLineTable.FromChunks(chunks),
            normalizedFolds,
            normalizedInlays,
            logicalToVisual,
            isPlain: normalizedFolds.Count == 0 && normalizedInlays.Length == 0,
            changeWindow: new ProjectionChangeWindow(
                oldWindow.StartLine,
                oldWindow.EndLine,
                newWindow.StartLine,
                newWindow.EndLine));
    }

    private static void CopyPrefixVisualLines(
        TextProjection previous,
        int[] destination,
        int count)
    {
        for (var logicalLine = 0; logicalLine < count; logicalLine++)
        {
            destination[logicalLine] = previous.GetVisualLineForLogicalLine(logicalLine);
        }
    }

    private static int CountVisibleLines(TextProjection projection, int endLine) =>
        CountVisibleLines(projection, 0, endLine);

    private static int CountVisibleLines(
        TextProjection projection,
        int startLine,
        int endLine)
    {
        var count = 0;
        for (var logicalLine = startLine; logicalLine < endLine; logicalLine++)
        {
            if (projection.GetVisualLineForLogicalLine(logicalLine) >= 0)
            {
                count++;
            }
        }

        return count;
    }

    private static void AddChangedFoldRanges(
        IReadOnlyList<FoldRange> previous,
        List<FoldRange> current,
        TextChange change,
        List<TextRange> oldImpact,
        List<TextRange> newImpact)
    {
        var matched = new bool[current.Count];
        var delta = change.NewText.Length - change.OldRange.Length;
        foreach (var oldFold in previous)
        {
            var currentIndex = FindFold(current, oldFold.Id, matched);
            var mappedRange = MapRange(oldFold.Range, change, delta);
            var changed = currentIndex < 0
                || !FoldMatches(oldFold, current[currentIndex], mappedRange)
                || Touches(oldFold.Range, change.OldRange);
            if (!changed)
            {
                matched[currentIndex] = true;
                continue;
            }

            oldImpact.Add(oldFold.Range);
            newImpact.Add(currentIndex >= 0 ? current[currentIndex].Range : mappedRange);
            if (currentIndex >= 0)
            {
                matched[currentIndex] = true;
            }
        }

        for (var index = 0; index < current.Count; index++)
        {
            if (!matched[index])
            {
                newImpact.Add(current[index].Range);
            }
        }
    }

    private static void AddChangedInlayRanges(
        IReadOnlyList<InlineAdornment> previous,
        InlineAdornment[] current,
        TextChange change,
        List<TextRange> oldImpact,
        List<TextRange> newImpact)
    {
        var matched = new bool[current.Length];
        var delta = change.NewText.Length - change.OldRange.Length;
        foreach (var oldInlay in previous)
        {
            var currentIndex = FindInlay(current, oldInlay, change, delta, matched);
            var mappedPosition = MapPosition(oldInlay.Anchor.Position.Offset, change, delta);
            var changed = currentIndex < 0
                || !InlayMatches(oldInlay, current[currentIndex], mappedPosition)
                || Touches(TextRange.Empty(oldInlay.Anchor.Position.Offset), change.OldRange);
            if (!changed)
            {
                matched[currentIndex] = true;
                continue;
            }

            oldImpact.Add(TextRange.Empty(oldInlay.Anchor.Position.Offset));
            newImpact.Add(currentIndex >= 0
                ? TextRange.Empty(current[currentIndex].Anchor.Position.Offset)
                : TextRange.Empty(mappedPosition));
            if (currentIndex >= 0)
            {
                matched[currentIndex] = true;
            }
        }

        for (var index = 0; index < current.Length; index++)
        {
            if (!matched[index])
            {
                newImpact.Add(TextRange.Empty(current[index].Anchor.Position.Offset));
            }
        }
    }

    private static int FindFold(
        List<FoldRange> folds,
        string id,
        bool[] matched)
    {
        for (var index = 0; index < folds.Count; index++)
        {
            if (!matched[index]
                && string.Equals(folds[index].Id, id, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindInlay(
        InlineAdornment[] inlays,
        InlineAdornment previous,
        TextChange change,
        int delta,
        bool[] matched)
    {
        var mappedPosition = MapPosition(previous.Anchor.Position.Offset, change, delta);
        for (var index = 0; index < inlays.Length; index++)
        {
            if (!matched[index]
                && string.Equals(inlays[index].Id, previous.Id, StringComparison.Ordinal)
                && InlayMatches(previous, inlays[index], mappedPosition))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool FoldMatches(
        FoldRange previous,
        FoldRange current,
        TextRange mappedRange) =>
        string.Equals(previous.Id, current.Id, StringComparison.Ordinal)
        && previous.Placeholder == current.Placeholder
        && current.Range == mappedRange;

    private static bool InlayMatches(
        InlineAdornment previous,
        InlineAdornment current,
        int mappedPosition) =>
        current.Anchor.Position.Offset == mappedPosition
        && current.Anchor.Affinity == previous.Anchor.Affinity
        && current.Kind == previous.Kind
        && ContentMatches(previous.Content, current.Content);

    private static bool ContentMatches(AdornmentContent previous, AdornmentContent current)
    {
        if (previous.Text != current.Text
            || previous.IconKey != current.IconKey
            || previous.Actions.Count != current.Actions.Count)
        {
            return false;
        }

        return previous.Actions.Zip(current.Actions).All(pair =>
            pair.First.Id == pair.Second.Id
            && pair.First.Label == pair.Second.Label
            && pair.First.CommandId == pair.Second.CommandId);
    }

    private static bool Touches(TextRange left, TextRange right) =>
        left.Start <= right.End && right.Start <= left.End;

    private static TextRange MapRange(TextRange range, TextChange change, int delta)
    {
        var start = MapPosition(range.Start, change, delta);
        var end = MapPosition(range.End, change, delta);
        return TextRange.FromBounds(start, end);
    }

    private static TextRange CombineRanges(List<TextRange> ranges)
    {
        if (ranges.Count == 0)
        {
            return TextRange.Empty(0);
        }

        var start = ranges.Min(range => range.Start);
        var end = ranges.Max(range => range.End);
        return TextRange.FromBounds(start, end);
    }

    private static TextProjection BuildPlainIncremental(
        TextSnapshot oldSnapshot,
        TextSnapshot snapshot,
        TextProjection previous,
        TextChange change)
    {
        var oldLines = previous.Snapshot.Lines;
        var newLines = snapshot.Lines;
        var delta = change.NewText.Length - change.OldRange.Length;
        var oldWindow = GetLineWindow(
            oldSnapshot,
            change.OldRange,
            EndsAfterLineBreak(oldSnapshot, change.OldRange));
        var oldWindowStart = oldLines.GetLineStart(oldWindow.StartLine);
        var oldWindowEnd = oldWindow.EndLine == oldLines.LineCount
            ? oldSnapshot.Length
            : oldLines.GetLineStart(oldWindow.EndLine);
        var mappedStart = Math.Clamp(MapPosition(oldWindowStart, change, delta), 0, snapshot.Length);
        var mappedEnd = Math.Clamp(MapPosition(oldWindowEnd, change, delta), mappedStart, snapshot.Length);
        // The mapped old window is empty for an insertion into an empty
        // document. Include the inserted range itself, otherwise new logical
        // lines are left with no visual-line mapping and caret hit testing can
        // index past the incremental projection table.
        var newWindowStart = Math.Min(mappedStart, change.NewRange.Start);
        var newWindowEnd = Math.Max(mappedEnd, change.NewRange.End);
        var newWindow = GetLineWindow(
            snapshot,
            TextRange.FromBounds(
                Math.Clamp(newWindowStart, 0, snapshot.Length),
                Math.Clamp(newWindowEnd, newWindowStart, snapshot.Length)));
        newWindow = IncludeTrailingLineIfNeeded(oldWindow, oldLines, snapshot, newWindow);
        var chunks = new List<ProjectedLineChunk>();
        previous.LineTable.AddRange(chunks, 0, oldWindow.StartLine);

        var changedLines = new List<ProjectedLine>(newWindow.EndLine - newWindow.StartLine);
        for (var logicalLine = newWindow.StartLine; logicalLine < newWindow.EndLine; logicalLine++)
        {
            changedLines.Add(CreatePlainLine(logicalLine, newLines.GetLineRange(logicalLine)));
        }

        ProjectedLineTable.FromLines(changedLines).AddRange(chunks, 0, changedLines.Count);
        previous.LineTable.AddRange(
            chunks,
            oldWindow.EndLine,
            oldLines.LineCount - oldWindow.EndLine,
            newWindow.EndLine - oldWindow.EndLine,
            delta);

        return new TextProjection(
            snapshot,
            ProjectedLineTable.FromChunks(chunks),
            Array.Empty<FoldRange>(),
            Array.Empty<InlineAdornment>(),
            logicalToVisual: null,
            isPlain: true,
            changeWindow: new ProjectionChangeWindow(
                oldWindow.StartLine,
                oldWindow.EndLine,
                newWindow.StartLine,
                newWindow.EndLine));
    }

    private static ProjectedLine CreatePlainLine(int logicalLine, TextRange sourceRange) =>
        new(
            logicalLine,
            sourceRange,
            new ProjectionInline[] { new ProjectedText(sourceRange) });

    private static void BuildPlainProjection(
        TextSnapshot snapshot,
        List<ProjectedLine> lines)
    {
        for (var logicalLine = 0; logicalLine < snapshot.Lines.LineCount; logicalLine++)
        {
            var sourceRange = snapshot.Lines.GetLineRange(logicalLine);
            lines.Add(new ProjectedLine(
                logicalLine,
                sourceRange,
                new ProjectionInline[] { new ProjectedText(sourceRange) }));
        }
    }

    private static List<ProjectionInline>? BuildLineInlines(
        TextRange line,
        List<FoldRange> folds,
        IReadOnlyList<InlineAdornment> inlays)
    {
        var firstFold = LowerBoundFold(folds, line.Start);
        var coveringFold = firstFold > 0
            && folds[firstFold - 1].Range.Start < line.Start
            && folds[firstFold - 1].Range.End > line.Start
            ? folds[firstFold - 1]
            : null;
        var cursor = coveringFold?.Range.End ?? line.Start;
        if (cursor > line.End)
        {
            return null;
        }

        var result = new List<ProjectionInline>();
        for (var foldIndex = firstFold; foldIndex < folds.Count; foldIndex++)
        {
            var fold = folds[foldIndex];
            if (fold.Range.Start > line.End)
            {
                break;
            }

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
        var firstInlay = LowerBoundInlay(inlays, start);
        for (var inlayIndex = firstInlay; inlayIndex < inlays.Count; inlayIndex++)
        {
            var inlay = inlays[inlayIndex];
            var position = inlay.Anchor.Position.Offset;
            if (position > end || position == end && !includeEnd)
            {
                break;
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

    private static (int StartLine, int EndLine) GetLineWindow(
        TextSnapshot snapshot,
        TextRange range,
        bool includeEndLine = false)
    {
        var firstLine = snapshot.Lines.GetLine(range.Start);
        var lastPosition = range.IsEmpty
            ? range.Start
            : Math.Min(snapshot.Length, range.End - 1);
        var endLine = snapshot.Lines.GetLine(lastPosition) + 1;
        if (includeEndLine && !range.IsEmpty)
        {
            endLine = Math.Max(endLine, snapshot.Lines.GetLine(range.End) + 1);
        }

        endLine = Math.Min(snapshot.Lines.LineCount, endLine);
        return (firstLine, endLine);
    }

    private static bool EndsAfterLineBreak(TextSnapshot snapshot, TextRange range) =>
        range.End > range.Start
        && range.End <= snapshot.Length
        && snapshot.GetText(new TextRange(range.End - 1, 1)) == "\n";

    private static (int StartLine, int EndLine) IncludeTrailingLineIfNeeded(
        (int StartLine, int EndLine) oldWindow,
        LineIndex oldLines,
        TextSnapshot snapshot,
        (int StartLine, int EndLine) newWindow)
    {
        if (oldWindow.EndLine == oldLines.LineCount
            && snapshot.Length > 0
            && (snapshot[snapshot.Length - 1] == '\n'
                || snapshot[snapshot.Length - 1] == '\r'))
        {
            return (newWindow.StartLine, snapshot.Lines.LineCount);
        }

        return newWindow;
    }

    private static int MapPosition(int position, TextChange change, int delta) =>
        position <= change.OldRange.Start
            ? position
            : position >= change.OldRange.End
                ? checked(position + delta)
                : change.NewRange.Start;

    private static int LowerBoundInlay(
        IReadOnlyList<InlineAdornment> inlays,
        int position)
    {
        var low = 0;
        var high = inlays.Count;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (inlays[middle].Anchor.Position.Offset < position)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private static int LowerBoundFold(
        List<FoldRange> folds,
        int position)
    {
        var low = 0;
        var high = folds.Count;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (folds[middle].Range.Start < position)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
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
            if (!ids.Add(fold.Id)
                || accepted.Count > 0 && Overlaps(accepted[^1].Range, fold.Range))
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
