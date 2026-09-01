using Azunyan.Core;

namespace Azunyan.Layout;

public readonly record struct LayoutMetrics
{
    public LayoutMetrics(double characterWidth, double lineHeight, double baseline)
    {
        ValidatePositive(characterWidth, nameof(characterWidth));
        ValidatePositive(lineHeight, nameof(lineHeight));
        if (!double.IsFinite(baseline) || baseline < 0 || baseline > lineHeight)
        {
            throw new ArgumentOutOfRangeException(nameof(baseline));
        }

        CharacterWidth = characterWidth;
        LineHeight = lineHeight;
        Baseline = baseline;
    }

    public double CharacterWidth { get; }

    public double LineHeight { get; }

    public double Baseline { get; }

    private static void ValidatePositive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }
}

public enum LayoutRunKind
{
    Text,
    FoldPlaceholder,
    InlineAdornment
}

public sealed record LayoutRun
{
    public LayoutRun(
        LayoutRunKind kind,
        string text,
        int visualStart,
        TextRange? sourceRange = null,
        DocumentAnchor? anchor = null,
        string? classification = null,
        string? adornmentKind = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(visualStart);

        Kind = kind;
        Text = text;
        VisualStart = visualStart;
        SourceRange = sourceRange;
        Anchor = anchor;
        Classification = classification;
        AdornmentKind = adornmentKind;
    }

    public LayoutRunKind Kind { get; }

    public string Text { get; }

    public int VisualStart { get; }

    public TextRange? SourceRange { get; }

    public DocumentAnchor? Anchor { get; }

    public string? Classification { get; }

    public string? AdornmentKind { get; }

    public int VisualEnd => checked(VisualStart + Text.Length);
}

/// <summary>
/// A layout result for one projected, unwrapped line. The first implementation
/// uses monospace metrics; a platform text shaper can replace it without
/// changing projection or provider contracts.
/// </summary>
public sealed class UnwrappedLineLayout
{
    internal UnwrappedLineLayout(
        ProjectedLine sourceLine,
        IReadOnlyList<LayoutRun> runs,
        LayoutMetrics metrics,
        int visualStart = 0,
        int? visualLength = null)
    {
        SourceLine = sourceLine;
        Runs = runs;
        Metrics = metrics;
        if (visualStart < 0 || visualStart > sourceLine.VisualLength)
        {
            throw new ArgumentOutOfRangeException(nameof(visualStart));
        }

        VisualStart = visualStart;
        VisualLength = visualLength ?? (sourceLine.VisualLength - visualStart);
        if (VisualLength < 0 || VisualStart + VisualLength > sourceLine.VisualLength)
        {
            throw new ArgumentOutOfRangeException(nameof(visualLength));
        }

        Width = VisualLength * metrics.CharacterWidth;
        Height = metrics.LineHeight;
        Baseline = metrics.Baseline;
    }

    public ProjectedLine SourceLine { get; }

    public IReadOnlyList<LayoutRun> Runs { get; }

    public LayoutMetrics Metrics { get; }

    public double Width { get; }

    public double Height { get; }

    public double Baseline { get; }

    public int VisualStart { get; }

    public int VisualLength { get; }

    public int VisualEnd => checked(VisualStart + VisualLength);

    public DocumentAnchor GetAnchorAtCaretStop(int caretStop)
    {
        if (caretStop < 0 || caretStop > VisualLength)
        {
            throw new ArgumentOutOfRangeException(nameof(caretStop));
        }

        return SourceLine.GetAnchor(VisualStart + caretStop);
    }

    public int GetCaretStop(DocumentAnchor anchor) =>
        SourceLine.GetVisualColumn(anchor) - VisualStart;

    public DocumentAnchor GetDocumentAnchorAtCaretStop(int caretStop)
    {
        if (caretStop < 0 || caretStop > VisualLength)
        {
            throw new ArgumentOutOfRangeException(nameof(caretStop));
        }

        var position = VisualStart + caretStop;
        foreach (var run in Runs)
        {
            if (position < run.VisualStart || position > run.VisualEnd)
            {
                continue;
            }

            var local = position - run.VisualStart;
            switch (run.Kind)
            {
                case LayoutRunKind.Text when run.SourceRange is { } sourceRange:
                    var offset = sourceRange.Start + Math.Min(local, sourceRange.Length);
                    return local >= sourceRange.Length
                        && !Runs.Any(next => next.VisualStart == run.VisualEnd)
                        ? DocumentAnchor.After(offset)
                        : DocumentAnchor.Before(offset);
                case LayoutRunKind.FoldPlaceholder when run.SourceRange is { } hidden:
                    if (local <= 0)
                    {
                        return DocumentAnchor.Before(hidden.Start);
                    }

                    if (local >= run.Text.Length)
                    {
                        return DocumentAnchor.After(hidden.End);
                    }

                    return local < run.Text.Length / 2
                        ? DocumentAnchor.Before(hidden.Start)
                        : DocumentAnchor.After(hidden.End);
                case LayoutRunKind.InlineAdornment when run.Anchor is { } anchor:
                    return local <= 0
                        ? DocumentAnchor.Before(anchor.Position.Offset)
                        : DocumentAnchor.After(anchor.Position.Offset);
            }
        }

        return SourceLine.GetAnchor(position);
    }

