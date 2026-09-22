using System.Diagnostics;
using System.Globalization;
using Azunyan.Core;

const int lineCount = 100_000;
const int iterations = 8;
var verify = args.Contains("--verify", StringComparer.Ordinal);
var text = BuildText(lineCount);
var document = new Document(text);
var oldSnapshot = document.Snapshot;
var previous = TextProjectionBuilder.Build(oldSnapshot);
var insertion = text.IndexOf("line-050000", StringComparison.Ordinal);
var change = document.Insert(insertion, "changed-");
var current = document.Snapshot;

for (var index = 0; index < 2; index++)
{
    _ = TextProjectionBuilder.Build(current);
    _ = TextProjectionBuilder.BuildIncremental(oldSnapshot, current, previous, change);
}

var full = Measure(
    iterations,
    () => TextProjectionBuilder.Build(current));
var incremental = Measure(
    iterations,
    () => TextProjectionBuilder.BuildIncremental(oldSnapshot, current, previous, change));

var fullProjection = TextProjectionBuilder.Build(current);
var incrementalProjection = TextProjectionBuilder.BuildIncremental(
    oldSnapshot,
    current,
    previous,
    change);
var equivalent = AreEquivalent(current, fullProjection, incrementalProjection);

var foldStart = text.IndexOf("line-075000", StringComparison.Ordinal);
var inlayPosition = text.IndexOf("line-090000", StringComparison.Ordinal) + 5;
var folds = new[] { new FoldRange("stable-fold", new TextRange(foldStart, 8), "...") };
var inlays = new[]
{
    new InlineAdornment(
        "stable-inlay",
        DocumentAnchor.Before(inlayPosition),
        "type-hint",
        new AdornmentContent(" : int"))
};
var currentFolds = new[] { new FoldRange("stable-fold", new TextRange(foldStart + 8, 8), "...") };
var currentInlays = new[]
{
    new InlineAdornment(
        "stable-inlay",
        DocumentAnchor.Before(inlayPosition + 8),
        "type-hint",
        new AdornmentContent(" : int"))
};
var previousDecorated = TextProjectionBuilder.Build(oldSnapshot, folds, inlays);
var fullDecorated = Measure(
    iterations,
    () => TextProjectionBuilder.Build(current, currentFolds, currentInlays));
var incrementalDecorated = Measure(
    iterations,
    () => TextProjectionBuilder.BuildIncremental(
        oldSnapshot,
        current,
        previousDecorated,
        change,
        currentFolds,
        currentInlays));
var fullDecoratedProjection = TextProjectionBuilder.Build(current, currentFolds, currentInlays);
var incrementalDecoratedProjection = TextProjectionBuilder.BuildIncremental(
    oldSnapshot,
    current,
    previousDecorated,
    change,
    currentFolds,
    currentInlays);
var decoratedEquivalent = AreEquivalent(
    current,
    fullDecoratedProjection,
    incrementalDecoratedProjection);

var editDocument = new Document(text);
_ = editDocument.Snapshot.Lines.LineCount;
var editPosition = editDocument.Snapshot.Lines.GetLineStart(lineCount / 2) + 4;
var lineLookup = Measure(
    iterations,
    () => editDocument.Snapshot.Lines.GetLineColumn(editPosition));
var editIndex = 0;
var editAndLineLookup = Measure(
    iterations,
    () =>
    {
        var position = editPosition + editIndex++;
        editDocument.Insert(position, "x");
        return editDocument.Snapshot.Lines.GetLineColumn(position + 1);
    });

var inputSnapshot = new TextSnapshot(text);
var inputCalculator = new SlidingInputWindowCalculator();
var inputPosition = inputSnapshot.Lines.GetLineStart(lineCount / 2) + 16;
var inputChange = new TextChange(TextRange.Empty(inputPosition), string.Empty, "x");
var oldInputWindow = TextRange.FromBounds(inputPosition - 1024, inputPosition + 1024);
var currentInputWindow = new TextRange(oldInputWindow.Start, oldInputWindow.Length + 1);
var alignedInputWindow = Measure(
    iterations,
    () => inputCalculator.Calculate(
        inputSnapshot,
        TextSelection.Caret(inputPosition),
        currentWindow: oldInputWindow));
var reusedInputWindow = Measure(
    iterations,
    () => SlidingInputWindowCalculator.TryReuseAlignedWindow(
        currentInputWindow,
        inputChange,
        inputSnapshot.Length + 1,
        out _));
var inputWindowReused = SlidingInputWindowCalculator.TryReuseAlignedWindow(
    currentInputWindow,
    inputChange,
    inputSnapshot.Length + 1,
    out var verifiedInputWindow)
    && verifiedInputWindow == currentInputWindow;

