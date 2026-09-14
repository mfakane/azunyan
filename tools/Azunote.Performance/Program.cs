using System.Diagnostics;
using System.Globalization;
using System.Text;
using Azunote;
using Azunyan.Core;

// Component benchmark, not an input-to-screen latency benchmark. Baseline reproduces
// the former two-menu evaluation pattern, including fresh contexts for every tool.
Console.WriteLine("operation,characters,tools,p50_ms,p95_ms,p99_ms,bytes_per_iteration");
var directory = Directory.CreateTempSubdirectory("azunote-performance-");
try
{
    Directory.CreateDirectory(Path.Combine(directory.FullName, ".git"));
    File.WriteAllText(Path.Combine(directory.FullName, ".env"), "BENCHMARK_VALUE=example");
    foreach (var size in new[] { 10_000, 1_000_000, 10_000_000 })
    {
        var document = string.Concat(Enumerable.Repeat("line text\r\n", (size + 10) / 11))[..size];
        var snapshot = new TextSnapshot(document);
        var session = new DocumentSession();
        session.MarkSaved(Path.Combine(directory.FullName, "test.txt"), document,
            TextEncodingKind.Utf8, LineEndingKind.CrLf);
        Measure("saved_compare_before", size, 0, () =>
            _ = string.Equals(Normalize(document), Normalize(document), StringComparison.Ordinal));
        Measure("saved_compare_after", size, 0, () => _ = session.IsSameAsSaved(document));

        foreach (var count in new[] { 0, 10, 100 })
        {
            var settings = Enumerable.Range(0, count).Select(index => new ExternalToolSettings
            {
                Name = $"Tool {index}", Launch = new() { Command = Environment.ProcessPath! }
            }).ToArray();
            var tools = settings.ToDictionary(tool => tool, tool => new PreparedExternalTool(tool));
            var input = new ExternalToolEvaluationInput(snapshot, new TextSelection(0, 128), session.State, "text", tools);
            Measure("two_menus_before", size, count, () =>
            {
                for (var menu = 0; menu < 2; menu++)
                    foreach (var tool in settings)
                        _ = ExternalToolAvailability.Evaluate(tool, input.CreateContext(tool.DefinitionDirectory));
            });
            Measure("one_batch_cold", size, count, () =>
            {
                using var cache = new ExternalToolAvailabilityCache();
                _ = input.Evaluate(() => true, cache);
            });
            using var warm = new ExternalToolAvailabilityCache(lifetime: TimeSpan.FromMinutes(1));
            Measure("one_batch_warm", size, count, () => _ = input.Evaluate(() => true, warm));
        }
    }
}
finally { directory.Delete(recursive: true); }

static void Measure(string operation, int size, int tools, Action action)
{
    for (var i = 0; i < 3; i++) action();
    var samples = new double[30];
    var allocation = GC.GetAllocatedBytesForCurrentThread();
    for (var i = 0; i < samples.Length; i++)
    {
        var start = Stopwatch.GetTimestamp();
        action();
        samples[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }
    var bytes = (GC.GetAllocatedBytesForCurrentThread() - allocation) / samples.Length;
    Array.Sort(samples);
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"{operation},{size},{tools},{samples[14]:F3},{samples[28]:F3},{samples[29]:F3},{bytes}"));
}

static string Normalize(string text)
{
    if (text.IndexOf('\r') < 0) return text;
    var normalized = new StringBuilder(text.Length);
    for (var index = 0; index < text.Length; index++)
    {
        if (text[index] == '\r')
        {
            if (index + 1 < text.Length && text[index + 1] == '\n') index++;
            normalized.Append('\n');
        }
        else normalized.Append(text[index]);
    }
    return normalized.ToString();
}
