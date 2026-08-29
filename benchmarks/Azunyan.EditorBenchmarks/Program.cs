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

Console.WriteLine($"scenario=100k-lines-middle-insert");
Console.WriteLine($"full_mean_ms={full.Elapsed.TotalMilliseconds / iterations:F3}");
Console.WriteLine($"incremental_mean_ms={incremental.Elapsed.TotalMilliseconds / iterations:F3}");
Console.WriteLine($"full_mean_allocated_bytes={full.AllocatedBytes / iterations}");
Console.WriteLine($"incremental_mean_allocated_bytes={incremental.AllocatedBytes / iterations}");
Console.WriteLine($"visual_lines={incrementalProjection.VisualLineCount}");
Console.WriteLine($"equivalent={equivalent}");

if (verify && !equivalent)
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

    foreach (var line in new[] { 0, expected.VisualLineCount / 2, expected.VisualLineCount - 1 })
    {
        if (line < 0 || line >= expected.VisualLineCount
            || expected.Lines[line].SourceRange != actual.Lines[line].SourceRange
            || snapshot.GetText(expected.Lines[line].SourceRange)
                != snapshot.GetText(actual.Lines[line].SourceRange))
        {
            return false;
        }
    }

    return true;
}

readonly record struct Measurement(TimeSpan Elapsed, long AllocatedBytes);