var foldSnapshot = new TextSnapshot(text);
var largeFoldStart = foldSnapshot.Lines.GetLineStart(lineCount / 4);
var largeFoldEnd = foldSnapshot.Lines.GetLineEnd(lineCount * 3 / 4);
var largeFold = new[]
{
    new FoldRange("large-fold", TextRange.FromBounds(largeFoldStart, largeFoldEnd), " ...")
};
var unfoldedProjection = TextProjectionBuilder.Build(foldSnapshot);
var unfoldedRows = VisualRowMapBuilder.Build(unfoldedProjection);
var foldedProjection = TextProjectionBuilder.BuildIncremental(
    foldSnapshot,
    unfoldedProjection,
    largeFold,
    inlays: null);
var fullFoldProjection = TextProjectionBuilder.Build(foldSnapshot, largeFold);
var foldProjectionFull = Measure(
    iterations,
    () => TextProjectionBuilder.Build(foldSnapshot, largeFold));
var foldProjectionIncremental = Measure(
    iterations,
    () => TextProjectionBuilder.BuildIncremental(
        foldSnapshot,
        unfoldedProjection,
        largeFold,
        inlays: null));
var fullFoldRows = VisualRowMapBuilder.Build(fullFoldProjection);
var incrementalFoldRows = VisualRowMapBuilder.BuildIncremental(
    unfoldedProjection,
    foldedProjection,
    unfoldedRows);
var foldRowsFull = Measure(
    iterations,
    () => VisualRowMapBuilder.Build(fullFoldProjection));
var foldRowsIncremental = Measure(
    iterations,
    () => VisualRowMapBuilder.BuildIncremental(
        unfoldedProjection,
        foldedProjection,
        unfoldedRows));
var foldEquivalent = AreEquivalent(foldSnapshot, fullFoldProjection, foldedProjection);
var foldRowsEquivalent = AreRowsEquivalent(fullFoldRows, incrementalFoldRows);
var foldReusesEdges = ReferenceEquals(unfoldedProjection.Lines[0], foldedProjection.Lines[0])
    && ReferenceEquals(unfoldedProjection.Lines[^1], foldedProjection.Lines[^1]);

Console.WriteLine($"scenario=100k-lines-middle-insert");
Console.WriteLine($"full_mean_ms={full.Elapsed.TotalMilliseconds / iterations:F3}");
Console.WriteLine($"incremental_mean_ms={incremental.Elapsed.TotalMilliseconds / iterations:F3}");
Console.WriteLine($"full_mean_allocated_bytes={full.AllocatedBytes / iterations}");
Console.WriteLine($"incremental_mean_allocated_bytes={incremental.AllocatedBytes / iterations}");
Console.WriteLine($"visual_lines={incrementalProjection.VisualLineCount}");
Console.WriteLine($"equivalent={equivalent}");
Console.WriteLine($"decorated_full_mean_ms={fullDecorated.Elapsed.TotalMilliseconds / iterations:F3}");
Console.WriteLine($"decorated_incremental_mean_ms={incrementalDecorated.Elapsed.TotalMilliseconds / iterations:F3}");
Console.WriteLine($"decorated_full_mean_allocated_bytes={fullDecorated.AllocatedBytes / iterations}");
Console.WriteLine($"decorated_incremental_mean_allocated_bytes={incrementalDecorated.AllocatedBytes / iterations}");
Console.WriteLine($"decorated_visual_lines={incrementalDecoratedProjection.VisualLineCount}");
Console.WriteLine($"decorated_equivalent={decoratedEquivalent}");
Console.WriteLine($"line_lookup_mean_ms={lineLookup.Elapsed.TotalMilliseconds / iterations:F3}");
Console.WriteLine($"line_lookup_mean_allocated_bytes={lineLookup.AllocatedBytes / iterations}");
Console.WriteLine($"edit_and_line_lookup_mean_ms={editAndLineLookup.Elapsed.TotalMilliseconds / iterations:F3}");
Console.WriteLine($"edit_and_line_lookup_mean_allocated_bytes={editAndLineLookup.AllocatedBytes / iterations}");
Console.WriteLine($"input_window_align_mean_ms={alignedInputWindow.Elapsed.TotalMilliseconds / iterations:F3}");
Console.WriteLine($"input_window_reuse_mean_ms={reusedInputWindow.Elapsed.TotalMilliseconds / iterations:F3}");
Console.WriteLine($"input_window_reused={inputWindowReused}");
Console.WriteLine($"fold_projection_full_mean_ms={foldProjectionFull.Elapsed.TotalMilliseconds / iterations:F3}");
Console.WriteLine($"fold_projection_incremental_mean_ms={foldProjectionIncremental.Elapsed.TotalMilliseconds / iterations:F3}");
Console.WriteLine($"fold_projection_full_mean_allocated_bytes={foldProjectionFull.AllocatedBytes / iterations}");
Console.WriteLine($"fold_projection_incremental_mean_allocated_bytes={foldProjectionIncremental.AllocatedBytes / iterations}");
Console.WriteLine($"fold_rows_full_mean_ms={foldRowsFull.Elapsed.TotalMilliseconds / iterations:F3}");
Console.WriteLine($"fold_rows_incremental_mean_ms={foldRowsIncremental.Elapsed.TotalMilliseconds / iterations:F3}");
Console.WriteLine($"fold_rows_full_mean_allocated_bytes={foldRowsFull.AllocatedBytes / iterations}");
Console.WriteLine($"fold_rows_incremental_mean_allocated_bytes={foldRowsIncremental.AllocatedBytes / iterations}");
Console.WriteLine($"fold_equivalent={foldEquivalent}");
Console.WriteLine($"fold_rows_equivalent={foldRowsEquivalent}");
Console.WriteLine($"fold_reuses_edges={foldReusesEdges}");

