using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Azunote;

/// <summary>Opt-in measurements; no document text, paths or environment values are recorded.</summary>
internal static class ShellPerformance
{
    internal const string MeterName = "Azunote.Shell";
    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> Operations = Meter.CreateCounter<long>("azunote.shell.operations");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("azunote.shell.duration", "ms");

    public static Measurement Measure(string operation) => new(operation);

    internal readonly struct Measurement : IDisposable
    {
        private readonly string _operation;
        private readonly long _start;

        public Measurement(string operation)
        {
            _operation = operation;
            _start = Duration.Enabled ? Stopwatch.GetTimestamp() : 0;
            if (Operations.Enabled)
            {
                Operations.Add(1, new KeyValuePair<string, object?>("operation", operation));
            }
        }

        public void Dispose()
        {
            if (_start != 0)
            {
                Duration.Record(Stopwatch.GetElapsedTime(_start).TotalMilliseconds,
                    new KeyValuePair<string, object?>("operation", _operation));
            }
        }
    }
}