    public UnwrappedLineLayout Slice(int visualStart, int visualLength)
    {
        if (visualStart < VisualStart
            || visualLength < 0
            || visualStart + visualLength > VisualEnd)
        {
            throw new ArgumentOutOfRangeException(nameof(visualStart));
        }

        var end = visualStart + visualLength;
        var runs = Runs
            .Select(run => SliceRun(run, visualStart, end))
            .Where(run => run is not null)
            .Cast<LayoutRun>()
            .ToArray();
        return new UnwrappedLineLayout(
            SourceLine,
            runs,
            Metrics,
            visualStart,
            visualLength);
    }

    private static LayoutRun? SliceRun(LayoutRun run, int start, int end)
    {
        var overlapStart = Math.Max(start, run.VisualStart);
        var overlapEnd = Math.Min(end, run.VisualEnd);
        if (overlapEnd <= overlapStart)
        {
            return null;
        }

        var textOffset = overlapStart - run.VisualStart;
        var text = run.Text.Substring(textOffset, overlapEnd - overlapStart);
        var sourceRange = run.SourceRange is { } source
            && run.Kind == LayoutRunKind.Text
            ? TextRange.FromBounds(source.Start + textOffset, source.Start + textOffset + text.Length)
            : run.SourceRange;
        return new LayoutRun(
            run.Kind,
            text,
            overlapStart - start,
            sourceRange,
            run.Anchor,
            run.Classification,
            run.AdornmentKind);
    }
}

public interface IUnwrappedLineLayoutEngine
{
    UnwrappedLineLayout Layout(
        TextSnapshot snapshot,
        ProjectedLine line,
        IReadOnlyList<SyntaxSpan> syntax,
        LayoutMetrics metrics);
}

/// <summary>
/// A deterministic layout engine for the first custom surface milestone. It
/// creates render-neutral runs and uses the same projection mapping for hit
/// testing. It is not the final glyph shaper.
/// </summary>
public sealed class MonospaceLineLayoutEngine : IUnwrappedLineLayoutEngine
{
    public UnwrappedLineLayout Layout(
        TextSnapshot snapshot,
        ProjectedLine line,
        IReadOnlyList<SyntaxSpan> syntax,
        LayoutMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(syntax);

        var runs = new List<LayoutRun>();
        var visualStart = 0;
        foreach (var inline in line.Inlines)
        {
            switch (inline)
            {
                case ProjectedText text:
                    AddTextRuns(runs, snapshot, text.Source, syntax, ref visualStart);
                    break;
                case FoldPlaceholder fold:
                    runs.Add(new LayoutRun(
                        LayoutRunKind.FoldPlaceholder,
                        fold.DisplayText,
                        visualStart,
                        fold.HiddenSource,
                        DocumentAnchor.Before(fold.HiddenSource.Start),
                        adornmentKind: fold.FoldId));
                    visualStart += fold.DisplayText.Length;
                    break;
                case InlineAdornment adornment:
                    runs.Add(new LayoutRun(
                        LayoutRunKind.InlineAdornment,
                        adornment.Content.Text,
                        visualStart,
                        anchor: adornment.Anchor,
                        adornmentKind: adornment.Kind));
                    visualStart += adornment.Content.Text.Length;
                    break;
            }
        }

        return new UnwrappedLineLayout(line, runs, metrics);
    }

    private static void AddTextRuns(
        List<LayoutRun> runs,
        TextSnapshot snapshot,
        TextRange source,
        IReadOnlyList<SyntaxSpan> syntax,
        ref int visualStart)
    {
        if (source.IsEmpty)
        {
            return;
        }

        var boundaries = syntax
            .Where(span => Intersects(span.Range, source))
            .SelectMany(span => new[]
            {
                Math.Max(source.Start, span.Range.Start),
                Math.Min(source.End, span.Range.End)
            })
            .Append(source.Start)
            .Append(source.End)
            .Distinct()
            .OrderBy(value => value)
            .ToArray();

        for (var index = 0; index + 1 < boundaries.Length; index++)
        {
            var start = boundaries[index];
            var end = boundaries[index + 1];
            if (end <= start)
            {
                continue;
            }

            var range = TextRange.FromBounds(start, end);
            var classification = syntax
                .FirstOrDefault(span => span.Range.Contains(start))
                .Classification;
            runs.Add(new LayoutRun(
                LayoutRunKind.Text,
                snapshot.GetText(range),
                visualStart,
                range,
                classification: classification));
            visualStart += range.Length;
        }
    }

    private static bool Intersects(TextRange left, TextRange right) =>
        left.Start < right.End && right.Start < left.End;
}