if (verify && (!equivalent
    || !decoratedEquivalent
    || !inputWindowReused
    || !foldEquivalent
    || !foldRowsEquivalent
    || !foldReusesEdges))
{
    throw new InvalidOperationException("An incremental editor result differs from its full-build reference.");
}

static string BuildText(int lineCount)
{
    var builder = new System.Text.StringBuilder(lineCount * 110);
    for (var line = 0; line < lineCount; line++)
    {
        builder.Append("line-");
        builder.Append(line.ToString("D6", CultureInfo.InvariantCulture));
        builder.Append(' ');
        builder.Append('x', 96);
        builder.Append('\n');
    }

    return builder.ToString();
}

static Measurement Measure<T>(int count, Func<T> action)
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    var before = GC.GetAllocatedBytesForCurrentThread();
    var stopwatch = Stopwatch.StartNew();
    for (var index = 0; index < count; index++)
    {
        _ = action();
    }

    stopwatch.Stop();
    return new Measurement(stopwatch.Elapsed, GC.GetAllocatedBytesForCurrentThread() - before);
}

static bool AreRowsEquivalent(VisualRowMap expected, VisualRowMap actual)
{
    if (expected.Rows.Count != actual.Rows.Count)
    {
        return false;
    }

    for (var index = 0; index < expected.Rows.Count; index++)
    {
        var left = expected.Rows[index];
        var right = actual.Rows[index];
        if (left.Kind != right.Kind
            || left.LogicalLine != right.LogicalLine
            || left.TextStartColumn != right.TextStartColumn
            || left.TextLength != right.TextLength
            || left.VisualRowIndex != right.VisualRowIndex
            || DescribeBlock(left.BlockAdornment) != DescribeBlock(right.BlockAdornment))
        {
            return false;
        }
    }

    return true;
}

static bool AreEquivalent(
    TextSnapshot snapshot,
    TextProjection expected,
    TextProjection actual)
{
    if (expected.VisualLineCount != actual.VisualLineCount)
    {
        return false;
    }

    for (var line = 0; line < expected.VisualLineCount; line++)
    {
        if (expected.Lines[line].LogicalLine != actual.Lines[line].LogicalLine
            || expected.Lines[line].SourceRange != actual.Lines[line].SourceRange
            || expected.Lines[line].VisualLength != actual.Lines[line].VisualLength
            || snapshot.GetText(expected.Lines[line].SourceRange)
                != snapshot.GetText(actual.Lines[line].SourceRange)
            || expected.Lines[line].Inlines.Count != actual.Lines[line].Inlines.Count
            || expected.Lines[line].Inlines.Zip(actual.Lines[line].Inlines)
                .Any(pair => DescribeInline(pair.First) != DescribeInline(pair.Second)))
        {
            return false;
        }
    }

    return true;
}

static string DescribeInline(ProjectionInline inline) => inline switch
{
    ProjectedText text => $"text:{text.Source}",
    FoldPlaceholder fold => $"fold:{fold.FoldId}:{fold.HiddenSource}:{fold.DisplayText}",
    InlineAdornment inlay =>
        $"inlay:{inlay.Id}:{inlay.Anchor}:{inlay.Kind}:{DescribeContent(inlay.Content)}",
    _ => throw new ArgumentOutOfRangeException(nameof(inline))
};

static string DescribeBlock(BlockAdornment? block) => block is null
    ? string.Empty
    : $"{block.Id}:{block.Anchor}:{block.Kind}:{DescribeContent(block.Content)}:{block.DesiredHeight}";

static string DescribeContent(AdornmentContent content) =>
    $"{content.Text}:{content.IconKey}:{string.Join(',', content.Actions.Select(action => $"{action.Id}/{action.Label}/{action.CommandId}"))}";

readonly record struct Measurement(TimeSpan Elapsed, long AllocatedBytes);
