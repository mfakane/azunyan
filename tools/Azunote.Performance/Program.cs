using System.Diagnostics;
using System.Globalization;
using System.Text;
using Azunote;
using Azunyan.Core;

// Component benchmark, not an input-to-screen latency benchmark. Baseline reproduces
// the former two-menu evaluation pattern, including fresh contexts for every tool.
Console.WriteLine("operation,characters,tools,p50_ms,p95_ms,p99_ms,bytes_per_iteration");
if (args.Contains("--watch-cache", StringComparer.Ordinal))
{
    CompareWatchedCache();
    return;
}
if (args.Contains("--pwsh-warm", StringComparer.Ordinal))
{
    ComparePowerShellWarmStart();
    return;
}
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

static void CompareWatchedCache()
{
    var directory = Directory.CreateTempSubdirectory("azunote-watch-performance-");
    try
    {
        Directory.CreateDirectory(Path.Combine(directory.FullName, ".git"));
        File.WriteAllText(Path.Combine(directory.FullName, ".env"), "BENCHMARK_VALUE=example");
        var session = new DocumentSession();
        var snapshot = new TextSnapshot(new string('x', 1_000_000));
        session.MarkSaved(Path.Combine(directory.FullName, "test.txt"), snapshot.Text,
            TextEncodingKind.Utf8, LineEndingKind.Lf);
        var tools = Enumerable.Range(0, 100).Select(index => new ExternalToolSettings
        {
            Name = $"Tool {index}", Launch = new() { Command = Environment.ProcessPath! }
        }).ToDictionary(tool => tool, tool => new PreparedExternalTool(tool));
        var input = new ExternalToolEvaluationInput(snapshot, new TextSelection(0, 128), session.State, "text", tools);
        var clock = new BenchmarkClock();
        using var registry = new SharedFileWatchRegistry();
        using var ttl = new ExternalToolAvailabilityCache(clock);
        using var watched = new ExternalToolAvailabilityCache(clock, watches: registry);
        // JIT warm-up with both paths before comparing expiry behavior.
        for (var i = 0; i < 100; i++)
        {
            input.Evaluate(() => true, ttl);
            input.Evaluate(() => true, watched);
        }
        Measure("ttl_batches_2s_apart", 1_000_000, 100, () =>
        {
            clock.Advance();
            input.Evaluate(() => true, ttl);
        });
        Measure("watched_batches_2s_apart", 1_000_000, 100, () =>
        {
            clock.Advance();
            input.Evaluate(() => true, watched);
        });
    }
    finally { directory.Delete(recursive: true); }
}

// The only mode that launches an external tool. It compares a cold pwsh launch with a
// launch that takes a process the pool already started and parked. Each warm sample waits
// for a parked process outside the measured region, which is the case the pool is for:
// the process is warmed while the user is editing, not while the tool runs.
static void ComparePowerShellWarmStart()
{
    const int PerLineCount = 8;

    if (ExternalToolLaunchResolver.ResolvePowerShell() is null)
    {
        Console.Error.WriteLine("No PowerShell interpreter was found.");
        return;
    }

    var definition = new ExternalToolDefinition(
        "$text = [Console]::In.ReadToEnd(); $text | ConvertFrom-Json | ConvertTo-Json -Depth 100",
        inputMode: ExternalToolInputMode.Document,
        stdin: "${input}",
        commandMode: ExternalToolCommandMode.Pwsh);
    var document = "{\"name\":\"azunote\",\"values\":[1,2,3],\"nested\":{\"a\":true}}";
    var context = new ExternalToolContext(null, document, string.Empty);

    Run(warmPool: null);
    MeasureRuns("pwsh_cold", () => Run(warmPool: null));

    using var pool = new PowerShellWarmPool();
    pool.EnsureWarm();
    MeasureRuns("pwsh_warm", () => Run(pool), before: () => WaitForWarm(pool));

    // A per run launches one process per part. The reservation raises the
    // waiting count for that run only, so deeper pools help the later parts.
    var perDefinition = new ExternalToolDefinition(
        "$null = [Console]::In.ReadToEnd()",
        inputMode: ExternalToolInputMode.Document,
        per: "line",
        stdin: "${input}",
        commandMode: ExternalToolCommandMode.Pwsh);
    var lines = string.Join('\n', Enumerable.Repeat("line text", PerLineCount));
    var perContext = new ExternalToolContext(null, lines, string.Empty);
    foreach (var size in new[] { 1, 2, 4, 8 })
    {
        using var perPool = new PowerShellWarmPool();
        perPool.Configure(maxProcesses: size, idleProcesses: 1);
        perPool.EnsureWarm();
        MeasureRuns(
            string.Create(CultureInfo.InvariantCulture, $"pwsh_per{PerLineCount}_max{size}"),
            () => RunPer(perPool),
            before: () => WaitForWarm(perPool));

        ExternalToolResult RunPer(PowerShellWarmPool warmPool)
        {
            var result = ExternalToolRunner.RunAsync(perDefinition, perContext, warmPool)
                .GetAwaiter().GetResult();
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(result.StandardError);
            }

            return result;
        }
    }

    ExternalToolResult Run(PowerShellWarmPool? warmPool)
    {
        var result = ExternalToolRunner.RunAsync(definition, context, warmPool)
            .GetAwaiter().GetResult();
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(result.StandardError);
        }

        return result;
    }

    static void WaitForWarm(PowerShellWarmPool pool)
    {
        for (var attempt = 0; attempt < 200 && !pool.HasWarmProcess; attempt++)
        {
            Thread.Sleep(25);
        }
    }
}

// Fewer samples than Measure because every sample starts a real process.
static void MeasureRuns(string operation, Func<ExternalToolResult> run, Action? before = null)
{
    before?.Invoke();
    run();
    var samples = new double[15];
    for (var i = 0; i < samples.Length; i++)
    {
        // The wait for a parked process stays outside the measured region.
        before?.Invoke();
        var start = Stopwatch.GetTimestamp();
        run();
        samples[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    Array.Sort(samples);
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"{operation},{0},{1},{samples[7]:F3},{samples[13]:F3},{samples[14]:F3},0"));
}

sealed class BenchmarkClock : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance() => _now += TimeSpan.FromSeconds(2);
}
