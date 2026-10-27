using System.Globalization;

namespace Azunote;

/// <summary>
/// What a language server and Azunote said to each other, and what the server
/// wrote to standard error. The last lines are kept so a log opened later
/// starts with what already happened.
/// </summary>
internal sealed class LanguageServerLog
{
    private const int KeptLines = 2000;
    private const int MaximumLineLength = 4000;

    private readonly object _gate = new();
    private readonly Queue<string> _lines = new();
    private readonly TimeProvider _timeProvider;
    private Action<string>? _appended;

    public LanguageServerLog(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Adds a line, cut short when it is very long, such as a message carrying a whole document.</summary>
    public void Write(string text)
    {
        if (text.Length > MaximumLineLength)
        {
            text = string.Create(
                CultureInfo.InvariantCulture,
                $"{text.AsSpan(0, MaximumLineLength)}… ({text.Length - MaximumLineLength} more characters)");
        }

        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"[{_timeProvider.GetLocalNow():HH:mm:ss.fff}] {text.ReplaceLineEndings(" ")}{Environment.NewLine}");
        lock (_gate)
        {
            _lines.Enqueue(line);
            if (_lines.Count > KeptLines)
            {
                _lines.Dequeue();
            }

            _appended?.Invoke(line);
        }
    }

    /// <summary>
    /// Returns the kept lines and calls <paramref name="appended"/> with every
    /// line written after them, in order, until the subscription is disposed.
    /// It is called on the writing thread and must return quickly.
    /// </summary>
    public IDisposable Subscribe(Action<string> appended, out string text)
    {
        ArgumentNullException.ThrowIfNull(appended);
        lock (_gate)
        {
            text = string.Concat(_lines);
            _appended += appended;
        }

        return new Subscription(this, appended);
    }

    private sealed class Subscription(LanguageServerLog log, Action<string> appended) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                lock (log._gate)
                {
                    log._appended -= appended;
                }
            }
        }
    }
}
