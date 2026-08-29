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

if (verify && (!equivalent || !decoratedEquivalent))
{
    throw new InvalidOperationException("Incremental projection differs from a full projection.");
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

static Measurement Measure(int count, Func<TextProjection> action)
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
                .Any(pair => pair.First.GetType() != pair.Second.GetType()))
        {
            return false;
        }
    }

    return true;
}

readonly record struct Measurement(TimeSpan Elapsed, long AllocatedBytes);
